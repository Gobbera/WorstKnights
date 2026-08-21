using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("World/Teleporters/Teleport Volume Controller")]
public class TeleportVolumeController : MonoBehaviour
{
    public enum TeleportRouteMode
    {
        OneWay = 0,
        LinkedTwoWay = 1
    }

    public enum TeleportActivationMode
    {
        Instant = 0,
        TimedStay = 1
    }

    private enum TeleportTargetKind
    {
        Player = 0,
        Enemy = 1,
        Item = 2,
        Other = 3
    }

    private sealed class TeleportableTarget
    {
        public int Key;
        public TeleportTargetKind Kind;
        public Transform Root;
        public Rigidbody Rigidbody;
        public PhotonView PhotonView;
        public PlayerHealth PlayerHealth;
        public EnemySetup EnemySetup;
        public WorldPickupItem PickupItem;
    }

    private sealed class TrackedTargetState
    {
        public TeleportableTarget Target;
        public int OverlapCount;
        public float EnteredTime;
        public bool IgnoreUntilExit;
    }

    [Header("Identity")]
    [SerializeField] private string teleporterName = "Teleporter";
    [Header("Route")]
    [SerializeField] private TeleportRouteMode routeMode = TeleportRouteMode.OneWay;
    [SerializeField] private Transform destinationPoint;
    [SerializeField] private TeleportVolumeController linkedReturnTeleporter;
    [Header("Activation")]
    [SerializeField] private TeleportActivationMode activationMode = TeleportActivationMode.Instant;
    [SerializeField] [Min(0f)] private float requiredStayDuration = 0.5f;
    [SerializeField] [Min(0.01f)] private float sourceReentryBlockDuration = 0.1f;
    [Header("Targets")]
    [SerializeField] private bool allowPlayers = true;
    [SerializeField] private bool allowEnemies;
    [SerializeField] private bool allowItems;
    [SerializeField] private bool allowOtherObjects;
    [SerializeField] private LayerMask detectionMask = Physics.DefaultRaycastLayers;

    private readonly Dictionary<int, TrackedTargetState> trackedTargets = new Dictionary<int, TrackedTargetState>();
    private readonly Dictionary<int, float> transientBlockedKeys = new Dictionary<int, float>();
    private readonly List<int> pendingRemovalKeys = new List<int>();
    private readonly List<int> expiredBlockKeys = new List<int>();

    private Collider triggerCollider;

    public string DisplayName => string.IsNullOrWhiteSpace(teleporterName) ? gameObject.name : teleporterName;

    private void Reset()
    {
        EnsureTriggerCollider();
    }

    private void Awake()
    {
        EnsureTriggerCollider();
    }

    private void OnValidate()
    {
        requiredStayDuration = Mathf.Max(0f, requiredStayDuration);
        sourceReentryBlockDuration = Mathf.Max(0.01f, sourceReentryBlockDuration);

        if (linkedReturnTeleporter == this)
            linkedReturnTeleporter = null;

        EnsureTriggerCollider();
    }

    private void FixedUpdate()
    {
        CleanupExpiredTransientBlocks();
        if (trackedTargets.Count == 0)
            return;

        pendingRemovalKeys.Clear();
        foreach (KeyValuePair<int, TrackedTargetState> trackedEntry in trackedTargets)
        {
            TrackedTargetState trackedState = trackedEntry.Value;
            TeleportableTarget target = trackedState != null ? trackedState.Target : null;
            if (trackedState == null
                || target == null
                || !IsTargetStillValid(target)
                || trackedState.OverlapCount <= 0
                || !IsTargetStillOverlappingVolume(target))
            {
                pendingRemovalKeys.Add(trackedEntry.Key);
                continue;
            }

            if (trackedState.IgnoreUntilExit)
                continue;

            if (!CanTeleportNow(trackedState))
                continue;

            TeleportTarget(target);
            pendingRemovalKeys.Add(trackedEntry.Key);
        }

        for (int i = 0; i < pendingRemovalKeys.Count; i++)
            trackedTargets.Remove(pendingRemovalKeys[i]);
    }

