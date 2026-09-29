using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
[AddComponentMenu("Audio/Ambience/Ambient Audio Controller")]
public sealed class AmbientAudioController : MonoBehaviour
{
    private const float SourceStopVolumeThreshold = 0.0001f;

    private sealed class LoopSourceState
    {
        public AudioSource Source;
        public AudioCue Cue;
        public float BaseVolume;
        public float TargetVolume;
        public float FadeSpeed;
        public bool StopWhenSilent;
    }

    private sealed class LoopLayer
    {
        private readonly LoopSourceState[] states =
        {
            new LoopSourceState(),
            new LoopSourceState()
        };

        private int activeIndex;
        private string childName;

        public void EnsureSources(Transform parent, string layerName)
        {
            if (parent == null)
                return;

            childName = string.IsNullOrWhiteSpace(layerName) ? "Ambient Loop" : layerName;
            for (int i = 0; i < states.Length; i++)
            {
                LoopSourceState state = states[i];
                if (state.Source != null)
                    continue;

                GameObject sourceObject = new GameObject($"{childName}_{i + 1}");
                sourceObject.transform.SetParent(parent, false);

                AudioSource source = sourceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;

                state.Source = source;
            }
        }

        public void SetTarget(
            AudioCue cue,
            float volumeMultiplier,
            float fadeTime,
            AudioMixerGroup fallbackMixerGroup)
        {
            volumeMultiplier = Mathf.Max(0f, volumeMultiplier);
            fadeTime = Mathf.Max(0f, fadeTime);

            if (cue == null || !cue.HasPlayableClip())
            {
                FadeOutAll(fadeTime);
                return;
            }

            LoopSourceState activeState = states[activeIndex];
            if (activeState.Source != null
                && activeState.Source.isPlaying
                && activeState.Cue == cue
                && !activeState.StopWhenSilent)
            {
                SetStateTargetVolume(activeState, activeState.Source.volume, activeState.BaseVolume * volumeMultiplier, fadeTime);
                FadeOutInactiveSources(fadeTime);
                return;
            }

            int nextIndex = activeIndex == 0 ? 1 : 0;
            LoopSourceState nextState = states[nextIndex];
            if (nextState.Source == null)
                return;

            AudioClip clip = cue.GetRandomClip();
            if (clip == null)
            {
                FadeOutAll(fadeTime);
                return;
            }

            nextState.Source.Stop();
            cue.ConfigureAudioSource(nextState.Source, clip, fallbackMixerGroup, forceLoop: true);
            float configuredVolume = nextState.Source.volume;
            nextState.BaseVolume = configuredVolume;
            nextState.Source.volume = 0f;
            nextState.Cue = cue;
            nextState.StopWhenSilent = false;
            SetStateTargetVolume(nextState, 0f, configuredVolume * volumeMultiplier, fadeTime);
            nextState.Source.Play();

            activeState.StopWhenSilent = true;
            SetStateTargetVolume(activeState, activeState.Source != null ? activeState.Source.volume : 0f, 0f, fadeTime);

            activeIndex = nextIndex;
        }

        public void Update(float deltaTime, Vector3 worldPosition)
        {
            for (int i = 0; i < states.Length; i++)
            {
                LoopSourceState state = states[i];
                AudioSource source = state.Source;
                if (source == null)
                    continue;

                source.transform.position = worldPosition;

                if (!source.isPlaying)
                    continue;

                if (Mathf.Approximately(source.volume, state.TargetVolume))
                {
                    source.volume = state.TargetVolume;
                }
                else if (state.FadeSpeed <= 0f)
                {
                    source.volume = state.TargetVolume;
                }
                else
                {
                    source.volume = Mathf.MoveTowards(source.volume, state.TargetVolume, state.FadeSpeed * deltaTime);
                }

                if (!state.StopWhenSilent || source.volume > SourceStopVolumeThreshold)
                    continue;

                source.Stop();
                source.clip = null;
                source.volume = 0f;
                source.loop = true;
                state.Cue = null;
                state.BaseVolume = 0f;
                state.TargetVolume = 0f;
                state.FadeSpeed = 0f;
                state.StopWhenSilent = false;
            }
        }

