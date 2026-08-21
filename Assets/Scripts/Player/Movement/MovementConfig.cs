using UnityEngine;

[System.Serializable]
public sealed class AttackComboStepConfig
{
    [Min(0.05f)] public float animationDuration = 1f;
    [Min(0f)] public float inputWindowOpensBeforeEnd = 0.25f;
    [Min(0f)] public float inputWindowClosesAfterEnd = 0.2f;

    public AttackComboStepConfig()
    {
    }

    public AttackComboStepConfig(float animationDuration, float inputWindowOpensBeforeEnd, float inputWindowClosesAfterEnd)
    {
        this.animationDuration = animationDuration;
        this.inputWindowOpensBeforeEnd = inputWindowOpensBeforeEnd;
        this.inputWindowClosesAfterEnd = inputWindowClosesAfterEnd;
    }
}

[CreateAssetMenu(fileName = "MovementConfig", menuName = "Player/MovementConfig")]
public class MovementConfig : ScriptableObject
{
    public const int AttackComboStepCount = 3;

    [Header("Movement Speeds")]
    public float walkSpeed = 3f;
    [Tooltip("Multiplier applied to walk speed while the movement input has a backward component (S, SA, or SD).")]
    [Range(0f, 1f)] public float walkBackSpeedMultiplier = 0.8f;
    public float sprintSpeed = 7f;

    [Header("Sprint Strafe Bias")]
    [Tooltip("Disable to remove the sprint strafe bias system and let sprint diagonals use their full intended direction.")]
    public bool enableSprintStrafeBias = true;
    [Range(0f, 1f)] public float sprintStrafeInfluence = 0.45f;
    [Min(0f)] public float sprintStrafeOpenTime = 0.45f;
    [Range(1f, 4f)] public float sprintStrafeCurveExponent = 1.6f;

    public float crouchSpeed = 1.5f;
    public float maxSlideSpeed = 7f;

    [Header("Acceleration / Deceleration")]
    public float groundAcceleration = 45f;
    public float airAcceleration = 18f;
    public float accelerationTime = 0.1f;
    public float decelerationTime = 0.15f;
    public float sprintReleaseDecelerationTime = 0.22f;

    [Header("Direction Change Inertia")]
    public float directionChangeMinPlanarSpeed = 0.9f;
    [Tooltip("Velocity below this value ends the hard brake phase and lets the new input direction take over.")]
    public float directionChangeBrakeExitPlanarSpeed = 0.35f;
    [Range(90f, 180f)] public float walkCameraTurnReversalAngle = 145f;
    [Range(90f, 180f)] public float sprintReversalAngle = 120f;
    [Range(-1f, 1f)] public float cameraTurnReversalInputAlignmentDot = 0.6f;
    [Range(-1f, 1f)] public float walkInputReversalDot = -0.35f;
    [Range(-1f, 1f)] public float sprintInputReversalDot = 0f;
    public float directionChangeInputMemoryDuration = 0.12f;
    [Range(0f, 1f)] public float walkReversalSpeedMultiplier = 0.22f;
    [Range(0f, 1f)] public float sprintReversalSpeedMultiplier = 0.05f;
    public float walkReversalHoldTime = 0.14f;
    public float sprintReversalHoldTime = 0.2f;
    public float walkReversalRecoveryTime = 0.3f;
    public float sprintReversalRecoveryTime = 0.4f;
    [Range(0.05f, 2f)] public float walkReversalAccelerationMultiplier = 0.45f;
    [Range(0.05f, 2f)] public float sprintReversalAccelerationMultiplier = 0.35f;

    [Header("Locomotion Animation")]
    [Tooltip("Planar speed below this value can fall back to input intent so locomotion starts again after a full stop.")]
    public float animationVelocityInputDeadzone = 0.08f;

    [Header("Jump")]
    public float jumpForce = 7.25f;
    public float jumpCooldown = 0.15f;
    public float jumpInputCooldown = 0.12f;
    public float jumpDelay = 0.08f;
    public float jumpGroundIgnoreTime = 0.12f;

