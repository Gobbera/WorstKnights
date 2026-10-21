using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum EnemyTraversalSide
{
    None = 0,
    SideA = 1,
    SideB = 2
}

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
[AddComponentMenu("Enemy/Traversal/Enemy Traversal Volume")]
public sealed class EnemyTraversalVolume : MonoBehaviour
{
    private static readonly HashSet<EnemyTraversalVolume> activeVolumes = new HashSet<EnemyTraversalVolume>();

    private sealed class InteractionBlockContact
    {
        public EnemyTraversalController TraversalController;
        public DoorController DoorController;
        public readonly List<Collider> Colliders = new List<Collider>(4);
    }

    [Header("Route")]
    [SerializeField] private Transform sideA;
    [SerializeField] private Transform sideB;
    [SerializeField] private Transform[] midpoints = Array.Empty<Transform>();

    [Header("Motion")]
    [SerializeField] [Min(0f)] private float traversalSpeed = 3f;
    [SerializeField] [Min(0f)] private float entryPauseDuration = 0.05f;
    [SerializeField] [Min(0f)] private float exitPauseDuration;
    [SerializeField] private bool alignToPathDirection = true;
    [SerializeField] [Min(0f)] private float cooldownAfterTraversal = 2f;

    [Header("Door")]
    [SerializeField] private DoorController doorController;
    [SerializeField] private bool gateTraversalByDoorState;
    [SerializeField] private bool autoResolveDoorController = true;
    [SerializeField] [Min(0f)] private float doorSearchRadius = 3f;
    [SerializeField] private bool reorientOpenDoorWhenNeeded = true;
    [SerializeField] private bool reorientOnlyWhenDoorObstructsTraversal = true;
    [SerializeField] [Min(0.01f)] private float doorObstructionProbeRadius = 0.7f;
    [SerializeField] [Min(0.05f)] private float doorObstructionProbeSpacing = 0.35f;
    [SerializeField] [Min(0f)] private float doorReadyTimeout = 4f;
    [SerializeField] [Min(0f)] private float doorSettleDelay = 0.05f;
    [SerializeField] private bool blockPlayerInteractionWhileEnemyInside = true;
    [SerializeField] private bool requireEnemyDoorPermissionForPlayerBlock = true;

    [Header("Retrigger Guard")]
    [SerializeField] private bool requireDestinationAcrossSides = true;
    [SerializeField] [Min(0f)] private float sideClassificationTolerance = 0.15f;
    [SerializeField] [Min(0f)] private float rearmDistancePastExit = 0.75f;

    [Header("Search Areas")]
    [SerializeField] private EnemySearchArea sideASearchArea;
    [SerializeField] private EnemySearchArea sideBSearchArea;

    [Header("Debug")]
    [SerializeField] private bool drawTraversalGizmos = true;
    [SerializeField] private Color routeColor = new Color(0.25f, 0.85f, 1f, 0.9f);
    [SerializeField] private Color sideAColor = new Color(0.25f, 1f, 0.35f, 0.9f);
    [SerializeField] private Color sideBColor = new Color(1f, 0.55f, 0.1f, 0.9f);
    private readonly List<InteractionBlockContact> interactionBlockContacts = new List<InteractionBlockContact>(4);

    public float TraversalSpeed => Mathf.Max(0f, traversalSpeed);
    public float EntryPauseDuration => Mathf.Max(0f, entryPauseDuration);
    public float ExitPauseDuration => Mathf.Max(0f, exitPauseDuration);
    public float CooldownAfterTraversal => Mathf.Max(0f, cooldownAfterTraversal);
    public bool AlignToPathDirection => alignToPathDirection;
    public DoorController DoorController => ResolveDoorController();
    public bool GateTraversalByDoorState => gateTraversalByDoorState || (autoResolveDoorController && ResolveDoorController() != null);
    public float DoorSearchRadius => Mathf.Max(0f, doorSearchRadius);
    public bool ReorientOpenDoorWhenNeeded => reorientOpenDoorWhenNeeded;
    public bool ReorientOnlyWhenDoorObstructsTraversal => reorientOnlyWhenDoorObstructsTraversal;
    public float DoorObstructionProbeRadius => Mathf.Max(0.01f, doorObstructionProbeRadius);
    public float DoorObstructionProbeSpacing => Mathf.Max(0.05f, doorObstructionProbeSpacing);
    public float DoorReadyTimeout => Mathf.Max(0f, doorReadyTimeout);
    public float DoorSettleDelay => Mathf.Max(0f, doorSettleDelay);
    public bool RequireDestinationAcrossSides => requireDestinationAcrossSides;
    public float SideClassificationTolerance => Mathf.Max(0f, sideClassificationTolerance);
    public float RearmDistancePastExit => Mathf.Max(0f, rearmDistancePastExit);
    public static IReadOnlyCollection<EnemyTraversalVolume> ActiveVolumes => activeVolumes;

