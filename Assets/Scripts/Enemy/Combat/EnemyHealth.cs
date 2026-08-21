using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyHealth : CombatHealth
{
    [Header("Death")]
    [SerializeField] private bool destroyOnDeath = true;
    [SerializeField] [Min(0f)] private float destroyDelay = 0f;

    private EnemySetup enemySetup;
    private PhotonView photonView;
    private bool destroyScheduled;
    private bool suppressDamageKnockback;

    public override CombatAlignment Alignment => CombatAlignment.Enemy;
    public int DamageSequence { get; private set; }
    public bool DestroysOnDeath => destroyOnDeath;

    protected override void Awake()
    {
        photonView = GetComponent<PhotonView>();
        enemySetup = GetComponent<EnemySetup>();
        base.Awake();
    }

    public void ReceivePlayerDamage(DamageInfo damageInfo)
    {
        if (CanApplyDamageLocally())
        {
            ApplyDamage(damageInfo);
            return;
        }

        Player targetClient = ResolveAuthorityClient();
        if (targetClient == null)
            return;

        photonView.RPC(
            nameof(RpcRequestDamage),
            targetClient,
            damageInfo.Amount,
            ResolveInstigatorViewId(damageInfo.Instigator),
            (int)damageInfo.SourceAlignment,
            damageInfo.HitPoint,
            damageInfo.HitDirection);
    }

    public void ReceivePlayerKick(
        DamageInfo damageInfo,
        float baseKnockbackForce,
        float upwardKnockbackForce,
        float knockbackDuration)
    {
        if (CanApplyDamageLocally())
        {
            ApplyKickDamageAndKnockback(damageInfo, baseKnockbackForce, upwardKnockbackForce, knockbackDuration);
            return;
        }

        Player targetClient = ResolveAuthorityClient();
        if (targetClient == null)
            return;

        photonView.RPC(
            nameof(RpcRequestKick),
            targetClient,
            damageInfo.Amount,
            ResolveInstigatorViewId(damageInfo.Instigator),
            (int)damageInfo.SourceAlignment,
            damageInfo.HitPoint,
            damageInfo.HitDirection,
            baseKnockbackForce,
            upwardKnockbackForce,
            knockbackDuration);
    }

    public void ApplyNetworkState(float replicatedHealth, bool replicatedIsAlive)
    {
        ApplyReplicatedState(replicatedHealth, replicatedIsAlive);
    }

    public void ApplyReplicatedDamageSequence(int sequence)
    {
        DamageSequence = Mathf.Max(0, sequence);
    }

    protected override bool CanReceiveDamage(DamageInfo damageInfo, bool ignoreDamageImmunity)
    {
        return CanApplyDamageLocally() && base.CanReceiveDamage(damageInfo, ignoreDamageImmunity);
    }

    protected override void OnDamaged(DamageInfo damageInfo)
    {
        if (CurrentHealth > 0f)
            DamageSequence++;

        if (suppressDamageKnockback)
            return;

        if (!TryBuildDamageKnockback(damageInfo, out Vector3 velocityChange, out float controlLockDuration))
            return;

        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        EnemyMotor enemyMotor = enemySetup != null ? enemySetup.EnemyMotor : GetComponent<EnemyMotor>();
        enemyMotor?.ApplyDamageKnockback(velocityChange, controlLockDuration);
    }

    protected override void OnDied(DamageInfo damageInfo)
    {
        if (damageInfo.SourceAlignment == CombatAlignment.Player)
            AwardKillToInstigator(damageInfo.Instigator);

        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        EnemyAttack enemyAttack = enemySetup != null ? enemySetup.EnemyAttack : GetComponent<EnemyAttack>();
        enemyAttack?.CancelCurrentAttack();

        if (enemySetup != null)
        {
            enemySetup.ApplyAliveState(false);
            enemySetup.ApplySimulationState(CanApplyDamageLocally(), false);
        }

        TryScheduleDestroyOnDeath();
    }

    protected override void OnReplicatedStateApplied()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemySetup == null)
            return;

        enemySetup.ApplyAliveState(IsAlive);
        enemySetup.ApplySimulationState(false, IsAlive);

        if (!IsAlive)
        {
            EnemyAttack enemyAttack = enemySetup.EnemyAttack != null ? enemySetup.EnemyAttack : GetComponent<EnemyAttack>();
            enemyAttack?.CancelCurrentAttack();
        }

        if (!IsAlive && destroyOnDeath)
            TryScheduleDestroyLocally(ResolveDestroyDelay());
    }

    [PunRPC]
    private void RpcRequestDamage(
        float amount,
        int instigatorViewId,
        int sourceAlignment,
        Vector3 hitPoint,
        Vector3 hitDirection)
    {
        if (!CanApplyDamageLocally())
            return;

        GameObject instigator = ResolveInstigatorObject(instigatorViewId);
        ApplyDamage(new DamageInfo(amount, instigator, (CombatAlignment)sourceAlignment, hitPoint, hitDirection));
    }

    [PunRPC]
    private void RpcRequestKick(
        float amount,
        int instigatorViewId,
        int sourceAlignment,
        Vector3 hitPoint,
        Vector3 hitDirection,
        float baseKnockbackForce,
        float upwardKnockbackForce,
        float knockbackDuration)
    {
        if (!CanApplyDamageLocally())
            return;

        GameObject instigator = ResolveInstigatorObject(instigatorViewId);
        ApplyKickDamageAndKnockback(
            new DamageInfo(amount, instigator, (CombatAlignment)sourceAlignment, hitPoint, hitDirection),
            baseKnockbackForce,
            upwardKnockbackForce,
            knockbackDuration);
    }

    private void ApplyKickDamageAndKnockback(
        DamageInfo damageInfo,
        float baseKnockbackForce,
        float upwardKnockbackForce,
        float knockbackDuration)
    {
        bool wasAlive = IsAlive;
        bool previousSuppressDamageKnockback = suppressDamageKnockback;
        suppressDamageKnockback = true;

        try
        {
            ApplyDamage(damageInfo);
        }
        finally
        {
            suppressDamageKnockback = previousSuppressDamageKnockback;
        }

        if (!wasAlive || !IsAlive)
            return;

        ApplyKickKnockback(damageInfo, baseKnockbackForce, upwardKnockbackForce, knockbackDuration);
    }

    private void ApplyKickKnockback(
        DamageInfo damageInfo,
        float baseKnockbackForce,
        float upwardKnockbackForce,
        float knockbackDuration)
    {
        EnemyKickReaction kickReaction = GetComponent<EnemyKickReaction>();
        if (kickReaction != null && !kickReaction.CanBePushedByKick)
            return;

        float reactionMultiplier = kickReaction != null ? kickReaction.KnockbackMultiplier : 1f;
        float durationMultiplier = kickReaction != null ? kickReaction.DurationMultiplier : 1f;
        float horizontalStrength = Mathf.Max(0f, baseKnockbackForce) * reactionMultiplier;
        float upwardStrength = Mathf.Max(0f, upwardKnockbackForce) * reactionMultiplier;
        float duration = Mathf.Max(0f, knockbackDuration) * durationMultiplier;

        if (horizontalStrength <= 0.0001f && upwardStrength <= 0.0001f)
            return;

        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        EnemyMotor enemyMotor = enemySetup != null ? enemySetup.EnemyMotor : GetComponent<EnemyMotor>();
        if (enemyMotor == null)
            return;

        Vector3 planarDirection = ResolveKickKnockbackDirection(damageInfo);
        enemyMotor.ApplyDamageKnockback(planarDirection * horizontalStrength + Vector3.up * upwardStrength, duration);
    }

    private Vector3 ResolveKickKnockbackDirection(DamageInfo damageInfo)
    {
        Vector3 hitDirection = Vector3.ProjectOnPlane(damageInfo.HitDirection, Vector3.up);
        if (hitDirection.sqrMagnitude > 0.0001f)
            return hitDirection.normalized;

        if (damageInfo.Instigator != null)
        {
            Vector3 awayFromInstigator = Vector3.ProjectOnPlane(transform.position - damageInfo.Instigator.transform.position, Vector3.up);
            if (awayFromInstigator.sqrMagnitude > 0.0001f)
                return awayFromInstigator.normalized;
        }

        Vector3 fallbackDirection = Vector3.ProjectOnPlane(transform.position - damageInfo.HitPoint, Vector3.up);
        if (fallbackDirection.sqrMagnitude > 0.0001f)
            return fallbackDirection.normalized;

        Vector3 selfBackward = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
        return selfBackward.sqrMagnitude > 0.0001f ? selfBackward.normalized : Vector3.back;
    }

    private bool CanApplyDamageLocally()
    {
        return photonView == null
            || PhotonNetwork.OfflineMode
            || !PhotonNetwork.InRoom
            || photonView.IsMine;
    }

    private void TryScheduleDestroyOnDeath()
    {
        if (!destroyOnDeath || destroyScheduled)
            return;

        float delay = ResolveDestroyDelay();
        if (photonView != null
            && PhotonNetwork.InRoom
            && photonView.ViewID != 0
            && CanApplyDamageLocally())
        {
            photonView.RPC(nameof(RpcScheduleDestroy), RpcTarget.All, delay);
            return;
        }

        TryScheduleDestroyLocally(delay);
    }

    [PunRPC]
    private void RpcScheduleDestroy(float delay)
    {
        TryScheduleDestroyLocally(Mathf.Max(0f, delay));
    }

    private void TryScheduleDestroyLocally(float delay)
    {
        if (destroyScheduled)
            return;

        destroyScheduled = true;
        Destroy(gameObject, Mathf.Max(0f, delay));
    }

    private float ResolveDestroyDelay()
    {
        float delay = Mathf.Max(0f, destroyDelay);

        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        EnemyAnimationController enemyAnimationController = enemySetup != null
            ? enemySetup.EnemyAnimationController
            : GetComponent<EnemyAnimationController>();

        if (enemyAnimationController != null
            && enemyAnimationController.TryGetDeathSequenceDestroyDelay(out float animationDelay))
        {
            delay = Mathf.Max(delay, animationDelay);
        }

        return delay;
    }

    private Player ResolveAuthorityClient()
    {
        if (photonView == null || !PhotonNetwork.InRoom)
            return null;

        if (photonView.IsRoomView)
            return PhotonNetwork.MasterClient;

        return photonView.Owner;
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

    private static void AwardKillToInstigator(GameObject instigator)
    {
        PlayerHealth instigatorHealth = instigator != null ? instigator.GetComponentInParent<PlayerHealth>() : null;
        if (instigatorHealth != null)
        {
            instigatorHealth.RegisterEnemyKill();
            return;
        }

        if (RoomManager.instance == null)
            return;

        RoomManager.instance.kills++;
        RoomManager.instance.SetHashes();
    }
}
