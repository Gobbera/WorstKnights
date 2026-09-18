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
    private bool waitingAtSearchPoint;

    public EnemySearchState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Search;

    public override void Enter()
    {
        activeSearchRadius = Brain.ResolveActiveSearchRadius();
        activeSearchSpeed = Brain.ResolveActiveSearchSpeed();
        activeSearchPointWaitTime = Brain.ResolveActiveSearchPointWaitTime();
        activeSearchPointCount = Brain.ResolveActiveSearchPointCount();
        searchEndsAt = Time.time + Brain.ResolveActiveSearchDuration();
        sampledPointCount = 0;
        waitingAtSearchPoint = false;

        if (Brain.HasLastKnownTargetPosition)
        {
            Brain.SetNavigationDestination(
                Brain.LastKnownTargetPosition,
                activeSearchSpeed,
                Brain.SearchStoppingDistance,
                force: true);
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
