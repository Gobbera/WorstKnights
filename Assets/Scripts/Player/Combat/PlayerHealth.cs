using System.Collections;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView))]
public class PlayerHealth : CombatHealth
{
    private const string SpawnPointName = "SpawnPoint";

    [Header("Respawn")]
    [SerializeField] private bool respawnOnDeath = true;
    [SerializeField] [Min(0f)] private float respawnDelay = 1.25f;
    [SerializeField] [Min(0f)] private float respawnInvulnerabilityDuration = 1f;

    [Header("Strong Impact Ragdoll Tuning")]
    [SerializeField] [InspectorName("Enable Strong Knockback Ragdoll")] private bool enableRagdollFromStrongKnockback = true;
    [SerializeField] [InspectorName("Knockback Min Speed")] [Min(0f)] private float minStrongKnockbackForImpactRagdoll = 12f;
    [SerializeField] [InspectorName("Recover After Grounded Seconds")] [Min(0f)] private float strongKnockbackRecoverAfterGroundedSeconds = 1.25f;
    [SerializeField, HideInInspector, Min(0f)] private float strongKnockbackImpactImpulseMultiplier = 0.75f;
    [SerializeField, HideInInspector, Min(0f)] private float strongKnockbackImpactUpwardMultiplier = 0.08f;
    [SerializeField, HideInInspector, Min(0f)] private float maxStrongKnockbackImpactImpulse = 12f;
    [SerializeField, HideInInspector, Min(0f)] private float maxStrongKnockbackImpactUpward = 2.5f;

    [Header("High Fall Ragdoll Tuning")]
    [SerializeField] [InspectorName("Enable High Fall Ragdoll")] private bool enableHighFallRagdoll = true;
    [SerializeField] [InspectorName("Fall Min Height")] [Min(0f)] private float minFallHeightForRagdoll = 6f;
    [SerializeField] [InspectorName("Fall Min Down Speed")] [Min(0f)] private float minFallDownSpeedForRagdoll = 10f;
    [SerializeField] [InspectorName("Recover After Grounded Seconds")] [Min(0f)] private float highFallRecoverAfterGroundedSeconds = 1.5f;
    [SerializeField, HideInInspector, Min(0f)] private float highFallBaseImpactImpulse = 1f;
    [SerializeField, HideInInspector, Min(0f)] private float highFallDistanceImpulseMultiplier = 0.8f;
    [SerializeField, HideInInspector, Min(0f)] private float highFallDownSpeedImpulseMultiplier = 0.25f;
    [SerializeField, HideInInspector, Min(0f)] private float highFallUpwardImpulse = 0f;
    [SerializeField, HideInInspector, Min(0f)] private float maxHighFallImpactImpulse = 10f;
    [SerializeField, HideInInspector, Min(0f)] private float maxHighFallImpactUpward = 1f;

    private PhotonView photonView;
    private PlayerController playerController;
    private PlayerMovement playerMovement;
    private HeadBobController headBobController;
    private Rigidbody rb;
    private HandEquipmentController handEquipmentController;
    private PlayerRagdollController ragdollController;
    private IMeleeImpactReceiver meleeImpactReceiver;
    private Coroutine respawnRoutine;
    private Coroutine impactRagdollRecoveryRoutine;
    private bool suppressDamageKnockback;

    public override CombatAlignment Alignment => CombatAlignment.Player;
    public bool IsLocallyOwned => photonView == null || photonView.IsMine;
    public bool CanBeTargetedByEnemy => IsAlive;

    protected override void Awake()
    {
        photonView = GetComponent<PhotonView>();
        playerController = GetComponent<PlayerController>();
        playerMovement = GetComponent<PlayerMovement>();
        headBobController = GetComponentInChildren<HeadBobController>(true);
        rb = GetComponent<Rigidbody>();
        handEquipmentController = GetComponent<HandEquipmentController>();
        ragdollController = GetComponent<PlayerRagdollController>();
        if (ragdollController == null)
            ragdollController = gameObject.AddComponent<PlayerRagdollController>();
        ResolveMeleeImpactReceiver();
        base.Awake();
    }

    private void Start()
    {
        BroadcastLifeState();
    }

