using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

public sealed partial class FlamethrowerTrapController
{
    private const string SurfaceObjectName = "Flamethrower Surface Spread (Runtime)";
    private const float SurfaceSkin = 0.06f;
    private const int MaxSurfacePatches = 8;
    private sealed class SurfacePatch
    {
        public VisualEffect effect;
        public Collider collider;
        public Vector3 point, normal, tangent, bitangent, incident;
        public double started, lastFed, nextGeometryUpdate;
        public float lifetime, incidentSpeed;
        public bool feeding;
        public Vector4 limitsA = Vector4.zero, limitsB = Vector4.zero;
    }
    private readonly List<SurfacePatch> surfacePatches = new List<SurfacePatch>(MaxSurfacePatches);

    private void RefreshSurfacePatches(double now, bool feed)
    {
        SurfacePatch fed = null;
        if (feed && surfaceSpreadEnabled && surfaceSpreadAsset != null && surfaceEmissionRate > 0f)
        {
            RaycastHit hit = SurfaceContact;
            foreach (SurfacePatch patch in surfacePatches)
                if (patch.feeding && patch.collider == hit.collider && Vector3.Dot(patch.normal, hit.normal) > 0.97f
                    && (patch.point - hit.point).sqrMagnitude < 0.35f * 0.35f) { fed = patch; break; }
            if (fed == null) fed = CreateSurfacePatch(hit, now);
            fed.feeding = true;
            fed.lastFed = now;
        }
        foreach (SurfacePatch patch in surfacePatches)
            if (patch != fed && patch.feeding) StopSurfacePatch(patch);
        for (int i = surfacePatches.Count - 1; i >= 0; i--)
        {
            SurfacePatch patch = surfacePatches[i];
            if (patch.effect == null || (!patch.feeding && now - patch.lastFed >= patch.lifetime))
            {
                DestroySurfacePatch(patch);
                surfacePatches.RemoveAt(i);
                continue;
            }
            patch.effect.transform.SetPositionAndRotation(patch.point, Quaternion.identity);
            if (now >= patch.nextGeometryUpdate)
            {
                UpdateSurfaceLimits(patch);
                patch.nextGeometryUpdate = now + 0.1d;
            }
            ConfigureSurfaceEffect(patch);
        }
    }

