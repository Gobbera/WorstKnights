using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-1100)]
public sealed class VoiceSettingsPanel : MonoBehaviour
{
    private const string ModeRowName = "TXT_Voice Mode";
    private const string BindingRowName = "TXT_Push-to-Talk Key";
    private const float RowSpacing = 55f;
    private static VoiceSettingsPanel activeCapture;
    private static int lastCaptureInputFrame = -1;

    [SerializeField] private TMP_Dropdown modeDropdown;
    [SerializeField] private Button bindingButton;
    [SerializeField] private TMP_Text bindingLabel;

    private bool eventsBound;
    private bool capturing;
    private bool captureArmed;
    private float captureDeadline;

    public static bool IsAnyRebinding => activeCapture != null && activeCapture.capturing;
    public static bool BlocksMenuToggleThisFrame => IsAnyRebinding || lastCaptureInputFrame == Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        activeCapture = null;
        lastCaptureInputFrame = -1;
    }

    public static VoiceSettingsPanel EnsureForMenu(SettingsMenu menu)
    {
        if (menu == null) return null;
        var panel = menu.GetComponent<VoiceSettingsPanel>();
        if (panel == null) panel = menu.gameObject.AddComponent<VoiceSettingsPanel>();
        panel.EnsureControls(menu);
        panel.BindEvents();
        panel.Refresh();
        return panel;
    }

    private void OnEnable()
    {
        EnsureControls(GetComponent<SettingsMenu>());
        BindEvents();
        VoiceTransmissionPreferences.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        CancelRebinding();
        VoiceTransmissionPreferences.Changed -= Refresh;
    }

    private void OnDestroy()
    {
        if (eventsBound)
        {
            if (modeDropdown != null) modeDropdown.onValueChanged.RemoveListener(SetMode);
            if (bindingButton != null) bindingButton.onClick.RemoveListener(BeginRebinding);
        }
    }

    private void Update()
    {
        if (!capturing) return;
        var overlay = GetComponentInParent<InGameSettingsMenuController>();
        if ((overlay != null && !overlay.IsOpen) || Time.unscaledTime >= captureDeadline)
        {
            CancelRebinding();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            CancelRebinding();
            return;
        }
        if (!captureArmed)
        {
            captureArmed = !keyboard.anyKey.isPressed;
            return;
        }

        foreach (var key in keyboard.allKeys)
            if (key.wasPressedThisFrame && VoiceTransmissionPreferences.IsValidBinding(key.keyCode))
            {
                CompleteRebinding(key.keyCode);
                return;
            }
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CancelRebinding();
    }

    private void BindEvents()
    {
        if (eventsBound || modeDropdown == null || bindingButton == null) return;
        modeDropdown.onValueChanged.AddListener(SetMode);
        bindingButton.onClick.AddListener(BeginRebinding);
        eventsBound = true;
    }

    private void SetMode(int index)
    {
        CancelRebinding();
        VoiceTransmissionPreferences.SetMode(index == 1 ? VoiceTransmissionMode.PushToTalk : VoiceTransmissionMode.OpenMicrophone);
        Refresh();
    }

    public void BeginRebinding()
    {
        if (VoiceTransmissionPreferences.Mode != VoiceTransmissionMode.PushToTalk) return;
        if (activeCapture != null) activeCapture.CancelRebinding();
        activeCapture = this;
        capturing = true;
        captureArmed = false;
        captureDeadline = Time.unscaledTime + 10f;
        VivoxVoiceManager.Instance?.SuspendVoiceTransmission();
        Refresh();
    }

    private void CompleteRebinding(Key key)
    {
        CancelRebinding();
        VoiceTransmissionPreferences.SetPushToTalkKey(key);
        Refresh();
    }

    public void CancelRebinding()
    {
        if (!capturing) return;
        capturing = false;
        captureArmed = false;
        lastCaptureInputFrame = Time.frameCount;
        if (activeCapture == this) activeCapture = null;
        VivoxVoiceManager.Instance?.SuspendVoiceTransmission();
        Refresh();
    }

    public static void CancelRebindingForMenu(GameObject menuRoot)
    {
        if (activeCapture != null && menuRoot != null
            && (activeCapture.gameObject == menuRoot || activeCapture.transform.IsChildOf(menuRoot.transform)))
            activeCapture.CancelRebinding();
    }

    private void Refresh()
    {
        if (modeDropdown != null)
        {
            modeDropdown.captionText.textWrappingMode = TextWrappingModes.NoWrap;
            modeDropdown.SetValueWithoutNotify((int)VoiceTransmissionPreferences.Mode);
        }
        if (bindingButton != null)
            bindingButton.interactable = VoiceTransmissionPreferences.Mode == VoiceTransmissionMode.PushToTalk;
        if (bindingLabel != null)
            bindingLabel.text = capturing ? "Press key (Esc)" : VoiceTransmissionPreferences.BindingDisplayName;
    }

    private void EnsureControls(SettingsMenu menu)
    {
        if (menu == null || menu.resolutionDropdown == null) return;
        Transform template = menu.resolutionDropdown.transform.parent;
        Transform rows = template.parent;
        if (rows == null) return;
        Transform modeRow = rows.Find(ModeRowName);
        if (modeDropdown == null)
        {
            if (modeRow == null) modeRow = CreateRow(template, rows, ModeRowName, "Voice Mode");
            modeDropdown = modeRow.GetComponentInChildren<TMP_Dropdown>(true);
            modeDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            modeDropdown.ClearOptions();
            modeDropdown.AddOptions(new System.Collections.Generic.List<string> { "Open Microphone", "Push to Talk" });
            modeDropdown.captionText.fontSize = 24f;
            modeDropdown.captionText.enableAutoSizing = true;
            modeDropdown.captionText.fontSizeMin = 16f;
            modeDropdown.captionText.fontSizeMax = 24f;
            modeDropdown.itemText.fontSize = 24f;
        }

        if (bindingButton != null) return;
        Transform bindingRow = rows.Find(BindingRowName);
        if (bindingRow == null) bindingRow = CreateRow(template, rows, BindingRowName, "Push-to-Talk Key");
        bindingButton = bindingRow.GetComponentInChildren<Button>(true);
        if (bindingButton != null)
        {
            bindingLabel = bindingButton.GetComponentInChildren<TMP_Text>(true);
            return;
        }

        TMP_Dropdown oldDropdown = bindingRow.GetComponentInChildren<TMP_Dropdown>(true);
        var buttonRoot = new GameObject("Push-to-Talk Binding", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonRoot.layer = oldDropdown.gameObject.layer;
        buttonRoot.transform.SetParent(bindingRow, false);
        var buttonRect = (RectTransform)buttonRoot.transform;
        var oldRect = (RectTransform)oldDropdown.transform;
        buttonRect.anchorMin = oldRect.anchorMin; buttonRect.anchorMax = oldRect.anchorMax;
        buttonRect.pivot = oldRect.pivot; buttonRect.anchoredPosition = oldRect.anchoredPosition;
        buttonRect.sizeDelta = oldRect.sizeDelta; buttonRect.localScale = oldRect.localScale;
        var image = buttonRoot.GetComponent<Image>();
        if (oldDropdown.targetGraphic is Image oldImage)
        {
            image.sprite = oldImage.sprite; image.type = oldImage.type; image.color = oldImage.color;
        }
        bindingButton = buttonRoot.GetComponent<Button>();
        bindingButton.targetGraphic = image;
        bindingButton.colors = oldDropdown.colors;
        bindingLabel = Instantiate(oldDropdown.captionText, buttonRoot.transform, false);
        bindingLabel.name = "Binding Label";
        bindingLabel.raycastTarget = false;
        bindingLabel.alignment = TextAlignmentOptions.Center;
        bindingLabel.enableAutoSizing = true;
        bindingLabel.fontSize = 24f;
        bindingLabel.fontSizeMin = 16f;
        bindingLabel.fontSizeMax = 24f;
        var labelRect = (RectTransform)bindingLabel.transform;
        labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 2f); labelRect.offsetMax = new Vector2(-8f, -2f);
        oldDropdown.gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(oldDropdown.gameObject);
        else DestroyImmediate(oldDropdown.gameObject);
    }

    private static Transform CreateRow(Transform template, Transform parent, string name, string label)
    {
        float lowest = ((RectTransform)template).anchoredPosition.y;
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i) is RectTransform row) lowest = Mathf.Min(lowest, row.anchoredPosition.y);
        Transform created = Instantiate(template.gameObject, parent).transform;
        created.name = name;
        created.GetComponent<TMP_Text>().text = label;
        RectTransform rect = (RectTransform)created;
        rect.anchoredPosition = new Vector2(((RectTransform)template).anchoredPosition.x, lowest - RowSpacing);
        return created;
    }
}
