using UnityEngine;

public enum SignalTriggerAction { FollowState, Toggle, Activate, Deactivate }

[DisallowMultipleComponent]
[AddComponentMenu("World/Mechanisms/Targets/Signal Interaction Trigger Target")]
public sealed class SignalInteractionTriggerTarget : MonoBehaviour, IInteractionTriggerTarget
{
    [Tooltip("Shared signal consumed by platforms, doors or other signal-driven mechanisms.")]
    [SerializeField] private DoorSignalSource signalSource;
    [Tooltip("Follow State follows press/release. Other actions run only on action transitions.")]
    [SerializeField] private SignalTriggerAction action;

    public bool CanTrigger => isActiveAndEnabled && signalSource != null && signalSource.isActiveAndEnabled;
    public bool IsTriggerBusy => false;
    private void Reset() => signalSource = GetComponent<DoorSignalSource>();

    public void ReceiveTrigger(InteractionTriggerContext context)
    {
        if (!CanTrigger) return;
        if (action == SignalTriggerAction.FollowState)
        {
            signalSource.RequestSetSignalState(context.IsActive);
            return;
        }
        if (!context.TriggersAction) return;
        switch (action)
        {
            case SignalTriggerAction.Toggle: signalSource.Toggle(); break;
            case SignalTriggerAction.Activate: signalSource.Activate(); break;
            case SignalTriggerAction.Deactivate: signalSource.Deactivate(); break;
        }
    }
}
