using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Destruction/Destructible Reaction Signal Bridge")]
public class DestructibleReactionSignalBridge : MonoBehaviour
{
    [HideInInspector] [SerializeField] private DestructibleObjectController destructible;
    [HideInInspector] [SerializeField] private ReactionSignalEmitter signalEmitter;
    [HideInInspector] [SerializeField] private ReactionSignalReceiver signalReceiver;
    [Header("Signals")]
    [SerializeField] private string damagedSignalId = "Damaged";
    [SerializeField] private string destroyedSignalId = "Destroyed";

    private bool subscribed;

    public string DamagedSignalId => damagedSignalId;
    public string DestroyedSignalId => destroyedSignalId;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void HandleDamaged(DestructibleObjectController _, DamageInfo damageInfo)
    {
        if (string.IsNullOrWhiteSpace(damagedSignalId))
            return;

        ReactionSignalContext signalContext = ReactionSignalContext.FromDamageInfo(damageInfo);
        if (signalEmitter != null && signalEmitter.TryEmit(damagedSignalId, damageInfo.HitPoint, damageInfo.HitDirection, signalContext))
            return;

        signalReceiver?.ReceiveSignal(damagedSignalId, damageInfo.HitPoint, damageInfo.HitDirection, signalContext);
    }

    private void HandleDestroyed(DestructibleObjectController _, DamageInfo damageInfo)
    {
        if (string.IsNullOrWhiteSpace(destroyedSignalId))
            return;

        ReactionSignalContext signalContext = ReactionSignalContext.FromDamageInfo(damageInfo);
        if (signalEmitter != null && signalEmitter.TryEmit(destroyedSignalId, damageInfo.HitPoint, damageInfo.HitDirection, signalContext))
            return;

        signalReceiver?.ReceiveSignal(destroyedSignalId, damageInfo.HitPoint, damageInfo.HitDirection, signalContext);
    }

    private void Subscribe()
    {
        if (subscribed || destructible == null)
            return;

        destructible.Damaged += HandleDamaged;
        destructible.Destroyed += HandleDestroyed;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || destructible == null)
            return;

        destructible.Damaged -= HandleDamaged;
        destructible.Destroyed -= HandleDestroyed;
        subscribed = false;
    }

    private void ResolveReferences()
    {
        if (destructible == null)
            destructible = GetComponent<DestructibleObjectController>();

        if (signalEmitter == null)
            signalEmitter = GetComponent<ReactionSignalEmitter>();

        if (signalReceiver == null)
            signalReceiver = GetComponent<ReactionSignalReceiver>();
    }
}
