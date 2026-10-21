using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemySetup))]
[AddComponentMenu("Enemy/Traversal/Enemy Traversal Controller")]
public sealed class EnemyTraversalController : MonoBehaviour
{
    private enum DoorPreparationState
    {
        None,
        WaitingForExistingMotion,
        WaitingClosedForReopen,
        WaitingOpen
    }

    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyNavigation enemyNavigation;
    [SerializeField] private EnemyMotor enemyMotor;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CapsuleCollider capsule;
    [SerializeField] private Animator animator;

    [Header("Traversal")]
    [SerializeField] private bool enableTraversal = true;
    [SerializeField] [Min(0f)] private float defaultTraversalSpeed = 3f;
    [SerializeField] [Min(0.01f)] private float pointReachDistance = 0.18f;
    [SerializeField] private bool preserveCurrentHeight = true;
    [SerializeField] [Min(0f)] private float turnSpeed = 10f;
    [SerializeField] [Min(0f)] private float timeBetweenTraversals = 1.5f;
    [SerializeField] [Min(0f)] private float routeStallTimeout = 2.5f;

    [Header("Door Interaction")]
    [SerializeField] private bool canOpenDoors;
    [SerializeField] private bool closeAndReopenMisdirectedOpenDoors = true;

    [Header("Collider Tuning")]
    [SerializeField] private bool useTemporaryCapsuleRadius = true;
    [SerializeField] [Range(0.1f, 1f)] private float traversalCapsuleRadiusMultiplier = 0.68f;
    [SerializeField] [Min(0.01f)] private float minimumTraversalCapsuleRadius = 0.2f;

    [Header("Animation")]
    [SerializeField] private string traversalTriggerName = "Traversal";

    [Header("Debug")]
    [SerializeField] private bool drawTraversalGizmos = true;
    [SerializeField] private Color activeRouteColor = new Color(1f, 0.2f, 0.85f, 0.9f);

    private readonly List<Vector3> activeRoute = new List<Vector3>(6);
    private readonly List<BoxCollider> temporarilyDisabledBodyColliders = new List<BoxCollider>(2);
    private readonly List<Collider> ignoredDoorColliders = new List<Collider>(4);
    private readonly HashSet<EnemyTraversalVolume> overlappingVolumes = new HashSet<EnemyTraversalVolume>();
    private EnemyTraversalVolume activeVolume;
    private EnemyTraversalVolume blockedVolumeUntilExit;
    private EnemyTraversalVolume cooldownVolume;
    private EnemyTraversalSide activeExitSide;
    private EnemyTraversalSide blockedExitSide;
    private DoorPreparationState doorPreparationState;
    private DoorController activeDoor;
    private DoorController traversalDoor;
    private int activeRouteIndex;
    private bool traversing;
    private bool waitingForExitPause;
    private float waitUntil;
    private float nextTraversalAllowedTime;
    private float cooldownVolumeBlockedUntil;
    private float doorPreparationDeadline;
    private Vector3 activeDoorInteractorPosition;
    private Vector3 traversalEntryPosition;
    private float lastRouteProgressDistance;
    private float lastRouteProgressTime;
    private int lastRouteProgressIndex;
    private float originalCapsuleRadius;
    private bool hasOriginalCapsuleRadius;

