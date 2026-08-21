using UnityEngine;

[CreateAssetMenu(fileName = "MovementConfig", menuName = "Player/MovementConfig")]
public class MovementConfig : ScriptableObject
{
    [Header("Movement Speeds")]
    public float walkSpeed = 3f;
    public float sprintSpeed = 7f;
    public float crouchSpeed = 1.5f;
    public float maxSlideSpeed = 7f;

    [Header("Acceleration / Deceleration")]
    public float groundAcceleration = 45f;
    public float airAcceleration = 18f;
    public float accelerationTime = 0.1f;
    public float decelerationTime = 0.15f;

    [Header("Direction Change Inertia")]
    public float directionChangeMinPlanarSpeed = 0.9f;
    [Range(90f, 180f)] public float walkCameraTurnReversalAngle = 145f;
    [Range(90f, 180f)] public float sprintReversalAngle = 120f;
    [Range(-1f, 1f)] public float cameraTurnReversalInputAlignmentDot = 0.6f;
    [Range(-1f, 1f)] public float sprintInputReversalDot = 0f;
    public float directionChangeInputMemoryDuration = 0.12f;
    [Range(0f, 1f)] public float walkReversalSpeedMultiplier = 0.22f;
    [Range(0f, 1f)] public float sprintReversalSpeedMultiplier = 0.05f;
    public float walkReversalHoldTime = 0.14f;
    public float sprintReversalHoldTime = 0.2f;
    public float walkReversalRecoveryTime = 0.3f;
    public float sprintReversalRecoveryTime = 0.4f;
    [Min(1f)] public float walkReversalAccelerationMultiplier = 1.2f;
    [Min(1f)] public float sprintReversalAccelerationMultiplier = 1.9f;

    [Header("Jump")]
    public float jumpForce = 7.25f;
    public float jumpCooldown = 0.15f;
    public float jumpInputCooldown = 0.12f;
    public float jumpDelay = 0.08f;
    public float jumpGroundIgnoreTime = 0.12f;

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
}
