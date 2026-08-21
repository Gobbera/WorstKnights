using UnityEngine;

public interface IPlayerMovement
{
    void Move(Vector2 input);
    void Attack();
    void Kick();
    bool TryKick();
    void Jump();
    void SetSprintHeld(bool isHeld);
    void StartCrouch();
    void StopCrouch();
    void SetState(MovementState state);
    void RefreshGrounding();
    bool IsGrounded { get; }
    bool IsJumpQueued { get; }
    bool IsMovementControlLocked { get; }
    bool IsKickActionActive { get; }
    bool CanStartKick { get; }
    bool CanSprint { get; }
    MovementState CurrentState { get; }
}