    private void OnDisable()
    {
        trackedTargets.Clear();
        transientBlockedKeys.Clear();
        pendingRemovalKeys.Clear();
        expiredBlockKeys.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!TryResolveTeleportTarget(other, includeTransientBlocks: false, out TeleportableTarget target))
            return;

        TrackedTargetState trackedState = GetOrCreateTrackedTargetState(target);
        trackedState.Target = target;
        trackedState.OverlapCount++;

        if (trackedState.OverlapCount == 1)
            trackedState.EnteredTime = Time.time;

        if (trackedState.IgnoreUntilExit)
            return;

        TryTeleportImmediately(trackedState);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!TryResolveTeleportTarget(other, includeTransientBlocks: false, out TeleportableTarget target))
            return;

        TrackedTargetState trackedState = GetOrCreateTrackedTargetState(target);
        trackedState.Target = target;
        trackedState.OverlapCount = Mathf.Max(1, trackedState.OverlapCount);

        if (trackedState.IgnoreUntilExit)
            return;

        TryTeleportImmediately(trackedState);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryResolveTeleportTarget(other, includeTransientBlocks: true, out TeleportableTarget target))
            return;

        if (!trackedTargets.TryGetValue(target.Key, out TrackedTargetState trackedState))
            return;

        trackedState.OverlapCount = Mathf.Max(0, trackedState.OverlapCount - 1);
        if (trackedState.OverlapCount > 0)
            return;

        trackedTargets.Remove(target.Key);
    }

    private void TryTeleportImmediately(TrackedTargetState trackedState)
    {
        if (trackedState == null || trackedState.IgnoreUntilExit || activationMode != TeleportActivationMode.Instant)
            return;

        if (!CanTeleportNow(trackedState))
            return;

        TeleportableTarget target = trackedState.Target;
        if (target == null)
            return;

        TeleportTarget(target);
        trackedTargets.Remove(target.Key);
    }

    private bool CanTeleportNow(TrackedTargetState trackedState)
    {
        if (trackedState == null || trackedState.Target == null || destinationPoint == null)
            return false;

        if (activationMode != TeleportActivationMode.TimedStay)
            return true;

        return Time.time + 0.0001f >= trackedState.EnteredTime + Mathf.Max(0f, requiredStayDuration);
    }

    private void TeleportTarget(TeleportableTarget target)
    {
        if (target == null || target.Root == null || destinationPoint == null)
            return;

        ApplyTransientEntryBlock(target.Key);
        MoveTargetToDestination(target, destinationPoint.position, destinationPoint.rotation);

        if (routeMode == TeleportRouteMode.LinkedTwoWay
            && linkedReturnTeleporter != null
            && linkedReturnTeleporter != this)
        {
            linkedReturnTeleporter.RegisterArrivalIgnore(target);
        }
    }

    private void MoveTargetToDestination(TeleportableTarget target, Vector3 worldPosition, Quaternion worldRotation)
    {
        Transform targetTransform = target.Rigidbody != null ? target.Rigidbody.transform : target.Root;
        if (targetTransform == null)
            return;

        if (target.Rigidbody != null)
        {
            target.Rigidbody.position = worldPosition;
            target.Rigidbody.rotation = worldRotation;
            target.Rigidbody.linearVelocity = Vector3.zero;
            target.Rigidbody.angularVelocity = Vector3.zero;
        }

        targetTransform.SetPositionAndRotation(worldPosition, worldRotation);
    }

    private void RegisterArrivalIgnore(TeleportableTarget target)
    {
        if (target == null || target.Root == null)
            return;

        TrackedTargetState trackedState = GetOrCreateTrackedTargetState(target);
        trackedState.Target = target;
        trackedState.OverlapCount = Mathf.Max(1, trackedState.OverlapCount);
        trackedState.EnteredTime = Time.time;
        trackedState.IgnoreUntilExit = true;
    }

    private TrackedTargetState GetOrCreateTrackedTargetState(TeleportableTarget target)
    {
        if (trackedTargets.TryGetValue(target.Key, out TrackedTargetState trackedState))
            return trackedState;

        trackedState = new TrackedTargetState
        {
            Target = target,
            EnteredTime = Time.time
        };
        trackedTargets[target.Key] = trackedState;
        return trackedState;
    }

    private void ApplyTransientEntryBlock(int targetKey)
    {
        transientBlockedKeys[targetKey] = Time.time + Mathf.Max(0.01f, sourceReentryBlockDuration);
    }

    private void CleanupExpiredTransientBlocks()
    {
        if (transientBlockedKeys.Count == 0)
            return;

        expiredBlockKeys.Clear();
        foreach (KeyValuePair<int, float> blockEntry in transientBlockedKeys)
        {
            if (Time.time + 0.0001f >= blockEntry.Value)
                expiredBlockKeys.Add(blockEntry.Key);
        }

        for (int i = 0; i < expiredBlockKeys.Count; i++)
            transientBlockedKeys.Remove(expiredBlockKeys[i]);
    }

    private bool TryResolveTeleportTarget(Collider other, bool includeTransientBlocks, out TeleportableTarget target)
    {
        target = null;

        if (!TryResolvePotentialTarget(other, out TeleportableTarget potentialTarget))
            return false;

        if (!includeTransientBlocks
            && transientBlockedKeys.TryGetValue(potentialTarget.Key, out float blockUntilTime)
            && Time.time + 0.0001f < blockUntilTime)
        {
            return false;
        }

        if (!IsTargetAllowed(potentialTarget, other))
            return false;

        target = potentialTarget;
        return true;
    }

    private bool TryResolvePotentialTarget(Collider other, out TeleportableTarget target)
    {
        target = null;

        if (other == null)
            return false;

        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            Transform root = playerHealth.transform;
            PhotonView photonView = playerHealth.GetComponent<PhotonView>();
            target = new TeleportableTarget
            {
                Key = ResolveStableTargetKey(photonView, root),
                Kind = TeleportTargetKind.Player,
                Root = root,
                Rigidbody = playerHealth.GetComponent<Rigidbody>(),
                PhotonView = photonView,
                PlayerHealth = playerHealth
            };
            return true;
        }

        EnemySetup enemySetup = other.GetComponentInParent<EnemySetup>();
        if (enemySetup != null)
        {
            Transform root = enemySetup.transform;
            PhotonView photonView = enemySetup.PhotonView;
            target = new TeleportableTarget
            {
                Key = ResolveStableTargetKey(photonView, root),
                Kind = TeleportTargetKind.Enemy,
                Root = root,
                Rigidbody = enemySetup.Rigidbody,
                PhotonView = photonView,
                EnemySetup = enemySetup
            };
            return true;
        }

        WorldPickupItem pickupItem = other.GetComponentInParent<WorldPickupItem>();
        if (pickupItem != null)
        {
            Transform root = pickupItem.transform;
            PhotonView photonView = pickupItem.GetComponent<PhotonView>();
            target = new TeleportableTarget
            {
                Key = ResolveStableTargetKey(photonView, root),
                Kind = TeleportTargetKind.Item,
                Root = root,
                Rigidbody = pickupItem.GetComponent<Rigidbody>(),
                PhotonView = photonView,
                PickupItem = pickupItem
            };
            return true;
        }

        Transform otherRoot = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform.root;
        if (otherRoot == null || otherRoot == transform || otherRoot.IsChildOf(transform))
            return false;

        PhotonView otherPhotonView = otherRoot.GetComponent<PhotonView>();
        target = new TeleportableTarget
        {
            Key = ResolveStableTargetKey(otherPhotonView, otherRoot),
            Kind = TeleportTargetKind.Other,
            Root = otherRoot,
            Rigidbody = other.attachedRigidbody != null ? other.attachedRigidbody : otherRoot.GetComponent<Rigidbody>(),
            PhotonView = otherPhotonView
        };
        return true;
    }

    private bool IsTargetAllowed(TeleportableTarget target, Collider sourceCollider)
    {
        if (target == null || target.Root == null)
            return false;

        if (!PassesDetectionMask(sourceCollider, target.Root))
            return false;

        switch (target.Kind)
        {
            case TeleportTargetKind.Player:
                return allowPlayers
                    && target.PlayerHealth != null
                    && target.PlayerHealth.IsLocallyOwned;

            case TeleportTargetKind.Enemy:
                return allowEnemies
                    && target.EnemySetup != null
                    && target.EnemySetup.HasAuthority;

            case TeleportTargetKind.Item:
                if (!allowItems || target.PickupItem == null || target.PickupItem.IsEquipped)
                    return false;

                return HasLocalAuthority(target.PhotonView);

            case TeleportTargetKind.Other:
                return allowOtherObjects && HasLocalAuthority(target.PhotonView);

            default:
                return false;
        }
    }

    private bool HasLocalAuthority(PhotonView photonView)
    {
        return photonView == null
            || PhotonNetwork.OfflineMode
            || !PhotonNetwork.InRoom
            || photonView.IsMine;
    }

    private bool PassesDetectionMask(Collider sourceCollider, Transform targetRoot)
    {
        int sourceLayerMaskBit = sourceCollider != null ? 1 << sourceCollider.gameObject.layer : 0;
        int targetLayerMaskBit = targetRoot != null ? 1 << targetRoot.gameObject.layer : 0;
        int activeMask = detectionMask.value;
        return (activeMask & sourceLayerMaskBit) != 0 || (activeMask & targetLayerMaskBit) != 0;
    }

    private bool IsTargetStillValid(TeleportableTarget target)
    {
        if (target == null || target.Root == null)
            return false;

        switch (target.Kind)
        {
            case TeleportTargetKind.Player:
                return target.PlayerHealth != null;
            case TeleportTargetKind.Enemy:
                return target.EnemySetup != null;
            case TeleportTargetKind.Item:
                return target.PickupItem != null;
            default:
                return true;
        }
    }

    private bool IsTargetStillOverlappingVolume(TeleportableTarget target)
    {
        if (target == null || target.Root == null)
            return false;

        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();

        if (triggerCollider == null || !triggerCollider.enabled)
            return false;

        Bounds volumeBounds = triggerCollider.bounds;
        Collider[] targetColliders = target.Root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < targetColliders.Length; i++)
        {
            Collider targetCollider = targetColliders[i];
            if (targetCollider == null || !targetCollider.enabled)
                continue;

            if (volumeBounds.Intersects(targetCollider.bounds))
                return true;
        }

        return false;
    }

    private static int ResolveStableTargetKey(PhotonView photonView, Transform root)
    {
        if (photonView != null && photonView.ViewID != 0)
            return photonView.ViewID;

        return root != null ? root.GetHashCode() : 0;
    }

    private void EnsureTriggerCollider()
    {
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null && !triggerCollider.isTrigger)
            triggerCollider.isTrigger = true;
    }

    private void OnDrawGizmosSelected()
    {
        Collider previewCollider = triggerCollider != null ? triggerCollider : GetComponent<Collider>();
        if (previewCollider == null)
            return;

        ResolveGizmoColors(out Color fillColor, out Color wireColor);
        Bounds volumeBounds = previewCollider.bounds;
        Gizmos.color = fillColor;
        Gizmos.DrawCube(volumeBounds.center, volumeBounds.size);
        Gizmos.color = wireColor;
        Gizmos.DrawWireCube(volumeBounds.center, volumeBounds.size);

        if (destinationPoint == null)
            return;

        Vector3 destinationPosition = destinationPoint.position;
        Gizmos.DrawLine(volumeBounds.center, destinationPosition);
        Gizmos.DrawWireSphere(destinationPosition, 0.18f);

        if (routeMode != TeleportRouteMode.LinkedTwoWay || linkedReturnTeleporter == null)
            return;

        Gizmos.color = new Color(0.35f, 1f, 0.9f, 0.9f);
        Gizmos.DrawLine(destinationPosition, linkedReturnTeleporter.transform.position);
        Gizmos.DrawWireSphere(linkedReturnTeleporter.transform.position, 0.12f);
    }

    private void ResolveGizmoColors(out Color fillColor, out Color wireColor)
    {
        if (activationMode == TeleportActivationMode.TimedStay)
        {
            fillColor = new Color(0.2f, 0.8f, 1f, 0.18f);
            wireColor = new Color(0.2f, 0.8f, 1f, 0.9f);
            return;
        }

        fillColor = new Color(0.1f, 1f, 0.55f, 0.18f);
        wireColor = new Color(0.1f, 1f, 0.55f, 0.9f);
    }
}
