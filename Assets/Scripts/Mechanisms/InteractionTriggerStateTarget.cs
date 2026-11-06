using UnityEngine;

public enum InteractionTriggerStateCommand { SetB, SetA, Toggle }

[DisallowMultipleComponent]
[AddComponentMenu("World/Mechanisms/Targets/Interaction Trigger State Target")]
public sealed class InteractionTriggerStateTarget : MonoBehaviour, IInteractionTriggerTarget
{
    [Tooltip("Persistent-state trigger to control. Its gameplay, mechanism pose and network state are updated together.")]
    [SerializeField] private InteractionTriggerSystem controlledTrigger;
    [Tooltip("SetB and SetA call a specific state; Toggle requests the opposite state.")]
    [SerializeField] private InteractionTriggerStateCommand command;

    private bool RequestedState => command == InteractionTriggerStateCommand.SetB
        || (command == InteractionTriggerStateCommand.Toggle && controlledTrigger != null && !controlledTrigger.IsActive);
    public bool CanTrigger => isActiveAndEnabled && controlledTrigger != null && controlledTrigger.LinkedSystem != this
        && controlledTrigger.CanSetState(RequestedState);
    public bool IsTriggerBusy => controlledTrigger != null && controlledTrigger.LinkedSystem != this && controlledTrigger.IsBusy;

    public void ReceiveTrigger(InteractionTriggerContext context)
    {
        if (!context.TriggersAction || context.Source == controlledTrigger || !CanTrigger) return;
        controlledTrigger.TrySetState(RequestedState, context.Activator);
    }
}
