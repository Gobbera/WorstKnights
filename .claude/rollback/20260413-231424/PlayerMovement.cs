using UnityEngine;
using Photon.Pun;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerMovement : MonoBehaviour, IPlayerMovement, IPunObservable
{
    [SerializeField] private MovementConfig config;
    [SerializeField] private Transform orientation;
    [Header("Network Sync")]
    [SerializeField] private float remotePositionLerpSpeed = 12f;
    [SerializeField] private float remoteRotationLerpSpeed = 16f;
    [SerializeField] private float remoteTeleportDistance = 4f;

    private readonly RaycastHit[] sphereCastHits = new RaycastHit[8];
    private readonly RaycastHit[] raycastHits = new RaycastHit[8];

    private const float InputDeadzone = 0.0001f;
    private float currentAcceleration;
    private float directionChangeSpeedMultiplier = 1f;
    private float directionChangeHoldTimer;
    private float directionChangeRecoveryTimer;
    private float directionChangeInputMemoryTimer;
    private float startYScale;
    private float nextJumpInputAllowedTime;
    private float jumpGroundIgnoreUntil;
    private float airborneMomentumSpeed;
    private bool exitingSlope;
    private bool jumpQueued;
    private bool networkJumpQueued;
    private int jumpAnimationSequence;
    private int networkJumpAnimationSequence;
    private Vector3 desiredMoveDirection;
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
    public MovementConfig Config => config;
    public bool IsJumpQueued => jumpQueued;
    public int JumpAnimationSequence => jumpAnimationSequence;
    public int LandingAnimationSequence => landingAnimationSequence;
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

        startYScale = transform.localScale.y;
        networkPosition = transform.position;
        networkRotation = transform.rotation;
    }

    private void Start()
    {
        ApplyAuthorityState();

        if (rb != null && config != null)
            RefreshSurfaceState();

        InitializeLandingAnimationState();
    }

    private void Update()
    {
        if (rb == null || config == null)
            return;

        if (!HasAuthority())
        {
            ApplyRemoteState();
            return;
        }

        RefreshSurfaceState();
        rb.linearDamping = IsGrounded && !surfaceState.IsSlidingSlope ? config.groundDrag : 0f;
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

        rawInput = Vector2.ClampMagnitude(input, 1f);

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
        CurrentState = MovementState.crouching;
        transform.localScale = new Vector3(transform.localScale.x, config.crouchYScale, transform.localScale.z);

        if (rb != null)
            rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
    }

    public void StopCrouch()
    {
        CurrentState = MovementState.idle;
        transform.localScale = new Vector3(transform.localScale.x, startYScale, transform.localScale.z);
    }

    public void SetState(MovementState state)
    {
        CurrentState = state;
    }

    private void RefreshSurfaceState()
    {
        surfaceState = ProbeSurface(desiredMoveDirection);

        if (IsJumpGroundSuppressed())
            SuppressGrounding(ref surfaceState);

        IsGrounded = surfaceState.HasGround;
        OnSlope = surfaceState.IsWalkableSlope || surfaceState.IsSlidingSlope;
        IsSlidingOnSlope = surfaceState.IsSlidingSlope;
        IsTouchingWall = surfaceState.HasWallBlock;

        if (surfaceState.HasGround && rb != null)
            airborneMomentumSpeed = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up).magnitude;
    }

    private bool HasAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private void ApplyAuthorityState()
    {
        if (rb == null)
            return;

        bool isMine = HasAuthority();
        rb.isKinematic = !isMine;
        rb.useGravity = isMine;

        if (!isMine)
            rb.linearVelocity = Vector3.zero;
    }

    private void ApplyRemoteState()
    {
        if (!hasNetworkState)
            return;

        CurrentState = networkState;
        Input = networkInput;
        IsGrounded = networkGrounded;
        jumpQueued = networkJumpQueued;
        jumpAnimationSequence = networkJumpAnimationSequence;
        landingAnimationSequence = networkLandingAnimationSequence;
        OnSlope = false;
        IsSlidingOnSlope = false;
        IsTouchingWall = false;
        rb.linearDamping = 0f;
    }

    private void InterpolateRemoteTransform(float deltaTime)
    {
        if (!hasNetworkState || rb == null)
            return;

        float distanceToTarget = Vector3.Distance(rb.position, networkPosition);
        if (distanceToTarget > remoteTeleportDistance)
        {
            rb.position = networkPosition;
            rb.rotation = networkRotation;
            return;
        }

        Vector3 predictedPosition = networkPosition + networkVelocity * (deltaTime * 0.5f);
        float positionT = 1f - Mathf.Exp(-remotePositionLerpSpeed * deltaTime);
        float rotationT = 1f - Mathf.Exp(-remoteRotationLerpSpeed * deltaTime);

        rb.MovePosition(Vector3.Lerp(rb.position, predictedPosition, positionT));
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, networkRotation, rotationT));
    }

    private void ApplyMovement()
    {
        if (surfaceState.IsSlidingSlope && !exitingSlope)
        {
            ApplySlidingMovement();
            return;
        }

        Vector3 moveDirection = GetEffectiveMoveDirection();
        float inputMagnitude = Mathf.Clamp01(moveDirection.magnitude);

        Vector3 supportNormal = surfaceState.IsWalkableSlope && !exitingSlope ? surfaceState.GroundNormal : Vector3.up;
        Vector3 targetVelocity = Vector3.zero;

        if (moveDirection.sqrMagnitude > 0.0001f && inputMagnitude > 0f)
        {
            Vector3 projectedDirection = Vector3.ProjectOnPlane(moveDirection.normalized, supportNormal);
            if (projectedDirection.sqrMagnitude > 0.0001f)
            {
                float targetSpeed = GetTargetMoveSpeed(includeDirectionChange: true) * inputMagnitude * currentAcceleration;
                targetVelocity = projectedDirection.normalized * targetSpeed;
            }
        }

        Vector3 currentPlanarVelocity = GetPlanarVelocity(supportNormal);
        bool shouldApplyAirCorrection = IsGrounded || inputMagnitude > 0f;
        Vector3 velocityDelta = shouldApplyAirCorrection ? targetVelocity - currentPlanarVelocity : Vector3.zero;

        float airControl = Mathf.Max(config.airMultiplier, 0f);
        float acceleration = IsGrounded
            ? config.groundAcceleration * GetDirectionChangeAccelerationMultiplier()
            : config.airAcceleration * airControl;

        if (shouldApplyAirCorrection && acceleration > 0f && velocityDelta.sqrMagnitude > 0.0001f)
        {
            Vector3 accelerationForce = Vector3.ClampMagnitude(velocityDelta / Time.fixedDeltaTime, acceleration);
            rb.AddForce(accelerationForce, ForceMode.Acceleration);
        }

        if (surfaceState.HasGround && rb.linearVelocity.y <= 0.5f)
            rb.AddForce(-surfaceState.GroundNormal * config.groundSnapAcceleration, ForceMode.Acceleration);
    }

    private void ApplySlidingMovement()
    {
        Vector3 slideDirection = Vector3.ProjectOnPlane(Vector3.down, surfaceState.GroundNormal);
        if (slideDirection.sqrMagnitude > 0.0001f)
        {
            slideDirection.Normalize();
            rb.AddForce(slideDirection * config.slideAcceleration, ForceMode.Acceleration);
        }

        if (surfaceState.HasGround && rb.linearVelocity.y <= 0.5f)
            rb.AddForce(-surfaceState.GroundNormal * config.groundSnapAcceleration, ForceMode.Acceleration);
    }

    private bool ApplyStepAssist()
    {
        if (!surfaceState.HasStep || !surfaceState.HasGround || surfaceState.IsSlidingSlope || desiredMoveDirection.sqrMagnitude <= 0.0001f)
            return false;

        float liftAmount = Mathf.Min(surfaceState.StepHeight, config.stepLiftSpeed * Time.fixedDeltaTime);
        if (liftAmount <= 0f)
            return false;

        rb.MovePosition(rb.position + Vector3.up * liftAmount);
        return true;
    }

    private float GetCurrentSpeed()
    {
        switch (CurrentState)
        {
            case MovementState.crouching:
                return config.crouchSpeed;
            case MovementState.sprinting:
                return config.sprintSpeed;
            default:
                return config.walkSpeed;
        }
    }

    private float GetTargetMoveSpeed(bool includeDirectionChange)
    {
        float baseSpeed = GetCurrentSpeed();
        bool preserveMomentum = jumpQueued || !IsGrounded;
        if (includeDirectionChange && !preserveMomentum)
            baseSpeed *= GetDirectionChangeSpeedMultiplier();

        return preserveMomentum ? Mathf.Max(baseSpeed, airborneMomentumSpeed) : baseSpeed;
    }

    private void SpeedControl()
    {
        float maxSpeed = surfaceState.IsSlidingSlope ? config.maxSlideSpeed : GetTargetMoveSpeed(includeDirectionChange: false);
        if (maxSpeed <= 0f)
            return;

        bool useSurfacePlane = surfaceState.IsWalkableSlope || surfaceState.IsSlidingSlope;
        Vector3 planeNormal = useSurfacePlane ? surfaceState.GroundNormal : Vector3.up;
        Vector3 planarVelocity = GetPlanarVelocity(planeNormal);

        if (planarVelocity.magnitude <= maxSpeed)
            return;

        Vector3 limitedPlanarVelocity = planarVelocity.normalized * maxSpeed;
        if (useSurfacePlane)
        {
            Vector3 normalVelocity = Vector3.Project(rb.linearVelocity, planeNormal);
            rb.linearVelocity = limitedPlanarVelocity + normalVelocity;
        }
        else
        {
            rb.linearVelocity = new Vector3(limitedPlanarVelocity.x, rb.linearVelocity.y, limitedPlanarVelocity.z);
        }
    }

    private Vector3 GetPlanarVelocity(Vector3 planeNormal)
    {
        if (planeNormal == Vector3.up)
            return new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        return Vector3.ProjectOnPlane(rb.linearVelocity, planeNormal);
    }

    private Vector3 GetEffectiveMoveDirection()
    {
        if (desiredMoveDirection.sqrMagnitude <= InputDeadzone)
            return Vector3.zero;

        if (surfaceState.IsSlidingSlope && !exitingSlope)
            return Vector3.zero;

        Vector3 effectiveMoveDirection = Vector3.ProjectOnPlane(desiredMoveDirection, Vector3.up);
        if (surfaceState.HasWallBlock)
            effectiveMoveDirection = Vector3.ProjectOnPlane(effectiveMoveDirection, surfaceState.WallNormal);

        return Vector3.ProjectOnPlane(effectiveMoveDirection, Vector3.up);
    }

    private void UpdateEffectiveInput()
    {
        Input = CalculateEffectiveInput(GetDirectionChangeSpeedMultiplier());
    }

    private void UpdateDirectionChangeInertia(float deltaTime)
    {
        AdvanceDirectionChangeInertia(deltaTime);
        UpdateDirectionChangeInputMemory(deltaTime);

        if (!CanUseDirectionChangeInertia())
        {
            ResetDirectionChangeInertia();
            ClearDirectionChangeHistory();
            return;
        }

        if (rawInput.sqrMagnitude <= InputDeadzone || desiredMoveDirection.sqrMagnitude <= InputDeadzone)
            return;

        if (!IsDirectionChangeInertiaActive()
            && TryGetDirectionChangeProfile(out DirectionChangeInertiaProfile profile)
            && ShouldTriggerDirectionChange(profile))
        {
            BeginDirectionChangeInertia(profile);
        }

        CacheDirectionChangeHistory();
    }

    private void AdvanceDirectionChangeInertia(float deltaTime)
    {
        if (!IsDirectionChangeInertiaActive())
        {
            directionChangeSpeedMultiplier = 1f;
            return;
        }

        if (directionChangeHoldTimer > 0f)
        {
            directionChangeHoldTimer = Mathf.Max(0f, directionChangeHoldTimer - deltaTime);
            directionChangeSpeedMultiplier = activeDirectionChangeProfile.speedMultiplier;

            if (directionChangeHoldTimer > 0f)
                return;
        }

        if (directionChangeRecoveryTimer > 0f)
        {
            directionChangeRecoveryTimer = Mathf.Max(0f, directionChangeRecoveryTimer - deltaTime);

            float recoveryDuration = Mathf.Max(activeDirectionChangeProfile.recoveryDuration, 0.0001f);
            float recoveryProgress = 1f - (directionChangeRecoveryTimer / recoveryDuration);
            directionChangeSpeedMultiplier = Mathf.Lerp(activeDirectionChangeProfile.speedMultiplier, 1f, recoveryProgress);

            if (directionChangeRecoveryTimer > 0f)
                return;
        }

        ResetDirectionChangeInertia();
    }

    private bool CanUseDirectionChangeInertia()
    {
        return HasAuthority()
            && config != null
            && IsGrounded
            && !jumpQueued
            && !surfaceState.IsSlidingSlope
            && !exitingSlope
            && (CurrentState == MovementState.walking || CurrentState == MovementState.sprinting);
    }

    private bool TryGetDirectionChangeProfile(out DirectionChangeInertiaProfile profile)
    {
        switch (CurrentState)
        {
            case MovementState.walking:
                profile = new DirectionChangeInertiaProfile(
                    config.walkCameraTurnReversalAngle,
                    config.walkReversalHoldTime,
                    config.walkReversalRecoveryTime,
                    config.walkReversalSpeedMultiplier,
                    config.walkReversalAccelerationMultiplier);
                return true;
            case MovementState.sprinting:
                profile = new DirectionChangeInertiaProfile(
                    config.sprintReversalAngle,
                    config.sprintReversalHoldTime,
                    config.sprintReversalRecoveryTime,
                    config.sprintReversalSpeedMultiplier,
                    config.sprintReversalAccelerationMultiplier);
                return true;
            default:
                profile = default;
                return false;
        }
    }

    private bool ShouldTriggerDirectionChange(DirectionChangeInertiaProfile profile)
    {
        if (!hasDirectionChangeHistory
            || rememberedDirectionalInput.sqrMagnitude <= InputDeadzone)
        {
            return false;
        }

        Vector3 currentDesiredDirection = desiredMoveDirection.normalized;
        Vector3 supportNormal = surfaceState.IsWalkableSlope ? surfaceState.GroundNormal : Vector3.up;
        Vector3 planarVelocity = GetPlanarVelocity(supportNormal);
        float planarSpeed = planarVelocity.magnitude;
        if (planarSpeed < Mathf.Max(0f, config.directionChangeMinPlanarSpeed))
            return false;

        float momentumDot = Vector3.Dot(planarVelocity / planarSpeed, currentDesiredDirection);
        if (momentumDot > profile.reversalDotThreshold)
            return false;

        Vector2 currentInputDirection = rawInput.normalized;
        Vector2 previousInputDirection = rememberedDirectionalInput.normalized;
        float inputDot = Vector2.Dot(previousInputDirection, currentInputDirection);

        bool cameraDrivenReversal = inputDot >= config.cameraTurnReversalInputAlignmentDot;
        bool inputDrivenReversal = inputDot <= config.sprintInputReversalDot;

        switch (CurrentState)
        {
            case MovementState.walking:
                return cameraDrivenReversal;
            case MovementState.sprinting:
                return cameraDrivenReversal || inputDrivenReversal;
            default:
                return false;
        }
    }

    private void BeginDirectionChangeInertia(DirectionChangeInertiaProfile profile)
    {
        activeDirectionChangeProfile = profile;
        directionChangeHoldTimer = Mathf.Max(0f, profile.holdDuration);
        directionChangeRecoveryTimer = Mathf.Max(0f, profile.recoveryDuration);
        directionChangeSpeedMultiplier = Mathf.Clamp01(profile.speedMultiplier);
    }

    private void ResetDirectionChangeInertia()
    {
        directionChangeHoldTimer = 0f;
        directionChangeRecoveryTimer = 0f;
        directionChangeSpeedMultiplier = 1f;
        activeDirectionChangeProfile = default;
    }

    private bool IsDirectionChangeInertiaActive()
    {
        return directionChangeHoldTimer > 0f || directionChangeRecoveryTimer > 0f;
    }

    private void CacheDirectionChangeHistory()
    {
        rememberedDirectionalInput = rawInput;
        directionChangeInputMemoryTimer = GetDirectionChangeInputMemoryDuration();
        hasDirectionChangeHistory = true;
    }

    private void ClearDirectionChangeHistory()
    {
        rememberedDirectionalInput = Vector2.zero;
        directionChangeInputMemoryTimer = 0f;
        hasDirectionChangeHistory = false;
    }

    private void UpdateDirectionChangeInputMemory(float deltaTime)
    {
        if (!hasDirectionChangeHistory)
            return;

        if (rawInput.sqrMagnitude > InputDeadzone)
        {
            directionChangeInputMemoryTimer = GetDirectionChangeInputMemoryDuration();
            return;
        }

        directionChangeInputMemoryTimer = Mathf.Max(0f, directionChangeInputMemoryTimer - deltaTime);
        if (directionChangeInputMemoryTimer <= 0f)
            ClearDirectionChangeHistory();
    }

    private float GetDirectionChangeInputMemoryDuration()
    {
        return Mathf.Max(0f, config != null ? config.directionChangeInputMemoryDuration : 0.12f);
    }

    private float GetDirectionChangeSpeedMultiplier()
    {
        return IsDirectionChangeInertiaActive() ? directionChangeSpeedMultiplier : 1f;
    }

    private float GetDirectionChangeAccelerationMultiplier()
    {
        return IsDirectionChangeInertiaActive()
            ? Mathf.Max(1f, activeDirectionChangeProfile.accelerationMultiplier)
            : 1f;
    }

    private Vector2 CalculateEffectiveInput(float magnitudeScale)
    {
        if (orientation == null || rawInput.sqrMagnitude <= InputDeadzone)
            return Vector2.zero;

        Vector3 effectiveMoveDirection = GetEffectiveMoveDirection();
        if (effectiveMoveDirection.sqrMagnitude <= InputDeadzone)
            return Vector2.zero;

        Vector3 localDirection = orientation.InverseTransformDirection(effectiveMoveDirection);
        return Vector2.ClampMagnitude(new Vector2(localDirection.x, localDirection.z), 1f) * Mathf.Clamp01(magnitudeScale);
    }

    private void ApplyJumpForce()
    {
        if (rb == null || config == null || !jumpQueued)
            return;

        RefreshSurfaceState();
        jumpQueued = false;
        exitingSlope = true;
        jumpGroundIgnoreUntil = Time.time + Mathf.Max(0f, config.jumpGroundIgnoreTime);

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        airborneMomentumSpeed = horizontalVelocity.magnitude;
        rb.linearVelocity = horizontalVelocity;
        CurrentState = MovementState.air;
        rb.AddForce(Vector3.up * config.jumpForce, ForceMode.Impulse);

        float slopeExitDuration = Mathf.Max(config.jumpCooldown, config.jumpGroundIgnoreTime);
        Invoke(nameof(ResetJump), slopeExitDuration);
    }

    private void ResetJump()
    {
        jumpQueued = false;
        exitingSlope = false;
    }

    private void InitializeLandingAnimationState()
    {
        wasGroundedForLandingAnimation = IsGrounded;
        hasAirborneLandingPhase = !IsGrounded;
        landingAnimationArmed = false;
        landingAnimationTriggered = false;
        airborneLandingTime = 0f;
        airborneLandingStartHeight = transform.position.y;
        highestAirborneLandingHeight = airborneLandingStartHeight;
        groundedLandingConfirmTime = 0f;
        mostNegativeLandingVerticalSpeed = VerticalVelocity;
    }

    private void UpdateLandingAnimationState(float deltaTime)
    {
        if (!HasAuthority() || config == null)
            return;

        float verticalSpeed = VerticalVelocity;

        if (IsGrounded)
        {
            if (hasAirborneLandingPhase)
            {
                groundedLandingConfirmTime += deltaTime;

                if (!landingAnimationTriggered
                    && landingAnimationArmed
                    && groundedLandingConfirmTime >= Mathf.Max(0f, config.groundedConfirmTimeForLand))
                {
                    landingAnimationSequence++;
                    landingAnimationTriggered = true;
                }

                if (groundedLandingConfirmTime >= Mathf.Max(0f, config.groundedConfirmTimeForLand))
                    ResetLandingAnimationState();
            }
            else
            {
                groundedLandingConfirmTime = 0f;
            }

            wasGroundedForLandingAnimation = true;
            return;
        }

        groundedLandingConfirmTime = 0f;

        if (!hasAirborneLandingPhase || wasGroundedForLandingAnimation)
        {
            BeginAirborneLandingPhase(verticalSpeed);
        }
        else
        {
            airborneLandingTime += deltaTime;
            highestAirborneLandingHeight = Mathf.Max(highestAirborneLandingHeight, transform.position.y);
            mostNegativeLandingVerticalSpeed = Mathf.Min(mostNegativeLandingVerticalSpeed, verticalSpeed);
        }

        landingAnimationArmed |= ShouldArmLandingAnimation();
        wasGroundedForLandingAnimation = false;
    }

    private void BeginAirborneLandingPhase(float verticalSpeed)
    {
        hasAirborneLandingPhase = true;
        airborneLandingTime = 0f;
        airborneLandingStartHeight = transform.position.y;
        highestAirborneLandingHeight = airborneLandingStartHeight;
        groundedLandingConfirmTime = 0f;
        landingAnimationArmed = false;
        landingAnimationTriggered = false;
        mostNegativeLandingVerticalSpeed = verticalSpeed;
    }

    private bool ShouldArmLandingAnimation()
    {
        float airborneRise = highestAirborneLandingHeight - airborneLandingStartHeight;

        if (airborneLandingTime >= Mathf.Max(0f, config.minAirTimeForLand))
            return true;

        if (airborneRise >= Mathf.Max(0f, config.minAirHeightForLand))
            return true;

        if (mostNegativeLandingVerticalSpeed <= -Mathf.Max(0f, config.minDownwardSpeedForLand))
            return true;

        return false;
    }

    private void ResetLandingAnimationState()
    {
        hasAirborneLandingPhase = false;
        landingAnimationArmed = false;
        landingAnimationTriggered = false;
        airborneLandingTime = 0f;
        airborneLandingStartHeight = transform.position.y;
        highestAirborneLandingHeight = airborneLandingStartHeight;
        groundedLandingConfirmTime = 0f;
        mostNegativeLandingVerticalSpeed = VerticalVelocity;
    }

    private bool IsJumpGroundSuppressed()
    {
        return Time.time < jumpGroundIgnoreUntil;
    }

    private void SuppressGrounding(ref SurfaceProbeResult result)
    {
        result.HasGround = false;
        result.IsWalkableSlope = false;
        result.IsSlidingSlope = false;
        result.HasStep = false;
        result.GroundAngle = 0f;
    }

    private SurfaceProbeResult ProbeSurface(Vector3 moveDirection)
    {
        SurfaceProbeResult result = new SurfaceProbeResult();
        if (config == null)
            return result;

        CapsuleShape capsuleShape = CapsuleShape.Create(transform, capsule, config.playerHeight);
        int collisionMask = GetCollisionMask();

        result.GroundProbeRadius = capsuleShape.Radius * Mathf.Clamp(config.groundProbeRadiusScale, 0.5f, 1f);
        result.GroundProbeDistance = Mathf.Max(config.groundProbeDistance, 0.05f);
        result.GroundProbeOrigin = capsuleShape.BottomHemisphereCenter + Vector3.up * 0.02f;

        if (TrySphereCastIgnoringSelf(result.GroundProbeOrigin, result.GroundProbeRadius, Vector3.down, result.GroundProbeDistance + 0.02f, collisionMask, out RaycastHit groundHit))
        {
            float groundAngle = Vector3.Angle(Vector3.up, groundHit.normal);
            if (groundAngle <= config.slideSlopeAngle)
            {
                result.HasGround = true;
                result.GroundHit = groundHit;
                result.GroundNormal = groundHit.normal;
                result.GroundAngle = groundAngle >= config.minSlopeAngleToAffect ? groundAngle : 0f;
                result.IsWalkableSlope = groundAngle >= config.minSlopeAngleToAffect && groundAngle <= config.maxSlopeAngle;
                result.IsSlidingSlope = !exitingSlope && groundAngle > config.maxSlopeAngle && groundAngle <= config.slideSlopeAngle;
            }
        }

        result.WallProbeRadius = capsuleShape.Radius * Mathf.Clamp(config.wallCheckRadiusScale, 0.3f, 1f);
        result.WallProbeDistance = Mathf.Max(config.wallCheckDistance, 0.05f);

        float lowerProbeHeight = result.WallProbeRadius + Mathf.Max(0.02f, config.maxStepHeight * 0.5f);
        float upperProbeHeight = Mathf.Clamp(capsuleShape.Height * config.upperWallCheckHeightRatio, result.WallProbeRadius + 0.05f, capsuleShape.Height - result.WallProbeRadius);

        result.LowerWallProbeOrigin = capsuleShape.LowestPoint + Vector3.up * lowerProbeHeight;
        result.UpperWallProbeOrigin = capsuleShape.LowestPoint + Vector3.up * upperProbeHeight;

        Vector3 horizontalMove = Vector3.ProjectOnPlane(moveDirection, Vector3.up);
        if (horizontalMove.sqrMagnitude <= 0.0001f)
            return result;

        result.ProbeDirection = horizontalMove.normalized;

        bool hasLowerHit = TrySphereCastIgnoringSelf(result.LowerWallProbeOrigin, result.WallProbeRadius, result.ProbeDirection, result.WallProbeDistance, collisionMask, out RaycastHit lowerHit);
        bool hasUpperHit = TrySphereCastIgnoringSelf(result.UpperWallProbeOrigin, result.WallProbeRadius, result.ProbeDirection, result.WallProbeDistance, collisionMask, out RaycastHit upperHit);

        if (hasLowerHit)
        {
            result.HasLowerHit = true;
            result.LowerHit = lowerHit;
        }

        if (hasUpperHit)
        {
            result.HasUpperHit = true;
            result.UpperHit = upperHit;
        }

        bool lowerIsWall = hasLowerHit && IsWallLike(lowerHit.normal);
        bool upperIsWall = hasUpperHit && IsWallLike(upperHit.normal);

        if (upperIsWall)
        {
            result.HasWallBlock = true;
            result.WallHit = upperHit;
            result.WallNormal = upperHit.normal;
            return result;
        }

        if (lowerIsWall && result.HasGround && !result.IsSlidingSlope && TryFindStep(result.ProbeDirection, capsuleShape, collisionMask, out RaycastHit stepHit, out float stepHeight))
        {
            result.HasStep = true;
            result.StepHit = stepHit;
            result.StepHeight = stepHeight;
            return result;
        }

        if (lowerIsWall)
        {
            result.HasWallBlock = true;
            result.WallHit = lowerHit;
            result.WallNormal = lowerHit.normal;
        }

        return result;
    }

    private bool TryFindStep(Vector3 moveDirection, CapsuleShape capsuleShape, int collisionMask, out RaycastHit stepHit, out float stepHeight)
    {
        stepHit = default;
        stepHeight = 0f;

        Vector3 origin = capsuleShape.LowestPoint
            + Vector3.up * (config.maxStepHeight + config.groundProbeDistance + 0.05f)
            + moveDirection * (capsuleShape.Radius + config.stepSearchDistance);

        float rayDistance = config.maxStepHeight + config.groundProbeDistance + 0.1f;
        if (!TryRaycastIgnoringSelf(origin, Vector3.down, rayDistance, collisionMask, out stepHit))
            return false;

        float stepSurfaceAngle = Vector3.Angle(Vector3.up, stepHit.normal);
        if (stepSurfaceAngle > config.maxSlopeAngle)
            return false;

        stepHeight = stepHit.point.y - capsuleShape.LowestPoint.y;
        return stepHeight > 0.01f && stepHeight <= config.maxStepHeight;
    }

    private bool IsWallLike(Vector3 normal)
    {
        float surfaceAngle = Vector3.Angle(Vector3.up, normal);
        return surfaceAngle > config.slideSlopeAngle;
    }

    private int GetCollisionMask()
    {
        return config.groundLayer.value == 0 ? Physics.DefaultRaycastLayers : config.groundLayer.value;
    }

    private bool TrySphereCastIgnoringSelf(Vector3 origin, float radius, Vector3 direction, float distance, int collisionMask, out RaycastHit closestHit)
    {
        closestHit = default;
        int hitCount = Physics.SphereCastNonAlloc(origin, radius, direction, sphereCastHits, distance, collisionMask, QueryTriggerInteraction.Ignore);
        return TryGetClosestValidHit(sphereCastHits, hitCount, out closestHit);
    }

    private bool TryRaycastIgnoringSelf(Vector3 origin, Vector3 direction, float distance, int collisionMask, out RaycastHit closestHit)
    {
        closestHit = default;
        int hitCount = Physics.RaycastNonAlloc(origin, direction, raycastHits, distance, collisionMask, QueryTriggerInteraction.Ignore);
        return TryGetClosestValidHit(raycastHits, hitCount, out closestHit);
    }

    private bool TryGetClosestValidHit(RaycastHit[] hits, int hitCount, out RaycastHit closestHit)
    {
        closestHit = default;
        bool foundHit = false;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || IsSelfCollider(hit.collider))
                continue;

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                foundHit = true;
            }
        }

        return foundHit;
    }

    private bool IsSelfCollider(Collider collider)
    {
        if (collider == null)
            return false;

        if (capsule != null && collider == capsule)
            return true;

        if (rb != null && collider.attachedRigidbody == rb)
            return true;

        return collider.transform.root == transform.root;
    }

    private void OnDrawGizmosSelected()
    {
        if (config == null)
            return;

        SurfaceProbeResult gizmoState = Application.isPlaying
            ? surfaceState
            : ProbeSurface(orientation != null ? orientation.forward : transform.forward);

        DrawGroundGizmos(gizmoState);
        DrawWallAndStepGizmos(gizmoState);
        DrawSurfaceLabels(gizmoState);
    }

    private void DrawGroundGizmos(SurfaceProbeResult gizmoState)
    {
        Color groundColor = Color.red;
        if (gizmoState.IsWalkableSlope)
            groundColor = Color.green;
        else if (gizmoState.IsSlidingSlope)
            groundColor = new Color(1f, 0.55f, 0f);
        else if (gizmoState.HasGround)
            groundColor = Color.yellow;

        Gizmos.color = groundColor;
        Gizmos.DrawWireSphere(gizmoState.GroundProbeOrigin, gizmoState.GroundProbeRadius);
        Gizmos.DrawLine(gizmoState.GroundProbeOrigin, gizmoState.GroundProbeOrigin + Vector3.down * gizmoState.GroundProbeDistance);
        Gizmos.DrawWireSphere(gizmoState.GroundProbeOrigin + Vector3.down * gizmoState.GroundProbeDistance, gizmoState.GroundProbeRadius);

        if (!gizmoState.HasGround)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(gizmoState.GroundHit.point, gizmoState.GroundHit.point + gizmoState.GroundNormal * 0.6f);
        Gizmos.DrawWireSphere(gizmoState.GroundHit.point, 0.05f);
    }

    private void DrawWallAndStepGizmos(SurfaceProbeResult gizmoState)
    {
        if (gizmoState.ProbeDirection.sqrMagnitude <= 0.0001f)
            return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.7f);
        Gizmos.DrawWireSphere(gizmoState.LowerWallProbeOrigin, gizmoState.WallProbeRadius);
        Gizmos.DrawLine(gizmoState.LowerWallProbeOrigin, gizmoState.LowerWallProbeOrigin + gizmoState.ProbeDirection * gizmoState.WallProbeDistance);
        Gizmos.DrawWireSphere(gizmoState.LowerWallProbeOrigin + gizmoState.ProbeDirection * gizmoState.WallProbeDistance, gizmoState.WallProbeRadius);

        Gizmos.color = new Color(1f, 0.65f, 0f, 0.7f);
        Gizmos.DrawWireSphere(gizmoState.UpperWallProbeOrigin, gizmoState.WallProbeRadius);
        Gizmos.DrawLine(gizmoState.UpperWallProbeOrigin, gizmoState.UpperWallProbeOrigin + gizmoState.ProbeDirection * gizmoState.WallProbeDistance);
        Gizmos.DrawWireSphere(gizmoState.UpperWallProbeOrigin + gizmoState.ProbeDirection * gizmoState.WallProbeDistance, gizmoState.WallProbeRadius);

        if (gizmoState.HasWallBlock)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(gizmoState.WallHit.point, 0.1f);
            Gizmos.DrawLine(gizmoState.WallHit.point, gizmoState.WallHit.point + gizmoState.WallNormal * 0.5f);
        }

        if (!gizmoState.HasStep)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(gizmoState.StepHit.point, 0.08f);
        Gizmos.DrawLine(gizmoState.StepHit.point, gizmoState.StepHit.point + Vector3.up * gizmoState.StepHeight);
    }

    private void DrawSurfaceLabels(SurfaceProbeResult gizmoState)
    {
#if UNITY_EDITOR
        if (gizmoState.HasGround)
        {
            string slopeMode = gizmoState.IsSlidingSlope ? "Slide" : gizmoState.IsWalkableSlope ? "Walkable" : "Ground";
            Handles.Label(gizmoState.GroundHit.point + Vector3.up * 0.15f, $"{slopeMode} {gizmoState.GroundAngle:F1} deg");
        }

        Vector3 labelPosition = transform.position + Vector3.up * 1.2f;
        string wallStatus = gizmoState.HasWallBlock ? "Wall block" : "Wall free";
        string stepStatus = gizmoState.HasStep ? $"Step {gizmoState.StepHeight:F2}m" : "No step";
        string jumpStatus = jumpQueued ? "Jump queued" : IsJumpGroundSuppressed() ? "Jump unground" : "Ground active";
        Handles.Label(labelPosition, $"{wallStatus}\n{stepStatus}\n{jumpStatus}");
#endif
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(rb != null ? rb.linearVelocity : Vector3.zero);
            stream.SendNext((int)CurrentState);
            stream.SendNext(Input);
            stream.SendNext(IsGrounded);
            stream.SendNext(jumpQueued);
            stream.SendNext(jumpAnimationSequence);
            stream.SendNext(landingAnimationSequence);
            return;
        }

        networkPosition = (Vector3)stream.ReceiveNext();
        networkRotation = (Quaternion)stream.ReceiveNext();
        networkVelocity = (Vector3)stream.ReceiveNext();
        networkState = (MovementState)(int)stream.ReceiveNext();
        networkInput = (Vector2)stream.ReceiveNext();
        networkGrounded = (bool)stream.ReceiveNext();
        networkJumpQueued = (bool)stream.ReceiveNext();
        networkJumpAnimationSequence = (int)stream.ReceiveNext();
        networkLandingAnimationSequence = (int)stream.ReceiveNext();

        if (!hasNetworkState)
        {
            hasNetworkState = true;
            transform.SetPositionAndRotation(networkPosition, networkRotation);
        }

        ApplyRemoteState();
    }

    private struct SurfaceProbeResult
    {
        public bool HasGround;
        public bool IsWalkableSlope;
        public bool IsSlidingSlope;
        public bool HasWallBlock;
        public bool HasStep;
        public bool HasLowerHit;
        public bool HasUpperHit;
        public float GroundAngle;
        public float StepHeight;
        public float GroundProbeRadius;
        public float GroundProbeDistance;
        public float WallProbeRadius;
        public float WallProbeDistance;
        public Vector3 GroundNormal;
        public Vector3 WallNormal;
        public Vector3 ProbeDirection;
        public Vector3 GroundProbeOrigin;
        public Vector3 LowerWallProbeOrigin;
        public Vector3 UpperWallProbeOrigin;
        public RaycastHit GroundHit;
        public RaycastHit WallHit;
        public RaycastHit LowerHit;
        public RaycastHit UpperHit;
        public RaycastHit StepHit;
    }

    private readonly struct CapsuleShape
    {
        public readonly float Height;
        public readonly float Radius;
        public readonly Vector3 Center;
        public readonly Vector3 LowestPoint;
        public readonly Vector3 BottomHemisphereCenter;

        private CapsuleShape(float height, float radius, Vector3 center, Vector3 lowestPoint, Vector3 bottomHemisphereCenter)
        {
            Height = height;
            Radius = radius;
            Center = center;
            LowestPoint = lowestPoint;
            BottomHemisphereCenter = bottomHemisphereCenter;
        }

        public static CapsuleShape Create(Transform target, CapsuleCollider capsuleCollider, float fallbackHeight)
        {
            float scaleX = Mathf.Abs(target.lossyScale.x);
            float scaleY = Mathf.Abs(target.lossyScale.y);
            float scaleZ = Mathf.Abs(target.lossyScale.z);

            float radius = capsuleCollider != null
                ? capsuleCollider.radius * Mathf.Max(scaleX, scaleZ)
                : 0.5f * Mathf.Max(scaleX, scaleZ);

            float height = capsuleCollider != null
                ? Mathf.Max(capsuleCollider.height * scaleY, radius * 2f)
                : Mathf.Max(fallbackHeight * scaleY, radius * 2f);

            Vector3 center = capsuleCollider != null
                ? target.TransformPoint(capsuleCollider.center)
                : target.position;

            Vector3 lowestPoint = center - Vector3.up * (height * 0.5f);
            Vector3 bottomHemisphereCenter = lowestPoint + Vector3.up * radius;

            return new CapsuleShape(height, radius, center, lowestPoint, bottomHemisphereCenter);
        }
    }

    private readonly struct DirectionChangeInertiaProfile
    {
        public readonly float reversalDotThreshold;
        public readonly float holdDuration;
        public readonly float recoveryDuration;
        public readonly float speedMultiplier;
        public readonly float accelerationMultiplier;

        public DirectionChangeInertiaProfile(
            float reversalAngle,
            float holdDuration,
            float recoveryDuration,
            float speedMultiplier,
            float accelerationMultiplier)
        {
            reversalDotThreshold = Mathf.Cos(Mathf.Clamp(reversalAngle, 0f, 180f) * Mathf.Deg2Rad);
            this.holdDuration = Mathf.Max(0f, holdDuration);
            this.recoveryDuration = Mathf.Max(0f, recoveryDuration);
            this.speedMultiplier = Mathf.Clamp01(speedMultiplier);
            this.accelerationMultiplier = Mathf.Max(1f, accelerationMultiplier);
        }
    }
}