    public bool TryBuildRoute(Vector3 actorPosition, List<Vector3> route)
    {
        return TryBuildRoute(actorPosition, route, out _, out _);
    }

    public bool TryBuildRoute(
        Vector3 actorPosition,
        List<Vector3> route,
        out EnemyTraversalSide entrySide,
        out EnemyTraversalSide exitSide)
    {
        entrySide = ResolveNearestSide(actorPosition);
        return TryBuildRoute(entrySide, route, out exitSide);
    }

    public bool TryBuildRoute(EnemyTraversalSide entrySide, List<Vector3> route, out EnemyTraversalSide exitSide)
    {
        exitSide = EnemyTraversalSide.None;
        if (route == null)
            return false;

        route.Clear();
        if (sideA == null || sideB == null || entrySide == EnemyTraversalSide.None)
            return false;

        Vector3 sideAPosition = sideA.position;
        Vector3 sideBPosition = sideB.position;

        if (entrySide == EnemyTraversalSide.SideA)
        {
            exitSide = EnemyTraversalSide.SideB;
            AddPoint(route, sideAPosition);
            AddMidpoints(route, forward: true);
            AddPoint(route, sideBPosition);
        }
        else
        {
            exitSide = EnemyTraversalSide.SideA;
            AddPoint(route, sideBPosition);
            AddMidpoints(route, forward: false);
            AddPoint(route, sideAPosition);
        }

        return route.Count >= 2;
    }

    public bool TryBuildRoute(
        EnemyTraversalSide entrySide,
        Vector3 actorPosition,
        List<Vector3> route,
        out EnemyTraversalSide exitSide)
    {
        if (!TryBuildRoute(entrySide, route, out exitSide))
            return false;

        Vector3 firstSegment = Vector3.ProjectOnPlane(route[1] - route[0], Vector3.up);
        float segmentLength = firstSegment.magnitude;
        if (segmentLength > 0.001f)
        {
            Vector3 direction = firstSegment / segmentLength;
            Vector3 entryToActor = Vector3.ProjectOnPlane(actorPosition - route[0], Vector3.up);
            float progress = Mathf.Clamp(Vector3.Dot(entryToActor, direction), 0f, segmentLength);
            route[0] += direction * progress;
        }

        return true;
    }

    public bool IsDestinationAcrossSides(Vector3 actorPosition, Vector3 destination)
    {
        EnemyTraversalSide actorSide = ResolveSide(actorPosition);
        EnemyTraversalSide destinationSide = ResolveSide(destination);
        return actorSide != EnemyTraversalSide.None
            && destinationSide != EnemyTraversalSide.None
            && actorSide != destinationSide;
    }

    public EnemyTraversalSide ResolveSide(Vector3 position)
    {
        if (!TryResolveSidePositions(out Vector3 sideAPosition, out Vector3 sideBPosition))
            return EnemyTraversalSide.None;

        Vector3 sideAxis = Vector3.ProjectOnPlane(sideBPosition - sideAPosition, Vector3.up);
        float axisLength = sideAxis.magnitude;
        if (axisLength <= 0.001f)
            return ResolveNearestSide(position);

        Vector3 center = (sideAPosition + sideBPosition) * 0.5f;
        float signedDistance = Vector3.Dot(
            Vector3.ProjectOnPlane(position - center, Vector3.up),
            sideAxis / axisLength);
        float tolerance = SideClassificationTolerance;

        if (signedDistance > tolerance)
            return EnemyTraversalSide.SideB;

        if (signedDistance < -tolerance)
            return EnemyTraversalSide.SideA;

        return ResolveNearestSide(position);
    }

