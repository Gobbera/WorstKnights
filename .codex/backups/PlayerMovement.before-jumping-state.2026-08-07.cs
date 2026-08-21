using Photon.Pun;
using UnityEngine;

public enum PlayerEmoteType
{
    None = 0,
    ThumbsUp = 1,
    Point = 2
}

public enum PlayerDamageAnimationType
{
    None = 0,
    ReactionDamage = 1
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(PhotonView))]
public partial class PlayerMovement : MonoBehaviour, IPlayerMovement, IPunObservable
{
    private const float InputDeadzone = 0.0001f;
    private const float StandClearancePadding = 0.02f;
    private const float StaminaEpsilon = 0.01f;
    private const float SurfaceProbeVerticalOffset = 0.02f;
    private const float GroundSnapMaxSeparationSpeed = 1.5f;
    private const float SurfaceAdhesionGraceTime = 0.12f;
    private const float SurfaceAdhesionExtraProbeDistance = 0.24f;
    private const float SurfaceAdhesionMaxSnapDistance = 0.35f;
    private const int FirstAttackComboStep = 1;
    private const int MaxAttackComboStep = MovementConfig.AttackComboStepCount;
    private const string OrientationObjectName = "Orientation";
    private const string ModelObjectName = "Model";
    private const string FirstPersonCameraObjectName = "FP_Camera";
    private const string FirstPersonModelObjectName = "FPS_Model";
    private const string ReactionDamageStateName = "Reaction Damage";
    private const string UpperBodyAttackLayerName = "Upper Body Attack";
    private const string ThumbsUpLayerName = "Thumbs Up";
    private static PhysicsMaterial runtimeSlideColliderMaterial;

    [SerializeField] private MovementConfig config;
    [SerializeField] private Transform orientation;
    [Header("Network Sync")]
    [SerializeField] private float remotePositionLerpSpeed = 12f;
    [SerializeField] private float remoteRotationLerpSpeed = 16f;
    [SerializeField] private float remoteTeleportDistance = 4f;
    [Header("Head Look")]
    [SerializeField] private bool enableHeadLook = true;
    [SerializeField] private bool allowHeadLookWhileWalking;
    [SerializeField] private bool allowHeadLookWhileCrouching = true;
    [SerializeField] private bool disableHeadLookDuringAttackLayer = true;
    [Header("Attack Aim Pose")]
    [SerializeField] private bool enableAttackAimPose = true;
    [SerializeField] [Range(0f, 1f)] private float attackAimPoseWeight = 0.75f;
    [SerializeField] [Range(0f, 60f)] private float attackAimPitchLimit = 35f;
    [SerializeField] [Range(0f, 30f)] private float attackAimSpinePitchLimit = 14f;
    [SerializeField] [Range(0f, 35f)] private float attackAimChestPitchLimit = 22f;
    [SerializeField] [Range(0f, 15f)] private float attackAimHeadPitchLimit = 2f;
    [SerializeField] [Min(0f)] private float attackAimAcquireSmoothTime = 0.06f;
    [SerializeField] [Min(0f)] private float attackAimReleaseSmoothTime = 0.16f;
    [Header("Head Look Safety")]
    [SerializeField] private bool disableHeadLookDuringThumbsUpLayer = true;
    [SerializeField] [Range(0f, 80f)] private float horizontalHeadLookLimit = 35f;
    [SerializeField] [Range(0f, 45f)] private float headPitchLimit = 18f;
    [SerializeField] [Range(0f, 30f)] private float chestPitchLimit = 12f;
    [SerializeField] [Range(0f, 25f)] private float spinePitchLimit = 8f;
    [SerializeField] [Min(0f)] private float headLookAcquireSmoothTime = 0.12f;
    [SerializeField] [Min(0f)] private float headLookReleaseSmoothTime = 0.12f;
    [SerializeField] private bool invertPosePitch = true;
    [SerializeField] private bool invertPoseYaw;
    [SerializeField] private string spineBonePath = "EditedKnight/Armature/Root/Hip/Spine";
    [SerializeField] private string chestBonePath = "EditedKnight/Armature/Root/Hip/Spine/Chest";
    [SerializeField] private string headBonePath = "EditedKnight/Armature/Root/Hip/Spine/Chest/Head";
    [Header("Head Look Axes")]
    [SerializeField] private Vector3 spinePitchAxis = Vector3.forward;
    [SerializeField] private Vector3 chestPitchAxis = Vector3.forward;
    [SerializeField] private Vector3 headPitchAxis = Vector3.right;
    [SerializeField] private Vector3 headYawAxis = Vector3.up;

    private readonly RaycastHit[] sphereCastHits = new RaycastHit[8];
    private readonly RaycastHit[] raycastHits = new RaycastHit[8];

    private float currentAcceleration;
    private float attackMovementSlowUntil;
    private float kickMovementSlowUntil;
    private float fallMovementSlowUntil;
    private float damageKnockbackControlLockUntil;
    private float directionChangeSpeedMultiplier = 1f;
    private float directionChangeHoldTimer;
    private float directionChangeRecoveryTimer;
    private float directionChangeInputMemoryTimer;
    private float startCapsuleHeight;
    private float nextAttackAllowedTime;
    private float activeAttackEndTime;
    private float activeAttackInputWindowOpenTime;
    private float activeAttackInputWindowCloseTime;
    private float nextKickAllowedTime;
    private float activeKickEndTime;
    private float activePickupActionEndTime;
    private float activeDrawActionEndTime;
    private float activeInventoryItemActionEndTime;
    private float activeEmoteActionEndTime;
    private float nextJumpInputAllowedTime;
    private float jumpGroundIgnoreUntil;
    private float airborneMomentumSpeed;
    private float currentLocomotionSpeed;
    private float locomotionSpeedVelocity;
    private float sprintStrafeOpenTimer;
    private float sprintStrafeDirectionSign;
    private float currentStamina;
    private float lastStaminaUseTime = float.NegativeInfinity;
    private bool exitingSlope;
    private bool jumpQueued;
    private bool networkJumpQueued;
    private bool hasLocomotionSpeedSample;
    private bool isSprintHeld;
    private bool isConsumingStamina;
    private bool sprintRecoveryLocked;
    private bool sprintRecoveryPrimed;
    private bool sprintReleaseBlendActive;
    private bool sprintRepressRequiredAfterAttack;
    private bool networkAttackMovementSlowActive;
    private bool networkKickMovementSlowActive;
    private bool networkFallMovementSlowActive;
    private bool hasQueuedAttackComboStep;
    private bool emoteWheelSelectionActive;
    private bool directionChangeBrakeActive;
    private bool rightHandOccupied;
    private bool leftHandOccupied;
    private bool leftHandTorchEquipped;
    private bool networkRightHandOccupied;
    private bool networkLeftHandOccupied;
    private bool networkLeftHandTorchEquipped;
    private HandType pickupAnimationHand = HandType.Right;
    private HandType networkPickupAnimationHand = HandType.Right;
    private HandType drawAnimationHand = HandType.Right;
    private HandType networkDrawAnimationHand = HandType.Right;
    private int attackAnimationSequence;
    private int attackComboStep;
    private int queuedAttackComboStep;
    private int kickAnimationSequence;
    private int jumpAnimationSequence;
    private int networkAttackAnimationSequence;
    private int networkAttackComboStep;
    private int networkKickAnimationSequence;
    private int networkJumpAnimationSequence;
    private int pickupAnimationSequence;
    private int networkPickupAnimationSequence;
    private int drawAnimationSequence;
    private int networkDrawAnimationSequence;
    private int damageAnimationSequence;
    private int networkDamageAnimationSequence;
    private int emoteAnimationSequence;
    private int networkEmoteAnimationSequence;
    private Vector3 desiredMoveDirection;
    private Vector3 startCapsuleCenter;
    private Vector2 requestedInput;
    private Vector2 rawInput;
    private Vector2 rememberedDirectionalInput;
    private Rigidbody rb;
    private CapsuleCollider capsule;
    private PhotonView photonView;
    private PlayerHealth playerHealth;
    private Animator modelAnimator;
    private MouseLook mouseLook;
    private Transform spineBone;
    private Transform chestBone;
    private Transform headBone;
    private SurfaceProbeResult surfaceState;
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private Vector3 networkVelocity;
    private Vector2 networkInput;
    private Vector2 networkAnimationInput;
    private float lookYawOffset;
    private float lookPitch;
    private float appliedLookYawOffset;
    private float appliedLookPitch;
    private float appliedLookYawVelocity;
    private float appliedLookPitchVelocity;
    private float appliedAttackAimPitch;
    private float appliedAttackAimPitchVelocity;
    private float networkLookYawOffset;
    private float networkLookPitch;
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
    private int upperBodyAttackLayerIndex = -1;
    private int thumbsUpLayerIndex = -1;
    private PlayerDamageAnimationType currentDamageAnimationType;
    private PlayerDamageAnimationType networkDamageAnimationType;
    private PlayerEmoteType currentEmoteType;
    private PlayerEmoteType networkEmoteType;
    private bool hasDirectionChangeHistory;
    private bool wasHeadLookPoseAllowedLastFrame;
    private bool isHeadLookPoseAcquiring;
    private DirectionChangeInertiaProfile activeDirectionChangeProfile;
    private float surfaceAdhesionEligibleUntil;