        public void StopImmediately()
        {
            for (int i = 0; i < states.Length; i++)
            {
                LoopSourceState state = states[i];
                if (state.Source == null)
                    continue;

                state.Source.Stop();
                state.Source.clip = null;
                state.Source.volume = 0f;
                state.Cue = null;
                state.BaseVolume = 0f;
                state.TargetVolume = 0f;
                state.FadeSpeed = 0f;
                state.StopWhenSilent = false;
            }
        }

        private void FadeOutAll(float fadeTime)
        {
            for (int i = 0; i < states.Length; i++)
            {
                LoopSourceState state = states[i];
                state.StopWhenSilent = true;
                SetStateTargetVolume(state, state.Source != null ? state.Source.volume : 0f, 0f, fadeTime);
            }
        }

        private void FadeOutInactiveSources(float fadeTime)
        {
            for (int i = 0; i < states.Length; i++)
            {
                if (i == activeIndex)
                    continue;

                LoopSourceState state = states[i];
                state.StopWhenSilent = true;
                SetStateTargetVolume(state, state.Source != null ? state.Source.volume : 0f, 0f, fadeTime);
            }
        }

        private static void SetStateTargetVolume(LoopSourceState state, float currentVolume, float targetVolume, float fadeTime)
        {
            if (state == null)
                return;

            state.TargetVolume = Mathf.Max(0f, targetVolume);
            if (fadeTime <= 0f)
            {
                state.FadeSpeed = 0f;
                if (state.Source != null)
                    state.Source.volume = state.TargetVolume;

                return;
            }

            state.FadeSpeed = Mathf.Abs(state.TargetVolume - currentVolume) / fadeTime;
        }

    }

    [Header("Profiles")]
    [SerializeField] private AmbientAudioProfile defaultProfile;

    [Header("Playback Anchor")]
    [SerializeField] private Transform playbackAnchor;
    [SerializeField] private bool followActiveAudioListener = true;
    [SerializeField] private bool requireActiveAudioListener = true;
    [SerializeField] [Min(0.05f)] private float audioListenerRefreshInterval = 0.5f;

    [Header("Fallback Routing")]
    [SerializeField] private AudioMixerGroup fallbackMixerGroup;

    [Header("Profile Random One-Shots")]
    [SerializeField] private bool playProfileRandomOneShots = true;
    [SerializeField] private AmbientRandomPlaybackMode profileRandomPlaybackMode = AmbientRandomPlaybackMode.TwoDimensionalOrListener;
    [SerializeField] private bool avoidImmediateRandomRepeats = true;
    [SerializeField] private bool resetRandomTimerOnProfileChange = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging;

    private readonly LoopLayer primaryLoop = new LoopLayer();
    private readonly LoopLayer secondaryLoop = new LoopLayer();
    private readonly List<AmbientZone> activeZones = new List<AmbientZone>();
    private readonly Dictionary<AudioCue, float> randomCueLastPlaybackTimes = new Dictionary<AudioCue, float>();

    private AmbientAudioProfile appliedProfile;
    private AmbientAudioProfile randomTimerProfile;
    private AudioCue lastProfileRandomCue;
    private AudioListener cachedAudioListener;
    private float nextListenerRefreshTime;
    private float nextProfileRandomTime;
    private bool profileDirty = true;

    public AmbientAudioProfile DefaultProfile => defaultProfile;
    public AmbientAudioProfile CurrentProfile => ResolveCurrentProfile();
    public Transform PlaybackAnchor => ResolvePlaybackAnchor();

    private void Awake()
    {
        primaryLoop.EnsureSources(transform, "Primary Loop");
        secondaryLoop.EnsureSources(transform, "Secondary Loop");
        RefreshAudioListener(force: true);
    }

    private void OnEnable()
    {
        profileDirty = true;
        ScheduleNextProfileRandomTime(resetImmediately: true);
    }

    private void Update()
    {
        RefreshAudioListener(force: false);

        Transform anchor = ResolvePlaybackAnchor();
        bool canPlay = CanPlayAmbient(anchor);
        AmbientAudioProfile targetProfile = canPlay ? ResolveCurrentProfile() : null;
        float fadeTime = ResolveFadeTime(targetProfile);

        if (profileDirty || targetProfile != appliedProfile)
            ApplyProfile(targetProfile, fadeTime, allowDefaultFallback: canPlay);

        Vector3 playbackPosition = anchor != null ? anchor.position : transform.position;
        float deltaTime = Mathf.Max(0f, Time.deltaTime);
        primaryLoop.Update(deltaTime, playbackPosition);
        secondaryLoop.Update(deltaTime, playbackPosition);

        if (canPlay && playProfileRandomOneShots)
            UpdateProfileRandomOneShots(targetProfile, anchor);
    }

