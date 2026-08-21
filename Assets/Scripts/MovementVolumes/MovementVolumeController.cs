using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("World/Movement/Movement Volume Controller")]
public class MovementVolumeController : MonoBehaviour
{
    public enum MovementVolumeEffectMode
    {
        Accelerate = 0,
        Brake = 1,
        Slippery = 2,
        Trap = 3,
        Bounce = 4,
        Conveyor = 5
    }

    public enum ConveyorDirectionMode
    {
        Axis = 0,
        Diagonal = 1
    }

    public enum ConveyorAxis
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    public enum ConveyorDirection
    {
        Positive = 0,
        Negative = 1
    }

    public enum BounceDirectionMode
    {
        VolumeUp = 0,
        WorldUp = 1,
        CustomDirection = 2
    }

    private sealed class OccupantState
    {
        public PlayerMovement PlayerMovement;
        public int OverlapCount;
        public bool OneShotEffectConsumed;
    }

    private sealed class RigidbodyOccupantState
    {
        public Rigidbody Body;
        public int OverlapCount;
    }

    [Header("Identity")]
    [SerializeField] private string volumeName = "Movement Volume";
    [Header("Effect")]
    [SerializeField] private MovementVolumeEffectMode effectMode = MovementVolumeEffectMode.Accelerate;
    [Header("Accelerate")]
    [SerializeField] [Min(0f)] private float accelerateSpeedMultiplier = 1.45f;
    [SerializeField] [Min(0f)] private float accelerateAccelerationMultiplier = 1.3f;
    [Header("Brake")]
    [SerializeField] [Range(0f, 1f)] private float brakeSpeedMultiplier = 0.45f;
    [SerializeField] [Min(0f)] private float brakeAccelerationMultiplier = 0.75f;
    [SerializeField] [Min(0f)] private float brakeGroundDragMultiplier = 2.25f;
    [Header("Slippery")]
    [SerializeField] [Min(0f)] private float slipperySpeedMultiplier = 1f;
    [SerializeField] [Range(0f, 1f)] private float slipperySteeringMultiplier = 0.35f;
    [SerializeField] [Range(0f, 1f)] private float slipperyGroundDragMultiplier = 0.12f;
    [Header("Trap")]
    [SerializeField] [Min(0f)] private float trapDuration = 1.25f;
    [SerializeField] private bool zeroPlanarVelocityOnTrap = true;
    [Header("Bounce")]
    [SerializeField] private BounceDirectionMode bounceDirectionMode = BounceDirectionMode.VolumeUp;
    [SerializeField] private Vector3 customBounceDirection = Vector3.up;
    [SerializeField] [Min(0f)] private float minIncomingBounceSpeed = 0f;
    [SerializeField] [Min(0f)] private float minBounceLaunchSpeed = 4.5f;
    [SerializeField] [Min(0f)] private float bounceRestitution = 1f;
    [SerializeField] [Min(0f)] private float bounceSpeedBonus = 0f;
    [SerializeField] [Min(0f)] private float maxBounceLaunchSpeed = 14f;
    [SerializeField] [Min(0f)] private float lateralVelocityMultiplier = 0.8f;
    [Header("Conveyor")]
    [SerializeField] private ConveyorDirectionMode conveyorDirectionMode = ConveyorDirectionMode.Axis;
    [SerializeField] private ConveyorAxis conveyorAxis = ConveyorAxis.Z;
    [SerializeField] private ConveyorDirection conveyorDirection = ConveyorDirection.Positive;
    [SerializeField] private Vector3 conveyorDiagonalDirection = new Vector3(1f, 0f, 1f);
    [SerializeField] private bool conveyorUseLocalDirection = true;
    [FormerlySerializedAs("conveyorPushSpeed")]
    [SerializeField] [Min(0f)] private float conveyorSpeed = 3f;
    [SerializeField] private bool conveyorAffectsRigidbodies = true;
    [SerializeField] private LayerMask conveyorRigidbodyDetectionMask = Physics.DefaultRaycastLayers;
    [Header("Detection")]
    [SerializeField] private LayerMask playerDetectionMask = Physics.DefaultRaycastLayers;