    public void ReceiveDamage(DamageInfo damageInfo)
    {
        if (CanApplyDamageLocally())
        {
            ApplyDamageAndBroadcast(damageInfo);
            return;
        }

        if (photonView.Owner == null)
            return;

        photonView.RPC(
            nameof(RpcReceiveDamage),
            photonView.Owner,
            damageInfo.Amount,
            ResolveInstigatorViewId(damageInfo.Instigator),
            (int)damageInfo.SourceAlignment,
            (int)damageInfo.PlayerDamageAnimation,
            (int)damageInfo.PlayerCameraImpact,
            damageInfo.HitPoint,
            damageInfo.HitDirection);
    }

    public void ReceiveKickDamage(DamageInfo damageInfo, Vector3 velocityChange, float controlLockDuration)
    {
        if (CanApplyDamageLocally())
        {
            ApplyKickDamageAndKnockback(damageInfo, velocityChange, controlLockDuration);
            return;
        }

        if (photonView.Owner == null)
            return;

        photonView.RPC(
            nameof(RpcReceiveKickDamage),
            photonView.Owner,
            damageInfo.Amount,
            ResolveInstigatorViewId(damageInfo.Instigator),
            (int)damageInfo.SourceAlignment,
            (int)damageInfo.PlayerDamageAnimation,
            (int)damageInfo.PlayerCameraImpact,
            damageInfo.HitPoint,
            damageInfo.HitDirection,
            velocityChange,
            controlLockDuration);
    }

    public void ReceiveEnemyDamage(
        float amount,
        GameObject instigator,
        Vector3 hitPoint,
        Vector3 hitDirection,
        PlayerDamageAnimationType playerDamageAnimation = PlayerDamageAnimationType.ReactionDamage,
        PlayerCameraImpactType playerCameraImpact = PlayerCameraImpactType.DefaultHit)
    {
        ReceiveDamage(new DamageInfo(
            amount,
            instigator,
            CombatAlignment.Enemy,
            hitPoint,
            hitDirection,
            playerDamageAnimation,
            playerCameraImpact));
    }

    public void ReceiveFallDamage(float amount)
    {
        ReceiveFallDamage(amount, 0f, 0f);
    }

    public void ReceiveFallDamage(float amount, float fallDistance, float downwardSpeed)
    {
        if (!CanApplyDamageLocally())
            return;

        bool wasAlive = IsAlive;

        ApplyDamageAndBroadcast(
            new DamageInfo(amount, null, CombatAlignment.Neutral, transform.position, Vector3.zero, PlayerDamageAnimationType.None),
            suppressKnockback: true);

        if (wasAlive && IsAlive)
            TryStartImpactRagdollFromHighFall(fallDistance, downwardSpeed);
    }

    public void ReceiveEnvironmentalDamage(
        float amount,
        GameObject instigator,
        Vector3 hitPoint,
        Vector3 hitDirection,
        bool ignoreDamageImmunity = true,
        bool suppressKnockback = true,
        PlayerDamageAnimationType playerDamageAnimation = PlayerDamageAnimationType.None,
        PlayerCameraImpactType playerCameraImpact = PlayerCameraImpactType.None)
    {
        if (!CanApplyDamageLocally())
            return;

        ApplyDamageAndBroadcast(
            new DamageInfo(amount, instigator, CombatAlignment.Neutral, hitPoint, hitDirection, playerDamageAnimation, playerCameraImpact),
            suppressKnockback,
            ignoreDamageImmunity);
    }

    public void ReceiveEnvironmentalKill(
        GameObject instigator,
        Vector3 hitPoint,
        Vector3 hitDirection,
        bool ignoreDamageImmunity = true,
        bool suppressKnockback = true)
    {
        if (!IsAlive)
            return;

        float lethalAmount = Mathf.Max(CurrentHealth, MaxHealth) + 1f;
        ReceiveEnvironmentalDamage(
            lethalAmount,
            instigator,
            hitPoint,
            hitDirection,
            ignoreDamageImmunity,
            suppressKnockback,
            PlayerDamageAnimationType.None,
            PlayerCameraImpactType.None);
    }