    public EnemyTraversalSide ResolveNearestSide(Vector3 position)
    {
        if (!TryResolveSidePositions(out Vector3 sideAPosition, out Vector3 sideBPosition))
            return EnemyTraversalSide.None;

        float sideADistanceSqr = Vector3.ProjectOnPlane(sideAPosition - position, Vector3.up).sqrMagnitude;
        float sideBDistanceSqr = Vector3.ProjectOnPlane(sideBPosition - position, Vector3.up).sqrMagnitude;
        return sideADistanceSqr <= sideBDistanceSqr ? EnemyTraversalSide.SideA : EnemyTraversalSide.SideB;
    }

    public bool TryGetSidePosition(EnemyTraversalSide side, out Vector3 position)
    {
        Transform point = side == EnemyTraversalSide.SideA ? sideA
            : side == EnemyTraversalSide.SideB ? sideB : null;
        position = point != null ? point.position : Vector3.zero;
        return point != null;
    }

    public EnemySearchArea GetSearchArea(EnemyTraversalSide side)
    {
        return side == EnemyTraversalSide.SideA ? sideASearchArea
            : side == EnemyTraversalSide.SideB ? sideBSearchArea : null;
    }

    public bool IsWithinApproachDistance(Vector3 position, float distance)
    {
        BoxCollider triggerCollider = GetComponent<BoxCollider>();
        if (triggerCollider == null || !triggerCollider.enabled)
            return false;

        Vector3 closestPoint = triggerCollider.ClosestPoint(position);
        return (closestPoint - position).sqrMagnitude <= distance * distance;
    }

    public bool HasPassedSide(Vector3 position, EnemyTraversalSide side)
    {
        return HasPassedSide(position, side, RearmDistancePastExit);
    }

    public bool HasPassedSide(Vector3 position, EnemyTraversalSide side, float distancePastSide)
    {
        if (side == EnemyTraversalSide.None || !TryResolveSidePositions(out Vector3 sideAPosition, out Vector3 sideBPosition))
            return false;

        Vector3 sideAxis = Vector3.ProjectOnPlane(sideBPosition - sideAPosition, Vector3.up);
        float axisLength = sideAxis.magnitude;
        if (axisLength <= 0.001f)
            return false;

        Vector3 axisDirection = sideAxis / axisLength;
        Vector3 sidePosition = side == EnemyTraversalSide.SideA ? sideAPosition : sideBPosition;
        Vector3 sideToPosition = Vector3.ProjectOnPlane(position - sidePosition, Vector3.up);
        float signedDistancePastSide = Vector3.Dot(sideToPosition, axisDirection);

        if (side == EnemyTraversalSide.SideA)
            signedDistancePastSide = -signedDistancePastSide;

        return signedDistancePastSide >= Mathf.Max(0f, distancePastSide);
    }

    public bool ContainsPoint(Vector3 worldPosition)
    {
        BoxCollider triggerCollider = GetComponent<BoxCollider>();
        if (triggerCollider == null || !triggerCollider.enabled)
            return false;

        Vector3 localPosition = triggerCollider.transform.InverseTransformPoint(worldPosition) - triggerCollider.center;
        Vector3 halfSize = triggerCollider.size * 0.5f;
        return Mathf.Abs(localPosition.x) <= halfSize.x
            && Mathf.Abs(localPosition.y) <= halfSize.y
            && Mathf.Abs(localPosition.z) <= halfSize.z;
    }

    public bool TryResolveDoorController(out DoorController resolvedDoorController)
    {
        resolvedDoorController = ResolveDoorController();
        return resolvedDoorController != null;
    }

    public bool ShouldReorientDoorForTraversal(DoorController resolvedDoorController, Vector3 actorPosition)
    {
        if (!reorientOpenDoorWhenNeeded
            || resolvedDoorController == null
            || !resolvedDoorController.IsOpen
            || !resolvedDoorController.WouldChangeOpenDirectionFromWorldPosition(actorPosition))
        {
            return false;
        }

        return !reorientOnlyWhenDoorObstructsTraversal || IsDoorObstructingTraversalRoute(resolvedDoorController);
    }

    private DoorController ResolveDoorController()
    {
        if (doorController != null)
            return doorController;

        if (!autoResolveDoorController || DoorSearchRadius <= 0f)
            return null;

        doorController = FindNearestDoorController();
        return doorController;
    }

