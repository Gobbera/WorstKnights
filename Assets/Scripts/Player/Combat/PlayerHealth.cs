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

    private PhotonView photonView;
    private PlayerController playerController;
    private PlayerMovement playerMovement;
    private HeadBobController headBobController;
    private Rigidbody rb;
    private HandEquipmentController handEquipmentController;
    private IMeleeImpactReceiver meleeImpactReceiver;
    private Coroutine respawnRoutine;
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
        if (!CanApplyDamageLocally())
            return;

        ApplyDamageAndBroadcast(
            new DamageInfo(amount, null, CombatAlignment.Neutral, transform.position, Vector3.zero, PlayerDamageAnimationType.None),
            suppressKnockback: true);
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
        if (RoomManager.instance != null)
        {
            RoomManager.instance.deaths++;
            RoomManager.instance.SetHashes();
        }

        DropEquippedItemsOnDeath();
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
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (respawnDelay > 0f)
            yield return new WaitForSeconds(respawnDelay);

        Respawn();
        respawnRoutine = null;
    }

    private void Respawn()
    {
        Transform spawnPoint = ResolveSpawnPoint();
        if (spawnPoint != null)
            transform.SetPositionAndRotation(spawnPoint.position + Vector3.up, spawnPoint.rotation);

        if (rb != null)
        {
            rb.position = transform.position;
            rb.rotation = transform.rotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (playerMovement != null)
        {
            playerMovement.ClearTemporaryMovementPenalties();
            playerMovement.SetState(MovementState.idle);
        }

        RestoreFullHealth();
        SetInvulnerableFor(respawnInvulnerabilityDuration);
        BroadcastLifeState();

        if (playerController != null)
            playerController.enabled = true;
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

    private void BroadcastLifeState()
    {
        if (!IsLocallyOwned || photonView == null || !PhotonNetwork.InRoom)
            return;

        photonView.RPC(nameof(RpcSyncLifeState), RpcTarget.Others, CurrentHealth, IsAlive);
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
