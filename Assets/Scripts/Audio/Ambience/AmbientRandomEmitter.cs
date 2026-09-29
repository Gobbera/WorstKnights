using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Audio/Ambience/Ambient Random Emitter")]
public sealed class AmbientRandomEmitter : MonoBehaviour
{
    [Header("Cues")]
    [SerializeField] private AudioCue[] cues = Array.Empty<AudioCue>();

    [Header("Timing")]
    [SerializeField] private Vector2 startupDelayRange = new Vector2(1f, 4f);
    [SerializeField] private Vector2 intervalRange = new Vector2(10f, 24f);
    [SerializeField] [Range(0f, 1f)] private float chancePerInterval = 1f;
    [SerializeField] private bool avoidImmediateRepeats = true;

    [Header("Playback")]
    [SerializeField] private AmbientRandomPlaybackMode playbackMode = AmbientRandomPlaybackMode.AtEmitterPosition;
    [SerializeField] private Transform playbackAnchor;
    [SerializeField] private bool requireActiveAudioListener = true;
    [SerializeField] [Min(0.05f)] private float audioListenerRefreshInterval = 0.5f;

    [Header("Debug Gizmos")]
    [SerializeField] private bool drawDistanceGizmosWhenSelected = true;
    [SerializeField] private bool drawLabels;

    private readonly Dictionary<AudioCue, float> cueLastPlaybackTimes = new Dictionary<AudioCue, float>();

    private AudioCue lastCue;
    private AudioListener cachedAudioListener;
    private float nextListenerRefreshTime;
    private float nextPlaybackTime;

    private void OnEnable()
    {
        RefreshAudioListener(force: true);
        ScheduleNextPlayback(startup: true);
    }

    private void Update()
    {
        RefreshAudioListener(force: false);

        if (requireActiveAudioListener && cachedAudioListener == null)
            return;

        if (Time.time + 0.0001f < nextPlaybackTime)
            return;

        ScheduleNextPlayback(startup: false);

        if (UnityEngine.Random.value > chancePerInterval)
            return;

        AudioCue cue = GetRandomCue();
        if (cue == null || !CanPlayCue(cue))
            return;

        PlayCue(cue);
        lastCue = cue;
        cueLastPlaybackTimes[cue] = Time.time;
    }

    private AudioCue GetRandomCue()
    {
        if (cues == null || cues.Length == 0)
            return null;

        int playableCount = 0;
        for (int i = 0; i < cues.Length; i++)
        {
            if (IsPlayableCue(cues[i]))
                playableCount++;
        }

        if (playableCount == 0)
            return null;

        int startIndex = UnityEngine.Random.Range(0, cues.Length);
        AudioCue fallbackCue = null;
        for (int i = 0; i < cues.Length; i++)
        {
            AudioCue cue = cues[(startIndex + i) % cues.Length];
            if (!IsPlayableCue(cue))
                continue;

            if (fallbackCue == null)
                fallbackCue = cue;

            if (!avoidImmediateRepeats || playableCount <= 1 || cue != lastCue)
                return cue;
        }

        return fallbackCue;
    }

    private bool CanPlayCue(AudioCue cue)
    {
        if (!IsPlayableCue(cue))
            return false;

        if (cue.Cooldown <= 0f)
            return true;

        return !cueLastPlaybackTimes.TryGetValue(cue, out float lastTime)
            || Time.time - lastTime >= cue.Cooldown;
    }

    private void PlayCue(AudioCue cue)
    {
        Transform anchor = ResolvePlaybackAnchor();
        switch (playbackMode)
        {
            case AmbientRandomPlaybackMode.FollowAnchor:
                GameAudioService.Play(cue, anchor != null ? anchor : transform);
                break;

            case AmbientRandomPlaybackMode.TwoDimensionalOrListener:
                if (cue.Is3D)
                    GameAudioService.Play(cue, anchor != null ? anchor.position : transform.position);
                else
                    GameAudioService.Play(cue);
                break;

            case AmbientRandomPlaybackMode.AtEmitterPosition:
            default:
                GameAudioService.Play(cue, anchor != null ? anchor.position : transform.position);
                break;
        }
    }

    private Transform ResolvePlaybackAnchor()
    {
        if (playbackAnchor != null)
            return playbackAnchor;

        if (playbackMode == AmbientRandomPlaybackMode.TwoDimensionalOrListener && cachedAudioListener != null)
            return cachedAudioListener.transform;

        return transform;
    }

    private void ScheduleNextPlayback(bool startup)
    {
        Vector2 range = startup ? startupDelayRange : intervalRange;
        range = GetOrderedRange(range);
        nextPlaybackTime = Time.time + UnityEngine.Random.Range(range.x, range.y);
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
        if (cues == null)
            cues = Array.Empty<AudioCue>();

        startupDelayRange = ClampRange(startupDelayRange, 0f);
        intervalRange = ClampRange(intervalRange, 0.1f);
        chancePerInterval = Mathf.Clamp01(chancePerInterval);
        audioListenerRefreshInterval = Mathf.Max(0.05f, audioListenerRefreshInterval);
    }

    private static bool IsPlayableCue(AudioCue cue)
    {
        return cue != null && cue.HasPlayableClip();
    }

    private static Vector2 ClampRange(Vector2 range, float minimum)
    {
        range = GetOrderedRange(range);
        range.x = Mathf.Max(minimum, range.x);
        range.y = Mathf.Max(range.x, range.y);
        return range;
    }

    private static Vector2 GetOrderedRange(Vector2 range)
    {
        return new Vector2(
            Mathf.Min(range.x, range.y),
            Mathf.Max(range.x, range.y));
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDistanceGizmosWhenSelected || cues == null)
            return;

        Transform anchor = playbackAnchor != null ? playbackAnchor : transform;
        Vector3 origin = anchor.position;
        int labelIndex = 0;
        for (int i = 0; i < cues.Length; i++)
        {
            AudioCue cue = cues[i];
            if (cue == null || !cue.DrawDistanceGizmos || !cue.Is3D || cue.SpatialBlend <= 0.001f)
                continue;

            float minDistance = Mathf.Max(0.01f, cue.MinDistance);
            float maxDistance = Mathf.Max(minDistance, cue.MaxDistance);

            Gizmos.color = cue.MinDistanceGizmoColor;
            Gizmos.DrawWireSphere(origin, minDistance);
            Gizmos.color = cue.MaxDistanceGizmoColor;
            Gizmos.DrawWireSphere(origin, maxDistance);

#if UNITY_EDITOR
            if (drawLabels)
                UnityEditor.Handles.Label(origin + Vector3.up * (0.25f + labelIndex * 0.35f), $"{cue.name}\nMin {minDistance:0.##}m | Max {maxDistance:0.##}m");
#endif

            labelIndex++;
        }
    }
}
