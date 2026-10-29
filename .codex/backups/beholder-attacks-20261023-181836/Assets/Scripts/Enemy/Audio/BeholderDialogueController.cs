using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BeholderDialogueController : MonoBehaviour
{
    private struct ReplicatedCue
    {
        public int sequence;
        public EnemyState state;
        public BeholderMood mood;
        public int cueIndex;
        public int clipIndex;
        public float volume;
        public float pitch;
        public double startedAt;
    }

    [Serializable]
    public sealed class StateLines
    {
        public EnemyState state;
        public AudioCue[] neutral = Array.Empty<AudioCue>();
        public AudioCue[] irritated = Array.Empty<AudioCue>();
        public AudioCue[] angry = Array.Empty<AudioCue>();
        public AudioCue[] infuriated = Array.Empty<AudioCue>();
        public Vector2 intervalSeconds = new Vector2(8f, 14f);

        public AudioCue[] GetCues(BeholderMood mood)
        {
            AudioCue[] moodCues = mood switch
            {
                BeholderMood.Irritated => irritated,
                BeholderMood.Angry => angry,
                BeholderMood.Infuriated => infuriated,
                _ => neutral
            };

            return moodCues != null && moodCues.Length > 0 ? moodCues : neutral;
        }
    }

    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private BeholderMoodController moodController;
    [SerializeField] private AudioSource voiceSource;
    [SerializeField] private StateLines[] linesByState = Array.Empty<StateLines>();
    [SerializeField] [Min(0f)] private float chaseSearchTransitionCooldown = 2f;

    private readonly Dictionary<int, int> lastCueByCategory = new Dictionary<int, int>();
    private readonly Dictionary<AudioCue, int> lastClipByCue = new Dictionary<AudioCue, int>();
    private readonly Dictionary<AudioCue, float> lastPlaybackTimes = new Dictionary<AudioCue, float>();
    private readonly Queue<ReplicatedCue> pendingReplicatedCues = new Queue<ReplicatedCue>();
    private float nextSpeakTime;
    private int cueSequence;
    private int lastAppliedCueSequence;
    private EnemyState cueState;
    private BeholderMood cueMood;
    private int cueIndex = -1;
    private int cueClipIndex = -1;
    private float cueVolume;
    private float cuePitch = 1f;
    private double cueStartedAt;
    private bool reflectionCuePending;
    private bool searchWindingDown;
    private bool alertCuePending;
    private bool chaseCuePending;
    private bool hasObservedState;
    private EnemyState observedState;
    private float voiceEndsAt;

    public int CueSequence => cueSequence;
    public EnemyState CueState => cueState;
    public BeholderMood CueMood => cueMood;
    public int CueIndex => cueIndex;
    public int CueClipIndex => cueClipIndex;
    public float CueVolume => cueVolume;
    public float CuePitch => cuePitch;
    public double CueStartedAt => cueStartedAt;
    public bool IsSearchLinePlaying => voiceSource != null && voiceSource.isPlaying && cueState == EnemyState.Search;

    public void SetSearchWindingDown(bool windingDown)
    {
        searchWindingDown = windingDown;
    }

    public void OnAuthorityChanged(bool hasAuthority)
    {
        pendingReplicatedCues.Clear();
        if (!hasAuthority)
        {
            lastAppliedCueSequence = Mathf.Max(lastAppliedCueSequence, cueSequence);
            reflectionCuePending = false;
            alertCuePending = false;
            chaseCuePending = false;
        }
        else
        {
            cueSequence = Mathf.Max(cueSequence, lastAppliedCueSequence);
        }

        hasObservedState = false;
    }

    private void Awake()
    {
        ResolveReferences();
        PrepareVoiceSource();
        nextSpeakTime = Time.time + UnityEngine.Random.Range(2f, 4f);
    }

    private void Update()
    {
        ResolveReferences();
        if (enemyHealth != null && !enemyHealth.IsAlive)
        {
            if (voiceSource != null && voiceSource.isPlaying)
                voiceSource.Stop();
            return;
        }

        if (enemySetup != null && !enemySetup.HasAuthority)
        {
            PlayPendingReplicatedCues();
            return;
        }

        if (enemyBrain == null || moodController == null || voiceSource == null)
            return;

        EnemyState state = enemyBrain.CurrentState;
        if (state == EnemyState.Traversal)
            return;

        ObserveState(state);

        if (alertCuePending)
        {
            if (state != EnemyState.Alert && state != EnemyState.Chase && state != EnemyState.Attack)
                alertCuePending = false;
            else if (voiceSource.isPlaying)
                return;
            else
            {
                alertCuePending = false;
                TryPlayStateCue(EnemyState.Alert);
                return;
            }
        }

        if (chaseCuePending)
        {
            if (state != EnemyState.Chase && state != EnemyState.Attack)
                chaseCuePending = false;
            else if (voiceSource.isPlaying)
                return;
            else
            {
                chaseCuePending = false;
                TryPlayStateCue(EnemyState.Chase);
                return;
            }
        }

        if (reflectionCuePending)
        {
            if (state != EnemyState.Reflection)
                reflectionCuePending = false;
            else if (voiceSource.isPlaying)
                return;
            else
            {
                reflectionCuePending = false;
                TryPlayStateCue(EnemyState.Reflection);
                return;
            }
        }

        if (voiceSource.isPlaying || Time.time < nextSpeakTime)
            return;

        if (searchWindingDown && state == EnemyState.Search)
            return;

        if (state == EnemyState.Dead || state == EnemyState.PreSpawn
            || state == EnemyState.Spawning || state == EnemyState.Reflection)
            return;

        TryPlayStateCue(state);
    }

    private void ObserveState(EnemyState state)
    {
        if (hasObservedState && observedState == state)
            return;

        EnemyState previous = observedState;
        bool hadPrevious = hasObservedState;
        hasObservedState = true;
        observedState = state;

        if (hadPrevious && (((previous == EnemyState.Chase || previous == EnemyState.Attack)
            && state == EnemyState.Search) || (previous == EnemyState.Search && state == EnemyState.Chase)))
        {
            float availableAt = Mathf.Max(Time.time, voiceEndsAt) + chaseSearchTransitionCooldown;
            nextSpeakTime = Mathf.Min(nextSpeakTime, availableAt);
        }

        if (state == EnemyState.Alert)
        {
            reflectionCuePending = false;
            chaseCuePending = false;
            if (voiceSource.isPlaying && cueState != EnemyState.Chase)
                voiceSource.Stop();
            alertCuePending = true;
        }
        else if (state == EnemyState.Chase)
        {
            chaseCuePending = true;
        }
        else if (state == EnemyState.Reflection)
        {
            reflectionCuePending = true;
        }
    }

    private bool TryPlayStateCue(EnemyState state)
    {
        if (moodController == null || voiceSource == null || voiceSource.isPlaying
            || (enemyHealth != null && !enemyHealth.IsAlive))
            return false;

        BeholderMood mood = moodController.CurrentMood;
        StateLines bank = FindBank(state);
        AudioCue[] cues = bank?.GetCues(mood);
        if (cues == null || cues.Length == 0)
            return false;

        int category = (int)state * 4 + (int)mood;
        int selectedCueIndex = ChooseCueIndex(cues, category, state == EnemyState.Alert);
        if (selectedCueIndex < 0)
            return false;

        AudioCue selectedCue = cues[selectedCueIndex];
        int selectedClipIndex = ChooseClipIndex(selectedCue);
        if (selectedClipIndex < 0)
            return false;

        AudioClip clip = selectedCue.Clips[selectedClipIndex];
        PlayCue(selectedCue, clip, 0f);
        lastPlaybackTimes[selectedCue] = Time.unscaledTime;
        cueSequence++;
        cueState = state;
        cueMood = mood;
        cueIndex = selectedCueIndex;
        cueClipIndex = selectedClipIndex;
        cueVolume = voiceSource.volume;
        cuePitch = voiceSource.pitch;
        cueStartedAt = ResolveNetworkTime();
        voiceEndsAt = Time.time + PlaybackDuration(clip, cuePitch);

        float minimum = Mathf.Max(0f, bank.intervalSeconds.x);
        float maximum = Mathf.Max(minimum, bank.intervalSeconds.y);
        nextSpeakTime = Time.time + PlaybackDuration(clip, cuePitch) + UnityEngine.Random.Range(minimum, maximum);
        return true;
    }

    public void ApplyReplicatedCue(
        int sequence,
        EnemyState state,
        BeholderMood mood,
        int selectedCueIndex,
        int selectedClipIndex,
        float volume,
        float pitch,
        double startedAt)
    {
        if (sequence <= lastAppliedCueSequence)
            return;

        if (sequence == cueSequence && voiceSource != null && voiceSource.isPlaying)
        {
            lastAppliedCueSequence = sequence;
            return;
        }

        lastAppliedCueSequence = sequence;
        ReplicatedCue cue = new ReplicatedCue
        {
            sequence = sequence,
            state = state,
            mood = mood,
            cueIndex = selectedCueIndex,
            clipIndex = selectedClipIndex,
            volume = volume,
            pitch = pitch,
            startedAt = startedAt
        };

        bool protectedChasePlaying = voiceSource != null && voiceSource.isPlaying
            && cueState == EnemyState.Chase;
        if (cue.state == EnemyState.Alert || !protectedChasePlaying)
            pendingReplicatedCues.Clear();

        if (protectedChasePlaying)
        {
            pendingReplicatedCues.Enqueue(cue);
            return;
        }

        PlayReplicatedCue(cue, resumeAtNetworkTime: true);
    }

    private void PlayPendingReplicatedCues()
    {
        if (voiceSource == null || voiceSource.isPlaying)
            return;

        while (pendingReplicatedCues.Count > 0 && !voiceSource.isPlaying)
            PlayReplicatedCue(pendingReplicatedCues.Dequeue(), resumeAtNetworkTime: false);
    }

    private void PlayReplicatedCue(ReplicatedCue cue, bool resumeAtNetworkTime)
    {
        cueSequence = Mathf.Max(cueSequence, cue.sequence);
        cueState = cue.state;
        cueMood = cue.mood;
        cueIndex = cue.cueIndex;
        cueClipIndex = cue.clipIndex;
        cueVolume = cue.volume;
        cuePitch = cue.pitch;
        cueStartedAt = cue.startedAt;

        StateLines bank = FindBank(cue.state);
        AudioCue[] cues = bank?.GetCues(cue.mood);
        if (cues == null || cue.cueIndex < 0 || cue.cueIndex >= cues.Length)
            return;

        AudioCue selectedCue = cues[cue.cueIndex];
        AudioClip[] clips = selectedCue != null ? selectedCue.Clips : null;
        if (clips == null || cue.clipIndex < 0 || cue.clipIndex >= clips.Length || clips[cue.clipIndex] == null)
            return;

        lastCueByCategory[(int)cue.state * 4 + (int)cue.mood] = cue.cueIndex;
        lastClipByCue[selectedCue] = cue.clipIndex;
        float elapsed = resumeAtNetworkTime
            ? (float)Math.Max(0d, ResolveNetworkTime() - cue.startedAt)
            : 0f;
        lastPlaybackTimes[selectedCue] = Time.unscaledTime - elapsed;
        AudioClip clip = clips[cue.clipIndex];
        float duration = PlaybackDuration(clip, cue.pitch);
        if (elapsed >= duration)
            return;

        voiceEndsAt = Time.time + duration - elapsed;

        float minimum = Mathf.Max(0f, bank.intervalSeconds.x);
        float maximum = Mathf.Max(minimum, bank.intervalSeconds.y);
        nextSpeakTime = Time.time + duration - elapsed + UnityEngine.Random.Range(minimum, maximum);
        float offset = cue.pitch < 0f ? clip.length - elapsed * Mathf.Abs(cue.pitch) : elapsed * Mathf.Abs(cue.pitch);
        PlayCue(selectedCue, clip, offset, cue.volume, cue.pitch);
    }

    private StateLines FindBank(EnemyState state)
    {
        if (linesByState == null)
            return null;

        for (int i = 0; i < linesByState.Length; i++)
        {
            if (linesByState[i] != null && linesByState[i].state == state)
                return linesByState[i];
        }

        return null;
    }

    private int ChooseCueIndex(AudioCue[] cues, int category, bool ignoreCooldown)
    {
        int validCount = 0;
        for (int i = 0; i < cues.Length; i++)
        {
            if (IsCueAvailable(cues[i], ignoreCooldown))
                validCount++;
        }

        if (validCount == 0)
            return -1;

        int previous = lastCueByCategory.TryGetValue(category, out int last) ? last : -1;
        bool excludePrevious = validCount > 1 && previous >= 0 && previous < cues.Length
            && IsCueAvailable(cues[previous], ignoreCooldown);
        int chosen = UnityEngine.Random.Range(0, excludePrevious ? validCount - 1 : validCount);
        for (int i = 0, validIndex = 0; i < cues.Length; i++)
        {
            if (!IsCueAvailable(cues[i], ignoreCooldown))
                continue;

            if (excludePrevious && i == previous)
                continue;

            if (validIndex++ == chosen)
            {
                lastCueByCategory[category] = i;
                return i;
            }
        }

        return -1;
    }

    private bool IsCueAvailable(AudioCue cue, bool ignoreCooldown)
    {
        if (cue == null || !cue.HasPlayableClip())
            return false;

        return ignoreCooldown || cue.Cooldown <= 0f
            || !lastPlaybackTimes.TryGetValue(cue, out float lastTime)
            || Time.unscaledTime - lastTime >= cue.Cooldown;
    }

    private int ChooseClipIndex(AudioCue cue)
    {
        AudioClip[] clips = cue.Clips;
        int validCount = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                validCount++;
        }

        if (validCount == 0)
            return -1;

        int previous = lastClipByCue.TryGetValue(cue, out int last) ? last : -1;
        bool excludePrevious = validCount > 1 && previous >= 0 && previous < clips.Length && clips[previous] != null;
        int chosen = UnityEngine.Random.Range(0, excludePrevious ? validCount - 1 : validCount);
        for (int i = 0, validIndex = 0; i < clips.Length; i++)
        {
            if (clips[i] == null || (excludePrevious && i == previous))
                continue;

            if (validIndex++ == chosen)
            {
                lastClipByCue[cue] = i;
                return i;
            }
        }

        return -1;
    }

    private void PlayCue(AudioCue cue, AudioClip clip, float offset, float? volume = null, float? pitch = null)
    {
        if (voiceSource == null || cue == null || clip == null)
            return;

        voiceSource.Stop();
        if (volume.HasValue && pitch.HasValue)
            cue.ConfigureAudioSource(voiceSource, clip, volume.Value, pitch.Value);
        else
            cue.ConfigureAudioSource(voiceSource, clip);
        voiceSource.loop = false;
        voiceSource.dopplerLevel = 0f;
        float position = offset <= 0f && voiceSource.pitch < 0f ? clip.length : offset;
        voiceSource.time = Mathf.Clamp(position, 0f, Mathf.Max(0f, clip.length - 0.01f));
        voiceSource.Play();
    }

    private static float PlaybackDuration(AudioClip clip, float pitch)
    {
        return clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch));
    }

    private void PrepareVoiceSource()
    {
        if (voiceSource == null)
            return;

        voiceSource.playOnAwake = false;
        voiceSource.loop = false;
        voiceSource.dopplerLevel = 0f;
    }

    private void ResolveReferences()
    {
        if (enemySetup == null) enemySetup = GetComponent<EnemySetup>();
        if (enemyBrain == null) enemyBrain = GetComponent<EnemyBrain>();
        if (enemyHealth == null) enemyHealth = GetComponent<EnemyHealth>();
        if (moodController == null) moodController = GetComponent<BeholderMoodController>();
    }

    private static double ResolveNetworkTime()
    {
        return PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
    }
}
