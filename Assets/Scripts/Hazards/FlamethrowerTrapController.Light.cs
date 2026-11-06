using UnityEngine;

public sealed partial class FlamethrowerTrapController
{
    public const string EndLightObjectName = "Flamethrower End Light";
    private Vector3 terminalSurfaceNormal;
    private Vector3 lastEndLightPosition;
    private float endLightFill;

    private void RenderEndLight()
    {
        endLightFill = ResolvedLength > 0.001f ? Mathf.Clamp01(DamageLength / ResolvedLength) : 0f;
        Vector3 away = -JetDirection;
        if (terminalSurfaceNormal.sqrMagnitude > 0.001f)
        {
            float proximity = 1f - Mathf.Clamp01((ResolvedLength - DamageLength) / Mathf.Max(0.001f, endLightSurfaceOffset));
            away = Vector3.Lerp(away, terminalSurfaceNormal, proximity);
        }
        lastEndLightPosition = JetOrigin + JetDirection * DamageLength
            + away * Mathf.Min(endLightSurfaceOffset, DamageLength);
        FadeEndLight(1f);
    }

    private void FadeEndLight(float opacity)
    {
        if (endLight == null) return;
        float intensity = endLightEnabled ? endLightIntensity * endLightFill * opacity : 0f;
        endLight.transform.position = lastEndLightPosition;
        endLight.type = LightType.Point;
        endLight.color = endLightColor;
        endLight.range = endLightRange;
        endLight.intensity = intensity;
        endLight.enabled = intensity > 0.0001f;
    }

    private void DisableEndLight()
    {
        if (endLight == null) return;
        endLight.intensity = 0f;
        endLight.enabled = false;
    }
}
