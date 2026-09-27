using UnityEngine;

public sealed class EnemyChaseState : EnemyAIState
{
    private float nextDestinationUpdateTime;
    private float lostSightStartedAt;
    private bool hasLostSightStart;

    public EnemyChaseState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Chase;

    public override void Enter()
    {
        nextDestinationUpdateTime = 0f;
        hasLostSightStart = false;
    }

    public override void Tick(float deltaTime)
    {
        if (!Brain.HasValidTarget)
        {
            Brain.ClearCurrentTarget();
            Brain.ChangeState(Brain.HasLastKnownTargetPosition ? EnemyState.Search : EnemyState.Return);
            return;
        }

        bool canSeeTarget = Brain.TryRefreshVisibleTarget()
            || Brain.TryRefreshCombatMemoryTarget()
            || Brain.TryRefreshCloseAwarenessTarget();
        if (canSeeTarget)
        {
            hasLostSightStart = false;

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
            if (!hasLostSightStart)
            {
                lostSightStartedAt = Time.time;
                hasLostSightStart = true;
            }

            if (Time.time >= lostSightStartedAt + Brain.ResolveLostSightGraceDuration())
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
}
