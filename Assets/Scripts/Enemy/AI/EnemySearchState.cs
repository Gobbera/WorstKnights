using System.Collections.Generic;
using UnityEngine;

public sealed class EnemySearchState : EnemyAIState
{
    private float searchEndsAt;
    private float waitUntil;
    private float activeSearchRadius;
    private float activeSearchSpeed;
    private float activeSearchPointWaitTime;
    private int activeSearchPointCount;
    private int sampledPointCount;
    private int visitedSearchPointCount;
    private int authoredSearchPointIndex;
    private bool waitingAtSearchPoint;
    private bool arrivedAtLastSeen;
    private bool approachingDoorEntry;
    private bool windingDown;
    private bool reflectionDelayStarted;
    private float reflectionReadyAt;
    private float activePersistence;
    private float lastSeenStallElapsed;
    private Vector3 lastSeenProgressPosition;
    private EnemySearchArea activeSearchArea;
    private readonly List<EnemySearchArea> roomRoute = new List<EnemySearchArea>();
    private int roomRouteIndex;
    private BeholderDialogueController beholderDialogue;

    public EnemySearchState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Search;

    public override void Enter()
    {
        if (Brain.UseLastSeenSearchFlow)
        {
            Brain.BuildRoomSearchRoute(roomRoute);
            roomRouteIndex = 0;
            beholderDialogue = Brain.GetComponent<BeholderDialogueController>();
            beholderDialogue?.SetSearchWindingDown(false);
            activePersistence = Brain.GetComponent<BeholderMoodController>()?.SearchPersistenceMultiplier ?? 1f;
            activeSearchRadius = Brain.SearchRadius;
            activeSearchSpeed = Brain.SearchSpeed;
            activeSearchPointWaitTime = Brain.SearchPointWaitTime;
            activeSearchPointCount = Mathf.RoundToInt(Brain.SearchPointCount * activePersistence);
            activeSearchArea = roomRoute.Count > 0 ? roomRoute[0] : ResolveSearchArea();
            if (activeSearchArea != null)
                activeSearchPointCount = Mathf.Max(activeSearchPointCount, activeSearchArea.PointCount);
            searchEndsAt = float.PositiveInfinity;
        }
        else
        {
            roomRoute.Clear();
            activeSearchArea = null;
            activeSearchRadius = Brain.ResolveActiveSearchRadius();
            activeSearchSpeed = Brain.ResolveActiveSearchSpeed();
            activeSearchPointWaitTime = Brain.ResolveActiveSearchPointWaitTime();
            activeSearchPointCount = Brain.ResolveActiveSearchPointCount();
            searchEndsAt = Time.time + Brain.ResolveActiveSearchDuration();
        }

        sampledPointCount = 0;
        visitedSearchPointCount = 0;
        authoredSearchPointIndex = 0;
        waitingAtSearchPoint = false;
        arrivedAtLastSeen = false;
        approachingDoorEntry = false;
        windingDown = false;
        reflectionDelayStarted = false;
        lastSeenStallElapsed = 0f;

        if (Brain.HasLastKnownTargetPosition)
        {
            lastSeenProgressPosition = Brain.transform.position;
            EnemySearchArea occupiedArea = Brain.CurrentRoom;
            if (activeSearchArea != null && occupiedArea != null && occupiedArea != activeSearchArea
                && Brain.TryPrepareDoorBetweenAreas(occupiedArea, activeSearchArea))
            {
                Brain.SetLastKnownNavigationDestination(
                    activeSearchSpeed, Brain.SearchStoppingDistance, force: true);
            }
            else if (activeSearchArea != null && occupiedArea != null && occupiedArea != activeSearchArea)
            {
                Debug.LogWarning($"{Brain.name}: no traversal from {occupiedArea.name} to "
                    + $"{activeSearchArea.name}; skipping that search area.", Brain);
                if (!TryBeginNextRoom())
                    BeginWindDown();
            }
            else if (Brain.UseLastSeenSearchFlow && (ShouldSweepCurrentRoomBeforeLastKnown()
                || Brain.HasReachedLastKnownTargetPosition()))
                BeginLastSeenSweep();
            else
            {
                if (Brain.UseLastSeenSearchFlow)
                    Brain.SetLastKnownNavigationDestination(
                        activeSearchSpeed, Brain.SearchStoppingDistance, force: true);
                else
                    Brain.SetNavigationDestination(Brain.LastKnownTargetPosition,
                        activeSearchSpeed, Brain.SearchStoppingDistance, force: true);
            }
        }
        else
        {
            Brain.ChangeState(EnemyState.Return);
        }
    }

