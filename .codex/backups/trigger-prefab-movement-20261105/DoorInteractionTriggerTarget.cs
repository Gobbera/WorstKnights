using UnityEngine;

public enum DoorTriggerAction { FollowState, Toggle, Open, Close, Unlock, Lock }

[DisallowMultipleComponent]
[AddComponentMenu("World/Mechanisms/Targets/Door Interaction Trigger Target")]
public sealed class DoorInteractionTriggerTarget : MonoBehaviour, IInteractionTriggerTarget
{
    [SerializeField] private DoorController door;
    [SerializeField] private DoorTriggerAction action;
    [SerializeField] private bool unlockBeforeOpening;
    public bool CanTrigger => isActiveAndEnabled && door != null && door.isActiveAndEnabled;
    public bool IsTriggerBusy => door != null && door.isActiveAndEnabled && door.IsMoving;
    private void Reset() => door = GetComponent<DoorController>();
    public void ReceiveTrigger(InteractionTriggerContext context)
    {
        if (!CanTrigger) return;
        if (action == DoorTriggerAction.FollowState)
        {
            if (context.IsActive) Open(); else door.RequestClose();
            return;
        }
        if (!context.TriggersAction) return;
        switch (action)
        {
            case DoorTriggerAction.Toggle: if (door.IsOpen) door.RequestClose(); else Open(); break;
            case DoorTriggerAction.Open: Open(); break;
            case DoorTriggerAction.Close: door.RequestClose(); break;
            case DoorTriggerAction.Unlock: door.RequestUnlock(); break;
            case DoorTriggerAction.Lock: door.RequestLock(); break;
        }
    }
    private void Open()
    {
        if (unlockBeforeOpening) door.RequestUnlockAndOpen();
        else door.RequestOpen();
    }
}
