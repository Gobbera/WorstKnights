using System;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth), typeof(EnemyMotor), typeof(EnemyAttack))]
public class EnemyBrain : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private EnemyMotor enemyMotor;
    [SerializeField] private EnemyNavigation enemyNavigation;
    [SerializeField] private EnemyPerception enemyPerception;
    [SerializeField] private EnemyAttack enemyAttack;
    [SerializeField] private EnemyAnimationController enemyAnimationController;

    [Header("Spawn")]
    [SerializeField] private bool requireSpawnAnimation = true;
    [SerializeField] private EnemySpawnTriggerMode spawnTriggerMode = EnemySpawnTriggerMode.OnStart;

    [Header("Behavior")]
    [SerializeField] private EnemyState defaultState = EnemyState.Patrol;
    [SerializeField] private bool returnToPatrol = true;

    [Header("Idle")]
    [SerializeField] [Min(0f)] private float idleDuration = 1f;

    [Header("Patrol")]
    [SerializeField] private Transform[] patrolPoints = Array.Empty<Transform>();
    [SerializeField] private EnemyPatrolMode patrolMode = EnemyPatrolMode.Sequential;
    [SerializeField] [Min(0f)] private float patrolSpeed = 2f;
    [SerializeField] [Min(0f)] private float patrolWaitTime = 1f;
    [SerializeField] [Min(0f)] private float patrolStoppingDistance = 0.35f;
    [SerializeField] private bool startAtNearestPatrolPoint = true;

    [Header("Alert")]
    [SerializeField] [Min(0f)] private float alertDuration = 1.5f;
    [SerializeField] private bool moveToAlertPosition = true;
    [SerializeField] [Min(0f)] private float alertSpeed = 2.5f;
    [SerializeField] [Min(0f)] private float alertStoppingDistance = 0.75f;

    [Header("Chase")]
    [SerializeField] [Min(0f)] private float chaseSpeed = 3.25f;
    [SerializeField] [Min(0f)] private float maxChaseDistance = 22f;
    [SerializeField] [Min(0.02f)] private float chaseDestinationUpdateInterval = 0.25f;
    [SerializeField] [Min(0f)] private float chaseStoppingDistance = 1.1f;
    [SerializeField] [Min(0f)] private float lostSightGraceDuration = 0.4f;

    [Header("Combat Memory")]
    [SerializeField] [Min(0f)] private float combatLostSightGraceDuration = 1.5f;
    [SerializeField] [Min(0f)] private float combatMemoryDuration = 6f;
    [FormerlySerializedAs("closeCombatAwarenessRadius")]
    [SerializeField] [Min(0f)] private float combatMemoryAwarenessRadius = 3f;
    [SerializeField] private bool combatMemoryRequiresLineOfSight = true;
    [SerializeField] [Min(0f)] private float combatSearchDuration = 8f;
    [SerializeField] [Min(0f)] private float combatSearchRadius = 4f;
    [SerializeField] [Min(0)] private int combatSearchPointCount = 5;
    [SerializeField] [Min(0f)] private float combatSearchPointWaitTime = 0.8f;
    [SerializeField] [Min(0f)] private float combatSearchSpeed = 3f;

    [Header("Close Awareness")]
    [SerializeField] private bool enableCloseAwareness = true;
    [SerializeField] [Min(0f)] private float closeAwarenessRadius = 1.4f;
    [SerializeField] private bool closeAwarenessRequiresLineOfSight = true;

    [Header("Search")]
    [SerializeField] [Min(0f)] private float searchDuration = 4f;
    [SerializeField] [Min(0f)] private float searchRadius = 3f;
    [SerializeField] [Min(0)] private int searchPointCount = 3;
    [SerializeField] [Min(0f)] private float searchSpeed = 2.5f;
    [SerializeField] [Min(0f)] private float searchStoppingDistance = 0.75f;
    [SerializeField] [Min(0f)] private float searchPointWaitTime = 0.6f;

    [Header("Return")]
    [SerializeField] [Min(0f)] private float returnSpeed = 2.75f;
    [SerializeField] [Min(0f)] private float returnStoppingDistance = 0.75f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private bool drawDebugLabels = true;
    [SerializeField] private Color lastKnownPositionColor = new Color(0.2f, 0.65f, 1f, 0.9f);
    [SerializeField] private Color investigationPositionColor = new Color(1f, 0.55f, 0.05f, 0.9f);
    [SerializeField] private Color destinationColor = new Color(0.25f, 1f, 0.35f, 0.9f);
    [SerializeField] private Color closeAwarenessColor = new Color(0.1f, 0.95f, 1f, 0.9f);
    [SerializeField] private Color combatMemoryColor = new Color(1f, 0.1f, 0.85f, 0.9f);

    private EnemyStateMachine stateMachine;
    private PlayerHealth currentTarget;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Vector3 lastKnownTargetPosition;
    private Vector3 investigationPosition;
    private bool hasLastKnownTargetPosition;
    private bool hasInvestigationPosition;
    private bool simulationEnabled = true;
    private bool spawnSequenceStarted;
    private bool spawnSequenceComplete;
    private bool externalStateOverrideActive;
    private EnemyState externalStateOverride = EnemyState.PreSpawn;
    private float lastCombatAwarenessTime = float.NegativeInfinity;
    private bool hasCombatAwareness;

    public EnemyState CurrentState => externalStateOverrideActive ? externalStateOverride : (stateMachine != null ? stateMachine.CurrentStateId : EnemyState.Idle);
    public PlayerHealth CurrentTarget => currentTarget;
    public Vector3 LastKnownTargetPosition => lastKnownTargetPosition;
    public Vector3 InvestigationPosition => investigationPosition;
    public Vector3 HomePosition => homePosition;
    public bool HasLastKnownTargetPosition => hasLastKnownTargetPosition;
    public bool HasInvestigationPosition => hasInvestigationPosition;
    public bool HasValidTarget => currentTarget != null && currentTarget.CanBeTargetedByEnemy;
    public bool HasPatrolPoints => CountValidPatrolPoints() > 0;
    public bool ShouldReturnToPatrol => returnToPatrol;
    public bool MoveToAlertPosition => moveToAlertPosition;
    public bool IsAttackLocked => enemyAttack != null && enemyAttack.IsAttackLocked;
    public float IdleDuration => Mathf.Max(0f, idleDuration);
    public float PatrolSpeed => Mathf.Max(0f, patrolSpeed);
    public float PatrolWaitTime => Mathf.Max(0f, patrolWaitTime);
    public float PatrolStoppingDistance => Mathf.Max(0f, patrolStoppingDistance);
    public float AlertDuration => Mathf.Max(0f, alertDuration);
    public float AlertSpeed => Mathf.Max(0f, alertSpeed);
    public float AlertStoppingDistance => Mathf.Max(0f, alertStoppingDistance);
    public float ChaseSpeed => Mathf.Max(0f, chaseSpeed);
    public float ChaseDestinationUpdateInterval => Mathf.Max(0.02f, chaseDestinationUpdateInterval);
    public float ChaseStoppingDistance => Mathf.Max(0f, chaseStoppingDistance);
    public float LostSightGraceDuration => Mathf.Max(0f, lostSightGraceDuration);
    public float CombatLostSightGraceDuration => Mathf.Max(0f, combatLostSightGraceDuration);
    public float CombatMemoryDuration => Mathf.Max(0f, combatMemoryDuration);
    public float CombatMemoryAwarenessRadius => Mathf.Max(0f, combatMemoryAwarenessRadius);
    public bool CombatMemoryRequiresLineOfSight => combatMemoryRequiresLineOfSight;
    public float CombatSearchDuration => Mathf.Max(0f, combatSearchDuration);
    public float CombatSearchRadius => Mathf.Max(0f, combatSearchRadius);
    public int CombatSearchPointCount => Mathf.Max(0, combatSearchPointCount);
    public float CombatSearchPointWaitTime => Mathf.Max(0f, combatSearchPointWaitTime);
    public float CombatSearchSpeed => Mathf.Max(0f, combatSearchSpeed);
    public bool HasActiveCombatMemory => IsCombatMemoryActive();
    public bool EnableCloseAwareness => enableCloseAwareness;
    public float CloseAwarenessRadius => Mathf.Max(0f, closeAwarenessRadius);
    public bool CloseAwarenessRequiresLineOfSight => closeAwarenessRequiresLineOfSight;
    public float CombatMemoryRemaining => ResolveCombatMemoryRemaining();
    public float SearchDuration => Mathf.Max(0f, searchDuration);
    public float SearchRadius => Mathf.Max(0f, searchRadius);
    public int SearchPointCount => Mathf.Max(0, searchPointCount);
    public float SearchSpeed => Mathf.Max(0f, searchSpeed);
    public float SearchStoppingDistance => Mathf.Max(0f, searchStoppingDistance);
    public float SearchPointWaitTime => Mathf.Max(0f, searchPointWaitTime);
    public float ReturnSpeed => Mathf.Max(0f, returnSpeed);
    public float ReturnStoppingDistance => Mathf.Max(0f, returnStoppingDistance);

    private void Awake()
    {
        ResolveReferences();
        stateMachine = new EnemyStateMachine(this);
        homePosition = transform.position;
        homeRotation = transform.rotation;
        spawnSequenceComplete = !requireSpawnAnimation;
        spawnSequenceStarted = !requireSpawnAnimation;
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (stateMachine == null)
            stateMachine = new EnemyStateMachine(this);
    }

    private void FixedUpdate()
    {
        ResolveReferences();

        if (!simulationEnabled || (enemySetup != null && !enemySetup.HasAuthority))
            return;

        if (enemyHealth == null || !enemyHealth.IsAlive)
        {
            ClearCurrentTarget();
            ClearCombatAwareness();
            StopNavigation();
            SetExternalState(EnemyState.Dead);
            return;
        }

        TickPerception();

        if (ShouldWaitForSpawnSequence())
        {
            UpdateSpawnSequenceGate();
            return;
        }

        ClearExternalState();
        EnsureBehaviorState();

        float deltaTime = Time.fixedDeltaTime;
        stateMachine.Tick(deltaTime);
        enemyNavigation?.Tick(deltaTime);
    }

    public void SetSimulationEnabled(bool enabled)
    {
        if (simulationEnabled == enabled)
            return;

        simulationEnabled = enabled;
        if (!enabled)
        {
            ClearCurrentTarget();
            ClearCombatAwareness();
            StopNavigation();
            enemyNavigation?.SetSimulationEnabled(false);
            if (enemyHealth != null && !enemyHealth.IsAlive)
                SetExternalState(EnemyState.Dead);
            else
                SetExternalState(ShouldWaitForSpawnSequence() ? EnemyState.PreSpawn : EnemyState.Idle);
            return;
        }

        ClearExternalState();
        enemyNavigation?.SetSimulationEnabled(true);
        EnsureBehaviorState();
    }

    public void ApplyReplicatedState(EnemyState state)
    {
        if (enemySetup != null && enemySetup.HasAuthority)
            return;

        SetExternalState(state);
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

        if (state != EnemyState.Chase && state != EnemyState.Patrol && state != EnemyState.Return && state != EnemyState.Search && state != EnemyState.Alert)
            StopNavigation();
    }

    public void CompleteSpawnSequence()
    {
        spawnSequenceStarted = true;
        spawnSequenceComplete = true;

        if (CurrentState == EnemyState.PreSpawn || CurrentState == EnemyState.Spawning)
            ClearExternalState();

        EnsureBehaviorState(forceReset: true);
    }

    public void NotifyDamaged(DamageInfo damageInfo)
    {
        ResolveReferences();

        if (!simulationEnabled || enemyHealth == null || !enemyHealth.IsAlive)
            return;

        if (ShouldWaitForSpawnSequence())
            CompleteSpawnSequence();

        PlayerHealth instigatorHealth = damageInfo.Instigator != null
            ? damageInfo.Instigator.GetComponentInParent<PlayerHealth>()
            : null;

        if (instigatorHealth != null && instigatorHealth.CanBeTargetedByEnemy)
        {
            currentTarget = instigatorHealth;
            Vector3 targetPosition = instigatorHealth.transform.position;
            RegisterCombatAwareness(targetPosition);
            SetInvestigationPosition(targetPosition);
            ChangeState(EnemyState.Chase);
            return;
        }

        Vector3 stimulusPosition = ResolveDamageStimulusPosition(damageInfo);
        RegisterCombatAwareness(stimulusPosition);
        SetInvestigationPosition(stimulusPosition);
        ChangeState(EnemyState.Alert);
    }

    public void ChangeState(EnemyState state)
    {
        ClearExternalState();
        if (stateMachine == null)
            stateMachine = new EnemyStateMachine(this);

        stateMachine.ChangeState(state);
    }

    public EnemyState ResolveDefaultBehaviorState()
    {
        if (defaultState == EnemyState.Patrol && !HasPatrolPoints)
            return EnemyState.Idle;

        if (defaultState == EnemyState.Chase
            || defaultState == EnemyState.Attack
            || defaultState == EnemyState.Search
            || defaultState == EnemyState.Return
            || defaultState == EnemyState.Alert
            || defaultState == EnemyState.Dead
            || defaultState == EnemyState.PreSpawn
            || defaultState == EnemyState.Spawning)
        {
            return HasPatrolPoints ? EnemyState.Patrol : EnemyState.Idle;
        }

        return defaultState;
    }

    public bool TryRefreshVisibleTarget()
    {
        if (enemyPerception == null)
            return false;

        if (!enemyPerception.TryGetVisibleTarget(currentTarget, out PlayerHealth visibleTarget, out Vector3 visiblePosition))
            return false;

        currentTarget = visibleTarget;
        RegisterCombatAwareness(visiblePosition);
        return true;
    }

    public bool TryRefreshCloseAwarenessTarget()
    {
        if (!enableCloseAwareness || CloseAwarenessRadius <= 0f)
            return false;

        if (TryResolveCloseAwarenessTarget(currentTarget, out PlayerHealth preferredTarget, out Vector3 preferredPosition))
        {
            currentTarget = preferredTarget;
            RegisterCombatAwareness(preferredPosition);
            SetInvestigationPosition(preferredPosition);
            return true;
        }

        EnemyTargetRegistry.PruneInvalidTargets();
        if (EnemyTargetRegistry.Count == 0)
            EnemyTargetRegistry.RefreshFromScene();

        PlayerHealth nearestTarget = null;
        Vector3 nearestPosition = Vector3.zero;
        float nearestDistanceSqr = float.PositiveInfinity;

        for (int i = 0; i < EnemyTargetRegistry.Count; i++)
        {
            PlayerHealth candidate = EnemyTargetRegistry.GetTarget(i);
            if (!TryResolveCloseAwarenessTarget(candidate, out _, out Vector3 candidatePosition))
                continue;

            float distanceSqr = Vector3.ProjectOnPlane(candidatePosition - transform.position, Vector3.up).sqrMagnitude;
            if (distanceSqr >= nearestDistanceSqr)
                continue;

            nearestTarget = candidate;
            nearestPosition = candidatePosition;
            nearestDistanceSqr = distanceSqr;
        }

        if (nearestTarget == null)
            return false;

        currentTarget = nearestTarget;
        RegisterCombatAwareness(nearestPosition);
        SetInvestigationPosition(nearestPosition);
        return true;
    }

    public bool TryRefreshCombatMemoryTarget()
    {
        if (!IsCombatMemoryActive() || !HasValidTarget || CombatMemoryAwarenessRadius <= 0f)
            return false;

        if (!TryResolveTargetInRadius(
                currentTarget,
                CombatMemoryAwarenessRadius,
                combatMemoryRequiresLineOfSight,
                out Vector3 targetPosition))
        {
            return false;
        }

        RegisterCombatAwareness(targetPosition);
        SetInvestigationPosition(targetPosition);
        return true;
    }

    public bool TryConsumeHeardNoise(out NoiseEvent noiseEvent)
    {
        noiseEvent = default;
        return enemyPerception != null && enemyPerception.TryConsumeHeardNoise(out noiseEvent);
    }

    public void SetLastKnownTargetPosition(Vector3 position)
    {
        lastKnownTargetPosition = position;
        hasLastKnownTargetPosition = true;
    }

    public void ClearLastKnownTargetPosition()
    {
        hasLastKnownTargetPosition = false;
        lastKnownTargetPosition = Vector3.zero;
    }

    public void SetInvestigationPosition(Vector3 position)
    {
        investigationPosition = position;
        hasInvestigationPosition = true;
    }

    public void ClearInvestigationPosition()
    {
        hasInvestigationPosition = false;
        investigationPosition = Vector3.zero;
    }

    public void ClearCurrentTarget()
    {
        currentTarget = null;
    }

    public void RegisterCombatAwareness(Vector3 position)
    {
        SetLastKnownTargetPosition(position);
        lastCombatAwarenessTime = Time.time;
        hasCombatAwareness = true;
    }

    public void ClearCombatAwareness()
    {
        hasCombatAwareness = false;
        lastCombatAwarenessTime = float.NegativeInfinity;
    }

    public void StopNavigation()
    {
        enemyNavigation?.Stop();
        enemyMotor?.Stop();
    }

    public bool SetNavigationDestination(Vector3 destination, float speed, float stoppingDistance, bool force)
    {
        if (enemyNavigation == null)
            return false;

        return enemyNavigation.SetDestination(destination, speed, stoppingDistance, force);
    }

    public bool NavigationHasReachedDestination()
    {
        return enemyNavigation == null || enemyNavigation.HasReachedDestination();
    }

    public bool TrySampleNavigationPosition(Vector3 candidatePosition, float maxDistance, out Vector3 sampledPosition)
    {
        if (enemyNavigation != null)
            return enemyNavigation.TrySamplePosition(candidatePosition, maxDistance, out sampledPosition);

        sampledPosition = candidatePosition;
        return false;
    }

    public void RotateTowards(Vector3 position, float deltaTime)
    {
        if (enemyNavigation != null)
            enemyNavigation.RotateTowardsPosition(position, deltaTime);
        else if (enemyMotor != null)
            enemyMotor.RotateTowards(Vector3.ProjectOnPlane(position - transform.position, Vector3.up), deltaTime);
    }

    public void TrackCurrentTargetDuringAttack(float deltaTime)
    {
        if (enemyAttack == null || !enemyAttack.CanTrackTargetDuringCurrentAttack || !HasValidTarget)
            return;

        Vector3 targetPosition = hasLastKnownTargetPosition ? lastKnownTargetPosition : currentTarget.transform.position;
        if (enemyNavigation != null)
            enemyNavigation.RotateTowardsPosition(targetPosition, deltaTime, enemyAttack.AttackTurnSpeedMultiplier);
        else if (enemyMotor != null)
            enemyMotor.RotateTowards(Vector3.ProjectOnPlane(targetPosition - transform.position, Vector3.up), deltaTime, enemyAttack.AttackTurnSpeedMultiplier);
    }

    public bool TryAttackCurrentTarget()
    {
        if (enemyAttack == null || !HasValidTarget)
            return false;

        Vector3 planarOffset = Vector3.ProjectOnPlane(currentTarget.transform.position - transform.position, Vector3.up);
        bool attackStarted = enemyAttack.TryAttack(currentTarget, planarOffset);
        if (attackStarted)
            RegisterCombatAwareness(currentTarget.transform.position);

        return attackStarted;
    }

    public bool IsCurrentTargetInsideAttackStartRange()
    {
        if (!HasValidTarget || enemyAttack == null)
            return false;

        Vector3 planarOffset = Vector3.ProjectOnPlane(currentTarget.transform.position - transform.position, Vector3.up);
        float attackStartRange = enemyAttack.AttackStartRange;
        return planarOffset.sqrMagnitude <= attackStartRange * attackStartRange;
    }

    public bool IsLastKnownPositionBeyondMaxChaseDistance()
    {
        if (!hasLastKnownTargetPosition || maxChaseDistance <= 0f)
            return false;

        Vector3 planarOffset = Vector3.ProjectOnPlane(lastKnownTargetPosition - homePosition, Vector3.up);
        return planarOffset.sqrMagnitude > maxChaseDistance * maxChaseDistance;
    }

    public bool IsCurrentTargetInsideCombatMemoryAwarenessRadius()
    {
        if (!HasValidTarget || CombatMemoryAwarenessRadius <= 0f)
            return false;

        return TryResolveTargetInRadius(
            currentTarget,
            CombatMemoryAwarenessRadius,
            combatMemoryRequiresLineOfSight,
            out _);
    }

    public float ResolveLostSightGraceDuration()
    {
        return IsCombatMemoryActive()
            ? Mathf.Max(LostSightGraceDuration, CombatLostSightGraceDuration)
            : LostSightGraceDuration;
    }

    public float ResolveActiveSearchDuration()
    {
        return IsCombatMemoryActive()
            ? Mathf.Max(SearchDuration, CombatSearchDuration)
            : SearchDuration;
    }

    public float ResolveActiveSearchRadius()
    {
        return IsCombatMemoryActive()
            ? Mathf.Max(SearchRadius, CombatSearchRadius)
            : SearchRadius;
    }

    public int ResolveActiveSearchPointCount()
    {
        return IsCombatMemoryActive()
            ? Mathf.Max(SearchPointCount, CombatSearchPointCount)
            : SearchPointCount;
    }

    public float ResolveActiveSearchPointWaitTime()
    {
        return IsCombatMemoryActive()
            ? Mathf.Max(SearchPointWaitTime, CombatSearchPointWaitTime)
            : SearchPointWaitTime;
    }

    public float ResolveActiveSearchSpeed()
    {
        return IsCombatMemoryActive()
            ? Mathf.Max(SearchSpeed, CombatSearchSpeed)
            : SearchSpeed;
    }

    public int ResolveInitialPatrolIndex()
    {
        int count = CountValidPatrolPoints();
        if (count <= 0)
            return -1;

        if (!startAtNearestPatrolPoint)
            return 0;

        int nearestIndex = -1;
        float nearestDistanceSqr = float.PositiveInfinity;
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            Transform point = patrolPoints[i];
            if (point == null)
                continue;

            float distanceSqr = Vector3.ProjectOnPlane(point.position - transform.position, Vector3.up).sqrMagnitude;
            if (distanceSqr >= nearestDistanceSqr)
                continue;

            nearestDistanceSqr = distanceSqr;
            nearestIndex = i;
        }

        return nearestIndex >= 0 ? nearestIndex : 0;
    }

    public int ResolveNextPatrolIndex(int currentIndex)
    {
        int count = CountValidPatrolPoints();
        if (count <= 0)
            return -1;

        if (patrolMode == EnemyPatrolMode.Random && count > 1)
            return ResolveRandomPatrolIndex(currentIndex);

        int nextIndex = currentIndex;
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            nextIndex = (nextIndex + 1) % patrolPoints.Length;
            if (patrolPoints[nextIndex] != null)
                return nextIndex;
        }

        return ResolveInitialPatrolIndex();
    }

    public bool TryGetPatrolPointPosition(int index, out Vector3 position)
    {
        position = Vector3.zero;
        if (patrolPoints == null || index < 0 || index >= patrolPoints.Length || patrolPoints[index] == null)
            return false;

        position = patrolPoints[index].position;
        return true;
    }

    public float ResolvePatrolWaitTime(int index)
    {
        if (patrolPoints != null && index >= 0 && index < patrolPoints.Length && patrolPoints[index] != null)
        {
            EnemyPatrolPoint patrolPoint = patrolPoints[index].GetComponent<EnemyPatrolPoint>();
            if (patrolPoint != null)
                return patrolPoint.ResolveWaitTime(patrolWaitTime);
        }

        return PatrolWaitTime;
    }

    public bool TryGetReturnPosition(out Vector3 returnPosition)
    {
        if (returnToPatrol && HasPatrolPoints)
        {
            int patrolIndex = ResolveInitialPatrolIndex();
            if (TryGetPatrolPointPosition(patrolIndex, out returnPosition))
                return true;
        }

        returnPosition = homePosition;
        return true;
    }

    private void TickPerception()
    {
        enemyPerception?.Tick(currentTarget);
    }

    private bool ShouldWaitForSpawnSequence()
    {
        return requireSpawnAnimation && !spawnSequenceComplete;
    }

    private void UpdateSpawnSequenceGate()
    {
        StopNavigation();

        if (!spawnSequenceStarted)
        {
            if (spawnTriggerMode == EnemySpawnTriggerMode.OnFirstAwareness && !HasInitialAwareness())
            {
                SetExternalState(EnemyState.PreSpawn);
                return;
            }

            spawnSequenceStarted = true;
        }

        SetExternalState(EnemyState.Spawning);
    }

    private bool HasInitialAwareness()
    {
        if (enemyPerception == null)
            return true;

        if (enemyPerception.HasVisibleTarget || enemyPerception.HasHeardNoise)
            return true;

        return enemyPerception.TryGetVisibleTarget(currentTarget, out _, out _)
            || TryRefreshCloseAwarenessTarget();
    }

    private Vector3 ResolveDamageStimulusPosition(DamageInfo damageInfo)
    {
        if (damageInfo.Instigator != null)
            return damageInfo.Instigator.transform.position;

        if (damageInfo.HitPoint.sqrMagnitude > 0.0001f)
            return damageInfo.HitPoint;

        return transform.position;
    }

    private bool IsCombatMemoryActive()
    {
        if (!hasCombatAwareness)
            return false;

        if (CombatMemoryDuration <= 0f)
            return IsCurrentTargetInsideCombatMemoryAwarenessRadius();

        if (Time.time <= lastCombatAwarenessTime + CombatMemoryDuration)
            return true;

        return TryResolveTargetInRadius(
            currentTarget,
            CombatMemoryAwarenessRadius,
            combatMemoryRequiresLineOfSight,
            out _);
    }

    private bool TryResolveCloseAwarenessTarget(
        PlayerHealth target,
        out PlayerHealth resolvedTarget,
        out Vector3 targetPosition)
    {
        resolvedTarget = null;
        targetPosition = Vector3.zero;

        if (target == null || !target.CanBeTargetedByEnemy)
            return false;

        if (!TryResolveTargetInRadius(
                target,
                CloseAwarenessRadius,
                closeAwarenessRequiresLineOfSight,
                out targetPosition))
        {
            return false;
        }

        resolvedTarget = target;
        return true;
    }

    private bool TryResolveTargetInRadius(
        PlayerHealth target,
        float radius,
        bool requireLineOfSight,
        out Vector3 targetPosition)
    {
        targetPosition = Vector3.zero;
        if (target == null || !target.CanBeTargetedByEnemy)
            return false;

        float safeRadius = Mathf.Max(0f, radius);
        if (safeRadius <= 0f)
            return false;

        EnemyVision vision = enemyPerception != null ? enemyPerception.Vision : GetComponent<EnemyVision>();
        if (vision != null)
        {
            return vision.CanSenseTargetInRadius(
                target,
                safeRadius,
                requireLineOfSight,
                out targetPosition);
        }

        targetPosition = target.transform.position;
        Vector3 planarOffset = Vector3.ProjectOnPlane(targetPosition - transform.position, Vector3.up);
        if (planarOffset.sqrMagnitude > safeRadius * safeRadius)
            return false;

        return true;
    }

    private float ResolveCombatMemoryRemaining()
    {
        if (!hasCombatAwareness || CombatMemoryDuration <= 0f)
            return 0f;

        return Mathf.Max(0f, lastCombatAwarenessTime + CombatMemoryDuration - Time.time);
    }

    private void EnsureBehaviorState(bool forceReset = false)
    {
        if (stateMachine == null)
            stateMachine = new EnemyStateMachine(this);

        if (!forceReset && stateMachine.HasCurrentState)
            return;

        stateMachine.ChangeState(ResolveDefaultBehaviorState());
    }

    private void SetExternalState(EnemyState state)
    {
        externalStateOverride = state;
        externalStateOverrideActive = true;
    }

    private void ClearExternalState()
    {
        externalStateOverrideActive = false;
    }

    private int CountValidPatrolPoints()
    {
        if (patrolPoints == null)
            return 0;

        int count = 0;
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            if (patrolPoints[i] != null)
                count++;
        }

        return count;
    }

    private int ResolveRandomPatrolIndex(int currentIndex)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int candidateIndex = UnityEngine.Random.Range(0, patrolPoints.Length);
            if (candidateIndex != currentIndex && patrolPoints[candidateIndex] != null)
                return candidateIndex;
        }

        return ResolveNextPatrolIndexSequential(currentIndex);
    }

    private int ResolveNextPatrolIndexSequential(int currentIndex)
    {
        int nextIndex = currentIndex;
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            nextIndex = (nextIndex + 1) % patrolPoints.Length;
            if (patrolPoints[nextIndex] != null)
                return nextIndex;
        }

        return -1;
    }

    private void ResolveReferences()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (enemyMotor == null)
            enemyMotor = GetComponent<EnemyMotor>();

        if (enemyNavigation == null)
            enemyNavigation = GetComponent<EnemyNavigation>();

        if (enemyPerception == null)
            enemyPerception = GetComponent<EnemyPerception>();

        if (enemyAttack == null)
            enemyAttack = GetComponent<EnemyAttack>();

        if (enemyAnimationController == null)
            enemyAnimationController = GetComponent<EnemyAnimationController>();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
            return;

        DrawDebugDisc(closeAwarenessColor, enableCloseAwareness && CloseAwarenessRadius > 0f, transform.position, CloseAwarenessRadius);
        DrawDebugDisc(combatMemoryColor, HasActiveCombatMemory && CombatMemoryAwarenessRadius > 0f, transform.position, CombatMemoryAwarenessRadius);
        DrawDebugSphere(lastKnownPositionColor, hasLastKnownTargetPosition, lastKnownTargetPosition, 0.28f);
        DrawDebugLine(lastKnownPositionColor, hasLastKnownTargetPosition, transform.position, lastKnownTargetPosition);
        DrawDebugSphere(investigationPositionColor, hasInvestigationPosition, investigationPosition, 0.24f);
        DrawDebugLine(investigationPositionColor, hasInvestigationPosition, transform.position, investigationPosition);

        EnemyNavigation navigation = enemyNavigation != null ? enemyNavigation : GetComponent<EnemyNavigation>();
        if (navigation != null && navigation.HasDestination)
        {
            DrawDebugSphere(destinationColor, true, navigation.CurrentDestination, 0.14f);
            DrawDebugLine(destinationColor, true, transform.position, navigation.CurrentDestination);
        }