    public override void Tick(float deltaTime)
    {
        if (TryChaseVisibleTarget() || TryAlertFromNoise())
            return;

        if (Brain.UseLastSeenSearchFlow)
        {
            TickLastSeenSearch(deltaTime);
            return;
        }

        if (Time.time >= searchEndsAt)
        {
            Brain.ChangeState(EnemyState.Return);
            return;
        }

        if (waitingAtSearchPoint)
        {
            if (Time.time < waitUntil)
                return;

            waitingAtSearchPoint = false;
            TryMoveToNextSearchPoint();
            return;
        }

        if (!Brain.NavigationHasReachedDestination())
            return;

        waitingAtSearchPoint = true;
        waitUntil = Time.time + activeSearchPointWaitTime;
    }

    public override void Exit()
    {
        beholderDialogue?.SetSearchWindingDown(false);
    }

    private void TickLastSeenSearch(float deltaTime)
    {
        if (windingDown)
        {
            if (beholderDialogue != null && beholderDialogue.IsSearchLinePlaying)
            {
                reflectionDelayStarted = false;
                return;
            }

            if (!reflectionDelayStarted)
            {
                reflectionReadyAt = Time.time + Brain.SearchToReflectionDelay;
                reflectionDelayStarted = true;
            }

            if (Time.time >= reflectionReadyAt)
                Brain.ChangeState(EnemyState.Reflection);
            return;
        }

        if (!arrivedAtLastSeen)
        {
            if (Brain.HasDoorInvestigation && Brain.HasEnteredDoorInvestigationSide())
            {
                Brain.ClearRoomCrossing();
                if (roomRouteIndex > 0 || ShouldSweepCurrentRoomBeforeLastKnown())
                    BeginLastSeenSweep();
                else
                    Brain.SetLastKnownNavigationDestination(
                        activeSearchSpeed, Brain.SearchStoppingDistance, force: true);
                return;
            }

            if (!Brain.HasEnteredDoorInvestigationSide())
            {
                if (!approachingDoorEntry || !Brain.NavigationHasUsablePath()
                    || Brain.NavigationHasReachedDestination())
                    approachingDoorEntry = Brain.TryApproachDoorInvestigationEntry() || approachingDoorEntry;

                if (HasStalledOnWayToLastSeen(deltaTime))
                    SkipUnreachableAreaOrReflect();
                return;
            }

            if (!Brain.HasReachedLastKnownTargetPosition())
            {
                if (HasStalledOnWayToLastSeen(deltaTime))
                    SkipUnreachableAreaOrReflect();
                return;
            }

            BeginLastSeenSweep();
            return;
        }

        int requiredPoints = activeSearchArea != null ? activeSearchArea.PointCount : 0;
        if (Time.time >= searchEndsAt
            && visitedSearchPointCount >= requiredPoints)
        {
            CompleteAreaOrWindDown();
            return;
        }

        if (waitingAtSearchPoint)
        {
            if (Time.time < waitUntil)
                return;

            waitingAtSearchPoint = false;
            if (!TryMoveToNextLastSeenPoint())
                WaitForRemainingSearchTimeOrWindDown();
            return;
        }

        if (!Brain.NavigationHasUsablePath())
        {
            if (!TryMoveToNextLastSeenPoint())
                WaitForRemainingSearchTimeOrWindDown();
            return;
        }

        if (Brain.NavigationHasReachedDestination())
        {
            visitedSearchPointCount++;
            WaitAtSearchPoint();
        }
        else if (HasStalledOnWayToLastSeen(deltaTime) && !TryMoveToNextLastSeenPoint())
        {
            WaitForRemainingSearchTimeOrWindDown();
        }
    }

