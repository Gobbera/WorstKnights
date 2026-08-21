using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(PhotonView))]
public partial class PlayerMovement : MonoBehaviour, IPlayerMovement, IPunObservable
{
    private const float InputDeadzone = 0.0001f;
    private const float StandClearancePadding = 0.02f;

    [SerializeField] private MovementConfig config;
    [SerializeField] private Transform orientation;
    [Header("Network Sync")]
    [SerializeField] private float remotePositionLerpSpeed = 12f;
    [SerializeField] private float remoteRotationLerpSpeed = 16f;
    [SerializeField] private float remoteTeleportDistance = 4f;

    private readonly RaycastHit[] sphereCastHits = new RaycastHit[8];
    private readonly RaycastHit[] raycastHits = new RaycastHit[8];

    private float currentAcceleration;
    private float directionChangeSpeedMultiplier = 1f;
    private float directionChangeHoldTimer;
    private float directionChangeRecoveryTimer;
    private float directionChangeInputMemoryTimer;
    private float startCapsuleHeight;
    private float nextJumpInputAllowedTime;
    private float jumpGroundIgnoreUntil;
    private float airborneMomentumSpeed;
    private float currentLocomotionSpeed;
    private float locomotionSpeedVelocity;
    private float sprintStrafeOpenTimer;
    private float sprintStrafeDirectionSign;
    private bool exitingSlope;
    private bool jumpQueued;
    private bool networkJumpQueued;
    private bool hasLocomotionSpeedSample;
    private bool sprintReleaseBlendActive;
    private int jumpAnimationSequence;
    private int networkJumpAnimationSequence;
    private Vector3 desiredMoveDirection;
    private Vector3 startCapsuleCenter;
    private Vector2 requestedInput;
    private Vector2 rawInput;
    private Vector2 rememberedDirectionalInput;
    private Rigidbody rb;
    private CapsuleCollider capsule;
    private PhotonView photonView;
    private SurfaceProbeResult surfaceState;
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private Vector3 networkVelocity;
    private Vector2 networkInput;
    private Vector2 networkAnimationInput;
    private MovementState networkState;
    private bool networkGrounded;
    private bool hasNetworkState;
    private bool wasGroundedForLandingAnimation;
    private bool hasAirborneLandingPhase;
    private bool landingAnimationArmed;
    private bool landingAnimationTriggered;
    private float airborneLandingTime;
    private float airborneLandingStartHeight;
    private float highestAirborneLandingHeight;
    private float groundedLandingConfirmTime;
    private float mostNegativeLandingVerticalSpeed;
    private int landingAnimationSequence;
    private int networkLandingAnimationSequence;
    private bool hasDirectionChangeHistory;
    private DirectionChangeInertiaProfile activeDirectionChangeProfile;

    public bool OnSlope { get; private set; }
    public bool IsTouchingWall { get; private set; }
    public bool IsSlidingOnSlope { get; private set; }
    public float CurrentSlopeAngle => surfaceState.GroundAngle;

