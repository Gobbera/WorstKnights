public abstract class EnemyAIState
{
    protected EnemyAIState(EnemyBrain brain)
    {
        Brain = brain;
    }

    protected EnemyBrain Brain { get; }
    public abstract EnemyState StateId { get; }

    public virtual void Enter()
    {
    }

    public virtual void Tick(float deltaTime)
    {
    }

    public virtual void Exit()
    {
    }

    protected bool TryChaseVisibleTarget()
    {
        bool hadTargetAwareness = Brain.HasTargetAwareness;
        if (!Brain.TryRefreshVisibleTarget()
            && !Brain.TryRefreshCombatMemoryTarget()
            && !Brain.TryRefreshCloseAwarenessTarget())
        {
            return false;
        }

        if (Brain.ShouldAlertBeforeChase(hadTargetAwareness))
        {
            Brain.BeginTargetAlert();
            Brain.ChangeState(EnemyState.Alert);
        }
        else
        {
            Brain.ClearActiveAlertContext();
            Brain.ChangeState(EnemyState.Chase);
        }

        return true;
    }

    protected bool TryAlertFromNoise()
    {
        if (!Brain.TryConsumeHeardNoise(out NoiseEvent noiseEvent))
            return false;

        Brain.BeginInvestigationAlert(noiseEvent.Position);
        Brain.ChangeState(EnemyState.Alert);
        return true;
    }

    protected void ReturnOrIdle()
    {
        Brain.ChangeState(Brain.ShouldReturnToPatrol ? EnemyState.Return : Brain.ResolveDefaultBehaviorState());
    }
}
