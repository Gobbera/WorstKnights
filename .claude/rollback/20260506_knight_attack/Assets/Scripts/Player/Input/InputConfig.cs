using UnityEngine;

[CreateAssetMenu(fileName = "InputConfig", menuName = "Player/InputConfig")]
public class InputConfig : ScriptableObject
{
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.LeftControl;
    [Range(0f, 1f)] public float movementDeadzone = 0.15f;
}
