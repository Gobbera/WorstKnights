using UnityEngine;

public sealed class EnemyAttackState : EnemyAIState
{
    public EnemyAttackState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Attack;

    public override void Enter()
    {
        Brain.StopNavigation();
    }

    public override void Tick(float deltaTime)
    {
        if (Brain.IsAttackLocked)
        {
            Brain.TickAttackMovement();
            Brain.TrackCurrentTargetDuringAttack(deltaTime);
            return;
        }

        Brain.StopNavigation();

        if (!Brain.CanStartAttack)
        {
            Brain.ChangeState(ResolveFallbackState());
            return;
        }

        if (!Brain.HasValidTarget)
        {
            Brain.ClearCurrentTarget();
            Brain.ChangeState(Brain.HasLastKnownTargetPosition ? EnemyState.Search : EnemyState.Return);
            return;
        }

        bool canSeeTarget = Brain.TryRefreshPursuitTarget();
        if (!canSeeTarget)
        {
            Brain.ChangeState(Brain.HasLastKnownTargetPosition ? EnemyState.Chase : EnemyState.Search);
            return;
        }

        if (!Brain.IsCurrentTargetInsideAttackStartRange())
        {
            Brain.ChangeState(EnemyState.Chase);
            return;
        }

        Brain.RotateTowards(Brain.LastKnownTargetPosition, deltaTime);
        if (!Brain.TryAttackCurrentTarget())
            return;

        Brain.TickAttackMovement();
    }

    public override void Exit()
    {
        Brain.CancelBeholderAttack();
    }

    private EnemyState ResolveFallbackState()
    {
        if (Brain.HasValidTarget)
            return EnemyState.Chase;

        return Brain.HasLastKnownTargetPosition ? EnemyState.Search : EnemyState.Return;
    }
}