    private static readonly Vector3 DefaultConveyorDiagonalDirection = new Vector3(1f, 0f, 1f);

    private readonly Dictionary<int, OccupantState> occupants = new Dictionary<int, OccupantState>();
    private readonly Dictionary<int, RigidbodyOccupantState> rigidbodyOccupants = new Dictionary<int, RigidbodyOccupantState>();
    private readonly List<int> occupantKeysPendingRemoval = new List<int>();
    private readonly List<int> rigidbodyOccupantKeysPendingRemoval = new List<int>();

    private Collider triggerCollider;

    public string DisplayName => string.IsNullOrWhiteSpace(volumeName) ? gameObject.name : volumeName;
    public bool IsContinuousEffect =>
        effectMode == MovementVolumeEffectMode.Accelerate
        || effectMode == MovementVolumeEffectMode.Brake
        || effectMode == MovementVolumeEffectMode.Slippery
        || effectMode == MovementVolumeEffectMode.Conveyor;

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
        accelerateSpeedMultiplier = Mathf.Max(0f, accelerateSpeedMultiplier);
        accelerateAccelerationMultiplier = Mathf.Max(0f, accelerateAccelerationMultiplier);
        brakeSpeedMultiplier = Mathf.Clamp01(brakeSpeedMultiplier);
        brakeAccelerationMultiplier = Mathf.Max(0f, brakeAccelerationMultiplier);
        brakeGroundDragMultiplier = Mathf.Max(0f, brakeGroundDragMultiplier);
        slipperySpeedMultiplier = Mathf.Max(0f, slipperySpeedMultiplier);
        slipperySteeringMultiplier = Mathf.Clamp01(slipperySteeringMultiplier);
        slipperyGroundDragMultiplier = Mathf.Clamp01(slipperyGroundDragMultiplier);
        trapDuration = Mathf.Max(0f, trapDuration);
        minIncomingBounceSpeed = Mathf.Max(0f, minIncomingBounceSpeed);
        minBounceLaunchSpeed = Mathf.Max(0f, minBounceLaunchSpeed);
        bounceRestitution = Mathf.Max(0f, bounceRestitution);
        bounceSpeedBonus = Mathf.Max(0f, bounceSpeedBonus);
        maxBounceLaunchSpeed = Mathf.Max(0f, maxBounceLaunchSpeed);
        lateralVelocityMultiplier = Mathf.Max(0f, lateralVelocityMultiplier);
        conveyorSpeed = Mathf.Max(0f, conveyorSpeed);

        if (bounceDirectionMode == BounceDirectionMode.CustomDirection && customBounceDirection.sqrMagnitude <= 0.0001f)
            customBounceDirection = Vector3.up;

        if (conveyorDirectionMode == ConveyorDirectionMode.Diagonal && conveyorDiagonalDirection.sqrMagnitude <= 0.0001f)
            conveyorDiagonalDirection = DefaultConveyorDiagonalDirection;

