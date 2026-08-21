using UnityEngine;

public enum ReactionSignalTargetMode
{
    SelfReceiver = 0,
    OtherReceiver = 1
}

[DisallowMultipleComponent]
[AddComponentMenu("World/Reactions/Reaction Signal Emitter")]
public class ReactionSignalEmitter : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private ReactionSignalReceiver signalReceiver;

    public ReactionSignalReceiver SignalReceiver => signalReceiver;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    public bool TryEmit(string signalId)
    {
        return TryEmit(signalId, transform.position, transform.forward);
    }

    public bool TryEmitFromHere(string signalId)
    {
        return TryEmit(signalId, transform.position, transform.forward);
    }

    public bool TryEmit(string signalId, Vector3 worldPosition, Vector3 worldDirection)
    {
        return TryEmit(signalId, worldPosition, worldDirection, ReactionSignalContext.Empty);
    }

    public bool TryEmit(string signalId, Vector3 worldPosition, Vector3 worldDirection, ReactionSignalContext context)
    {
        ResolveReferences();
        return TryEmitToReceiver(signalReceiver, signalId, worldPosition, worldDirection, context);
    }

    public bool TryEmitToReceiver(ReactionSignalReceiver targetReceiver, string signalId, Vector3 worldPosition, Vector3 worldDirection)
    {
        return TryEmitToReceiver(targetReceiver, signalId, worldPosition, worldDirection, ReactionSignalContext.Empty);
    }

    public bool TryEmitToReceiver(
        ReactionSignalReceiver targetReceiver,
        string signalId,
        Vector3 worldPosition,
        Vector3 worldDirection,
        ReactionSignalContext context)
    {
        if (targetReceiver == null || string.IsNullOrWhiteSpace(signalId))
            return false;

        targetReceiver.ReceiveSignal(signalId, worldPosition, worldDirection, context);
        return true;
    }

    public static bool TryEmit(Component targetSource, string signalId, Vector3 worldPosition, Vector3 worldDirection)
    {
        return TryEmit(targetSource, signalId, worldPosition, worldDirection, ReactionSignalContext.Empty);
    }

    public static bool TryEmit(
        Component targetSource,
        string signalId,
        Vector3 worldPosition,
        Vector3 worldDirection,
        ReactionSignalContext context)
    {
        if (!TryResolveReceiver(targetSource, out ReactionSignalReceiver receiver) || string.IsNullOrWhiteSpace(signalId))
            return false;

        receiver.ReceiveSignal(signalId, worldPosition, worldDirection, context);
        return true;
    }

    public static bool TryResolveReceiver(Component source, out ReactionSignalReceiver receiver)
    {
        receiver = null;
        if (source == null)
            return false;

        receiver = source.GetComponent<ReactionSignalReceiver>();
        if (receiver != null)
            return true;

        receiver = source.GetComponentInParent<ReactionSignalReceiver>();
        if (receiver != null)
            return true;

        receiver = source.GetComponentInChildren<ReactionSignalReceiver>();
        return receiver != null;
    }

    private void ResolveReferences()
    {
        if (signalReceiver == null)
            TryResolveReceiver(this, out signalReceiver);
    }
}
