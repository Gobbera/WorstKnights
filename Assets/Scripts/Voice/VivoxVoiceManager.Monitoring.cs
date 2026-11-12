using System;
using Unity.Services.Vivox;
using Unity.Services.Vivox.AudioTaps;
using UnityEngine;

public sealed partial class VivoxVoiceManager
{
    private bool microphoneTestOpen;
    private VivoxCaptureSourceTap microphoneMonitorTap;
    private AudioSource microphoneMonitorSource;
    private float microphoneMonitorStartedAt;
    public bool MicrophoneMonitorRequested { get; private set; }
    public bool IsMicrophoneMonitorRunning => microphoneMonitorTap != null && microphoneMonitorTap.IsRunning
        && microphoneMonitorSource != null && !microphoneMonitorSource.mute;
    public string MicrophoneMonitorError { get; private set; } = string.Empty;
    public bool CanMonitorMicrophone => microphoneTestOpen && isActiveAndEnabled && !applicationIsQuitting
        && applicationHasFocus && !applicationPaused && Application.platform != RuntimePlatform.WebGLPlayer
        && IsVoiceConnected && TryGetInitializedVoiceService(out var service) && service.IsLoggedIn;

    public void SetMicrophoneTestOpen(bool open)
    {
        if (microphoneTestOpen == open) return;
        StopMicrophoneMonitoring(); microphoneTestOpen = open;
        MicrophoneMonitorError = string.Empty;
    }

    public bool SetMicrophoneMonitoring(bool enabled)
    {
        if (!enabled) { StopMicrophoneMonitoring(); return true; }
        if (!CanMonitorMicrophone) return false;
        MicrophoneMonitorError = string.Empty;
        MicrophoneMonitorRequested = true;
        UpdateMicrophoneMonitoring();
        return MicrophoneMonitorRequested;
    }

    public void StopMicrophoneMonitoring()
    {
        MicrophoneMonitorRequested = false;
        if (microphoneMonitorSource != null) microphoneMonitorSource.mute = true;
        if (microphoneMonitorTap != null)
        {
            microphoneMonitorTap.enabled = false;
            if (Application.isPlaying) Destroy(microphoneMonitorTap.gameObject);
            else DestroyImmediate(microphoneMonitorTap.gameObject);
        }
        microphoneMonitorTap = null; microphoneMonitorSource = null;
    }

    private void UpdateMicrophoneMonitoring()
    {
        if (!MicrophoneMonitorRequested) return;
        if (!CanMonitorMicrophone) { StopMicrophoneMonitoring(); return; }
        if (microphoneMonitorTap == null)
        {
            GameObject root = null;
            try
            {
                root = new GameObject("Vivox Microphone Test Playback"); root.SetActive(false);
                root.transform.SetParent(transform, false);
                microphoneMonitorSource = root.AddComponent<AudioSource>();
                ConfigureMicrophoneMonitorSource(microphoneMonitorSource, settings);
                microphoneMonitorTap = root.AddComponent<VivoxCaptureSourceTap>();
                microphoneMonitorTap.AutoAcquireChannel = false;
                microphoneMonitorTap.ChannelName = activeChannelName;
                microphoneMonitorStartedAt = Time.unscaledTime;
                root.SetActive(true);
            }
            catch (Exception exception)
            {
                StopMicrophoneMonitoring();
                if (root != null) { if (Application.isPlaying) Destroy(root); else DestroyImmediate(root); }
                MicrophoneMonitorError = "Microphone playback is unavailable on this device.";
                Debug.LogWarning($"VivoxVoiceManager: microphone test playback unavailable. {exception.Message}", this);
                return;
            }
        }
        if (!microphoneMonitorTap.IsRunning && Time.unscaledTime - microphoneMonitorStartedAt > 2f)
        {
            StopMicrophoneMonitoring();
            MicrophoneMonitorError = "Microphone playback could not start. Reconnect voice and retry.";
            return;
        }
        bool inputAvailable = TryGetInitializedVoiceService(out var service) && service.IsLoggedIn
            && !service.IsInputDeviceMuted && IsMicrophoneTransmissionAllowed && intensityMonitor.InputAvailable;
        microphoneMonitorSource.mute = !ShouldPlayMicrophoneMonitor(microphoneTestOpen, MicrophoneMonitorRequested,
            microphoneMonitorTap.IsRunning, inputAvailable);
    }

    private static bool ShouldPlayMicrophoneMonitor(bool testOpen, bool requested, bool tapRunning, bool inputAvailable)
        => testOpen && requested && tapRunning && inputAvailable;

    private static void ConfigureMicrophoneMonitorSource(AudioSource source, VivoxVoiceSettings configuration)
    {
        source.playOnAwake = false; source.loop = true; source.mute = true;
        source.spatialBlend = source.panStereo = source.dopplerLevel = 0f; source.pitch = 1f;
        source.volume = configuration != null ? configuration.MicrophoneMonitorVolume : 0.35f;
        source.bypassEffects = source.bypassListenerEffects = source.bypassReverbZones = true;
        source.reverbZoneMix = 0f; source.ignoreListenerPause = true;
        source.outputAudioMixerGroup = configuration != null ? configuration.VoiceMixerGroup : null;
    }
}