    public void RegisterEnemyKill()
    {
        if (photonView == null || PhotonNetwork.OfflineMode || !PhotonNetwork.InRoom || IsLocallyOwned)
        {
            IncrementKillCount();
            return;
        }

        if (photonView.Owner != null)
            photonView.RPC(nameof(RpcRegisterEnemyKill), photonView.Owner);
    }

    public float RestoreHealth(float amount)
    {
        if (!IsLocallyOwned)
            return 0f;

        float restoredAmount = RecoverHealth(amount);
        if (restoredAmount > 0f)
            BroadcastLifeState();

        return restoredAmount;
    }

    protected override bool CanReceiveDamage(DamageInfo damageInfo, bool ignoreDamageImmunity)
    {
        return CanApplyDamageLocally() && base.CanReceiveDamage(damageInfo, ignoreDamageImmunity);
    }

    protected override void OnDamaged(DamageInfo damageInfo)
    {
        if (!CanApplyDamageLocally())
            return;

        if (playerMovement != null && damageInfo.PlayerDamageAnimation != PlayerDamageAnimationType.None)
            playerMovement.TriggerDamageAnimation(damageInfo.PlayerDamageAnimation);

        headBobController ??= GetComponentInChildren<HeadBobController>(true);
        if (headBobController != null && damageInfo.PlayerCameraImpact != PlayerCameraImpactType.None)
            headBobController.PlayDamageImpact(damageInfo.PlayerCameraImpact, damageInfo.Amount);

        EmitEnemyImpactReaction(damageInfo);

        if (suppressDamageKnockback)
            return;

        if (!TryBuildDamageKnockback(damageInfo, out Vector3 velocityChange, out float controlLockDuration))
            return;

        if (playerMovement != null)
        {
            playerMovement.ApplyDamageKnockback(velocityChange, controlLockDuration);
            return;
        }

        if (rb != null)
            rb.AddForce(velocityChange, ForceMode.VelocityChange);
    }

    protected override void OnDied(DamageInfo damageInfo)
    {
        CancelImpactRagdollRecovery();

        if (RoomManager.instance != null)
        {
            RoomManager.instance.deaths++;
            RoomManager.instance.SetHashes();
        }

        DropEquippedItemsOnDeath();
        ApplyRagdollState(
            active: true,
            damageInfo.HitPoint,
            damageInfo.HitDirection,
            useDefaultImpulse: true,
            impulse: 0f,
            upward: 0f);
        BroadcastRagdollState(
            active: true,
            damageInfo.HitPoint,
            damageInfo.HitDirection,
            useDefaultImpulse: true,
            impulse: 0f,
            upward: 0f);
        BroadcastLifeState();

        if (!respawnOnDeath)
        {
            if (playerController != null)
                playerController.enabled = false;

            return;
        }

        if (respawnRoutine != null)
            StopCoroutine(respawnRoutine);

        respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        if (playerController != null)
            playerController.enabled = false;

        if (rb != null)
        {
            ClearRootVelocityIfDynamic();
        }

        if (respawnDelay > 0f)
            yield return new WaitForSeconds(respawnDelay);

        Respawn();
        respawnRoutine = null;
    }

    private void Respawn()
    {
        CancelImpactRagdollRecovery();

        ApplyRagdollState(
            active: false,
            Vector3.zero,
            Vector3.zero,
            useDefaultImpulse: false,
            impulse: 0f,
            upward: 0f);

        Transform spawnPoint = ResolveSpawnPoint();
        if (spawnPoint != null)
            transform.SetPositionAndRotation(spawnPoint.position + Vector3.up, spawnPoint.rotation);

        if (rb != null)
        {
            rb.position = transform.position;
            rb.rotation = transform.rotation;
            ClearRootVelocityIfDynamic();
        }

        if (playerMovement != null)
        {
            playerMovement.ClearTemporaryMovementPenalties();
            playerMovement.SetState(MovementState.idle);
        }

        RestoreFullHealth();
        SetInvulnerableFor(respawnInvulnerabilityDuration);
        BroadcastRagdollState(
            active: false,
            Vector3.zero,
            Vector3.zero,
            useDefaultImpulse: false,
            impulse: 0f,
            upward: 0f);
        BroadcastLifeState();

        if (playerController != null)
            playerController.enabled = true;
    }