    private bool HasStalledOnWayToLastSeen(float deltaTime)
    {
        float timeout = Brain.LastSeenStallTimeout;
        if (timeout <= 0f)
            return false;

        Vector3 planarProgress = Vector3.ProjectOnPlane(
            Brain.transform.position - lastSeenProgressPosition, Vector3.up);
        float minimumProgress = Brain.LastSeenProgressDistance;
        if (planarProgress.sqrMagnitude >= minimumProgress * minimumProgress)
        {
            lastSeenProgressPosition = Brain.transform.position;
            lastSeenStallElapsed = 0f;
            return false;
        }

        lastSeenStallElapsed += Mathf.Max(0f, deltaTime);
        return lastSeenStallElapsed >= timeout;
    }

    private void WaitAtSearchPoint()
    {
        waitingAtSearchPoint = true;
        waitUntil = Time.time + activeSearchPointWaitTime;
        Brain.StopNavigation();
    }

    private bool TryMoveToNextLastSeenPoint()
    {
        if (sampledPointCount >= activeSearchPointCount
            && (activeSearchArea == null || Time.time >= searchEndsAt))
            return false;

        if (activeSearchArea != null)
        {
            int firstAttemptedPoint = authoredSearchPointIndex;
            while (authoredSearchPointIndex < activeSearchArea.PointCount)
            {
                if (!activeSearchArea.TryGetPoint(authoredSearchPointIndex++, out Vector3 authoredPosition)
                    || !Brain.TryResolveReachableNavigationPosition(authoredPosition, 2f,
                        out Vector3 sampledAuthoredPosition)
                    || !activeSearchArea.ContainsPoint(sampledAuthoredPosition)
                    || !Brain.SetNavigationDestination(
                        sampledAuthoredPosition, activeSearchSpeed, Brain.SearchStoppingDistance, force: true))
                    continue;

                sampledPointCount++;
                lastSeenProgressPosition = Brain.transform.position;
                lastSeenStallElapsed = 0f;
                return true;
            }

            if (sampledPointCount == 0 && authoredSearchPointIndex > firstAttemptedPoint)
                Debug.LogWarning($"{Brain.name}: no reachable search point in {activeSearchArea.name}. "
                    + "Check NavMesh and search-area bounds.", Brain);
            return false;
        }

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Vector2 direction = Random.insideUnitCircle.normalized;
            Vector2 randomCircle = direction * Random.Range(activeSearchRadius * 0.35f, activeSearchRadius);
            Vector3 candidate = Brain.LastKnownPursuitPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);
            if (!Brain.TryResolveReachableNavigationPosition(candidate, 2f, out Vector3 sampledPosition)
                || Vector3.ProjectOnPlane(sampledPosition - Brain.transform.position, Vector3.up)
                    .sqrMagnitude < 1.25f * 1.25f
                || !Brain.NavigationCanReach(sampledPosition))
                continue;

            if (!Brain.SetNavigationDestination(sampledPosition, activeSearchSpeed, Brain.SearchStoppingDistance, force: true))
                continue;

