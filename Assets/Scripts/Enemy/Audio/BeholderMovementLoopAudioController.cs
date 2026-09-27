using System;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMotor))]
[AddComponentMenu("Enemy/Beholder Movement Loop Audio Controller")]
public sealed class BeholderMovementLoopAudioController : MonoBehaviour
{
    [Serializable]
    private sealed class LoopLayer
    {
        [SerializeField] private AudioCue cue;
        [SerializeField] private AudioClip fallbackClip;
        [SerializeField] private AudioSource source;

        [SerializeField] [Range(0f, 1f)] private float volumeAtZeroIntensity;
        [SerializeField] [Range(0f, 1f)] private float volumeAtFullIntensity = 1f;
        [SerializeField] [Min(0f)] private float pitchAtZeroIntensity = 1f;
        [SerializeField] [Min(0f)] private float pitchAtFullIntensity = 1f;
        [SerializeField] private AnimationCurve volumeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve pitchCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] [Min(0f)] private float silentVolumeThreshold = 0.001f;
        [SerializeField] private bool stopWhenSilent = true;

        private AudioCue configuredCue;
        private AudioClip configuredFallbackClip;
        private float configuredBaseVolume = 1f;
        private float configuredBasePitch = 1f;

        public static LoopLayer CreateHover()
        {
            return new LoopLayer
            {
                volumeAtZeroIntensity = 0.28f,
                volumeAtFullIntensity = 0.58f,
                pitchAtZeroIntensity = 0.92f,
                pitchAtFullIntensity = 1.14f,
                volumeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                pitchCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                stopWhenSilent = false
            };
        }

        public static LoopLayer CreateMovement()
        {
            return new LoopLayer
            {
                volumeAtZeroIntensity = 0f,
                volumeAtFullIntensity = 0.9f,
                pitchAtZeroIntensity = 0.86f,
                pitchAtFullIntensity = 1.32f,
                volumeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                pitchCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                stopWhenSilent = true
            };
        }

        public void UpdatePlayback(
            Transform fallbackAnchor,
            bool autoCreateSource,
            AudioMixerGroup fallbackMixerGroup,
            float fallbackSpatialBlend,
            float fallbackSpread,
            float fallbackMinDistance,
            float fallbackMaxDistance,
            AudioRolloffMode fallbackRolloffMode,
            float intensity,
            float masterVolume,
            bool shouldPlay)
        {
            if (!HasPlayableContent())
            {
                Stop();
                return;
            }

            ResolveSource(fallbackAnchor, autoCreateSource);
            if (source == null)
                return;

            if (!EnsureConfigured(
                    fallbackMixerGroup,
                    fallbackSpatialBlend,
                    fallbackSpread,
                    fallbackMinDistance,
                    fallbackMaxDistance,
                    fallbackRolloffMode))
            {
                Stop();
                return;
            }

            if (!shouldPlay)
            {
                source.volume = 0f;
                source.Stop();
                return;
            }

            float safeIntensity = Mathf.Clamp01(intensity);
            float volumeT = Mathf.Clamp01(EvaluateCurve(volumeCurve, safeIntensity));
            float pitchT = Mathf.Clamp01(EvaluateCurve(pitchCurve, safeIntensity));
            source.volume = Mathf.Max(0f, configuredBaseVolume * Mathf.Lerp(volumeAtZeroIntensity, volumeAtFullIntensity, volumeT) * Mathf.Clamp01(masterVolume));
            source.pitch = Mathf.Clamp(configuredBasePitch * Mathf.Lerp(pitchAtZeroIntensity, pitchAtFullIntensity, pitchT), -3f, 3f);

            bool audible = source.clip != null && source.volume > silentVolumeThreshold;
            if (audible)
            {
                if (!source.isPlaying)
                    source.Play();

                return;
            }

            if (stopWhenSilent)
                source.Stop();
        }

        public void Stop()
        {
            if (source == null)
                return;

            source.Stop();
        }