    private SurfacePatch CreateSurfacePatch(RaycastHit hit, double now)
    {
        if (surfacePatches.Count >= MaxSurfacePatches)
        {
            DestroySurfacePatch(surfacePatches[0]);
            surfacePatches.RemoveAt(0);
        }
        Vector3 normal = hit.normal.normalized;
        Vector3 tangent = Vector3.ProjectOnPlane(JetDirection, normal);
        if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right);
        tangent.Normalize();
        var visual = new GameObject(SurfaceObjectName);
        visual.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        visual.layer = flameEffect != null ? flameEffect.gameObject.layer : gameObject.layer;
        visual.transform.SetParent(transform, false);
        visual.transform.SetPositionAndRotation(hit.point, Quaternion.identity);
        var effect = visual.AddComponent<VisualEffect>();
        effect.initialEventName = "OnStop";
        effect.visualEffectAsset = surfaceSpreadAsset;
        var patch = new SurfacePatch
        {
            effect = effect, collider = hit.collider, point = hit.point, normal = normal, tangent = tangent,
            bitangent = Vector3.Cross(normal, tangent).normalized, incident = JetDirection, started = now, lastFed = now,
            lifetime = surfaceParticleLifetime, incidentSpeed = range / Mathf.Max(0.5f, fillTime), feeding = true
        };
        surfacePatches.Add(patch);
        UpdateSurfaceLimits(patch);
        ConfigureSurfaceEffect(patch);
        effect.Reinit();
        effect.Play();
        return patch;
    }

    private void ConfigureSurfaceEffect(SurfacePatch patch)
    {
        VisualEffect effect = patch.effect;
        effect.SetVector3("Surface Position", patch.point);
        effect.SetVector3("Surface Normal", patch.normal);
        effect.SetVector3("Surface Tangent", patch.tangent);
        effect.SetVector3("Surface Bitangent", patch.bitangent);
        effect.SetVector3("Surface Incident", patch.incident);
        effect.SetFloat("Surface Speed", patch.incidentSpeed);
        effect.SetFloat("Surface Retention", surfaceSpeedRetention);
        effect.SetFloat("Surface Reflection", surfaceReflection);
        effect.SetFloat("Surface Friction", surfaceFriction);
        effect.SetFloat("Surface Drag", surfaceDrag);
        effect.SetFloat("Surface Buoyancy", surfaceBuoyancy);
        effect.SetFloat("Surface Lifetime", patch.lifetime);
        effect.SetFloat("Surface Fade", surfaceDissipation);
        effect.SetFloat("Surface Scale", endRadius / 1.1f * 0.45f);
        effect.SetFloat("Surface Rate", surfaceEmissionRate);
        // Contact particles sit close to geometry and need a much shorter fade than the free jet.
        effect.SetFloat("Soft Particules Factor", surfaceSoftFadeDistance);
        effect.SetVector4("Surface Limits A", patch.limitsA);
        effect.SetVector4("Surface Limits B", patch.limitsB);
        effect.SetVector3("Surface Bounds Center", patch.normal * surfaceLayerThickness * 0.5f);
        effect.SetVector3("Surface Bounds Size", Vector3.one * (surfaceSpreadRadius * 2f + 4f));
    }

    private void UpdateSurfaceLimits(SurfacePatch patch)
    {
        Vector3 origin = patch.point + patch.normal * SurfaceSkin;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4f;
            Vector3 direction = patch.tangent * Mathf.Cos(angle) + patch.bitangent * Mathf.Sin(angle);
            int count = CastObstructions(origin, direction, surfaceSpreadRadius);
            float limit = surfaceSpreadRadius;
            for (int h = 0; h < count; h++)
                if (IsObstruction(obstructionHits[h].collider)) limit = Mathf.Min(limit, Mathf.Max(0f, obstructionHits[h].distance - SurfaceSkin));
            if (i < 4) patch.limitsA[i] = limit; else patch.limitsB[i - 4] = limit;
        }
    }

    private void StopSurfaceEmission()
    {
        foreach (SurfacePatch patch in surfacePatches)
            if (patch.feeding) StopSurfacePatch(patch);
    }

    private static void StopSurfacePatch(SurfacePatch patch)
    {
        patch.feeding = false;
        if (patch.effect != null) patch.effect.Stop();
    }

    private void DestroySurfacePatch(SurfacePatch patch)
    {
        if (patch.effect == null) return;
        patch.effect.Stop();
        if (Application.IsPlaying(gameObject)) Destroy(patch.effect.gameObject);
        else DestroyImmediate(patch.effect.gameObject);
    }

    private void ClearSurfacePatches()
    {
        foreach (SurfacePatch patch in surfacePatches) DestroySurfacePatch(patch);
        surfacePatches.Clear();
        // Clean transient children surviving an Editor script reload as well.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != SurfaceObjectName) continue;
            if (Application.IsPlaying(gameObject)) Destroy(child.gameObject); else DestroyImmediate(child.gameObject);
        }
    }

    private float SurfaceReach(SurfacePatch patch, double now)
    {
        float age = Mathf.Min(patch.lifetime, Mathf.Max(0f, (float)(now - patch.started)));
        float travel = surfaceDrag > 0.0001f ? (1f - Mathf.Exp(-surfaceDrag * age)) / surfaceDrag : age;
        float upwardTravel = Vector3.ProjectOnPlane(Vector3.up, patch.normal).magnitude * surfaceBuoyancy * age * age * 0.5f;
        return Mathf.Min(surfaceSpreadRadius, patch.incidentSpeed * surfaceSpeedRetention * travel + upwardTravel + SurfaceSkin);
    }

    private float SurfaceDirectionalReach(SurfacePatch patch, Vector3 radial, double now)
    {
        if (radial.sqrMagnitude < 0.0001f) return startRadius;
        Vector3 direction = radial.normalized;
        float inward = Mathf.Max(0f, -Vector3.Dot(patch.incident, patch.normal));
        Vector3 bias = Vector3.ProjectOnPlane(patch.incident, patch.normal) * (1f - surfaceFriction);
        float scatter = inward * (1f - surfaceFriction);
        float along = Vector3.Dot(bias, direction);
        float discriminant = scatter * scatter - Mathf.Max(0f, bias.sqrMagnitude - along * along);
        float speed = discriminant >= 0f ? Mathf.Max(0f, along + Mathf.Sqrt(discriminant)) : 0f;
        speed = Mathf.Min(1f, speed) * patch.incidentSpeed * surfaceSpeedRetention;
        float age = Mathf.Min(patch.lifetime, Mathf.Max(0f, (float)(now - patch.started)));
        float travel = surfaceDrag > 0.0001f ? (1f - Mathf.Exp(-surfaceDrag * age)) / surfaceDrag : age;
        float upwardDrift = Mathf.Max(0f, Vector3.Dot(direction, Vector3.ProjectOnPlane(Vector3.up, patch.normal)))
            * surfaceBuoyancy * age * age * 0.5f;
        return Mathf.Min(surfaceSpreadRadius, speed * travel + upwardDrift) + startRadius * 0.5f;
    }

    private void ApplySurfaceDamage(double now)
    {
        if (!surfaceSpreadEnabled || surfaceDamageMultiplier <= 0f) return;
        foreach (SurfacePatch patch in surfacePatches)
        {
            float fade = patch.feeding ? 1f : Mathf.Pow(Mathf.Clamp01(1f - (float)(now - patch.lastFed) / patch.lifetime), surfaceDissipation);
            float radius = SurfaceReach(patch, now);
            if (fade <= 0.001f || radius <= 0.001f) continue;
            Vector3 center = patch.point + patch.normal * surfaceLayerThickness * 0.5f;
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(center, radius + surfaceLayerThickness, overlaps,
                targetMask, QueryTriggerInteraction.Ignore)) == overlaps.Length) Array.Resize(ref overlaps, overlaps.Length * 2);
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i];
                if (collider == null || collider.transform.IsChildOf(transform)) continue;
                CombatHealth health = collider.GetComponentInParent<CombatHealth>();
                if (!CanDamageTarget(health, now)) continue;
                Vector3 contact = collider.ClosestPoint(center);
                Vector3 offset = contact - patch.point;
                float height = Vector3.Dot(offset, patch.normal);
                if (height < -0.005f || height > surfaceLayerThickness) continue;
                Vector3 radial = offset - patch.normal * height;
                if (radial.sqrMagnitude > radius * radius) continue;
                float directionalReach = SurfaceDirectionalReach(patch, radial, now);
                if (radial.sqrMagnitude > directionalReach * directionalReach) continue;
                float angle = Mathf.Atan2(Vector3.Dot(radial, patch.bitangent), Vector3.Dot(radial, patch.tangent));
                float sector = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f) * 8f;
                int a = Mathf.FloorToInt(sector) % 8, b = (a + 1) % 8;
                float limitA = a < 4 ? patch.limitsA[a] : patch.limitsB[a - 4];
                float limitB = b < 4 ? patch.limitsA[b] : patch.limitsB[b - 4];
                if (radial.sqrMagnitude > Mathf.Pow(Mathf.Min(limitA, limitB), 2f)) continue;
                if (!HasLineOfSight(patch.point + patch.normal * SurfaceSkin, contact)) continue;
                DamageTarget(health, contact, radial.sqrMagnitude > 0.001f ? radial.normalized : patch.normal, now, surfaceDamageMultiplier * fade);
            }
        }
    }
}