    private void OnDisable()
    {
        primaryLoop.StopImmediately();
        secondaryLoop.StopImmediately();
        activeZones.Clear();
        appliedProfile = null;
        randomTimerProfile = null;
        lastProfileRandomCue = null;
        profileDirty = true;
    }

    public void EnterZone(AmbientZone zone)
    {
        if (zone == null || zone.Profile == null)
            return;

        activeZones.Remove(zone);
        activeZones.Add(zone);
        profileDirty = true;

        if (verboseLogging)
            Debug.Log($"[AmbientAudioController] Entrou na zona '{zone.DisplayName}'.", zone);
    }

    public void ExitZone(AmbientZone zone)
    {
        if (zone == null)
            return;

        if (!activeZones.Remove(zone))
            return;

        profileDirty = true;

        if (verboseLogging)
            Debug.Log($"[AmbientAudioController] Saiu da zona '{zone.DisplayName}'.", zone);
    }

    private void ApplyProfile(AmbientAudioProfile profile, float fadeTime, bool allowDefaultFallback)
    {
        AudioCue primaryCue = allowDefaultFallback ? ResolvePrimaryLoop(profile) : null;
        float primaryVolume = allowDefaultFallback ? ResolvePrimaryLoopVolume(profile) : 0f;
        AudioCue secondaryCue = allowDefaultFallback ? ResolveSecondaryLoop(profile) : null;
        float secondaryVolume = allowDefaultFallback ? ResolveSecondaryLoopVolume(profile) : 0f;

        primaryLoop.SetTarget(primaryCue, primaryVolume, fadeTime, fallbackMixerGroup);
        secondaryLoop.SetTarget(secondaryCue, secondaryVolume, fadeTime, fallbackMixerGroup);

        if (resetRandomTimerOnProfileChange && profile != appliedProfile)
            ScheduleNextProfileRandomTime(resetImmediately: true);

        appliedProfile = profile;
        profileDirty = false;
    }

    private AmbientAudioProfile ResolveCurrentProfile()
    {
        AmbientZone topZone = ResolveTopPriorityZone();
        return topZone != null && topZone.Profile != null
            ? topZone.Profile
            : defaultProfile;
    }

    private AmbientZone ResolveTopPriorityZone()
    {
        AmbientZone bestZone = null;
        int bestPriority = int.MinValue;

        for (int i = activeZones.Count - 1; i >= 0; i--)
        {
            AmbientZone zone = activeZones[i];
            if (zone == null || !zone.isActiveAndEnabled || zone.Profile == null)
            {
                activeZones.RemoveAt(i);
                profileDirty = true;
                continue;
            }

            if (bestZone != null && zone.Priority <= bestPriority)
                continue;

            bestZone = zone;
            bestPriority = zone.Priority;
        }

        return bestZone;
    }

    private float ResolveFadeTime(AmbientAudioProfile profile)
    {
        AmbientZone topZone = ResolveTopPriorityZone();
        if (topZone != null && topZone.Profile == profile && topZone.HasFadeTimeOverride)
            return topZone.FadeTime;

        return profile != null ? profile.FadeTime : defaultProfile != null ? defaultProfile.FadeTime : 1f;
    }

    private AudioCue ResolvePrimaryLoop(AmbientAudioProfile profile)
    {
        if (profile != null && profile.HasPrimaryLoop)
            return profile.PrimaryLoop;

        return defaultProfile != null && defaultProfile.HasPrimaryLoop
            ? defaultProfile.PrimaryLoop
            : null;
    }

    private float ResolvePrimaryLoopVolume(AmbientAudioProfile profile)
    {
        if (profile != null && profile.HasPrimaryLoop)
            return profile.PrimaryLoopVolume;

        return defaultProfile != null && defaultProfile.HasPrimaryLoop
            ? defaultProfile.PrimaryLoopVolume
            : 0f;
    }

    private AudioCue ResolveSecondaryLoop(AmbientAudioProfile profile)
    {
        if (profile != null && profile.HasSecondaryLoop)
            return profile.SecondaryLoop;

        return profile == defaultProfile && defaultProfile != null && defaultProfile.HasSecondaryLoop
            ? defaultProfile.SecondaryLoop
            : null;
    }

