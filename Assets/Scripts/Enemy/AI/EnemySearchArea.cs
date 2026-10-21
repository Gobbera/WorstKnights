using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[AddComponentMenu("Enemy/AI/Enemy Search Area")]
public sealed class EnemySearchArea : MonoBehaviour
{
    private static readonly HashSet<EnemySearchArea> activeAreas = new HashSet<EnemySearchArea>();

    [SerializeField] private Transform[] pointsOfInterest = Array.Empty<Transform>();
    [SerializeField] private BoxCollider areaBounds;
    [SerializeField] private BoxCollider[] additionalAreaBounds = Array.Empty<BoxCollider>();

    private readonly List<BoxCollider> bounds = new List<BoxCollider>();

    public int PointCount => pointsOfInterest != null && pointsOfInterest.Length > 0
        ? pointsOfInterest.Length : transform.childCount;

    public bool TryGetPoint(int index, out Vector3 position)
    {
        Transform point = pointsOfInterest != null && pointsOfInterest.Length > 0
            ? index >= 0 && index < pointsOfInterest.Length ? pointsOfInterest[index] : null
            : index >= 0 && index < transform.childCount ? transform.GetChild(index) : null;
        position = point != null ? point.position : Vector3.zero;
        return point != null;
    }

    public bool ContainsPoint(Vector3 position)
    {
        bool hasEnabledBounds = false;
        foreach (BoxCollider box in bounds)
        {
            if (box == null || !box.enabled || !box.gameObject.activeInHierarchy)
                continue;

            hasEnabledBounds = true;
            if ((box.ClosestPoint(position) - position).sqrMagnitude <= 0.01f)
                return true;
        }

        return !hasEnabledBounds;
    }

    public static EnemySearchArea FindContaining(Vector3 position, Scene scene)
    {
        EnemySearchArea bestArea = null;
        float bestDistanceSqr = float.PositiveInfinity;
        foreach (EnemySearchArea area in activeAreas)
        {
            if (area == null || !area.isActiveAndEnabled || area.gameObject.scene != scene)
                continue;

            if (!area.HasEnabledBounds() || !area.ContainsPoint(position))
                continue;

            float distanceSqr = area.DistanceToNearestPointSqr(position);
            if (bestArea != null && distanceSqr >= bestDistanceSqr)
                continue;

            bestArea = area;
            bestDistanceSqr = distanceSqr;
        }

        return bestArea;
    }

    public static EnemySearchArea FindByAnchor(Vector3 anchor, Scene scene)
    {
        foreach (EnemySearchArea area in activeAreas)
        {
            if (area != null && area.isActiveAndEnabled && area.gameObject.scene == scene
                && (area.transform.position - anchor).sqrMagnitude < 0.01f)
                return area;
        }

        return null;
    }

    public static EnemySearchArea FindForPosition(Vector3 position, Scene scene, float maxPointDistance)
    {
        EnemySearchArea bestArea = null;
        bool bestContainsPosition = false;
        float bestDistanceSqr = float.PositiveInfinity;

        foreach (EnemySearchArea area in activeAreas)
        {
            if (area == null || !area.isActiveAndEnabled || area.gameObject.scene != scene || area.PointCount == 0)
                continue;

            bool containsPosition = area.HasEnabledBounds() && area.ContainsPoint(position);
            float distanceSqr = area.DistanceToNearestPointSqr(position);
            if (!containsPosition && distanceSqr > maxPointDistance * maxPointDistance)
                continue;

            if (bestArea != null && (bestContainsPosition && !containsPosition
                || bestContainsPosition == containsPosition && distanceSqr >= bestDistanceSqr))
                continue;

            bestArea = area;
            bestContainsPosition = containsPosition;
            bestDistanceSqr = distanceSqr;
        }

        return bestArea;
    }

    private float DistanceToNearestPointSqr(Vector3 position)
    {
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < PointCount; i++)
        {
            if (!TryGetPoint(i, out Vector3 point))
                continue;

            Vector3 offset = Vector3.ProjectOnPlane(point - position, Vector3.up);
            nearest = Mathf.Min(nearest, offset.sqrMagnitude);
        }

        return nearest;
    }

    private bool HasEnabledBounds()
    {
        foreach (BoxCollider box in bounds)
        {
            if (box != null && box.enabled && box.gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    private void RefreshBounds()
    {
        bounds.Clear();
        GetComponents(bounds);
        if (areaBounds != null && !bounds.Contains(areaBounds))
            bounds.Add(areaBounds);

        if (additionalAreaBounds == null)
            return;

        foreach (BoxCollider box in additionalAreaBounds)
        {
            if (box != null && !bounds.Contains(box))
                bounds.Add(box);
        }
    }

    private void OnEnable()
    {
        RefreshBounds();
        activeAreas.Add(this);
    }

    private void OnValidate()
    {
        RefreshBounds();
    }

    private void OnDisable()
    {
        activeAreas.Remove(this);
    }
}
