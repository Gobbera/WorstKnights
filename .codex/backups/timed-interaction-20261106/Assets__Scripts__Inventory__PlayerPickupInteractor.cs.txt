using System;
using TMPro;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView))]
public class PlayerPickupInteractor : MonoBehaviour
{
    private const string FirstPersonCameraName = "FP_Camera";
    private const int MaxHits = 64;
    private const string PasscodeFieldControlName = "DoorPasscodeField";
    private const float PasscodePanelWidth = 320f;
    private const float PasscodePanelHeight = 140f;
    private const string InteractionPromptObjectName = "InteractionPrompt";
    private const string WorldInteractionPromptObjectName = "WorldInteractionPrompt";
    private static readonly string[] PreferredCanvasRootNames = { "Gameplay Canvas", "Canvas", "Inventory", "Invetory" };

    [FormerlySerializedAs("pickupKey")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] [Min(0.5f)] private float pickupDistance = 3f;
    [SerializeField] [Min(0f)] private float pickupSphereRadius = 0.25f;
    [FormerlySerializedAs("pickupMask")]
    [SerializeField] private LayerMask interactionMask = Physics.DefaultRaycastLayers;
    [SerializeField] private Camera interactionCamera;
    [SerializeField] private HandEquipmentController handEquipmentController;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PhotonView photonView;
    [Header("Pickup Outline")]
    [SerializeField] private bool highlightPickupUnderAim = true;
    [SerializeField] [Min(0.5f)] private float outlineDistance = 3f;
    [SerializeField] [Min(0f)] private float outlineRayRadius;
    [SerializeField] private LayerMask outlineMask = Physics.DefaultRaycastLayers;
    [Header("Interaction Prompt")]
    [SerializeField] private bool showInteractionPrompt = true;
    [SerializeField] private Vector2 interactionPromptOffset = new Vector2(0f, -72f);
    [SerializeField] [Min(120f)] private float interactionPromptWidth = 640f;
    [SerializeField] [Min(20f)] private float interactionPromptHeight = 44f;
    [SerializeField] [Min(8f)] private float interactionPromptFontSize = 22f;
    [SerializeField] private Color interactionPromptColor = new Color(0.92f, 0.94f, 0.96f, 0.96f);
    [SerializeField] private bool showPickupPrompt = true;
    [SerializeField] private string pickupPrompt = "[{key}]";
    [SerializeField] private Color pickupPromptColor = new Color(0.92f, 0.94f, 0.96f, 0.62f);
    [SerializeField] private bool preferObjectPromptAnchors = true;

    private readonly RaycastHit[] sphereCastHits = new RaycastHit[MaxHits];
    private readonly RaycastHit[] aimHits = new RaycastHit[MaxHits];
    private readonly RaycastHit[] outlineHits = new RaycastHit[MaxHits];

    private readonly struct InteractionFocus
    {
        public InteractionFocus(WorldPickupItem pickup, IPlayerInteractable interactable)
        {
            Pickup = pickup;
            Interactable = interactable;
        }

        public WorldPickupItem Pickup { get; }
        public IPlayerInteractable Interactable { get; }
    }
    private DoorController activePasscodeDoor;
    private string passcodeBuffer = string.Empty;
    private bool focusPasscodeField;
    private WorldPickupItem highlightedPickup;
    private Outline highlightedOutline;
    private bool highlightedOutlineWasEnabled;
    private Canvas interactionPromptCanvas;
    private CanvasGroup interactionPromptCanvasGroup;
    private TextMeshProUGUI interactionPromptText;
    private CanvasGroup worldInteractionPromptCanvasGroup;
    private RectTransform worldInteractionPromptRect;
    private TextMeshProUGUI worldInteractionPromptText;

    public HandEquipmentController HandEquipmentController
    {
        get
        {
            ResolveReferences();
            return handEquipmentController;
        }
    }

    public bool IsInteractionUiOpen => activePasscodeDoor != null;
    public KeyCode InteractKey => interactKey;
    public string InteractKeyText => GetKeyDisplayName(interactKey);

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (!HasLocalAuthority())
        {
            ClearPickupOutline();
            SetInteractionPromptVisible(false);
            return;
        }

        if (activePasscodeDoor != null)
        {
            ClearPickupOutline();
            SetInteractionPromptVisible(false);

            if (!activePasscodeDoor.isActiveAndEnabled || !activePasscodeDoor.IsLocked)
                CancelPasscodeEntry();

            return;
        }

