using UnityEngine;

public enum BeholderBeamAimMode { MarkedPoint, FollowVisibleTarget }

[CreateAssetMenu(menuName = "Worst Knights/Beholder/Lightning Beam Attack")]
public sealed class BeholderLightningBeamAttack : BeholderAttackDefinition
{
    [Header("Raio contínuo")]
    public LightningBeamController vfxPrefab;
    [Min(0.01f)] public float duration = 3f;
    [Min(0.01f)] public float range = 50f;
    public LayerMask hitMask = Physics.DefaultRaycastLayers;
    public BeholderBeamAimMode aimMode;
    [Tooltip("Graus por segundo ao acompanhar o alvo. Zero corrige imediatamente.")]
    [Min(0f)] public float trackingSpeed = 90f;
    [Min(0f)] public float damagePerTick;
    [Min(0.01f)] public float hitInterval = 0.25f;
    public AudioCue loopAudio;

    public override bool HoldAnimation => true;
    public override bool StreamsPresentation => true;
    public override AudioCue SustainedAudio => loopAudio;
    public override bool HasPresentation => vfxPrefab != null;
    public override BeholderAttackExecution CreateExecution(BeholderAttackContext context) => new Execution(this, context);
    public override void PresentSegment(BeholderAttackPresentation presentation, Vector3 start, Vector3 end, bool hit) => presentation.ShowBeam(this, start, end, hit);

    private sealed class Execution : BeholderAttackExecution
    {
        private readonly BeholderLightningBeamAttack settings;
        private float elapsed, nextHit;
        public Execution(BeholderLightningBeamAttack settings, BeholderAttackContext context) : base(context) { this.settings = settings; }
        public override void Begin() { Sample(0f); }
        public override void Tick(float deltaTime)
        {
            if (IsFinished) return;
            elapsed += Mathf.Max(0f, deltaTime);
            if (elapsed >= Mathf.Max(0.01f, settings.duration))
            {
                IsFinished = true;
                Context.StopBeam();
                return;
            }
            Sample(deltaTime);
        }
        private void Sample(float deltaTime)
        {
            if (settings.aimMode == BeholderBeamAimMode.FollowVisibleTarget)
                Context.FollowVisibleTarget(settings.trackingSpeed, deltaTime);
            var segment = Context.Cast(settings.range, settings.hitMask);
            Context.Present(settings, segment);
            if (elapsed + 0.0001f < nextHit) return;
            Context.ApplyDamage(segment, settings.damagePerTick);
            // Missed frames never accumulate a burst of damage on the next collider.
            nextHit = elapsed + Mathf.Max(0.01f, settings.hitInterval);
        }
        public override void Cancel() { IsFinished = true; Context.StopBeam(); }
    }
}
