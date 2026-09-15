using UnityEngine;

public static class FootstepSurfaceUtility
{
    public static bool TryFindGroundHit(
        Vector3 footPosition,
        LayerMask groundMask,
        float originLift,
        float probeDistance,
        Transform ignoredRoot,
        Rigidbody ignoredRigidbody,
        Collider ignoredCollider,
        RaycastHit[] hits,
        out RaycastHit groundHit)
    {
        groundHit = default;
        if (hits == null || hits.Length == 0)
            return false;

        float safeOriginLift = Mathf.Max(0f, originLift);
        float safeProbeDistance = Mathf.Max(0.05f, probeDistance);
        Vector3 origin = footPosition + Vector3.up * safeOriginLift;
        int mask = groundMask.value == 0 ? Physics.DefaultRaycastLayers : groundMask.value;
        mask = DestructibleDebrisCollision.ExcludeDebrisLayer(mask);

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            hits,
            safeOriginLift + safeProbeDistance,
            mask,
            QueryTriggerInteraction.Ignore);

        bool foundHit = false;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null || IsIgnoredCollider(hitCollider, ignoredRoot, ignoredRigidbody, ignoredCollider))
                continue;

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                groundHit = hit;
                foundHit = true;
            }
        }

        return foundHit;
    }

    private static bool IsIgnoredCollider(Collider candidate, Transform ignoredRoot, Rigidbody ignoredRigidbody, Collider ignoredCollider)
    {
        if (candidate == null)
            return true;

        if (ignoredCollider != null && candidate == ignoredCollider)
            return true;

        if (ignoredRigidbody != null && candidate.attachedRigidbody == ignoredRigidbody)
            return true;

        return ignoredRoot != null && candidate.transform.IsChildOf(ignoredRoot);
    }
}