    protected override void OnReplicatedStateApplied()
    {
        if (IsAlive)
        {
            ApplyRagdollState(
                active: false,
                Vector3.zero,
                Vector3.zero,
                useDefaultImpulse: false,
                impulse: 0f,
                upward: 0f);
            return;
        }

        ApplyRagdollState(
            active: true,
            transform.position + Vector3.up,
            -transform.forward,
            useDefaultImpulse: true,
            impulse: 0f,
            upward: 0f);
    }

    public void RequestDebugRagdollToggle(Vector3 hitPoint, Vector3 hitDirection, float impulse, float upward)
    {
        if (!IsLocallyOwned)
            return;

        EnsureRagdollController();
        if (ragdollController == null)
            return;

        CancelImpactRagdollRecovery();

        bool shouldActivate = !ragdollController.IsRagdollActive;
        ApplyRagdollState(
            shouldActivate,
            hitPoint,
            hitDirection,
            useDefaultImpulse: false,
            impulse,
            upward);
        BroadcastRagdollState(
            shouldActivate,
            hitPoint,
            hitDirection,
            useDefaultImpulse: false,
            impulse,
            upward);
    }

    public bool RequestImpactRagdoll(
        Vector3 hitPoint,
        Vector3 hitDirection,
        float impulse,
        float upward,
        float recoverAfterGroundedSeconds)
    {
        if (CanApplyDamageLocally())
            return ApplyImpactRagdoll(hitPoint, hitDirection, impulse, upward, recoverAfterGroundedSeconds);

        if (photonView == null || photonView.Owner == null)
            return false;

        photonView.RPC(
            nameof(RpcRequestImpactRagdoll),
            photonView.Owner,
            hitPoint,
            hitDirection,
            impulse,
            upward,
            recoverAfterGroundedSeconds);

        return true;
    }

    [PunRPC]
    private void RpcReceiveDamage(
        float amount,
        int instigatorViewId,
        int sourceAlignment,
        int playerDamageAnimation,
        int playerCameraImpact,
        Vector3 hitPoint,
        Vector3 hitDirection)
    {
        GameObject instigator = ResolveInstigatorObject(instigatorViewId);
        ApplyDamageAndBroadcast(new DamageInfo(
            amount,
            instigator,
            (CombatAlignment)sourceAlignment,
            hitPoint,
            hitDirection,
            (PlayerDamageAnimationType)playerDamageAnimation,
            (PlayerCameraImpactType)playerCameraImpact));
    }

    [PunRPC]
    private void RpcReceiveKickDamage(
        float amount,
        int instigatorViewId,
        int sourceAlignment,
        int playerDamageAnimation,
        int playerCameraImpact,
        Vector3 hitPoint,
        Vector3 hitDirection,
        Vector3 velocityChange,
        float controlLockDuration)
    {
        GameObject instigator = ResolveInstigatorObject(instigatorViewId);
        ApplyKickDamageAndKnockback(
            new DamageInfo(
                amount,
                instigator,
                (CombatAlignment)sourceAlignment,
                hitPoint,
                hitDirection,
                (PlayerDamageAnimationType)playerDamageAnimation,
                (PlayerCameraImpactType)playerCameraImpact),
            velocityChange,
            controlLockDuration);
    }

    [PunRPC]
    private void RpcRequestImpactRagdoll(
        Vector3 hitPoint,
        Vector3 hitDirection,
        float impulse,
        float upward,
        float recoverAfterGroundedSeconds)
    {
        if (!CanApplyDamageLocally())
            return;

        ApplyImpactRagdoll(hitPoint, hitDirection, impulse, upward, recoverAfterGroundedSeconds);
    }

    [PunRPC]
    private void RpcRegisterEnemyKill()
    {
        IncrementKillCount();
    }

    [PunRPC]
    private void RpcSyncLifeState(float replicatedHealth, bool replicatedIsAlive)
    {
        if (IsLocallyOwned)
            return;

        ApplyReplicatedState(replicatedHealth, replicatedIsAlive);
    }

