using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement), typeof(PhotonView))]
public class PlayerKickAttack : MonoBehaviour
{
    private const string FirstPersonCameraName = "FP_Camera";
    private const int MaxHitColliders = 16;

    [Header("Kick")]
    [SerializeField] [Min(0f)] private float kickDamage = 8f;
    [SerializeField] [Min(0f)] private float knockbackForce = 2.4f;
    [SerializeField] [Min(0f)] private float upwardKnockbackForce;
    [SerializeField] [Min(0.05f)] private float enemyKnockbackDuration = 0.22f;
    [SerializeField] [Min(0f)] private float playerControlLockDuration = 0.12f;
    [SerializeField] [Min(0.1f)] private float hitRadius = 0.75f;
    [SerializeField] [Min(0.1f)] private float hitDistance = 1.05f;
    [SerializeField] [Min(0f)] private float kickOriginHeight = 0.85f;
    [SerializeField] private LayerMask hitMask = Physics.DefaultRaycastLayers;
    [SerializeField] private PlayerCameraImpactType playerCameraImpactType = PlayerCameraImpactType.DefaultHit;

    [Header("References")]
    [SerializeField] private Transform kickOrigin;
    [SerializeField] private Transform kickDirection;

    private readonly Collider[] hitBuffer = new Collider[MaxHitColliders];
    private readonly HashSet<Component> processedTargets = new HashSet<Component>();

