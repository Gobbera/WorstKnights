using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Doors/Door Reaction Signal Bridge")]
public class DoorReactionSignalBridge : MonoBehaviour
{
    [HideInInspector] [SerializeField] private DoorController door;
    [HideInInspector] [SerializeField] private ReactionSignalEmitter signalEmitter;
    [HideInInspector] [SerializeField] private ReactionSignalReceiver signalReceiver;
    [Header("Signals")]
    [SerializeField] private string openedSignalId = "Opened";
    [SerializeField] private string closedSignalId = "Closed";
    [SerializeField] private string lockedSignalId = "Locked";
    [SerializeField] private string unlockedSignalId = "Unlocked";

    private bool subscribed;

    public string OpenedSignalId => openedSignalId;
    public string ClosedSignalId => closedSignalId;
    public string LockedSignalId => lockedSignalId;
    public string UnlockedSignalId => unlockedSignalId;

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

    private void HandleOpened(DoorController source)
    {
        EmitSignal(openedSignalId, source);
    }

    private void HandleClosed(DoorController source)
    {
        EmitSignal(closedSignalId, source);
    }

    private void HandleLocked(DoorController source)
    {
        EmitSignal(lockedSignalId, source);
    }

    private void HandleUnlocked(DoorController source)
    {
        EmitSignal(unlockedSignalId, source);
    }

    private void Subscribe()
    {
        if (subscribed || door == null)
            return;

        door.Opened += HandleOpened;
        door.Closed += HandleClosed;
        door.Locked += HandleLocked;
        door.Unlocked += HandleUnlocked;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || door == null)
            return;

        door.Opened -= HandleOpened;
        door.Closed -= HandleClosed;
        door.Locked -= HandleLocked;
        door.Unlocked -= HandleUnlocked;
        subscribed = false;
    }

    private void EmitSignal(string signalId, DoorController source)
    {
        if (string.IsNullOrWhiteSpace(signalId))
            return;

        ResolveReferences();

        Vector3 worldPosition = ResolveWorldPosition(source);
        Vector3 worldDirection = ResolveWorldDirection(source);

        if (signalEmitter != null && signalEmitter.TryEmit(signalId, worldPosition, worldDirection))
            return;

        signalReceiver?.ReceiveSignal(signalId, worldPosition, worldDirection);
    }

    private void ResolveReferences()
    {
        if (door == null)
            door = GetComponent<DoorController>();

        if (signalEmitter == null)
            signalEmitter = GetComponent<ReactionSignalEmitter>();

        if (signalReceiver == null)
            signalReceiver = GetComponent<ReactionSignalReceiver>();
    }

    private Vector3 ResolveWorldPosition(DoorController source)
    {
        return ResolveWorldOrigin(source).position;
    }

    private Vector3 ResolveWorldDirection(DoorController source)
    {
        Transform origin = ResolveWorldOrigin(source);
        return origin.forward;
    }

    private Transform ResolveWorldOrigin(DoorController source)
    {
        if (source != null && source.MovingPart != null)
            return source.MovingPart;

        return transform;
    }
}
