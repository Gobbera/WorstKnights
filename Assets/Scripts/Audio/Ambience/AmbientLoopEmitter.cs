using UnityEngine;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Audio/Ambience/Ambient Loop Emitter")]
public sealed class AmbientLoopEmitter : MonoBehaviour
{
    private const string AutoSourceName = "Ambient Loop Source";
    private const float SilentVolumeThreshold = 0.0001f;

    [Header("Cue")]
    [SerializeField] private AudioCue loopCue;
    [SerializeField] [Range(0f, 2f)] private float volumeMultiplier = 1f;
    [SerializeField] [Range(0.1f, 3f)] private float pitchMultiplier = 1f;

    [Header("Playback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Transform playbackAnchor;
    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private bool stopOnDisable = true;
    [SerializeField] private bool autoCreateAudioSource = true;
    [SerializeField] private bool applyCueSettingsContinuously = true;

    [Header("Listener")]
    [SerializeField] private bool requireActiveAudioListener = true;
    [SerializeField] [Min(0.05f)] private float audioListenerRefreshInterval = 0.5f;

    [Header("Source Settings")]
    [SerializeField] private AudioMixerGroup fallbackMixerGroup;
    [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;
    [SerializeField] [Range(0f, 5f)] private float dopplerLevel;

    [Header("Fading")]
    [SerializeField] [Min(0f)] private float fadeInTime = 0.5f;
    [SerializeField] [Min(0f)] private float fadeOutTime = 0.5f;

    [Header("Debug Gizmos")]
    [SerializeField] private bool drawDistanceGizmosWhenSelected = true;
    [SerializeField] private bool drawDistanceGizmosWhenNotSelected;
    [SerializeField] private bool drawLabels = true;
    [SerializeField] [Min(0f)] private float labelHeightOffset = 0.25f;

    private AudioCue activeCue;
    private AudioClip activeClip;
    private AudioListener cachedAudioListener;
    private float nextListenerRefreshTime;
    private float volumeRandomMultiplier = 1f;
    private float pitchRandomMultiplier = 1f;
    private float targetVolume;
    private float volumeFadeSpeed;
    private bool playRequested;
    private bool stopWhenSilent;
    private bool warnedMissingClip;
    private bool warnedMissingSource;

    public AudioCue LoopCue => loopCue;
    public AudioSource Source => audioSource;
    public Transform PlaybackAnchor => playbackAnchor != null ? playbackAnchor : transform;
    public bool IsPlaying => audioSource != null && audioSource.isPlaying && !stopWhenSilent;

    private void Awake()
    {
        EnsureAudioSource();
        RefreshAudioListener(force: true);
    }

    private void OnEnable()
    {
        RefreshAudioListener(force: true);

        if (playOnEnable)
            PlayLoop();
    }

    private void Update()
    {
        RefreshAudioListener(force: false);
        UpdateSourceTransform();

        if (playRequested && loopCue != null && !loopCue.HasPlayableClip())
            WarnMissingClipOnce();

        bool canPlay = CanPlay();
        if (playRequested && canPlay)
            EnsureLoopStarted();
        else
            FadeOutAndStop();

        UpdateRuntimeCueSettings();
        UpdateVolume(Time.deltaTime);
    }

    private void OnDisable()
    {
        if (stopOnDisable)
            StopLoop(immediate: true);
    }

    public void PlayLoop()
    {
        playRequested = true;
        stopWhenSilent = false;
        warnedMissingClip = false;
        warnedMissingSource = false;
    }

    public void StopLoop()
    {
        StopLoop(immediate: false);
    }

    public void StopLoopImmediate()
    {
        StopLoop(immediate: true);
    }

    public void SetLoopCue(AudioCue cue, bool restartIfPlaying = true)
    {
        if (loopCue == cue)
            return;

        loopCue = cue;
        warnedMissingClip = false;

        if (!restartIfPlaying)
            return;

        activeCue = null;
        activeClip = null;
        if (audioSource != null)
            audioSource.Stop();
    }

    private void StopLoop(bool immediate)
    {
        playRequested = false;
        if (immediate || fadeOutTime <= 0f)
        {
            StopSourceImmediately();
            return;
        }

        FadeOutAndStop();
    }

    private void EnsureLoopStarted()
    {
        AudioSource source = EnsureAudioSource();
        if (source == null)
            return;

        if (source.isPlaying && activeCue == loopCue && activeClip != null)
        {
            stopWhenSilent = false;
            SetVolumeTarget(ResolveTargetVolume(), fadeInTime);
            return;
        }

        AudioClip clip = loopCue != null ? loopCue.GetRandomClip() : null;
        if (clip == null)
        {
            WarnMissingClipOnce();
            FadeOutAndStop();
            return;
        }

        activeCue = loopCue;
        activeClip = clip;
        volumeRandomMultiplier = ResolveRandomMultiplier(loopCue.RandomVolumeRange);
        pitchRandomMultiplier = ResolveRandomMultiplier(loopCue.RandomPitchRange);

        ConfigureSourceFromCue(source, loopCue, clip);
        source.volume = fadeInTime > 0f ? 0f : ResolveTargetVolume();
        SetVolumeTarget(ResolveTargetVolume(), fadeInTime);
        stopWhenSilent = false;
        source.Play();
    }

    private void UpdateRuntimeCueSettings()
    {
        if (!applyCueSettingsContinuously || audioSource == null || activeCue == null || audioSource.clip == null)
            return;

        ApplyCueSourceSettings(audioSource, activeCue);

        if (!stopWhenSilent)
            SetVolumeTarget(ResolveTargetVolume(), fadeInTime);
    }

    private void UpdateVolume(float deltaTime)
    {
        if (audioSource == null || !audioSource.isPlaying)
            return;

        float fadeTime = stopWhenSilent ? fadeOutTime : fadeInTime;
        if (fadeTime <= 0f || volumeFadeSpeed <= 0f)
        {
            audioSource.volume = targetVolume;
        }
        else
        {
            audioSource.volume = Mathf.MoveTowards(audioSource.volume, targetVolume, volumeFadeSpeed * Mathf.Max(0f, deltaTime));
        }

        if (Mathf.Approximately(audioSource.volume, targetVolume))
            volumeFadeSpeed = 0f;

        if (!stopWhenSilent || audioSource.volume > SilentVolumeThreshold)
            return;

        StopSourceImmediately();
    }

    private void FadeOutAndStop()
    {
        if (audioSource == null || !audioSource.isPlaying)
            return;

        stopWhenSilent = true;
        SetVolumeTarget(0f, fadeOutTime);

        if (fadeOutTime <= 0f)
            StopSourceImmediately();
    }

    private void StopSourceImmediately()
    {
        if (audioSource == null)
            return;

        audioSource.Stop();
        audioSource.clip = null;
        audioSource.volume = 0f;
        activeCue = null;
        activeClip = null;
        targetVolume = 0f;
        volumeFadeSpeed = 0f;
        stopWhenSilent = false;
    }

    private bool CanPlay()
    {
        if (loopCue == null || !loopCue.HasPlayableClip())
            return false;

        if (!requireActiveAudioListener)
            return true;

        return cachedAudioListener != null
            && cachedAudioListener.enabled
            && cachedAudioListener.gameObject.activeInHierarchy;
    }

    private AudioSource EnsureAudioSource()
    {
        if (audioSource != null)
        {
            PrepareAudioSource(audioSource);
            return audioSource;
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            PrepareAudioSource(audioSource);
            return audioSource;
        }

        if (!autoCreateAudioSource)
        {
            WarnMissingSourceOnce();
            return null;
        }

        Transform existingSource = transform.Find(AutoSourceName);
        if (existingSource != null)
            audioSource = existingSource.GetComponent<AudioSource>();

        if (audioSource == null)
        {
            GameObject sourceObject = new GameObject(AutoSourceName);
            sourceObject.transform.SetParent(transform, false);
            audioSource = sourceObject.AddComponent<AudioSource>();
        }

        PrepareAudioSource(audioSource);
        return audioSource;
    }

    private void PrepareAudioSource(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = true;
        source.rolloffMode = rolloffMode;
        source.dopplerLevel = dopplerLevel;
    }

    private void ConfigureSourceFromCue(AudioSource source, AudioCue cue, AudioClip clip)
    {
        if (source == null || cue == null)
            return;

        source.clip = clip;
        source.loop = true;
        ApplyCueSourceSettings(source, cue);
        source.pitch = ResolveTargetPitch();
    }

    private void ApplyCueSourceSettings(AudioSource source, AudioCue cue)
    {
        if (source == null || cue == null)
            return;

        source.spatialBlend = cue.Is3D ? cue.SpatialBlend : 0f;
        source.spread = cue.Is3D ? cue.Spread : 0f;
        source.minDistance = cue.MinDistance;
        source.maxDistance = cue.MaxDistance;
        source.outputAudioMixerGroup = cue.MixerGroup != null ? cue.MixerGroup : fallbackMixerGroup;
        source.bypassReverbZones = !cue.AffectedByEnvironmentReverb;
        source.reverbZoneMix = cue.EnvironmentReverbSend;
        source.rolloffMode = rolloffMode;
        source.dopplerLevel = dopplerLevel;
        source.pitch = ResolveTargetPitch();
    }

    private void UpdateSourceTransform()
    {
        if (audioSource == null || audioSource.transform == transform)
            return;

        Transform anchor = PlaybackAnchor;
        if (anchor != null)
            audioSource.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
    }

    private float ResolveTargetVolume()
    {
        return activeCue != null
            ? Mathf.Max(0f, activeCue.BaseVolume * volumeRandomMultiplier * volumeMultiplier)
            : 0f;
    }

    private void SetVolumeTarget(float volume, float fadeTime)
    {
        float nextTargetVolume = Mathf.Max(0f, volume);
        if (Mathf.Approximately(targetVolume, nextTargetVolume) && volumeFadeSpeed > 0f)
            return;

        targetVolume = nextTargetVolume;
        if (audioSource == null)
        {
            volumeFadeSpeed = 0f;
            return;
        }

        if (fadeTime <= 0f)
        {
            volumeFadeSpeed = 0f;
            audioSource.volume = targetVolume;
            return;
        }

        volumeFadeSpeed = Mathf.Abs(targetVolume - audioSource.volume) / Mathf.Max(0.0001f, fadeTime);
    }

    private float ResolveTargetPitch()
    {
        return activeCue != null
            ? Mathf.Clamp(activeCue.BasePitch * pitchRandomMultiplier * pitchMultiplier, -3f, 3f)
            : 1f;
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

    private void WarnMissingClipOnce()
    {
        if (warnedMissingClip || loopCue == null)
            return;

        warnedMissingClip = true;
        Debug.LogWarning($"[AmbientLoopEmitter] '{loopCue.name}' nao possui AudioClip valido para loop.", loopCue);
    }

    private void WarnMissingSourceOnce()
    {
        if (warnedMissingSource)
            return;

        warnedMissingSource = true;
        Debug.LogWarning("[AmbientLoopEmitter] Nenhum AudioSource configurado e Auto Create Audio Source esta desligado.", gameObject);
    }

    private void OnValidate()
    {
        volumeMultiplier = Mathf.Max(0f, volumeMultiplier);
        pitchMultiplier = Mathf.Clamp(pitchMultiplier, 0.1f, 3f);
        audioListenerRefreshInterval = Mathf.Max(0.05f, audioListenerRefreshInterval);
        dopplerLevel = Mathf.Clamp(dopplerLevel, 0f, 5f);
        fadeInTime = Mathf.Max(0f, fadeInTime);
        fadeOutTime = Mathf.Max(0f, fadeOutTime);
        labelHeightOffset = Mathf.Max(0f, labelHeightOffset);

        if (audioSource != null)
            PrepareAudioSource(audioSource);
    }

    private static float ResolveRandomMultiplier(Vector2 range)
    {
        Vector2 orderedRange = new Vector2(
            Mathf.Min(range.x, range.y),
            Mathf.Max(range.x, range.y));

        return Random.Range(orderedRange.x, orderedRange.y);
    }

    private void OnDrawGizmos()
    {
        if (drawDistanceGizmosWhenNotSelected)
            DrawDistanceGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (drawDistanceGizmosWhenSelected)
            DrawDistanceGizmos();
    }

    private void DrawDistanceGizmos()
    {
        AudioCue cue = loopCue;
        if (cue == null || !cue.DrawDistanceGizmos || !cue.Is3D || cue.SpatialBlend <= 0.001f)
            return;

        Transform anchor = playbackAnchor != null ? playbackAnchor : transform;
        Vector3 origin = anchor.position;
        float minDistance = Mathf.Max(0.01f, cue.MinDistance);
        float maxDistance = Mathf.Max(minDistance, cue.MaxDistance);

        Gizmos.color = cue.MinDistanceGizmoColor;
        Gizmos.DrawWireSphere(origin, minDistance);
        Gizmos.color = cue.MaxDistanceGizmoColor;
        Gizmos.DrawWireSphere(origin, maxDistance);

#if UNITY_EDITOR
        if (drawLabels)
            Handles.Label(origin + Vector3.up * labelHeightOffset, $"{cue.name}\nMin {minDistance:0.##}m | Max {maxDistance:0.##}m");
#endif
    }
}
