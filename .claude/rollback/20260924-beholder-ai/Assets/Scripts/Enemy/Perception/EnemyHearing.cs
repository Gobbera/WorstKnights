using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyHearing : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;

    [Header("Hearing")]
    [SerializeField] private bool enableHearing = true;
    [SerializeField] [Min(0f)] private float hearingMultiplier = 1f;
    [SerializeField] [Min(0f)] private float minimumAudibleIntensity = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool drawHearingGizmo = true;
    [SerializeField] [Min(0f)] private float previewHearingRadius = 12f;
    [SerializeField] private Color hearingGizmoColor = new Color(0.2f, 0.65f, 1f, 0.25f);

    private NoiseEvent pendingNoise;
    private bool hasPendingNoise;

    public bool EnableHearing => enableHearing;
    public float HearingMultiplier => Mathf.Max(0f, hearingMultiplier);
    public bool HasPendingNoise => hasPendingNoise;
    public NoiseEvent PendingNoise => pendingNoise;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        NoiseSystem.NoiseEmitted += HandleNoiseEmitted;
    }

    private void OnDisable()
    {
        NoiseSystem.NoiseEmitted -= HandleNoiseEmitted;
        hasPendingNoise = false;
    }

    public bool TryConsumeNoise(out NoiseEvent noiseEvent)
    {
        noiseEvent = pendingNoise;
        if (!hasPendingNoise)
            return false;

        hasPendingNoise = false;
        return true;
    }

    public bool CanHear(NoiseEvent noiseEvent)
    {
        if (!enableHearing || noiseEvent.Intensity < minimumAudibleIntensity)
            return false;

        float audibleRadius = noiseEvent.Radius * Mathf.Max(0f, noiseEvent.Intensity) * HearingMultiplier;
        if (audibleRadius <= 0.0001f)
            return false;

        Vector3 planarOffset = Vector3.ProjectOnPlane(noiseEvent.Position - transform.position, Vector3.up);
        return planarOffset.sqrMagnitude <= audibleRadius * audibleRadius;
    }

    private void HandleNoiseEmitted(NoiseEvent noiseEvent)
    {
        if (!CanHear(noiseEvent))
            return;

        if (!hasPendingNoise)
        {
            pendingNoise = noiseEvent;
            hasPendingNoise = true;
            return;
        }

        float currentDistanceSqr = Vector3.ProjectOnPlane(pendingNoise.Position - transform.position, Vector3.up).sqrMagnitude;
        float candidateDistanceSqr = Vector3.ProjectOnPlane(noiseEvent.Position - transform.position, Vector3.up).sqrMagnitude;
        if (noiseEvent.Intensity > pendingNoise.Intensity || candidateDistanceSqr < currentDistanceSqr)
            pendingNoise = noiseEvent;
    }

    private void ResolveReferences()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawHearingGizmo || !enableHearing)
            return;

        Gizmos.color = hearingGizmoColor;
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, previewHearingRadius) * HearingMultiplier);
    }
}