    public bool IsGrounded { get; private set; }
    public MovementState CurrentState { get; private set; }
    public Vector2 Input { get; private set; }
    public Vector2 AnimationInput { get; private set; }
    public MovementConfig Config => config;
    public bool IsJumpQueued => jumpQueued;
    public int JumpAnimationSequence => jumpAnimationSequence;
    public int LandingAnimationSequence => landingAnimationSequence;
    public float LocomotionScale { get; private set; } = 1f;
    public Vector3 PlanarVelocity => HasAuthority()
        ? (rb != null ? new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z) : Vector3.zero)
        : new Vector3(networkVelocity.x, 0f, networkVelocity.z);
    public float PlanarSpeed => PlanarVelocity.magnitude;
    public float VerticalVelocity => HasAuthority() ? (rb != null ? rb.linearVelocity.y : 0f) : networkVelocity.y;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        photonView = GetComponent<PhotonView>();

        if (rb == null)
        {
            Debug.LogError("PlayerMovement: Rigidbody component not found!", gameObject);
        }
        else
        {
            rb.freezeRotation = true;
            rb.useGravity = true;
        }

        if (capsule == null)
            Debug.LogWarning("PlayerMovement: CapsuleCollider not found. Falling back to config.playerHeight for probes.", gameObject);
        else
        {
            startCapsuleHeight = capsule.height;
            startCapsuleCenter = capsule.center;
        }

        networkPosition = transform.position;
        networkRotation = transform.rotation;
    }

    private void Start()
    {
        ApplyAuthorityState();

        if (rb != null && config != null)
            RefreshSurfaceState();

        InitializeLocomotionSpeedBlend();
        InitializeLandingAnimationState();
    }

    private void Update()
    {
        if (rb == null || config == null)
            return;

        if (!HasAuthority())
        {
            ApplyRemoteState();
            UpdateLocomotionSpeedBlend(Time.deltaTime);
            return;
        }

        RefreshSurfaceState();
        rb.linearDamping = IsGrounded && !surfaceState.IsSlidingSlope ? config.groundDrag : 0f;
        UpdateLocomotionSpeedBlend(Time.deltaTime);
        UpdateLandingAnimationState(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (rb == null || config == null)
            return;

        if (!HasAuthority())
        {
            InterpolateRemoteTransform(Time.fixedDeltaTime);
            return;
        }

        RefreshSurfaceState();

        if (ApplyStepAssist())
            RefreshSurfaceState();

        UpdateDirectionChangeInertia(Time.fixedDeltaTime);
        UpdateEffectiveInput();
        ApplyMovement();
        SpeedControl();
    }

    public void Move(Vector2 input)
    {
        if (!HasAuthority())
            return;

        if (rb == null || config == null || orientation == null)
        {
            Debug.LogError("PlayerMovement: Missing required components (Rigidbody, MovementConfig, or orientation)!", gameObject);
            return;
        }

        requestedInput = new Vector2(
            Mathf.Clamp(input.x, -1f, 1f),
            Mathf.Clamp(input.y, -1f, 1f));
        rawInput = GetMovementInputForCurrentState(requestedInput);

        desiredMoveDirection = orientation.forward * rawInput.y + orientation.right * rawInput.x;
        desiredMoveDirection = Vector3.ProjectOnPlane(desiredMoveDirection, Vector3.up);
        if (desiredMoveDirection.sqrMagnitude > 1f)
            desiredMoveDirection.Normalize();

        float accelerationTime = Mathf.Max(config.accelerationTime, 0.0001f);
        float decelerationTime = Mathf.Max(config.decelerationTime, 0.0001f);

        if (rawInput.sqrMagnitude > InputDeadzone)
            currentAcceleration = Mathf.MoveTowards(currentAcceleration, 1f, Time.deltaTime / accelerationTime);
        else
            currentAcceleration = Mathf.MoveTowards(currentAcceleration, 0f, Time.deltaTime / decelerationTime);

        RefreshSurfaceState();
        UpdateEffectiveInput();
    }

    public void Jump()
    {
        RefreshSurfaceState();

        if (Time.time < nextJumpInputAllowedTime || !IsGrounded || jumpQueued || exitingSlope)
            return;

        nextJumpInputAllowedTime = Time.time + Mathf.Max(0f, config.jumpInputCooldown);

        CancelInvoke(nameof(ApplyJumpForce));
        CancelInvoke(nameof(ResetJump));

        jumpQueued = true;
        jumpAnimationSequence++;

        if (config.jumpDelay > 0f)
            Invoke(nameof(ApplyJumpForce), config.jumpDelay);
        else
            ApplyJumpForce();
    }

    public void StartCrouch()
    {
        if (config == null || !SetMovementStateInternal(MovementState.crouching))
            return;

        if (rb != null && IsGrounded)
            rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
    }

    public void StopCrouch()
    {
        if (CurrentState != MovementState.crouching || !CanStandFromCrouch())
            return;

        SetMovementStateInternal(MovementState.idle);
    }

    public void SetState(MovementState state)
    {
        SetMovementStateInternal(state);
    }

    private bool SetMovementStateInternal(MovementState state)
    {
        if (CurrentState == state)
        {
            ApplyMovementStateCapsule();
            return false;
        }

        sprintReleaseBlendActive = CurrentState == MovementState.sprinting && state == MovementState.walking;
        CurrentState = state;

        if (CurrentState != MovementState.sprinting)
            ResetSprintStrafeBiasState();

        ApplyMovementStateCapsule();
        return true;
    }

    private Vector2 GetMovementInputForCurrentState(Vector2 input)
    {
        Vector2 clampedInput = Vector2.ClampMagnitude(input, 1f);
        UpdateSprintStrafeBiasState(clampedInput);

        if (!ShouldBiasSprintStrafe(clampedInput))
            return clampedInput;

        // Preserve the original input magnitude while only opening lateral influence over time.
        Vector2 biasedInput = new Vector2(
            clampedInput.x * GetSprintStrafeTimedInfluence(),
            clampedInput.y);
        float biasedMagnitude = biasedInput.magnitude;
        if (biasedMagnitude <= InputDeadzone)
            return Vector2.zero;

        return Vector2.ClampMagnitude(biasedInput * (clampedInput.magnitude / biasedMagnitude), 1f);
    }

    private void UpdateSprintStrafeBiasState(Vector2 input)
    {
        if (!ShouldBiasSprintStrafe(input))
        {
            ResetSprintStrafeBiasState();
            return;
        }

        float lateralSign = Mathf.Sign(input.x);
        if (Mathf.Abs(sprintStrafeDirectionSign) > 0f && lateralSign != sprintStrafeDirectionSign)
            sprintStrafeOpenTimer = 0f;

        sprintStrafeDirectionSign = lateralSign;

        float openTime = Mathf.Max(0f, config.sprintStrafeOpenTime);
        if (openTime <= InputDeadzone)
            return;

        sprintStrafeOpenTimer = Mathf.Min(openTime, sprintStrafeOpenTimer + Time.deltaTime);
    }

    private float GetSprintStrafeTimedInfluence()
    {
        float maxInfluence = Mathf.Clamp01(config.sprintStrafeInfluence);
        float openTime = Mathf.Max(0f, config.sprintStrafeOpenTime);
        if (openTime <= InputDeadzone)
            return maxInfluence;

        float openProgress = Mathf.Clamp01(sprintStrafeOpenTimer / openTime);
        float curvedProgress = Mathf.Pow(openProgress, Mathf.Max(1f, config.sprintStrafeCurveExponent));
        return Mathf.Lerp(0f, maxInfluence, curvedProgress);
    }

    private void ResetSprintStrafeBiasState()
    {
        sprintStrafeOpenTimer = 0f;
        sprintStrafeDirectionSign = 0f;
    }

    private bool ShouldBiasSprintStrafe(Vector2 input)
    {
        return config != null
            && config.enableSprintStrafeBias
            && CurrentState == MovementState.sprinting
            && input.y > InputDeadzone
            && Mathf.Abs(input.x) > InputDeadzone
            && config.sprintStrafeInfluence < 0.9999f;
    }

    private void ApplyMovementStateCapsule()
    {
        if (config == null || capsule == null)
            return;

        float crouchHeightMultiplier = Mathf.Clamp(config.crouchYScale, 0.1f, 1f);
        float targetHeight = CurrentState == MovementState.crouching
            ? Mathf.Max(capsule.radius * 2f, startCapsuleHeight * crouchHeightMultiplier)
            : startCapsuleHeight;

        Vector3 targetCenter = startCapsuleCenter;
        if (CurrentState == MovementState.crouching)
            targetCenter.y -= (startCapsuleHeight - targetHeight) * 0.5f;

        if (!Mathf.Approximately(capsule.height, targetHeight))
            capsule.height = targetHeight;

        if ((capsule.center - targetCenter).sqrMagnitude > 0.000001f)
            capsule.center = targetCenter;
    }

    private bool CanStandFromCrouch()
    {
        if (config == null || CurrentState != MovementState.crouching || capsule == null)
            return true;

        CapsuleShape capsuleShape = CapsuleShape.Create(transform, capsule, config.playerHeight);
        float standingHeight = startCapsuleHeight * Mathf.Abs(transform.lossyScale.y);
        float extraHeight = standingHeight - capsuleShape.Height;
        if (extraHeight <= 0.0001f)
            return true;

        Vector3 topHemisphereCenter = capsuleShape.LowestPoint + Vector3.up * (capsuleShape.Height - capsuleShape.Radius);
        float checkRadius = Mathf.Max(0.01f, capsuleShape.Radius * 0.95f);
        return !TrySphereCastIgnoringSelf(
            topHemisphereCenter,
            checkRadius,
            Vector3.up,
            extraHeight + StandClearancePadding,
            GetCollisionMask(),
            out _);
    }
}