    public bool OnSlope { get; private set; }
    public bool IsTouchingWall { get; private set; }
    public bool IsSlidingOnSlope { get; private set; }
    public float CurrentSlopeAngle => surfaceState.GroundAngle;

    public bool IsGrounded { get; private set; }
    public MovementState CurrentState { get; private set; }
    public Vector2 Input { get; private set; }
    public Vector2 AnimationInput { get; private set; }
    public MovementConfig Config => config;
    public bool HasLocomotionIntent => HasAuthority()
        ? rawInput.sqrMagnitude > InputDeadzone
        : networkInput.sqrMagnitude > InputDeadzone;
    public bool IsJumpQueued => jumpQueued;
    public bool IsMovementControlLocked => HasAuthority() && Time.time < GetEffectiveMovementControlLockUntil();
    public int AttackAnimationSequence => attackAnimationSequence;
    public int AttackComboStep => attackComboStep;
    public int KickAnimationSequence => kickAnimationSequence;
    public int JumpAnimationSequence => jumpAnimationSequence;
    public int LandingAnimationSequence => landingAnimationSequence;
    public int PickupAnimationSequence => pickupAnimationSequence;
    public HandType PickupAnimationHand => pickupAnimationHand;
    public int DrawAnimationSequence => drawAnimationSequence;
    public HandType DrawAnimationHand => drawAnimationHand;
    public int DamageAnimationSequence => damageAnimationSequence;
    public PlayerDamageAnimationType CurrentDamageAnimationType => currentDamageAnimationType;
    public int EmoteAnimationSequence => emoteAnimationSequence;
    public PlayerEmoteType CurrentEmoteType => currentEmoteType;
    public bool IsRightHandOccupied => rightHandOccupied;
    public bool IsLeftHandOccupied => leftHandOccupied;
    public bool IsLeftHandTorchEquipped => leftHandTorchEquipped;
    public bool IsEmoteWheelSelectionActive => emoteWheelSelectionActive;
    public float LocomotionScale { get; private set; } = 1f;
    public float CurrentStamina => currentStamina;
    public float MaxStamina => GetConfiguredMaxStamina();
    public float StaminaNormalized => MaxStamina <= StaminaEpsilon ? 0f : currentStamina / MaxStamina;
    public bool IsConsumingStamina => isConsumingStamina;
    public bool IsInventorySlotChangeLocked
    {
        get
        {
            if (!HasAuthority())
                return false;

            UpdateAttackComboState();
            return IsInventorySlotChangeLockedAt(Time.time);
        }
    }
    public bool ShouldShowStaminaBar => isConsumingStamina || currentStamina < MaxStamina - StaminaEpsilon;
    public bool CanSprint
    {
        get
        {
            if (HasAuthority())
                UpdateAttackComboState();

            return HasAvailableStaminaForSprint()
                && !sprintRepressRequiredAfterAttack
                && !IsAttackSprintBlocked(Time.time);
        }
    }
    public bool UsesDrivenLookControl => true;
    public float LookYawOffset => HasAuthority() ? lookYawOffset : networkLookYawOffset;
    public float LookPitch => HasAuthority() ? lookPitch : networkLookPitch;
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
        playerHealth = GetComponent<PlayerHealth>();
        CacheLookReferences();

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
            ApplyCharacterColliderTuning();
        }

        networkPosition = transform.position;
        networkRotation = transform.rotation;
    }

    private void Start()
    {
        ApplyAuthorityState();
        InitializeStaminaState();

        if (rb != null && config != null)
            RefreshSurfaceState();

        CacheLookReferences();
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
        RefreshMovementVolumeModifiers();
        rb.linearDamping = IsGrounded && !surfaceState.IsSlidingSlope ? ResolveGroundDrag() : 0f;
        UpdateLocomotionSpeedBlend(Time.deltaTime);
        UpdateLandingAnimationState(Time.deltaTime);
        UpdateAttackComboState();
    }

    private void LateUpdate()
    {
        UpdateLookPose();
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
        RefreshMovementVolumeModifiers();

        if (IsMovementControlLocked)
        {
            ClearMovementInput();
            return;
        }

        if (ApplyStepAssist())
            RefreshSurfaceState();

        UpdateDirectionChangeInertia(Time.fixedDeltaTime);
        UpdateStamina(Time.fixedDeltaTime);
        UpdateEffectiveInput();
        ApplyMovement();
        if (ApplyRecentGroundAdhesion())
            RefreshSurfaceState();
        SpeedControl();
        ApplyMovementVolumeConveyor();
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

        if (IsMovementControlLocked)
        {
            ClearMovementInput();
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
        TryBeginDirectionChangeInertiaFromCurrentInput();
        UpdateEffectiveInput();
    }

    public void ApplyDamageKnockback(Vector3 velocityChange, float controlLockDuration)
    {
        if (!HasAuthority() || rb == null || velocityChange.sqrMagnitude <= 0.0001f)
            return;

        rb.AddForce(velocityChange, ForceMode.VelocityChange);

        float safeLockDuration = Mathf.Max(0f, controlLockDuration);
        if (safeLockDuration > 0f)
            damageKnockbackControlLockUntil = Mathf.Max(damageKnockbackControlLockUntil, Time.time + safeLockDuration);
    }

    public void ClearTemporaryMovementPenalties()
    {
        attackMovementSlowUntil = 0f;
        kickMovementSlowUntil = 0f;
        activeKickEndTime = 0f;
        activePickupActionEndTime = 0f;
        activeDrawActionEndTime = 0f;
        activeInventoryItemActionEndTime = 0f;
        activeEmoteActionEndTime = 0f;
        fallMovementSlowUntil = 0f;
        damageKnockbackControlLockUntil = 0f;
        ResetAttackComboState();
        sprintReleaseBlendActive = false;
        sprintRepressRequiredAfterAttack = false;
        ClearMovementVolumeRuntimeState();
        UpdateLocomotionSpeedBlend(0f);
    }

    public void Attack()
    {
        TryAttack();
    }

    public bool TryAttack()
    {
        if (!HasAuthority())
            return false;

        UpdateAttackComboState();

        float now = Time.time;
        if (IsKickActionActive(now) || IsNonCombatInventoryActionActive(now))
            return false;

        if (!HasActiveAttackCombo(now))
        {
            ResetAttackComboState();

            if (now < nextAttackAllowedTime)
                return false;

            return TryStartAttackComboStep(FirstAttackComboStep, consumeStamina: true);
        }

        if (attackComboStep >= MaxAttackComboStep || hasQueuedAttackComboStep || !IsAttackComboInputWindowOpen(now))
            return false;

        if (!TryConsumeAttackStamina())
            return false;

        int nextComboStep = Mathf.Clamp(attackComboStep + 1, FirstAttackComboStep, MaxAttackComboStep);
        if (now >= activeAttackEndTime)
        {
            return TryStartAttackComboStep(nextComboStep, consumeStamina: false);
        }

        QueueAttackComboStep(nextComboStep);
        return true;
    }

    private void UpdateAttackComboState()
    {
        if (!HasAuthority() || attackComboStep <= 0)
            return;

        float now = Time.time;
        if (hasQueuedAttackComboStep && now >= activeAttackEndTime)
        {
            int nextComboStep = queuedAttackComboStep;
            hasQueuedAttackComboStep = false;
            queuedAttackComboStep = 0;
            TryStartAttackComboStep(nextComboStep, consumeStamina: false);
            return;
        }

        if (!hasQueuedAttackComboStep && now > activeAttackInputWindowCloseTime)
            ResetAttackComboState();
    }

    private bool TryStartAttackComboStep(int comboStep, bool consumeStamina)
    {
        int safeComboStep = Mathf.Clamp(comboStep, FirstAttackComboStep, MaxAttackComboStep);
        if (consumeStamina && !TryConsumeAttackStamina())
            return false;

        float now = Time.time;
        float duration = GetAttackComboDuration(safeComboStep);
        float opensBeforeEnd = GetAttackComboInputWindowOpensBeforeEnd(safeComboStep);
        float closesAfterEnd = GetAttackComboInputWindowClosesAfterEnd(safeComboStep);

        attackComboStep = safeComboStep;
        hasQueuedAttackComboStep = false;
        queuedAttackComboStep = 0;
        activeAttackEndTime = now + duration;
        activeAttackInputWindowOpenTime = now + Mathf.Max(0f, duration - opensBeforeEnd);
        activeAttackInputWindowCloseTime = activeAttackEndTime + closesAfterEnd;

        if (safeComboStep == FirstAttackComboStep)
            nextAttackAllowedTime = now + GetAttackComboStartCooldown();

        RequireSprintRepressIfHeld();
        StopSprintForAttack();
        ApplyAttackMovementSlow();
        attackAnimationSequence++;
        return true;
    }

    private void QueueAttackComboStep(int comboStep)
    {
        queuedAttackComboStep = Mathf.Clamp(comboStep, FirstAttackComboStep, MaxAttackComboStep);
        hasQueuedAttackComboStep = true;
    }

    private bool HasActiveAttackCombo(float now)
    {
        return attackComboStep > 0 && now <= activeAttackInputWindowCloseTime;
    }

    private bool IsAttackSprintBlocked(float now)
    {
        return activeAttackEndTime > 0f && now <= activeAttackEndTime;
    }

    private void RequireSprintRepressIfHeld()
    {
        if (isSprintHeld)
            sprintRepressRequiredAfterAttack = true;
    }

    private void StopSprintForAttack()
    {
        if (CurrentState != MovementState.sprinting)
            return;

        SetMovementStateInternal(MovementState.walking);
    }

    private bool IsAttackComboInputWindowOpen(float now)
    {
        return now >= activeAttackInputWindowOpenTime && now <= activeAttackInputWindowCloseTime;
    }

    private void ResetAttackComboState()
    {
        attackComboStep = 0;
        queuedAttackComboStep = 0;
        hasQueuedAttackComboStep = false;
        activeAttackEndTime = 0f;
        activeAttackInputWindowOpenTime = 0f;
        activeAttackInputWindowCloseTime = 0f;
    }

    private float GetAttackComboStartCooldown()
    {
        return config != null ? Mathf.Max(0.05f, config.attackCooldown) : 1.1f;
    }

    private float GetAttackComboDuration(int comboStep)
    {
        AttackComboStepConfig stepConfig = GetAttackComboStepConfig(comboStep);
        return stepConfig != null ? Mathf.Max(0.05f, stepConfig.animationDuration) : 1f;
    }

    private float GetAttackComboInputWindowOpensBeforeEnd(int comboStep)
    {
        AttackComboStepConfig stepConfig = GetAttackComboStepConfig(comboStep);
        return stepConfig != null ? Mathf.Max(0f, stepConfig.inputWindowOpensBeforeEnd) : 0.25f;
    }

    private float GetAttackComboInputWindowClosesAfterEnd(int comboStep)
    {
        AttackComboStepConfig stepConfig = GetAttackComboStepConfig(comboStep);
        return stepConfig != null ? Mathf.Max(0f, stepConfig.inputWindowClosesAfterEnd) : 0.2f;
    }

    private AttackComboStepConfig GetAttackComboStepConfig(int comboStep)
    {
        return config != null ? config.GetAttackComboStep(comboStep) : null;
    }

    private bool IsKickActionActive(float now)
    {
        return activeKickEndTime > 0f && now <= activeKickEndTime;
    }

    private bool IsPickupActionActive(float now)
    {
        return activePickupActionEndTime > 0f && now <= activePickupActionEndTime;
    }

    private bool IsDrawActionActive(float now)
    {
        return activeDrawActionEndTime > 0f && now <= activeDrawActionEndTime;
    }

    private bool IsInventoryItemActionActive(float now)
    {
        return activeInventoryItemActionEndTime > 0f && now <= activeInventoryItemActionEndTime;
    }

    private bool IsEmoteActionActive(float now)
    {
        return activeEmoteActionEndTime > 0f && now <= activeEmoteActionEndTime;
    }

    private bool IsNonCombatInventoryActionActive(float now)
    {
        return IsPickupActionActive(now)
            || IsDrawActionActive(now)
            || IsInventoryItemActionActive(now)
            || IsEmoteActionActive(now)
            || emoteWheelSelectionActive;
    }

    private bool IsInventorySlotChangeLockedAt(float now)
    {
        return HasActiveAttackCombo(now)
            || IsKickActionActive(now)
            || IsNonCombatInventoryActionActive(now);
    }

    private float GetKickActionDuration()
    {
        return config != null ? Mathf.Max(0.05f, config.kickActionDuration) : 0.75f;
    }

    private float GetPickupInventoryLockDuration()
    {
        return config != null ? Mathf.Max(0f, config.pickupInventoryLockDuration) : 0.65f;
    }

    private float GetDrawInventoryLockDuration()
    {
        return config != null ? Mathf.Max(0f, config.drawInventoryLockDuration) : 0.55f;
    }

    private float GetItemUseInventoryLockDuration()
    {
        return config != null ? Mathf.Max(0f, config.itemUseInventoryLockDuration) : 0.45f;
    }

    private float GetEmoteInventoryLockDuration()
    {
        return config != null ? Mathf.Max(0f, config.emoteInventoryLockDuration) : 0.8f;
    }

    public void Kick()
    {
        if (!HasAuthority())
            return;

        UpdateAttackComboState();

        float now = Time.time;
        if (HasActiveAttackCombo(now) || IsNonCombatInventoryActionActive(now))
            return;

        float cooldown = config != null
            ? Mathf.Max(0.05f, config.kickCooldown)
            : 0.9f;

        if (now < nextKickAllowedTime)
            return;

        if (!TryConsumeKickStamina())
            return;

        nextKickAllowedTime = now + cooldown;
        activeKickEndTime = now + GetKickActionDuration();
        ApplyKickMovementSlow();
        kickAnimationSequence++;
    }

    public bool CanChangeInventorySlots()
    {
        return !IsInventorySlotChangeLocked;
    }

    public bool CanBeginInventoryItemAction()
    {
        if (!HasAuthority())
            return false;

        UpdateAttackComboState();
        return !IsInventorySlotChangeLockedAt(Time.time);
    }

    public void SetEmoteWheelSelectionActive(bool active)
    {
        if (!HasAuthority())
            return;

        emoteWheelSelectionActive = active;
    }

    public void BeginInventoryItemActionLock()
    {
        if (!HasAuthority())
            return;

        BeginActionLock(ref activeInventoryItemActionEndTime, GetItemUseInventoryLockDuration());
    }

    public bool TriggerPickupAnimation(HandType hand)
    {
        if (!HasAuthority())
            return false;

        UpdateAttackComboState();
        float now = Time.time;
        if (IsInventorySlotChangeLockedAt(now))
            return false;

        pickupAnimationHand = hand;
        pickupAnimationSequence++;
        BeginActionLock(ref activePickupActionEndTime, GetPickupInventoryLockDuration());
        return true;
    }

    public bool TriggerDrawAnimation(HandType hand)
    {
        if (!HasAuthority())
            return false;

        UpdateAttackComboState();

        float now = Time.time;
        if (IsInventorySlotChangeLockedAt(now))
            return false;

        drawAnimationHand = hand;
        drawAnimationSequence++;
        BeginActionLock(ref activeDrawActionEndTime, GetDrawInventoryLockDuration());
        return true;
    }

    public void TriggerDamageAnimation(PlayerDamageAnimationType type)
    {
        if (!HasAuthority() || type == PlayerDamageAnimationType.None)
            return;

        currentDamageAnimationType = type;
        damageAnimationSequence++;
    }

    public bool TriggerEmote(PlayerEmoteType type)
    {
        if (!HasAuthority() || type == PlayerEmoteType.None)
            return false;

        UpdateAttackComboState();

        float now = Time.time;
        if (IsInventorySlotChangeLockedAt(now))
            return false;

        currentEmoteType = type;
        emoteAnimationSequence++;
        BeginActionLock(ref activeEmoteActionEndTime, GetEmoteInventoryLockDuration());
        return true;
    }

    public void TriggerThumbsUpEmote()
    {
        TriggerEmote(PlayerEmoteType.ThumbsUp);
    }

    private static void BeginActionLock(ref float actionEndTime, float duration)
    {
        if (duration <= 0f)
            return;

        actionEndTime = Mathf.Max(actionEndTime, Time.time + duration);
    }

    public void Jump()
    {
        RefreshSurfaceState();

        if (Time.time < nextJumpInputAllowedTime || !IsGrounded || jumpQueued || exitingSlope)
            return;

        if (!TryConsumeJumpStamina())
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

    public void SetSprintHeld(bool isHeld)
    {
        bool releasedSprintWhileLocked = sprintRecoveryLocked && isSprintHeld && !isHeld;

        isSprintHeld = isHeld;

        if (!isSprintHeld)
            sprintRepressRequiredAfterAttack = false;

        if (releasedSprintWhileLocked)
        {
            sprintRecoveryPrimed = true;
            TryUnlockSprintRecovery();
        }
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

    public void RefreshGrounding()
    {
        if (!HasAuthority() || config == null)
            return;

        RefreshSurfaceState();
    }

    public void SetHandAnimationState(bool rightOccupied, bool leftOccupied, bool leftTorchActive = false)
    {
        if (!HasAuthority())
            return;

        rightHandOccupied = rightOccupied;
        leftHandOccupied = leftOccupied;
        leftHandTorchEquipped = leftOccupied && leftTorchActive;
    }

    private void InitializeStaminaState()
    {
        currentStamina = GetConfiguredMaxStamina();
        lastStaminaUseTime = float.NegativeInfinity;
        isSprintHeld = false;
        isConsumingStamina = false;
        sprintRecoveryLocked = false;
        sprintRecoveryPrimed = false;
        sprintRepressRequiredAfterAttack = false;
    }

    private void UpdateStamina(float deltaTime)
    {
        if (!HasAuthority() || config == null || deltaTime <= 0f)
            return;

        if (ConsumeSprintStamina(deltaTime))
            return;

        isConsumingStamina = false;
        RegenerateStamina(deltaTime);
    }

    private bool ConsumeSprintStamina(float deltaTime)
    {
        if (!ShouldDrainSprintStamina())
            return false;

        float sprintCostPerSecond = Mathf.Max(0f, config.sprintStaminaCostPerSecond);
        if (sprintCostPerSecond <= 0f)
            return false;

        float requestedDrain = sprintCostPerSecond * deltaTime;
        float consumed = ConsumeAvailableStamina(requestedDrain);
        if (consumed <= StaminaEpsilon)
        {
            LockSprintRecovery();
            SetMovementStateInternal(MovementState.walking);
            return false;
        }

        isConsumingStamina = true;
        if (currentStamina <= StaminaEpsilon)
        {
            LockSprintRecovery();
            SetMovementStateInternal(MovementState.walking);
        }

        return true;
    }

    private bool ShouldDrainSprintStamina()
    {
        return CurrentState == MovementState.sprinting
            && IsGrounded
            && requestedInput.y > InputDeadzone
            && PlanarSpeed > 0.1f;
    }

    private void RegenerateStamina(float deltaTime)
    {
        float maxStamina = GetConfiguredMaxStamina();
        if (currentStamina >= maxStamina - StaminaEpsilon)
        {
            currentStamina = maxStamina;
            TryUnlockSprintRecovery();
            return;
        }

        if (sprintRecoveryLocked && !sprintRecoveryPrimed)
            return;

        if (Time.time < lastStaminaUseTime + Mathf.Max(0f, config.staminaRegenDelay))
            return;

        float staminaRegenPerSecond = Mathf.Max(0f, config.staminaRegenPerSecond);
        if (staminaRegenPerSecond <= 0f)
            return;

        currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenPerSecond * deltaTime);
        TryUnlockSprintRecovery();
    }

    private bool TryConsumeAttackStamina()
    {
        return TryConsumeStamina(config != null ? config.attackStaminaCost : 0f);
    }

    private bool TryConsumeKickStamina()
    {
        return TryConsumeStamina(config != null ? config.kickStaminaCost : 0f);
    }

    private void ApplyAttackMovementSlow()
    {
        if (config == null)
            return;

        float slowDuration = Mathf.Max(0f, config.attackMovementSlowDuration);
        if (slowDuration <= 0f)
            return;

        attackMovementSlowUntil = Mathf.Max(attackMovementSlowUntil, Time.time + slowDuration);
    }

    private void ApplyKickMovementSlow()
    {
        if (config == null)
            return;

        float slowDuration = Mathf.Max(0f, config.kickMovementSlowDuration);
        if (slowDuration <= 0f)
            return;

        kickMovementSlowUntil = Mathf.Max(kickMovementSlowUntil, Time.time + slowDuration);
    }

    private bool TryConsumeJumpStamina()
    {
        return TryConsumeStamina(config != null ? config.jumpStaminaCost : 0f);
    }

    private bool TryConsumeStamina(float amount)
    {
        float staminaCost = Mathf.Max(0f, amount);
        if (staminaCost <= StaminaEpsilon)
            return true;

        if (currentStamina + StaminaEpsilon < staminaCost)
            return false;

        currentStamina = Mathf.Max(0f, currentStamina - staminaCost);
        MarkStaminaConsumed();
        return true;
    }

    private float ConsumeAvailableStamina(float amount)
    {
        float requestedAmount = Mathf.Max(0f, amount);
        if (requestedAmount <= 0f || currentStamina <= 0f)
            return 0f;

        float consumedAmount = Mathf.Min(currentStamina, requestedAmount);
        currentStamina = Mathf.Max(0f, currentStamina - consumedAmount);

        if (consumedAmount > StaminaEpsilon)
            MarkStaminaConsumed();

        return consumedAmount;
    }

    private void MarkStaminaConsumed()
    {
        lastStaminaUseTime = Time.time;
    }

    private bool HasAvailableStaminaForSprint()
    {
        if (sprintRecoveryLocked)
            return false;

        return config == null
            || Mathf.Max(0f, config.sprintStaminaCostPerSecond) <= 0f
            || currentStamina > StaminaEpsilon;
    }

    private float GetConfiguredMaxStamina()
    {
        return config != null ? Mathf.Max(1f, config.maxStamina) : 100f;
    }

    private void LockSprintRecovery()
    {
        sprintRecoveryLocked = true;
        sprintRecoveryPrimed = !isSprintHeld;
    }

    private void TryUnlockSprintRecovery()
    {
        if (!sprintRecoveryLocked || !sprintRecoveryPrimed)
            return;

        if (currentStamina + StaminaEpsilon < GetSprintRecoveryUnlockStamina())
            return;

        sprintRecoveryLocked = false;
        sprintRecoveryPrimed = false;
    }

    private float GetSprintRecoveryUnlockStamina()
    {
        if (config == null)
            return 0f;

        return Mathf.Clamp(config.sprintRecoveryUnlockStamina, 0f, GetConfiguredMaxStamina());
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

    private void ClearMovementInput()
    {
        requestedInput = Vector2.zero;
        rawInput = Vector2.zero;
        desiredMoveDirection = Vector3.zero;
        currentAcceleration = 0f;
        ResetDirectionChangeInertia();
        ResetSprintStrafeBiasState();
        UpdateEffectiveInput();
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

    public void SetLookPoseState(float yawOffset, float pitch)
    {
        if (!HasAuthority())
            return;

        lookYawOffset = Mathf.DeltaAngle(0f, yawOffset);
        lookPitch = Mathf.Clamp(pitch, -89f, 89f);
    }

    private void CacheLookReferences()
    {
        if (orientation == null)
        {
            Transform orientationTransform = transform.Find(OrientationObjectName);
            if (orientationTransform != null)
                orientation = orientationTransform;
        }

        if (!IsUsableLookMouse(mouseLook))
            mouseLook = ResolveLookMouse();

        Animator resolvedAnimator = modelAnimator;
        if (!IsUsableLookAnimator(resolvedAnimator))
            resolvedAnimator = ResolveLookAnimator();

        if (modelAnimator != resolvedAnimator)
        {
            modelAnimator = resolvedAnimator;
            spineBone = null;
            chestBone = null;
            headBone = null;
            upperBodyAttackLayerIndex = -1;
            thumbsUpLayerIndex = -1;
        }

        if (modelAnimator == null)
            return;

        if (spineBone == null)
            spineBone = ResolveLookBone(spineBonePath, "Spine");

        if (chestBone == null)
            chestBone = ResolveLookBone(chestBonePath, "Chest");

        if (headBone == null)
            headBone = ResolveLookBone(headBonePath, "Head");

        if (upperBodyAttackLayerIndex < 0)
            upperBodyAttackLayerIndex = modelAnimator.GetLayerIndex(UpperBodyAttackLayerName);

        if (thumbsUpLayerIndex < 0)
            thumbsUpLayerIndex = modelAnimator.GetLayerIndex(ThumbsUpLayerName);
    }

    private MouseLook ResolveLookMouse()
    {
        MouseLook[] mouseLooks = GetComponentsInChildren<MouseLook>(true);
        MouseLook activeNamedMouseLook = null;
        MouseLook namedMouseLook = null;
        MouseLook activeMouseLook = null;

        for (int i = 0; i < mouseLooks.Length; i++)
        {
            MouseLook candidate = mouseLooks[i];
            if (candidate == null)
                continue;

            bool isNamedFirstPersonCamera = string.Equals(candidate.gameObject.name, FirstPersonCameraObjectName, System.StringComparison.Ordinal);
            bool isActive = candidate.isActiveAndEnabled && candidate.GetComponent<Camera>() != null;

            if (isNamedFirstPersonCamera && isActive)
                return candidate;

            if (isNamedFirstPersonCamera && namedMouseLook == null)
                namedMouseLook = candidate;

            if (isActive && activeMouseLook == null)
                activeMouseLook = candidate;

            if (isNamedFirstPersonCamera && activeNamedMouseLook == null)
                activeNamedMouseLook = candidate;
        }

        return activeNamedMouseLook != null ? activeNamedMouseLook : namedMouseLook != null ? namedMouseLook : activeMouseLook;
    }

    private bool IsUsableLookMouse(MouseLook candidate)
    {
        if (candidate == null)
            return false;

        if (!candidate.isActiveAndEnabled)
            return false;

        return string.Equals(candidate.gameObject.name, FirstPersonCameraObjectName, System.StringComparison.Ordinal)
            || candidate.GetComponent<Camera>() != null;
    }

    private Animator ResolveLookAnimator()
    {
        Transform modelRoot = FindDescendantByName(transform, ModelObjectName);
        if (modelRoot != null)
        {
            Animator modelRootAnimator = modelRoot.GetComponent<Animator>();
            if (IsUsableLookAnimator(modelRootAnimator))
                return modelRootAnimator;

            Animator modelChildAnimator = modelRoot.GetComponentInChildren<Animator>(true);
            if (IsUsableLookAnimator(modelChildAnimator))
                return modelChildAnimator;
        }

        Animator[] animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator candidate = animators[i];
            if (IsUsableLookAnimator(candidate))
                return candidate;
        }

        return null;
    }

    private bool IsUsableLookAnimator(Animator candidate)
    {
        if (candidate == null)
            return false;

        if (IsUnderNamedAncestor(candidate.transform, FirstPersonModelObjectName))
            return false;

        if (CanResolveLookPath(candidate, spineBonePath)
            || CanResolveLookPath(candidate, chestBonePath)
            || CanResolveLookPath(candidate, headBonePath))
        {
            return true;
        }

        return FindDescendantByName(candidate.transform, "Spine") != null
            || FindDescendantByName(candidate.transform, "Chest") != null
            || FindDescendantByName(candidate.transform, "Head") != null;
    }

    private static bool CanResolveLookPath(Animator candidate, string bonePath)
    {
        return candidate != null
            && !string.IsNullOrWhiteSpace(bonePath)
            && candidate.transform.Find(bonePath) != null;
    }

    private static bool IsUnderNamedAncestor(Transform candidate, string ancestorName)
    {
        if (candidate == null || string.IsNullOrWhiteSpace(ancestorName))
            return false;

        Transform current = candidate;
        while (current != null)
        {
            if (string.Equals(current.name, ancestorName, System.StringComparison.Ordinal))
                return true;

            current = current.parent;
        }

        return false;
    }

    private Transform ResolveLookBone(string preferredPath, string fallbackName)
    {
        if (modelAnimator == null)
            return null;

        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            Transform preferred = modelAnimator.transform.Find(preferredPath);
            if (preferred != null)
                return preferred;
        }

        return FindDescendantByName(modelAnimator.transform, fallbackName);
    }

    private void ApplyCharacterColliderTuning()
    {
        if (capsule == null)
            return;

        PhysicsMaterial slideMaterial = GetOrCreateSlideColliderMaterial();
        if (capsule.sharedMaterial == slideMaterial)
            return;

        capsule.sharedMaterial = slideMaterial;
    }

    private static PhysicsMaterial GetOrCreateSlideColliderMaterial()
    {
        if (runtimeSlideColliderMaterial != null)
            return runtimeSlideColliderMaterial;

        runtimeSlideColliderMaterial = new PhysicsMaterial("PlayerLowFriction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        runtimeSlideColliderMaterial.hideFlags = HideFlags.HideAndDontSave;
        return runtimeSlideColliderMaterial;
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        if (string.Equals(root.name, targetName, System.StringComparison.Ordinal))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform match = FindDescendantByName(root.GetChild(i), targetName);
            if (match != null)
                return match;
        }

        return null;
    }

    private void UpdateLookPose()
    {
        CacheLookReferences();

        if (HasAuthority())
            UpdateLocalLookPose();
        else
            UpdateRemoteLookPose();
    }

    private void UpdateLocalLookPose()
    {
        if (mouseLook == null)
            return;

        float aimYaw = mouseLook.ViewYaw;
        float aimPitch = mouseLook.ViewPitch;
        bool allowPose = ShouldApplyHeadLookPose();
        bool allowAttackAimPose = ShouldApplyAttackAimPose();
        UpdateAttackAimPose(allowAttackAimPose, aimPitch);

        if (allowPose && !wasHeadLookPoseAllowedLastFrame)
            isHeadLookPoseAcquiring = true;
        else if (!allowPose)
            isHeadLookPoseAcquiring = false;

        float bodyYaw;
        float yawOffset;
        float pitch;

        if (allowPose)
        {
            bodyYaw = transform.eulerAngles.y;
            float safeYawLimit = Mathf.Max(0f, horizontalHeadLookLimit);
            float targetYawOffset = Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, aimYaw), -safeYawLimit, safeYawLimit);
            float targetPitch = ResolveHeadLookPitchTargetForCurrentAttackAim(aimPitch);

            if (isHeadLookPoseAcquiring)
            {
                SmoothAppliedLookPose(targetYawOffset, targetPitch, headLookAcquireSmoothTime);

                if (IsLookPoseNearTarget(targetYawOffset, targetPitch))
                {
                    SetAppliedLookPoseImmediate(targetYawOffset, targetPitch);
                    isHeadLookPoseAcquiring = false;
                }
            }
            else
            {
                SetAppliedLookPoseImmediate(targetYawOffset, targetPitch);
            }

            if (HasVisibleAttackAimPose(appliedAttackAimPitch))
                SetAppliedLookPitchImmediate(targetPitch);

            yawOffset = appliedLookYawOffset;
            pitch = appliedLookPitch;
            bodyYaw = aimYaw - yawOffset;
        }
        else
        {
            ReleaseAppliedLookPose();
            yawOffset = appliedLookYawOffset;
            pitch = appliedLookPitch;
            bodyYaw = aimYaw - yawOffset;
        }

        transform.rotation = Quaternion.Euler(0f, bodyYaw, 0f);

        ApplyOrientationLook(yawOffset);
        mouseLook.ApplyDrivenLookRotation(yawOffset);
        SetLookPoseState(yawOffset, aimPitch);
        wasHeadLookPoseAllowedLastFrame = allowPose;

        if (ShouldApplyHeadLookPoseToBones(allowPose, yawOffset, pitch))
            ApplyHeadLookPose(yawOffset, pitch);

        if (HasVisibleAttackAimPose(appliedAttackAimPitch))
            ApplyAttackAimPose(appliedAttackAimPitch);
    }

    private void UpdateRemoteLookPose()
    {
        if (!enableHeadLook)
            return;

        if (headBone == null && chestBone == null && spineBone == null)
            return;

        float replicatedYawOffset = networkLookYawOffset;
        float replicatedPitch = networkLookPitch;
        bool allowPose = ShouldApplyHeadLookPose();
        bool allowAttackAimPose = ShouldApplyAttackAimPose();
        UpdateReplicatedAttackAimPose(allowAttackAimPose, replicatedPitch);

        if (allowPose)
        {
            float targetPitch = ResolveHeadLookPitchTargetForCurrentAttackAim(replicatedPitch);
            float smoothTime = wasHeadLookPoseAllowedLastFrame ? 0f : headLookAcquireSmoothTime;
            SmoothAppliedLookPose(replicatedYawOffset, targetPitch, smoothTime);

            if (HasVisibleAttackAimPose(appliedAttackAimPitch))
                SetAppliedLookPitchImmediate(targetPitch);
        }
        else
        {
            ReleaseAppliedLookPose();
        }

        wasHeadLookPoseAllowedLastFrame = allowPose;

        if (ShouldApplyHeadLookPoseToBones(allowPose, appliedLookYawOffset, appliedLookPitch))
            ApplyHeadLookPose(appliedLookYawOffset, appliedLookPitch);

        if (HasVisibleAttackAimPose(appliedAttackAimPitch))
            ApplyAttackAimPose(appliedAttackAimPitch);
    }

    private void ApplyOrientationLook(float yawOffset)
    {
        if (orientation == null)
            return;

        orientation.localRotation = Quaternion.Euler(0f, yawOffset, 0f);
    }

    private bool ShouldApplyHeadLookPose()
    {
        if (!enableHeadLook)
            return false;

        if (headBone == null && chestBone == null && spineBone == null)
            return false;

        switch (CurrentState)
        {
            case MovementState.idle:
                break;
            case MovementState.crouching:
                if (!allowHeadLookWhileCrouching)
                    return false;
                break;
            case MovementState.walking:
                if (!allowHeadLookWhileWalking)
                    return false;
                break;
            default:
                return false;
        }

        if (disableHeadLookDuringAttackLayer && IsActionLayerActive(upperBodyAttackLayerIndex))
            return false;

        if (disableHeadLookDuringThumbsUpLayer && IsActionLayerActive(thumbsUpLayerIndex))
            return false;

        if (IsBaseLayerStateActive(ReactionDamageStateName))
            return false;

        return true;
    }

    private bool ShouldApplyHeadLookPoseToBones(bool allowPose, float yawOffset, float pitch)
    {
        if (!HasVisibleLookPose(yawOffset, pitch))
            return false;

        if (allowPose)
            return true;

        return ShouldApplyHeadLookReleasePose();
    }

    private bool ShouldApplyHeadLookReleasePose()
    {
        if (!enableHeadLook)
            return false;

        if (headBone == null && chestBone == null && spineBone == null)
            return false;

        if (disableHeadLookDuringAttackLayer && IsActionLayerActive(upperBodyAttackLayerIndex))
            return false;

        if (disableHeadLookDuringThumbsUpLayer && IsActionLayerActive(thumbsUpLayerIndex))
            return false;

        if (IsBaseLayerStateActive(ReactionDamageStateName))
            return false;

        return true;
    }

    private bool ShouldApplyAttackAimPose()
    {
        if (!enableHeadLook || !enableAttackAimPose)
            return false;

        if (spineBone == null && chestBone == null && headBone == null)
            return false;

        if (!IsActionLayerActive(upperBodyAttackLayerIndex))
            return false;

        if (IsBaseLayerStateActive(ReactionDamageStateName))
            return false;

        return true;
    }

    private void UpdateAttackAimPose(bool allowPose, float targetPitch)
    {
        float safeWeight = Mathf.Clamp01(attackAimPoseWeight);
        float safeTargetPitch = allowPose
            ? Mathf.Clamp(targetPitch, -attackAimPitchLimit, attackAimPitchLimit) * safeWeight
            : 0f;

        float smoothTime = allowPose ? attackAimAcquireSmoothTime : attackAimReleaseSmoothTime;
        SmoothAppliedAttackAimPose(safeTargetPitch, smoothTime);
    }

    private void UpdateReplicatedAttackAimPose(bool allowPose, float replicatedPitch)
    {
        float safeWeight = Mathf.Clamp01(attackAimPoseWeight);
        float safeTargetPitch = allowPose
            ? Mathf.Clamp(replicatedPitch, -attackAimPitchLimit, attackAimPitchLimit) * safeWeight
            : 0f;

        float smoothTime = allowPose ? attackAimAcquireSmoothTime : attackAimReleaseSmoothTime;
        SmoothAppliedAttackAimPose(safeTargetPitch, smoothTime);
    }

    private float ResolveHeadLookPitchTargetForCurrentAttackAim(float targetPitch)
    {
        float visibleTargetPitch = ClampHeadLookPitchToVisibleRange(targetPitch);
        if (!HasVisibleAttackAimPose(appliedAttackAimPitch))
            return visibleTargetPitch;

        return ClampHeadLookPitchToVisibleRange(visibleTargetPitch - appliedAttackAimPitch);
    }

    private float ClampHeadLookPitchToVisibleRange(float pitch)
    {
        float visiblePitchLimit = Mathf.Max(0f, headPitchLimit)
            + Mathf.Max(0f, chestPitchLimit)
            + Mathf.Max(0f, spinePitchLimit);

        return visiblePitchLimit > 0.001f
            ? Mathf.Clamp(pitch, -visiblePitchLimit, visiblePitchLimit)
            : 0f;
    }

    private void SetAppliedAttackAimPoseImmediate(float pitch)
    {
        appliedAttackAimPitch = Mathf.Clamp(pitch, -attackAimPitchLimit, attackAimPitchLimit);
        appliedAttackAimPitchVelocity = 0f;
    }

    private void SmoothAppliedAttackAimPose(float targetPitch, float smoothTime)
    {
        float safeTargetPitch = Mathf.Clamp(targetPitch, -attackAimPitchLimit, attackAimPitchLimit);
        float safeSmoothTime = Mathf.Max(0f, smoothTime);
        if (safeSmoothTime <= 0.0001f)
        {
            SetAppliedAttackAimPoseImmediate(safeTargetPitch);
            return;
        }

        float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
        appliedAttackAimPitch = Mathf.SmoothDamp(
            appliedAttackAimPitch,
            safeTargetPitch,
            ref appliedAttackAimPitchVelocity,
            safeSmoothTime,
            Mathf.Infinity,
            deltaTime);

        if (Mathf.Abs(appliedAttackAimPitch - safeTargetPitch) <= 0.01f)
            SetAppliedAttackAimPoseImmediate(safeTargetPitch);
    }

    private bool IsActionLayerActive(int layerIndex)
    {
        if (modelAnimator == null || layerIndex < 0 || layerIndex >= modelAnimator.layerCount)
            return false;

        if (modelAnimator.GetLayerWeight(layerIndex) <= 0.001f)
            return false;

        AnimatorStateInfo currentState = modelAnimator.GetCurrentAnimatorStateInfo(layerIndex);
        if (IsNonEmptyLayerState(currentState))
            return true;

        return modelAnimator.IsInTransition(layerIndex)
            && IsNonEmptyLayerState(modelAnimator.GetNextAnimatorStateInfo(layerIndex));
    }

    private static bool IsNonEmptyLayerState(AnimatorStateInfo stateInfo)
    {
        return stateInfo.shortNameHash != 0
            && stateInfo.shortNameHash != Animator.StringToHash("Empty");
    }

    private bool IsBaseLayerStateActive(string stateName)
    {
        if (modelAnimator == null || string.IsNullOrWhiteSpace(stateName))
            return false;

        int shortStateHash = Animator.StringToHash(stateName);
        int fullPathHash = Animator.StringToHash($"Base Layer.{stateName}");
        AnimatorStateInfo currentState = modelAnimator.GetCurrentAnimatorStateInfo(0);
        if (MatchesState(currentState, shortStateHash, fullPathHash))
            return true;

        return modelAnimator.IsInTransition(0)
            && MatchesState(modelAnimator.GetNextAnimatorStateInfo(0), shortStateHash, fullPathHash);
    }

    private static bool MatchesState(AnimatorStateInfo stateInfo, int shortStateHash, int fullPathHash)
    {
        return stateInfo.shortNameHash == shortStateHash
            || stateInfo.fullPathHash == fullPathHash;
    }

    private void SetAppliedLookPoseImmediate(float yawOffset, float pitch)
    {
        appliedLookYawOffset = Mathf.DeltaAngle(0f, yawOffset);
        appliedLookPitch = Mathf.Clamp(pitch, -89f, 89f);
        appliedLookYawVelocity = 0f;
        appliedLookPitchVelocity = 0f;
    }

    private void SetAppliedLookPitchImmediate(float pitch)
    {
        appliedLookPitch = Mathf.Clamp(pitch, -89f, 89f);
        appliedLookPitchVelocity = 0f;
    }

    private void SmoothAppliedLookPose(float targetYawOffset, float targetPitch, float smoothTime)
    {
        float safeSmoothTime = Mathf.Max(0f, smoothTime);
        if (safeSmoothTime <= 0.0001f)
        {
            SetAppliedLookPoseImmediate(targetYawOffset, targetPitch);
            return;
        }

        float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
        float safeTargetYawOffset = Mathf.DeltaAngle(0f, targetYawOffset);
        float safeTargetPitch = Mathf.Clamp(targetPitch, -89f, 89f);

        appliedLookYawOffset = Mathf.SmoothDampAngle(
            appliedLookYawOffset,
            safeTargetYawOffset,
            ref appliedLookYawVelocity,
            safeSmoothTime,
            Mathf.Infinity,
            deltaTime);

        appliedLookPitch = Mathf.SmoothDamp(
            appliedLookPitch,
            safeTargetPitch,
            ref appliedLookPitchVelocity,
            safeSmoothTime,
            Mathf.Infinity,
            deltaTime);
    }

    private void ReleaseAppliedLookPose()
    {
        SmoothAppliedLookPose(0f, 0f, headLookReleaseSmoothTime);

        if (Mathf.Abs(appliedLookYawOffset) <= 0.01f)
        {
            appliedLookYawOffset = 0f;
            appliedLookYawVelocity = 0f;
        }

        if (Mathf.Abs(appliedLookPitch) <= 0.01f)
        {
            appliedLookPitch = 0f;
            appliedLookPitchVelocity = 0f;
        }
    }

    private static bool HasVisibleLookPose(float yawOffset, float pitch)
    {
        return Mathf.Abs(yawOffset) > 0.001f || Mathf.Abs(pitch) > 0.001f;
    }

    private static bool HasVisibleAttackAimPose(float pitch)
    {
        return Mathf.Abs(pitch) > 0.001f;
    }

    private bool IsLookPoseNearTarget(float targetYawOffset, float targetPitch)
    {
        return Mathf.Abs(Mathf.DeltaAngle(appliedLookYawOffset, targetYawOffset)) <= 0.1f
            && Mathf.Abs(appliedLookPitch - targetPitch) <= 0.1f;
    }

    private void ApplyHeadLookPose(float yawOffset, float pitch)
    {
        float poseYaw = invertPoseYaw ? -yawOffset : yawOffset;
        float remainingPitch = (invertPosePitch ? -pitch : pitch);

        float headPitch = ConsumePitchContribution(ref remainingPitch, headPitchLimit);
        float chestPitch = ConsumePitchContribution(ref remainingPitch, chestPitchLimit);
        float spinePitch = ConsumePitchContribution(ref remainingPitch, spinePitchLimit);

        if (spineBone != null && Mathf.Abs(spinePitch) > 0.001f)
            spineBone.localRotation = ApplyLocalAxisRotation(spineBone.localRotation, spinePitch, spinePitchAxis, Vector3.forward);

        if (chestBone != null && Mathf.Abs(chestPitch) > 0.001f)
            chestBone.localRotation = ApplyLocalAxisRotation(chestBone.localRotation, chestPitch, chestPitchAxis, Vector3.forward);

        if (headBone != null)
        {
            float clampedHeadYaw = Mathf.Clamp(poseYaw, -horizontalHeadLookLimit, horizontalHeadLookLimit);
            if (Mathf.Abs(headPitch) > 0.001f || Mathf.Abs(clampedHeadYaw) > 0.001f)
            {
                Quaternion headRotation = headBone.localRotation;
                headRotation = ApplyLocalAxisRotation(headRotation, headPitch, headPitchAxis, Vector3.right);
                headRotation = ApplyLocalAxisRotation(headRotation, clampedHeadYaw, headYawAxis, Vector3.up);
                headBone.localRotation = headRotation;
            }
        }
    }

    private void ApplyAttackAimPose(float pitch)
    {
        float remainingPitch = invertPosePitch ? -pitch : pitch;

        float spinePitch = ConsumePitchContribution(ref remainingPitch, attackAimSpinePitchLimit);
        float chestPitch = ConsumePitchContribution(ref remainingPitch, attackAimChestPitchLimit);
        float headPitch = ConsumePitchContribution(ref remainingPitch, attackAimHeadPitchLimit);

        if (spineBone != null && Mathf.Abs(spinePitch) > 0.001f)
            spineBone.localRotation = ApplyLocalAxisRotation(spineBone.localRotation, spinePitch, spinePitchAxis, Vector3.forward);

        if (chestBone != null && Mathf.Abs(chestPitch) > 0.001f)
            chestBone.localRotation = ApplyLocalAxisRotation(chestBone.localRotation, chestPitch, chestPitchAxis, Vector3.forward);

        if (headBone != null && Mathf.Abs(headPitch) > 0.001f)
            headBone.localRotation = ApplyLocalAxisRotation(headBone.localRotation, headPitch, headPitchAxis, Vector3.right);
    }

    private static float ConsumePitchContribution(ref float remainingPitch, float limit)
    {
        float safeLimit = Mathf.Max(0f, limit);
        if (safeLimit <= 0.001f || Mathf.Abs(remainingPitch) <= 0.001f)
            return 0f;

        float contribution = Mathf.Clamp(remainingPitch, -safeLimit, safeLimit);
        remainingPitch -= contribution;
        return contribution;
    }

    private static Quaternion ApplyLocalAxisRotation(Quaternion baseRotation, float angle, Vector3 localAxis, Vector3 fallbackAxis)
    {
        if (Mathf.Abs(angle) <= 0.001f)
            return baseRotation;

        Vector3 safeAxis = localAxis.sqrMagnitude > 0.0001f ? localAxis.normalized : fallbackAxis.normalized;
        return baseRotation * Quaternion.AngleAxis(angle, safeAxis);
    }
}