        if (InGameSettingsMenuController.IsAnyMenuOpen)
        {
            ClearPickupOutline();
            SetInteractionPromptVisible(false);
            return;
        }

        InteractionFocus focus = TryResolveInteractionCamera(logWarning: false)
            ? ResolveInteractionFocus()
            : default;
        UpdatePickupOutline(focus);
        UpdateInteractionPrompt(focus);

        if (Input.GetKeyDown(interactKey))
            TryInteract(focus);
    }

    private void OnDisable()
    {
        ClearPickupOutline();
        SetInteractionPromptVisible(false);
    }

    private void OnGUI()
    {
        if (!HasLocalAuthority() || activePasscodeDoor == null || InGameSettingsMenuController.IsAnyMenuOpen)
            return;

        Rect panelRect = new Rect(
            (Screen.width - PasscodePanelWidth) * 0.5f,
            (Screen.height - PasscodePanelHeight) * 0.5f,
            PasscodePanelWidth,
            PasscodePanelHeight);

        GUI.Box(panelRect, string.Empty);
        GUI.Label(
            new Rect(panelRect.x + 16f, panelRect.y + 12f, panelRect.width - 32f, 36f),
            $"{activePasscodeDoor.DisplayName}\nDigite a senha para destrancar.");

        GUI.SetNextControlName(PasscodeFieldControlName);
        passcodeBuffer = GUI.TextField(
            new Rect(panelRect.x + 16f, panelRect.y + 56f, panelRect.width - 32f, 24f),
            passcodeBuffer,
            32);

        if (focusPasscodeField)
        {
            GUI.FocusControl(PasscodeFieldControlName);
            focusPasscodeField = false;
        }

        if (GUI.Button(new Rect(panelRect.x + 16f, panelRect.y + 96f, 136f, 28f), "Confirmar"))
            SubmitPasscode();

        if (GUI.Button(new Rect(panelRect.xMax - 152f, panelRect.y + 96f, 136f, 28f), "Cancelar"))
            CancelPasscodeEntry();

        Event currentEvent = Event.current;
        if (currentEvent.type != EventType.KeyDown)
            return;

        if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
        {
            SubmitPasscode();
            currentEvent.Use();
        }
        else if (currentEvent.keyCode == KeyCode.Escape)
        {
            CancelPasscodeEntry();
            currentEvent.Use();
        }
    }

    public bool TryInteract()
    {
        if (!TryResolveInteractionCamera())
            return false;

        return TryInteract(ResolveInteractionFocus());
    }

    public bool TryInteractWithHeldItem(HandType hand, ItemDefinition itemDefinition, KeyCode actionKey)
    {
        if (itemDefinition == null || actionKey == KeyCode.None)
            return false;

        if (!TryResolveInteractionCamera(logWarning: false))
            return false;

        int hitCount = PerformInteractionCast();
        if (!TryFindBestInteractable(hitCount, out IPlayerInteractable interactable))
            return false;

        return TryExecuteHeldItemInteraction(interactable, hand, itemDefinition, actionKey);
    }

    public bool TriggerLitTorchAnimation(HandType hand)
    {
        ResolveReferences();
        return playerMovement != null && playerMovement.TriggerLitTorchAnimation(hand);
    }

    public bool TryPickupItem()
    {
        if (!TryResolveInteractionCamera())
            return false;

        return TryPickupItem(ResolveInteractionFocus().Pickup);
    }

    public void BeginPasscodeEntry(DoorController door)
    {
        if (door == null)
            return;

        activePasscodeDoor = door;
        passcodeBuffer = string.Empty;
        focusPasscodeField = true;
    }

    public void CancelPasscodeEntry()
    {
        activePasscodeDoor = null;
        passcodeBuffer = string.Empty;
        focusPasscodeField = false;
    }

    private void SubmitPasscode()
    {
        if (activePasscodeDoor == null)
            return;

        DoorController targetDoor = activePasscodeDoor;
        bool unlockedDoor = targetDoor.TrySubmitPasscode(passcodeBuffer, this);
        if (unlockedDoor || !targetDoor.IsLocked)
            CancelPasscodeEntry();
    }

    private bool TryResolveInteractionCamera(bool logWarning = true)
    {
        ResolveReferences();

        if (interactionCamera != null)
            return true;

        if (logWarning)
            Debug.LogWarning("[PlayerPickupInteractor] Nenhuma camera local foi encontrada para interacao.", gameObject);

        return false;
    }

    private int PerformInteractionCast()
    {
        Vector3 origin = interactionCamera.transform.position;
        Vector3 direction = interactionCamera.transform.forward;
        return Physics.SphereCastNonAlloc(
            origin,
            pickupSphereRadius,
            direction,
            sphereCastHits,
            pickupDistance,
            DestructibleDebrisCollision.ExcludeDebrisLayer(interactionMask.value),
            QueryTriggerInteraction.Collide);
    }

    private InteractionFocus ResolveInteractionFocus()
    {
        int mask = DestructibleDebrisCollision.ExcludeDebrisLayer(interactionMask.value);
        Transform cameraTransform = interactionCamera.transform;
        int aimHitCount = Physics.RaycastNonAlloc(
            cameraTransform.position,
            cameraTransform.forward,
            aimHits,
            pickupDistance,
            mask,
            QueryTriggerInteraction.Collide);
        WorldPickupItem aimedPickup = FindVisiblePickup(aimHits, aimHitCount);
        if (aimedPickup != null)
            return new InteractionFocus(aimedPickup, null);

        int hitCount = PerformInteractionCast();
        if (TryFindBestInteractable(hitCount, out IPlayerInteractable interactable))
            return new InteractionFocus(null, interactable);

        TryFindBestPickup(hitCount, out WorldPickupItem nearbyPickup);
        return new InteractionFocus(nearbyPickup, null);
    }

    private bool TryInteract(InteractionFocus focus)
    {
        if (focus.Pickup != null)
            return TryPickupItem(focus.Pickup);

        return focus.Interactable != null && focus.Interactable.TryInteract(this);
    }

    private bool TryPickupItem(WorldPickupItem bestPickup)
    {
        ResolveReferences();

        if (handEquipmentController == null)
        {
            Debug.LogWarning("[PlayerPickupInteractor] HandEquipmentController nao encontrado.", gameObject);
            return false;
        }

        if (bestPickup == null || !bestPickup.CanBePickedUp)
        {
            Debug.Log("[PlayerPickupInteractor] Nenhum item coletavel encontrado na frente do jogador.");
            return false;
        }

        bool wasHighlightedPickup = bestPickup == highlightedPickup;
        if (wasHighlightedPickup)
            ClearPickupOutline(forceDisable: true);

        bool equipped = handEquipmentController.TryEquipWorldItem(bestPickup);
        if (!equipped && wasHighlightedPickup)
            UpdatePickupOutline(ResolveInteractionFocus());

        return equipped;
    }

    private bool TryFindBestPickup(int hitCount, out WorldPickupItem bestPickup)
    {
        bestPickup = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = sphereCastHits[i];
            if (hit.collider == null)
                continue;

            if (hit.collider.transform.IsChildOf(transform))
                continue;

            WorldPickupItem pickupItem = hit.collider.GetComponentInParent<WorldPickupItem>();
            if (!IsValidPickupOutlineTarget(pickupItem) || !IsPickupHitVisible(hit, pickupItem))
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            bestPickup = pickupItem;
        }

        return bestPickup != null;
    }

    private bool IsPickupHitVisible(RaycastHit pickupHit, WorldPickupItem pickupItem)
    {
        Vector3 origin = interactionCamera.transform.position;
        Vector3 toPickup = pickupHit.point - origin;
        float distance = toPickup.magnitude;
        if (distance < 0.01f)
            return false;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            toPickup / distance,
            aimHits,
            distance,
            DestructibleDebrisCollision.ExcludeDebrisLayer(interactionMask.value),
            QueryTriggerInteraction.Ignore);
        if (hitCount == aimHits.Length)
            return false;

        for (int i = 0; i < hitCount; i++)
        {
            Collider blocker = aimHits[i].collider;
            if (blocker == null || blocker.transform.IsChildOf(transform))
                continue;

            if (blocker.GetComponentInParent<WorldPickupItem>() != pickupItem
                && aimHits[i].distance < distance - 0.01f)
                return false;
        }

        return true;
    }

    private WorldPickupItem FindVisiblePickup(RaycastHit[] hits, int hitCount)
    {
        if (hitCount == hits.Length)
            return null;

        WorldPickupItem bestPickup = null;
        float bestPickupDistance = float.MaxValue;
        Collider nearestSolid = null;
        float nearestSolidDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            Collider collider = hit.collider;
            if (collider == null || collider.transform.IsChildOf(transform) || collider.isTrigger)
                continue;

            if (hit.distance < nearestSolidDistance)
            {
                nearestSolid = collider;
                nearestSolidDistance = hit.distance;
            }

            WorldPickupItem candidate = collider.GetComponentInParent<WorldPickupItem>();
            if (!IsValidPickupOutlineTarget(candidate) || hit.distance >= bestPickupDistance)
                continue;

            bestPickup = candidate;
            bestPickupDistance = hit.distance;
        }

        if (bestPickup == null || nearestSolid == null)
            return null;

        return nearestSolidDistance + 0.01f >= bestPickupDistance
            || nearestSolid.GetComponentInParent<WorldPickupItem>() == bestPickup
                ? bestPickup
                : null;
    }

    private void UpdateInteractionPrompt(InteractionFocus focus)
    {
        if (!showInteractionPrompt || activePasscodeDoor != null)
        {
            SetInteractionPromptVisible(false);
            return;
        }

        if (focus.Interactable != null
            && TryResolveInteractionPrompt(focus.Interactable, out string promptText))
        {
            if (TryResolveInteractionPromptPlacement(focus.Interactable, out PlayerInteractionPromptPlacement placement))
                SetWorldInteractionPrompt(promptText, placement);
            else
                SetInteractionPrompt(promptText);

            return;
        }

        if (showPickupPrompt && focus.Pickup != null)
        {
            SetPickupInteractionPrompt();
            return;
        }

        SetInteractionPromptVisible(false);
    }

    private bool TryResolveInteractionPrompt(IPlayerInteractable interactable, out string promptText)
    {
        promptText = string.Empty;
        if (interactable == null)
            return false;

        if (TryResolveHeldItemInteractionPrompt(interactable, out promptText))
            return true;

        string promptTemplate = PlayerInteractionPrompt.DefaultTemplate;
        if (interactable is IPlayerInteractionPromptProvider promptProvider)
        {
            PlayerInteractionPromptContext context = new PlayerInteractionPromptContext(this, interactKey, InteractKeyText);
            if (!promptProvider.TryGetInteractionPrompt(context, out promptTemplate))
                return false;
        }

        promptText = PlayerInteractionPrompt.Format(promptTemplate, InteractKeyText);
        return !string.IsNullOrWhiteSpace(promptText);
    }

    private bool TryResolveInteractionPromptPlacement(
        IPlayerInteractable interactable,
        out PlayerInteractionPromptPlacement placement)
    {
        placement = default;
        if (!preferObjectPromptAnchors || interactable is not IPlayerInteractionPromptPlacementProvider placementProvider)
            return false;

        PlayerInteractionPromptContext context = new PlayerInteractionPromptContext(this, interactKey, InteractKeyText);
        return placementProvider.TryGetInteractionPromptPlacement(context, out placement)
            && placement.HasAnchor;
    }

    private bool TryResolveHeldItemInteractionPrompt(IPlayerInteractable interactable, out string promptText)
    {
        promptText = string.Empty;
        if (interactable is not IPlayerHeldItemInteractionProvider heldItemProvider)
            return false;

        ResolveReferences();
        if (handEquipmentController == null)
            return false;

        if (TryResolveHeldItemInteractionPrompt(heldItemProvider, HandType.Right, out promptText))
            return true;

        return TryResolveHeldItemInteractionPrompt(heldItemProvider, HandType.Left, out promptText);
    }

    private bool TryResolveHeldItemInteractionPrompt(
        IPlayerHeldItemInteractionProvider heldItemProvider,
        HandType hand,
        out string promptText)
    {
        promptText = string.Empty;
        if (heldItemProvider == null || handEquipmentController == null)
            return false;

        int activeIndex = handEquipmentController.GetActiveSlotIndex(hand);
        ItemDefinition itemDefinition = handEquipmentController.GetItem(hand, activeIndex);
        if (itemDefinition == null)
            return false;

        KeyCode actionKey = handEquipmentController.GetUseKey(hand);
        if (actionKey == KeyCode.None)
            return false;

        PlayerHeldItemInteractionContext context = CreateHeldItemInteractionContext(hand, itemDefinition, actionKey);
        if (!heldItemProvider.TryGetHeldItemInteractionPrompt(context, out string promptTemplate))
            return false;

        promptText = PlayerInteractionPrompt.Format(promptTemplate, context.ActionKeyText);
        return !string.IsNullOrWhiteSpace(promptText);
    }

    private bool TryExecuteHeldItemInteraction(
        IPlayerInteractable interactable,
        HandType hand,
        ItemDefinition itemDefinition,
        KeyCode actionKey)
    {
        if (interactable is not IPlayerHeldItemInteractionProvider heldItemProvider)
            return false;

        PlayerHeldItemInteractionContext context = CreateHeldItemInteractionContext(hand, itemDefinition, actionKey);
        return heldItemProvider.TryInteractWithHeldItem(context);
    }

    private PlayerHeldItemInteractionContext CreateHeldItemInteractionContext(
        HandType hand,
        ItemDefinition itemDefinition,
        KeyCode actionKey)
    {
        ResolveReferences();
        string actionKeyText = GetKeyDisplayName(actionKey);
        return new PlayerHeldItemInteractionContext(
            this,
            handEquipmentController,
            hand,
            itemDefinition,
            actionKey,
            actionKeyText);
    }

    private void SetInteractionPrompt(string promptText)
    {
        SetScreenInteractionPrompt(promptText, interactionPromptColor);
    }

    private void SetPickupInteractionPrompt()
    {
        string promptTemplate = string.IsNullOrWhiteSpace(pickupPrompt)
            ? "[{key}]"
            : pickupPrompt;
        string promptText = PlayerInteractionPrompt.Format(promptTemplate, InteractKeyText);
        SetScreenInteractionPrompt(promptText, pickupPromptColor);
    }

    private void SetScreenInteractionPrompt(string promptText, Color promptColor)
    {
        SetWorldInteractionPromptVisible(false);
        EnsureInteractionPromptUi();
        if (interactionPromptText == null || interactionPromptCanvasGroup == null)
            return;

        ConfigureInteractionPromptRect();
        interactionPromptText.text = promptText;
        interactionPromptText.color = promptColor;
        interactionPromptText.fontSize = interactionPromptFontSize;
        SetScreenInteractionPromptVisible(true);
    }

    private void SetWorldInteractionPrompt(string promptText, PlayerInteractionPromptPlacement placement)
    {
        SetScreenInteractionPromptVisible(false);
        EnsureWorldInteractionPromptUi();
        if (worldInteractionPromptText == null
            || worldInteractionPromptCanvasGroup == null
            || worldInteractionPromptRect == null)
        {
            return;
        }

        if (!UpdateWorldInteractionPromptTransform(placement))
        {
            SetWorldInteractionPromptVisible(false);
            return;
        }

        ConfigureWorldInteractionPromptRect();
        worldInteractionPromptText.text = promptText;
        worldInteractionPromptText.color = interactionPromptColor;
        worldInteractionPromptText.fontSize = interactionPromptFontSize;
        SetWorldInteractionPromptVisible(true);
    }

    private void SetInteractionPromptVisible(bool visible)
    {
        SetScreenInteractionPromptVisible(visible);
        SetWorldInteractionPromptVisible(visible);
    }

    private void SetScreenInteractionPromptVisible(bool visible)
    {
        if (interactionPromptCanvasGroup == null)
            return;

        interactionPromptCanvasGroup.alpha = visible ? 1f : 0f;
        interactionPromptCanvasGroup.interactable = false;
        interactionPromptCanvasGroup.blocksRaycasts = false;
    }

    private void SetWorldInteractionPromptVisible(bool visible)
    {
        if (worldInteractionPromptCanvasGroup == null)
            return;

        worldInteractionPromptCanvasGroup.alpha = visible ? 1f : 0f;
        worldInteractionPromptCanvasGroup.interactable = false;
        worldInteractionPromptCanvasGroup.blocksRaycasts = false;
    }

    private void EnsureInteractionPromptUi()
    {
        if (interactionPromptText != null && interactionPromptCanvasGroup != null)
            return;

        Canvas targetCanvas = FindGameplayCanvas();
        if (targetCanvas == null)
            targetCanvas = CreateGameplayCanvas();

        if (targetCanvas == null)
            return;

        interactionPromptCanvas = targetCanvas;
        interactionPromptText = FindInteractionPromptText(targetCanvas);
        if (interactionPromptText != null)
        {
            interactionPromptCanvasGroup = interactionPromptText.GetComponent<CanvasGroup>();
            if (interactionPromptCanvasGroup == null)
                interactionPromptCanvasGroup = interactionPromptText.gameObject.AddComponent<CanvasGroup>();

            ConfigureInteractionPromptText();
            return;
        }

        GameObject promptObject = new GameObject(InteractionPromptObjectName, typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI), typeof(Shadow));
        promptObject.transform.SetParent(targetCanvas.transform, false);

        interactionPromptCanvasGroup = promptObject.GetComponent<CanvasGroup>();
        interactionPromptText = promptObject.GetComponent<TextMeshProUGUI>();
        Shadow shadow = promptObject.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        ConfigureInteractionPromptText();
        ConfigureInteractionPromptRect();
        SetInteractionPromptVisible(false);
    }

    private void EnsureWorldInteractionPromptUi()
    {
        if (worldInteractionPromptText != null
            && worldInteractionPromptCanvasGroup != null
            && worldInteractionPromptRect != null)
        {
            return;
        }

        Canvas targetCanvas = FindGameplayCanvas();
        if (targetCanvas == null)
            targetCanvas = CreateGameplayCanvas();

        if (targetCanvas == null)
            return;

        worldInteractionPromptText = FindWorldInteractionPromptText(targetCanvas);
        if (worldInteractionPromptText != null)
        {
            worldInteractionPromptRect = worldInteractionPromptText.rectTransform;
            worldInteractionPromptCanvasGroup = worldInteractionPromptText.GetComponent<CanvasGroup>();
            if (worldInteractionPromptCanvasGroup == null)
                worldInteractionPromptCanvasGroup = worldInteractionPromptText.gameObject.AddComponent<CanvasGroup>();

            ConfigureWorldInteractionPromptText();
            ConfigureWorldInteractionPromptRect();
            return;
        }

        GameObject promptObject = new GameObject(WorldInteractionPromptObjectName, typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI), typeof(Shadow));
        promptObject.transform.SetParent(targetCanvas.transform, false);
        promptObject.transform.SetAsLastSibling();

        worldInteractionPromptRect = promptObject.GetComponent<RectTransform>();
        worldInteractionPromptCanvasGroup = promptObject.GetComponent<CanvasGroup>();
        worldInteractionPromptText = promptObject.GetComponent<TextMeshProUGUI>();
        Shadow shadow = promptObject.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        ConfigureWorldInteractionPromptText();
        ConfigureWorldInteractionPromptRect();
        SetWorldInteractionPromptVisible(false);
    }

    private void ConfigureInteractionPromptText()
    {
        if (interactionPromptText == null)
            return;

        interactionPromptText.raycastTarget = false;
        interactionPromptText.alignment = TextAlignmentOptions.Center;
        interactionPromptText.textWrappingMode = TextWrappingModes.NoWrap;
        interactionPromptText.overflowMode = TextOverflowModes.Ellipsis;
        interactionPromptText.fontSize = interactionPromptFontSize;
        interactionPromptText.color = interactionPromptColor;
    }

    private void ConfigureWorldInteractionPromptText()
    {
        if (worldInteractionPromptText == null)
            return;

        worldInteractionPromptText.raycastTarget = false;
        worldInteractionPromptText.alignment = TextAlignmentOptions.Center;
        worldInteractionPromptText.textWrappingMode = TextWrappingModes.NoWrap;
        worldInteractionPromptText.overflowMode = TextOverflowModes.Ellipsis;
        worldInteractionPromptText.fontSize = interactionPromptFontSize;
        worldInteractionPromptText.color = interactionPromptColor;
    }

    private void ConfigureInteractionPromptRect()
    {
        if (interactionPromptText == null)
            return;

        RectTransform promptRect = interactionPromptText.rectTransform;
        promptRect.anchorMin = new Vector2(0.5f, 0.5f);
        promptRect.anchorMax = new Vector2(0.5f, 0.5f);
        promptRect.pivot = new Vector2(0.5f, 0.5f);
        promptRect.anchoredPosition = interactionPromptOffset;
        promptRect.sizeDelta = new Vector2(interactionPromptWidth, interactionPromptHeight);
    }

    private void ConfigureWorldInteractionPromptRect()
    {
        if (worldInteractionPromptRect == null)
            return;

        worldInteractionPromptRect.anchorMin = new Vector2(0.5f, 0.5f);
        worldInteractionPromptRect.anchorMax = new Vector2(0.5f, 0.5f);
        worldInteractionPromptRect.pivot = new Vector2(0.5f, 0.5f);
        worldInteractionPromptRect.sizeDelta = new Vector2(interactionPromptWidth, interactionPromptHeight);
        worldInteractionPromptRect.localRotation = Quaternion.identity;
        worldInteractionPromptRect.localScale = Vector3.one;
    }

    private bool UpdateWorldInteractionPromptTransform(PlayerInteractionPromptPlacement placement)
    {
        if (worldInteractionPromptRect == null || interactionCamera == null || !placement.HasAnchor)
            return false;

        Vector3 worldPosition = placement.ResolveWorldPosition();
        Vector3 screenPoint = interactionCamera.WorldToScreenPoint(worldPosition);
        if (screenPoint.z <= 0.01f)
            return false;

        Canvas targetCanvas = worldInteractionPromptRect.GetComponentInParent<Canvas>();
        RectTransform canvasRect = targetCanvas != null ? targetCanvas.transform as RectTransform : null;
        if (targetCanvas == null || canvasRect == null)
            return false;

        Camera uiCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : targetCanvas.worldCamera != null
                ? targetCanvas.worldCamera
                : interactionCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPoint,
                uiCamera,
                out Vector2 localPoint))
        {
            return false;
        }

        worldInteractionPromptRect.SetAsLastSibling();
        worldInteractionPromptRect.anchoredPosition = localPoint;
        worldInteractionPromptRect.localRotation = Quaternion.identity;
        worldInteractionPromptRect.localScale = Vector3.one;

        return true;
    }

    private static TextMeshProUGUI FindInteractionPromptText(Canvas targetCanvas)
    {
        if (targetCanvas == null)
            return null;

        TextMeshProUGUI[] texts = targetCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TextMeshProUGUI text = texts[i];
            if (text != null && string.Equals(text.gameObject.name, InteractionPromptObjectName, StringComparison.Ordinal))
                return text;
        }

        return null;
    }

    private static TextMeshProUGUI FindWorldInteractionPromptText(Canvas targetCanvas)
    {
        if (targetCanvas == null)
            return null;

        TextMeshProUGUI[] texts = targetCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TextMeshProUGUI text = texts[i];
            if (text != null && string.Equals(text.gameObject.name, WorldInteractionPromptObjectName, StringComparison.Ordinal))
                return text;
        }

        return null;
    }

    private static Canvas FindGameplayCanvas()
    {
        Transform preferredRoot = FindSceneTransformByNames(PreferredCanvasRootNames);
        if (preferredRoot != null)
        {
            Canvas preferredCanvas = preferredRoot.GetComponentInParent<Canvas>(true);
            if (preferredCanvas != null)
                return preferredCanvas;
        }

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas candidate = canvases[i];
            if (candidate != null && candidate.isActiveAndEnabled && candidate.renderMode == RenderMode.ScreenSpaceOverlay)
                return candidate;
        }

        return canvases.Length > 0 ? canvases[0] : null;
    }

    private static Canvas CreateGameplayCanvas()
    {
        GameObject canvasObject = new GameObject("Gameplay Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas createdCanvas = canvasObject.GetComponent<Canvas>();
        createdCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return createdCanvas;
    }

    private static Transform FindSceneTransformByNames(string[] objectNames)
    {
        Transform[] sceneTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform sceneTransform = sceneTransforms[i];
            if (sceneTransform == null)
                continue;

            for (int j = 0; j < objectNames.Length; j++)
            {
                if (string.Equals(sceneTransform.name, objectNames[j], StringComparison.Ordinal))
                    return sceneTransform;
            }
        }

        return null;
    }

    public static string GetKeyDisplayName(KeyCode keyCode)
    {
        if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
            return ((int)keyCode - (int)KeyCode.Alpha0).ToString();

        if (keyCode >= KeyCode.Keypad0 && keyCode <= KeyCode.Keypad9)
            return $"Num {(int)keyCode - (int)KeyCode.Keypad0}";

        return keyCode switch
        {
            KeyCode.Mouse0 => "LMB",
            KeyCode.Mouse1 => "RMB",
            KeyCode.Mouse2 => "MMB",
            KeyCode.Space => "Space",
            KeyCode.Return => "Enter",
            KeyCode.KeypadEnter => "Enter",
            KeyCode.LeftShift => "Shift",
            KeyCode.RightShift => "Shift",
            KeyCode.LeftControl => "Ctrl",
            KeyCode.RightControl => "Ctrl",
            KeyCode.LeftAlt => "Alt",
            KeyCode.RightAlt => "Alt",
            KeyCode.Escape => "Esc",
            _ => keyCode.ToString()
        };
    }

    private bool TryFindBestInteractable(int hitCount, out IPlayerInteractable bestInteractable)
    {
        bestInteractable = null;
        int bestPriority = int.MinValue;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = sphereCastHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                continue;

            if (!TryGetInteractableFromCollider(hit.collider, out IPlayerInteractable interactable))
                continue;

            bool shouldReplace = interactable.InteractionPriority > bestPriority;
            if (!shouldReplace
                && interactable.InteractionPriority == bestPriority
                && hit.distance < bestDistance)
            {
                shouldReplace = true;
            }

            if (!shouldReplace)
                continue;

            bestInteractable = interactable;
            bestPriority = interactable.InteractionPriority;
            bestDistance = hit.distance;
        }

        return bestInteractable != null;
    }

    private static bool TryGetInteractableFromCollider(Collider collider, out IPlayerInteractable bestInteractable)
    {
        bestInteractable = null;
        if (collider == null)
            return false;

        WorldPickupItem pickupItem = collider.GetComponentInParent<WorldPickupItem>();
        MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>(true);
        int bestPriority = int.MinValue;

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is not IPlayerInteractable interactable)
                continue;

            if (pickupItem != null && !behaviours[i].transform.IsChildOf(pickupItem.transform))
                continue;

            if (interactable.InteractionPriority <= bestPriority)
                continue;

            bestPriority = interactable.InteractionPriority;
            bestInteractable = interactable;
        }

        return bestInteractable != null;
    }

    private void ResolveReferences()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (handEquipmentController == null)
            handEquipmentController = GetComponent<HandEquipmentController>();

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

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

    private bool HasLocalAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private void UpdatePickupOutline(InteractionFocus focus)
    {
        if (!highlightPickupUnderAim || focus.Pickup == null)
        {
            ClearPickupOutline();
            return;
        }

        ResolveReferences();
        if (interactionCamera == null)
        {
            ClearPickupOutline();
            return;
        }

        SetHighlightedPickup(FindPickupUnderAim() == focus.Pickup ? focus.Pickup : null);
    }

    private WorldPickupItem FindPickupUnderAim()
    {
        Transform cameraTransform = interactionCamera.transform;
        Vector3 origin = cameraTransform.position;
        Vector3 direction = cameraTransform.forward;

        int hitCount = outlineRayRadius > 0f
            ? Physics.SphereCastNonAlloc(
                origin,
                outlineRayRadius,
                direction,
                outlineHits,
                outlineDistance,
                DestructibleDebrisCollision.ExcludeDebrisLayer(outlineMask.value),
                QueryTriggerInteraction.Collide)
            : Physics.RaycastNonAlloc(
                origin,
                direction,
                outlineHits,
                outlineDistance,
                DestructibleDebrisCollision.ExcludeDebrisLayer(outlineMask.value),
                QueryTriggerInteraction.Collide);

        return FindVisiblePickup(outlineHits, hitCount);
    }

    private static bool IsValidPickupOutlineTarget(WorldPickupItem pickupItem)
    {
        return pickupItem != null
            && pickupItem.gameObject.activeInHierarchy
            && !pickupItem.IsEquipped
            && pickupItem.CanBePickedUp
            && !pickupItem.IsPresentationClone;
    }

    private void SetHighlightedPickup(WorldPickupItem pickupItem)
    {
        if (highlightedPickup == pickupItem)
            return;

        ClearPickupOutline();
        if (pickupItem == null)
            return;

        Outline outline = ResolvePickupOutline(pickupItem);
        if (outline == null)
            return;

        highlightedPickup = pickupItem;
        highlightedOutline = outline;
        highlightedOutlineWasEnabled = outline.enabled;

        outline.enabled = true;
    }

    private Outline ResolvePickupOutline(WorldPickupItem pickupItem)
    {
        Outline outline = pickupItem.GetComponent<Outline>();
        if (outline == null)
            outline = pickupItem.GetComponentInChildren<Outline>(true);

        return outline;
    }

    private void ClearPickupOutline(bool forceDisable = false)
    {
        if (highlightedOutline != null)
            highlightedOutline.enabled = !forceDisable && highlightedOutlineWasEnabled;

        highlightedPickup = null;
        highlightedOutline = null;
        highlightedOutlineWasEnabled = false;
    }
}
