using UnityEngine;

public sealed partial class FlamethrowerTrapController
{
    private static readonly int RangeId = Shader.PropertyToID("Jet Range");
    private static readonly int WidthId = Shader.PropertyToID("Jet Width");
    private static readonly int OpacityId = Shader.PropertyToID("Jet Opacity");
    private static readonly int BoundsCenterId = Shader.PropertyToID("Jet Bounds Center");
    private static readonly int BoundsSizeId = Shader.PropertyToID("Jet Bounds Size");
    private bool visualFading;
    private double visualFadeStartedAt;

    public bool HasCompatibleEffect() => flamethrowerPivot != null && flameEffect != null
        && flameEffect.visualEffectAsset != null && flameEffect.HasFloat(RangeId) && flameEffect.HasFloat(WidthId)
        && flameEffect.HasFloat(OpacityId);

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
        flameEffect.SetFloat(OpacityId, 1f);
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

    private void RefreshVisualFade(double now, double endedAt)
    {
        StopFlameAudio();
        if (flameEffect == null || !flameEffect.isActiveAndEnabled || fadeOutDuration <= 0f)
        {
            ClearVisuals();
            return;
        }
        if (visualPlaying)
        {
            visualPlaying = false;
            visualFading = true;
            visualFadeStartedAt = endedAt;
            // Stop spawning, retaining the existing particles and their simulation.
            flameEffect.Stop();
        }
        if (!visualFading) return;
        float progress = Mathf.Clamp01((float)((now - visualFadeStartedAt) / fadeOutDuration));
        if (progress >= 1f) { ClearVisuals(); return; }
        // Restoring a moving dispenser must not snap the fading particles back with its pivot.
        flameEffect.transform.SetPositionAndRotation(JetOrigin, JetRotation);
        flameEffect.SetFloat(OpacityId, 1f - Mathf.SmoothStep(0f, 1f, progress));
        flameEffect.pause = false;
    }

    private void ClearVisuals()
    {
        StopFlameAudio();
        visualFading = false;
        if (flameEffect == null) return;
        if (flameEffect.HasFloat(OpacityId)) flameEffect.SetFloat(OpacityId, 0f);
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