        EnsureTriggerCollider();
    }

    private void FixedUpdate()
    {
        if (occupants.Count == 0 && rigidbodyOccupants.Count == 0)
            return;

        occupantKeysPendingRemoval.Clear();
        foreach (KeyValuePair<int, OccupantState> occupantEntry in occupants)
        {
            OccupantState occupant = occupantEntry.Value;
            PlayerMovement playerMovement = occupant != null ? occupant.PlayerMovement : null;
            if (occupant == null
                || playerMovement == null
                || occupant.OverlapCount <= 0
                || !IsPlayerStillOverlappingVolume(playerMovement))
            {
                occupantKeysPendingRemoval.Add(occupantEntry.Key);
                continue;
            }

            if (IsContinuousEffect)
                playerMovement.RegisterMovementVolume(this);
        }

        for (int i = 0; i < occupantKeysPendingRemoval.Count; i++)
            RemoveOccupant(occupantKeysPendingRemoval[i]);

        if (effectMode != MovementVolumeEffectMode.Conveyor || rigidbodyOccupants.Count == 0)
            return;

        rigidbodyOccupantKeysPendingRemoval.Clear();
        foreach (KeyValuePair<int, RigidbodyOccupantState> occupantEntry in rigidbodyOccupants)
        {
            RigidbodyOccupantState occupant = occupantEntry.Value;
            Rigidbody body = occupant != null ? occupant.Body : null;
            if (occupant == null
                || body == null
                || body.isKinematic
                || occupant.OverlapCount <= 0
                || !IsRigidbodyStillOverlappingVolume(body))
            {
                rigidbodyOccupantKeysPendingRemoval.Add(occupantEntry.Key);
                continue;
            }

            ApplyConveyorToRigidbody(body);
        }

        for (int i = 0; i < rigidbodyOccupantKeysPendingRemoval.Count; i++)
            rigidbodyOccupants.Remove(rigidbodyOccupantKeysPendingRemoval[i]);
    }

    private void OnDisable()
    {
        occupantKeysPendingRemoval.Clear();
        foreach (KeyValuePair<int, OccupantState> occupantEntry in occupants)
            UnregisterContinuousEffect(occupantEntry.Value);

        occupants.Clear();
        rigidbodyOccupants.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (TryGetLocalPlayerMovement(other, out int playerKey, out PlayerMovement playerMovement))
        {
            OccupantState occupant = GetOrCreateOccupantState(playerKey, playerMovement);
            occupant.OverlapCount++;
            occupant.PlayerMovement = playerMovement;

            if (occupant.OverlapCount == 1)
                occupant.OneShotEffectConsumed = false;

            if (IsContinuousEffect)
                playerMovement.RegisterMovementVolume(this);

            TryApplyOneShotEffect(occupant);
        }

        if (TryGetConveyorRigidbody(other, out int bodyKey, out Rigidbody body))
        {
            RigidbodyOccupantState bodyOccupant = GetOrCreateRigidbodyOccupantState(bodyKey, body);
            bodyOccupant.OverlapCount++;
            bodyOccupant.Body = body;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (TryGetLocalPlayerMovement(other, out int playerKey, out PlayerMovement playerMovement))
        {
            OccupantState occupant = GetOrCreateOccupantState(playerKey, playerMovement);
            occupant.PlayerMovement = playerMovement;

            if (occupant.OverlapCount <= 0)
            {
                occupant.OverlapCount = 1;
                occupant.OneShotEffectConsumed = false;
            }
            else
            {
                occupant.OverlapCount = Mathf.Max(1, occupant.OverlapCount);
            }

            if (IsContinuousEffect)
                playerMovement.RegisterMovementVolume(this);

            TryApplyOneShotEffect(occupant);
        }

        if (TryGetConveyorRigidbody(other, out int bodyKey, out Rigidbody body))
        {
            RigidbodyOccupantState bodyOccupant = GetOrCreateRigidbodyOccupantState(bodyKey, body);
            bodyOccupant.Body = body;
            bodyOccupant.OverlapCount = Mathf.Max(1, bodyOccupant.OverlapCount);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (TryGetLocalPlayerMovement(other, out int playerKey, out _)
            && occupants.TryGetValue(playerKey, out OccupantState occupant))
        {
            occupant.OverlapCount = Mathf.Max(0, occupant.OverlapCount - 1);
            if (occupant.OverlapCount <= 0)
                RemoveOccupant(playerKey);
        }

        if (TryGetConveyorRigidbody(other, out int bodyKey, out _)
            && rigidbodyOccupants.TryGetValue(bodyKey, out RigidbodyOccupantState bodyOccupant))
        {
            bodyOccupant.OverlapCount = Mathf.Max(0, bodyOccupant.OverlapCount - 1);
            if (bodyOccupant.OverlapCount <= 0)
                rigidbodyOccupants.Remove(bodyKey);
        }
    }

    public void AccumulateContinuousMovementModifiers(
        ref float speedMultiplier,
        ref float accelerationMultiplier,
        ref float groundDragMultiplier,
        ref Vector3 conveyorVelocity)
    {
        switch (effectMode)
        {
            case MovementVolumeEffectMode.Accelerate:
                speedMultiplier *= accelerateSpeedMultiplier;
                accelerationMultiplier *= accelerateAccelerationMultiplier;
                break;

            case MovementVolumeEffectMode.Brake:
                speedMultiplier *= brakeSpeedMultiplier;
                accelerationMultiplier *= brakeAccelerationMultiplier;
                groundDragMultiplier *= brakeGroundDragMultiplier;
                break;

            case MovementVolumeEffectMode.Slippery:
                speedMultiplier *= slipperySpeedMultiplier;
                accelerationMultiplier *= slipperySteeringMultiplier;
                groundDragMultiplier *= slipperyGroundDragMultiplier;
                break;

            case MovementVolumeEffectMode.Conveyor:
                conveyorVelocity += ResolveConveyorDirection() * conveyorSpeed;
                break;
        }
    }

    private void TryApplyOneShotEffect(OccupantState occupant)
    {
        if (occupant == null || occupant.PlayerMovement == null || occupant.OneShotEffectConsumed)
            return;

        switch (effectMode)
        {
            case MovementVolumeEffectMode.Trap:
                occupant.PlayerMovement.ApplyMovementVolumeTrap(trapDuration, zeroPlanarVelocityOnTrap);
                occupant.OneShotEffectConsumed = true;
                break;

            case MovementVolumeEffectMode.Bounce:
                if (occupant.PlayerMovement.TryApplyMovementVolumeBounce(
                        ResolveBounceDirection(),
                        minIncomingBounceSpeed,
                        minBounceLaunchSpeed,
                        bounceRestitution,
                        bounceSpeedBonus,
                        maxBounceLaunchSpeed,
                        lateralVelocityMultiplier))
                {
                    occupant.OneShotEffectConsumed = true;
                }
                break;
        }
    }

    private OccupantState GetOrCreateOccupantState(int playerKey, PlayerMovement playerMovement)
    {
        if (occupants.TryGetValue(playerKey, out OccupantState occupant))
            return occupant;

        occupant = new OccupantState
        {
            PlayerMovement = playerMovement
        };
        occupants[playerKey] = occupant;
        return occupant;
    }

    private RigidbodyOccupantState GetOrCreateRigidbodyOccupantState(int bodyKey, Rigidbody body)
    {
        if (rigidbodyOccupants.TryGetValue(bodyKey, out RigidbodyOccupantState occupant))
            return occupant;

        occupant = new RigidbodyOccupantState
        {
            Body = body
        };
        rigidbodyOccupants[bodyKey] = occupant;
        return occupant;
    }

    private void RemoveOccupant(int playerKey)
    {
        if (!occupants.TryGetValue(playerKey, out OccupantState occupant))
            return;

        UnregisterContinuousEffect(occupant);
        occupants.Remove(playerKey);
    }

    private void UnregisterContinuousEffect(OccupantState occupant)
    {
        if (!IsContinuousEffect || occupant == null || occupant.PlayerMovement == null)
            return;

        occupant.PlayerMovement.UnregisterMovementVolume(this);
    }

    private bool TryGetLocalPlayerMovement(Collider other, out int playerKey, out PlayerMovement playerMovement)
    {
        playerKey = 0;
        playerMovement = null;

        if (other == null)
            return false;

        playerMovement = other.GetComponentInParent<PlayerMovement>();
        if (playerMovement == null)
            return false;

        PhotonView playerPhotonView = playerMovement.GetComponent<PhotonView>();
        if (playerPhotonView != null && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && !playerPhotonView.IsMine)
            return false;

        int colliderLayerMaskBit = 1 << other.gameObject.layer;
        int playerLayerMaskBit = 1 << playerMovement.gameObject.layer;
        int layerMaskValue = playerDetectionMask.value;
        if ((layerMaskValue & colliderLayerMaskBit) == 0 && (layerMaskValue & playerLayerMaskBit) == 0)
            return false;

        playerKey = playerPhotonView != null && playerPhotonView.ViewID != 0
            ? playerPhotonView.ViewID
            : playerMovement.GetHashCode();
        return true;
    }

    private bool TryGetConveyorRigidbody(Collider other, out int bodyKey, out Rigidbody body)
    {
        bodyKey = 0;
        body = null;

        if (effectMode != MovementVolumeEffectMode.Conveyor || !conveyorAffectsRigidbodies || other == null)
            return false;

        if (other.GetComponentInParent<PlayerMovement>() != null)
            return false;

        body = other.attachedRigidbody;
        if (body == null || body.isKinematic)
            return false;

        int colliderLayerMaskBit = 1 << other.gameObject.layer;
        int bodyLayerMaskBit = 1 << body.gameObject.layer;
        int layerMaskValue = conveyorRigidbodyDetectionMask.value;
        if ((layerMaskValue & colliderLayerMaskBit) == 0 && (layerMaskValue & bodyLayerMaskBit) == 0)
            return false;

        PhotonView bodyPhotonView = body.GetComponentInParent<PhotonView>();
        if (bodyPhotonView != null && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && !bodyPhotonView.IsMine)
            return false;

        bodyKey = bodyPhotonView != null && bodyPhotonView.ViewID != 0
            ? bodyPhotonView.ViewID
            : body.GetHashCode();
        return true;
    }

    private bool IsPlayerStillOverlappingVolume(PlayerMovement playerMovement)
    {
        if (playerMovement == null)
            return false;

        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();

        if (triggerCollider == null || !triggerCollider.enabled)
            return false;

        Bounds volumeBounds = triggerCollider.bounds;
        Collider[] playerColliders = playerMovement.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < playerColliders.Length; i++)
        {
            Collider playerCollider = playerColliders[i];
            if (playerCollider == null || !playerCollider.enabled)
                continue;

            if (volumeBounds.Intersects(playerCollider.bounds))
                return true;
        }

        return false;
    }

    private bool IsRigidbodyStillOverlappingVolume(Rigidbody body)
    {
        if (body == null)
            return false;

        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();

        if (triggerCollider == null || !triggerCollider.enabled)
            return false;

        Bounds volumeBounds = triggerCollider.bounds;
        Collider[] bodyColliders = body.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < bodyColliders.Length; i++)
        {
            Collider bodyCollider = bodyColliders[i];
            if (bodyCollider == null || !bodyCollider.enabled)
                continue;

            if (bodyCollider == triggerCollider)
                continue;

            if (volumeBounds.Intersects(bodyCollider.bounds))
                return true;
        }

        return false;
    }

    private Vector3 ResolveBounceDirection()
    {
        switch (bounceDirectionMode)
        {
            case BounceDirectionMode.WorldUp:
                return Vector3.up;
            case BounceDirectionMode.CustomDirection:
                return customBounceDirection.sqrMagnitude > 0.0001f ? customBounceDirection.normalized : Vector3.up;
            default:
                return transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        }
    }

    private Vector3 ResolveConveyorDirection()
    {
        Vector3 rawDirection;
        if (conveyorDirectionMode == ConveyorDirectionMode.Diagonal)
        {
            rawDirection = conveyorDiagonalDirection.sqrMagnitude > 0.0001f
                ? conveyorDiagonalDirection
                : DefaultConveyorDiagonalDirection;
        }
        else
        {
            rawDirection = GetConveyorAxisDirectionVector(conveyorAxis, conveyorDirection);
        }

        Vector3 worldDirection = conveyorUseLocalDirection
            ? transform.TransformDirection(rawDirection)
            : rawDirection;

        return worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : transform.forward;
    }

    private static Vector3 GetConveyorAxisDirectionVector(ConveyorAxis axis, ConveyorDirection direction)
    {
        float directionSign = direction == ConveyorDirection.Positive ? 1f : -1f;
        switch (axis)
        {
            case ConveyorAxis.Y:
                return Vector3.up * directionSign;
            case ConveyorAxis.X:
                return Vector3.right * directionSign;
            default:
                return Vector3.forward * directionSign;
        }
    }

    private void ApplyConveyorToRigidbody(Rigidbody body)
    {
        if (body == null || body.isKinematic)
            return;

        Vector3 conveyorDirection = ResolveConveyorDirection();
        float targetSpeed = Mathf.Max(0f, conveyorSpeed);
        if (targetSpeed <= 0.0001f)
            return;

        float currentSpeedAlongDirection = Vector3.Dot(body.linearVelocity, conveyorDirection);
        float missingSpeed = targetSpeed - currentSpeedAlongDirection;
        if (missingSpeed <= 0.0001f)
            return;

        body.AddForce(conveyorDirection * missingSpeed, ForceMode.VelocityChange);
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

        Bounds volumeBounds = previewCollider.bounds;
        ResolveGizmoColors(out Color fillColor, out Color wireColor);
        Gizmos.color = fillColor;
        Gizmos.DrawCube(volumeBounds.center, volumeBounds.size);
        Gizmos.color = wireColor;
        Gizmos.DrawWireCube(volumeBounds.center, volumeBounds.size);

        if (effectMode != MovementVolumeEffectMode.Bounce && effectMode != MovementVolumeEffectMode.Conveyor)
            return;

        Vector3 bounceDirection = effectMode == MovementVolumeEffectMode.Conveyor
            ? ResolveConveyorDirection()
            : ResolveBounceDirection();
        Vector3 arrowStart = volumeBounds.center;
        Vector3 arrowEnd = arrowStart + bounceDirection * Mathf.Max(0.5f, volumeBounds.extents.y + 0.5f);
        Gizmos.DrawLine(arrowStart, arrowEnd);
        Gizmos.DrawWireSphere(arrowEnd, 0.08f);
    }

    private void ResolveGizmoColors(out Color fillColor, out Color wireColor)
    {
        switch (effectMode)
        {
            case MovementVolumeEffectMode.Brake:
                fillColor = new Color(0.25f, 0.7f, 1f, 0.18f);
                wireColor = new Color(0.25f, 0.7f, 1f, 0.9f);
                break;

            case MovementVolumeEffectMode.Slippery:
                fillColor = new Color(0.55f, 1f, 1f, 0.18f);
                wireColor = new Color(0.55f, 1f, 1f, 0.9f);
                break;

            case MovementVolumeEffectMode.Trap:
                fillColor = new Color(0.8f, 0.2f, 1f, 0.18f);
                wireColor = new Color(0.8f, 0.2f, 1f, 0.9f);
                break;

            case MovementVolumeEffectMode.Bounce:
                fillColor = new Color(0.2f, 1f, 0.45f, 0.18f);
                wireColor = new Color(0.2f, 1f, 0.45f, 0.9f);
                break;

            case MovementVolumeEffectMode.Conveyor:
                fillColor = new Color(1f, 0.45f, 0.15f, 0.18f);
                wireColor = new Color(1f, 0.45f, 0.15f, 0.9f);
                break;

            default:
                fillColor = new Color(1f, 0.8f, 0.15f, 0.18f);
                wireColor = new Color(1f, 0.8f, 0.15f, 0.9f);
                break;
        }
    }
}
