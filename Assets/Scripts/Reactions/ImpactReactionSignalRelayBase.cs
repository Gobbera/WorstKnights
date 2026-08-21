using Photon.Pun;
using UnityEngine;

public abstract class ImpactReactionSignalRelayBase : MonoBehaviour, IMeleeImpactReceiver
{
    [HideInInspector] [SerializeField] private ReactionSignalEmitter signalEmitter;
    [HideInInspector] [SerializeField] private ReactionSignalReceiver signalReceiver;
    [HideInInspector] [SerializeField] private PhotonView photonView;
    [Header("Signals")]
    [SerializeField] private string signalId = "Hit";
    [Header("Networking")]
    [SerializeField] private bool broadcastInMultiplayer = true;

    public string SignalId => signalId;

    protected virtual void Reset()
    {
        ResolveSignalReferences();
    }

    protected virtual void Awake()
    {
        ResolveSignalReferences();
    }

    protected virtual void OnValidate()
    {
        ResolveSignalReferences();
    }

    public void ReceiveMeleeImpact(DamageInfo damageInfo, Collider hitCollider)
    {
        ResolveSignalReferences();
        if (string.IsNullOrWhiteSpace(signalId))
            return;

        Vector3 hitPoint = ResolveHitPoint(damageInfo, hitCollider);
        ReactionSignalContext signalContext = ReactionSignalContext.FromDamageInfo(damageInfo);
        EmitSignal(signalId, hitPoint, damageInfo.HitDirection, signalContext, broadcast: true);
    }

    [PunRPC]
    public void RpcReceiveImpactReactionSignal(
        string receivedSignalId,
        Vector3 worldPosition,
        Vector3 worldDirection,
        float impactVfxAttackAngle,
        bool hasImpactVfxAttackAngle)
    {
        ResolveSignalReferences();
        if (string.IsNullOrWhiteSpace(receivedSignalId))
            return;

        ReactionSignalContext signalContext = new ReactionSignalContext(
            impactVfxAttackAngle,
            hasImpactVfxAttackAngle);
        EmitSignal(receivedSignalId, worldPosition, worldDirection, signalContext, broadcast: false);
    }

    private void EmitSignal(
        string emittedSignalId,
        Vector3 worldPosition,
        Vector3 worldDirection,
        ReactionSignalContext signalContext,
        bool broadcast)
    {
        if (string.IsNullOrWhiteSpace(emittedSignalId))
            return;

        if (signalEmitter != null && signalEmitter.TryEmit(emittedSignalId, worldPosition, worldDirection, signalContext))
        {
            TryBroadcastSignal(emittedSignalId, worldPosition, worldDirection, signalContext, broadcast);
            return;
        }

        signalReceiver?.ReceiveSignal(emittedSignalId, worldPosition, worldDirection, signalContext);
        TryBroadcastSignal(emittedSignalId, worldPosition, worldDirection, signalContext, broadcast);
    }

    private void TryBroadcastSignal(
        string emittedSignalId,
        Vector3 worldPosition,
        Vector3 worldDirection,
        ReactionSignalContext signalContext,
        bool broadcast)
    {
        if (!broadcast || !CanBroadcastInMultiplayer())
            return;

        photonView.RPC(
            nameof(RpcReceiveImpactReactionSignal),
            RpcTarget.Others,
            emittedSignalId,
            worldPosition,
            worldDirection,
            signalContext.ImpactVfxAttackAngle,
            signalContext.HasImpactVfxAttackAngle);
    }

    private bool CanBroadcastInMultiplayer()
    {
        if (!broadcastInMultiplayer
            || PhotonNetwork.OfflineMode
            || !PhotonNetwork.InRoom)
        {
            return false;
        }

        ResolvePhotonView();
        return photonView != null && photonView.ViewID != 0;
    }

    private Vector3 ResolveHitPoint(DamageInfo damageInfo, Collider hitCollider)
    {
        if (IsFiniteVector(damageInfo.HitPoint))
            return damageInfo.HitPoint;

        if (hitCollider != null)
            return hitCollider.ClosestPoint(transform.position);

        return transform.position;
    }

    private void ResolveSignalReferences()
    {
        if (signalEmitter == null)
            signalEmitter = GetComponent<ReactionSignalEmitter>();

        if (signalReceiver == null)
            ReactionSignalEmitter.TryResolveReceiver(this, out signalReceiver);

        ResolvePhotonView();
    }

    private void ResolvePhotonView()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z));
    }
}
