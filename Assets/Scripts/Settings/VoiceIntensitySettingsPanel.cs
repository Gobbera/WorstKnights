using TMPro;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-1200)]
public sealed class VoiceIntensitySettingsPanel : MonoBehaviour
{
    private const string ResourcePath = "UI/VoiceIntensitySettingsPanel";
    private static VoiceIntensitySettingsPanel activePanel;
    private static int closedFrame = -1;
    [SerializeField] private RectTransform openRow;
    [SerializeField] private Button openButton;
    [SerializeField] private GameObject dialog;
    [SerializeField] private Slider meter;
    [SerializeField] private Image meterFill;
    [SerializeField] private TMP_Text levelLabel;
    [SerializeField] private TMP_Text inputLabel;
    [SerializeField] private Button automaticGainButton;
    [SerializeField] private TMP_Text automaticGainLabel;
    [SerializeField] private TMP_Text profileLabel;
    [SerializeField] private Button monitorButton;
    [SerializeField] private TMP_Text monitorLabel;
    [SerializeField] private TMP_Text monitorHelp;
    [SerializeField] private TMP_Text rangeLabel;
    [SerializeField] private TMP_Text instruction;
    [SerializeField] private Slider progress;
    [SerializeField] private Button measureButton;
    [SerializeField] private TMP_Text measureLabel;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button closeButton;
    private RectTransform microphoneRow;
    private TMP_Dropdown microphoneDropdown;
    private readonly List<string> microphoneDeviceIds = new List<string>();
    private VivoxVoiceManager microphoneManager;
    private int microphoneRevision = -1;
    private bool suppressMicrophoneSelection;
    private bool eventsBound;
    private SettingsMenu owner;

    public static bool BlocksMenuToggleThisFrame => activePanel != null || closedFrame == Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { activePanel = null; closedFrame = -1; }

