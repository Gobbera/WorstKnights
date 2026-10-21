using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth), typeof(EnemyMotor), typeof(EnemyAttack))]
public class EnemyBrain : MonoBehaviour
{
    private const float RecentContactDuration = 0.15f;

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
    [SerializeField] private bool useGeneratedHomePatrol;
    [SerializeField] [Min(0f)] private float generatedHomePatrolRadius = 4f;
    [SerializeField] [Min(2)] private int generatedHomePatrolPointCount = 4;

    [Header("Global Patrol")]
    [SerializeField] private Transform globalPatrolCenter;
    [SerializeField] [Min(0f)] private float globalPatrolRadius = 32f;
    [SerializeField] [Min(0f)] private float globalPatrolMinimumDistance = 8f;
    [SerializeField] [Min(0.1f)] private float globalPatrolSampleDistance = 8f;
    [SerializeField] [Min(1)] private int globalPatrolSampleAttempts = 12;

    [Header("Alert")]
    [SerializeField] [Min(0f)] private float alertDuration = 1.5f;
    [SerializeField] private bool alertBeforeChaseOnFirstTargetDetection = true;
    [SerializeField] private bool rotateDuringAlert = true;
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

    [Header("Last Seen Search")]
    [SerializeField] private bool useLastSeenSearchFlow;
    [SerializeField] [Min(0f)] private float reflectionPauseDuration = 2.5f;
    [SerializeField] [Min(0f)] private float searchToReflectionDelay = 4f;
    [SerializeField] [Min(0f)] private float lastSeenStallTimeout = 6f;
    [SerializeField] [Min(0.05f)] private float lastSeenProgressDistance = 0.4f;
    [SerializeField] [Min(0f)] private float targetSwitchDistanceAdvantage = 2f;

    [Header("Room Investigation")]
    [SerializeField] [Min(0f)] private float roomChangeConfirmationTime = 0.2f;
    [SerializeField] [Min(1f)] private float playerEvidenceLifetime = 45f;

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
    private bool activeAlertHasTarget;
    private int activeAlertSequence;
    private bool alertArmed = true;
    private PlayerHealth recentlyTouchedPlayer;
    private float lastPlayerContactTime = float.NegativeInfinity;
    private EnemyTraversalVolume investigationDoorVolume;
    private EnemyTraversalSide investigationDoorSide;
    private EnemySearchArea lastKnownRoom;
    private float stunnedUntil = float.NegativeInfinity;
    private int traversalLockCount;
    private BeholderMoodController beholderMood;
    private readonly EnemyInvestigationMemory investigationMemory = new EnemyInvestigationMemory();
    private float nextEvidenceVisionScanTime;
    private float lastCurrentTargetHearingTime = float.NegativeInfinity;

    public EnemyState CurrentState => externalStateOverrideActive ? externalStateOverride : (stateMachine != null ? stateMachine.CurrentStateId : EnemyState.Idle);
    public PlayerHealth CurrentTarget => currentTarget;
    public Vector3 LastKnownTargetPosition => lastKnownTargetPosition;
    public EnemySearchArea CurrentRoom
    {
        get
        {
            if (lastKnownRoom != null && lastKnownRoom.isActiveAndEnabled
                && lastKnownRoom.ContainsPoint(transform.position))
                return lastKnownRoom;

            EnemySearchArea room = EnemySearchArea.FindContaining(transform.position, gameObject.scene);
            if (room != null)
                lastKnownRoom = room;
            return lastKnownRoom != null && lastKnownRoom.isActiveAndEnabled ? lastKnownRoom : null;
        }
    }
    public Vector3 LastKnownPursuitPosition
    {
        get
        {
            if (!HasDoorInvestigation
                || !investigationDoorVolume.TryGetSidePosition(investigationDoorSide, out Vector3 exitPosition))
                return lastKnownTargetPosition;

            Vector3 exitOffset = Vector3.ProjectOnPlane(lastKnownTargetPosition - exitPosition, Vector3.up);
            return investigationDoorVolume.ResolveSide(lastKnownTargetPosition) == investigationDoorSide
                && (investigationDoorVolume.HasPassedSide(lastKnownTargetPosition, investigationDoorSide, 0f)
                    || exitOffset.sqrMagnitude <= 0.45f * 0.45f)
                ? lastKnownTargetPosition : exitPosition;
        }
    }
    public Vector3 InvestigationPosition => investigationPosition;
    public Vector3 HomePosition => homePosition;
    public bool HasLastKnownTargetPosition => hasLastKnownTargetPosition;
    public bool HasDoorInvestigation => investigationDoorVolume != null
        && investigationDoorSide != EnemyTraversalSide.None;
    public bool HasInvestigationPosition => hasInvestigationPosition;
    public bool HasValidTarget => currentTarget != null && currentTarget.CanBeTargetedByEnemy;
    public bool HasPatrolPoints => CountResolvedPatrolPoints() > 0;
    public bool ShouldReturnToPatrol => returnToPatrol;
    public bool MoveToAlertPosition => moveToAlertPosition;
    public bool RotateDuringAlert => rotateDuringAlert;
    public bool HasTargetAwareness => HasValidTarget || HasActiveCombatMemory;
    public bool HasActiveTargetAlert => activeAlertHasTarget && HasValidTarget;
    public int ActiveAlertSequence => activeAlertSequence;
    public bool CanTriggerAlert => !useLastSeenSearchFlow || alertArmed;
    public bool AlertArmed => alertArmed;
    public bool IsAttackLocked => enemyAttack != null && enemyAttack.IsAttackLocked;
    public bool CanStartAttack => enemyAttack != null && enemyAttack.CanStartAttack;
    public bool IsStunned => Time.time < stunnedUntil;
    public bool IsTraversing => traversalLockCount > 0;
    public float StunRemaining => Mathf.Max(0f, stunnedUntil - Time.time);
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
    public bool UseLastSeenSearchFlow => useLastSeenSearchFlow;
    public bool HasRoomEvidenceForCurrentTarget => useLastSeenSearchFlow
        && investigationMemory.TryGetSignalArea(currentTarget, out _);
    public bool HasInvestigationOrigin => investigationMemory.HasInvestigationOrigin;
    public bool HasFreshCurrentTargetHearing => Time.time - lastCurrentTargetHearingTime <= 0.5f;
    public float ReflectionPauseDuration => Mathf.Max(0f, reflectionPauseDuration);
    public float SearchToReflectionDelay => Mathf.Max(0f, searchToReflectionDelay);
    public float LastSeenStallTimeout => Mathf.Max(0f, lastSeenStallTimeout);
    public float LastSeenProgressDistance => Mathf.Max(0.05f, lastSeenProgressDistance);
    public float ReturnSpeed => Mathf.Max(0f, returnSpeed);
    public event Action ReflectionCompleted;