    private PlayerMovement playerMovement;
    private PlayerHealth playerHealth;
    private PhotonView photonView;
    private int lastKickSequence;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        playerHealth = GetComponent<PlayerHealth>();
        photonView = GetComponent<PhotonView>();
        ResolveKickTransforms();
    }

    private void Update()
    {
        if (photonView != null && !photonView.IsMine)
            return;

        if (playerMovement == null)
            return;

        if (playerMovement.KickAnimationSequence == lastKickSequence)
            return;

        lastKickSequence = playerMovement.KickAnimationSequence;
        TryHitTargets();
    }

    private void TryHitTargets()
    {
        ResolveKickTransforms();

        Vector3 origin = kickOrigin != null
            ? kickOrigin.position
            : transform.position + Vector3.up * kickOriginHeight;

        Vector3 forward = kickDirection != null ? kickDirection.forward : transform.forward;
        Vector3 planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (planarForward.sqrMagnitude <= 0.0001f)
            planarForward = transform.forward;

        planarForward.Normalize();

        Vector3 hitCenter = origin + planarForward * Mathf.Max(0.1f, hitDistance);
        float hitProbeDistance = Mathf.Max(0.05f, hitDistance + hitRadius);
        int hitCount = Physics.OverlapSphereNonAlloc(
            hitCenter,
            Mathf.Max(0.05f, hitRadius),
            hitBuffer,
            DestructibleDebrisCollision.ExcludeDebrisLayer(hitMask.value),
            QueryTriggerInteraction.Ignore);

        processedTargets.Clear();
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = hitBuffer[i];
            if (hitCollider == null)
                continue;

            IDamageable damageable = hitCollider.GetComponentInParent<IDamageable>();
            IMeleeImpactReceiver impactReceiver = hitCollider.GetComponentInParent<IMeleeImpactReceiver>();
            Component targetComponent = damageable as Component ?? impactReceiver as Component;
            if (targetComponent == null)
                continue;

            PlayerHealth targetPlayerHealth = damageable as PlayerHealth;
            if ((damageable != null && !damageable.IsAlive) || IsSelfTarget(targetComponent, targetPlayerHealth))
                continue;

            if (!processedTargets.Add(targetComponent))
                continue;

            Vector3 resolvedHitPoint = ResolveHitPoint(origin, hitCenter, planarForward, hitProbeDistance, hitCollider);
            DamageInfo damageInfo = new DamageInfo(
                kickDamage,
                gameObject,
                CombatAlignment.Player,
                resolvedHitPoint,
                planarForward,
                PlayerDamageAnimationType.None,
                playerCameraImpactType);

            if (damageable is EnemyHealth enemyHealth)
            {
                enemyHealth.ReceivePlayerKick(
                    damageInfo,
                    knockbackForce,
                    upwardKnockbackForce,
                    enemyKnockbackDuration);
                continue;
            }

            if (targetPlayerHealth != null)
            {
                DamageInfo playerDamageInfo = new DamageInfo(
                    kickDamage,
                    gameObject,
                    CombatAlignment.Neutral,
                    resolvedHitPoint,
                    planarForward,
                    PlayerDamageAnimationType.ReactionDamage,
                    playerCameraImpactType);

                targetPlayerHealth.ReceiveKickDamage(
                    playerDamageInfo,
                    BuildKnockbackVelocity(planarForward, knockbackForce, upwardKnockbackForce),
                    playerControlLockDuration);
                continue;
            }

            if (damageable != null)
                damageable.ApplyDamage(damageInfo);

            impactReceiver?.ReceiveMeleeImpact(damageInfo, hitCollider);
        }
    }

    private bool IsSelfTarget(Component targetComponent, PlayerHealth targetPlayerHealth)
    {
        if (targetComponent != null && targetComponent.transform.IsChildOf(transform))
            return true;

        if (targetPlayerHealth == null)
            return false;

        if (playerHealth != null && targetPlayerHealth == playerHealth)
            return true;

        PhotonView targetPhotonView = targetPlayerHealth.GetComponent<PhotonView>();
        return photonView != null
            && targetPhotonView != null
            && photonView.ViewID != 0
            && targetPhotonView.ViewID == photonView.ViewID;
    }

    private static Vector3 BuildKnockbackVelocity(Vector3 planarDirection, float horizontalForce, float upwardForce)
    {
        Vector3 safePlanarDirection = Vector3.ProjectOnPlane(planarDirection, Vector3.up);
        if (safePlanarDirection.sqrMagnitude <= 0.0001f)
            safePlanarDirection = Vector3.forward;

        return safePlanarDirection.normalized * Mathf.Max(0f, horizontalForce)
            + Vector3.up * Mathf.Max(0f, upwardForce);
    }

    private static Vector3 ResolveHitPoint(
        Vector3 origin,
        Vector3 hitCenter,
        Vector3 fallbackDirection,
        float hitProbeDistance,
        Collider hitCollider)
    {
        if (hitCollider == null)
            return hitCenter;

        Vector3 rayDirection = hitCenter - origin;
        float rayDistance = rayDirection.magnitude;
        if (rayDistance <= 0.0001f)
        {
            rayDirection = fallbackDirection.sqrMagnitude > 0.0001f ? fallbackDirection.normalized : Vector3.forward;
            rayDistance = Mathf.Max(0.05f, hitProbeDistance);
        }
        else
        {
            rayDirection /= rayDistance;
        }

        if (hitCollider.Raycast(new Ray(origin, rayDirection), out RaycastHit raycastHit, Mathf.Max(rayDistance, hitProbeDistance)))
            return raycastHit.point;

        Vector3 closestFromCenter = hitCollider.ClosestPoint(hitCenter);
        if (IsFiniteVector(closestFromCenter))
            return closestFromCenter;

        Vector3 closestFromOrigin = hitCollider.ClosestPoint(origin);
        if (IsFiniteVector(closestFromOrigin))
            return closestFromOrigin;

        return hitCenter;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z));
    }

    private void ResolveKickTransforms()
    {
        if (kickOrigin != null && kickDirection != null)
            return;

        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera playerCamera = cameras[i];
            if (playerCamera == null)
                continue;

            if (!string.Equals(playerCamera.gameObject.name, FirstPersonCameraName, StringComparison.Ordinal))
                continue;

            kickOrigin = playerCamera.transform;
            kickDirection = playerCamera.transform;
            return;
        }

        kickOrigin = transform;
        kickDirection = transform;
    }
}