    [PunRPC]
    private void RpcSetRagdollState(
        bool active,
        Vector3 hitPoint,
        Vector3 hitDirection,
        Vector3 rootPosition,
        Quaternion rootRotation,
        bool useDefaultImpulse,
        float impulse,
        float upward)
    {
        if (IsLocallyOwned)
            return;

        ApplyNetworkRootPose(rootPosition, rootRotation);
        ApplyRagdollState(active, hitPoint, hitDirection, useDefaultImpulse, impulse, upward);
    }

    private void BroadcastLifeState()
    {
        if (!IsLocallyOwned || photonView == null || !PhotonNetwork.InRoom)
            return;

        photonView.RPC(nameof(RpcSyncLifeState), RpcTarget.Others, CurrentHealth, IsAlive);
    }

    private void BroadcastRagdollState(
        bool active,
        Vector3 hitPoint,
        Vector3 hitDirection,
        bool useDefaultImpulse,
        float impulse,
        float upward)
    {
        if (!IsLocallyOwned || photonView == null || !PhotonNetwork.InRoom)
            return;

        photonView.RPC(
            nameof(RpcSetRagdollState),
            RpcTarget.Others,
            active,
            hitPoint,
            hitDirection,
            transform.position,
            transform.rotation,
            useDefaultImpulse,
            impulse,
            upward);
    }

    private void ApplyRagdollState(
        bool active,
        Vector3 hitPoint,
        Vector3 hitDirection,
        bool useDefaultImpulse,
        float impulse,
        float upward)
    {
        if (!active)
            CancelImpactRagdollRecovery();

        EnsureRagdollController();
        if (ragdollController == null)
            return;

        if (active)
        {
            if (ragdollController.IsRagdollActive)
                return;

            if (useDefaultImpulse)
            {
                ragdollController.ActivateRagdoll(new DamageInfo(
                    0f,
                    null,
                    CombatAlignment.Neutral,
                    hitPoint,
                    hitDirection));
                return;
            }

            ragdollController.ActivateRagdoll(hitPoint, hitDirection, impulse, upward);
            return;
        }

        if (!ragdollController.IsRagdollActive)
            return;

        ragdollController.SetAnimatedState();
    }

    private void ApplyNetworkRootPose(Vector3 rootPosition, Quaternion rootRotation)
    {
        transform.SetPositionAndRotation(rootPosition, rootRotation);

        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (rb == null)
            return;

        rb.position = rootPosition;
        rb.rotation = rootRotation;
        ClearRootVelocityIfDynamic();
    }

    private void EnsureRagdollController()
    {
        if (ragdollController == null)
            ragdollController = GetComponent<PlayerRagdollController>();
    }

    private void ApplyDamageAndBroadcast(DamageInfo damageInfo, bool suppressKnockback = false, bool ignoreDamageImmunity = false)
    {
        float previousHealth = CurrentHealth;
        bool wasAlive = IsAlive;
        suppressDamageKnockback = suppressKnockback;
        if (ignoreDamageImmunity)
            ApplyDamageIgnoringImmunity(damageInfo);
        else
            ApplyDamage(damageInfo);
        suppressDamageKnockback = false;

        if (!Mathf.Approximately(CurrentHealth, previousHealth) || IsAlive != wasAlive)
            BroadcastLifeState();
    }

    private void ApplyKickDamageAndKnockback(DamageInfo damageInfo, Vector3 velocityChange, float controlLockDuration)
    {
        bool wasAlive = IsAlive;
        ApplyDamageAndBroadcast(damageInfo, suppressKnockback: true);

        if (!wasAlive || !IsAlive)
            return;

        ApplyExplicitKnockback(velocityChange, controlLockDuration);
        TryStartImpactRagdollFromStrongKnockback(damageInfo, velocityChange);
    }

    private void ApplyExplicitKnockback(Vector3 velocityChange, float controlLockDuration)
    {
        if (velocityChange.sqrMagnitude <= 0.0001f)
            return;

        if (playerMovement != null)
        {
            playerMovement.ApplyDamageKnockback(velocityChange, controlLockDuration);
            return;
        }

        if (rb != null)
            rb.AddForce(velocityChange, ForceMode.VelocityChange);
    }

