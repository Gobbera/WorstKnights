using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("World/Hazards/Hazard Volume Controller")]
public class HazardVolumeController : MonoBehaviour
{
    public enum HazardEffectMode
    {
        InstantKill = 0,
        InstantDamage = 1,
        DamageOverTime = 2
    }

    private sealed class OccupantState
    {
        public PlayerHealth PlayerHealth;
        public int OverlapCount;
        public float NextDamageTime;
        public bool OneShotEffectConsumed;
    }

    [Header("Identity")]
    [SerializeField] private string hazardName = "Hazard";
    [Header("Effect")]
    [SerializeField] private HazardEffectMode effectMode = HazardEffectMode.InstantKill;
    [SerializeField] [Min(0f)] private float damageAmount = 25f;
    [SerializeField] [Min(0f)] private float damagePerSecond = 20f;
    [SerializeField] [Min(0.05f)] private float damageTickInterval = 0.25f;
    [SerializeField] private bool ignoreDamageImmunity = true;
    [SerializeField] private bool suppressDamageKnockback = true;
    [Header("Detection")]
    [SerializeField] private LayerMask playerDetectionMask = Physics.DefaultRaycastLayers;

    private readonly Dictionary<int, OccupantState> occupants = new Dictionary<int, OccupantState>();
    private readonly List<int> occupantKeysPendingRemoval = new List<int>();

    private Collider triggerCollider;

