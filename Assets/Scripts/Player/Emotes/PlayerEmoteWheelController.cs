using System;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView), typeof(PlayerMovement))]
public sealed class PlayerEmoteWheelController : MonoBehaviour
{
    public const int SlotCapacity = 5;

    private const string FirstPersonCameraName = "FP_Camera";

    [Header("Input")]
    [FormerlySerializedAs("thumbsUpKey")]
    [SerializeField] private KeyCode wheelKey = KeyCode.T;
    [SerializeField] private KeyCode cancelKey = KeyCode.Escape;
    [Header("Emotes")]
    [SerializeField] private EmoteWheelSlotConfig[] emoteWheelSlots = CreateDefaultEmoteWheelSlots();
    [SerializeField] [HideInInspector] private PlayerEmoteType[] legacyEmoteWheelOptions = { PlayerEmoteType.ThumbsUp, PlayerEmoteType.Point };
    [Header("View")]
    [SerializeField] private Camera interactionCamera;
    [SerializeField] private MouseLook mouseLook;
    [SerializeField] private bool pauseLookInputWhileOpen = true;
    [SerializeField] private CursorLockMode openCursorLockMode = CursorLockMode.Confined;
    [SerializeField] private bool showCursorWhileOpen = true;

    private PhotonView photonView;
    private PlayerMovement playerMovement;
    private bool isWheelOpen;
    private PlayerEmoteType hoveredEmote = PlayerEmoteType.None;
    private PlayerEmoteType pendingEmoteSelection = PlayerEmoteType.None;
    private CursorLockMode cursorLockStateBeforeWheel;
    private bool cursorVisibleBeforeWheel;
    private bool hasCursorOverride;

    public event Action<bool> WheelVisibilityChanged;
    public event Action<PlayerEmoteType> WheelHoverChanged;
    public event Action<PlayerEmoteType> WheelCommitted;

    public bool IsWheelOpen => isWheelOpen;
    public PlayerEmoteType HoveredEmote => hoveredEmote;
    public PlayerEmoteType PendingEmoteSelection => pendingEmoteSelection;
    public int EmoteWheelSlotCount => SlotCapacity;
    public KeyCode WheelKey => wheelKey;

    private void Awake()
    {
        EnsureEmoteWheelSlots();
        ResolveReferences();
    }

    private void Start()
    {
        ResolveReferences();

        if (HasLocalAuthority())
            EmoteWheelUI.BindLocalPlayer(this);
    }

    private void Update()
    {
        if (!HasLocalAuthority())
            return;

        HandleRuntimeInput();
    }

    private void OnDisable()
    {
        CloseWheel(notifySelectionCleared: false);
    }

    public void HandleRuntimeInput()
    {
        if (Input.GetKeyDown(wheelKey))
            BeginWheel();

        if (!isWheelOpen)
            return;

        if (Input.GetKeyDown(cancelKey))
        {
            CancelWheel();
            return;
        }

        if (Input.GetKeyUp(wheelKey))
            CommitWheelSelection();
    }

    public void BeginWheel()
    {
        ResolveReferences();

        if (!HasLocalAuthority() || isWheelOpen)
            return;

        if (playerMovement != null && !playerMovement.CanBeginInventoryItemAction())
            return;

        EnsureEmoteWheelSlots();
        ApplyWheelCursorState();
        SetWheelSelectionLock(true);

        isWheelOpen = true;
        hoveredEmote = PlayerEmoteType.None;
        pendingEmoteSelection = PlayerEmoteType.None;
        WheelVisibilityChanged?.Invoke(true);
        WheelHoverChanged?.Invoke(hoveredEmote);
    }

    public void PreviewEmote(PlayerEmoteType emoteType)
    {
        if (!isWheelOpen)
            return;

        hoveredEmote = emoteType;
        pendingEmoteSelection = emoteType;
        WheelHoverChanged?.Invoke(hoveredEmote);
    }

    public void CommitWheelSelection()
    {
        if (!isWheelOpen)
            return;

        PlayerEmoteType emoteType = pendingEmoteSelection;
        CloseWheel(notifySelectionCleared: true);

        if (emoteType != PlayerEmoteType.None)
            TryTriggerEmote(emoteType);
    }

    public void CancelWheel()
    {
        CloseWheel(notifySelectionCleared: true);
    }

    public bool TryTriggerEmote(PlayerEmoteType emoteType)
    {
        ResolveReferences();

        if (playerMovement == null || emoteType == PlayerEmoteType.None)
            return false;

        if (!playerMovement.TriggerEmote(emoteType))
            return false;

        WheelCommitted?.Invoke(emoteType);
        return true;
    }

    public EmoteWheelSlotConfig GetEmoteWheelSlot(int slotIndex)
    {
        EnsureEmoteWheelSlots();

        if (slotIndex < 0 || slotIndex >= emoteWheelSlots.Length)
            return null;

        return emoteWheelSlots[slotIndex];
    }