    private void DropEquippedItemsOnDeath()
    {
        handEquipmentController ??= GetComponent<HandEquipmentController>();
        handEquipmentController?.DropAllEquippedItemsOnDeath();
    }

    private bool ApplyImpactRagdoll(
        Vector3 hitPoint,
        Vector3 hitDirection,
        float impulse,
        float upward,
        float recoverAfterGroundedSeconds)
    {
        if (!CanApplyDamageLocally() || !IsAlive)
            return false;

        EnsureRagdollController();
        if (ragdollController == null)
            return false;

        if (ragdollController.IsRagdollActive)
            return false;

        ApplyRagdollState(
            active: true,
            hitPoint,
            hitDirection,
            useDefaultImpulse: false,
            Mathf.Max(0f, impulse),
            Mathf.Max(0f, upward));
        BroadcastRagdollState(
            active: true,
            hitPoint,
            hitDirection,
            useDefaultImpulse: false,
            Mathf.Max(0f, impulse),
            Mathf.Max(0f, upward));
        StartImpactRagdollRecovery(recoverAfterGroundedSeconds);
        return true;
    }

    private void TryStartImpactRagdollFromStrongKnockback(DamageInfo damageInfo, Vector3 velocityChange)
    {
        if (!enableRagdollFromStrongKnockback || velocityChange.sqrMagnitude <= 0.0001f)
            return;

        float knockbackStrength = velocityChange.magnitude;
        if (knockbackStrength < minStrongKnockbackForImpactRagdoll)
            return;

        Vector3 hitDirection = damageInfo.HitDirection.sqrMagnitude > 0.0001f
            ? damageInfo.HitDirection
            : velocityChange;
        float impulse = ApplyMaxValue(
            knockbackStrength * strongKnockbackImpactImpulseMultiplier,
            maxStrongKnockbackImpactImpulse);
        float upward = ApplyMaxValue(
            Mathf.Max(0f, velocityChange.y) * strongKnockbackImpactUpwardMultiplier,
            maxStrongKnockbackImpactUpward);

        ApplyImpactRagdoll(
            damageInfo.HitPoint,
            hitDirection,
            impulse,
            upward,
            strongKnockbackRecoverAfterGroundedSeconds);
    }

    private void TryStartImpactRagdollFromHighFall(float fallDistance, float downwardSpeed)
    {
        if (!enableHighFallRagdoll)
            return;

        fallDistance = Mathf.Max(0f, fallDistance);
        downwardSpeed = Mathf.Max(0f, downwardSpeed);

        if (fallDistance < minFallHeightForRagdoll && downwardSpeed < minFallDownSpeedForRagdoll)
            return;

        float heightExcess = Mathf.Max(0f, fallDistance - minFallHeightForRagdoll);
        float speedExcess = Mathf.Max(0f, downwardSpeed - minFallDownSpeedForRagdoll);
        float impulse = highFallBaseImpactImpulse
            + heightExcess * highFallDistanceImpulseMultiplier
            + speedExcess * highFallDownSpeedImpulseMultiplier;
        impulse = ApplyMaxValue(impulse, maxHighFallImpactImpulse);
        float upward = ApplyMaxValue(highFallUpwardImpulse, maxHighFallImpactUpward);

        Vector3 hitDirection = rb != null
            ? Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up)
            : Vector3.zero;
        if (hitDirection.sqrMagnitude <= 0.0001f)
            hitDirection = -transform.forward;

