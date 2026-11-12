using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;

public readonly struct VoiceInputDeviceOption
{
    public string DeviceId { get; }
    public string DisplayName { get; }

    public VoiceInputDeviceOption(string deviceId, string displayName)
    {
        DeviceId = deviceId ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Unknown microphone" : displayName;
    }
}

public sealed partial class VivoxVoiceManager
{
    public const ParticipantPropertyUpdateFrequency IntensityUpdateFrequency = ParticipantPropertyUpdateFrequency.TenPerSecond;
    private readonly VoiceIntensityMonitor intensityMonitor = new VoiceIntensityMonitor();
    private string intensityProfileKey;
    private float nextIntensityEnvironmentCheck;
    private int savedIntensityRevision;
    private const string AutomaticInputLevelKey = "voice.input.automaticLevel.v1";
    private const string InputDevicePlayerPrefsKey = "voice.input.deviceId.v1";
    private readonly List<VoiceInputDeviceOption> inputDevices = new List<VoiceInputDeviceOption>();
    private bool inputDeviceSwitching;
    private bool applyingSavedInputDevice;

    public VoiceIntensityMonitor LocalVoiceIntensity => intensityMonitor;
    public string InputMicrophoneName { get; private set; } = "System microphone";
    public bool? InputAutomaticGain { get; private set; }
    public IReadOnlyList<VoiceInputDeviceOption> InputDevices => inputDevices;
    public string ActiveInputDeviceId { get; private set; } = string.Empty;
    public string InputDeviceError { get; private set; } = string.Empty;
    public int InputDeviceRevision { get; private set; }
    public bool IsInputDeviceSwitching => inputDeviceSwitching;
    public bool CanSelectInputDevice => TryGetInitializedVoiceService(out IVivoxService service)
        && service.IsLoggedIn && inputDevices.Count > 0 && !inputDeviceSwitching;

