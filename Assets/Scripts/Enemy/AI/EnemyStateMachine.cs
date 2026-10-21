using System;

public sealed class EnemyStateMachine
{
    private readonly EnemyAIState idleState;
    private readonly EnemyAIState patrolState;
    private readonly EnemyAIState alertState;
    private readonly EnemyAIState chaseState;
    private readonly EnemyAIState attackState;
    private readonly EnemyAIState searchState;
    private readonly EnemyAIState returnState;
    private readonly EnemyAIState reflectionState;

    private EnemyAIState currentState;

    public EnemyStateMachine(EnemyBrain brain)
    {
        if (brain == null)
            throw new ArgumentNullException(nameof(brain));

        idleState = new EnemyIdleState(brain);
        patrolState = new EnemyPatrolState(brain);
        alertState = new EnemyAlertState(brain);
        chaseState = new EnemyChaseState(brain);
        attackState = new EnemyAttackState(brain);
        searchState = new EnemySearchState(brain);
        returnState = new EnemyReturnState(brain);
        reflectionState = new EnemyReflectionState(brain);
    }

    public EnemyState CurrentStateId => currentState != null ? currentState.StateId : EnemyState.Idle;
    public bool HasCurrentState => currentState != null;

    public void ChangeState(EnemyState stateId)
    {
        EnemyAIState nextState = ResolveState(stateId);
        if (nextState == null || nextState == currentState)
            return;

        currentState?.Exit();
        currentState = nextState;
        currentState.Enter();
    }

    public void Tick(float deltaTime)
    {
        currentState?.Tick(deltaTime);
    }

    private EnemyAIState ResolveState(EnemyState stateId)
    {
        switch (stateId)
        {
            case EnemyState.Patrol:
                return patrolState;
            case EnemyState.Alert:
                return alertState;
            case EnemyState.Chase:
                return chaseState;
            case EnemyState.Attack:
                return attackState;
            case EnemyState.Search:
                return searchState;
            case EnemyState.Return:
                return returnState;
            case EnemyState.Reflection:
                return reflectionState;
            default:
                return idleState;
        }
    }
}