        ApplyImpactRagdoll(
            transform.position,
            hitDirection,
            impulse,
            upward,
            highFallRecoverAfterGroundedSeconds);
    }

    private void StartImpactRagdollRecovery(float recoverAfterGroundedSeconds)
    {
        CancelImpactRagdollRecovery();

        if (recoverAfterGroundedSeconds <= 0f)
        {
            ApplyRagdollState(
                active: false,
                Vector3.zero,
                Vector3.zero,
                useDefaultImpulse: false,
                impulse: 0f,
                upward: 0f);
            BroadcastRagdollState(
                active: false,
                Vector3.zero,
                Vector3.zero,
                useDefaultImpulse: false,
                impulse: 0f,
                upward: 0f);
            return;
        }

        impactRagdollRecoveryRoutine = StartCoroutine(ImpactRagdollRecoveryRoutine(recoverAfterGroundedSeconds));
    }

    private IEnumerator ImpactRagdollRecoveryRoutine(float recoverAfterGroundedSeconds)
    {
        float groundedTime = 0f;

        while (ragdollController != null && ragdollController.IsRagdollActive)
        {
            yield return new WaitForFixedUpdate();

            if (!IsAlive)
            {
                impactRagdollRecoveryRoutine = null;
                yield break;
            }

            if (ragdollController.HasRagdollGroundContact())
                groundedTime += Time.fixedDeltaTime;
            else
                groundedTime = 0f;

            if (groundedTime < recoverAfterGroundedSeconds)
                continue;

            impactRagdollRecoveryRoutine = null;
            ApplyRagdollState(
                active: false,
                Vector3.zero,
                Vector3.zero,
                useDefaultImpulse: false,
                impulse: 0f,
                upward: 0f);
            BroadcastRagdollState(
                active: false,
                Vector3.zero,
                Vector3.zero,
                useDefaultImpulse: false,
                impulse: 0f,
                upward: 0f);
            yield break;
        }

        impactRagdollRecoveryRoutine = null;
    }

    private void CancelImpactRagdollRecovery()
    {
        if (impactRagdollRecoveryRoutine == null)
            return;

        StopCoroutine(impactRagdollRecoveryRoutine);
        impactRagdollRecoveryRoutine = null;
    }

    private static float ApplyMaxValue(float value, float maxValue)
    {
        value = Mathf.Max(0f, value);
        return maxValue > 0f ? Mathf.Min(value, maxValue) : value;
    }

    private void ClearRootVelocityIfDynamic()
    {
        if (rb == null || rb.isKinematic)
            return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void EmitEnemyImpactReaction(DamageInfo damageInfo)
    {
        if (damageInfo.SourceAlignment != CombatAlignment.Enemy)
            return;

        ResolveMeleeImpactReceiver();
        meleeImpactReceiver?.ReceiveMeleeImpact(damageInfo, null);
    }

    private void ResolveMeleeImpactReceiver()
    {
        if (meleeImpactReceiver != null)
            return;

        meleeImpactReceiver = GetComponent<IMeleeImpactReceiver>();
        if (meleeImpactReceiver != null)
            return;

        meleeImpactReceiver = GetComponentInParent<IMeleeImpactReceiver>();
        if (meleeImpactReceiver != null)
            return;

        meleeImpactReceiver = GetComponentInChildren<IMeleeImpactReceiver>(true);
    }

    private bool CanApplyDamageLocally()
    {
        return photonView == null
            || PhotonNetwork.OfflineMode
            || !PhotonNetwork.InRoom
            || photonView.IsMine;
    }

    private void IncrementKillCount()
    {
        if (RoomManager.instance == null)
            return;

        RoomManager.instance.kills++;
        RoomManager.instance.SetHashes();
    }

    private static int ResolveInstigatorViewId(GameObject instigator)
    {
        PhotonView instigatorView = instigator != null ? instigator.GetComponentInParent<PhotonView>() : null;
        return instigatorView != null ? instigatorView.ViewID : 0;
    }

    private static GameObject ResolveInstigatorObject(int viewId)
    {
        PhotonView instigatorView = viewId != 0 ? PhotonView.Find(viewId) : null;
        return instigatorView != null ? instigatorView.gameObject : null;
    }

    private static Transform ResolveSpawnPoint()
    {
        if (RoomManager.instance != null && RoomManager.instance.spawnPoints != null)
        {
            for (int i = 0; i < RoomManager.instance.spawnPoints.Length; i++)
            {
                Transform spawnPoint = RoomManager.instance.spawnPoints[i];
                if (spawnPoint != null)
                    return spawnPoint;
            }
        }

        Transform[] sceneTransforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform sceneTransform = sceneTransforms[i];
            if (sceneTransform != null && string.Equals(sceneTransform.name, SpawnPointName, System.StringComparison.Ordinal))
                return sceneTransform;
        }

        return null;
    }
}
