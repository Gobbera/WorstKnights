using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public class GameAudioService : MonoBehaviour
{
    private const string ServiceObjectName = "GameAudioService";
    private const string PoolRootName = "VoicePool";
    private const string ActiveRootName = "ActiveVoices";
    private const int DefaultInitialPoolSize = 8;
    private const int DefaultMaxPoolSize = 24;

    private sealed class PooledVoice
    {
        public readonly GameObject GameObject;
        public readonly AudioSource Source;
        public readonly AudioFollowTarget FollowTarget;

        public PooledVoice(GameObject gameObject, AudioSource source, AudioFollowTarget followTarget)
        {
            GameObject = gameObject;
            Source = source;
            FollowTarget = followTarget;
        }
    }

    private static GameAudioService instance;

    [Header("Pool")]
    [SerializeField] [Min(1)] private int initialPoolSize = DefaultInitialPoolSize;
    [SerializeField] [Min(1)] private int maxPoolSize = DefaultMaxPoolSize;
    [SerializeField] private bool persistAcrossScenes = true;

    [Header("Fallback Mixer Groups")]
    [SerializeField] private AudioMixerGroup defaultUiMixerGroup;
    [SerializeField] private AudioMixerGroup defaultWorldMixerGroup;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging;

    private readonly Queue<PooledVoice> availableVoices = new Queue<PooledVoice>();
    private readonly List<PooledVoice> activeVoices = new List<PooledVoice>();
    private bool initialized;
    private Transform poolRoot;
    private Transform activeRoot;

    public static GameAudioService Instance => EnsureInstance();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        InitializeIfNeeded();
    }

    private void Update()
    {
        ReclaimFinishedVoices();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static AudioSource Play(AudioCue cue)
    {
        return Instance.PlayCue(cue);
    }

    public static AudioSource Play(AudioCue cue, Vector3 worldPosition)
    {
        return Instance.PlayCue(cue, worldPosition);
    }

    public static AudioSource Play(AudioCue cue, Transform followTarget)
    {
        return Instance.PlayCue(cue, followTarget);
    }

    public static void Stop(AudioSource source)
    {
        if (source == null || instance == null)
            return;

        instance.StopVoice(source);
    }

    public AudioSource PlayCue(AudioCue cue)
    {
        InitializeIfNeeded();
        return cue != null && cue.Is3D
            ? PlayCue(cue, transform.position)
            : PlayInternal(cue, null, transform.position);
    }

    public AudioSource PlayCue(AudioCue cue, Vector3 worldPosition)
    {
        InitializeIfNeeded();
        return PlayInternal(cue, null, worldPosition);
    }

    public AudioSource PlayCue(AudioCue cue, Transform followTarget)
    {
        InitializeIfNeeded();
        Vector3 startPosition = followTarget != null ? followTarget.position : transform.position;
        return PlayInternal(cue, followTarget, startPosition);
    }

    public void StopVoice(AudioSource source)
    {
        if (source == null)
            return;

        for (int i = activeVoices.Count - 1; i >= 0; i--)
        {
            PooledVoice voice = activeVoices[i];
            if (voice.Source != source)
                continue;

            ReleaseVoiceAtIndex(i);
            return;
        }
    }

    private static GameAudioService EnsureInstance()
    {
        if (instance != null)
            return instance;

        GameAudioService[] services = FindObjectsByType<GameAudioService>(FindObjectsInactive.Include);
        if (services.Length > 0)
        {
            instance = services[0];
            instance.InitializeIfNeeded();
            return instance;
        }

        GameObject serviceObject = new GameObject(ServiceObjectName);
        instance = serviceObject.AddComponent<GameAudioService>();
        return instance;
    }

    private void InitializeIfNeeded()
    {
        if (initialized)
            return;

        initialized = true;
        initialPoolSize = Mathf.Max(1, initialPoolSize);
        maxPoolSize = Mathf.Max(initialPoolSize, maxPoolSize);

        if (persistAcrossScenes)
            DontDestroyOnLoad(gameObject);

        EnsureVoiceRoots();

        for (int i = availableVoices.Count + activeVoices.Count; i < initialPoolSize; i++)
            availableVoices.Enqueue(CreateVoice(i));
    }

    private AudioSource PlayInternal(AudioCue cue, Transform followTarget, Vector3 startPosition)
    {
        if (cue == null)
        {
            if (verboseLogging)
                Debug.LogWarning("[GameAudioService] Ignorando playback de AudioCue nulo.", this);

            return null;
        }

        AudioClip clip = cue.GetRandomClip();
        if (clip == null)
        {
            Debug.LogWarning($"[GameAudioService] '{cue.name}' nao possui nenhum AudioClip valido.", cue);
            return null;
        }

        PooledVoice voice = AcquireVoice();
        if (voice == null)
        {
            Debug.LogWarning($"[GameAudioService] Pool esgotado para tocar '{cue.name}'.", this);
            return null;
        }

        ConfigureVoice(voice, cue, clip, followTarget, startPosition);
        voice.Source.Play();
        return voice.Source;
    }

    private PooledVoice AcquireVoice()
    {
        ReclaimFinishedVoices();

        if (availableVoices.Count > 0)
        {
            PooledVoice pooledVoice = availableVoices.Dequeue();
            activeVoices.Add(pooledVoice);
            return pooledVoice;
        }

        int totalVoiceCount = availableVoices.Count + activeVoices.Count;
        if (totalVoiceCount < Mathf.Max(initialPoolSize, maxPoolSize))
        {
            PooledVoice createdVoice = CreateVoice(totalVoiceCount);
            activeVoices.Add(createdVoice);
            return createdVoice;
        }

        if (activeVoices.Count == 0)
            return null;

        ReleaseVoiceAtIndex(0);
        if (availableVoices.Count == 0)
            return null;

        PooledVoice recycledVoice = availableVoices.Dequeue();
        activeVoices.Add(recycledVoice);
        return recycledVoice;
    }

    private PooledVoice CreateVoice(int index)
    {
        EnsureVoiceRoots();

        GameObject voiceObject = new GameObject($"AudioVoice_{index:00}");
        voiceObject.transform.SetParent(poolRoot, false);

        AudioSource audioSource = voiceObject.AddComponent<AudioSource>();
        AudioFollowTarget followTarget = voiceObject.AddComponent<AudioFollowTarget>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.dopplerLevel = 0f;

        return new PooledVoice(voiceObject, audioSource, followTarget);
    }

    private void ConfigureVoice(PooledVoice voice, AudioCue cue, AudioClip clip, Transform followTarget, Vector3 startPosition)
    {
        Transform voiceTransform = voice.GameObject.transform;
        voiceTransform.SetParent(activeRoot, true);
        voiceTransform.SetPositionAndRotation(startPosition, Quaternion.identity);

        AudioSource audioSource = voice.Source;
        audioSource.Stop();
        audioSource.clip = clip;
        audioSource.loop = cue.Loop;
        audioSource.volume = cue.GetRandomizedVolume();
        audioSource.pitch = cue.GetRandomizedPitch();
        audioSource.spatialBlend = cue.Is3D ? cue.SpatialBlend : 0f;
        audioSource.spread = cue.Is3D ? cue.Spread : 0f;
        audioSource.minDistance = cue.MinDistance;
        audioSource.maxDistance = cue.MaxDistance;
        audioSource.outputAudioMixerGroup = ResolveMixerGroup(cue);

        if (cue.Is3D && cue.Anchor == AudioPlaybackAnchor.FollowTransform && followTarget != null)
            voice.FollowTarget.SetTarget(followTarget);
        else
            voice.FollowTarget.ClearTarget();
    }

    private AudioMixerGroup ResolveMixerGroup(AudioCue cue)
    {
        if (cue != null && cue.MixerGroup != null)
            return cue.MixerGroup;

        return cue != null && cue.Is3D
            ? defaultWorldMixerGroup
            : defaultUiMixerGroup;
    }

    private void ReclaimFinishedVoices()
    {
        for (int i = activeVoices.Count - 1; i >= 0; i--)
        {
            AudioSource audioSource = activeVoices[i].Source;
            if (audioSource == null || audioSource.isPlaying)
                continue;

            ReleaseVoiceAtIndex(i);
        }
    }

    private void ReleaseVoiceAtIndex(int index)
    {
        if (index < 0 || index >= activeVoices.Count)
            return;

        PooledVoice voice = activeVoices[index];
        activeVoices.RemoveAt(index);
        ResetVoice(voice);
        availableVoices.Enqueue(voice);
    }

    private void ResetVoice(PooledVoice voice)
    {
        if (voice == null || voice.Source == null)
            return;

        voice.FollowTarget.ClearTarget();
        voice.Source.Stop();
        voice.Source.clip = null;
        voice.Source.loop = false;
        voice.Source.volume = 1f;
        voice.Source.pitch = 1f;
        voice.Source.spatialBlend = 0f;
        voice.Source.spread = 0f;
        voice.Source.minDistance = 1f;
        voice.Source.maxDistance = 500f;
        voice.Source.outputAudioMixerGroup = null;
        voice.GameObject.transform.SetParent(poolRoot, false);
        voice.GameObject.transform.localPosition = Vector3.zero;
        voice.GameObject.transform.localRotation = Quaternion.identity;
    }

    private void EnsureVoiceRoots()
    {
        if (poolRoot == null)
            poolRoot = EnsureChildRoot(PoolRootName);

        if (activeRoot == null)
            activeRoot = EnsureChildRoot(ActiveRootName);
    }

    private Transform EnsureChildRoot(string rootName)
    {
        Transform child = transform.Find(rootName);
        if (child != null)
            return child;

        GameObject childObject = new GameObject(rootName);
        childObject.transform.SetParent(transform, false);
        return childObject.transform;
    }
}

[DisallowMultipleComponent]
internal sealed class AudioFollowTarget : MonoBehaviour
{
    private Transform target;

    public void SetTarget(Transform followTarget)
    {
        target = followTarget;
        enabled = target != null;

        if (target != null)
            transform.SetPositionAndRotation(target.position, target.rotation);
    }

    public void ClearTarget()
    {
        target = null;
        enabled = false;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            enabled = false;
            return;
        }

        transform.SetPositionAndRotation(target.position, target.rotation);
    }
}
