using UnityEngine;
using TMPro;
using UnityEngine.Audio;
using UnityEngine.UI;
using System.Collections.Generic;

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
