using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth), typeof(EnemyMotor), typeof(EnemyAttack))]
public class EnemyBrain : MonoBehaviour
{
    [Header("Targeting")]
    [SerializeField] [Min(0.5f)] private float detectionRange = 16f;
    [SerializeField] [Min(0.05f)] private float targetRefreshInterval = 0.25f;

    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private EnemyMotor enemyMotor;
    [SerializeField] private EnemyAttack enemyAttack;
    [SerializeField] private EnemyAnimationController enemyAnimationController;

    [Header("Spawn")]
    [SerializeField] private bool requireSpawnAnimation = true;

    private PlayerHealth currentTarget;
    private float nextTargetRefreshTime;
    private bool simulationEnabled = true;
    private bool spawnSequenceStarted;
    private bool spawnSequenceComplete;

    public EnemyState CurrentState { get; private set; } = EnemyState.PreSpawn;
    public PlayerHealth CurrentTarget => currentTarget;

    private void Awake()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (enemyMotor == null)
            enemyMotor = GetComponent<EnemyMotor>();

        if (enemyAttack == null)
            enemyAttack = GetComponent<EnemyAttack>();

        if (enemyAnimationController == null)
            enemyAnimationController = GetComponent<EnemyAnimationController>();
    }

    private void FixedUpdate()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (enemyMotor == null)
            enemyMotor = GetComponent<EnemyMotor>();

        if (enemyAttack == null)
            enemyAttack = GetComponent<EnemyAttack>();

        if (enemyAnimationController == null)
            enemyAnimationController = GetComponent<EnemyAnimationController>();

        if (!simulationEnabled || (enemySetup != null && !enemySetup.HasAuthority))
            return;

        if (enemyHealth == null || !enemyHealth.IsAlive)
        {
            currentTarget = null;
            enemyMotor?.Stop();
            CurrentState = EnemyState.Dead;
            return;
        }

        if (ShouldWaitForSpawnSequence())
        {
            UpdateSpawnSequenceGate();
            return;
        }

        if (enemyAttack != null && enemyAttack.IsAttackLocked)
        {
            if (enemyAttack.CanTrackTargetDuringCurrentAttack
                && currentTarget != null
                && currentTarget.CanBeTargetedByEnemy)
            {
                Vector3 attackPlanarOffset = Vector3.ProjectOnPlane(currentTarget.transform.position - transform.position, Vector3.up);
                enemyMotor?.RotateTowards(attackPlanarOffset, Time.fixedDeltaTime, enemyAttack.AttackTurnSpeedMultiplier);
            }

            enemyMotor?.Stop();
            CurrentState = EnemyState.Attacking;
            return;
        }

        RefreshTargetIfNeeded();
        if (currentTarget == null)
        {
            enemyMotor?.Stop();
            CurrentState = EnemyState.Idle;
            return;
        }

        Vector3 planarOffset = Vector3.ProjectOnPlane(currentTarget.transform.position - transform.position, Vector3.up);
        float sqrDistance = planarOffset.sqrMagnitude;
        float detectionRangeSqr = detectionRange * detectionRange;
        if (!currentTarget.CanBeTargetedByEnemy || sqrDistance > detectionRangeSqr)
        {
            currentTarget = null;
            enemyMotor?.Stop();
            CurrentState = EnemyState.Idle;
            return;
        }

        enemyMotor?.RotateTowards(planarOffset, Time.fixedDeltaTime);

        float attackStartRange = enemyAttack != null ? enemyAttack.AttackStartRange : 0f;
        if (sqrDistance <= attackStartRange * attackStartRange
            && enemyAttack != null
            && enemyAttack.TryAttack(currentTarget, planarOffset))
        {
            enemyMotor?.Stop();
            CurrentState = EnemyState.Attacking;
            return;
        }

        float attackRange = enemyAttack != null ? enemyAttack.AttackRange : 0f;
        if (sqrDistance > attackRange * attackRange)
        {
            CurrentState = EnemyState.Chasing;
            enemyMotor?.MoveTowards(planarOffset, Time.fixedDeltaTime);
            return;
        }

        enemyMotor?.Stop();
        CurrentState = EnemyState.Idle;
    }

    public void SetSimulationEnabled(bool enabled)
    {
        if (simulationEnabled == enabled)
            return;

        simulationEnabled = enabled;
        if (enabled)
            return;

        currentTarget = null;
        enemyMotor?.Stop();
        if (enemyHealth != null && !enemyHealth.IsAlive)
            CurrentState = EnemyState.Dead;
        else
            CurrentState = ShouldWaitForSpawnSequence() ? EnemyState.PreSpawn : EnemyState.Idle;
    }

    public void ApplyReplicatedState(EnemyState state)
    {
        if (enemySetup != null && enemySetup.HasAuthority)
            return;

        CurrentState = state;
        if (state == EnemyState.PreSpawn)
        {
            spawnSequenceStarted = false;
            spawnSequenceComplete = false;
        }
        else if (state == EnemyState.Spawning)
        {
            spawnSequenceStarted = true;
            spawnSequenceComplete = false;
        }
        else
        {
            spawnSequenceStarted = true;
            spawnSequenceComplete = true;
        }

        if (state != EnemyState.Chasing)
            enemyMotor?.Stop();
    }

    public void CompleteSpawnSequence()
    {
        spawnSequenceStarted = true;
        spawnSequenceComplete = true;
        nextTargetRefreshTime = 0f;

        if (CurrentState == EnemyState.PreSpawn || CurrentState == EnemyState.Spawning)
            CurrentState = EnemyState.Idle;
    }

    private bool ShouldWaitForSpawnSequence()
    {
        return requireSpawnAnimation && !spawnSequenceComplete;
    }

    private void UpdateSpawnSequenceGate()
    {
        enemyMotor?.Stop();

        if (!spawnSequenceStarted)
        {
            RefreshTargetIfNeeded();
            if (currentTarget == null)
            {
                CurrentState = EnemyState.PreSpawn;
                return;
            }

            spawnSequenceStarted = true;
        }

        CurrentState = EnemyState.Spawning;
    }

    private void RefreshTargetIfNeeded()
    {
        if (Time.time < nextTargetRefreshTime && currentTarget != null && currentTarget.CanBeTargetedByEnemy)
            return;

        nextTargetRefreshTime = Time.time + Mathf.Max(0.05f, targetRefreshInterval);
        currentTarget = FindNearestTarget();
    }

    private PlayerHealth FindNearestTarget()
    {
        PlayerHealth[] players = Object.FindObjectsByType<PlayerHealth>(FindObjectsInactive.Exclude);
        PlayerHealth nearestTarget = null;
        float nearestDistanceSqr = detectionRange * detectionRange;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerHealth player = players[i];
            if (player == null || !player.CanBeTargetedByEnemy)
                continue;

            Vector3 planarOffset = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up);
            float distanceSqr = planarOffset.sqrMagnitude;
            if (distanceSqr > nearestDistanceSqr)
                continue;

            nearestTarget = player;
            nearestDistanceSqr = distanceSqr;
        }

        return nearestTarget;
    }
}
