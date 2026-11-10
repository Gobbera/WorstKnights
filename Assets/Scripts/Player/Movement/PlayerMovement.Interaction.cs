using UnityEngine;

public partial class PlayerMovement
{
    internal void PrepareForInteractionLock()
    {
        ClearMovementInput();
        SetSprintHeld(false);
        jumpQueued = false;
        CancelInvoke(nameof(ApplyJumpForce));
    }
}