    public bool IsTraversing => traversing;
    public bool CanOpenDoors => canOpenDoors;

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
        CancelTraversal();
        overlappingVolumes.Clear();
    }

    private void FixedUpdate()
    {
        if (!traversing)
        {
            TryReleaseBlockedVolume();
            TryStartOverlappingTraversal();
            return;
        }

        ResolveReferences();
        if (!CanContinueTraversal())
        {
            CancelTraversal();
            return;
        }

        KeepTemporaryBodyCollisionShape();

        if (!UpdateDoorPreparation())
        {
            if (traversing)
                StopMotion();

            return;
        }

        if (doorPreparationState == DoorPreparationState.None && traversalDoor != null
            && (!traversalDoor.IsOpen || traversalDoor.IsMoving))
        {
            if (!canOpenDoors || traversalDoor.IsLocked)
            {
                CancelTraversal();
                return;
            }

            BeginDoorPreparation(activeVolume, traversalEntryPosition);
            StopMotion();
            return;
        }

        if (waitingForExitPause)
        {
            StopMotion();
            if (Time.time >= waitUntil)
                CompleteTraversal();

            return;
        }

        if (Time.time < waitUntil)
        {
            StopMotion();
            return;
        }

        MoveAlongRoute(Time.fixedDeltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryStartTraversal(RegisterOverlappingVolume(other));
    }

    private void OnTriggerStay(Collider other)
    {
        TryStartTraversal(RegisterOverlappingVolume(other));
    }

    private void OnTriggerExit(Collider other)
    {
        EnemyTraversalVolume traversalVolume = ResolveVolume(other);
        if (traversalVolume == null)
            return;

        overlappingVolumes.Remove(traversalVolume);
        TryReleaseBlockedVolume();
    }

    private bool TryStartTraversal(EnemyTraversalVolume traversalVolume)
    {
        if (traversalVolume == null)
            return false;

        Vector3 bodyPosition = ResolveBodyPosition();
        bool forceClosedDoorRetry = ShouldForceClosedDoorTraversalRetry(traversalVolume, bodyPosition);

        if (!CanStartTraversal(forceClosedDoorRetry))
            return false;

        if (!forceClosedDoorRetry && IsVolumeBlocked(traversalVolume))
            return false;

        if (!ShouldStartForNavigationDestination(traversalVolume, bodyPosition))
            return false;

        EnemyTraversalSide entrySide = traversalVolume.ResolveSide(bodyPosition);
        if (!traversalVolume.TryBuildRoute(entrySide, bodyPosition, activeRoute, out EnemyTraversalSide exitSide))
            return false;

        if (!CanPrepareDoorForTraversal(traversalVolume, bodyPosition))
        {
            activeRoute.Clear();
            return false;
        }

        if (forceClosedDoorRetry)
            ClearTraversalCooldown(traversalVolume);

        BeginTraversal(traversalVolume, exitSide);
        return true;
    }

    private void BeginTraversal(EnemyTraversalVolume traversalVolume, EnemyTraversalSide exitSide)
    {
        activeVolume = traversalVolume;
        blockedVolumeUntilExit = traversalVolume;
        activeExitSide = exitSide;
        traversing = true;
        waitingForExitPause = false;
        activeRouteIndex = 0;
        waitUntil = Time.time + traversalVolume.EntryPauseDuration;
        traversalEntryPosition = ResolveBodyPosition();
        traversalDoor = TryResolveTraversalDoor(traversalVolume, out DoorController door) ? door : null;
        ResetRouteProgress();

        enemyBrain?.BeginTraversal();
        StopMotion();
        ApplyTemporaryCapsuleRadius();
        ApplyTemporaryBodyCollisionShape();
        BeginDoorPreparation(traversalVolume, ResolveBodyPosition());
        TriggerTraversalAnimation();
    }

    private void MoveAlongRoute(float deltaTime)
    {
        if (activeRouteIndex < 0 || activeRouteIndex >= activeRoute.Count)
        {
            CompleteTraversal();
            return;
        }

        Vector3 bodyPosition = ResolveBodyPosition();
        Vector3 target = ResolveTargetPosition(activeRoute[activeRouteIndex], bodyPosition);
        Vector3 planarOffset = Vector3.ProjectOnPlane(target - bodyPosition, Vector3.up);
        float reachDistance = Mathf.Max(0.01f, pointReachDistance);
        if (planarOffset.sqrMagnitude <= reachDistance * reachDistance)
        {
            activeRouteIndex++;
            ResetRouteProgress();
            if (activeRouteIndex >= activeRoute.Count)
                StartExitPauseOrComplete();

            return;
        }

        if (HasRouteStalled(planarOffset.magnitude))
        {
            Debug.LogWarning($"[EnemyTraversal] '{name}' parou no ponto {activeRouteIndex + 1} de "
                + $"'{activeVolume.name}' em {bodyPosition}. Verifique a largura da passagem e os pontos Side A/B.", this);
            CancelTraversal();
            return;
        }

        float speed = ResolveTraversalSpeed();
        Vector3 nextPosition = Vector3.MoveTowards(bodyPosition, target, speed * deltaTime);
        MoveBody(nextPosition);
        RotateTowards(planarOffset, deltaTime);
        enemyMotor?.ApplyExternalPlanarSpeed(speed);
    }

    private void StartExitPauseOrComplete()
    {
        float exitPause = activeVolume != null ? activeVolume.ExitPauseDuration : 0f;
        if (exitPause <= 0f)
        {
            CompleteTraversal();
            return;
        }

        waitingForExitPause = true;
        waitUntil = Time.time + exitPause;
        StopMotion();
    }

    private void CompleteTraversal()
    {
        RestoreDoorCollision();
        RestoreBodyCollisionShape();
        RestoreCapsuleRadius();
        EnemyTraversalVolume completedVolume = activeVolume;
        EnemyTraversalSide completedExitSide = activeExitSide;
        traversing = false;
        waitingForExitPause = false;
        activeVolume = null;
        traversalDoor = null;
        activeExitSide = EnemyTraversalSide.None;
        activeRoute.Clear();
        activeRouteIndex = 0;
        ClearDoorPreparation();
        StopMotion();
        enemyBrain?.EndTraversal();
        BlockCompletedVolume(completedVolume, completedExitSide);
    }

    private void CancelTraversal()
    {
        if (!traversing)
            return;

        RestoreDoorCollision();
        RestoreBodyCollisionShape();
        RestoreCapsuleRadius();
        EnemyTraversalVolume cancelledVolume = activeVolume;
        bool requireExitBeforeRetry = cancelledVolume != null
            && activeExitSide != EnemyTraversalSide.None
            && cancelledVolume.ResolveSide(ResolveBodyPosition()) == activeExitSide;
        traversing = false;
        waitingForExitPause = false;
        activeVolume = null;
        traversalDoor = null;
        activeExitSide = EnemyTraversalSide.None;
        activeRoute.Clear();
        activeRouteIndex = 0;
        ClearDoorPreparation();
        StopMotion();
        enemyBrain?.EndTraversal();
        BlockCancelledVolume(cancelledVolume, requireExitBeforeRetry);
    }

    private bool CanStartTraversal(bool ignoreTraversalCooldown = false)
    {
        if (!enableTraversal || traversing)
            return false;

        if (!ignoreTraversalCooldown && Time.time < nextTraversalAllowedTime)
            return false;

        ResolveReferences();
        if (enemySetup != null && !enemySetup.HasAuthority)
            return false;

        EnemyHealth enemyHealth = enemySetup != null ? enemySetup.EnemyHealth : GetComponent<EnemyHealth>();
        if (enemyHealth != null && !enemyHealth.IsAlive)
            return false;

        return enemyBrain == null || (!enemyBrain.IsStunned && !enemyBrain.IsTraversing);
    }

    private bool IsVolumeBlocked(EnemyTraversalVolume traversalVolume)
    {
        if (traversalVolume == null)
            return true;

        TryReleaseBlockedVolume();

        if (traversalVolume == blockedVolumeUntilExit)
            return true;

        if (traversalVolume != cooldownVolume)
            return false;

        if (Time.time < cooldownVolumeBlockedUntil)
            return true;

        cooldownVolume = null;
        return false;
    }

    private bool ShouldStartForNavigationDestination(EnemyTraversalVolume traversalVolume, Vector3 bodyPosition)
    {
        if (traversalVolume == null || !traversalVolume.RequireDestinationAcrossSides)
            return true;

        if (enemyNavigation == null || !enemyNavigation.HasDestination)
            return false;

        return traversalVolume.IsDestinationAcrossSides(bodyPosition, enemyNavigation.CurrentDestination);
    }

    private void TryStartOverlappingTraversal()
    {
        foreach (EnemyTraversalVolume traversalVolume in overlappingVolumes)
        {
            if (traversalVolume == null)
                continue;

            if (TryStartTraversal(traversalVolume))
                return;
        }

        Vector3 bodyPosition = ResolveBodyPosition();
        float approachDistance = capsule != null ? capsule.radius + 0.2f : 0.8f;
        foreach (EnemyTraversalVolume traversalVolume in EnemyTraversalVolume.ActiveVolumes)
        {
            if (traversalVolume == null || overlappingVolumes.Contains(traversalVolume)
                || traversalVolume.gameObject.scene != gameObject.scene
                || !traversalVolume.IsWithinApproachDistance(bodyPosition, approachDistance))
                continue;

            if (TryStartTraversal(traversalVolume))
                return;
        }
    }

    private bool CanPrepareDoorForTraversal(EnemyTraversalVolume traversalVolume, Vector3 bodyPosition)
    {
        if (!TryResolveTraversalDoor(traversalVolume, out DoorController doorController))
            return true;

        if (!doorController.IsOpen)
        {
            return canOpenDoors
                && !doorController.IsLocked
                && ShouldTreatClosedDoorAsBlocking(traversalVolume, doorController, bodyPosition);
        }

        if (ShouldReorientDoorForTraversal(traversalVolume, doorController, bodyPosition))
            return canOpenDoors && !doorController.IsLocked;

        return true;
    }

    private void BeginDoorPreparation(EnemyTraversalVolume traversalVolume, Vector3 bodyPosition)
    {
        ClearDoorPreparation();

        if (!TryResolveTraversalDoor(traversalVolume, out DoorController doorController))
            return;

        activeDoor = doorController;
        activeDoorInteractorPosition = bodyPosition;

        float timeout = traversalVolume != null ? traversalVolume.DoorReadyTimeout : 0f;
        doorPreparationDeadline = timeout > 0f ? Time.time + timeout : float.PositiveInfinity;

        if (doorController.IsMoving)
        {
            doorPreparationState = DoorPreparationState.WaitingForExistingMotion;
            return;
        }

        if (ShouldReorientDoorForTraversal(traversalVolume, doorController, bodyPosition))
        {
            doorController.RequestClose();
            doorPreparationState = DoorPreparationState.WaitingClosedForReopen;
            return;
        }

        if (!doorController.IsOpen)
        {
            if (!ShouldTreatClosedDoorAsBlocking(traversalVolume, doorController, bodyPosition))
            {
                CompleteDoorPreparation();
                return;
            }

            RequestDoorOpenForTraversal(doorController);
            doorPreparationState = DoorPreparationState.WaitingOpen;
            return;
        }

        CompleteDoorPreparation();
    }

    private bool UpdateDoorPreparation()
    {
        if (doorPreparationState == DoorPreparationState.None)
            return true;

        if (activeDoor == null)
        {
            ClearDoorPreparation();
            return true;
        }

        if (Time.time > doorPreparationDeadline)
        {
            CancelTraversal();
            return false;
        }

        switch (doorPreparationState)
        {
            case DoorPreparationState.WaitingForExistingMotion:
                return UpdateWaitingForExistingDoorMotion();
            case DoorPreparationState.WaitingClosedForReopen:
                return UpdateWaitingForDoorClose();
            case DoorPreparationState.WaitingOpen:
                return UpdateWaitingForDoorOpen();
            default:
                return true;
        }
    }

    private bool UpdateWaitingForExistingDoorMotion()
    {
        if (activeDoor == null || activeDoor.IsMoving)
            return false;

        if (ShouldReorientDoorForTraversal(activeVolume, activeDoor, activeDoorInteractorPosition))
        {
            if (!canOpenDoors || activeDoor.IsLocked)
            {
                CancelTraversal();
                return false;
            }

            activeDoor.RequestClose();
            doorPreparationState = DoorPreparationState.WaitingClosedForReopen;
            return false;
        }

        if (!activeDoor.IsOpen)
        {
            if (!ShouldTreatClosedDoorAsBlocking(activeVolume, activeDoor, activeDoorInteractorPosition))
            {
                CompleteDoorPreparation();
                return true;
            }

            if (!canOpenDoors || activeDoor.IsLocked)
            {
                CancelTraversal();
                return false;
            }

            RequestDoorOpenForTraversal(activeDoor);
            doorPreparationState = DoorPreparationState.WaitingOpen;
            return false;
        }

        CompleteDoorPreparation();
        return true;
    }

    private bool UpdateWaitingForDoorClose()
    {
        if (activeDoor == null || activeDoor.IsMoving || activeDoor.IsOpen)
            return false;

        if (!RequestDoorOpenForTraversal(activeDoor) && !activeDoor.IsOpen)
        {
            CancelTraversal();
            return false;
        }

        doorPreparationState = DoorPreparationState.WaitingOpen;
        return false;
    }

    private bool UpdateWaitingForDoorOpen()
    {
        if (activeDoor == null || activeDoor.IsMoving || !activeDoor.IsOpen)
            return false;

        CompleteDoorPreparation();
        return true;
    }

    private bool RequestDoorOpenForTraversal(DoorController doorController)
    {
        if (doorController == null)
            return false;

        return doorController.RequestOpenByEnemy(activeDoorInteractorPosition);
    }

    private bool ShouldReorientDoorForTraversal(
        EnemyTraversalVolume traversalVolume,
        DoorController doorController,
        Vector3 bodyPosition)
    {
        if (!closeAndReopenMisdirectedOpenDoors
            || traversalVolume == null
            || doorController == null)
        {
            return false;
        }

        if (!traversalVolume.ReorientOpenDoorWhenNeeded
            || !doorController.IsOpen
            || !doorController.WouldChangeOpenDirectionFromWorldPosition(bodyPosition))
        {
            return false;
        }

        return !traversalVolume.ReorientOnlyWhenDoorObstructsTraversal
            || traversalVolume.IsDoorObstructingTraversalRoute(
                doorController, bodyPosition, activeRoute, ResolveDoorProbeRadius(traversalVolume))
            || IsDoorContactObstructingTraversal(doorController);
    }

    private bool IsDoorObstructingTraversalRoute(EnemyTraversalVolume traversalVolume, DoorController doorController)
    {
        if (doorController == null)
            return false;

        if (!doorController.IsOpen && IsClosedDoorContactBlockingTraversal(doorController))
            return true;

        if (traversalVolume != null && traversalVolume.IsDoorObstructingTraversalRoute(
                doorController, ResolveBodyPosition(), activeRoute, ResolveDoorProbeRadius(traversalVolume)))
            return true;

        return IsDoorContactObstructingTraversal(doorController);
    }

    private bool ShouldTreatClosedDoorAsBlocking(
        EnemyTraversalVolume traversalVolume,
        DoorController doorController,
        Vector3 bodyPosition)
    {
        if (doorController == null || doorController.IsOpen)
            return false;

        if (traversalVolume != null
            && enemyNavigation != null
            && enemyNavigation.HasDestination
            && traversalVolume.IsDestinationAcrossSides(bodyPosition, enemyNavigation.CurrentDestination))
            return true;

        return IsDoorObstructingTraversalRoute(traversalVolume, doorController)
            || ShouldForceClosedDoorTraversalRetry(traversalVolume, bodyPosition);
    }

    private bool ShouldForceClosedDoorTraversalRetry(EnemyTraversalVolume traversalVolume, Vector3 bodyPosition)
    {
        if (!canOpenDoors
            || traversalVolume == null
            || !traversalVolume.ContainsPoint(bodyPosition)
            || enemyNavigation == null
            || !enemyNavigation.HasDestination
            || !traversalVolume.IsDestinationAcrossSides(bodyPosition, enemyNavigation.CurrentDestination))
            return false;

        bool retryBeforeCrossing = blockedVolumeUntilExit != traversalVolume
            && cooldownVolume == traversalVolume
            && Time.time < cooldownVolumeBlockedUntil;
        bool reverseCompletedCrossing = blockedVolumeUntilExit == traversalVolume
            && blockedExitSide != EnemyTraversalSide.None
            && traversalVolume.ResolveSide(bodyPosition) == blockedExitSide
            && traversalVolume.ResolveSide(enemyNavigation.CurrentDestination) != blockedExitSide;
        if (!retryBeforeCrossing && !reverseCompletedCrossing)
            return false;

        if (!TryResolveTraversalDoor(traversalVolume, out DoorController doorController))
            return false;

        return doorController != null
            && !doorController.IsOpen
            && !doorController.IsMoving
            && !doorController.IsLocked;
    }

    private bool IsClosedDoorContactBlockingTraversal(DoorController doorController)
    {
        if (doorController == null)
            return false;

        DoorEnemyObstructionSensor[] sensors = doorController.GetComponentsInChildren<DoorEnemyObstructionSensor>(true);
        for (int i = 0; i < sensors.Length; i++)
        {
            DoorEnemyObstructionSensor sensor = sensors[i];
            if (sensor != null
                && sensor.isActiveAndEnabled
                && sensor.HasPersistentClosedDoorContact(this))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsDoorContactObstructingTraversal(DoorController doorController)
    {
        if (doorController == null)
            return false;

        DoorEnemyObstructionSensor[] sensors = doorController.GetComponentsInChildren<DoorEnemyObstructionSensor>(true);
        for (int i = 0; i < sensors.Length; i++)
        {
            DoorEnemyObstructionSensor sensor = sensors[i];
            if (sensor != null
                && sensor.isActiveAndEnabled
                && sensor.HasPersistentObstructingContact(this))
            {
                return true;
            }
        }

        return false;
    }

    private void CompleteDoorPreparation()
    {
        IgnoreOpenDoorCollision();
        ResetRouteProgress();
        float settleDelay = activeVolume != null ? activeVolume.DoorSettleDelay : 0f;
        if (settleDelay > 0f)
            waitUntil = Mathf.Max(waitUntil, Time.time + settleDelay);

        ClearDoorPreparation();
    }

    private void ClearDoorPreparation()
    {
        doorPreparationState = DoorPreparationState.None;
        activeDoor = null;
        activeDoorInteractorPosition = Vector3.zero;
        doorPreparationDeadline = float.PositiveInfinity;
    }

    private static bool TryResolveTraversalDoor(EnemyTraversalVolume traversalVolume, out DoorController doorController)
    {
        doorController = null;
        if (traversalVolume == null || !traversalVolume.GateTraversalByDoorState)
            return false;

        doorController = traversalVolume.DoorController;
        return doorController != null;
    }

    private void TryReleaseBlockedVolume()
    {
        if (blockedVolumeUntilExit == null)
            return;

        if (Time.time < cooldownVolumeBlockedUntil)
            return;

        Vector3 bodyPosition = ResolveBodyPosition();
        bool canRelease = blockedExitSide != EnemyTraversalSide.None
            ? blockedVolumeUntilExit.HasPassedSide(bodyPosition, blockedExitSide)
            : !blockedVolumeUntilExit.ContainsPoint(bodyPosition);

        if (!canRelease)
            return;

        if (cooldownVolume == blockedVolumeUntilExit)
            cooldownVolume = null;

        blockedVolumeUntilExit = null;
        blockedExitSide = EnemyTraversalSide.None;
    }

    private void ClearTraversalCooldown(EnemyTraversalVolume traversalVolume)
    {
        if (traversalVolume == null || cooldownVolume != traversalVolume)
            return;

        cooldownVolume = null;
        cooldownVolumeBlockedUntil = float.NegativeInfinity;
        nextTraversalAllowedTime = Mathf.Min(nextTraversalAllowedTime, Time.time);
    }

    private void BlockCompletedVolume(EnemyTraversalVolume traversalVolume, EnemyTraversalSide exitSide)
    {
        if (traversalVolume == null)
            return;

        ArmTraversalCooldown(traversalVolume);
        blockedVolumeUntilExit = traversalVolume;
        blockedExitSide = exitSide;
    }

    private void BlockCancelledVolume(EnemyTraversalVolume traversalVolume, bool requireExitBeforeRetry)
    {
        if (traversalVolume == null)
            return;

        ArmTraversalCooldown(traversalVolume);

        if (!requireExitBeforeRetry)
        {
            if (blockedVolumeUntilExit == traversalVolume)
            {
                blockedVolumeUntilExit = null;
                blockedExitSide = EnemyTraversalSide.None;
            }

            return;
        }

        blockedVolumeUntilExit = traversalVolume;
        blockedExitSide = EnemyTraversalSide.None;
    }

    private void ArmTraversalCooldown(EnemyTraversalVolume traversalVolume)
    {
        float cooldown = Mathf.Max(timeBetweenTraversals, traversalVolume.CooldownAfterTraversal);
        nextTraversalAllowedTime = Time.time + cooldown;
        cooldownVolume = traversalVolume;
        cooldownVolumeBlockedUntil = nextTraversalAllowedTime;
    }

    private bool CanContinueTraversal()
    {
        if (enemySetup != null && !enemySetup.HasAuthority)
            return false;

        EnemyHealth enemyHealth = enemySetup != null ? enemySetup.EnemyHealth : GetComponent<EnemyHealth>();
        if (enemyHealth != null && !enemyHealth.IsAlive)
            return false;

        return enemyBrain == null || !enemyBrain.IsStunned;
    }

    private float ResolveTraversalSpeed()
    {
        float volumeSpeed = activeVolume != null ? activeVolume.TraversalSpeed : 0f;
        return Mathf.Max(0.01f, volumeSpeed > 0f ? volumeSpeed : defaultTraversalSpeed);
    }

    private float ResolveDoorProbeRadius(EnemyTraversalVolume traversalVolume)
    {
        float configuredRadius = traversalVolume.DoorObstructionProbeRadius;
        if (capsule == null)
            return configuredRadius;

        float originalRadius = hasOriginalCapsuleRadius ? originalCapsuleRadius : capsule.radius;
        float traversalRadius = useTemporaryCapsuleRadius
            ? Mathf.Min(originalRadius, Mathf.Max(minimumTraversalCapsuleRadius,
                originalRadius * traversalCapsuleRadiusMultiplier))
            : originalRadius;
        return Mathf.Min(configuredRadius, traversalRadius + 0.05f);
    }

    private Vector3 ResolveTargetPosition(Vector3 routePoint, Vector3 bodyPosition)
    {
        if (!preserveCurrentHeight)
            return routePoint;

        routePoint.y = bodyPosition.y;
        return routePoint;
    }

    private void MoveBody(Vector3 nextPosition)
    {
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.MovePosition(nextPosition);
            return;
        }

        transform.position = nextPosition;
    }

    private void RotateTowards(Vector3 planarOffset, float deltaTime)
    {
        if (activeVolume != null && !activeVolume.AlignToPathDirection)
            return;

        if (planarOffset.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(planarOffset.normalized, Vector3.up);
        float rotationT = turnSpeed <= 0f ? 1f : 1f - Mathf.Exp(-turnSpeed * deltaTime);
        Quaternion nextRotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationT);

        if (rb != null)
            rb.MoveRotation(nextRotation);
        else
            transform.rotation = nextRotation;
    }

    private void StopMotion()
    {
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        enemyMotor?.ApplyExternalPlanarSpeed(0f);
    }

    private void ApplyTemporaryCapsuleRadius()
    {
        if (!useTemporaryCapsuleRadius || capsule == null || hasOriginalCapsuleRadius)
            return;

        originalCapsuleRadius = capsule.radius;
        hasOriginalCapsuleRadius = true;
        float targetRadius = Mathf.Max(
            minimumTraversalCapsuleRadius,
            originalCapsuleRadius * Mathf.Clamp(traversalCapsuleRadiusMultiplier, 0.1f, 1f));
        capsule.radius = Mathf.Min(originalCapsuleRadius, targetRadius);
    }

    private void ApplyTemporaryBodyCollisionShape()
    {
        if (capsule == null || !capsule.enabled)
            return;

        BoxCollider[] bodyColliders = GetComponents<BoxCollider>();
        for (int i = 0; i < bodyColliders.Length; i++)
        {
            BoxCollider bodyCollider = bodyColliders[i];
            if (bodyCollider == null || !bodyCollider.enabled || bodyCollider.isTrigger)
                continue;

            temporarilyDisabledBodyColliders.Add(bodyCollider);
            bodyCollider.enabled = false;
        }
    }

    private void KeepTemporaryBodyCollisionShape()
    {
        for (int i = 0; i < temporarilyDisabledBodyColliders.Count; i++)
        {
            BoxCollider bodyCollider = temporarilyDisabledBodyColliders[i];
            if (bodyCollider != null && bodyCollider.enabled)
                bodyCollider.enabled = false;
        }
    }

    private void RestoreBodyCollisionShape()
    {
        bool isAlive = enemySetup == null || enemySetup.EnemyHealth == null || enemySetup.EnemyHealth.IsAlive;
        for (int i = 0; i < temporarilyDisabledBodyColliders.Count; i++)
        {
            BoxCollider bodyCollider = temporarilyDisabledBodyColliders[i];
            if (bodyCollider != null && isAlive)
                bodyCollider.enabled = true;
        }

        temporarilyDisabledBodyColliders.Clear();
    }

    private void IgnoreOpenDoorCollision()
    {
        if (capsule == null || !capsule.enabled || activeDoor == null)
            return;

        Transform colliderRoot = activeDoor.MovingPart != null ? activeDoor.MovingPart : activeDoor.transform;
        Collider[] doorColliders = colliderRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < doorColliders.Length; i++)
        {
            Collider doorCollider = doorColliders[i];
            if (doorCollider == null || !doorCollider.enabled || doorCollider.isTrigger
                || ignoredDoorColliders.Contains(doorCollider))
                continue;

            Physics.IgnoreCollision(capsule, doorCollider, true);
            ignoredDoorColliders.Add(doorCollider);
        }
    }

    private void RestoreDoorCollision()
    {
        if (capsule != null)
        {
            for (int i = 0; i < ignoredDoorColliders.Count; i++)
            {
                Collider doorCollider = ignoredDoorColliders[i];
                if (doorCollider != null)
                    Physics.IgnoreCollision(capsule, doorCollider, false);
            }
        }

        ignoredDoorColliders.Clear();
    }

    private void ResetRouteProgress()
    {
        lastRouteProgressDistance = activeRouteIndex < activeRoute.Count
            ? Vector3.ProjectOnPlane(activeRoute[activeRouteIndex] - ResolveBodyPosition(), Vector3.up).magnitude
            : 0f;
        lastRouteProgressIndex = activeRouteIndex;
        lastRouteProgressTime = Time.time;
    }

    private bool HasRouteStalled(float distanceToPoint)
    {
        if (routeStallTimeout <= 0f)
            return false;

        if (lastRouteProgressIndex != activeRouteIndex
            || lastRouteProgressDistance - distanceToPoint >= 0.08f)
        {
            ResetRouteProgress();
            return false;
        }

        return Time.time - lastRouteProgressTime >= routeStallTimeout;
    }

    private void RestoreCapsuleRadius()
    {
        if (!hasOriginalCapsuleRadius || capsule == null)
            return;

        capsule.radius = originalCapsuleRadius;
        hasOriginalCapsuleRadius = false;
    }

    private void TriggerTraversalAnimation()
    {
        if (animator == null || string.IsNullOrWhiteSpace(traversalTriggerName))
            return;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Trigger
                && parameter.name == traversalTriggerName)
            {
                animator.ResetTrigger(traversalTriggerName);
                animator.SetTrigger(traversalTriggerName);
                return;
            }
        }
    }

    private Vector3 ResolveBodyPosition()
    {
        return rb != null ? rb.position : transform.position;
    }

    private static EnemyTraversalVolume ResolveVolume(Collider triggerCollider)
    {
        return triggerCollider != null ? triggerCollider.GetComponentInParent<EnemyTraversalVolume>() : null;
    }

    private EnemyTraversalVolume RegisterOverlappingVolume(Collider triggerCollider)
    {
        EnemyTraversalVolume traversalVolume = ResolveVolume(triggerCollider);
        if (traversalVolume != null)
            overlappingVolumes.Add(traversalVolume);

        return traversalVolume;
    }

    private void ResolveReferences()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyBrain == null)
            enemyBrain = enemySetup != null ? enemySetup.EnemyBrain : GetComponent<EnemyBrain>();

        if (enemyNavigation == null)
            enemyNavigation = enemySetup != null ? enemySetup.EnemyNavigation : GetComponent<EnemyNavigation>();

        if (enemyMotor == null)
            enemyMotor = enemySetup != null ? enemySetup.EnemyMotor : GetComponent<EnemyMotor>();

        if (rb == null)
            rb = enemySetup != null ? enemySetup.Rigidbody : GetComponent<Rigidbody>();

        if (capsule == null)
            capsule = enemySetup != null ? enemySetup.Capsule : GetComponent<CapsuleCollider>();

        if (animator == null)
            animator = enemySetup != null ? enemySetup.Animator : GetComponentInChildren<Animator>(true);
    }

    private void OnValidate()
    {
        defaultTraversalSpeed = Mathf.Max(0f, defaultTraversalSpeed);
        pointReachDistance = Mathf.Max(0.01f, pointReachDistance);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        timeBetweenTraversals = Mathf.Max(0f, timeBetweenTraversals);
        routeStallTimeout = Mathf.Max(0f, routeStallTimeout);
        traversalCapsuleRadiusMultiplier = Mathf.Clamp(traversalCapsuleRadiusMultiplier, 0.1f, 1f);
        minimumTraversalCapsuleRadius = Mathf.Max(0.01f, minimumTraversalCapsuleRadius);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawTraversalGizmos || activeRoute == null || activeRoute.Count == 0)
            return;

        Gizmos.color = activeRouteColor;
        for (int i = 0; i < activeRoute.Count; i++)
        {
            Gizmos.DrawWireSphere(activeRoute[i], 0.16f);
            if (i > 0)
                Gizmos.DrawLine(activeRoute[i - 1], activeRoute[i]);
        }
    }
}