#if UNITY_EDITOR
        if (!drawDebugLabels)
            return;

        string targetName = currentTarget != null ? currentTarget.name : "none";
        string combatMemoryText = HasActiveCombatMemory
            ? $"active ({CombatMemoryRemaining:0.0}s)"
            : "inactive";
        string label = $"Enemy AI\nState: {CurrentState}\nTarget: {targetName}\nCombat Memory: {combatMemoryText}";
        Handles.Label(transform.position + Vector3.up * 2.25f, label);
#endif
    }

    private static void DrawDebugSphere(Color color, bool shouldDraw, Vector3 position, float radius)
    {
        if (!shouldDraw)
            return;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(position, Mathf.Max(0.01f, radius));
    }

    private static void DrawDebugLine(Color color, bool shouldDraw, Vector3 from, Vector3 to)
    {
        if (!shouldDraw)
            return;

        Gizmos.color = color;
        Gizmos.DrawLine(from + Vector3.up * 0.12f, to + Vector3.up * 0.12f);
    }

    private static void DrawDebugDisc(Color color, bool shouldDraw, Vector3 center, float radius)
    {
        if (!shouldDraw)
            return;

        float safeRadius = Mathf.Max(0.01f, radius);
#if UNITY_EDITOR
        Handles.color = color;
        Handles.DrawWireDisc(center + Vector3.up * 0.05f, Vector3.up, safeRadius);
#endif
        Gizmos.color = color;
        Gizmos.DrawWireSphere(center, safeRadius);
    }
}
