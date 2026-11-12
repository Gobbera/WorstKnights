using System;
using System.Collections.Generic;
using Unity.Services.Vivox;
using Unity.Services.Vivox.AudioTaps;
using UnityEngine;

public sealed partial class VivoxVoiceManager
{
    private static readonly AnimationCurve FlatVoiceRolloff = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    private sealed class ParticipantVoiceOutput
    {
        public VivoxParticipant Participant;
        public VivoxParticipantTap Tap;
        public AudioSource Source;
    }

    private readonly Dictionary<VivoxParticipant, ParticipantVoiceOutput> unityVoiceOutputs =
        new Dictionary<VivoxParticipant, ParticipantVoiceOutput>();
    private Transform unityVoiceListener;
    private float nextUnityVoiceListenerRefresh;
    private float nextUnityVoiceOutputRetry;

    public override void OnDisable()
    {
        if (Instance == this) SuspendVoiceTransmission();
        ClearUnityVoiceOutputs();
        ResetLocalVoiceActivity();
        base.OnDisable();
    }

    private static bool ShouldUseUnityVoiceOutput(VivoxVoiceSettings voiceSettings, VivoxVoiceMode mode)
    {
        return voiceSettings != null && voiceSettings.UnityVoiceOutput
            && (mode == VivoxVoiceMode.NonPositional2D
                || (mode == VivoxVoiceMode.Positional3D && !voiceSettings.UseNativeVivoxPositionalAudio));
    }

    private void UpdateUnityVoiceOutputs()
    {
        if (!ShouldUseUnityVoiceOutput(settings, activeVoiceMode)
            || Application.platform == RuntimePlatform.WebGLPlayer)
        {
            ClearUnityVoiceOutputs();
            return;
        }

        if (!IsVoiceConnected || !TryGetVivoxService(out IVivoxService service)
            || !service.ActiveChannels.TryGetValue(activeChannelName, out var participants))
        {
            foreach (var output in unityVoiceOutputs.Values)
                if (output.Source != null) output.Source.mute = true;
            return;
        }

        Transform listener = ResolveUnityVoiceListener();
        for (int i = 0; i < participants.Count; i++)
        {
            VivoxParticipant participant = participants[i];
            if (!TryResolveRemoteParticipantPlayerId(participant, out string playerId)) continue;
            ParticipantVoiceOutput output = EnsureUnityVoiceOutput(participant);
            if (output == null) continue;

            bool wantsDirection = activeVoiceMode == VivoxVoiceMode.Positional3D
                && settings.AllowDirectionalPanning;
            bool hasSpeakerAnchor = TryResolveRemoteVoiceAnchor(playerId, out Transform speakerAnchor);
            bool directional = wantsDirection && hasSpeakerAnchor;
            PositionUnityVoiceSource(output.Source, listener, speakerAnchor, directional);
            ConfigureUnityVoiceSource(output.Source, settings, directional);

            GetParticipantProximityMix(playerId, out int localVolume, out bool proximityMuted);
            bool muted = proximityMuted
                || (wantsDirection && !hasSpeakerAnchor)
                || participant.IsMuted || service.IsOutputDeviceMuted;
            ApplyUnityVoiceSourceListenerMix(output.Source, localVolume, muted || !output.Tap.IsRunning,
                GetParticipantListeningPreference(playerId));
        }
    }

    private ParticipantVoiceOutput EnsureUnityVoiceOutput(VivoxParticipant participant)
    {
        if (unityVoiceOutputs.TryGetValue(participant, out ParticipantVoiceOutput output))
        {
            if (output.Source != null && output.Tap != null && output.Tap.IsRunning) return output;
            RemoveUnityVoiceOutput(participant);
        }

        if (Time.unscaledTime < nextUnityVoiceOutputRetry || participant.ParticipantTapAudioSource != null)
            return null;

        try
        {
            GameObject tapObject = participant.CreateVivoxParticipantTap("Vivox Remote Voice", silenceInChannelAudioMix: true);
            if (tapObject == null) return null;
            AudioSource source = tapObject.GetComponent<AudioSource>();
            source.mute = true;
            var tap = tapObject.GetComponent<VivoxParticipantTap>();
            if (tap == null || !tap.IsRunning)
                throw new InvalidOperationException("The participant audio tap could not register.");

            tapObject.transform.SetParent(transform, false);
            ConfigureUnityVoiceSource(source, settings, directional: false);
            output = new ParticipantVoiceOutput { Participant = participant, Tap = tap, Source = source };
            unityVoiceOutputs.Add(participant, output);
            return output;
        }
        catch (Exception exception)
        {
            // Destroying our failed tap releases original-mix suppression as well.
            participant.DestroyVivoxParticipantTap();
            nextUnityVoiceOutputRetry = Time.unscaledTime + 2f;
            Debug.LogWarning($"VivoxVoiceManager: Unity voice output unavailable; retaining direct Vivox playback. {exception.Message}", this);
            return null;
        }
    }