    private float ResolveSecondaryLoopVolume(AmbientAudioProfile profile)
    {
        if (profile != null && profile.HasSecondaryLoop)
            return profile.SecondaryLoopVolume;

        return profile == defaultProfile && defaultProfile != null && defaultProfile.HasSecondaryLoop
            ? defaultProfile.SecondaryLoopVolume
            : 0f;
    }

    private void UpdateProfileRandomOneShots(AmbientAudioProfile targetProfile, Transform anchor)
    {
        AmbientAudioProfile randomProfile = ResolveRandomProfile(targetProfile);
        if (randomProfile == null)
        {
            randomTimerProfile = null;
            return;
        }

        if (randomProfile != randomTimerProfile)
        {
            randomTimerProfile = randomProfile;
            ScheduleNextProfileRandomTime(resetImmediately: true);
        }

        if (Time.time + 0.0001f < nextProfileRandomTime)
            return;

        ScheduleNextProfileRandomTime(resetImmediately: false);

        if (UnityEngine.Random.value > randomProfile.RandomChance)
            return;

        AudioCue cue = randomProfile.GetRandomOneShot(lastProfileRandomCue, avoidImmediateRandomRepeats);
        if (cue == null || !CanPlayRandomCue(cue))
            return;

        PlayRandomCue(cue, profileRandomPlaybackMode, anchor);
        lastProfileRandomCue = cue;
        randomCueLastPlaybackTimes[cue] = Time.time;
    }

    private AmbientAudioProfile ResolveRandomProfile(AmbientAudioProfile targetProfile)
    {
        if (targetProfile != null && targetProfile.HasRandomOneShots)
            return targetProfile;

        return defaultProfile != null && defaultProfile.HasRandomOneShots
            ? defaultProfile
            : null;
    }

    private bool CanPlayRandomCue(AudioCue cue)
    {
        if (cue == null || !cue.HasPlayableClip())
            return false;

        if (cue.Cooldown <= 0f)
            return true;

        return !randomCueLastPlaybackTimes.TryGetValue(cue, out float lastTime)
            || Time.time - lastTime >= cue.Cooldown;
    }

    private void PlayRandomCue(AudioCue cue, AmbientRandomPlaybackMode playbackMode, Transform anchor)
    {
        switch (playbackMode)
        {
            case AmbientRandomPlaybackMode.AtEmitterPosition:
                GameAudioService.Play(cue, transform.position);
                break;

            case AmbientRandomPlaybackMode.FollowAnchor:
                GameAudioService.Play(cue, anchor != null ? anchor : transform);
                break;

            default:
                if (cue.Is3D)
                    GameAudioService.Play(cue, anchor != null ? anchor.position : transform.position);
                else
                    GameAudioService.Play(cue);
                break;
        }
    }

    private void ScheduleNextProfileRandomTime(bool resetImmediately)
    {
        AmbientAudioProfile randomProfile = ResolveRandomProfile(ResolveCurrentProfile());
        float delay = randomProfile != null ? randomProfile.GetRandomInterval() : 1f;
        nextProfileRandomTime = Time.time + (resetImmediately ? delay : Mathf.Max(0.1f, delay));
    }

    private bool CanPlayAmbient(Transform anchor)
    {
        if (!requireActiveAudioListener)
            return true;

        return cachedAudioListener != null
            && cachedAudioListener.enabled
            && cachedAudioListener.gameObject.activeInHierarchy
            && (anchor != null || !followActiveAudioListener);
    }

    private Transform ResolvePlaybackAnchor()
    {
        if (playbackAnchor != null)
            return playbackAnchor;

        if (followActiveAudioListener && cachedAudioListener != null)
            return cachedAudioListener.transform;

        return transform;
    }

    private void RefreshAudioListener(bool force)
    {
        if (!force && Time.unscaledTime < nextListenerRefreshTime)
            return;

        nextListenerRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, audioListenerRefreshInterval);
        if (cachedAudioListener != null
            && cachedAudioListener.enabled
            && cachedAudioListener.gameObject.activeInHierarchy)
        {
            return;
        }

        cachedAudioListener = null;
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null || !listener.enabled || !listener.gameObject.activeInHierarchy)
                continue;

            cachedAudioListener = listener;
            return;
        }
    }

    private void OnValidate()
    {
        audioListenerRefreshInterval = Mathf.Max(0.05f, audioListenerRefreshInterval);
    }
}