    public void SetEmotionalSpeedMultiplier(float multiplier)
    {
        enemyNavigation?.SetSpeedMultiplier(multiplier);
    }

    public float ReturnStoppingDistance => Mathf.Max(0f, returnStoppingDistance);

    private void Awake()
    {
        ResolveReferences();
        beholderMood = GetComponent<BeholderMoodController>();
        stateMachine = new EnemyStateMachine(this);
        homePosition = transform.position;
        homeRotation = transform.rotation;
        spawnSequenceComplete = !requireSpawnAnimation;
        spawnSequenceStarted = !requireSpawnAnimation;
    }

    private void OnEnable()
    {
        ResolveReferences();
        beholderMood = GetComponent<BeholderMoodController>();
        if (stateMachine == null)
            stateMachine = new EnemyStateMachine(this);

        if (enemyPerception != null && enemyPerception.Hearing != null)
            enemyPerception.Hearing.PlayerHeard += OnPlayerHeard;
    }

    private void OnDisable()
    {
        if (enemyPerception != null && enemyPerception.Hearing != null)
            enemyPerception.Hearing.PlayerHeard -= OnPlayerHeard;
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
            investigationMemory.Clear();
            ClearActiveAlertContext();
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

        if (IsStunned)
        {
            StopNavigation();
            SetExternalState(EnemyState.Stunned);
            return;
        }

        if (IsTraversing)
        {
            SetExternalState(EnemyState.Traversal);
            return;
        }

        ClearExternalState();
        EnsureBehaviorState();

        float deltaTime = Time.fixedDeltaTime;
        stateMachine.Tick(deltaTime);
        enemyNavigation?.Tick(deltaTime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        ReportPlayerContact(collision.collider.GetComponentInParent<PlayerHealth>());
    }

    private void OnCollisionStay(Collision collision)
    {
        ReportPlayerContact(collision.collider.GetComponentInParent<PlayerHealth>());
    }

    public void ReportPlayerContact(PlayerHealth player)
    {
        if (!useLastSeenSearchFlow || !simulationEnabled || ShouldWaitForSpawnSequence()
            || (enemySetup != null && !enemySetup.HasAuthority)
            || enemyHealth == null || !enemyHealth.IsAlive
            || player == null || !player.CanBeTargetedByEnemy)
            return;

        if ((CurrentState == EnemyState.Chase || CurrentState == EnemyState.Attack)
            && currentTarget != null && currentTarget != player)
        {
            RecordPlayerEvidence(player, player.transform.position);
            return;
        }

        recentlyTouchedPlayer = player;
        lastPlayerContactTime = Time.time;
        investigationMemory.ClearOrigin();
        currentTarget = player;
        Vector3 position = player.transform.position;
        ClearDoorInvestigation();
        RegisterCombatAwareness(position);
        RecordPlayerEvidence(player, position);
        SetInvestigationPosition(position);

        EnemyState state = CurrentState;
        if (state == EnemyState.Chase || state == EnemyState.Attack)
            return;

        if (state == EnemyState.Alert)
        {
            if (!HasActiveTargetAlert)
                ConfirmTargetDuringAlert();
            return;
        }

        if (CanTriggerAlert)
        {
            BeginTargetAlert();
            ChangeState(EnemyState.Alert);
        }
        else
        {
            ChangeState(EnemyState.Chase);
        }
    }

    public void SetSimulationEnabled(bool enabled)
    {
        if (simulationEnabled == enabled)
            return;

        simulationEnabled = enabled;
        if (!enabled)
        {
            ClearCurrentTarget();
            recentlyTouchedPlayer = null;
            lastPlayerContactTime = float.NegativeInfinity;
            ClearDoorInvestigation();
            ClearCombatAwareness();
            investigationMemory.Clear();
            ClearActiveAlertContext();
            traversalLockCount = 0;
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

    public void ApplyReplicatedAlertArmed(bool armed)
    {
        if (enemySetup != null && enemySetup.HasAuthority)
            return;

        alertArmed = armed;
    }

    public void CompleteSpawnSequence()
    {
        spawnSequenceStarted = true;
        spawnSequenceComplete = true;

        if (CurrentState == EnemyState.PreSpawn || CurrentState == EnemyState.Spawning)
            ClearExternalState();

        EnsureBehaviorState(forceReset: true);
    }

    public void ApplyStun(float duration)
    {
        float safeDuration = Mathf.Max(0f, duration);
        if (safeDuration <= 0f)
            return;

        stunnedUntil = Mathf.Max(stunnedUntil, Time.time + safeDuration);
        StopNavigation();
        SetExternalState(EnemyState.Stunned);
    }

    public void BeginTraversal()
    {
        traversalLockCount++;
        enemyNavigation?.Pause();
        enemyMotor?.Stop();
        SetExternalState(EnemyState.Traversal);
    }

    public void EndTraversal()
    {
        if (traversalLockCount <= 0)
            return;

        traversalLockCount--;
        if (traversalLockCount > 0)
            return;

        if (CurrentState == EnemyState.Traversal)
            ClearExternalState();

        if (simulationEnabled
            && (enemySetup == null || enemySetup.HasAuthority)
            && !IsStunned
            && (enemyHealth == null || enemyHealth.IsAlive))
            enemyNavigation?.Resume();

        EnsureBehaviorState();
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
            RecordPlayerEvidence(instigatorHealth, targetPosition);
            SetInvestigationPosition(targetPosition);
            ChangeState(EnemyState.Chase);
            return;
        }

        Vector3 stimulusPosition = ResolveDamageStimulusPosition(damageInfo);
        RegisterCombatAwareness(stimulusPosition);
        if (!CanTriggerAlert)
        {
            if (CurrentState != EnemyState.Chase && CurrentState != EnemyState.Attack
                && CurrentState != EnemyState.Alert)
                ChangeState(EnemyState.Search);
            return;
        }

        BeginInvestigationAlert(stimulusPosition);
        ChangeState(EnemyState.Alert);
    }

    public void ChangeState(EnemyState state)
    {
        bool preserveStunOverride = externalStateOverrideActive
            && externalStateOverride == EnemyState.Stunned
            && IsStunned
            && state != EnemyState.Stunned
            && state != EnemyState.Dead;
        bool preserveTraversalOverride = externalStateOverrideActive
            && externalStateOverride == EnemyState.Traversal
            && IsTraversing
            && state != EnemyState.Traversal
            && state != EnemyState.Stunned
            && state != EnemyState.Dead;

        if (!preserveStunOverride && !preserveTraversalOverride)
            ClearExternalState();

        if (state == EnemyState.Stunned)
        {
            StopNavigation();
            SetExternalState(EnemyState.Stunned);
            return;
        }

        if (state == EnemyState.Traversal)
        {
            StopNavigation();
            SetExternalState(EnemyState.Traversal);
            return;
        }

        if (state != EnemyState.Alert)
            ClearActiveAlertContext();

        if (useLastSeenSearchFlow && state == EnemyState.Chase)
            alertArmed = false;

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
            || defaultState == EnemyState.Reflection
            || defaultState == EnemyState.Return
            || defaultState == EnemyState.Alert
            || defaultState == EnemyState.Dead
            || defaultState == EnemyState.PreSpawn
            || defaultState == EnemyState.Spawning
            || defaultState == EnemyState.Stunned
            || defaultState == EnemyState.Traversal)
        {
            return HasPatrolPoints ? EnemyState.Patrol : EnemyState.Idle;
        }

        return defaultState;
    }

    public bool TryRefreshVisibleTarget()
    {
        if (enemyPerception == null)
            return false;

        if (useLastSeenSearchFlow && currentTarget != null
            && (CurrentState == EnemyState.Chase || CurrentState == EnemyState.Attack)
            && enemyPerception.Vision != null)
        {
            if (!enemyPerception.Vision.CanSeeTarget(currentTarget, out Vector3 trackedPosition))
                return false;

            ClearDoorInvestigation();
            RegisterCombatAwareness(trackedPosition);
            RecordPlayerEvidence(currentTarget, trackedPosition);
            investigationMemory.ClearOrigin();
            return true;
        }

        PlayerHealth preferredTarget = useLastSeenSearchFlow ? null : currentTarget;
        if (!enemyPerception.TryGetVisibleTarget(preferredTarget, out PlayerHealth visibleTarget, out Vector3 visiblePosition))
            return false;

        if (useLastSeenSearchFlow && currentTarget != null && currentTarget != visibleTarget
            && enemyPerception.Vision != null
            && enemyPerception.Vision.CanSeeTarget(currentTarget, out Vector3 currentPosition))
        {
            Vector3 origin = enemyPerception.Vision.EyePosition;
            float currentDistance = Vector3.ProjectOnPlane(currentPosition - origin, Vector3.up).magnitude;
            float candidateDistance = Vector3.ProjectOnPlane(visiblePosition - origin, Vector3.up).magnitude;
            if (candidateDistance + targetSwitchDistanceAdvantage >= currentDistance)
            {
                visibleTarget = currentTarget;
                visiblePosition = currentPosition;
            }
        }

        if (!HasDoorInvestigation || currentTarget != visibleTarget
            || investigationDoorVolume.ResolveSide(visiblePosition) != investigationDoorSide
            || HasEnteredDoorInvestigationSide())
            ClearDoorInvestigation();
        currentTarget = visibleTarget;
        RegisterCombatAwareness(visiblePosition);
        RecordPlayerEvidence(visibleTarget, visiblePosition);
        investigationMemory.ClearOrigin();
        return true;
    }

    public bool TryRefreshPursuitTarget()
    {
        if (useLastSeenSearchFlow && recentlyTouchedPlayer != null
            && recentlyTouchedPlayer.CanBeTargetedByEnemy
            && Time.time <= lastPlayerContactTime + RecentContactDuration)
        {
            currentTarget = recentlyTouchedPlayer;
            RegisterCombatAwareness(currentTarget.transform.position);
            RecordPlayerEvidence(currentTarget, currentTarget.transform.position);
            investigationMemory.ClearOrigin();
            return true;
        }

        if (TryRefreshVisibleTarget())
            return true;

        return !useLastSeenSearchFlow
            && (TryRefreshCombatMemoryTarget() || TryRefreshCloseAwarenessTarget());
    }

    public void ApplyReplicatedTarget(PlayerHealth target)
    {
        if (enemySetup != null && enemySetup.HasAuthority)
            return;

        currentTarget = target;
    }

    public void NotifyReflectionCompleted()
    {
        alertArmed = true;
        recentlyTouchedPlayer = null;
        lastPlayerContactTime = float.NegativeInfinity;
        ReflectionCompleted?.Invoke();
        investigationMemory.Clear();
    }

    public bool TryRefreshCloseAwarenessTarget()
    {
        if (!enableCloseAwareness || CloseAwarenessRadius <= 0f)
            return false;

        if (TryResolveCloseAwarenessTarget(currentTarget, out PlayerHealth preferredTarget, out Vector3 preferredPosition))
        {
            currentTarget = preferredTarget;
            RegisterCombatAwareness(preferredPosition);
            RecordPlayerEvidence(preferredTarget, preferredPosition);
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
        RecordPlayerEvidence(nearestTarget, nearestPosition);
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
        RecordPlayerEvidence(currentTarget, targetPosition);
        SetInvestigationPosition(targetPosition);
        return true;
    }

    public bool TryConsumeHeardNoise(out NoiseEvent noiseEvent)
    {
        noiseEvent = default;
        return enemyPerception != null && enemyPerception.TryConsumeHeardNoise(out noiseEvent);
    }

    public bool TryBeginPursuitFromHeardPlayer(PlayerHealth player, Vector3 position)
    {
        if (!useLastSeenSearchFlow || player == null || !player.CanBeTargetedByEnemy)
            return false;

        currentTarget = player;
        recentlyTouchedPlayer = null;
        lastPlayerContactTime = float.NegativeInfinity;
        investigationMemory.ClearOrigin();
        ClearDoorInvestigation();
        RegisterCombatAwareness(position);
        RecordPlayerEvidence(player, position);
        return true;
    }

    public bool ShouldAlertBeforeChase(bool hadTargetAwareness)
    {
        return alertBeforeChaseOnFirstTargetDetection
            && AlertDuration > 0f
            && CanTriggerAlert
            && !hadTargetAwareness;
    }

    public void BeginTargetAlert()
    {
        if (useLastSeenSearchFlow)
            alertArmed = false;

        activeAlertHasTarget = HasValidTarget;
        if (HasValidTarget)
        {
            Vector3 targetPosition = currentTarget.transform.position;
            RegisterCombatAwareness(targetPosition);
            SetInvestigationPosition(targetPosition);
        }

        activeAlertSequence++;
    }

    public void ConfirmTargetDuringAlert()
    {
        if (!HasValidTarget)
            return;

        activeAlertHasTarget = true;
        Vector3 targetPosition = currentTarget.transform.position;
        RegisterCombatAwareness(targetPosition);
        SetInvestigationPosition(targetPosition);
    }

    public void BeginInvestigationAlert(Vector3 position)
    {
        if (useLastSeenSearchFlow)
            alertArmed = false;

        activeAlertHasTarget = false;
        SetInvestigationPosition(position);
        activeAlertSequence++;
    }

    public void ClearActiveAlertContext()
    {
        activeAlertHasTarget = false;
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
        ClearDoorInvestigation();
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
        lastCurrentTargetHearingTime = float.NegativeInfinity;
    }

    public void BeginLastKnownInvestigation()
    {
        if (useLastSeenSearchFlow)
            investigationMemory.BeginInvestigation(currentTarget);
    }

    public bool TryPursueNextPlayerEvidence()
    {
        if (!useLastSeenSearchFlow)
            return false;

        PlayerHealth previousTarget = currentTarget;
        investigationMemory.MarkPointVisited(previousTarget);
        if (!investigationMemory.TryGetNextPoint(previousTarget, Time.time, playerEvidenceLifetime,
                out PlayerHealth nextTarget, out Vector3 position))
            return false;

        currentTarget = nextTarget;
        recentlyTouchedPlayer = null;
        lastPlayerContactTime = float.NegativeInfinity;
        lastCurrentTargetHearingTime = float.NegativeInfinity;
        ClearDoorInvestigation();
        RegisterCombatAwareness(position);
        return true;
    }

    public void BuildRoomSearchRoute(List<EnemySearchArea> route)
    {
        investigationMemory.BuildSearchRoute(route, Time.time, playerEvidenceLifetime);
    }

    public EnemySearchArea InvestigationPrimaryArea => investigationMemory.PrimaryArea;
    public PlayerHealth InvestigationPrimaryPlayer => investigationMemory.PrimaryPlayer;

    public void CaptureInvestigation(List<EnemyInvestigationMemory.Snapshot> snapshots)
    {
        investigationMemory.Capture(snapshots, Time.time, playerEvidenceLifetime);
    }

    public void ApplyReplicatedInvestigation(List<EnemyInvestigationMemory.Snapshot> snapshots,
        EnemySearchArea primaryArea, PlayerHealth primaryPlayer, Vector3 lastKnown,
        bool hasLastKnown, float transportAge)
    {
        if (enemySetup != null && enemySetup.HasAuthority)
            return;

        investigationMemory.Restore(snapshots, primaryArea, primaryPlayer, Time.time, transportAge);
        if (hasLastKnown)
            SetLastKnownTargetPosition(lastKnown);
        else
            ClearLastKnownTargetPosition();
    }

    public void RestoreAuthorityAfterHandoff(EnemyState state, PlayerHealth target,
        Vector3 lastKnown, bool hasLastKnown, bool wasAlertArmed)
    {
        currentTarget = target;
        alertArmed = wasAlertArmed;
        if (hasLastKnown)
            SetLastKnownTargetPosition(lastKnown);

        EnemyState resumeState = state == EnemyState.Traversal || state == EnemyState.Attack
            ? EnemyState.Chase : state;
        if (resumeState == EnemyState.Chase && !hasLastKnown)
            resumeState = EnemyState.Search;
        if (resumeState == EnemyState.Search && !hasLastKnown)
            resumeState = ResolveDefaultBehaviorState();
        if (resumeState == EnemyState.Alert && target == null)
            resumeState = hasLastKnown ? EnemyState.Search : ResolveDefaultBehaviorState();
        ChangeState(resumeState);
    }

    public bool TryPrepareDoorForCurrentEvidence()
    {
        if (!HasRoomEvidenceForCurrentTarget
            || !investigationMemory.TryGetSignalArea(currentTarget, out EnemySearchArea destination))
            return false;

        EnemySearchArea currentArea = CurrentRoom;
        if (currentArea == destination)
        {
            ClearDoorInvestigation();
            return false;
        }

        if (HasDoorInvestigation && investigationDoorVolume.GetSearchArea(investigationDoorSide) == destination)
            return true;

        return currentArea != null && currentArea != destination
            && TryPrepareDoorBetweenAreas(currentArea, destination);
    }

    public bool TryPrepareDoorBetweenAreas(EnemySearchArea from, EnemySearchArea to)
    {
        if (from == null || to == null || from == to)
            return false;

        EnemyTraversalVolume nearest = null;
        EnemyTraversalSide exitSide = EnemyTraversalSide.None;
        float nearestDistanceSqr = float.PositiveInfinity;
        foreach (EnemyTraversalVolume volume in EnemyTraversalVolume.ActiveVolumes)
        {
            if (volume == null || volume.gameObject.scene != gameObject.scene
                || (volume.DoorController != null && volume.DoorController.IsLocked))
                continue;

            EnemyTraversalSide candidateSide = volume.GetSearchArea(EnemyTraversalSide.SideA) == from
                && volume.GetSearchArea(EnemyTraversalSide.SideB) == to ? EnemyTraversalSide.SideB
                : volume.GetSearchArea(EnemyTraversalSide.SideB) == from
                    && volume.GetSearchArea(EnemyTraversalSide.SideA) == to ? EnemyTraversalSide.SideA
                    : EnemyTraversalSide.None;
            if (candidateSide == EnemyTraversalSide.None
                || !volume.TryGetSidePosition(candidateSide, out _))
                continue;

            float distanceSqr = (volume.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr >= nearestDistanceSqr)
                continue;

            nearest = volume;
            exitSide = candidateSide;
            nearestDistanceSqr = distanceSqr;
        }

        if (nearest == null)
            return false;

        SetDoorInvestigation(nearest, exitSide);
        return true;
    }

    public bool TryGetDoorInvestigation(out EnemyTraversalVolume volume, out EnemyTraversalSide side)
    {
        volume = investigationDoorVolume;
        side = investigationDoorSide;
        return HasDoorInvestigation;
    }

    public bool HasEnteredDoorInvestigationSide()
    {
        if (!TryGetDoorInvestigation(out EnemyTraversalVolume volume, out EnemyTraversalSide side))
            return true;

        EnemySearchArea destinationRoom = volume.GetSearchArea(side);
        if (destinationRoom != null)
            return CurrentRoom == destinationRoom;

        if (volume.ResolveSide(transform.position) != side
            || !volume.TryGetSidePosition(side, out Vector3 sidePosition))
            return false;

        Vector3 offset = Vector3.ProjectOnPlane(transform.position - sidePosition, Vector3.up);
        return offset.sqrMagnitude <= 0.45f * 0.45f
            || volume.HasPassedSide(transform.position, side, 0.15f);
    }

    public bool TryApproachDoorInvestigationEntry()
    {
        if (!TryGetDoorInvestigation(out EnemyTraversalVolume volume, out EnemyTraversalSide exitSide)
            || enemyNavigation == null)
            return false;

        EnemyTraversalSide entrySide = exitSide == EnemyTraversalSide.SideA
            ? EnemyTraversalSide.SideB : EnemyTraversalSide.SideA;
        return volume.TryGetSidePosition(entrySide, out Vector3 entryPosition)
            && enemyNavigation.TryApproachTraversalEntry(entryPosition, volume, entrySide);
    }

    private void SetDoorInvestigation(EnemyTraversalVolume volume, EnemyTraversalSide side)
    {
        investigationDoorVolume = volume;
        investigationDoorSide = side;
    }

    private void ClearDoorInvestigation()
    {
        investigationDoorVolume = null;
        investigationDoorSide = EnemyTraversalSide.None;
    }

    public void ClearRoomCrossing()
    {
        ClearDoorInvestigation();
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

    public bool SetLastKnownNavigationDestination(float speed, float stoppingDistance, bool force)
    {
        if (enemyNavigation == null || !hasLastKnownTargetPosition)
            return false;

        Vector3 destination = LastKnownPursuitPosition;
        if (HasDoorInvestigation && !HasEnteredDoorInvestigationSide())
            return enemyNavigation.SetDestination(destination, speed, stoppingDistance, force);

        return enemyNavigation.TryResolveReachablePosition(destination, SearchRadius, out Vector3 reachable)
            && enemyNavigation.SetDestination(reachable, speed, stoppingDistance, force);
    }

    public bool TryResolveReachableNavigationPosition(Vector3 desired, float maxDistance, out Vector3 reachable)
    {
        if (enemyNavigation != null)
            return enemyNavigation.TryResolveReachablePosition(desired, maxDistance, out reachable);

        reachable = desired;
        return false;
    }

    public bool NavigationHasReachedDestination()
    {
        return enemyNavigation == null || enemyNavigation.HasReachedDestination();
    }

    public bool HasReachedLastKnownTargetPosition()
    {
        if (!hasLastKnownTargetPosition || enemyNavigation == null || !enemyNavigation.HasDestination
            || !HasEnteredDoorInvestigationSide()
            || !NavigationHasReachedDestination())
            return false;

        Vector3 offset = Vector3.ProjectOnPlane(enemyNavigation.CurrentDestination - transform.position, Vector3.up);
        float reachDistance = SearchStoppingDistance + 0.15f;
        return offset.sqrMagnitude <= reachDistance * reachDistance;
    }

    public bool NavigationHasUsablePath()
    {
        return enemyNavigation != null && enemyNavigation.HasUsablePath();
    }

    public bool NavigationCanReach(Vector3 destination)
    {
        return enemyNavigation != null && enemyNavigation.IsReachable(destination);
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
        if (enemyAttack == null || !enemyAttack.CanStartAttack || !HasValidTarget)
            return false;

        Vector3 planarOffset = Vector3.ProjectOnPlane(currentTarget.transform.position - transform.position, Vector3.up);
        bool attackStarted = enemyAttack.TryAttack(currentTarget, planarOffset);
        if (attackStarted)
            RegisterCombatAwareness(currentTarget.transform.position);

        return attackStarted;
    }

    public bool IsCurrentTargetInsideAttackStartRange()
    {
        if (!HasValidTarget || enemyAttack == null || !enemyAttack.CanStartAttack)
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
        if (useLastSeenSearchFlow)
            return Mathf.Max(1f, LostSightGraceDuration * (beholderMood != null
                ? beholderMood.ChasePersistenceMultiplier : 1f));

        return IsCombatMemoryActive()
            ? Mathf.Max(LostSightGraceDuration, CombatLostSightGraceDuration)
            : LostSightGraceDuration;
    }

    public float ResolveLastSeenStallTimeout()
    {
        float multiplier = useLastSeenSearchFlow && beholderMood != null
            ? beholderMood.ChasePersistenceMultiplier : 1f;
        return LastSeenStallTimeout * Mathf.Max(0.1f, multiplier);
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
        int count = CountResolvedPatrolPoints();
        if (count <= 0)
            return -1;

        if (ShouldUseGeneratedHomePatrol())
            return 0;

        if (ShouldUseGlobalPatrol())
            return 0;

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
        int count = CountResolvedPatrolPoints();
        if (count <= 0)
            return -1;

        if (ShouldUseGeneratedHomePatrol())
            return ResolveNextGeneratedPatrolIndex(currentIndex, count);

        if (ShouldUseGlobalPatrol())
            return 0;

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
        if (ShouldUseGeneratedHomePatrol())
            return TryGetGeneratedPatrolPointPosition(index, out position);

        if (ShouldUseGlobalPatrol())
            return TryGetGlobalPatrolPointPosition(out position);

        if (patrolPoints == null || index < 0 || index >= patrolPoints.Length || patrolPoints[index] == null)
            return false;

        position = patrolPoints[index].position;
        return true;
    }

    public float ResolvePatrolWaitTime(int index)
    {
        if (ShouldUseGeneratedHomePatrol())
            return PatrolWaitTime;

        if (ShouldUseGlobalPatrol())
            return PatrolWaitTime;

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
        if (returnToPatrol && ShouldUseGlobalPatrol() && TryGetGlobalPatrolPointPosition(out returnPosition))
            return true;

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
        enemyPerception?.Tick(useLastSeenSearchFlow ? null : currentTarget);
        if (enemyPerception == null)
            return;

        if (useLastSeenSearchFlow)
        {
            EnemyVision vision = enemyPerception.Vision;
            if (vision != null && Time.time >= nextEvidenceVisionScanTime)
            {
                nextEvidenceVisionScanTime = Time.time + vision.DetectionUpdateInterval;
                EnemyTargetRegistry.PruneInvalidTargets();
                if (EnemyTargetRegistry.Count == 0)
                    EnemyTargetRegistry.RefreshFromScene();
                for (int i = 0; i < EnemyTargetRegistry.Count; i++)
                {
                    PlayerHealth player = EnemyTargetRegistry.GetTarget(i);
                    if (vision.CanSeeTarget(player, out Vector3 position))
                        RecordPlayerEvidence(player, position);
                }
            }

            enemyPerception.Hearing?.ScanMovingPlayers();
            investigationMemory.ObserveRoomChanges(Time.time, roomChangeConfirmationTime,
                playerEvidenceLifetime, gameObject.scene);
        }

        if (enemyPerception.Hearing == null)
            return;

        if (!CanTriggerAlert)
        {
            if (CurrentState == EnemyState.Chase || CurrentState == EnemyState.Attack
                || CurrentState == EnemyState.Alert
                || enemyPerception.Hearing.PendingNoise.Source == null
                || enemyPerception.Hearing.PendingNoise.Source.GetComponentInParent<PlayerHealth>() == null)
                enemyPerception.TryConsumeHeardNoise(out _);
            return;
        }

        if (CurrentState == EnemyState.Chase || CurrentState == EnemyState.Attack)
            enemyPerception.Hearing.ClearPendingPlayerMovementNoise();
        else if (!useLastSeenSearchFlow && !enemyPerception.HasVisibleTarget)
            enemyPerception.Hearing.ScanMovingPlayers();
    }

    private void OnPlayerHeard(PlayerHealth player, Vector3 position)
    {
        if (!useLastSeenSearchFlow || !simulationEnabled
            || (enemySetup != null && !enemySetup.HasAuthority))
            return;

        RecordPlayerEvidence(player, position);
        if (player == currentTarget
            && (CurrentState == EnemyState.Chase || CurrentState == EnemyState.Attack))
        {
            lastCurrentTargetHearingTime = Time.time;
            RegisterCombatAwareness(position);
        }
    }

    private void RecordPlayerEvidence(PlayerHealth player, Vector3 position)
    {
        if (useLastSeenSearchFlow && (enemySetup == null || enemySetup.HasAuthority))
            investigationMemory.Record(player, position, Time.time, gameObject.scene);
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

    private int CountResolvedPatrolPoints()
    {
        if (ShouldUseGlobalPatrol())
            return 1;

        int configuredPatrolPointCount = CountValidPatrolPoints();
        if (configuredPatrolPointCount > 0)
            return configuredPatrolPointCount;

        return ShouldUseGeneratedHomePatrol() ? Mathf.Max(2, generatedHomePatrolPointCount) : 0;
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

    private bool ShouldUseGeneratedHomePatrol()
    {
        return CountValidPatrolPoints() == 0
            && patrolMode != EnemyPatrolMode.GlobalWander
            && useGeneratedHomePatrol
            && generatedHomePatrolRadius > 0f
            && generatedHomePatrolPointCount >= 2;
    }

    private bool ShouldUseGlobalPatrol()
    {
        return patrolMode == EnemyPatrolMode.GlobalWander
            && globalPatrolRadius > 0f;
    }

    private int ResolveNextGeneratedPatrolIndex(int currentIndex, int count)
    {
        if (patrolMode == EnemyPatrolMode.Random && count > 1)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int candidateIndex = UnityEngine.Random.Range(0, count);
                if (candidateIndex != currentIndex)
                    return candidateIndex;
            }
        }

        return (currentIndex + 1 + count) % count;
    }

    private bool TryGetGeneratedPatrolPointPosition(int index, out Vector3 position)
    {
        int count = Mathf.Max(2, generatedHomePatrolPointCount);
        if (index < 0 || index >= count)
        {
            position = Vector3.zero;
            return false;
        }

        float angle = 360f * index / count;
        Vector3 direction = homeRotation * (Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward);
        Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (planarDirection.sqrMagnitude <= 0.0001f)
            planarDirection = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;

        position = homePosition + planarDirection.normalized * generatedHomePatrolRadius;
        return true;
    }

    private bool TryGetGlobalPatrolPointPosition(out Vector3 position)
    {
        position = Vector3.zero;

        float radius = Mathf.Max(0f, globalPatrolRadius);
        if (radius <= 0f)
            return false;

        Vector3 center = globalPatrolCenter != null ? globalPatrolCenter.position : homePosition;
        float minimumDistance = Mathf.Min(Mathf.Max(0f, globalPatrolMinimumDistance), radius);
        float sampleDistance = Mathf.Max(0.1f, globalPatrolSampleDistance);
        int attempts = Mathf.Max(1, globalPatrolSampleAttempts);
        bool hasFallback = false;
        Vector3 fallback = center;
        bool requireNavigationSample = enemyNavigation != null && enemyNavigation.HasUsableAgent;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector3 candidate = center + ResolveRandomPlanarOffset(minimumDistance, radius);
            bool sampledNavigation = TrySampleNavigationPosition(candidate, sampleDistance, out Vector3 sampledPosition);
            if (sampledNavigation)
                candidate = sampledPosition;
            else if (requireNavigationSample)
                continue;

            if (requireNavigationSample && !enemyNavigation.IsReachable(candidate))
                continue;

            if (!hasFallback)
            {
                fallback = candidate;
                hasFallback = true;
            }

            if (minimumDistance <= 0f || IsFarEnoughFromCurrentPosition(candidate, minimumDistance))
            {
                position = candidate;
                return true;
            }
        }

        if (hasFallback)
        {
            position = fallback;
            return true;
        }

        return false;
    }

    private Vector3 ResolveRandomPlanarOffset(float minimumDistance, float radius)
    {
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float distance = radius > minimumDistance
            ? UnityEngine.Random.Range(minimumDistance, radius)
            : radius;

        return new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
    }

    private bool IsFarEnoughFromCurrentPosition(Vector3 position, float minimumDistance)
    {
        Vector3 planarOffset = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
        return planarOffset.sqrMagnitude >= minimumDistance * minimumDistance;
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
