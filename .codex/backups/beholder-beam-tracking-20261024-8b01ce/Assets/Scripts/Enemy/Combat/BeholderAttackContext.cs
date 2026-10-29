using System;
using UnityEngine;

public readonly struct BeholderAttackSegment
{
    public readonly Vector3 Start, End;
    public readonly RaycastHit Hit;
    public bool HasHit => Hit.collider != null;
    public BeholderAttackSegment(Vector3 start, Vector3 end, RaycastHit hit) { Start = start; End = end; Hit = hit; }
}

public sealed class BeholderAttackContext : IDisposable
{
    private readonly BeholderAttackController owner;
    private readonly EnemyVision vision;
    private readonly PlayerHealth target;
    private readonly Transform origin;
    private readonly Renderer eyeRenderer;
    private Mesh bakedEye;
    private RaycastHit[] hits = new RaycastHit[16];
    public Vector3 AimPoint { get; set; }
    public BeholderAttackSegment LastSegment { get; private set; }
    public bool HasAuthority => owner.IsAuthority;
    public bool HasOrigin => origin != null;
    public Vector3 OriginPosition
    {
        get
        {
            if (origin == null) return owner.transform.position;
            if (eyeRenderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                if (bakedEye == null) bakedEye = new Mesh { name = "Beholder attack eye origin" };
                skin.BakeMesh(bakedEye, true);
                return origin.TransformPoint(bakedEye.bounds.center);
            }
            return eyeRenderer != null ? eyeRenderer.bounds.center : origin.position;
        }
    }

    public BeholderAttackContext(BeholderAttackController owner, BeholderAttackDefinition settings, EnemyVision vision, PlayerHealth target, Vector3 aimPoint)
    {
        this.owner = owner;
        this.vision = vision;
        this.target = target;
        AimPoint = aimPoint;
        foreach (Transform candidate in owner.GetComponentsInChildren<Transform>(true))
            if (candidate.name == settings.originName) { origin = candidate; break; }
        if (origin != null) eyeRenderer = origin.GetComponent<Renderer>();
    }

    public BeholderAttackSegment Cast(float range, LayerMask mask)
    {
        Vector3 start = OriginPosition;
        Vector3 offset = AimPoint - start;
        float distance = Mathf.Max(0.01f, range);
        if (!HasAuthority || !IsFinite(start) || !IsFinite(offset) || offset.sqrMagnitude <= 0.00000001f)
            return LastSegment = new BeholderAttackSegment(start, start, default);
        Vector3 direction = offset.normalized;
        int count;
        while ((count = Physics.RaycastNonAlloc(start, direction, hits, distance, mask, QueryTriggerInteraction.Ignore)) == hits.Length)
            Array.Resize(ref hits, hits.Length * 2);
        RaycastHit nearest = default;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(owner.transform) || hits[i].distance >= nearestDistance) continue;
            nearest = hits[i]; nearestDistance = hits[i].distance;
        }
        return LastSegment = new BeholderAttackSegment(start, nearest.collider != null ? nearest.point : start + direction * distance, nearest);
    }

    public void FollowVisibleTarget(float degreesPerSecond, float deltaTime)
    {
        if (!HasAuthority || target == null || !target.CanBeTargetedByEnemy || vision == null
            || !vision.CanSeeTarget(target, out Vector3 visible)) return;
        Vector3 start = OriginPosition;
        Vector3 desired = visible - start;
        if (degreesPerSecond <= 0f) { AimPoint = visible; return; }
        Vector3 current = AimPoint - start;
        Vector3 direction = Vector3.RotateTowards(current.normalized, desired.normalized, degreesPerSecond * Mathf.Deg2Rad * Mathf.Max(0f, deltaTime), 0f);
        AimPoint = start + direction * desired.magnitude;
    }

    public void ApplyDamage(BeholderAttackSegment segment, float damage)
    {
        if (!HasAuthority || damage <= 0f || !segment.HasHit) return;
        var victim = segment.Hit.collider.GetComponentInParent<PlayerHealth>();
        if (victim == null || !victim.CanBeTargetedByEnemy || victim.transform.IsChildOf(owner.transform)) return;
        victim.ReceiveDamage(new DamageInfo(damage, owner.gameObject, CombatAlignment.Enemy, segment.Hit.point,
            (segment.End - segment.Start).normalized));
    }

    public void Present(BeholderAttackDefinition definition, BeholderAttackSegment segment) => owner.PresentSegment(definition, segment);
    public void StopBeam() => owner.StopPresentedBeam();
    public void Dispose()
    {
        if (bakedEye == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(bakedEye);
        else UnityEngine.Object.DestroyImmediate(bakedEye);
        bakedEye = null;
    }
    private static bool IsFinite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x)
        || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));
}
