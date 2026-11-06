using UnityEngine;

public enum InteractionTriggerType { Lever, PressureButton, Button }
public enum InteractionTriggerMode { Momentary, Toggle, AlternateAB, Once }
public enum PressureTriggerFilter { Players, Objects, Enemies, Anything, SpecificObject }

public readonly struct InteractionTriggerContext
{
    public InteractionTriggerContext(InteractionTriggerSystem source, GameObject activator)
    {
        Source = source;
        Activator = activator;
    }
    public InteractionTriggerSystem Source { get; }
    public GameObject Activator { get; }
    public PlayerHealth Player => Activator != null ? Activator.GetComponentInParent<PlayerHealth>() : null;
    public bool IsActive => Source.IsActive;
    public bool TriggersAction => IsActive || Source.Mode == InteractionTriggerMode.AlternateAB;
}

// Targets own their gameplay and replication. Commands and UnityEvents run once on authority.
public interface IInteractionTriggerTarget
{
    bool CanTrigger { get; }
    bool IsTriggerBusy { get; }
    void ReceiveTrigger(InteractionTriggerContext context);
}
