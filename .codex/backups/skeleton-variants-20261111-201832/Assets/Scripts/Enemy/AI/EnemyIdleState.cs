public sealed class EnemyIdleState : EnemyAIState
{
    private float remainingIdleTime;

    public EnemyIdleState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Idle;

    public override void Enter()
    {
        remainingIdleTime = Brain.IdleDuration;
        Brain.StopNavigation();
    }

    public override void Tick(float deltaTime)
    {
        if (TryChaseVisibleTarget() || TryAlertFromNoise())
            return;

        remainingIdleTime -= deltaTime;
        if (remainingIdleTime > 0f)
            return;

        if (Brain.HasPatrolPoints)
            Brain.ChangeState(EnemyState.Patrol);
        else
            remainingIdleTime = Brain.IdleDuration;
    }
}
