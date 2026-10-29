using UnityEngine;

public sealed partial class FlamethrowerTrapController
{
    private static readonly int RangeId = Shader.PropertyToID("Jet Range");
    private static readonly int WidthId = Shader.PropertyToID("Jet Width");
    private static readonly int BoundsCenterId = Shader.PropertyToID("Jet Bounds Center");
    private static readonly int BoundsSizeId = Shader.PropertyToID("Jet Bounds Size");

    public bool HasCompatibleEffect() => flamethrowerPivot != null && flameEffect != null
        && flameEffect.visualEffectAsset != null && flameEffect.HasFloat(RangeId) && flameEffect.HasFloat(WidthId);

    private void RenderJet()
    {
        if (!HasCompatibleEffect() || ResolvedLength < 0.01f) { ClearVisuals(); return; }
        flameEffect.transform.SetPositionAndRotation(JetOrigin, JetRotation);
        // The effect is authored in local space; compensate for any scaled dispenser parent.
        flameEffect.transform.localScale = Vector3.one;
        Vector3 inherited = flameEffect.transform.lossyScale;
        flameEffect.transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(inherited.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(inherited.y)), 1f / Mathf.Max(0.0001f, Mathf.Abs(inherited.z)));
        flameEffect.SetFloat(RangeId, ResolvedLength);
        flameEffect.SetFloat(WidthId, endRadius / 1.1f);
        float padding = Mathf.Max(2f, endRadius * 3f);
        if (flameEffect.HasVector3(BoundsCenterId)) flameEffect.SetVector3(BoundsCenterId, Vector3.forward * ResolvedLength * 0.5f);
        if (flameEffect.HasVector3(BoundsSizeId)) flameEffect.SetVector3(BoundsSizeId,
            new Vector3(padding * 2f, padding * 2f, ResolvedLength + padding * 2f));
        SyncFlameAudio();
        if (visualPlaying) return;
        flameEffect.initialEventName = "OnStop";
        flameEffect.pause = false;
        flameEffect.Reinit();
        flameEffect.Play();
        visualPlaying = true;
    }

    private void ClearVisuals()
    {
        StopFlameAudio();
        StopSurfaceEmission();
        if (flameEffect == null) return;
        flameEffect.initialEventName = "OnStop";
        if (!visualPlaying && !flameEffect.isActiveAndEnabled) return;
        if (visualPlaying || !flameEffect.pause)
        {
            flameEffect.Stop();
            flameEffect.Reinit();
            flameEffect.pause = true;
        }
        visualPlaying = false;
    }
}
