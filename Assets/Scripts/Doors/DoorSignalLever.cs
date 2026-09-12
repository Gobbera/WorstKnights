using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Doors/Door Signal Lever")]
public class DoorSignalLever : MonoBehaviour, IPlayerInteractable, IPlayerInteractionPromptProvider
{
    [SerializeField] private string leverName = "Lever";
    [SerializeField] private DoorSignalSource signalSource;
    [SerializeField] private bool toggleSignal = true;
    [SerializeField] private bool activateOnlyOnce;
    [Header("Interaction Prompt")]
    [SerializeField] private string interactionPrompt = "Press [{key}] to use the lever";

    public int InteractionPriority => 200;

    public string LeverName => string.IsNullOrWhiteSpace(leverName) ? gameObject.name : leverName;

    private void Reset()
    {
        if (signalSource == null)
            signalSource = GetComponent<DoorSignalSource>();
    }

    public bool TryInteract(PlayerPickupInteractor interactor)
    {
        if (signalSource == null)
            signalSource = GetComponent<DoorSignalSource>();

        if (signalSource == null)
        {
            Debug.LogWarning($"[DoorSignalLever] '{LeverName}' esta sem DoorSignalSource vinculado.", gameObject);
            return false;
        }

        if (activateOnlyOnce && signalSource.IsActive)
        {
            Debug.Log($"[DoorSignalLever] '{LeverName}' ja foi ativada.");
            return true;
        }

        if (toggleSignal)
            signalSource.Toggle();
        else
            signalSource.Activate();

        return true;
    }

    public bool TryGetInteractionPrompt(PlayerInteractionPromptContext context, out string promptTemplate)
    {
        promptTemplate = string.Empty;

        if (signalSource == null)
            signalSource = GetComponent<DoorSignalSource>();

        if (signalSource == null)
            return false;

        if (activateOnlyOnce && signalSource.IsActive)
            return false;

        promptTemplate = interactionPrompt;
        return true;
    }
}
