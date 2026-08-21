using UnityEngine;

public interface IPlayerInput
{
    Vector2 MovementInput { get; }
    bool AttackPressed { get; }
    bool KickPressed { get; }
    bool JumpPressed { get; }
    bool CrouchHeld { get; }
    bool CrouchPressed { get; }
    bool CrouchReleased { get; }
    bool SprintPressed { get; }
    void UpdateInput();
}