            sampledPointCount++;
            lastSeenProgressPosition = Brain.transform.position;
            lastSeenStallElapsed = 0f;
            return true;
        }

        return false;
    }

    private void WaitForRemainingSearchTimeOrWindDown()
    {
        if (activeSearchArea == null || sampledPointCount == 0 || Time.time >= searchEndsAt)
        {
            CompleteAreaOrWindDown();
            return;
        }

        waitingAtSearchPoint = true;
        waitUntil = searchEndsAt;
        Brain.StopNavigation();
    }

    private EnemySearchArea ResolveSearchArea()
    {
        return Brain.HasLastKnownTargetPosition
            ? EnemySearchArea.FindForPosition(
                Brain.LastKnownPursuitPosition, Brain.gameObject.scene, Brain.SearchRadius)
            : null;
    }

    private bool ShouldSweepCurrentRoomBeforeLastKnown()
    {
        return activeSearchArea != null && activeSearchArea == Brain.CurrentRoom
            && (roomRouteIndex > 0 || !activeSearchArea.ContainsPoint(Brain.LastKnownTargetPosition));
    }

    private void BeginLastSeenSweep()
    {
        arrivedAtLastSeen = true;
        lastSeenProgressPosition = Brain.transform.position;
        lastSeenStallElapsed = 0f;
        float searchDuration = Brain.SearchDuration * activePersistence;
        searchEndsAt = Time.time + searchDuration;
        if (activeSearchPointCount == 0 || Time.time >= searchEndsAt)
            CompleteAreaOrWindDown();
        else
            WaitAtSearchPoint();
    }

    private void BeginWindDown()
    {
        windingDown = true;
        Brain.StopNavigation();
        beholderDialogue?.SetSearchWindingDown(true);
    }

    private void CompleteAreaOrWindDown()
    {
        if (roomRoute.Count == 0 || !TryBeginNextRoom())
            BeginWindDown();
    }

    private bool TryBeginNextRoom()
    {
        while (roomRouteIndex + 1 < roomRoute.Count)
        {
            EnemySearchArea from = Brain.CurrentRoom;
            EnemySearchArea next = roomRoute[++roomRouteIndex];
            activeSearchArea = next;
            activeSearchPointCount = Mathf.Max(
                Mathf.RoundToInt(Brain.SearchPointCount * activePersistence), next.PointCount);
            sampledPointCount = 0;
            visitedSearchPointCount = 0;
            authoredSearchPointIndex = 0;
            waitingAtSearchPoint = false;
            arrivedAtLastSeen = false;
            approachingDoorEntry = false;
            searchEndsAt = float.PositiveInfinity;
            lastSeenProgressPosition = Brain.transform.position;
            lastSeenStallElapsed = 0f;
            if (from == next)
            {
                Brain.ClearRoomCrossing();
                BeginLastSeenSweep();
                return true;
            }

            if (!Brain.TryPrepareDoorBetweenAreas(from, next))
            {
                Debug.LogWarning($"{Brain.name}: no usable traversal links search areas "
                    + $"{from?.name} and {next?.name}; skipping {next?.name}.", Brain);
                continue;
            }

            Brain.SetLastKnownNavigationDestination(
                activeSearchSpeed, Brain.SearchStoppingDistance, force: true);
            return true;
        }

        return false;
    }

    private void SkipUnreachableAreaOrReflect()
    {
        if (activeSearchArea != null && activeSearchArea == Brain.CurrentRoom
            && activeSearchArea.PointCount > 0)
        {
            Debug.LogWarning($"{Brain.name}: could not reach the last known position; "
                + $"trying search points in {activeSearchArea.name}.", Brain);
            BeginLastSeenSweep();
            return;
        }

        if (roomRoute.Count > 0 && TryBeginNextRoom())
            return;

        Brain.ChangeState(EnemyState.Reflection);
    }

    private void TryMoveToNextSearchPoint()
    {
        if (sampledPointCount >= activeSearchPointCount)
            return;

        sampledPointCount++;
        Vector2 randomCircle = Random.insideUnitCircle * activeSearchRadius;
        Vector3 candidate = Brain.LastKnownTargetPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);

        if (Brain.TrySampleNavigationPosition(candidate, activeSearchRadius, out Vector3 sampledPosition))
            candidate = sampledPosition;

        Brain.SetNavigationDestination(candidate, activeSearchSpeed, Brain.SearchStoppingDistance, force: true);
    }
}

public sealed class EnemyReflectionState : EnemyAIState
{
    private float reflectionEndsAt;

    public EnemyReflectionState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Reflection;

    public override void Enter()
    {
        Brain.StopNavigation();
        reflectionEndsAt = Time.time + Brain.ReflectionPauseDuration;
    }

    public override void Tick(float deltaTime)
    {
        if (TryChaseVisibleTarget() || TryAlertFromNoise())
            return;

        if (Time.time < reflectionEndsAt)
            return;

        Brain.NotifyReflectionCompleted();
        Brain.ClearCurrentTarget();
        Brain.ClearInvestigationPosition();
        Brain.ClearLastKnownTargetPosition();
        Brain.ClearCombatAwareness();
        Brain.ChangeState(Brain.ResolveDefaultBehaviorState());
    }
}
