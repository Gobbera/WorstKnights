using System.Collections.Generic;
using UnityEngine;

public sealed partial class InteractionTriggerSystem
{
    private Collider[] overlaps = new Collider[16];
    private readonly List<GameObject> occupants = new List<GameObject>(4);
    public int OccupantCount => occupants.Count;

    private void FindOccupants()
    {
        occupants.Clear();
        if (mechanism == null || !mechanism.isActiveAndEnabled || mechanism.PressureSurface == null || !mechanism.PressureSurface.enabled || !mechanism.gameObject.activeInHierarchy) return;
        mechanism.GetDetectionBox(detectionHeight, footTolerance, out Vector3 center, out Vector3 halfSize, out float surface);
        int count;
        while ((count = Physics.OverlapBoxNonAlloc(center, halfSize, overlaps, mechanism.transform.rotation, occupancyMask, QueryTriggerInteraction.Ignore)) == overlaps.Length)
            System.Array.Resize(ref overlaps, overlaps.Length * 2);
        for (int i = 0; i < count; i++)
        {
            var collider = overlaps[i];
            if (collider == null || collider.transform.IsChildOf(transform)) continue;
            float feet = collider.bounds.min.y;
            if (feet < surface - mechanism.PressDistance - footTolerance || feet > surface + detectionHeight) continue;
            var actor = ResolveOccupant(collider);
            if (actor != null && !occupants.Contains(actor)) occupants.Add(actor);
        }
    }
    private GameObject ResolveOccupant(Collider collider)
    {
        var player = collider.GetComponentInParent<PlayerHealth>();
        var enemy = collider.GetComponentInParent<EnemyHealth>();
        // Exclude level geometry and actor colliders from the generic physics-object category.
        bool alivePlayer = player != null && player.IsAlive;
        bool aliveEnemy = enemy != null && enemy.IsAlive;
        Rigidbody body = collider.attachedRigidbody;
        bool physicsObject = player == null && enemy == null && body != null && !body.isKinematic;
        switch (pressureFilter)
        {
            case PressureTriggerFilter.Players: return alivePlayer ? player.gameObject : null;
            case PressureTriggerFilter.Enemies: return aliveEnemy ? enemy.gameObject : null;
            case PressureTriggerFilter.Objects: return physicsObject ? body.gameObject : null;
            case PressureTriggerFilter.SpecificObject:
                return specificObject != null && (collider.gameObject == specificObject || collider.transform.IsChildOf(specificObject.transform)) ? specificObject : null;
            default:
                return alivePlayer ? player.gameObject : aliveEnemy ? enemy.gameObject : physicsObject ? body.gameObject : null;
        }
    }
    private void OnDrawGizmosSelected()
    {
        var visual = mechanism != null ? mechanism : GetComponentInChildren<InteractionTriggerMechanism>(true);
        if (triggerType != InteractionTriggerType.PressureButton || visual == null || visual.PressureSurface == null) return;
        visual.GetDetectionBox(detectionHeight, footTolerance, out Vector3 center, out Vector3 halfSize, out _);
        Gizmos.color = IsActive ? Color.yellow : Color.cyan;
        Gizmos.matrix = Matrix4x4.TRS(center, visual.transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, halfSize * 2f);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
