using UnityEngine;

public sealed class EnemyAlertState : EnemyAIState
{
    private float alertEndsAt;
    private bool hasDestination;

    public EnemyAlertState(EnemyBrain brain) : base(brain)
    {
    }

    public override EnemyState StateId => EnemyState.Alert;

    public override void Enter()
    {
        alertEndsAt = Time.time + Brain.AlertDuration;
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

    public override void Tick(float deltaTime)
    {
        if (TryChaseVisibleTarget())
            return;

        if (Brain.TryConsumeHeardNoise(out NoiseEvent noiseEvent))
        {
            Brain.SetInvestigationPosition(noiseEvent.Position);
            alertEndsAt = Time.time + Brain.AlertDuration;
            hasDestination = Brain.MoveToAlertPosition;

            if (hasDestination)
            {
                Brain.SetNavigationDestination(
                    Brain.InvestigationPosition,
                    Brain.AlertSpeed,
                    Brain.AlertStoppingDistance,
                    force: true);
            }
        }

        if (Brain.HasInvestigationPosition)
            Brain.RotateTowards(Brain.InvestigationPosition, deltaTime);

        bool reachedInvestigation = !hasDestination || Brain.NavigationHasReachedDestination();
        if (!reachedInvestigation && Time.time < alertEndsAt)
            return;

        if (Brain.HasInvestigationPosition)
            Brain.SetLastKnownTargetPosition(Brain.InvestigationPosition);

        Brain.ChangeState(EnemyState.Search);
    }
}