        public void Validate()
        {
            if (volumeCurve == null || volumeCurve.length == 0)
                volumeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

            if (pitchCurve == null || pitchCurve.length == 0)
                pitchCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

            volumeAtZeroIntensity = Mathf.Clamp01(volumeAtZeroIntensity);
            volumeAtFullIntensity = Mathf.Clamp01(volumeAtFullIntensity);
            pitchAtZeroIntensity = Mathf.Max(0f, pitchAtZeroIntensity);
            pitchAtFullIntensity = Mathf.Max(0f, pitchAtFullIntensity);
            silentVolumeThreshold = Mathf.Max(0f, silentVolumeThreshold);
        }

        private bool HasPlayableContent()
        {
            if (cue != null)
                return cue.HasPlayableClip();

            return fallbackClip != null;
        }

        private void ResolveSource(Transform fallbackAnchor, bool autoCreateSource)
        {
            if (source != null || !autoCreateSource)
                return;

            Transform anchor = fallbackAnchor != null ? fallbackAnchor : null;
            if (anchor == null)
                return;

            source = anchor.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
        }

        private bool EnsureConfigured(
            AudioMixerGroup fallbackMixerGroup,
            float fallbackSpatialBlend,
            float fallbackSpread,
            float fallbackMinDistance,
            float fallbackMaxDistance,
            AudioRolloffMode fallbackRolloffMode)
        {
            if (source == null)
                return false;

            bool mustConfigure = configuredCue != cue
                || configuredFallbackClip != fallbackClip
                || source.clip == null;
            if (!mustConfigure)
                return true;

            AudioClip clip = cue != null ? cue.GetRandomClip() : fallbackClip;
            if (clip == null)
            {
                configuredCue = null;
                configuredFallbackClip = null;
                source.clip = null;
                return false;
            }

            source.Stop();
            source.playOnAwake = false;
            source.loop = true;
            source.dopplerLevel = 0f;

            if (cue != null)
            {
                cue.ConfigureAudioSource(source, clip, fallbackMixerGroup, forceLoop: true);
            }
            else
            {
                source.clip = clip;
                source.volume = 1f;
                source.pitch = 1f;
                source.spatialBlend = Mathf.Clamp01(fallbackSpatialBlend);
                source.spread = Mathf.Clamp(fallbackSpread, 0f, 360f);
                source.minDistance = Mathf.Max(0.01f, fallbackMinDistance);
                source.maxDistance = Mathf.Max(source.minDistance, fallbackMaxDistance);
                source.outputAudioMixerGroup = fallbackMixerGroup;
            }

            source.loop = true;
            source.rolloffMode = fallbackRolloffMode;
            configuredBaseVolume = Mathf.Max(0f, source.volume);
            configuredBasePitch = source.pitch;
            configuredCue = cue;
            configuredFallbackClip = fallbackClip;
            return true;
        }

