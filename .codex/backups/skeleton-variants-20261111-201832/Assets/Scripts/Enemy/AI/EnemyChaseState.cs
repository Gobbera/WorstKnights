using UnityEngine;

public sealed class EnemyChaseState : EnemyAIState
{
    private float nextDestinationUpdateTime;
    private float stalledFor;
    private Vector3 lastProgressPosition;
    private Vector3 lastKnownDestination;
    private bool followingLastKnownPosition;
    private bool hasLastKnownDestination;
    private bool wasCrossingRoom;
    private bool approachingDoorEntry;
    private bool investigationStarted;
    private float legacyLostSightStartedAt;
    private bool legacyHasLostSightStart;

    public EnemyChaseState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Chase;

    public override void Enter()
    {
        nextDestinationUpdateTime = 0f;
        stalledFor = 0f;
        followingLastKnownPosition = false;
        hasLastKnownDestination = false;
        wasCrossingRoom = false;
        approachingDoorEntry = false;
        investigationStarted = Brain.HasInvestigationOrigin;
        legacyHasLostSightStart = false;
    }

    public override void Tick(float deltaTime)
    {
        if (!Brain.UseLastSeenSearchFlow)
        {
            TickLegacyChase();
            return;
        }

        if (!Brain.HasValidTarget)
        {
            if (Brain.TryPursueNextPlayerEvidence())
                return;
            Brain.ClearCurrentTarget();
            Brain.ChangeState(Brain.HasLastKnownTargetPosition ? EnemyState.Search : EnemyState.Return);
            return;
        }

        bool canSeeTarget = Brain.TryRefreshPursuitTarget();
        if (canSeeTarget)
        {
            if (followingLastKnownPosition)
            {
                followingLastKnownPosition = false;
                hasLastKnownDestination = false;
                approachingDoorEntry = false;
                stalledFor = 0f;
                nextDestinationUpdateTime = 0f;
                investigationStarted = false;
            }

            if (Brain.IsLastKnownPositionBeyondMaxChaseDistance())
            {
                Brain.ClearCurrentTarget();
                Brain.ChangeState(EnemyState.Return);
                return;
            }

            if (Brain.IsCurrentTargetInsideAttackStartRange())
            {
                Brain.ChangeState(EnemyState.Attack);
                return;
            }
        }
        else
        {
            TickLastKnownPursuit(deltaTime);
            return;
        }

        if (Brain.HasLastKnownTargetPosition && Time.time >= nextDestinationUpdateTime)
        {
            Brain.SetNavigationDestination(
                Brain.LastKnownTargetPosition,
                Brain.ChaseSpeed,
                Brain.ChaseStoppingDistance,
                force: false);

            nextDestinationUpdateTime = Time.time + Brain.ChaseDestinationUpdateInterval;
        }
    }

    private void TickLegacyChase()
    {
        if (!Brain.HasValidTarget)
        {
            Brain.ClearCurrentTarget();
            Brain.ChangeState(Brain.HasLastKnownTargetPosition ? EnemyState.Search : EnemyState.Return);
            return;
        }

        bool canSeeTarget = Brain.TryRefreshPursuitTarget();
        if (canSeeTarget)
        {
            legacyHasLostSightStart = false;

            if (Brain.IsLastKnownPositionBeyondMaxChaseDistance())
            {
                Brain.ClearCurrentTarget();
                Brain.ChangeState(EnemyState.Return);
                return;
            }

            if (Brain.IsCurrentTargetInsideAttackStartRange())
            {
                Brain.ChangeState(EnemyState.Attack);
                return;
            }
        }
        else
        {
            if (!legacyHasLostSightStart)
            {
                legacyLostSightStartedAt = Time.time;
                legacyHasLostSightStart = true;
            }

            if (Time.time >= legacyLostSightStartedAt + Brain.ResolveLostSightGraceDuration())
            {
                Brain.ClearCurrentTarget();
                Brain.ChangeState(EnemyState.Search);
                return;
            }
        }

        if (!Brain.HasLastKnownTargetPosition)
        {
            Brain.ChangeState(EnemyState.Search);
            return;
        }

        if (Time.time >= nextDestinationUpdateTime)
        {
            Brain.SetNavigationDestination(
                Brain.LastKnownTargetPosition,
                Brain.ChaseSpeed,
                Brain.ChaseStoppingDistance,
                force: false);

            nextDestinationUpdateTime = Time.time + Brain.ChaseDestinationUpdateInterval;
        }
    }

