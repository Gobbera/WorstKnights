using UnityEngine;

public sealed class EnemyAlertState : EnemyAIState
{
    private float alertEndsAt;
    private int activeAlertSequence;
    private bool hasDestination;

    public EnemyAlertState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Alert;

    public override void Enter()
    {
        ConfigureActiveAlert();
    }

    public override void Tick(float deltaTime)
    {
        if (activeAlertSequence != Brain.ActiveAlertSequence)
            ConfigureActiveAlert();

        if (Brain.HasActiveTargetAlert)
        {
            TickTargetAlert(deltaTime);
            return;
        }

        if (TryChaseVisibleTarget())
            return;

        if (Brain.TryConsumeHeardNoise(out NoiseEvent noiseEvent))
        {
            Brain.BeginInvestigationAlert(noiseEvent.Position);
            ConfigureActiveAlert();
        }

        if (Brain.RotateDuringAlert && Brain.HasInvestigationPosition)
            Brain.RotateTowards(Brain.InvestigationPosition, deltaTime);

        bool reachedInvestigation = !hasDestination || Brain.NavigationHasReachedDestination();
        if (!reachedInvestigation && Time.time < alertEndsAt)
            return;

        if (Brain.HasInvestigationPosition)
            Brain.SetLastKnownTargetPosition(Brain.InvestigationPosition);

        Brain.ChangeState(EnemyState.Search);
    }

    public override void Exit()
    {
        Brain.ClearActiveAlertContext();
    }

    private void ConfigureActiveAlert()
    {
        activeAlertSequence = Brain.ActiveAlertSequence;
        alertEndsAt = Time.time + Brain.AlertDuration;

        if (Brain.HasActiveTargetAlert)
        {
            hasDestination = false;
            Brain.StopNavigation();
            return;
        }

        hasDestination = Brain.HasInvestigationPosition && Brain.MoveToAlertPosition;
        if (hasDestination)
        {
            Brain.SetNavigationDestination(
                Brain.InvestigationPosition,
                Brain.AlertSpeed,
                Brain.AlertStoppingDistance,
                force: true);
        }
        else
        {
            Brain.StopNavigation();
        }
    }

    private void TickTargetAlert(float deltaTime)
    {
        Brain.StopNavigation();

        Brain.TryRefreshVisibleTarget();
        Brain.TryRefreshCombatMemoryTarget();
        Brain.TryRefreshCloseAwarenessTarget();

        if (Brain.RotateDuringAlert && Brain.HasLastKnownTargetPosition)
            Brain.RotateTowards(Brain.LastKnownTargetPosition, deltaTime);

        if (Time.time < alertEndsAt)
            return;

        if (Brain.HasValidTarget && Brain.HasLastKnownTargetPosition)
        {
            Brain.ChangeState(EnemyState.Chase);
            return;
        }

        Brain.ChangeState(Brain.HasLastKnownTargetPosition ? EnemyState.Search : EnemyState.Return);
    }
}
