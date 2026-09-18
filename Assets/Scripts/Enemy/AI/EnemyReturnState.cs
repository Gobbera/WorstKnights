using UnityEngine;

public sealed class EnemyReturnState : EnemyAIState
{
    public EnemyReturnState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Return;

    public override void Enter()
    {
        if (Brain.TryGetReturnPosition(out Vector3 returnPosition))
            Brain.SetNavigationDestination(returnPosition, Brain.ReturnSpeed, Brain.ReturnStoppingDistance, force: true);
        else
            Brain.ChangeState(Brain.ResolveDefaultBehaviorState());
    }

    public override void Tick(float deltaTime)
    {
        if (TryChaseVisibleTarget() || TryAlertFromNoise())
            return;

        if (!Brain.NavigationHasReachedDestination())
            return;

        Brain.ClearInvestigationPosition();
        Brain.ClearLastKnownTargetPosition();
        Brain.ClearCombatAwareness();
        Brain.ChangeState(Brain.ResolveDefaultBehaviorState());
    }
}
