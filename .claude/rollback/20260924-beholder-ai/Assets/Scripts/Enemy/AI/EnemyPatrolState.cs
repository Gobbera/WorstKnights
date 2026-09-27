using UnityEngine;

public sealed class EnemyPatrolState : EnemyAIState
{
    private int patrolIndex = -1;
    private float waitUntil;
    private bool waitingAtPoint;

    public EnemyPatrolState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Patrol;

    public override void Enter()
    {
        waitingAtPoint = false;
        patrolIndex = Brain.ResolveInitialPatrolIndex();
        MoveToCurrentPoint(force: true);
    }

    public override void Tick(float deltaTime)
    {
        if (TryChaseVisibleTarget() || TryAlertFromNoise())
            return;

        if (!Brain.HasPatrolPoints)
        {
            Brain.ChangeState(EnemyState.Idle);
            return;
        }

        if (waitingAtPoint)
        {
            if (Time.time < waitUntil)
                return;

            waitingAtPoint = false;
            patrolIndex = Brain.ResolveNextPatrolIndex(patrolIndex);
            MoveToCurrentPoint(force: true);
            return;
        }

        if (!Brain.NavigationHasReachedDestination())
            return;

        waitingAtPoint = true;
        waitUntil = Time.time + Brain.ResolvePatrolWaitTime(patrolIndex);
        Brain.StopNavigation();
    }

    private void MoveToCurrentPoint(bool force)
    {
        if (!Brain.TryGetPatrolPointPosition(patrolIndex, out Vector3 patrolPosition))
        {
            Brain.ChangeState(EnemyState.Idle);
            return;
        }

        Brain.SetNavigationDestination(patrolPosition, Brain.PatrolSpeed, Brain.PatrolStoppingDistance, force);
    }
}
