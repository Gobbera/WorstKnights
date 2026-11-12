using UnityEngine;
using TMPro;
using UnityEngine.Audio;
using UnityEngine.UI;
using System.Collections.Generic;
using System;

public class SettingsMenu : MonoBehaviour
{
    private const string VolumePlayerPrefsKey = "volume";
    private const string ResolutionPlayerPrefsKey = "resolution";
    private const string FullscreenPlayerPrefsKey = "fullscreen";
    private const float GeneratedRowSpacing = 55f;

    public AudioMixer mixer;
    public Slider volumeSlider;
    public Slider mouseSensitivitySlider;
    public TMP_Dropdown resolutionDropdown;
    public Toggle fullscreenToggle;

    private List<Resolution> resolutions = new List<Resolution>();

    void Start()
    {
        BuildResolutionDropdown();

        if (volumeSlider != null)
            volumeSlider.onValueChanged.AddListener(SetVolume);

        if (mouseSensitivitySlider == null)
            mouseSensitivitySlider = CreateMouseSensitivitySliderFromVolumeTemplate();

        if (mouseSensitivitySlider != null)
        {
            mouseSensitivitySlider.minValue = MouseLook.MinSensitivityMultiplier;
            mouseSensitivitySlider.maxValue = MouseLook.MaxSensitivityMultiplier;
            mouseSensitivitySlider.wholeNumbers = false;
            mouseSensitivitySlider.onValueChanged.AddListener(SetMouseSensitivity);
        }

        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(SetResolution);

        if (fullscreenToggle != null)
            fullscreenToggle.onValueChanged.AddListener(SetFullscreen);

        LoadSettings();
        VoiceSettingsPanel.EnsureForMenu(this);
        VoiceIntensitySettingsPanel.EnsureForMenu(this);
        RoomVoiceSettingsPanel.EnsureForMenu(this);
        SettingsTabsController.EnsureForMenu(this);
    }

    void BuildResolutionDropdown()
    {
        resolutions.Clear();
        List<string> options = new List<string>();

        foreach (Resolution res in Screen.resolutions)
        {
            if (resolutions.Exists(r => r.width == res.width && r.height == res.height)) continue;

            resolutions.Add(res);
            options.Add(res.width + " x " + res.height);
        }
        if (resolutionDropdown == null)
            return;

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(options);
    }
    
    void LoadSettings()
    {
        float volume = PlayerPrefs.GetFloat(VolumePlayerPrefsKey, 0f);
        SetVolume(volume);

        float mouseSensitivity = PlayerPrefs.GetFloat(
            MouseLook.MouseSensitivityPlayerPrefsKey,
            MouseLook.DefaultSensitivityMultiplier);
        SetMouseSensitivity(mouseSensitivity);

        bool fullscreen = PlayerPrefs.GetInt(FullscreenPlayerPrefsKey, Screen.fullScreen ? 1 : 0) == 1;
        SetFullscreen(fullscreen);

        int resolutionIndex = PlayerPrefs.GetInt(ResolutionPlayerPrefsKey, GetCurrentResolutionIndex());
        
        SetResolution(resolutionIndex);
    }

    public void SetVolume(float value)
    {
        if (mixer != null)
            mixer.SetFloat("volume", value);

        if (volumeSlider != null)
            volumeSlider.value = value;

        PlayerPrefs.SetFloat(VolumePlayerPrefsKey, value);
    }

    public void SetMouseSensitivity(float value)
    {
        float clampedValue = MouseLook.ClampSensitivityMultiplier(value);

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.value = clampedValue;

        PlayerPrefs.SetFloat(MouseLook.MouseSensitivityPlayerPrefsKey, clampedValue);
        MouseLook.instance?.SetSensitivity(clampedValue);
    }

    public void SetResolution(int index)
    {
        if (index < 0 || index >= resolutions.Count)
            return;

        Resolution res = resolutions[index];
        Screen.SetResolution(res.width, res.height, Screen.fullScreen);

        if (resolutionDropdown != null)
            resolutionDropdown.value = index;

        PlayerPrefs.SetInt(ResolutionPlayerPrefsKey, index);
    }

    public void SetFullscreen(bool value)
    {
        Screen.fullScreen = value;

        if (fullscreenToggle != null)
            fullscreenToggle.isOn = value;

        PlayerPrefs.SetInt(FullscreenPlayerPrefsKey, value ? 1 : 0);
    }

