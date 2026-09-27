using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class EnemyNavigation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyMotor enemyMotor;
    [SerializeField] private NavMeshAgent navMeshAgent;

    [Header("NavMesh")]
    [SerializeField] private bool useNavMeshAgent = true;
    [SerializeField] private bool createAgentAtRuntime;
    [SerializeField] private bool useDirectMovementFallback = true;
    [SerializeField] [Min(0f)] private float samplePositionDistance = 2f;
    [SerializeField] private bool configureAgentFromCapsule;
    [SerializeField] [Min(0f)] private float agentRadiusPadding;
    [SerializeField] [Min(0.1f)] private float minimumAgentHeight = 0.1f;

    [Header("Runtime Agent Overrides")]
    [SerializeField] private bool forceManualAgentTransformSync = true;
    [SerializeField] private bool applyRuntimeAvoidanceSettings;
    [SerializeField] private ObstacleAvoidanceType obstacleAvoidance = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
    [SerializeField] [Range(0, 99)] private int avoidancePriority = 50;

    [Header("Destination")]
    [SerializeField] [Min(0f)] private float destinationRefreshDistance = 0.35f;
    [SerializeField] [Min(0f)] private float reachedDistanceTolerance = 0.15f;

    private Vector3 currentDestination;
    private float currentSpeed;
    private float currentStoppingDistance;
    private bool hasDestination;
    private bool paused;
    private bool simulationEnabled = true;

    public Vector3 CurrentDestination => currentDestination;
    public bool HasDestination => hasDestination;
    public bool HasUsableAgent => CanUseAgent();

    private void Awake()
    {
        ResolveReferences();
        ConfigureAgent();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ConfigureAgent();
        TryWarpAgentToNearestNavMesh();
    }

    private void OnDisable()
    {
        Stop();
    }

    public void SetSimulationEnabled(bool enabled)
    {
        simulationEnabled = enabled;
        if (!enabled)
            Stop();
    }

    public bool SetDestination(Vector3 destination, float speed, float stoppingDistance, bool force = false)
    {
        ResolveReferences();
        ConfigureAgent();

        if (!simulationEnabled)
            return false;

        currentSpeed = Mathf.Max(0f, speed);
        currentStoppingDistance = Mathf.Max(0f, stoppingDistance);

        if (!force
            && hasDestination
            && Vector3.ProjectOnPlane(destination - currentDestination, Vector3.up).sqrMagnitude <= destinationRefreshDistance * destinationRefreshDistance)
        {
            return true;
        }

        currentDestination = destination;
        hasDestination = true;
        paused = false;

        if (CanUseAgent())
            return SetAgentDestination(destination);

        return useDirectMovementFallback;
    }

    public void Stop()
    {
        hasDestination = false;
        paused = false;

        if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
            navMeshAgent.ResetPath();
        }

        enemyMotor?.Stop();
    }

    public void Pause()
    {
        paused = true;
        if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            navMeshAgent.isStopped = true;

        enemyMotor?.Stop();
    }

    public void Resume()
    {
        if (!hasDestination)
            return;

        paused = false;
        if (CanUseAgent())
        {
            navMeshAgent.isStopped = false;
            SetAgentDestination(currentDestination);
        }
    }

    public void Tick(float deltaTime)
    {
        if (!simulationEnabled || paused || !hasDestination || deltaTime <= 0f)
            return;

        ResolveReferences();
        if (enemyMotor == null)
            return;

        enemyMotor.SetMoveSpeed(currentSpeed);

        if (CanUseAgent())
        {
            MoveUsingNavMeshAgent(deltaTime);
            return;
        }

        if (useDirectMovementFallback)
            MoveDirectly(deltaTime);
    }

    public bool HasReachedDestination()
    {
        if (!hasDestination)
            return true;

        float threshold = Mathf.Max(0f, currentStoppingDistance) + Mathf.Max(0f, reachedDistanceTolerance);

        if (CanUseAgent())
        {
            if (navMeshAgent.pathPending)
                return false;

            if (navMeshAgent.pathStatus == NavMeshPathStatus.PathInvalid)
                return true;

            return navMeshAgent.remainingDistance <= threshold;
        }

        Vector3 planarOffset = Vector3.ProjectOnPlane(currentDestination - transform.position, Vector3.up);
        return planarOffset.sqrMagnitude <= threshold * threshold;
    }

    public bool IsPathValid()
    {
        if (!hasDestination)
            return true;

        if (!CanUseAgent())
            return useDirectMovementFallback;

        return navMeshAgent.pathStatus != NavMeshPathStatus.PathInvalid;
    }

    public bool ForceRepath()
    {
        if (!hasDestination || !simulationEnabled)
            return false;

        if (CanUseAgent())
            return SetAgentDestination(currentDestination);

        return useDirectMovementFallback;
    }

    public bool TrySamplePosition(Vector3 candidatePosition, float maxDistance, out Vector3 sampledPosition)
    {
        if (NavMesh.SamplePosition(candidatePosition, out NavMeshHit hit, Mathf.Max(0f, maxDistance), NavMesh.AllAreas))
        {
            sampledPosition = hit.position;
            return true;
        }

        sampledPosition = candidatePosition;
        return false;
    }

    public void RotateTowardsPosition(Vector3 position, float deltaTime, float turnSpeedMultiplier = 1f)
    {
        Vector3 planarOffset = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
        enemyMotor?.RotateTowards(planarOffset, deltaTime, turnSpeedMultiplier);
    }

    private bool SetAgentDestination(Vector3 destination)
    {
        if (!CanUseAgent())
            return false;

        if (TrySamplePosition(destination, samplePositionDistance, out Vector3 sampledDestination))
            destination = sampledDestination;

        currentDestination = destination;
        navMeshAgent.speed = Mathf.Max(0f, currentSpeed);
        navMeshAgent.stoppingDistance = Mathf.Max(0f, currentStoppingDistance);
        navMeshAgent.isStopped = false;
        navMeshAgent.nextPosition = transform.position;
        return navMeshAgent.SetDestination(destination);
    }

    private void MoveUsingNavMeshAgent(float deltaTime)
    {
        navMeshAgent.nextPosition = transform.position;

        Vector3 desiredVelocity = navMeshAgent.desiredVelocity;
        Vector3 planarDesiredVelocity = Vector3.ProjectOnPlane(desiredVelocity, Vector3.up);
        Vector3 moveOffset = planarDesiredVelocity.sqrMagnitude > 0.0001f
            ? planarDesiredVelocity
            : Vector3.ProjectOnPlane(navMeshAgent.steeringTarget - transform.position, Vector3.up);

        if (moveOffset.sqrMagnitude <= 0.0001f)
        {
            enemyMotor.Stop();
            return;
        }

        enemyMotor.MoveTowards(moveOffset, deltaTime);
        enemyMotor.RotateTowards(moveOffset, deltaTime);
        navMeshAgent.nextPosition = transform.position;
    }

    private void MoveDirectly(float deltaTime)
    {
        Vector3 planarOffset = Vector3.ProjectOnPlane(currentDestination - transform.position, Vector3.up);
        if (planarOffset.sqrMagnitude <= 0.0001f)
        {
            enemyMotor.Stop();
            return;
        }

        enemyMotor.MoveTowards(planarOffset, deltaTime);
        enemyMotor.RotateTowards(planarOffset, deltaTime);
    }

    private bool CanUseAgent()
    {
        return useNavMeshAgent
            && navMeshAgent != null
            && navMeshAgent.enabled
            && navMeshAgent.isOnNavMesh;
    }

    private void ConfigureAgent()
    {
        if (navMeshAgent == null)
            return;

        ConfigureAgentShapeFromCapsule();
        if (forceManualAgentTransformSync)
        {
            navMeshAgent.updatePosition = false;
            navMeshAgent.updateRotation = false;
        }

        if (applyRuntimeAvoidanceSettings)
        {
            navMeshAgent.obstacleAvoidanceType = obstacleAvoidance;
            navMeshAgent.avoidancePriority = Mathf.Clamp(avoidancePriority, 0, 99);
        }
    }

    private void ConfigureAgentShapeFromCapsule()
    {
        if (!configureAgentFromCapsule)
            return;

        CapsuleCollider capsule = enemySetup != null ? enemySetup.Capsule : GetComponent<CapsuleCollider>();
        if (capsule == null)
            return;

        Bounds capsuleBounds = capsule.bounds;
        float radius = Mathf.Max(capsuleBounds.extents.x, capsuleBounds.extents.z) + Mathf.Max(0f, agentRadiusPadding);
        float height = Mathf.Max(minimumAgentHeight, capsuleBounds.size.y);
        float baseOffset = Mathf.Max(0f, capsuleBounds.min.y - transform.position.y);

        navMeshAgent.radius = Mathf.Max(0.01f, radius);
        navMeshAgent.height = height;
        navMeshAgent.baseOffset = baseOffset;
    }

    private void ResolveReferences()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyMotor == null)
            enemyMotor = GetComponent<EnemyMotor>();

        if (navMeshAgent == null)
            navMeshAgent = GetComponent<NavMeshAgent>();

        if (navMeshAgent == null && createAgentAtRuntime && useNavMeshAgent && Application.isPlaying)
            navMeshAgent = gameObject.AddComponent<NavMeshAgent>();
    }

    private void TryWarpAgentToNearestNavMesh()
    {
        if (navMeshAgent == null || !navMeshAgent.enabled || navMeshAgent.isOnNavMesh)
            return;

        if (TrySamplePosition(transform.position, samplePositionDistance, out Vector3 sampledPosition))
            navMeshAgent.Warp(sampledPosition);
    }
}
