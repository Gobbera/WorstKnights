using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponAttackTrail : MonoBehaviour
{
    private const float MinDuration = 0.01f;

    [SerializeField, HideInInspector]
    private bool autoFindReferences = true;
    [SerializeField, HideInInspector] private ParticleSystem[] particleSystems = System.Array.Empty<ParticleSystem>();
    [SerializeField, HideInInspector] private ParticleSystemRenderer[] particleRenderers = System.Array.Empty<ParticleSystemRenderer>();

    [SerializeField] [Min(MinDuration)] [Tooltip("How long the trail keeps emitting after an attack starts.")]
    private float playDuration = 0.45f;
    [SerializeField] [Min(0f)] [Tooltip("Optional delay before the trail starts after the attack is accepted.")]
    private float startDelay;
    [SerializeField, HideInInspector]
    private bool clearBeforePlay = true;
    [SerializeField, HideInInspector]
    private bool clearWhenStopped = true;
    [SerializeField, HideInInspector]
    private bool disableRenderersWhileIdle = true;
    [SerializeField, HideInInspector]
    private bool respectAuthoredRendererVisibility;
    [SerializeField, HideInInspector]
    private bool restartWhenAlreadyPlaying = true;

    private bool[] authoredRendererStates = System.Array.Empty<bool>();
    private bool rendererStatesCaptured;
    private bool playQueued;
    private bool trailPlaying;
    private float queuedPlayTime;
    private float queuedDuration;
    private float stopTime;

    private void Reset()
    {
        ResolveReferences(force: true);
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        ResolveReferences(force: false);
        SanitizeSettings();
    }

    private void Awake()
    {
        ResolveReferences(force: false);
        CaptureAuthoredRendererStates();
        ConfigureParticleSystems();
        StopAttackTrail(clear: true, cancelQueuedPlay: true);
    }

    private void OnEnable()
    {
        StopAttackTrail(clear: true, cancelQueuedPlay: true);
    }

    private void OnDisable()
    {
        StopAttackTrail(clear: true, cancelQueuedPlay: true);
    }

    private void Update()
    {
        if (playQueued && Time.time >= queuedPlayTime)
        {
            float duration = queuedDuration;
            playQueued = false;
            StartAttackTrailNow(duration);
        }

        if (!trailPlaying || Time.time < stopTime)
            return;

        StopAttackTrail(clear: clearWhenStopped, cancelQueuedPlay: false);
    }

    public void PlayAttackTrail(float duration = -1f)
    {
        ResolveReferences(force: false);
        CaptureAuthoredRendererStates();
        ConfigureParticleSystems();
        SanitizeSettings();

        if (!HasAnyLiveReference(particleSystems))
        {
            Debug.LogWarning($"[WeaponAttackTrail] '{gameObject.name}' has no ParticleSystems assigned or found.", this);
            return;
        }

        if (!restartWhenAlreadyPlaying && (trailPlaying || playQueued))
            return;

        float safeDuration = ResolveDuration(duration);
        if (startDelay > 0f)
        {
            queuedPlayTime = Time.time + startDelay;
            queuedDuration = safeDuration;
            playQueued = true;
            return;
        }

        StartAttackTrailNow(safeDuration);
    }

    public void StopAttackTrail(bool clear = false)
    {
        StopAttackTrail(clear, cancelQueuedPlay: true);
    }

    public bool ControlsRenderer(Renderer renderer)
    {
        if (renderer == null)
            return false;

        ResolveReferences(force: false);
        if (particleRenderers == null || particleRenderers.Length == 0)
            return false;

        for (int i = 0; i < particleRenderers.Length; i++)
        {
            if (particleRenderers[i] == renderer)
                return true;
        }

        return false;
    }

    public void CopyAuthoredRendererStatesFrom(WeaponAttackTrail source)
    {
        if (source == null || source == this)
            return;

        source.ResolveReferences(force: false);
        source.CaptureAuthoredRendererStates();

        ResolveReferences(force: false);
        if (particleRenderers == null)
            particleRenderers = System.Array.Empty<ParticleSystemRenderer>();

        bool[] sourceRendererStates = source.authoredRendererStates;
        if (sourceRendererStates == null || sourceRendererStates.Length == 0)
        {
            authoredRendererStates = System.Array.Empty<bool>();
            rendererStatesCaptured = false;
            return;
        }

        authoredRendererStates = new bool[particleRenderers.Length];
        for (int i = 0; i < authoredRendererStates.Length; i++)
        {
            if (i < sourceRendererStates.Length)
            {
                authoredRendererStates[i] = sourceRendererStates[i];
                continue;
            }

            ParticleSystemRenderer particleRenderer = particleRenderers[i];
            authoredRendererStates[i] = particleRenderer != null && particleRenderer.enabled;
        }

        rendererStatesCaptured = true;
    }

    [ContextMenu("Play Attack Trail")]
    private void ContextPlayAttackTrail()
    {
        PlayAttackTrail();
    }

    [ContextMenu("Stop Attack Trail")]
    private void ContextStopAttackTrail()
    {
        StopAttackTrail(clear: true);
    }

    private void StartAttackTrailNow(float duration)
    {
        float safeDuration = Mathf.Max(MinDuration, duration);
        stopTime = Time.time + safeDuration;
        trailPlaying = true;

        SetRendererVisibility(visible: true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
                continue;

            if (clearBeforePlay)
                particleSystem.Clear(withChildren: true);

            particleSystem.Play(withChildren: true);
        }
    }

    private void StopAttackTrail(bool clear, bool cancelQueuedPlay)
    {
        if (cancelQueuedPlay)
            playQueued = false;

        trailPlaying = false;
        stopTime = 0f;

        if (particleSystems == null)
            particleSystems = System.Array.Empty<ParticleSystem>();

        ParticleSystemStopBehavior stopBehavior = clear
            ? ParticleSystemStopBehavior.StopEmittingAndClear
            : ParticleSystemStopBehavior.StopEmitting;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
                continue;

            particleSystem.Stop(withChildren: true, stopBehavior);
        }

        if (disableRenderersWhileIdle)
            SetRendererVisibility(visible: false);
    }

    private void ResolveReferences(bool force)
    {
        if (!autoFindReferences && !force)
            return;

        if (force || !HasAnyLiveReference(particleSystems))
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);

        if (force || !HasAnyLiveReference(particleRenderers))
        {
            particleRenderers = GetComponentsInChildren<ParticleSystemRenderer>(true);
            rendererStatesCaptured = false;
        }
    }

    private void CaptureAuthoredRendererStates()
    {
        if (rendererStatesCaptured
            && authoredRendererStates != null
            && particleRenderers != null
            && authoredRendererStates.Length == particleRenderers.Length)
        {
            return;
        }

        if (particleRenderers == null)
            particleRenderers = System.Array.Empty<ParticleSystemRenderer>();

        authoredRendererStates = new bool[particleRenderers.Length];
        for (int i = 0; i < particleRenderers.Length; i++)
        {
            ParticleSystemRenderer particleRenderer = particleRenderers[i];
            authoredRendererStates[i] = particleRenderer != null && particleRenderer.enabled;
        }

        rendererStatesCaptured = true;
    }

    private void ConfigureParticleSystems()
    {
        if (particleSystems == null)
            particleSystems = System.Array.Empty<ParticleSystem>();

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
                continue;

            ParticleSystem.MainModule main = particleSystem.main;
            main.playOnAwake = false;
        }
    }

    private void SanitizeSettings()
    {
        playDuration = Mathf.Max(MinDuration, playDuration);
        startDelay = Mathf.Max(0f, startDelay);
    }

    private float ResolveDuration(float duration)
    {
        return Mathf.Max(MinDuration, duration > 0f ? duration : playDuration);
    }

    private static bool HasAnyLiveReference<T>(T[] references) where T : Object
    {
        if (references == null || references.Length == 0)
            return false;

        for (int i = 0; i < references.Length; i++)
        {
            if (references[i] != null)
                return true;
        }

        return false;
    }

    private void SetRendererVisibility(bool visible)
    {
        if (particleRenderers == null)
            particleRenderers = System.Array.Empty<ParticleSystemRenderer>();

        for (int i = 0; i < particleRenderers.Length; i++)
        {
            ParticleSystemRenderer particleRenderer = particleRenderers[i];
            if (particleRenderer == null)
                continue;

            bool authoredVisible = !respectAuthoredRendererVisibility
                || (authoredRendererStates != null
                    && i < authoredRendererStates.Length
                    && authoredRendererStates[i]);
            particleRenderer.enabled = visible && authoredVisible;
        }
    }
}
