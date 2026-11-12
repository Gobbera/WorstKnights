using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMotor))]
[RequireComponent(typeof(AudioEmitter))]
[RequireComponent(typeof(FootstepPlaybackController))]
public class EnemyFootstepAudioController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyMotor enemyMotor;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private FootstepPlaybackController footstepPlayback;

    [Header("Fallback")]
    [FormerlySerializedAs("fallbackFootstepCue")]
    [SerializeField] private AudioCue surfaceFallbackCue;

    [Header("Cadence")]
    [SerializeField] [Min(0f)] private float minPlanarSpeed = 0.15f;
    [SerializeField] [Min(0.1f)] private float metersPerStep = 1.2f;
    [SerializeField] private bool onlyWhenChasing = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging;

    private float footstepDistanceAccumulator;
    private bool hasLoggedMissingCue;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResetFootstepState();
    }

    private void Update()
    {
        ResolveReferences();
        UpdateFootsteps(Time.deltaTime);
    }

    private void ResolveReferences()
    {
        if (enemyBrain == null)
            enemyBrain = GetComponent<EnemyBrain>();

        if (enemyMotor == null)
            enemyMotor = GetComponent<EnemyMotor>();

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (footstepPlayback == null)
            footstepPlayback = GetComponent<FootstepPlaybackController>();
    }

    private void UpdateFootsteps(float deltaTime)
    {
        if (deltaTime <= 0f || footstepPlayback == null || enemyMotor == null)
            return;

        if (!ShouldEmitFootsteps())
        {
            ResetFootstepState();
            return;
        }

        footstepDistanceAccumulator += Mathf.Max(0f, enemyMotor.CurrentPlanarSpeed) * deltaTime;
        float safeMetersPerStep = Mathf.Max(0.1f, metersPerStep);

        while (footstepDistanceAccumulator >= safeMetersPerStep)
        {
            if (!footstepPlayback.PlayFootstep(FootstepGait.EnemyWalk, surfaceFallbackCue))
                LogMissingCueOnce();

            footstepDistanceAccumulator -= safeMetersPerStep;
        }
    }

    private bool ShouldEmitFootsteps()
    {
        if (enemyHealth != null && !enemyHealth.IsAlive)
            return false;

        if (onlyWhenChasing && enemyBrain != null && enemyBrain.CurrentState != EnemyState.Chase)
            return false;

        return enemyMotor != null && enemyMotor.CurrentPlanarSpeed >= Mathf.Max(0f, minPlanarSpeed);
    }

    private void ResetFootstepState()
    {
        footstepDistanceAccumulator = 0f;
        hasLoggedMissingCue = false;
    }

    private void LogMissingCueOnce()
    {
        if (!verboseLogging || hasLoggedMissingCue)
            return;

        hasLoggedMissingCue = true;
        Debug.Log("[EnemyFootstepAudioController] Nenhum AudioCue de passo configurado para este inimigo/superficie.", gameObject);
    }
}
