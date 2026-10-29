using UnityEngine;

[CreateAssetMenu(menuName = "Worst Knights/Beholder/Bite Attack")]
public sealed class BeholderBiteAttack : BeholderAttackDefinition
{
    [Header("Mordida")]
    [Tooltip("Distância máxima, em metros, entre Origin Name e o collider do alvo para iniciar a mordida.")]
    [Min(0.01f)] public float range = 3f;

    public override bool RequiresVfx => false;
    public override bool HasPresentation => false;

    public override bool MeetsTargetConditions(Vector3 origin, PlayerHealth target)
    {
        if (target == null || !target.CanBeTargetedByEnemy) return false;
        float distanceSquared = float.PositiveInfinity;
        foreach (Collider collider in target.GetComponentsInChildren<Collider>())
            if (collider.enabled && !collider.isTrigger)
                distanceSquared = Mathf.Min(distanceSquared, (collider.ClosestPoint(origin) - origin).sqrMagnitude);
        if (float.IsPositiveInfinity(distanceSquared)) distanceSquared = (target.transform.position - origin).sqrMagnitude;
        return distanceSquared <= Mathf.Max(0f, range) * Mathf.Max(0f, range);
    }

    public override BeholderAttackExecution CreateExecution(BeholderAttackContext context) => new Execution(context);
    public override void PresentSegment(BeholderAttackPresentation presentation, Vector3 start, Vector3 end, bool hit) { }

    private sealed class Execution : BeholderAttackExecution
    {
        public Execution(BeholderAttackContext context) : base(context) { }
        public override void Begin() { IsFinished = Context.IsAnimationComplete; }
        public override void Tick(float deltaTime) { if (!IsFinished) IsFinished = Context.IsAnimationComplete; }
    }
}