    [Header("Combat")]
    [Tooltip("Minimum time before a fresh Attack_1 can start. Follow-up combo hits use their own window timings.")]
    [Min(0.05f)] public float attackCooldown = 1.1f;
    [Header("Attack Combo")]
    public AttackComboStepConfig attackComboStep1 = new AttackComboStepConfig(1f, 0.25f, 0.2f);
    public AttackComboStepConfig attackComboStep2 = new AttackComboStepConfig(1f, 0.25f, 0.2f);
    public AttackComboStepConfig attackComboStep3 = new AttackComboStepConfig(1f, 0.2f, 0.25f);
    [Range(0f, 1f)] public float attackMovementSpeedMultiplier = 0.55f;
    [Min(0f)] public float attackMovementSlowDuration = 0.3f;
    [Min(0.05f)] public float kickCooldown = 0.9f;
    [Tooltip("How long Kick is considered active for action-lock rules, independent from cooldown.")]
    [Min(0.05f)] public float kickActionDuration = 0.75f;
    [Range(0f, 1f)] public float kickMovementSpeedMultiplier = 0.45f;
    [Min(0f)] public float kickMovementSlowDuration = 0.25f;

    [Header("Inventory Action Locks")]
    [Tooltip("How long slot changes are blocked after picking up and equipping an item.")]
    [Min(0f)] public float pickupInventoryLockDuration = 0.65f;
    [Tooltip("How long slot changes are blocked after switching to an occupied slot and playing the draw animation.")]
    [Min(0f)] public float drawInventoryLockDuration = 0.55f;
    [Tooltip("How long slot changes are blocked after using a non-weapon item, such as a consumable.")]
    [Min(0f)] public float itemUseInventoryLockDuration = 0.45f;
    [Tooltip("How long slot changes are blocked after starting an emote.")]
    [Min(0f)] public float emoteInventoryLockDuration = 0.8f;

    [Header("Fall Impact")]
    public bool enableFallImpact = true;
    [Min(0f)] public float minFallHeightForDamage = 4f;
    [Min(0f)] public float fallDamagePerMeter = 12f;
    [Min(0f)] public float maxFallDamage = 90f;
    [Range(0f, 1f)] public float fallMovementSpeedMultiplier = 0.6f;
    [Min(0f)] public float fallMovementSlowDuration = 1.5f;

    [Header("Stamina")]
    [Min(1f)] public float maxStamina = 100f;
    [Min(0f)] public float sprintStaminaCostPerSecond = 15f;
    [Min(0f)] public float jumpStaminaCost = 20f;
    [Min(0f)] public float attackStaminaCost = 10f;
    [Min(0f)] public float kickStaminaCost = 12f;
    [Min(0f)] public float staminaRegenDelay = 0.5f;
    [Min(0f)] public float staminaRegenPerSecond = 25f;
    [Min(0f)] public float sprintRecoveryUnlockStamina = 20f;

    [Header("Airborne Animation")]
    public float fallingStartDelay = 0.08f;
    public float minAirTimeForLand = 0.1f;
    public float minAirHeightForLand = 0.08f;
    public float minDownwardSpeedForLand = 1.5f;
    public float groundedConfirmTimeForLand = 0.03f;

    [Header("Air Control")]
    [Range(0f, 1f)] public float airMultiplier = 0.4f;

    [Header("Crouch")]
    public float crouchYScale = 0.5f;

    [Header("Ground Detection")]
    public float playerHeight = 2f;
    public LayerMask groundLayer = Physics.DefaultRaycastLayers;
    public float groundProbeDistance = 0.18f;
    [Range(0.5f, 1f)] public float groundProbeRadiusScale = 0.9f;

    [Header("Slope Handling")]
    [Min(0f)] public float minSlopeAngleToAffect = 3f;
    [Range(0f, 89f)] public float maxSlopeAngle = 45f;
    [Range(0f, 89f)] public float slideSlopeAngle = 65f;
    public float groundSnapAcceleration = 6f;
    public float slideAcceleration = 9f;

    [Header("Wall / Step Detection")]
    public float wallCheckDistance = 0.2f;
    [Range(0.3f, 1f)] public float wallCheckRadiusScale = 0.75f;
    [Range(0.3f, 0.75f)] public float upperWallCheckHeightRatio = 0.58f;
    public float maxStepHeight = 0.28f;
    public float stepSearchDistance = 0.22f;
    public float stepLiftSpeed = 4.5f;

    [Header("Physics")]
    public float groundDrag = 4f;

    public AttackComboStepConfig GetAttackComboStep(int comboStep)
    {
        switch (comboStep)
        {
            case 1:
                return attackComboStep1;
            case 2:
                return attackComboStep2;
            case 3:
                return attackComboStep3;
            default:
                return attackComboStep1;
        }
    }
}
