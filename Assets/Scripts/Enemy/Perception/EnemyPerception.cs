using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyPerception : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private EnemyVision enemyVision;
    [SerializeField] private EnemyHearing enemyHearing;

    public EnemyVision Vision => enemyVision;
    public EnemyHearing Hearing => enemyHearing;
    public PlayerHealth VisibleTarget { get; private set; }
    public Vector3 VisibleTargetPosition { get; private set; }
    public bool HasVisibleTarget { get; private set; }
    public bool HasHeardNoise => enemyHearing != null && enemyHearing.HasPendingNoise;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Tick(PlayerHealth preferredTarget)
    {
        ResolveReferences();

        if (enemyHealth != null && !enemyHealth.IsAlive)
        {
            ClearVisibleTarget();
            return;
        }

        if (enemyVision != null && enemyVision.TryGetVisibleTarget(preferredTarget, out PlayerHealth target, out Vector3 targetPosition))
        {
            VisibleTarget = target;
            VisibleTargetPosition = targetPosition;
            HasVisibleTarget = true;
            return;
        }

        ClearVisibleTarget();
    }

    public bool TryGetVisibleTarget(PlayerHealth preferredTarget, out PlayerHealth target, out Vector3 targetPosition)
    {
        Tick(preferredTarget);
        target = VisibleTarget;
        targetPosition = VisibleTargetPosition;
        return HasVisibleTarget;
    }

    public bool TryConsumeHeardNoise(out NoiseEvent noiseEvent)
    {
        noiseEvent = default;
        if (enemyHearing == null)
            return false;

        return enemyHearing.TryConsumeNoise(out noiseEvent);
    }

    private void ClearVisibleTarget()
    {
        VisibleTarget = null;
        VisibleTargetPosition = Vector3.zero;
        HasVisibleTarget = false;
    }

    private void ResolveReferences()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (enemyVision == null)
            enemyVision = GetComponent<EnemyVision>();

        if (enemyHearing == null)
            enemyHearing = GetComponent<EnemyHearing>();
    }
}