        private static float EvaluateCurve(AnimationCurve curve, float intensity)
        {
            return curve != null && curve.length > 0 ? curve.Evaluate(intensity) : intensity;
        }
    }

    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyMotor enemyMotor;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private Transform audioAnchor;

    [Header("Loop Layers")]
    [SerializeField] private bool autoCreateSources = true;
    [SerializeField] private bool stopOnDisable = true;
    [SerializeField] private LoopLayer hoverLoop = LoopLayer.CreateHover();
    [SerializeField] private LoopLayer movementLoop = LoopLayer.CreateMovement();

    [Header("Fallback Source Settings")]
    [SerializeField] private AudioMixerGroup fallbackMixerGroup;
    [SerializeField] [Range(0f, 1f)] private float fallbackSpatialBlend = 1f;
    [SerializeField] [Range(0f, 360f)] private float fallbackSpread;
    [SerializeField] [Min(0.01f)] private float fallbackMinDistance = 2f;
    [SerializeField] [Min(0.01f)] private float fallbackMaxDistance = 28f;
    [SerializeField] private AudioRolloffMode fallbackRolloffMode = AudioRolloffMode.Logarithmic;

    [Header("Intensity")]
    [SerializeField] private bool usePlanarSpeed = true;
    [SerializeField] [Min(0f)] private float minAudibleSpeed = 0.05f;
    [SerializeField] [Min(0.01f)] private float patrolReferenceSpeed = 2.4f;
    [SerializeField] [Min(0.01f)] private float chaseReferenceSpeed = 4.1f;
    [SerializeField] [Min(0f)] private float intensityRiseSpeed = 3.5f;
    [SerializeField] [Min(0f)] private float intensityFallSpeed = 1.8f;

    [Header("State Floors")]
    [SerializeField] [Range(0f, 1f)] private float preSpawnIntensity;
    [SerializeField] [Range(0f, 1f)] private float idleIntensity = 0.12f;
    [SerializeField] [Range(0f, 1f)] private float patrolIntensity = 0.28f;
    [SerializeField] [Range(0f, 1f)] private float alertIntensity = 0.45f;
    [SerializeField] [Range(0f, 1f)] private float searchIntensity = 0.45f;
    [SerializeField] [Range(0f, 1f)] private float returnIntensity = 0.26f;
    [SerializeField] [Range(0f, 1f)] private float traversalIntensity = 0.55f;
    [SerializeField] [Range(0f, 1f)] private float chaseIntensity = 0.78f;
    [SerializeField] [Range(0f, 1f)] private float attackIntensity = 0.9f;
    [SerializeField] [Range(0f, 1f)] private float stunnedIntensity = 0.04f;

    public float CurrentIntensity => currentIntensity;

    private float currentIntensity;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
    }

    private void OnDisable()
    {
        if (!stopOnDisable)
            return;

        hoverLoop?.Stop();
        movementLoop?.Stop();
        currentIntensity = 0f;
    }

    private void Update()
    {
        ResolveReferences();
        UpdateIntensity(Time.deltaTime);
        UpdateLoops();
    }

    private void ResolveReferences()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyBrain == null)
            enemyBrain = enemySetup != null ? enemySetup.EnemyBrain : GetComponent<EnemyBrain>();

        if (enemyMotor == null)
            enemyMotor = enemySetup != null ? enemySetup.EnemyMotor : GetComponent<EnemyMotor>();

        if (enemyHealth == null)
            enemyHealth = enemySetup != null ? enemySetup.EnemyHealth : GetComponent<EnemyHealth>();

        if (audioAnchor == null && enemySetup != null)
            audioAnchor = enemySetup.ModelRoot;
    }

    private void UpdateIntensity(float deltaTime)
    {
        float targetIntensity = ResolveTargetIntensity();
        float speed = targetIntensity > currentIntensity ? intensityRiseSpeed : intensityFallSpeed;
        if (deltaTime <= 0f || speed <= 0f)
        {
            currentIntensity = targetIntensity;
            return;
        }

        currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity, speed * deltaTime);
    }

    private float ResolveTargetIntensity()
    {
        bool alive = enemyHealth == null || enemyHealth.IsAlive;
        if (!alive)
            return 0f;

        EnemyState state = enemyBrain != null ? enemyBrain.CurrentState : EnemyState.Idle;
        float stateFloor = ResolveStateFloor(state);
        float speedIntensity = ResolveSpeedIntensity(state);
        return Mathf.Clamp01(Mathf.Max(stateFloor, speedIntensity));
    }

    private float ResolveSpeedIntensity(EnemyState state)
    {
        if (!usePlanarSpeed || enemyMotor == null)
            return 0f;

        float referenceSpeed = ResolveReferenceSpeed(state);
        if (referenceSpeed <= minAudibleSpeed)
            return 0f;

        return Mathf.InverseLerp(minAudibleSpeed, referenceSpeed, Mathf.Max(0f, enemyMotor.CurrentPlanarSpeed));
    }

    private float ResolveReferenceSpeed(EnemyState state)
    {
        if (enemyBrain == null)
            return Mathf.Max(0.01f, chaseReferenceSpeed);

        switch (state)
        {
            case EnemyState.Patrol:
                return ResolveSpeed(enemyBrain.PatrolSpeed, patrolReferenceSpeed);
            case EnemyState.Alert:
                return ResolveSpeed(enemyBrain.AlertSpeed, chaseReferenceSpeed);
            case EnemyState.Search:
                return ResolveSpeed(enemyBrain.ResolveActiveSearchSpeed(), chaseReferenceSpeed);
            case EnemyState.Return:
                return ResolveSpeed(enemyBrain.ReturnSpeed, patrolReferenceSpeed);
            case EnemyState.Traversal:
                return ResolveSpeed(enemyBrain.ChaseSpeed, chaseReferenceSpeed);
            case EnemyState.Chase:
            case EnemyState.Attack:
                return ResolveSpeed(enemyBrain.ChaseSpeed, chaseReferenceSpeed);
            default:
                return Mathf.Max(0.01f, chaseReferenceSpeed);
        }
    }

    private float ResolveStateFloor(EnemyState state)
    {
        switch (state)
        {
            case EnemyState.PreSpawn:
            case EnemyState.Spawning:
                return preSpawnIntensity;
            case EnemyState.Patrol:
                return patrolIntensity;
            case EnemyState.Alert:
                return alertIntensity;
            case EnemyState.Search:
                return searchIntensity;
            case EnemyState.Return:
                return returnIntensity;
            case EnemyState.Traversal:
                return traversalIntensity;
            case EnemyState.Chase:
                return chaseIntensity;
            case EnemyState.Attack:
                return attackIntensity;
            case EnemyState.Stunned:
                return stunnedIntensity;
            case EnemyState.Dead:
                return 0f;
            case EnemyState.Idle:
            default:
                return idleIntensity;
        }
    }

    private void UpdateLoops()
    {
        bool alive = enemyHealth == null || enemyHealth.IsAlive;
        bool shouldPlay = alive || currentIntensity > 0.001f;
        float masterVolume = alive ? 1f : currentIntensity;
        Transform resolvedAnchor = audioAnchor != null ? audioAnchor : transform;

        hoverLoop?.UpdatePlayback(
            resolvedAnchor,
            autoCreateSources,
            fallbackMixerGroup,
            fallbackSpatialBlend,
            fallbackSpread,
            fallbackMinDistance,
            fallbackMaxDistance,
            fallbackRolloffMode,
            currentIntensity,
            masterVolume,
            shouldPlay);

        movementLoop?.UpdatePlayback(
            resolvedAnchor,
            autoCreateSources,
            fallbackMixerGroup,
            fallbackSpatialBlend,
            fallbackSpread,
            fallbackMinDistance,
            fallbackMaxDistance,
            fallbackRolloffMode,
            currentIntensity,
            masterVolume,
            shouldPlay);
    }

    private static float ResolveSpeed(float candidate, float fallback)
    {
        return Mathf.Max(0.01f, candidate > 0f ? candidate : fallback);
    }

    private void OnValidate()
    {
        if (hoverLoop == null)
            hoverLoop = LoopLayer.CreateHover();

        if (movementLoop == null)
            movementLoop = LoopLayer.CreateMovement();

        hoverLoop.Validate();
        movementLoop.Validate();

        fallbackSpatialBlend = Mathf.Clamp01(fallbackSpatialBlend);
        fallbackSpread = Mathf.Clamp(fallbackSpread, 0f, 360f);
        fallbackMinDistance = Mathf.Max(0.01f, fallbackMinDistance);
        fallbackMaxDistance = Mathf.Max(fallbackMinDistance, fallbackMaxDistance);
        minAudibleSpeed = Mathf.Max(0f, minAudibleSpeed);
        patrolReferenceSpeed = Mathf.Max(0.01f, patrolReferenceSpeed);
        chaseReferenceSpeed = Mathf.Max(0.01f, chaseReferenceSpeed);
        intensityRiseSpeed = Mathf.Max(0f, intensityRiseSpeed);
        intensityFallSpeed = Mathf.Max(0f, intensityFallSpeed);
    }
}
