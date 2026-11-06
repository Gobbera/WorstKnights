using UnityEngine;

public enum InteractionTriggerType { Lever, PressureButton, Button }
public enum InteractionTriggerMode { Momentary, Toggle, AlternateAB, Once }
public enum PressureTriggerFilter { Players, Objects, Enemies, Anything, SpecificObject }

public readonly struct InteractionTriggerContext
{
    public InteractionTriggerContext(InteractionTriggerSystem source, GameObject activator)
        : this(source, activator, source.IsActive, source.Mode) { }
    internal InteractionTriggerContext(InteractionTriggerSystem source, GameObject activator, bool active, InteractionTriggerMode mode)
    {
        Source = source;
        Activator = activator;
        IsActive = active;
        Mode = mode;
    }
    public InteractionTriggerSystem Source { get; }
    public GameObject Activator { get; }
    public PlayerHealth Player => Activator != null ? Activator.GetComponentInParent<PlayerHealth>() : null;
    public bool IsActive { get; }
    public InteractionTriggerMode Mode { get; }
    public bool TriggersAction => IsActive || Mode == InteractionTriggerMode.AlternateAB;
}

// Targets own their gameplay and replication. Commands and UnityEvents run once on authority.
public interface IInteractionTriggerTarget
{
    bool CanTrigger { get; }
    bool IsTriggerBusy { get; }
    void ReceiveTrigger(InteractionTriggerContext context);
}