    private int GetCurrentResolutionIndex()
    {
        if (resolutions == null || resolutions.Count == 0)
            return 0;

        for (int i = 0; i < resolutions.Count; i++)
        {
            Resolution resolution = resolutions[i];
            if (resolution.width == Screen.width && resolution.height == Screen.height)
                return i;
        }

        return Mathf.Clamp(resolutionDropdown != null ? resolutionDropdown.value : 0, 0, resolutions.Count - 1);
    }

    private Slider CreateMouseSensitivitySliderFromVolumeTemplate()
    {
        if (volumeSlider == null || volumeSlider.transform.parent == null)
            return null;

        Transform sourceRow = volumeSlider.transform.parent;
        Transform parent = sourceRow.parent;
        if (parent == null)
            return null;

        Transform existingRow = parent.Find("TXT_Mouse Sensitivity");
        if (existingRow != null && existingRow.TryGetComponent(out TMP_Text existingLabel))
        {
            existingLabel.text = "Mouse Sensitivity";
            return existingRow.GetComponentInChildren<Slider>(true);
        }

        GameObject row = Instantiate(sourceRow.gameObject, parent);
        row.name = "TXT_Mouse Sensitivity";

        TMP_Text label = row.GetComponent<TMP_Text>();
        if (label != null)
            label.text = "Mouse Sensitivity";

        RectTransform sourceRect = sourceRow as RectTransform;
        RectTransform rowRect = row.transform as RectTransform;
        if (sourceRect != null && rowRect != null)
        {
            rowRect.anchoredPosition = sourceRect.anchoredPosition + Vector2.down * GeneratedRowSpacing;
            MoveRowsBelow(parent, sourceRect.anchoredPosition.y, rowRect);
        }

        Slider slider = row.GetComponentInChildren<Slider>(true);
        if (slider != null)
        {
            slider.name = "Slider_Mouse Sensitivity";
            slider.onValueChanged.RemoveAllListeners();
        }

        return slider;
    }

    private void MoveRowsBelow(Transform parent, float sourceY, RectTransform generatedRow)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            RectTransform row = parent.GetChild(i) as RectTransform;
            if (row == null || row == generatedRow)
                continue;

            if (row.anchoredPosition.y >= sourceY)
                continue;

            row.anchoredPosition += Vector2.down * GeneratedRowSpacing;
        }
    }
}

public enum SettingsTab
{
    Controls,
    Video,
    Audio,
    Voip
}

[DisallowMultipleComponent]
public sealed class SettingsTabsController : MonoBehaviour
{
    private const string TabBarName = "Settings Tabs";
    private const float ContentStartY = -227f;
    private const float RowSpacing = 55f;
    private static readonly string[] TabLabels = { "Controls", "Video", "Audio", "VoIP" };

    private sealed class TabEntry
    {
        public GameObject Root;
        public SettingsTab Tab;
        public int Order;
        public float X;
    }

    private readonly List<TabEntry> entries = new List<TabEntry>();
    private readonly List<Button> tabButtons = new List<Button>();
    private SettingsMenu menu;
    private RectTransform rows;
    private RectTransform tabBar;
    private SettingsTab activeTab = SettingsTab.Controls;

    public SettingsTab ActiveTab => activeTab;
    public int TabCount => tabButtons.Count;

    public static SettingsTabsController EnsureForMenu(SettingsMenu settingsMenu)
    {
        if (settingsMenu == null) return null;
        SettingsTabsController controller = settingsMenu.GetComponent<SettingsTabsController>();
        if (controller == null) controller = settingsMenu.gameObject.AddComponent<SettingsTabsController>();
        controller.Initialize(settingsMenu);
        return controller;
    }

    public void SelectTab(SettingsTab tab)
    {
        activeTab = tab;
        ApplyTab();
    }

    private void Initialize(SettingsMenu settingsMenu)
    {
        menu = settingsMenu;
        rows = menu.resolutionDropdown != null
            ? menu.resolutionDropdown.transform.parent.parent as RectTransform
            : menu.transform as RectTransform;
        if (rows == null) return;
        EnsureTabBar();
        RefreshEntries();
        ApplyTab();
    }

    private void EnsureTabBar()
    {
        Transform existing = rows.Find(TabBarName);
        if (existing != null) tabBar = existing as RectTransform;
        if (tabBar == null)
        {
            var root = new GameObject(TabBarName, typeof(RectTransform));
            root.layer = rows.gameObject.layer;
            root.transform.SetParent(rows, false);
            tabBar = (RectTransform)root.transform;
            tabBar.anchorMin = tabBar.anchorMax = new Vector2(0.5f, 1f);
            tabBar.pivot = new Vector2(0.5f, 0.5f);
            tabBar.anchoredPosition = new Vector2(0f, -164f);
            tabBar.sizeDelta = new Vector2(680f, 44f);
        }

        if (tabButtons.Count > 0) return;
        for (int i = 0; i < TabLabels.Length; i++)
        {
            SettingsTab tab = (SettingsTab)i;
            Button button = CreateTabButton(TabLabels[i], i);
            button.onClick.AddListener(() => SelectTab(tab));
            tabButtons.Add(button);
        }
    }

