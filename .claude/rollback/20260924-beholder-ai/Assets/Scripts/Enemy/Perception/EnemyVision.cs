using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class EnemyVision : MonoBehaviour
{
    private const int RaycastBufferSize = 16;
    private const int ArcSegments = 32;

    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private Transform eyeTransform;

    [Header("Vision")]
    [SerializeField] private bool enableVision = true;
    [SerializeField] [Min(0f)] private float viewDistance = 16f;
    [SerializeField] [Range(1f, 360f)] private float viewAngle = 110f;
    [SerializeField] private LayerMask targetLayers = Physics.DefaultRaycastLayers;
    [SerializeField] private LayerMask obstacleLayers = Physics.DefaultRaycastLayers;
    [SerializeField] [Min(0.02f)] private float detectionUpdateInterval = 0.2f;
    [SerializeField] [Min(0f)] private float eyeHeightFallback = 1.55f;

    [Header("Gizmos")]
    [SerializeField] private bool drawVisionGizmo = true;
    [SerializeField] private Color visionGizmoColor = new Color(1f, 0.85f, 0.05f, 0.9f);

    private readonly RaycastHit[] raycastBuffer = new RaycastHit[RaycastBufferSize];
    private PlayerHealth cachedVisibleTarget;
    private Vector3 cachedVisibleTargetPosition;
    private float nextDetectionTime;
    private bool hasCachedVisibleTarget;

    public bool EnableVision => enableVision;
    public float ViewDistance => Mathf.Max(0f, viewDistance);
    public float ViewAngle => Mathf.Clamp(viewAngle, 1f, 360f);
    public Transform EyeTransform => eyeTransform;
    public Vector3 EyePosition => eyeTransform != null ? eyeTransform.position : transform.position + Vector3.up * Mathf.Max(0f, eyeHeightFallback);
    public Vector3 EyeForward => ResolvePlanarForward();

    private void Awake()
    {
        ResolveReferences();
    }

    public bool TryGetVisibleTarget(PlayerHealth preferredTarget, out PlayerHealth visibleTarget, out Vector3 visiblePosition)
    {
        visibleTarget = cachedVisibleTarget;
        visiblePosition = cachedVisibleTargetPosition;

        if (!enableVision)
        {
            ClearCachedVisibility();
            return false;
        }

        if (Time.time < nextDetectionTime)
            return hasCachedVisibleTarget && IsTargetStillValid(cachedVisibleTarget);

        nextDetectionTime = Time.time + Mathf.Max(0.02f, detectionUpdateInterval);

        if (preferredTarget != null && CanSeeTarget(preferredTarget, out Vector3 preferredPosition))
        {
            CacheVisibleTarget(preferredTarget, preferredPosition);
            visibleTarget = cachedVisibleTarget;
            visiblePosition = cachedVisibleTargetPosition;
            return true;
        }

        EnemyTargetRegistry.PruneInvalidTargets();
        if (EnemyTargetRegistry.Count == 0)
            EnemyTargetRegistry.RefreshFromScene();

        PlayerHealth nearestTarget = null;
        Vector3 nearestPosition = Vector3.zero;
        float nearestDistanceSqr = float.PositiveInfinity;

        for (int i = 0; i < EnemyTargetRegistry.Count; i++)
        {
            PlayerHealth candidate = EnemyTargetRegistry.GetTarget(i);
            if (!CanSeeTarget(candidate, out Vector3 candidatePosition))
                continue;

            float distanceSqr = Vector3.ProjectOnPlane(candidatePosition - EyePosition, Vector3.up).sqrMagnitude;
            if (distanceSqr >= nearestDistanceSqr)
                continue;

            nearestTarget = candidate;
            nearestPosition = candidatePosition;
            nearestDistanceSqr = distanceSqr;
        }

        if (nearestTarget == null)
        {
            ClearCachedVisibility();
            visibleTarget = null;
            visiblePosition = Vector3.zero;
            return false;
        }

        CacheVisibleTarget(nearestTarget, nearestPosition);
        visibleTarget = cachedVisibleTarget;
        visiblePosition = cachedVisibleTargetPosition;
        return true;
    }

    public bool CanSeeTarget(PlayerHealth target, out Vector3 targetPosition)
    {
        targetPosition = Vector3.zero;
        if (!enableVision || !IsTargetStillValid(target) || !IsTargetLayerAllowed(target))
            return false;

        targetPosition = ResolveTargetPosition(target);
        Vector3 eyePosition = EyePosition;
        Vector3 toTarget = targetPosition - eyePosition;
        float distance = toTarget.magnitude;
        if (distance <= 0.0001f || distance > ViewDistance)
            return false;

        Vector3 planarToTarget = Vector3.ProjectOnPlane(toTarget, Vector3.up);
        if (planarToTarget.sqrMagnitude > 0.0001f)
        {
            float angleToTarget = Vector3.Angle(EyeForward, planarToTarget.normalized);
            if (angleToTarget > ViewAngle * 0.5f)
                return false;
        }

        return HasLineOfSight(target, eyePosition, toTarget.normalized, distance);
    }

    public bool CanSenseTargetInRadius(
        PlayerHealth target,
        float radius,
        bool requireLineOfSight,
        out Vector3 targetPosition)
    {
        targetPosition = Vector3.zero;
        float safeRadius = Mathf.Max(0f, radius);
        if (safeRadius <= 0f || !IsTargetStillValid(target) || !IsTargetLayerAllowed(target))
            return false;

        targetPosition = ResolveTargetPosition(target);
        Vector3 planarOffset = Vector3.ProjectOnPlane(targetPosition - transform.position, Vector3.up);
        if (planarOffset.sqrMagnitude > safeRadius * safeRadius)
            return false;

        if (!requireLineOfSight)
            return true;

        Vector3 eyePosition = EyePosition;
        Vector3 toTarget = targetPosition - eyePosition;
        float distance = toTarget.magnitude;
        if (distance <= 0.0001f)
            return true;

        return HasLineOfSight(target, eyePosition, toTarget.normalized, distance);
    }

    private bool HasLineOfSight(PlayerHealth target, Vector3 eyePosition, Vector3 direction, float distance)
    {
        int sightMask = targetLayers.value | obstacleLayers.value;
        int hitCount = Physics.RaycastNonAlloc(
            eyePosition,
            direction,
            raycastBuffer,
            distance,
            sightMask,
            QueryTriggerInteraction.Ignore);

        bool foundRelevantHit = false;
        float closestDistance = float.PositiveInfinity;
        RaycastHit closestHit = default;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = raycastBuffer[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null || hitCollider.transform.IsChildOf(transform))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            closestHit = hit;
            foundRelevantHit = true;
        }

        if (!foundRelevantHit)
            return true;

        if (closestHit.collider.transform.IsChildOf(target.transform))
            return true;

        return !LayerMaskContains(obstacleLayers, closestHit.collider.gameObject.layer);
    }

    private Vector3 ResolveTargetPosition(PlayerHealth target)
    {
        Collider targetCollider = target.GetComponentInChildren<Collider>();
        if (targetCollider != null)
            return targetCollider.bounds.center;

        return target.transform.position + Vector3.up;
    }

    private Vector3 ResolvePlanarForward()
    {
        Transform forwardSource = eyeTransform != null ? eyeTransform : transform;
        Vector3 forward = Vector3.ProjectOnPlane(forwardSource.forward, Vector3.up);
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    private bool IsTargetLayerAllowed(PlayerHealth target)
    {
        if (target == null)
            return false;

        if (LayerMaskContains(targetLayers, target.gameObject.layer))
            return true;

        Collider targetCollider = target.GetComponentInChildren<Collider>();
        return targetCollider != null && LayerMaskContains(targetLayers, targetCollider.gameObject.layer);
    }

    private static bool IsTargetStillValid(PlayerHealth target)
    {
        return target != null && target.CanBeTargetedByEnemy;
    }

    private static bool LayerMaskContains(LayerMask mask, int layer)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    private void CacheVisibleTarget(PlayerHealth target, Vector3 position)
    {
        cachedVisibleTarget = target;
        cachedVisibleTargetPosition = position;
        hasCachedVisibleTarget = true;
    }

    private void ClearCachedVisibility()
    {
        cachedVisibleTarget = null;
        cachedVisibleTargetPosition = Vector3.zero;
        hasCachedVisibleTarget = false;
    }

    private void ResolveReferences()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (eyeTransform == null && enemySetup != null)
        {
            Transform candidateEye = enemySetup.AttackOrigin;
            if (candidateEye != null && candidateEye != transform)
                eyeTransform = candidateEye;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawVisionGizmo || !enableVision)
            return;

        Vector3 origin = EyePosition;
        Vector3 forward = EyeForward;
        float distance = ViewDistance;
        float angle = ViewAngle;
        float halfAngle = angle * 0.5f;
        Vector3 left = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
        Vector3 right = Quaternion.AngleAxis(halfAngle, Vector3.up) * forward;

#if UNITY_EDITOR
        Handles.color = new Color(visionGizmoColor.r, visionGizmoColor.g, visionGizmoColor.b, 0.16f);
        Handles.DrawSolidArc(origin, Vector3.up, left, angle, distance);
        Handles.color = visionGizmoColor;
        Handles.DrawWireArc(origin, Vector3.up, left, angle, distance);
#endif

        Gizmos.color = visionGizmoColor;
        Gizmos.DrawLine(origin, origin + left * distance);
        Gizmos.DrawLine(origin, origin + right * distance);
        Gizmos.DrawLine(origin, origin + forward * distance);

        Vector3 previousPoint = origin + left * distance;
        for (int i = 1; i <= ArcSegments; i++)
        {
            float t = i / (float)ArcSegments;
            Vector3 direction = Quaternion.AngleAxis(Mathf.Lerp(-halfAngle, halfAngle, t), Vector3.up) * forward;
            Vector3 currentPoint = origin + direction * distance;
            Gizmos.DrawLine(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }
}
