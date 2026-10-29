using UnityEngine;

[CreateAssetMenu(menuName = "Worst Knights/Beholder/Bite Attack")]
public sealed class BeholderBiteAttack : BeholderAttackDefinition
{
    [Header("Mordida")]
    [Tooltip("Distância máxima, em metros, entre Origin Name e o collider do alvo para iniciar a mordida.")]
    [Min(0.01f)] public float range = 3f;
    [Min(0f)] public float damage = 20f;
    [Tooltip("Instante do acerto em segundos da animação. O alcance é verificado novamente nesse instante.")]
    [BeholderAttackController.AudioTime, Min(0f)] public float hitTime = 1f;

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

    public override BeholderAttackExecution CreateExecution(BeholderAttackContext context) => new Execution(this, context);
    public override void PresentSegment(BeholderAttackPresentation presentation, Vector3 start, Vector3 end, bool hit) { }

    private sealed class Execution : BeholderAttackExecution
    {
        private readonly BeholderBiteAttack settings;
        private bool hitResolved;
        public Execution(BeholderBiteAttack settings, BeholderAttackContext context) : base(context) { this.settings = settings; }
        public override void Begin() { Sample(); }
        public override void Tick(float deltaTime) { if (!IsFinished) Sample(); }
        private void Sample()
        {
            float hitAt = Mathf.Clamp(settings.hitTime, 0f, settings.animation.length);
            if (!hitResolved && Context.AnimationTime >= hitAt)
            {
                hitResolved = true;
                Context.TryApplyMeleeDamage(settings.damage, settings.range);
            }
            IsFinished = Context.IsAnimationComplete;
        }
    }
}
