using System;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class EmoteWheelUI : MonoBehaviour
{
    private const string WheelObjectName = "EmoteWheel";
    private static readonly string[] PreferredCanvasRootNames = { "Gameplay Canvas", "Canvas", "Inventory", "Invetory" };

    private sealed class EmoteSlotView
    {
        public RectTransform root;
        public EmoteWheelSliceGraphic slice;
        public Image icon;
        public Text label;
    }

    [SerializeField] private PlayerEmoteWheelController targetController;
    [SerializeField] [Min(180f)] private float wheelSize = 360f;
    [SerializeField] [Min(24f)] private float innerRadius = 64f;
    [SerializeField] [Min(80f)] private float outerRadius = 165f;
    [SerializeField] [Range(0f, 8f)] private float segmentGapDegrees = 2f;
    [SerializeField] [Min(0f)] private float minSelectionRadius = 48f;
    [SerializeField] [Min(16f)] private float iconSize = 56f;
    [SerializeField] private Vector2 screenOffset;
    [Header("Colors")]
    [SerializeField] private Color activeColor = new Color(0.12f, 0.14f, 0.16f, 0.86f);
    [SerializeField] private Color selectedColor = new Color(0.95f, 0.72f, 0.28f, 0.95f);
    [SerializeField] private Color disabledColor = new Color(0.04f, 0.045f, 0.05f, 0.46f);
    [SerializeField] private Color centerColor = new Color(0.02f, 0.025f, 0.03f, 0.72f);
    [SerializeField] private Color iconColor = Color.white;
    [SerializeField] private Color labelColor = new Color(0.92f, 0.94f, 0.96f, 1f);
    [SerializeField] private Color disabledLabelColor = new Color(0.55f, 0.58f, 0.62f, 0.45f);

    private readonly EmoteSlotView[] slots = new EmoteSlotView[PlayerEmoteWheelController.SlotCapacity];
    private RectTransform rectTransform;
    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private EmoteWheelSliceGraphic centerGraphic;
    private Font fallbackFont;
    private int highlightedSlotIndex = -1;
    private bool isVisible;

    public static void BindLocalPlayer(PlayerEmoteWheelController controller)
    {
        if (controller == null)
            return;

        Canvas targetCanvas = FindGameplayCanvas();
        if (targetCanvas == null)
            targetCanvas = CreateGameplayCanvas();

        if (targetCanvas == null)
            return;

        EmoteWheelUI wheel = targetCanvas.GetComponentInChildren<EmoteWheelUI>(true);
        if (wheel == null)
            wheel = CreateWheel(targetCanvas.transform);

        wheel.SetTarget(controller);
    }

    private void Awake()
    {
        CacheComponents();
        EnsureBuilt();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        UnsubscribeFromTarget();
    }

    private void Update()
    {
        if (targetController == null)
            SetTarget(FindLocalController());

        bool shouldBeVisible = targetController != null && targetController.IsWheelOpen;
        if (shouldBeVisible != isVisible)
            SetVisible(shouldBeVisible);

        if (!shouldBeVisible)
            return;

        EnsureBuilt();
        UpdateMouseSelection();
        RefreshSlots();
    }

    public void SetTarget(PlayerEmoteWheelController controller)
    {
        if (targetController == controller)
        {
            RefreshSlots();
            return;
        }

        UnsubscribeFromTarget();
        targetController = controller;
        SubscribeToTarget();
        RefreshSlots();
        SetVisible(targetController != null && targetController.IsWheelOpen);
    }

    private void SubscribeToTarget()
    {
        if (targetController == null)
            return;

        targetController.WheelVisibilityChanged += HandleWheelVisibilityChanged;
        targetController.WheelHoverChanged += HandleWheelHoverChanged;
    }

    private void UnsubscribeFromTarget()
    {
        if (targetController == null)
            return;

        targetController.WheelVisibilityChanged -= HandleWheelVisibilityChanged;
        targetController.WheelHoverChanged -= HandleWheelHoverChanged;
    }

    private void HandleWheelVisibilityChanged(bool visible)
    {
        SetVisible(visible);
        RefreshSlots();
    }

    private void HandleWheelHoverChanged(PlayerEmoteType emoteType)
    {
        if (!isVisible)
            return;

        highlightedSlotIndex = FindSlotIndex(emoteType);
        RefreshSlots();
    }

    private void CacheComponents()
    {
        rectTransform = transform as RectTransform;
        canvas = GetComponentInParent<Canvas>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        fallbackFont = ResolveFallbackFont();
    }

    private void ConfigureRoot()
    {
        if (rectTransform == null)
            return;

        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = screenOffset;
        rectTransform.sizeDelta = Vector2.one * wheelSize;
    }

    private void EnsureBuilt()
    {
        CacheComponents();
        ConfigureRoot();

        if (centerGraphic == null)
            centerGraphic = CreateCenterGraphic();

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || slots[i].slice == null)
                slots[i] = CreateSlot(i);
        }
    }

    private EmoteWheelSliceGraphic CreateCenterGraphic()
    {
        GameObject centerObject = new GameObject("Center", typeof(RectTransform), typeof(EmoteWheelSliceGraphic));
        centerObject.transform.SetParent(transform, false);
        centerObject.transform.SetAsFirstSibling();

        RectTransform centerRect = centerObject.GetComponent<RectTransform>();
        StretchToParent(centerRect);

        EmoteWheelSliceGraphic graphic = centerObject.GetComponent<EmoteWheelSliceGraphic>();
        graphic.raycastTarget = false;
        graphic.Configure(0f, Mathf.Max(1f, innerRadius - 8f), 0f, 360f, centerColor);
        return graphic;
    }

    private EmoteSlotView CreateSlot(int slotIndex)
    {
        EmoteSlotView slot = new EmoteSlotView();
        GameObject slotObject = new GameObject($"Slot {slotIndex + 1}", typeof(RectTransform), typeof(EmoteWheelSliceGraphic));
        slotObject.transform.SetParent(transform, false);

        slot.root = slotObject.GetComponent<RectTransform>();
        StretchToParent(slot.root);

        slot.slice = slotObject.GetComponent<EmoteWheelSliceGraphic>();
        slot.slice.raycastTarget = false;

        GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(slotObject.transform, false);
        slot.icon = iconObject.GetComponent<Image>();
        slot.icon.raycastTarget = false;
        slot.icon.preserveAspect = true;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(slotObject.transform, false);
        slot.label = labelObject.GetComponent<Text>();
        slot.label.raycastTarget = false;
        slot.label.alignment = TextAnchor.MiddleCenter;
        slot.label.horizontalOverflow = HorizontalWrapMode.Wrap;
        slot.label.verticalOverflow = VerticalWrapMode.Truncate;
        slot.label.resizeTextForBestFit = true;
        slot.label.resizeTextMinSize = 10;
        slot.label.resizeTextMaxSize = 16;
        slot.label.fontSize = 14;
        slot.label.font = fallbackFont;

        return slot;
    }

    private void RefreshSlots()
    {
        EnsureBuilt();

        if (centerGraphic != null)
            centerGraphic.Configure(0f, Mathf.Max(1f, innerRadius - 8f), 0f, 360f, centerColor);

        for (int i = 0; i < slots.Length; i++)
        {
            EmoteSlotView slot = slots[i];
            EmoteWheelSlotConfig config = targetController != null ? targetController.GetEmoteWheelSlot(i) : null;
            bool hasEmote = config != null && config.HasEmote;
            bool selected = i == highlightedSlotIndex && hasEmote;
            Color fillColor = hasEmote ? (selected ? selectedColor : activeColor) : disabledColor;

            slot.slice.Configure(
                innerRadius,
                outerRadius,
                GetSliceStartAngle(i),
                GetSliceEndAngle(i),
                fillColor);

            Vector2 slotPosition = GetDirection(GetSliceCenterAngle(i)) * ((innerRadius + outerRadius) * 0.5f);
            ConfigureIcon(slot.icon, config, hasEmote, selected, slotPosition);
            ConfigureLabel(slot.label, config, hasEmote, selected, slotPosition);
        }
    }

    private void ConfigureIcon(Image icon, EmoteWheelSlotConfig config, bool hasEmote, bool selected, Vector2 anchoredPosition)
    {
        if (icon == null)
            return;

        RectTransform iconRect = icon.rectTransform;
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = anchoredPosition;
        iconRect.sizeDelta = Vector2.one * iconSize;

        Sprite sprite = config != null ? config.Icon : null;
        icon.enabled = hasEmote && sprite != null;
        icon.sprite = sprite;
        icon.color = selected ? Color.white : iconColor;
    }

    private void ConfigureLabel(Text label, EmoteWheelSlotConfig config, bool hasEmote, bool selected, Vector2 anchoredPosition)
    {
        if (label == null)
            return;

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = anchoredPosition;
        labelRect.sizeDelta = new Vector2(iconSize * 1.7f, iconSize * 0.72f);

        bool showLabel = hasEmote && (config == null || config.Icon == null);
        label.enabled = showLabel;
        label.text = showLabel && config != null ? config.Label : string.Empty;
        label.color = hasEmote ? (selected ? Color.white : labelColor) : disabledLabelColor;
    }

    private void UpdateMouseSelection()
    {
        if (targetController == null || !targetController.IsWheelOpen)
            return;

        int nextSlotIndex = ResolveSlotIndex(Input.mousePosition);
        if (nextSlotIndex == highlightedSlotIndex)
            return;

        highlightedSlotIndex = nextSlotIndex;
        PlayerEmoteType emoteType = GetEmoteTypeForSlot(nextSlotIndex);
        targetController.PreviewEmote(emoteType);
    }

    private int ResolveSlotIndex(Vector2 screenPosition)
    {
        if (rectTransform == null)
            return -1;

        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPosition, uiCamera, out Vector2 localPoint))
            return -1;

        if (localPoint.magnitude < minSelectionRadius)
            return -1;

        float angle = Mathf.Atan2(localPoint.y, localPoint.x) * Mathf.Rad2Deg;
        float sliceAngle = 360f / slots.Length;
        float normalizedAngle = Mathf.Repeat(90f - angle + sliceAngle * 0.5f, 360f);
        int slotIndex = Mathf.FloorToInt(normalizedAngle / sliceAngle);
        return Mathf.Clamp(slotIndex, 0, slots.Length - 1);
    }

    private PlayerEmoteType GetEmoteTypeForSlot(int slotIndex)
    {
        if (targetController == null || slotIndex < 0 || slotIndex >= slots.Length)
            return PlayerEmoteType.None;

        EmoteWheelSlotConfig config = targetController.GetEmoteWheelSlot(slotIndex);
        return config != null ? config.EmoteType : PlayerEmoteType.None;
    }

    private int FindSlotIndex(PlayerEmoteType emoteType)
    {
        if (targetController == null || emoteType == PlayerEmoteType.None)
            return -1;

        for (int i = 0; i < slots.Length; i++)
        {
            EmoteWheelSlotConfig config = targetController.GetEmoteWheelSlot(i);
            if (config != null && config.EmoteType == emoteType)
                return i;
        }

        return -1;
    }

    private void SetVisible(bool visible)
    {
        isVisible = visible;
        highlightedSlotIndex = visible ? highlightedSlotIndex : -1;

        if (canvasGroup == null)
            return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        if (visible)
            transform.SetAsLastSibling();
    }

    private float GetSliceCenterAngle(int slotIndex)
    {
        float sliceAngle = 360f / slots.Length;
        return 90f - slotIndex * sliceAngle;
    }

    private float GetSliceStartAngle(int slotIndex)
    {
        float sliceAngle = 360f / slots.Length;
        return GetSliceCenterAngle(slotIndex) - sliceAngle * 0.5f + segmentGapDegrees * 0.5f;
    }

    private float GetSliceEndAngle(int slotIndex)
    {
        float sliceAngle = 360f / slots.Length;
        return GetSliceCenterAngle(slotIndex) + sliceAngle * 0.5f - segmentGapDegrees * 0.5f;
    }

    private static Vector2 GetDirection(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    private static void StretchToParent(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static EmoteWheelUI CreateWheel(Transform parent)
    {
        GameObject wheelObject = new GameObject(WheelObjectName, typeof(RectTransform), typeof(CanvasGroup));
        wheelObject.transform.SetParent(parent, false);
        return wheelObject.AddComponent<EmoteWheelUI>();
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

    private static PlayerEmoteWheelController FindLocalController()
    {
        PlayerEmoteWheelController[] controllers = FindObjectsByType<PlayerEmoteWheelController>(FindObjectsInactive.Include);
        for (int i = 0; i < controllers.Length; i++)
        {
            PlayerEmoteWheelController controller = controllers[i];
            if (controller == null)
                continue;

            PhotonView view = controller.GetComponent<PhotonView>();
            if (view == null || view.IsMine)
                return controller;
        }

        return null;
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

    private static Font ResolveFallbackFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            return font;

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