    private DoorController FindNearestDoorController()
    {
        DoorController[] doorControllers = Resources.FindObjectsOfTypeAll<DoorController>();
        DoorController nearestDoorController = null;
        float nearestDistanceSqr = DoorSearchRadius * DoorSearchRadius;
        Vector3 volumePosition = transform.position;
        var volumeScene = gameObject.scene;

        for (int i = 0; i < doorControllers.Length; i++)
        {
            DoorController candidate = doorControllers[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
                continue;

            if (candidate.gameObject.scene != volumeScene)
                continue;

            float distanceSqr = Vector3.ProjectOnPlane(candidate.transform.position - volumePosition, Vector3.up).sqrMagnitude;
            if (distanceSqr > nearestDistanceSqr)
                continue;

            nearestDistanceSqr = distanceSqr;
            nearestDoorController = candidate;
        }

        return nearestDoorController;
    }

    public bool IsDoorObstructingTraversalRoute(DoorController resolvedDoorController)
    {
        if (resolvedDoorController == null)
            return false;

        Transform colliderRoot = resolvedDoorController.MovingPart != null
            ? resolvedDoorController.MovingPart
            : resolvedDoorController.transform;
        Collider[] doorColliders = colliderRoot.GetComponentsInChildren<Collider>(true);
        if ((doorColliders == null || doorColliders.Length == 0) && colliderRoot != resolvedDoorController.transform)
            doorColliders = resolvedDoorController.GetComponentsInChildren<Collider>(true);

        if (doorColliders == null || doorColliders.Length == 0)
            return true;

        Vector3 previous = sideA != null ? sideA.position : transform.position;
        if (midpoints != null)
        {
            for (int i = 0; i < midpoints.Length; i++)
            {
                Transform midpoint = midpoints[i];
                if (midpoint == null)
                    continue;

                if (IsAnyDoorColliderCloseToSegment(doorColliders, previous, midpoint.position))
                    return true;

                previous = midpoint.position;
            }
        }

        Vector3 end = sideB != null ? sideB.position : transform.position;
        return IsAnyDoorColliderCloseToSegment(doorColliders, previous, end);
    }

    public bool IsDoorObstructingTraversalRoute(
        DoorController resolvedDoorController,
        Vector3 actorPosition,
        IReadOnlyList<Vector3> route,
        float probeRadius)
    {
        if (resolvedDoorController == null || route == null || route.Count == 0)
            return false;

        Transform colliderRoot = resolvedDoorController.MovingPart != null
            ? resolvedDoorController.MovingPart
            : resolvedDoorController.transform;
        Collider[] doorColliders = colliderRoot.GetComponentsInChildren<Collider>(true);
        if ((doorColliders == null || doorColliders.Length == 0) && colliderRoot != resolvedDoorController.transform)
            doorColliders = resolvedDoorController.GetComponentsInChildren<Collider>(true);

        if (doorColliders == null || doorColliders.Length == 0)
            return false;

        int firstPoint = 0;
        if (route.Count > 1)
        {
            Vector3 entryDirection = Vector3.ProjectOnPlane(route[1] - route[0], Vector3.up);
            Vector3 entryToActor = Vector3.ProjectOnPlane(actorPosition - route[0], Vector3.up);
            if (Vector3.Dot(entryToActor, entryDirection) > 0f)
                firstPoint = 1;
        }

        Vector3 previous = actorPosition;
        previous.y = route[firstPoint].y;
        float radius = Mathf.Max(0.01f, probeRadius);
        for (int i = firstPoint; i < route.Count; i++)
        {
            if (IsAnyDoorColliderCloseToSegment(doorColliders, previous, route[i], radius))
                return true;

            previous = route[i];
        }

        return false;
    }

    private bool IsAnyDoorColliderCloseToSegment(
        Collider[] doorColliders, Vector3 segmentStart, Vector3 segmentEnd, float probeRadius = -1f)
    {
        for (int i = 0; i < doorColliders.Length; i++)
        {
            Collider doorCollider = doorColliders[i];
            if (doorCollider == null || !doorCollider.enabled || doorCollider.isTrigger)
                continue;

            if (IsColliderCloseToSegment(doorCollider, segmentStart, segmentEnd, probeRadius))
                return true;
        }

        return false;
    }

    private bool IsColliderCloseToSegment(
        Collider doorCollider, Vector3 segmentStart, Vector3 segmentEnd, float probeRadius)
    {
        Vector3 planarSegment = Vector3.ProjectOnPlane(segmentEnd - segmentStart, Vector3.up);
        float segmentLength = planarSegment.magnitude;
        if (segmentLength <= 0.001f)
            return IsColliderCloseToPoint(doorCollider, segmentStart, probeRadius);

        int stepCount = Mathf.Max(1, Mathf.CeilToInt(segmentLength / DoorObstructionProbeSpacing));
        for (int i = 0; i <= stepCount; i++)
        {
            float t = i / (float)stepCount;
            Vector3 samplePoint = Vector3.Lerp(segmentStart, segmentEnd, t);
            if (IsColliderCloseToPoint(doorCollider, samplePoint, probeRadius))
                return true;
        }

        return false;
    }

    private bool IsColliderCloseToPoint(Collider doorCollider, Vector3 samplePoint, float probeRadius)
    {
        float radius = probeRadius > 0f ? probeRadius : DoorObstructionProbeRadius;
        Bounds bounds = doorCollider.bounds;
        if (samplePoint.y < bounds.min.y - radius
            || samplePoint.y > bounds.max.y + radius)
        {
            return false;
        }

        Vector3 closestPoint = doorCollider.ClosestPoint(samplePoint);
        Vector3 planarOffset = Vector3.ProjectOnPlane(closestPoint - samplePoint, Vector3.up);
        return planarOffset.sqrMagnitude <= radius * radius;
    }

    private void AddMidpoints(List<Vector3> route, bool forward)
    {
        if (midpoints == null || midpoints.Length == 0)
            return;

        if (forward)
        {
            for (int i = 0; i < midpoints.Length; i++)
                AddTransformPoint(route, midpoints[i]);
            return;
        }

        for (int i = midpoints.Length - 1; i >= 0; i--)
            AddTransformPoint(route, midpoints[i]);
    }

    private static void AddTransformPoint(List<Vector3> route, Transform point)
    {
        if (point != null)
            AddPoint(route, point.position);
    }

    private static void AddPoint(List<Vector3> route, Vector3 point)
    {
        if (route.Count > 0 && Vector3.Distance(route[route.Count - 1], point) <= 0.01f)
            return;

        route.Add(point);
    }

    private bool TryResolveSidePositions(out Vector3 sideAPosition, out Vector3 sideBPosition)
    {
        sideAPosition = sideA != null ? sideA.position : Vector3.zero;
        sideBPosition = sideB != null ? sideB.position : Vector3.zero;
        return sideA != null && sideB != null;
    }

    private void LateUpdate()
    {
        CleanupInteractionBlockContacts();
    }

    private void OnEnable()
    {
        activeVolumes.Add(this);
    }

    private void OnDisable()
    {
        activeVolumes.Remove(this);
        ClearInteractionBlockContacts();
    }

    private void OnTriggerEnter(Collider other)
    {
        RegisterInteractionBlockContact(other);
    }

    private void OnTriggerStay(Collider other)
    {
        RegisterInteractionBlockContact(other);
    }

    private void OnTriggerExit(Collider other)
    {
        UnregisterInteractionBlockContact(other);
    }

    private void RegisterInteractionBlockContact(Collider other)
    {
        if (!blockPlayerInteractionWhileEnemyInside || other == null)
            return;

        DoorController resolvedDoorController = ResolveDoorController();
        if (resolvedDoorController == null)
            return;

        EnemyTraversalController traversalController = other.GetComponentInParent<EnemyTraversalController>();
        if (!CanUseInteractionBlockContact(traversalController))
            return;

        InteractionBlockContact contact = FindInteractionBlockContact(traversalController);
        if (contact == null)
        {
            contact = new InteractionBlockContact
            {
                TraversalController = traversalController,
                DoorController = resolvedDoorController
            };
            interactionBlockContacts.Add(contact);
            resolvedDoorController.RegisterEnemyTraversalInteractionBlocker(traversalController);
        }

        if (!contact.Colliders.Contains(other))
            contact.Colliders.Add(other);
    }

    private void UnregisterInteractionBlockContact(Collider other)
    {
        if (other == null)
            return;

        for (int i = interactionBlockContacts.Count - 1; i >= 0; i--)
        {
            InteractionBlockContact contact = interactionBlockContacts[i];
            if (contact == null)
            {
                interactionBlockContacts.RemoveAt(i);
                continue;
            }

            contact.Colliders.Remove(other);
            if (contact.Colliders.Count > 0)
                continue;

            contact.DoorController?.UnregisterEnemyTraversalInteractionBlocker(contact.TraversalController);
            interactionBlockContacts.RemoveAt(i);
        }
    }

    private void CleanupInteractionBlockContacts()
    {
        for (int i = interactionBlockContacts.Count - 1; i >= 0; i--)
        {
            InteractionBlockContact contact = interactionBlockContacts[i];
            if (contact == null)
            {
                interactionBlockContacts.RemoveAt(i);
                continue;
            }

            if (!CanUseInteractionBlockContact(contact.TraversalController)
                || contact.DoorController == null)
            {
                contact.DoorController?.UnregisterEnemyTraversalInteractionBlocker(contact.TraversalController);
                interactionBlockContacts.RemoveAt(i);
                continue;
            }

            for (int colliderIndex = contact.Colliders.Count - 1; colliderIndex >= 0; colliderIndex--)
            {
                Collider contactCollider = contact.Colliders[colliderIndex];
                if (contactCollider == null || !contactCollider.enabled || !contactCollider.gameObject.activeInHierarchy)
                    contact.Colliders.RemoveAt(colliderIndex);
            }

            if (contact.Colliders.Count > 0)
                continue;

            contact.DoorController.UnregisterEnemyTraversalInteractionBlocker(contact.TraversalController);
            interactionBlockContacts.RemoveAt(i);
        }
    }

    private void ClearInteractionBlockContacts()
    {
        for (int i = 0; i < interactionBlockContacts.Count; i++)
        {
            InteractionBlockContact contact = interactionBlockContacts[i];
            if (contact != null)
                contact.DoorController?.UnregisterEnemyTraversalInteractionBlocker(contact.TraversalController);
        }

        interactionBlockContacts.Clear();
    }

    private InteractionBlockContact FindInteractionBlockContact(EnemyTraversalController traversalController)
    {
        if (traversalController == null)
            return null;

        for (int i = 0; i < interactionBlockContacts.Count; i++)
        {
            InteractionBlockContact contact = interactionBlockContacts[i];
            if (contact != null && contact.TraversalController == traversalController)
                return contact;
        }

        return null;
    }

    private bool CanUseInteractionBlockContact(EnemyTraversalController traversalController)
    {
        if (!blockPlayerInteractionWhileEnemyInside
            || traversalController == null
            || !traversalController.isActiveAndEnabled)
        {
            return false;
        }

        return !requireEnemyDoorPermissionForPlayerBlock || traversalController.CanOpenDoors;
    }

    private void Reset()
    {
        Collider triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
            triggerCollider.isTrigger = true;

        if (doorController == null)
            doorController = GetComponentInParent<DoorController>();

        gateTraversalByDoorState = doorController != null || autoResolveDoorController;
    }

    private void OnValidate()
    {
        traversalSpeed = Mathf.Max(0f, traversalSpeed);
        entryPauseDuration = Mathf.Max(0f, entryPauseDuration);
        exitPauseDuration = Mathf.Max(0f, exitPauseDuration);
        cooldownAfterTraversal = Mathf.Max(0f, cooldownAfterTraversal);
        doorSearchRadius = Mathf.Max(0f, doorSearchRadius);
        doorObstructionProbeRadius = Mathf.Max(0.01f, doorObstructionProbeRadius);
        doorObstructionProbeSpacing = Mathf.Max(0.05f, doorObstructionProbeSpacing);
        doorReadyTimeout = Mathf.Max(0f, doorReadyTimeout);
        doorSettleDelay = Mathf.Max(0f, doorSettleDelay);
        sideClassificationTolerance = Mathf.Max(0f, sideClassificationTolerance);
        rearmDistancePastExit = Mathf.Max(0f, rearmDistancePastExit);

        Collider triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
            triggerCollider.isTrigger = true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawTraversalGizmos)
            return;

        DrawPoint(sideA, sideAColor);
        DrawPoint(sideB, sideBColor);

        if (sideA == null || sideB == null)
            return;

        Gizmos.color = routeColor;
        Vector3 previous = sideA.position;
        if (midpoints != null)
        {
            for (int i = 0; i < midpoints.Length; i++)
            {
                Transform midpoint = midpoints[i];
                if (midpoint == null)
                    continue;

                Gizmos.DrawLine(previous, midpoint.position);
                previous = midpoint.position;
            }
        }

        Gizmos.DrawLine(previous, sideB.position);
    }

    private static void DrawPoint(Transform point, Color color)
    {
        if (point == null)
            return;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(point.position, 0.22f);
#if UNITY_EDITOR
        Handles.color = color;
        Handles.Label(point.position + Vector3.up * 0.35f, point.name);
#endif
    }
}