    private void ResolveReferences()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        ResolveInteractionCamera();
        ResolveMouseLook();
    }

    private void ResolveInteractionCamera()
    {
        if (interactionCamera != null)
            return;

        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera playerCamera = cameras[i];
            if (playerCamera == null)
                continue;

            if (string.Equals(playerCamera.gameObject.name, FirstPersonCameraName, StringComparison.Ordinal))
            {
                interactionCamera = playerCamera;
                return;
            }

            if (interactionCamera == null && playerCamera.enabled)
                interactionCamera = playerCamera;
        }
    }

    private void ResolveMouseLook()
    {
        if (mouseLook != null)
            return;

        if (interactionCamera != null)
            mouseLook = interactionCamera.GetComponent<MouseLook>();

        if (mouseLook == null)
            mouseLook = GetComponentInChildren<MouseLook>(true);
    }

    private bool HasLocalAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private void ApplyWheelCursorState()
    {
        if (!hasCursorOverride)
        {
            cursorLockStateBeforeWheel = Cursor.lockState;
            cursorVisibleBeforeWheel = Cursor.visible;
            hasCursorOverride = true;
        }

        if (pauseLookInputWhileOpen && mouseLook != null)
            mouseLook.SetLookInputBlocked(true);

        Cursor.lockState = openCursorLockMode;
        Cursor.visible = showCursorWhileOpen;
    }

    private void RestoreWheelCursorState()
    {
        if (pauseLookInputWhileOpen && mouseLook != null)
            mouseLook.SetLookInputBlocked(false);

        if (!hasCursorOverride)
            return;

        Cursor.lockState = cursorLockStateBeforeWheel;
        Cursor.visible = cursorVisibleBeforeWheel;
        hasCursorOverride = false;
    }

    private void SetWheelSelectionLock(bool active)
    {
        if (playerMovement != null)
            playerMovement.SetEmoteWheelSelectionActive(active);
    }

    private void CloseWheel(bool notifySelectionCleared)
    {
        if (!isWheelOpen && !notifySelectionCleared)
        {
            RestoreWheelCursorState();
            SetWheelSelectionLock(false);
            return;
        }

        bool wasOpen = isWheelOpen;
        isWheelOpen = false;
        hoveredEmote = PlayerEmoteType.None;
        pendingEmoteSelection = PlayerEmoteType.None;

        if (wasOpen || hasCursorOverride)
            RestoreWheelCursorState();

        if (wasOpen)
            SetWheelSelectionLock(false);

        if (wasOpen)
            WheelVisibilityChanged?.Invoke(false);

        if (wasOpen || notifySelectionCleared)
            WheelHoverChanged?.Invoke(PlayerEmoteType.None);
    }

    private void EnsureEmoteWheelSlots()
    {
        if (emoteWheelSlots != null && emoteWheelSlots.Length == SlotCapacity)
        {
            bool allSlotsValid = true;
            for (int i = 0; i < emoteWheelSlots.Length; i++)
            {
                if (emoteWheelSlots[i] == null)
                {
                    emoteWheelSlots[i] = EmoteWheelSlotConfig.CreateEmpty();
                    allSlotsValid = false;
                }
            }

            if (HasConfiguredEmoteWheelSlot() || legacyEmoteWheelOptions == null || legacyEmoteWheelOptions.Length == 0)
                return;

            allSlotsValid = false;
            for (int i = 0; i < Mathf.Min(legacyEmoteWheelOptions.Length, SlotCapacity); i++)
                emoteWheelSlots[i] = EmoteWheelSlotConfig.Create(legacyEmoteWheelOptions[i]);

            for (int i = legacyEmoteWheelOptions.Length; i < SlotCapacity; i++)
                emoteWheelSlots[i] = EmoteWheelSlotConfig.CreateEmpty();

            if (allSlotsValid)
                return;
        }

        EmoteWheelSlotConfig[] nextSlots = CreateDefaultEmoteWheelSlots();
        if (emoteWheelSlots != null)
        {
            int copyCount = Mathf.Min(emoteWheelSlots.Length, nextSlots.Length);
            for (int i = 0; i < copyCount; i++)
            {
                if (emoteWheelSlots[i] != null)
                    nextSlots[i] = emoteWheelSlots[i];
            }
        }

        if (!HasConfiguredEmoteWheelSlot(nextSlots) && legacyEmoteWheelOptions != null)
        {
            int copyCount = Mathf.Min(legacyEmoteWheelOptions.Length, nextSlots.Length);
            for (int i = 0; i < copyCount; i++)
                nextSlots[i] = EmoteWheelSlotConfig.Create(legacyEmoteWheelOptions[i]);
        }

        emoteWheelSlots = nextSlots;
    }

    private bool HasConfiguredEmoteWheelSlot()
    {
        return HasConfiguredEmoteWheelSlot(emoteWheelSlots);
    }

    private static bool HasConfiguredEmoteWheelSlot(EmoteWheelSlotConfig[] slots)
    {
        if (slots == null)
            return false;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].HasEmote)
                return true;
        }

        return false;
    }

    private static EmoteWheelSlotConfig[] CreateDefaultEmoteWheelSlots()
    {
        return new[]
        {
            EmoteWheelSlotConfig.Create(PlayerEmoteType.ThumbsUp),
            EmoteWheelSlotConfig.Create(PlayerEmoteType.Point),
            EmoteWheelSlotConfig.CreateEmpty(),
            EmoteWheelSlotConfig.CreateEmpty(),
            EmoteWheelSlotConfig.CreateEmpty()
        };
    }
}
