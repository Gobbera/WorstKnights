using UnityEngine;

[DisallowMultipleComponent]
public class PlayerInputHandler : MonoBehaviour, IPlayerInput
{
    private const string DefaultInputConfigResourcePath = "InputConfig";

    [SerializeField] private InputConfig inputConfig;

    private KeyCode attackKeyFallback = KeyCode.Mouse0;
    private KeyCode kickKeyFallback = KeyCode.F;
    private KeyCode jumpKeyFallback = KeyCode.Space;
    private KeyCode sprintKeyFallback = KeyCode.LeftShift;
    private KeyCode crouchKeyFallback = KeyCode.LeftControl;
    private const float MovementDeadzoneFallback = 0.15f;

    private Vector2 movementInput;
    private static bool GameplayInputEnabled => !InGameSettingsMenuController.IsAnyMenuOpen;

    public Vector2 MovementInput => GameplayInputEnabled ? movementInput : Vector2.zero;
    public bool AttackPressed => GameplayInputEnabled && Input.GetKeyDown(GetAttackKey());
    public bool KickPressed => GameplayInputEnabled && Input.GetKeyDown(GetKickKey());
    public bool JumpPressed => GameplayInputEnabled && Input.GetKeyDown(GetJumpKey());
    public bool CrouchHeld => GameplayInputEnabled && Input.GetKey(GetCrouchKey());
    public bool CrouchPressed => GameplayInputEnabled && Input.GetKeyDown(GetCrouchKey());
    public bool CrouchReleased => GameplayInputEnabled && Input.GetKeyUp(GetCrouchKey());
    public bool SprintPressed => GameplayInputEnabled && Input.GetKey(GetSprintKey());

    private void Awake()
    {
        ResolveInputConfig();
    }

    private void ResolveInputConfig()
    {
        if (inputConfig == null)
        {
            inputConfig = Resources.Load<InputConfig>(DefaultInputConfigResourcePath);
            if (inputConfig != null)
            {
                Debug.Log("[PlayerInputHandler] InputConfig loaded from Resources/InputConfig.asset", gameObject);
            }
            else
            {
                InputConfig[] configs = Resources.LoadAll<InputConfig>(".");
                if (configs.Length > 0)
                {
                    inputConfig = configs[0];
                    Debug.Log("[PlayerInputHandler] InputConfig found in project. Assign it to the PlayerInputHandler field to avoid this search.", gameObject);
                }
                else
                {
                    Debug.LogWarning("[PlayerInputHandler] InputConfig not found in Resources or project. Using fallback keybinds (Mouse0, Space, Shift, Ctrl).", gameObject);
                }
            }
        }
        else
        {
            Debug.Log("[PlayerInputHandler] Using InputConfig from Inspector.", gameObject);
        }
    }

    public void UpdateInput()
    {
        if (!GameplayInputEnabled)
        {
            movementInput = Vector2.zero;
            return;
        }

        Vector2 rawMovementInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        float movementDeadzone = inputConfig != null
            ? Mathf.Clamp01(inputConfig.movementDeadzone)
            : MovementDeadzoneFallback;

        if (rawMovementInput.sqrMagnitude <= movementDeadzone * movementDeadzone)
        {
            movementInput = Vector2.zero;
            return;
        }

        // Preserve full axis intent for animation and higher-level locomotion decisions.
        // PlayerMovement still clamps physical motion internally, so diagonals do not gain speed.
        movementInput = rawMovementInput;
    }

    private KeyCode GetAttackKey() => inputConfig != null ? inputConfig.attackKey : attackKeyFallback;
    private KeyCode GetKickKey() => inputConfig != null ? inputConfig.kickKey : kickKeyFallback;
    private KeyCode GetJumpKey() => inputConfig != null ? inputConfig.jumpKey : jumpKeyFallback;
    private KeyCode GetSprintKey() => inputConfig != null ? inputConfig.sprintKey : sprintKeyFallback;
    private KeyCode GetCrouchKey() => inputConfig != null ? inputConfig.crouchKey : crouchKeyFallback;
}