    public async Task<bool> SetInputDeviceAsync(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId) || inputDeviceSwitching
            || !TryGetInitializedVoiceService(out IVivoxService service) || !service.IsLoggedIn)
            return false;

        VivoxInputDevice device = FindInputDevice(service, deviceId);
        if (device == null)
        {
            InputDeviceError = "The selected microphone is no longer available.";
            RefreshInputDevices(service, force: true);
            return false;
        }

        bool changed = await SwitchInputDeviceAsync(service, device);
        if (!changed) return false;
        PlayerPrefs.SetString(InputDevicePlayerPrefsKey, device.DeviceID);
        PlayerPrefs.Save();
        return true;
    }

    private async Task<bool> SwitchInputDeviceAsync(IVivoxService service, VivoxInputDevice device)
    {
        if (service == null || device == null || !service.IsLoggedIn || inputDeviceSwitching) return false;
        if (string.Equals(service.ActiveInputDevice?.DeviceID, device.DeviceID, StringComparison.Ordinal))
        {
            InputDeviceError = string.Empty;
            RefreshInputDevices(service, force: true);
            return true;
        }

        inputDeviceSwitching = true;
        InputDeviceError = string.Empty;
        InputDeviceRevision++;
        ResetLocalVoiceIntensity();
        try
        {
            await service.SetActiveInputDeviceAsync(device);
            RefreshIntensityEnvironment();
            RefreshInputDevices(service, force: true);
            return true;
        }
        catch (Exception exception)
        {
            InputDeviceError = $"Microphone change failed: {exception.Message}";
            Debug.LogWarning($"VivoxVoiceManager: {InputDeviceError}", this);
            RefreshInputDevices(service, force: true);
            return false;
        }
        finally
        {
            inputDeviceSwitching = false;
            InputDeviceRevision++;
        }
    }

    private async Task ApplySavedInputDeviceAsync(IVivoxService service)
    {
        if (applyingSavedInputDevice || inputDeviceSwitching || service == null || !service.IsLoggedIn) return;
        applyingSavedInputDevice = true;
        try
        {
            RefreshInputDevices(service);
            if (!PlayerPrefs.HasKey(InputDevicePlayerPrefsKey)) return;
            string savedId = PlayerPrefs.GetString(InputDevicePlayerPrefsKey, string.Empty);
            VivoxInputDevice savedDevice = FindInputDevice(service, savedId);
            if (savedDevice != null)
            {
                await SwitchInputDeviceAsync(service, savedDevice);
                return;
            }

            InputDeviceError = "Saved microphone unavailable. Using the system default.";
            VivoxInputDevice fallback = FindSystemDefaultInputDevice(service);
            if (fallback != null && !string.Equals(service.ActiveInputDevice?.DeviceID,
                    fallback.DeviceID, StringComparison.Ordinal))
                await SwitchInputDeviceAsync(service, fallback);
            RefreshInputDevices(service, force: true);
        }
        finally
        {
            applyingSavedInputDevice = false;
        }
    }

    private void OnAvailableInputDevicesChanged()
    {
        if (!TryGetInitializedVoiceService(out IVivoxService service) || !service.IsLoggedIn) return;
        RefreshInputDevices(service, force: true);
        _ = ApplySavedInputDeviceAsync(service);
    }

    private void OnEffectiveInputDeviceChanged()
    {
        if (!TryGetInitializedVoiceService(out IVivoxService service) || !service.IsLoggedIn) return;
        RefreshIntensityEnvironment();
        RefreshInputDevices(service, force: true);
    }

    private void RefreshInputDevices(IVivoxService service, bool force = false)
    {
        if (service == null) return;
        var available = service.AvailableInputDevices;
        var refreshed = new List<VoiceInputDeviceOption>(available?.Count ?? 0);
        if (available != null)
            for (int i = 0; i < available.Count; i++)
            {
                VivoxInputDevice device = available[i];
                if (device != null) refreshed.Add(new VoiceInputDeviceOption(device.DeviceID, device.DeviceName));
            }

        string activeId = service.ActiveInputDevice?.DeviceID ?? string.Empty;
        bool changed = force || !string.Equals(activeId, ActiveInputDeviceId, StringComparison.Ordinal)
            || refreshed.Count != inputDevices.Count;
        if (!changed)
            for (int i = 0; i < refreshed.Count; i++)
                if (!string.Equals(refreshed[i].DeviceId, inputDevices[i].DeviceId, StringComparison.Ordinal)
                    || !string.Equals(refreshed[i].DisplayName, inputDevices[i].DisplayName, StringComparison.Ordinal))
                { changed = true; break; }

        if (!changed) return;
        inputDevices.Clear();
        inputDevices.AddRange(refreshed);
        ActiveInputDeviceId = activeId;
        InputDeviceRevision++;
    }

    private static VivoxInputDevice FindInputDevice(IVivoxService service, string deviceId)
    {
        if (service?.AvailableInputDevices == null || string.IsNullOrEmpty(deviceId)) return null;
        for (int i = 0; i < service.AvailableInputDevices.Count; i++)
        {
            VivoxInputDevice device = service.AvailableInputDevices[i];
            if (device != null && string.Equals(device.DeviceID, deviceId, StringComparison.Ordinal)) return device;
        }
        return null;
    }

    private static VivoxInputDevice FindSystemDefaultInputDevice(IVivoxService service)
    {
        if (service?.AvailableInputDevices == null || service.AvailableInputDevices.Count == 0) return null;
        for (int i = 0; i < service.AvailableInputDevices.Count; i++)
        {
            VivoxInputDevice device = service.AvailableInputDevices[i];
            if (device != null && string.Equals(device.DeviceName, "Default System Device",
                    StringComparison.OrdinalIgnoreCase)) return device;
        }
        return service.AvailableInputDevices[0];
    }

    public bool SetAutomaticInputLevel(bool enabled)
    {
        if (!TryGetInputProcessing(out var processing)) return false;
        if (!ApplyAutomaticInputLevel(processing, enabled)) return false;
        PlayerPrefs.SetInt(AutomaticInputLevelKey, enabled ? 1 : 0); PlayerPrefs.Save();
        RefreshIntensityEnvironment();
        return true;
    }

    private void ApplySavedInputProcessing()
    {
        if (!PlayerPrefs.HasKey(AutomaticInputLevelKey) || !TryGetInputProcessing(out var processing)) return;
        bool enabled = PlayerPrefs.GetInt(AutomaticInputLevelKey) != 0;
        ApplyAutomaticInputLevel(processing, enabled);
    }

    private static bool ApplyAutomaticInputLevel(IVivoxGlobalAudioSettings processing, bool enabled)
    {
        try
        {
            if (processing.AutomaticGainControlEnabled != enabled) processing.AutomaticGainControlEnabled = enabled;
            return processing.AutomaticGainControlEnabled == enabled;
        }
        catch (NotImplementedException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static bool TryGetInputProcessing(out IVivoxGlobalAudioSettings processing)
    {
        processing = null;
        if (Application.platform == RuntimePlatform.WebGLPlayer || !TryGetInitializedVoiceService(out var service)
            || !service.IsLoggedIn) return false;
        try
        {
            processing = service.VivoxGlobalAudioSettings;
            return processing != null;
        }
        catch (NotImplementedException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    public bool BeginVoiceCalibration()
    {
        if (!IsVoiceConnected) return false;
        RefreshIntensityEnvironment();
        if (string.IsNullOrEmpty(intensityProfileKey)) return false;
        intensityMonitor.Calibration.Begin();
        return true;
    }

    public void ContinueVoiceCalibration() { intensityMonitor.Calibration.Continue(); }
    public void CancelVoiceCalibration()
    {
        var calibration = intensityMonitor.Calibration;
        if (calibration.Stage >= VoiceCalibrationStage.Silence && calibration.Stage <= VoiceCalibrationStage.Loud)
            calibration.Cancel();
    }

    public void ResetVoiceCalibration()
    {
        VoiceIntensityProfileStore.Clear(intensityProfileKey);
        intensityMonitor.SetProfile(VoiceIntensityProfile.Default);
        savedIntensityRevision = intensityMonitor.CompletedRevision;
    }

    private void ResetLocalVoiceIntensity()
    {
        var calibration = intensityMonitor.Calibration;
        if (calibration.Stage >= VoiceCalibrationStage.Silence && calibration.Stage <= VoiceCalibrationStage.Loud)
            calibration.Cancel("Calibration canceled because voice input changed.");
        intensityMonitor.ResetEnvelope();
    }

    private void UpdateLocalVoiceIntensity(float deltaTime)
    {
        if (IsVoiceConnected && Time.unscaledTime >= nextIntensityEnvironmentCheck)
        {
            nextIntensityEnvironmentCheck = Time.unscaledTime + 1f;
            RefreshIntensityEnvironment();
        }
        intensityMonitor.Update(LocalVoiceActivity, deltaTime);
        if (intensityMonitor.CompletedRevision != savedIntensityRevision)
        {
            VoiceIntensityProfileStore.Save(intensityProfileKey, intensityMonitor.Profile);
            savedIntensityRevision = intensityMonitor.CompletedRevision;
        }
    }

    private void RefreshIntensityEnvironment()
    {
        if (!IsVoiceConnected || !TryGetInitializedVoiceService(out IVivoxService service) || !service.IsLoggedIn) return;
        VivoxInputDevice effectiveDevice = service.EffectiveInputDevice ?? service.ActiveInputDevice;
        string microphone = effectiveDevice?.DeviceName ?? "System microphone";
        string microphoneIdentity = string.IsNullOrEmpty(effectiveDevice?.DeviceID)
            ? microphone : effectiveDevice.DeviceID;
        bool? automaticGain = null, noiseSuppression = null;
        if (Application.platform != RuntimePlatform.WebGLPlayer)
        {
            try
            {
                var processing = service.VivoxGlobalAudioSettings;
                if (processing != null)
                {
                    automaticGain = processing.AutomaticGainControlEnabled;
                    noiseSuppression = processing.NoiseSuppressionEnabled;
                }
            }
            catch (NotImplementedException) { }
            catch (NotSupportedException) { }
        }
        string key = VoiceIntensityProfileStore.KeyFor(microphoneIdentity,
            service.InputDeviceVolume, automaticGain, noiseSuppression);
        string legacyKey = VoiceIntensityProfileStore.KeyFor(microphone,
            service.InputDeviceVolume, automaticGain, noiseSuppression);
        InputMicrophoneName = microphone; InputAutomaticGain = automaticGain;
        if (key == intensityProfileKey) return;
        bool changed = intensityProfileKey != null;
        intensityProfileKey = key;
        VoiceIntensityProfile profile = VoiceIntensityProfileStore.Load(key);
        if (!VoiceIntensityProfileStore.Has(key) && VoiceIntensityProfileStore.Has(legacyKey))
        {
            profile = VoiceIntensityProfileStore.Load(legacyKey);
            VoiceIntensityProfileStore.Save(key, profile);
        }
        intensityMonitor.SetProfile(profile);
        savedIntensityRevision = intensityMonitor.CompletedRevision;
        if (changed) intensityMonitor.Calibration.Cancel("Microphone settings changed. Calibration profile reloaded.");
    }
}
