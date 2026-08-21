using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public partial class MovementAnimationController : MonoBehaviour
{
    private const string DefaultMirrorResourcePath = "MovementAnimatorMirror";
    private const float AnimationParameterSnapEpsilon = 0.001f;

    [Header("References")]
    public Animator animator;
    public PlayerMovement playerMovement;
    [SerializeField] private MovementAnimatorMirror animatorMirror;

    [Header("Animation Parameters")]
    [Range(-2f, 2f)]
    public float horizontal;
    [Range(-2f, 2f)]
    public float vertical;

    [Header("Smoothing")]
    [Range(0.05f, 0.5f)]
    public float animationSmoothTime = 0.1f;

    [Header("Airborne Animation")]
    [SerializeField] private bool allowFallingWithoutJump = true;
    [SerializeField] private bool suppressAirborneAnimations;

    [Header("Crouch Transition Animation")]
    [SerializeField] private bool enableCrouchTransitionAnimations = true;
    [SerializeField] private string crouchEnterStateName = "Crouch";
    [SerializeField] private string crouchExitStateName = "Stand Up";

    [Header("Idle Turn In Place")]
    [SerializeField] private bool enableIdleTurnInPlace = true;
    [SerializeField] [Range(45f, 180f)] private float idleTurnTriggerAngle = 85f;
    [SerializeField] [Range(0.05f, 1f)] private float idleTurnCooldown = 0.35f;
    [SerializeField] [Range(0f, 0.2f)] private float idleTurnMovementThreshold = 0.05f;

    private readonly HashSet<string> parameterNames = new HashSet<string>();
    private bool wasGrounded;
    private bool hasInitializedAnimatorState;
    private bool hasAirbornePhase;
    private bool pendingJumpAirborneStart;
    private bool airborneJumpStarted;
    private bool hasIdleTurnYawSample;
    private bool wasCrouching;
    private int lastJumpAnimationSequence;
    private int lastLandingAnimationSequence;
    private float airborneTime;
    private float idleTurnAccumulatedYaw;
    private float idleTurnCooldownTimer;
    private float lastIdleTurnYaw;
    private float movementMagnitude;
    private float horizontalVelocity;
    private float verticalVelocity;
    private float movementMagnitudeVelocity;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

        if (animatorMirror == null)
            animatorMirror = Resources.Load<MovementAnimatorMirror>(DefaultMirrorResourcePath);

        CacheAnimatorParameters();
    }

    private void Update()
    {
        if (playerMovement == null || animator == null)
            return;

        bool isGrounded = playerMovement.IsGrounded;
        bool isJumpQueued = playerMovement.IsJumpQueued;
        bool isCrouching = playerMovement.CurrentState == MovementState.crouching;
        bool isSprinting = playerMovement.CurrentState == MovementState.sprinting;
        float verticalSpeed = playerMovement.VerticalVelocity;
        float movementScale = GetMovementParameterScale();

        if (!hasInitializedAnimatorState)
            InitializeRuntimeState(isGrounded, isJumpQueued);

        if (playerMovement.JumpAnimationSequence != lastJumpAnimationSequence)
        {
            PlayJumpAnimation();
            lastJumpAnimationSequence = playerMovement.JumpAnimationSequence;
            pendingJumpAirborneStart = true;
        }

        if (playerMovement.LandingAnimationSequence != lastLandingAnimationSequence)
        {
            PlayLandAnimation();
            lastLandingAnimationSequence = playerMovement.LandingAnimationSequence;
        }

        UpdateCrouchTransitionAnimationState(isGrounded, isJumpQueued, isCrouching);
        UpdateAirborneAnimationState(isGrounded, isJumpQueued);
        UpdateIdleTurnAnimationState(isGrounded, isJumpQueued);

        bool isJumping = playerMovement.CurrentState == MovementState.air || isJumpQueued;
        bool isFalling = ComputeIsFalling(isGrounded, isJumpQueued, verticalSpeed);
        UpdateLocomotionAnimationState(isGrounded, isJumpQueued, movementScale);

        SetBoolIfExists(MovementAnimatorSemantic.IsGrounded, "IsGrounded", isGrounded);
        SetBoolIfExists(MovementAnimatorSemantic.IsCrouching, "IsCrouching", isCrouching);
        SetBoolIfExists(MovementAnimatorSemantic.IsSprinting, "IsSprinting", isSprinting);
        SetBoolIfExists(MovementAnimatorSemantic.IsJumping, "IsJumping", isJumping);
        SetBoolIfExists(MovementAnimatorSemantic.IsFalling, "IsFalling", isFalling);

        SetFloatDirectIfExists(MovementAnimatorSemantic.Horizontal, "Horizontal", horizontal);
        SetFloatDirectIfExists(MovementAnimatorSemantic.Vertical, "Vertical", vertical);
        SetFloatDirectIfExists(MovementAnimatorSemantic.MovementMagnitude, "MovementMagnitude", movementMagnitude);
        SetBoolIfExists(MovementAnimatorSemantic.IsMoving, "IsMoving", movementMagnitude > 0.1f);

        SetFloatIfExists(MovementAnimatorSemantic.SpeedMultiplier, "SpeedMultiplier", movementScale);
        SetFloatIfExists(MovementAnimatorSemantic.VerticalSpeed, "VerticalSpeed", verticalSpeed);
    }

    private void InitializeRuntimeState(bool isGrounded, bool isJumpQueued)
    {
        lastJumpAnimationSequence = playerMovement.JumpAnimationSequence;
        lastLandingAnimationSequence = playerMovement.LandingAnimationSequence;
        hasInitializedAnimatorState = true;
        wasGrounded = isGrounded;
        ResetTriggerIfExists(MovementAnimatorSemantic.JumpTrigger, "Jump");
        ResetTriggerIfExists(MovementAnimatorSemantic.LandTrigger, "Land");
        ResetTriggerIfExists(MovementAnimatorSemantic.CrouchEnterTrigger, "CrouchEnter");
        ResetTriggerIfExists(MovementAnimatorSemantic.CrouchExitTrigger, "CrouchExit");
        ResetTriggerIfExists(MovementAnimatorSemantic.IdleTurnLeftTrigger, "IdleTurnLeft");
        ResetTriggerIfExists(MovementAnimatorSemantic.IdleTurnRightTrigger, "IdleTurnRight");
        lastIdleTurnYaw = transform.eulerAngles.y;
        hasIdleTurnYawSample = true;
        wasCrouching = playerMovement.CurrentState == MovementState.crouching;

        if (!isGrounded)
            BeginAirbornePhase(isJumpQueued);
    }

    public void PlayCrouchEnterAnimation()
    {
        if (!enableCrouchTransitionAnimations)
            return;

        ResetTriggerIfExists(MovementAnimatorSemantic.CrouchExitTrigger, "CrouchExit");

        if (!string.IsNullOrWhiteSpace(crouchEnterStateName) && TryPlayState(crouchEnterStateName, 0.08f))
            return;

        ResetTriggerIfExists(MovementAnimatorSemantic.CrouchEnterTrigger, "CrouchEnter");
        SetTriggerIfExists(MovementAnimatorSemantic.CrouchEnterTrigger, "CrouchEnter");
    }

    public void PlayCrouchExitAnimation()
    {
        if (!enableCrouchTransitionAnimations)
            return;

        ResetTriggerIfExists(MovementAnimatorSemantic.CrouchEnterTrigger, "CrouchEnter");

        if (!string.IsNullOrWhiteSpace(crouchExitStateName) && TryPlayState(crouchExitStateName, 0.08f))
            return;

        ResetTriggerIfExists(MovementAnimatorSemantic.CrouchExitTrigger, "CrouchExit");
        SetTriggerIfExists(MovementAnimatorSemantic.CrouchExitTrigger, "CrouchExit");
    }

    public void PlayJumpAnimation()
    {
        ResetTriggerIfExists(MovementAnimatorSemantic.LandTrigger, "Land");

        if (TryPlayState("Jump", 0.12f))
            return;

        ResetTriggerIfExists(MovementAnimatorSemantic.JumpTrigger, "Jump");
        SetTriggerIfExists(MovementAnimatorSemantic.JumpTrigger, "Jump");
    }

    public void PlayLandAnimation()
    {
        ResetTriggerIfExists(MovementAnimatorSemantic.JumpTrigger, "Jump");

        if (TryPlayState("Landing", 0.05f))
            return;

        ResetTriggerIfExists(MovementAnimatorSemantic.LandTrigger, "Land");
        SetTriggerIfExists(MovementAnimatorSemantic.LandTrigger, "Land");
    }

    public void PlayIdleTurnLeftAnimation()
    {
        PlayIdleTurnAnimation("IdleTurnLeft", MovementAnimatorSemantic.IdleTurnLeftTrigger);
    }

    public void PlayIdleTurnRightAnimation()
    {
        PlayIdleTurnAnimation("IdleTurnRight", MovementAnimatorSemantic.IdleTurnRightTrigger);
    }

    public void SetAllowFallingWithoutJump(bool allow)
    {
        allowFallingWithoutJump = allow;
    }

    public void SetAirborneAnimationSuppressed(bool suppress)
    {
        suppressAirborneAnimations = suppress;

        if (suppress)
            SetBoolIfExists(MovementAnimatorSemantic.IsFalling, "IsFalling", false);
    }
}