    private static void PositionUnityVoiceSource(AudioSource source, Transform listener,
        Transform speaker, bool directional)
    {
        Transform anchor = directional && speaker != null ? speaker : listener;
        if (source != null && anchor != null)
            source.transform.position = anchor.position;
    }

    private static void ConfigureUnityVoiceSource(AudioSource source, VivoxVoiceSettings voiceSettings,
        bool directional)
    {
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = directional ? 1f : 0f;
        source.panStereo = 0f;
        source.dopplerLevel = 0f;
        source.spread = 0f;
        source.pitch = 1f;
        source.spatialize = directional && !string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName());
        source.minDistance = Mathf.Max(0.01f, voiceSettings.ConversationalDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.01f, voiceSettings.AudibleDistance);
        if (source.rolloffMode != AudioRolloffMode.Custom)
        {
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, FlatVoiceRolloff);
        }
        source.bypassEffects = false;
        source.bypassListenerEffects = false;
        source.bypassReverbZones = voiceSettings.EnvironmentReverbSend <= 0f;
        source.reverbZoneMix = voiceSettings.EnvironmentReverbSend;
        source.outputAudioMixerGroup = voiceSettings.VoiceMixerGroup;
    }

    private static void ApplyUnityVoiceSourceMix(AudioSource source, int localVolume, bool muted)
    {
        ApplyUnityVoiceSourceListenerMix(source, localVolume, muted, VoiceListeningPreference.Default);
    }

    private static void ApplyUnityVoiceSourceListenerMix(AudioSource source, int proximityVolume,
        bool muted, VoiceListeningPreference preference)
    {
        // Taps expose unmixed audio: apply user gain once, after the unchanged distance gain.
        var mix = new VoiceListeningMix(proximityVolume, muted, preference);
        source.volume = mix.UnityVolume;
        source.mute = mix.Muted;
    }

    private Transform ResolveUnityVoiceListener()
    {
        if (unityVoiceListener != null && unityVoiceListener.gameObject.activeInHierarchy
            && unityVoiceListener.TryGetComponent(out AudioListener current) && current.enabled)
            return unityVoiceListener;
        if (Time.unscaledTime < nextUnityVoiceListenerRefresh) return null;
        nextUnityVoiceListenerRefresh = Time.unscaledTime + 0.5f;
        unityVoiceListener = null;
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
            if (listener.enabled) { unityVoiceListener = listener.transform; break; }
        return unityVoiceListener;
    }

    private void RemoveUnityVoiceOutput(VivoxParticipant participant)
    {
        if (participant == null || !unityVoiceOutputs.TryGetValue(participant, out ParticipantVoiceOutput output)) return;
        unityVoiceOutputs.Remove(participant);
        ReleaseUnityVoiceOutput(output);
    }

    private static void ReleaseUnityVoiceOutput(ParticipantVoiceOutput output)
    {
        if (output.Source != null) output.Source.mute = true;
        if (output.Tap != null) output.Tap.enabled = false;
        output.Participant.DestroyVivoxParticipantTap();
    }

    private void ClearUnityVoiceOutputs()
    {
        foreach (var output in unityVoiceOutputs.Values) ReleaseUnityVoiceOutput(output);
        unityVoiceOutputs.Clear();
        unityVoiceListener = null;
        nextUnityVoiceListenerRefresh = 0f;
        nextUnityVoiceOutputRetry = 0f;
    }
}