    private Button CreateTabButton(string label, int index)
    {
        var root = new GameObject($"Tab {label}", typeof(RectTransform), typeof(Image), typeof(Button));
        root.layer = tabBar.gameObject.layer;
        root.transform.SetParent(tabBar, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(157f, 40f);
        rect.anchoredPosition = new Vector2(-250.5f + index * 167f, 0f);

        Image image = root.GetComponent<Image>();
        image.color = new Color(0.12f, 0.14f, 0.17f, 0.94f);
        Button button = root.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.78f, 0.84f, 0.9f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.layer = root.layer;
        textObject.transform.SetParent(root.transform, false);
        var textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 2f);
        textRect.offsetMax = new Vector2(-6f, -2f);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.enableAutoSizing = true;
        text.fontSize = 23f;
        text.fontSizeMin = 15f;
        text.fontSizeMax = 23f;
        if (menu.resolutionDropdown != null && menu.resolutionDropdown.captionText != null)
            text.font = menu.resolutionDropdown.captionText.font;
        return button;
    }

    private void RefreshEntries()
    {
        entries.Clear();
        Register(menu.mouseSensitivitySlider != null ? menu.mouseSensitivitySlider.transform.parent.gameObject : null,
            SettingsTab.Controls, 0);
        Register(menu.resolutionDropdown != null ? menu.resolutionDropdown.transform.parent.gameObject : null,
            SettingsTab.Video, 0);
        Register(menu.fullscreenToggle != null ? menu.fullscreenToggle.transform.parent.gameObject : null,
            SettingsTab.Video, 1);
        Register(menu.volumeSlider != null ? menu.volumeSlider.transform.parent.gameObject : null,
            SettingsTab.Audio, 0);

        Register(FindDirectRow("TXT_Voice Mode"), SettingsTab.Voip, 0);
        Register(FindDirectRow("TXT_Push-to-Talk Key"), SettingsTab.Voip, 1);
        Register(FindDirectRow("TXT_Microphone"), SettingsTab.Voip, 2);
        Register(FindDirectRow("TXT_Microphone Test"), SettingsTab.Voip, 3);
        RoomVoiceSettingsPanel roomVoice = menu.GetComponentInChildren<RoomVoiceSettingsPanel>(true);
        Register(roomVoice != null ? roomVoice.gameObject : null, SettingsTab.Voip, 4);
        entries.Sort((left, right) => left.Tab != right.Tab
            ? left.Tab.CompareTo(right.Tab) : left.Order.CompareTo(right.Order));
    }

    private GameObject FindDirectRow(string rowName)
    {
        Transform row = rows != null ? rows.Find(rowName) : null;
        return row != null ? row.gameObject : null;
    }

    private void Register(GameObject root, SettingsTab tab, int order)
    {
        if (root == null || entries.Exists(entry => entry.Root == root)) return;
        RectTransform rect = root.transform as RectTransform;
        entries.Add(new TabEntry
        {
            Root = root,
            Tab = tab,
            Order = order,
            X = rect != null ? rect.anchoredPosition.x : 0f
        });
    }

    private void ApplyTab()
    {
        if (menu == null || rows == null) return;
        if (activeTab != SettingsTab.Voip)
        {
            VoiceIntensitySettingsPanel.CloseForMenu(menu.gameObject);
            VoiceSettingsPanel.CancelRebindingForMenu(menu.gameObject);
        }

        float y = ContentStartY;
        for (int i = 0; i < entries.Count; i++)
        {
            TabEntry entry = entries[i];
            if (entry.Root == null) continue;
            bool visible = entry.Tab == activeTab;
            entry.Root.SetActive(visible);
            if (!visible || !(entry.Root.transform is RectTransform rect)) continue;
            rect.anchoredPosition = new Vector2(entry.X, y);
            y -= RowSpacing;
        }

        for (int i = 0; i < tabButtons.Count; i++)
        {
            bool selected = i == (int)activeTab;
            Image image = tabButtons[i] != null ? tabButtons[i].targetGraphic as Image : null;
            if (image != null) image.color = selected
                ? new Color(0.26f, 0.5f, 0.72f, 1f)
                : new Color(0.12f, 0.14f, 0.17f, 0.94f);
            if (tabButtons[i] != null) tabButtons[i].interactable = true;
        }
    }
}
