public enum MovementState
{
    idle = 0,
    walking = 1,
    sprinting = 2,
    crouching = 3,
    // Keep air at 4 because existing prefab FOV settings are serialized with this value.
    air = 4,
    jumping = 5
}
