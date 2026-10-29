using UnityEngine;

[CreateAssetMenu(menuName = "Worst Knights/Beholder/Lightning Strike Attack")]
public sealed class BeholderLightningStrikeAttack : BeholderAttackDefinition
{
    [Header("Disparo instantâneo")]
    public LightningStrikeController vfxPrefab;
    [Min(0.01f)] public float range = 50f;
    public LayerMask hitMask = Physics.DefaultRaycastLayers;
    [Min(0.1f)] public float visualLifetime = 2f;
    [Min(0f)] public float damage;

    public override bool HasPresentation => vfxPrefab != null;
    public override BeholderAttackExecution CreateExecution(BeholderAttackContext context) => new Execution(this, context);
    public override void PresentSegment(BeholderAttackPresentation presentation, Vector3 start, Vector3 end, bool hit) => presentation.ShowStrike(this, start, end, hit);

    private sealed class Execution : BeholderAttackExecution
    {
        private readonly BeholderLightningStrikeAttack settings;
        public Execution(BeholderLightningStrikeAttack settings, BeholderAttackContext context) : base(context) { this.settings = settings; }
        public override void Begin()
        {
            var segment = Context.Cast(settings.range, settings.hitMask);
            Context.Present(settings, segment);
            Context.ApplyDamage(segment, settings.damage);
            IsFinished = true;
        }
    }
}