    public static VoiceIntensitySettingsPanel EnsureForMenu(SettingsMenu menu)
    {
        var panel = menu.GetComponentInChildren<VoiceIntensitySettingsPanel>(true);
        if (panel == null)
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null) { Debug.LogWarning($"VoiceIntensitySettingsPanel: missing Resources/{ResourcePath}."); return null; }
            panel = Instantiate(prefab, menu.transform, false).GetComponent<VoiceIntensitySettingsPanel>();
        }
        panel.owner = menu;
        Transform rows = menu.resolutionDropdown != null ? menu.resolutionDropdown.transform.parent.parent : menu.transform;
        float lowest = -502f;
        foreach (Transform child in rows)
            if (child != panel.openRow && child is RectTransform row && child.name.StartsWith("TXT_"))
                lowest = Mathf.Min(lowest, row.anchoredPosition.y);
        panel.EnsureMicrophoneRow(menu, rows, lowest - 55f);
        if (panel.microphoneRow != null) lowest = Mathf.Min(lowest, panel.microphoneRow.anchoredPosition.y);
        panel.openRow.SetParent(rows, false);
        panel.openRow.anchoredPosition = new Vector2(-204.5f, lowest - 55f);
        panel.Bind();
        panel.RefreshMicrophoneDropdown(force: true);
        return panel;
    }

    private void OnEnable() { Bind(); dialog.SetActive(false); RefreshMicrophoneDropdown(force: true); }
    private void OnDisable() { Close(); }

    private void Bind()
    {
        if (eventsBound) return;
        profileLabel.enableAutoSizing = true;
        profileLabel.fontSizeMin = 16f;
        profileLabel.fontSizeMax = 19f;
        openButton.onClick.AddListener(Open);
        measureButton.onClick.AddListener(Measure);
        cancelButton.onClick.AddListener(Cancel);
        resetButton.onClick.AddListener(ResetProfile);
        closeButton.onClick.AddListener(Close);
        if (automaticGainButton != null) automaticGainButton.onClick.AddListener(ToggleAutomaticGain);
        if (monitorButton != null) monitorButton.onClick.AddListener(ToggleMonitoring);
        if (microphoneDropdown != null) microphoneDropdown.onValueChanged.AddListener(SelectMicrophone);
        eventsBound = true;
    }

    private void OnDestroy()
    {
        if (activePanel == this) activePanel = null;
        if (openRow != null && !openRow.IsChildOf(transform))
        {
            if (Application.isPlaying) Destroy(openRow.gameObject); else DestroyImmediate(openRow.gameObject);
        }
        if (microphoneRow != null && !microphoneRow.IsChildOf(transform))
        {
            if (Application.isPlaying) Destroy(microphoneRow.gameObject); else DestroyImmediate(microphoneRow.gameObject);
        }
    }

    public void Open()
    {
        if (activePanel != null && activePanel != this) activePanel.Close();
        if (owner != null) VoiceSettingsPanel.CancelRebindingForMenu(owner.gameObject);
        activePanel = this;
        VivoxVoiceManager.Instance?.SetMicrophoneTestOpen(true);
        transform.SetAsLastSibling(); dialog.SetActive(true); Refresh();
    }

    public void Close()
    {
        if (activePanel != this && (dialog == null || !dialog.activeSelf)) return;
        VivoxVoiceManager.Instance?.CancelVoiceCalibration();
        VivoxVoiceManager.Instance?.SetMicrophoneTestOpen(false);
        if (dialog != null) dialog.SetActive(false);
        if (activePanel == this) activePanel = null;
        closedFrame = Time.frameCount;
    }

    public static void CloseForMenu(GameObject menu)
    {
        if (activePanel != null && menu != null && activePanel.transform.IsChildOf(menu.transform)) activePanel.Close();
    }

    private void Update()
    {
        RefreshMicrophoneDropdown();
        if (activePanel != this) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
        Refresh();
    }

    private void EnsureMicrophoneRow(SettingsMenu menu, Transform rows, float y)
    {
        if (microphoneRow != null && microphoneDropdown != null) return;
        Transform existing = rows.Find("TXT_Microphone");
        GameObject rowObject;
        if (existing != null) rowObject = existing.gameObject;
        else
        {
            Transform sourceRow = menu.resolutionDropdown != null ? menu.resolutionDropdown.transform.parent : null;
            if (sourceRow == null) return;
            rowObject = Instantiate(sourceRow.gameObject, rows);
            rowObject.name = "TXT_Microphone";
        }

        microphoneRow = rowObject.transform as RectTransform;
        if (microphoneRow != null) microphoneRow.anchoredPosition = new Vector2(-204.5f, y);
        TMP_Text label = rowObject.GetComponent<TMP_Text>();
        if (label != null) label.text = "Microphone";
        microphoneDropdown = rowObject.GetComponentInChildren<TMP_Dropdown>(true);
        if (microphoneDropdown == null) return;
        microphoneDropdown.name = "Dropdown_Microphone";
        microphoneDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        if (eventsBound) microphoneDropdown.onValueChanged.AddListener(SelectMicrophone);
        RectTransform dropdownRect = microphoneDropdown.transform as RectTransform;
        if (dropdownRect != null) dropdownRect.sizeDelta = new Vector2(320f, dropdownRect.sizeDelta.y);
        if (microphoneDropdown.captionText != null)
        {
            microphoneDropdown.captionText.enableAutoSizing = true;
            microphoneDropdown.captionText.fontSizeMin = 12f;
            microphoneDropdown.captionText.fontSizeMax = 20f;
            microphoneDropdown.captionText.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private async void SelectMicrophone(int index)
    {
        if (suppressMicrophoneSelection || index < 0 || index >= microphoneDeviceIds.Count) return;
        VivoxVoiceManager manager = VivoxVoiceManager.Instance;
        if (manager == null) return;
        microphoneDropdown.interactable = false;
        await manager.SetInputDeviceAsync(microphoneDeviceIds[index]);
        RefreshMicrophoneDropdown(force: true);
    }

    private void RefreshMicrophoneDropdown(bool force = false)
    {
        if (microphoneDropdown == null) return;
        VivoxVoiceManager manager = VivoxVoiceManager.Instance;
        int revision = manager != null ? manager.InputDeviceRevision : -1;
        if (!force && manager == microphoneManager && revision == microphoneRevision) return;
        microphoneManager = manager;
        microphoneRevision = revision;
        suppressMicrophoneSelection = true;
        microphoneDeviceIds.Clear();
        microphoneDropdown.ClearOptions();

        var labels = new List<string>();
        if (manager == null || manager.InputDevices.Count == 0)
        {
            labels.Add(manager != null && manager.IsVoiceConnected ? "Detecting microphones..." : "Voice unavailable");
            microphoneDropdown.AddOptions(labels);
            microphoneDropdown.SetValueWithoutNotify(0);
            microphoneDropdown.interactable = false;
            suppressMicrophoneSelection = false;
            return;
        }

        int selected = 0;
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < manager.InputDevices.Count; i++)
        {
            string name = manager.InputDevices[i].DisplayName;
            nameCounts.TryGetValue(name, out int count);
            nameCounts[name] = count + 1;
        }
        var nameOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < manager.InputDevices.Count; i++)
        {
            VoiceInputDeviceOption option = manager.InputDevices[i];
            microphoneDeviceIds.Add(option.DeviceId);
            nameOccurrences.TryGetValue(option.DisplayName, out int occurrence);
            occurrence++;
            nameOccurrences[option.DisplayName] = occurrence;
            labels.Add(nameCounts[option.DisplayName] > 1
                ? $"{option.DisplayName} ({occurrence})" : option.DisplayName);
            if (string.Equals(option.DeviceId, manager.ActiveInputDeviceId, StringComparison.Ordinal)) selected = i;
        }
        microphoneDropdown.AddOptions(labels);
        microphoneDropdown.SetValueWithoutNotify(selected);
        microphoneDropdown.RefreshShownValue();
        microphoneDropdown.interactable = manager.CanSelectInputDevice;
        suppressMicrophoneSelection = false;
    }

    private void Measure()
    {
        var manager = VivoxVoiceManager.Instance;
        if (manager == null) return;
        var calibration = manager.LocalVoiceIntensity.Calibration;
        if (calibration.Stage >= VoiceCalibrationStage.Quiet && calibration.Stage <= VoiceCalibrationStage.Loud)
            manager.ContinueVoiceCalibration();
        else manager.BeginVoiceCalibration();
        Refresh();
    }

    private void Cancel() { VivoxVoiceManager.Instance?.CancelVoiceCalibration(); Refresh(); }
    private void ResetProfile() { VivoxVoiceManager.Instance?.ResetVoiceCalibration(); Refresh(); }
    private void ToggleAutomaticGain()
    {
        var manager = VivoxVoiceManager.Instance;
        if (manager != null && manager.InputAutomaticGain.HasValue)
            manager.SetAutomaticInputLevel(!manager.InputAutomaticGain.Value);
        Refresh();
    }

    private void ToggleMonitoring()
    {
        var manager = VivoxVoiceManager.Instance;
        if (manager != null) manager.SetMicrophoneMonitoring(!manager.MicrophoneMonitorRequested);
        Refresh();
    }

    private void Refresh()
    {
        var manager = VivoxVoiceManager.Instance;
        if (activePanel == this) manager?.SetMicrophoneTestOpen(true);
        bool connected = manager != null && manager.IsVoiceConnected;
        string microphone = manager?.InputMicrophoneName ?? "System microphone";
        if (manager != null && !string.IsNullOrEmpty(manager.InputDeviceError))
            microphone += $" — {manager.InputDeviceError}";
        Present(manager?.LocalVoiceIntensity, connected, microphone,
            manager?.InputAutomaticGain);
        RefreshTestOptions(manager, connected);
    }

    private void RefreshTestOptions(VivoxVoiceManager manager, bool connected)
    {
        bool monitoring = manager != null && manager.MicrophoneMonitorRequested;
        if (monitorButton != null) monitorButton.interactable = manager != null && manager.CanMonitorMicrophone;
        if (monitorLabel != null) monitorLabel.text = monitoring ? "Hear myself: On" : "Hear myself: Off";
        if (monitorHelp != null) monitorHelp.text = manager != null && !string.IsNullOrEmpty(manager.MicrophoneMonitorError)
            ? manager.MicrophoneMonitorError : "Local microphone playback. Use headphones to avoid feedback.";
        if (rangeLabel == null) return;
        rangeLabel.text = !connected ? "Voice range: Join a room to test"
            : manager.UsesDynamicVoiceRange ? $"Voice range limit: {manager.LocalVoiceRangeLimit:0.0} m"
            : manager.SupportsDynamicVoiceRange ? $"Voice range: {manager.LocalVoiceRangeLimit:0.0} m (calibrate for loud speech)"
            : "Voice range: Fixed in this mode";
    }

    private void Present(VoiceIntensityMonitor monitor, bool connected, string microphone, bool? automaticGain)
    {
        var calibration = monitor?.Calibration;
        bool calibrating = calibration != null && calibration.Stage >= VoiceCalibrationStage.Silence
            && calibration.Stage <= VoiceCalibrationStage.Loud;
        bool classified = monitor != null && monitor.Profile.IsCalibrated && !calibrating;
        float value = monitor == null ? 0f : classified ? monitor.Intensity : monitor.InputLevel;
        meter.SetValueWithoutNotify(value);
        VoiceIntensityBand band = monitor?.Band ?? VoiceIntensityBand.Silent;
        levelLabel.text = !connected ? "Unavailable" : monitor == null || !monitor.InputAvailable
            ? "Input closed" : classified ? $"Calibrated intensity: {band}  {Mathf.RoundToInt(value * 100f)}%"
            : $"Input level  {Mathf.RoundToInt(value * 100f)}%";
        meterFill.color = classified && band == VoiceIntensityBand.Loud ? new Color(1f, 0.7f, 0.3f) : new Color(0.45f, 1f, 0.65f);
        string automatic = automaticGain.HasValue ? automaticGain.Value ? "On" : "Off" : "Unavailable";
        inputLabel.text = microphone;
        if (automaticGainLabel != null) automaticGainLabel.text = $"Automatic input level: {automatic}";
        if (automaticGainButton != null) automaticGainButton.interactable = connected && automaticGain.HasValue;
        if (calibrating)
            profileLabel.text = $"Measured input: Room {Measured(calibration, VoiceCalibrationStage.Silence)} | Quiet {Measured(calibration, VoiceCalibrationStage.Quiet)} | Normal {Measured(calibration, VoiceCalibrationStage.Normal)} | Loud {Measured(calibration, VoiceCalibrationStage.Loud)}";
        else if (monitor != null && monitor.Profile.IsCalibrated)
            profileLabel.text = $"Saved input: Room {FormatLevel(monitor.Profile.Noise)} | Quiet {FormatLevel(monitor.Profile.Quiet)} | Normal {FormatLevel(monitor.Profile.Normal)} | Loud {FormatLevel(monitor.Profile.Loud)}";
        else profileLabel.text = "Calibrate to identify quiet, normal and louder speech";
        bool sampling = calibration != null && calibration.IsSampling;
        measureButton.interactable = connected && !sampling;
        cancelButton.interactable = calibration != null && calibration.Stage >= VoiceCalibrationStage.Silence
            && calibration.Stage <= VoiceCalibrationStage.Loud;
        resetButton.interactable = connected && monitor != null && monitor.Profile.IsCalibrated;
        progress.SetValueWithoutNotify(calibration?.Progress ?? 0f);
        measureLabel.text = sampling ? "Measuring..." : calibration != null && calibration.Stage >= VoiceCalibrationStage.Quiet
            && calibration.Stage <= VoiceCalibrationStage.Loud ? "Measure next step" : "Calibrate";
        if (!connected) { instruction.text = "Join a room with voice connected to test your microphone."; return; }
        string ptt = VoiceTransmissionPreferences.Mode == VoiceTransmissionMode.PushToTalk
            ? $"Hold {VoiceTransmissionPreferences.BindingDisplayName} while measuring. " : string.Empty;
        string step;
        switch (calibration.Stage)
        {
            case VoiceCalibrationStage.Silence: step = "Stay silent while the room noise is measured."; break;
            case VoiceCalibrationStage.Quiet: step = "Speak quietly and steadily at your usual microphone distance."; break;
            case VoiceCalibrationStage.Normal: step = "Speak normally and steadily at your usual microphone distance."; break;
            case VoiceCalibrationStage.Loud: step = "Speak at a comfortably louder level. Keep the same microphone distance."; break;
            default: step = string.IsNullOrEmpty(calibration.Message)
                ? "Start with silence, then measure quiet, normal and louder speech. Your voice follows the current microphone mode."
                : calibration.Message; break;
        }
        instruction.text = ptt + step + (sampling ? " Collecting about 3 seconds of clear samples." : string.Empty);
        if (calibration.Stage == VoiceCalibrationStage.Failed && monitor.Profile.IsCalibrated)
            instruction.text += " The previous calibration is still active.";
    }

    private static string Measured(VoiceIntensityCalibrator calibration, VoiceCalibrationStage stage)
    {
        float? level = calibration.MeasuredLevel(stage);
        return level.HasValue ? FormatLevel(level.Value) : "--";
    }

    private static string FormatLevel(float level)
        => (level * 100f).ToString("G3", CultureInfo.InvariantCulture) + "%";
}
