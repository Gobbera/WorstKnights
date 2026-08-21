using UnityEngine;

public interface IPlayerMovement
{
    void Move(Vector2 input);
    void Jump();
    void StartCrouch();
    void StopCrouch();
    void SetState(MovementState state);
    bool IsGrounded { get; }
    bool IsJumpQueued { get; }
    MovementState CurrentState { get; }
}
