using System;
using UnityEngine;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
        private Vector2 configuredVolumeRandomRange = Vector2.one;
        private Vector2 configuredPitchRandomRange = Vector2.one;
        private float configuredVolumeRandomMultiplier = 1f;
        private float configuredPitchRandomMultiplier = 1f;

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
            bool shouldPlay,
            Transform listenerTransform,
            bool useManualDistanceAttenuation,
            bool hardMuteBeyondMaxDistance,
            AnimationCurve manualDistanceFadeCurve)
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

            ApplySourceSettings(
                fallbackMixerGroup,
                fallbackSpatialBlend,
                fallbackSpread,
                fallbackMinDistance,
                fallbackMaxDistance,
                fallbackRolloffMode);

            if (!shouldPlay)
            {
                source.volume = 0f;
                source.Stop();
                return;
            }

            float safeIntensity = Mathf.Clamp01(intensity);
            float volumeT = Mathf.Clamp01(EvaluateCurve(volumeCurve, safeIntensity));
            float pitchT = Mathf.Clamp01(EvaluateCurve(pitchCurve, safeIntensity));
            float distanceMultiplier = ResolveManualDistanceMultiplier(
                listenerTransform,
                useManualDistanceAttenuation,
                hardMuteBeyondMaxDistance,
                manualDistanceFadeCurve);
            source.volume = Mathf.Max(0f, ResolveBaseVolume() * Mathf.Lerp(volumeAtZeroIntensity, volumeAtFullIntensity, volumeT) * Mathf.Clamp01(masterVolume) * distanceMultiplier);
            source.pitch = Mathf.Clamp(ResolveBasePitch() * Mathf.Lerp(pitchAtZeroIntensity, pitchAtFullIntensity, pitchT), -3f, 3f);

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

        public bool TryResolveDistanceSettings(
            float fallbackSpatialBlend,
            float fallbackMinDistance,
            float fallbackMaxDistance,
            out string sourceName,
            out bool isSpatial,
            out float spatialBlend,
            out float minDistance,
            out float maxDistance)
        {
            if (cue != null)
            {
                sourceName = cue.name;
                isSpatial = cue.Is3D && cue.SpatialBlend > 0.001f;
                spatialBlend = cue.Is3D ? cue.SpatialBlend : 0f;
                minDistance = Mathf.Max(0.01f, cue.MinDistance);
                maxDistance = Mathf.Max(minDistance, cue.MaxDistance);
                return true;
            }

            if (fallbackClip != null)
            {
                sourceName = fallbackClip.name;
                spatialBlend = Mathf.Clamp01(fallbackSpatialBlend);
                isSpatial = spatialBlend > 0.001f;
                minDistance = Mathf.Max(0.01f, fallbackMinDistance);
                maxDistance = Mathf.Max(minDistance, fallbackMaxDistance);
                return true;
            }

            sourceName = string.Empty;
            isSpatial = false;
            spatialBlend = 0f;
            minDistance = 0f;
            maxDistance = 0f;
            return false;
        }

        public bool ShouldDrawDistanceGizmo()
        {
            return cue == null || cue.DrawDistanceGizmos;
        }

        public bool TryGetCueDistanceGizmoColors(out Color minDistanceColor, out Color maxDistanceColor)
        {
            if (cue != null)
            {
                minDistanceColor = cue.MinDistanceGizmoColor;
                maxDistanceColor = cue.MaxDistanceGizmoColor;
                return true;
            }

            minDistanceColor = Color.clear;
            maxDistanceColor = Color.clear;
            return false;
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
            source.clip = clip;

            configuredVolumeRandomRange = cue != null ? GetOrderedRange(cue.RandomVolumeRange) : Vector2.one;
            configuredPitchRandomRange = cue != null ? GetOrderedRange(cue.RandomPitchRange) : Vector2.one;
            configuredVolumeRandomMultiplier = ResolveRandomMultiplier(configuredVolumeRandomRange);
            configuredPitchRandomMultiplier = ResolveRandomMultiplier(configuredPitchRandomRange);
            configuredCue = cue;
            configuredFallbackClip = fallbackClip;
            ApplySourceSettings(
                fallbackMixerGroup,
                fallbackSpatialBlend,
                fallbackSpread,
                fallbackMinDistance,
                fallbackMaxDistance,
                fallbackRolloffMode);
            return true;
        }

        private void ApplySourceSettings(
            AudioMixerGroup fallbackMixerGroup,
            float fallbackSpatialBlend,
            float fallbackSpread,
            float fallbackMinDistance,
            float fallbackMaxDistance,
            AudioRolloffMode fallbackRolloffMode)
        {
            if (source == null)
                return;

            RefreshCueRandomMultipliersIfNeeded();

            source.loop = true;
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.rolloffMode = fallbackRolloffMode;

            if (cue != null)
            {
                source.spatialBlend = cue.Is3D ? cue.SpatialBlend : 0f;
                source.spread = cue.Is3D ? cue.Spread : 0f;
                source.minDistance = Mathf.Max(0.01f, cue.MinDistance);
                source.maxDistance = Mathf.Max(source.minDistance, cue.MaxDistance);
                source.outputAudioMixerGroup = cue.MixerGroup != null ? cue.MixerGroup : fallbackMixerGroup;
                source.bypassReverbZones = !cue.AffectedByEnvironmentReverb;
                source.reverbZoneMix = cue.EnvironmentReverbSend;
                return;
            }

            source.spatialBlend = Mathf.Clamp01(fallbackSpatialBlend);
            source.spread = Mathf.Clamp(fallbackSpread, 0f, 360f);
            source.minDistance = Mathf.Max(0.01f, fallbackMinDistance);
            source.maxDistance = Mathf.Max(source.minDistance, fallbackMaxDistance);
            source.outputAudioMixerGroup = fallbackMixerGroup;
            source.bypassReverbZones = false;
            source.reverbZoneMix = source.spatialBlend > 0.001f ? 1f : 0f;
        }

        private void RefreshCueRandomMultipliersIfNeeded()
        {
            if (cue == null)
                return;

            Vector2 volumeRange = GetOrderedRange(cue.RandomVolumeRange);
            if (volumeRange != configuredVolumeRandomRange)
            {
                configuredVolumeRandomRange = volumeRange;
                configuredVolumeRandomMultiplier = ResolveRandomMultiplier(volumeRange);
            }

            Vector2 pitchRange = GetOrderedRange(cue.RandomPitchRange);
            if (pitchRange != configuredPitchRandomRange)
            {
                configuredPitchRandomRange = pitchRange;
                configuredPitchRandomMultiplier = ResolveRandomMultiplier(pitchRange);
            }
        }

        private float ResolveBaseVolume()
        {
            return cue != null
                ? Mathf.Max(0f, cue.BaseVolume * configuredVolumeRandomMultiplier)
                : 1f;
        }

        private float ResolveBasePitch()
        {
            return cue != null
                ? Mathf.Clamp(cue.BasePitch * configuredPitchRandomMultiplier, -3f, 3f)
                : 1f;
        }

        private float ResolveManualDistanceMultiplier(
            Transform listenerTransform,
            bool useManualDistanceAttenuation,
            bool hardMuteBeyondMaxDistance,
            AnimationCurve manualDistanceFadeCurve)
        {
            if (!useManualDistanceAttenuation || listenerTransform == null || source == null)
                return 1f;

            float spatialBlend = Mathf.Clamp01(source.spatialBlend);
            if (spatialBlend <= 0.001f)
                return 1f;

            float minDistance = Mathf.Max(0f, source.minDistance);
            float maxDistance = Mathf.Max(minDistance + 0.01f, source.maxDistance);
            float distance = Vector3.Distance(listenerTransform.position, source.transform.position);
            if (distance <= minDistance)
                return 1f;

            if (hardMuteBeyondMaxDistance && distance >= maxDistance)
                return 0f;

            float normalizedDistance = Mathf.InverseLerp(minDistance, maxDistance, distance);
            float attenuation = manualDistanceFadeCurve != null && manualDistanceFadeCurve.length > 0
                ? manualDistanceFadeCurve.Evaluate(normalizedDistance)
                : 1f - normalizedDistance;

            return Mathf.Lerp(1f, Mathf.Clamp01(attenuation), spatialBlend);
        }

        private static float EvaluateCurve(AnimationCurve curve, float intensity)
        {
            return curve != null && curve.length > 0 ? curve.Evaluate(intensity) : intensity;
        }

        private static float ResolveRandomMultiplier(Vector2 range)
        {
            Vector2 orderedRange = GetOrderedRange(range);
            return UnityEngine.Random.Range(orderedRange.x, orderedRange.y);
        }

        private static Vector2 GetOrderedRange(Vector2 range)
        {
            return new Vector2(
                Mathf.Min(range.x, range.y),
                Mathf.Max(range.x, range.y));
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
    [SerializeField] private AudioRolloffMode fallbackRolloffMode = AudioRolloffMode.Linear;

    [Header("Distance Attenuation")]
    [SerializeField] private bool useManualDistanceAttenuation = true;
    [SerializeField] private bool hardMuteBeyondMaxDistance = true;
    [SerializeField] private AnimationCurve manualDistanceFadeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
    [SerializeField] [Min(0f)] private float audioListenerRefreshInterval = 0.25f;

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

    [Header("Debug Gizmos")]
    [SerializeField] private bool drawAudioCueDistanceGizmos = true;
    [SerializeField] private bool drawAudioCueDistanceLabels = true;
    [SerializeField] private bool drawAudioCueMinDistance = true;
    [SerializeField] private bool drawAudioCueMaxDistance = true;
    [SerializeField] private Color hoverMinDistanceGizmoColor = new Color(0.2f, 0.85f, 1f, 0.45f);
    [SerializeField] private Color hoverMaxDistanceGizmoColor = new Color(0.2f, 0.45f, 1f, 0.35f);
    [SerializeField] private Color movementMinDistanceGizmoColor = new Color(1f, 0.25f, 0.45f, 0.45f);
    [SerializeField] private Color movementMaxDistanceGizmoColor = new Color(1f, 0.65f, 0.15f, 0.35f);
    [SerializeField] [Min(0f)] private float audioCueDistanceLabelHeight = 2.2f;

    public float CurrentIntensity => currentIntensity;

    private float currentIntensity;
    private Transform cachedAudioListenerTransform;
    private float nextAudioListenerRefreshTime;

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
        Transform listenerTransform = useManualDistanceAttenuation ? ResolveAudioListenerTransform() : null;

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
            shouldPlay,
            listenerTransform,
            useManualDistanceAttenuation,
            hardMuteBeyondMaxDistance,
            manualDistanceFadeCurve);

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
            shouldPlay,
            listenerTransform,
            useManualDistanceAttenuation,
            hardMuteBeyondMaxDistance,
            manualDistanceFadeCurve);
    }

    private Transform ResolveAudioListenerTransform()
    {
        if (cachedAudioListenerTransform != null && cachedAudioListenerTransform.gameObject.activeInHierarchy)
            return cachedAudioListenerTransform;

        float now = Time.unscaledTime;
        if (cachedAudioListenerTransform == null || now >= nextAudioListenerRefreshTime)
        {
            nextAudioListenerRefreshTime = now + Mathf.Max(0f, audioListenerRefreshInterval);
            AudioListener listener = FindAnyObjectByType<AudioListener>();
            cachedAudioListenerTransform = listener != null ? listener.transform : null;
        }

        return cachedAudioListenerTransform;
    }

    private static float ResolveSpeed(float candidate, float fallback)
    {
        return Mathf.Max(0.01f, candidate > 0f ? candidate : fallback);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawAudioCueDistanceGizmos)
            return;

        Transform anchor = ResolveGizmoAnchor();
        if (anchor == null)
            return;

        int labelIndex = 0;
        DrawLoopDistanceGizmo(
            hoverLoop,
            "Hover Loop",
            anchor.position,
            hoverMinDistanceGizmoColor,
            hoverMaxDistanceGizmoColor,
            ref labelIndex);

        DrawLoopDistanceGizmo(
            movementLoop,
            "Movement Loop",
            anchor.position,
            movementMinDistanceGizmoColor,
            movementMaxDistanceGizmoColor,
            ref labelIndex);
    }

    private Transform ResolveGizmoAnchor()
    {
        if (audioAnchor != null)
            return audioAnchor;

        EnemySetup setup = enemySetup != null ? enemySetup : GetComponent<EnemySetup>();
        return setup != null && setup.ModelRoot != null ? setup.ModelRoot : transform;
    }

    private void DrawLoopDistanceGizmo(
        LoopLayer loopLayer,
        string label,
        Vector3 origin,
        Color minDistanceColor,
        Color maxDistanceColor,
        ref int labelIndex)
    {
        if (loopLayer == null)
            return;

        if (!loopLayer.ShouldDrawDistanceGizmo())
            return;

        if (!loopLayer.TryResolveDistanceSettings(
                fallbackSpatialBlend,
                fallbackMinDistance,
                fallbackMaxDistance,
                out string sourceName,
                out bool isSpatial,
                out float spatialBlend,
                out float minDistance,
                out float maxDistance))
        {
            return;
        }

        if (loopLayer.TryGetCueDistanceGizmoColors(out Color cueMinDistanceColor, out Color cueMaxDistanceColor))
        {
            minDistanceColor = cueMinDistanceColor;
            maxDistanceColor = cueMaxDistanceColor;
        }

        if (!isSpatial)
        {
#if UNITY_EDITOR
            if (drawAudioCueDistanceLabels)
            {
                Handles.Label(
                    origin + Vector3.up * ResolveAudioCueDistanceLabelHeight(labelIndex),
                    $"{label}: {sourceName}\n2D / sem raio espacial");
            }
#endif
            labelIndex++;
            return;
        }

        if (drawAudioCueMinDistance)
        {
            Gizmos.color = minDistanceColor;
            Gizmos.DrawWireSphere(origin, minDistance);
        }

        if (drawAudioCueMaxDistance)
        {
            Gizmos.color = maxDistanceColor;
            Gizmos.DrawWireSphere(origin, maxDistance);
        }

#if UNITY_EDITOR
        if (drawAudioCueDistanceLabels)
        {
            Handles.Label(
                origin + Vector3.up * ResolveAudioCueDistanceLabelHeight(labelIndex),
                $"{label}: {sourceName}\nMin {minDistance:0.##}m | Max {maxDistance:0.##}m | Blend {spatialBlend:0.##}");
        }
#endif

        labelIndex++;
    }

    private float ResolveAudioCueDistanceLabelHeight(int labelIndex)
    {
        return Mathf.Max(0f, audioCueDistanceLabelHeight) + labelIndex * 0.35f;
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
        if (manualDistanceFadeCurve == null || manualDistanceFadeCurve.length == 0)
            manualDistanceFadeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        audioListenerRefreshInterval = Mathf.Max(0f, audioListenerRefreshInterval);
        hoverMinDistanceGizmoColor.a = Mathf.Clamp01(hoverMinDistanceGizmoColor.a);
        hoverMaxDistanceGizmoColor.a = Mathf.Clamp01(hoverMaxDistanceGizmoColor.a);
        movementMinDistanceGizmoColor.a = Mathf.Clamp01(movementMinDistanceGizmoColor.a);
        movementMaxDistanceGizmoColor.a = Mathf.Clamp01(movementMaxDistanceGizmoColor.a);
        audioCueDistanceLabelHeight = Mathf.Max(0f, audioCueDistanceLabelHeight);
        minAudibleSpeed = Mathf.Max(0f, minAudibleSpeed);
        patrolReferenceSpeed = Mathf.Max(0.01f, patrolReferenceSpeed);
        chaseReferenceSpeed = Mathf.Max(0.01f, chaseReferenceSpeed);
        intensityRiseSpeed = Mathf.Max(0f, intensityRiseSpeed);
        intensityFallSpeed = Mathf.Max(0f, intensityFallSpeed);
    }
}