    public string DisplayName => string.IsNullOrWhiteSpace(hazardName) ? gameObject.name : hazardName;

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
        damageAmount = Mathf.Max(0f, damageAmount);
        damagePerSecond = Mathf.Max(0f, damagePerSecond);
        damageTickInterval = Mathf.Max(0.05f, damageTickInterval);
        EnsureTriggerCollider();
    }

    private void FixedUpdate()
    {
        if (occupants.Count == 0)
            return;

        occupantKeysPendingRemoval.Clear();

        foreach (KeyValuePair<int, OccupantState> occupantEntry in occupants)
        {
            OccupantState occupant = occupantEntry.Value;
            PlayerHealth playerHealth = occupant != null ? occupant.PlayerHealth : null;
            if (playerHealth == null
                || occupant == null
                || occupant.OverlapCount <= 0
                || !IsPlayerStillOverlappingHazard(playerHealth))
            {
                occupantKeysPendingRemoval.Add(occupantEntry.Key);
                continue;
            }

            switch (effectMode)
            {
                case HazardEffectMode.DamageOverTime:
                    if (playerHealth.IsAlive)
                        ApplyDamageOverTime(occupant);
                    break;
            }
        }

        for (int i = 0; i < occupantKeysPendingRemoval.Count; i++)
            occupants.Remove(occupantKeysPendingRemoval[i]);
    }

    private void OnDisable()
    {
        occupants.Clear();
        occupantKeysPendingRemoval.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!TryGetLocalPlayerHealth(other, out int playerKey, out PlayerHealth playerHealth))
            return;

        OccupantState occupant = GetOrCreateOccupantState(playerKey, playerHealth);
        occupant.OverlapCount++;
        occupant.PlayerHealth = playerHealth;

        if (occupant.OverlapCount == 1)
            ResetOccupantState(occupant);

        TryApplyOneShotEffect(occupant);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!TryGetLocalPlayerHealth(other, out int playerKey, out PlayerHealth playerHealth))
            return;

        OccupantState occupant = GetOrCreateOccupantState(playerKey, playerHealth);
        occupant.PlayerHealth = playerHealth;
        if (occupant.OverlapCount <= 0)
            ResetOccupantState(occupant);

        occupant.OverlapCount = Mathf.Max(1, occupant.OverlapCount);
        TryApplyOneShotEffect(occupant);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryGetLocalPlayerHealth(other, out int playerKey, out _))
            return;

        if (!occupants.TryGetValue(playerKey, out OccupantState occupant))
            return;

        occupant.OverlapCount = Mathf.Max(0, occupant.OverlapCount - 1);
        if (occupant.OverlapCount > 0)
            return;

        occupants.Remove(playerKey);
    }

    private void ApplyInstantKill(PlayerHealth playerHealth)
    {
        if (playerHealth == null || !playerHealth.IsAlive)
            return;

        playerHealth.ReceiveEnvironmentalKill(
            gameObject,
            ResolveHitPoint(playerHealth),
            ResolveHitDirection(playerHealth),
            ignoreDamageImmunity,
            suppressDamageKnockback);
    }

    private void ApplyInstantDamage(PlayerHealth playerHealth)
    {
        if (playerHealth == null || !playerHealth.IsAlive || damageAmount <= 0f)
            return;

        playerHealth.ReceiveEnvironmentalDamage(
            damageAmount,
            gameObject,
            ResolveHitPoint(playerHealth),
            ResolveHitDirection(playerHealth),
            ignoreDamageImmunity,
            suppressDamageKnockback,
            PlayerDamageAnimationType.ReactionDamage,
            PlayerCameraImpactType.DefaultHit);
    }

    private void ApplyDamageOverTime(OccupantState occupant)
    {
        if (occupant == null || occupant.PlayerHealth == null)
            return;

        float tickInterval = Mathf.Max(0.05f, damageTickInterval);
        if (Time.time + 0.0001f < occupant.NextDamageTime)
            return;

        float stepDamage = Mathf.Max(0f, damagePerSecond) * tickInterval;
        PlayerHealth playerHealth = occupant.PlayerHealth;
        if (playerHealth == null || !playerHealth.IsAlive || stepDamage <= 0f)
            return;

        occupant.NextDamageTime = Time.time + tickInterval;
        playerHealth.ReceiveEnvironmentalDamage(
            stepDamage,
            gameObject,
            ResolveHitPoint(playerHealth),
            ResolveHitDirection(playerHealth),
            ignoreDamageImmunity,
            suppressDamageKnockback,
            PlayerDamageAnimationType.None,
            PlayerCameraImpactType.None);
    }

    private OccupantState GetOrCreateOccupantState(int playerKey, PlayerHealth playerHealth)
    {
        if (occupants.TryGetValue(playerKey, out OccupantState occupant))
            return occupant;

        occupant = new OccupantState
        {
            PlayerHealth = playerHealth
        };
        occupants[playerKey] = occupant;
        return occupant;
    }

    private void ResetOccupantState(OccupantState occupant)
    {
        if (occupant == null)
            return;

        occupant.OverlapCount = Mathf.Max(1, occupant.OverlapCount);
        occupant.NextDamageTime = Time.time + Mathf.Max(0.05f, damageTickInterval);
        occupant.OneShotEffectConsumed = false;
    }

    private void TryApplyOneShotEffect(OccupantState occupant)
    {
        if (occupant == null || occupant.PlayerHealth == null || occupant.OneShotEffectConsumed)
            return;

        switch (effectMode)
        {
            case HazardEffectMode.InstantKill:
                if (!occupant.PlayerHealth.IsAlive)
                    return;

                occupant.OneShotEffectConsumed = true;
                ApplyInstantKill(occupant.PlayerHealth);
                break;

            case HazardEffectMode.InstantDamage:
                if (!occupant.PlayerHealth.IsAlive || damageAmount <= 0f)
                    return;

                occupant.OneShotEffectConsumed = true;
                ApplyInstantDamage(occupant.PlayerHealth);
                break;
        }
    }

    private bool TryGetLocalPlayerHealth(Collider other, out int playerKey, out PlayerHealth playerHealth)
    {
        playerKey = 0;
        playerHealth = null;

        if (other == null)
            return false;

        playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null || !playerHealth.IsLocallyOwned)
            return false;

        int colliderLayerMaskBit = 1 << other.gameObject.layer;
        int playerLayerMaskBit = 1 << playerHealth.gameObject.layer;
        int layerMaskValue = playerDetectionMask.value;
        if ((layerMaskValue & colliderLayerMaskBit) == 0 && (layerMaskValue & playerLayerMaskBit) == 0)
            return false;

        PhotonView playerPhotonView = playerHealth.GetComponent<PhotonView>();
        playerKey = playerPhotonView != null && playerPhotonView.ViewID != 0
            ? playerPhotonView.ViewID
            : playerHealth.GetHashCode();
        return true;
    }

    private bool IsPlayerStillOverlappingHazard(PlayerHealth playerHealth)
    {
        if (playerHealth == null)
            return false;

        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();

        if (triggerCollider == null || !triggerCollider.enabled)
            return false;

        Bounds hazardBounds = triggerCollider.bounds;
        Collider[] playerColliders = playerHealth.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < playerColliders.Length; i++)
        {
            Collider playerCollider = playerColliders[i];
            if (playerCollider == null || !playerCollider.enabled)
                continue;

            if (hazardBounds.Intersects(playerCollider.bounds))
                return true;
        }

        return false;
    }

    private Vector3 ResolveHitPoint(PlayerHealth playerHealth)
    {
        if (playerHealth == null)
            return transform.position;

        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();

        if (triggerCollider != null)
            return triggerCollider.ClosestPoint(playerHealth.transform.position);

        return playerHealth.transform.position;
    }

    private Vector3 ResolveHitDirection(PlayerHealth playerHealth)
    {
        if (playerHealth == null)
            return Vector3.zero;

        Vector3 hazardCenter = triggerCollider != null ? triggerCollider.bounds.center : transform.position;
        Vector3 hitDirection = playerHealth.transform.position - hazardCenter;
        if (hitDirection.sqrMagnitude > 0.0001f)
            return hitDirection.normalized;

        return transform.forward.sqrMagnitude > 0.0001f ? transform.forward : Vector3.forward;
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

        Color fillColor;
        Color wireColor;
        switch (effectMode)
        {
            case HazardEffectMode.InstantDamage:
                fillColor = new Color(1f, 0.75f, 0.15f, 0.2f);
                wireColor = new Color(1f, 0.75f, 0.15f, 0.9f);
                break;

            case HazardEffectMode.DamageOverTime:
                fillColor = new Color(1f, 0.35f, 0.1f, 0.2f);
                wireColor = new Color(1f, 0.35f, 0.1f, 0.9f);
                break;

            default:
                fillColor = new Color(1f, 0.1f, 0.1f, 0.2f);
                wireColor = new Color(1f, 0.1f, 0.1f, 0.9f);
                break;
        }

        Bounds hazardBounds = previewCollider.bounds;
        Gizmos.color = fillColor;
        Gizmos.DrawCube(hazardBounds.center, hazardBounds.size);
        Gizmos.color = wireColor;
        Gizmos.DrawWireCube(hazardBounds.center, hazardBounds.size);
    }
}