    private void TickLastKnownPursuit(float deltaTime)
    {
        if (!Brain.HasLastKnownTargetPosition)
        {
            Brain.ClearCurrentTarget();
            Brain.ChangeState(EnemyState.Return);
            return;
        }

        if (!followingLastKnownPosition)
        {
            followingLastKnownPosition = true;
            lastProgressPosition = Brain.transform.position;
            stalledFor = 0f;
            if (!investigationStarted)
            {
                Brain.BeginLastKnownInvestigation();
                investigationStarted = true;
            }
        }

        Brain.TryPrepareDoorForCurrentEvidence();
        bool crossingRoom = Brain.HasDoorInvestigation && !Brain.HasEnteredDoorInvestigationSide();
        Vector3 destination = Brain.LastKnownPursuitPosition;
        if (!hasLastKnownDestination || crossingRoom != wasCrossingRoom
            || (destination - lastKnownDestination).sqrMagnitude > 0.01f)
        {
            Brain.SetLastKnownNavigationDestination(Brain.ChaseSpeed, Brain.SearchStoppingDistance, force: true);
            lastKnownDestination = destination;
            hasLastKnownDestination = true;
            wasCrossingRoom = crossingRoom;
            approachingDoorEntry = false;
        }

        if (!Brain.HasEnteredDoorInvestigationSide()
            && (!approachingDoorEntry || !Brain.NavigationHasUsablePath()))
            approachingDoorEntry = Brain.TryApproachDoorInvestigationEntry() || approachingDoorEntry;

        if (Brain.HasReachedLastKnownTargetPosition())
        {
            if (Brain.HasFreshCurrentTargetHearing)
                return;

            if (Brain.TryPursueNextPlayerEvidence())
            {
                followingLastKnownPosition = false;
                hasLastKnownDestination = false;
                approachingDoorEntry = false;
                nextDestinationUpdateTime = 0f;
                return;
            }

            Brain.ClearCurrentTarget();
            Brain.ChangeState(EnemyState.Search);
            return;
        }

        float timeout = Brain.ResolveLastSeenStallTimeout();
        if (timeout <= 0f)
            return;

        Vector3 progress = Vector3.ProjectOnPlane(Brain.transform.position - lastProgressPosition, Vector3.up);
        float minimumProgress = Brain.LastSeenProgressDistance;
        if (progress.sqrMagnitude >= minimumProgress * minimumProgress)
        {
            lastProgressPosition = Brain.transform.position;
            stalledFor = 0f;
            return;
        }

        stalledFor += Mathf.Max(0f, deltaTime);
        if (stalledFor >= timeout)
        {
            if (Brain.TryPursueNextPlayerEvidence())
            {
                followingLastKnownPosition = false;
                hasLastKnownDestination = false;
                approachingDoorEntry = false;
                nextDestinationUpdateTime = 0f;
                return;
            }

            Brain.ClearCurrentTarget();
            bool hasSearchArea = Brain.InvestigationPrimaryArea != null || Brain.HasDoorInvestigation
                || EnemySearchArea.FindForPosition(Brain.LastKnownPursuitPosition,
                    Brain.gameObject.scene, Brain.SearchRadius) != null;
            Brain.ChangeState(hasSearchArea ? EnemyState.Search : EnemyState.Reflection);
        }
    }
}
