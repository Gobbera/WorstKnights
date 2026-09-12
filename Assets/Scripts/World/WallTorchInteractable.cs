using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Torches/Wall Torch Interactable")]
public sealed class WallTorchInteractable : MonoBehaviour, IPlayerInteractable, IPlayerInteractionPromptProvider, IPlayerInteractionPromptPlacementProvider, IPlayerHeldItemInteractionProvider
{
    [SerializeField] private WallTorchStateController stateController;
    [SerializeField] private bool requireLitTorchInHand = true;
    [SerializeField] private bool consumeInteractionWhenAlreadyLit;
    [SerializeField] private bool logInteractionFailures = true;
    [SerializeField] private int interactionPriority = 150;
    [Header("Interaction Prompt")]
    [SerializeField] private string lightInteractionPrompt = "Press [{key}] to light the torch";
    [SerializeField] private Transform interactionPromptPivot;
    [SerializeField] private Vector3 interactionPromptLocalOffset;

    public int InteractionPriority => interactionPriority;

    private void Reset()
    {
        ResolveReferences();
        if (interactionPromptPivot == null)
            interactionPromptPivot = transform;
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    public bool TryInteract(PlayerPickupInteractor interactor)
    {
        ResolveReferences();

        if (stateController == null)
        {
            Debug.LogWarning($"[WallTorchInteractable] '{gameObject.name}' esta sem WallTorchStateController.", gameObject);
            return false;
        }

        if (stateController.IsLit)
        {
            if (logInteractionFailures)
                Debug.Log($"[WallTorchInteractable] '{stateController.TorchName}' ja esta acesa.");

            return consumeInteractionWhenAlreadyLit;
        }

        if (logInteractionFailures)
        {
            string requirement = requireLitTorchInHand ? "uma tocha acesa na mao" : "uma tocha na mao";
            Debug.Log($"[WallTorchInteractable] '{stateController.TorchName}' precisa que o jogador use a acao de {requirement} para ser acesa.");
        }

        return false;
    }

    public bool TryGetInteractionPrompt(PlayerInteractionPromptContext context, out string promptTemplate)
    {
        promptTemplate = string.Empty;
        return false;
    }

    public bool TryGetInteractionPromptPlacement(PlayerInteractionPromptContext context, out PlayerInteractionPromptPlacement placement)
    {
        placement = default;
        if (interactionPromptPivot == null)
            return false;

        placement = new PlayerInteractionPromptPlacement(interactionPromptPivot, interactionPromptLocalOffset);
        return true;
    }

    public bool TryGetHeldItemInteractionPrompt(PlayerHeldItemInteractionContext context, out string promptTemplate)
    {
        promptTemplate = string.Empty;
        if (!CanUseHeldTorch(context, logFailures: false))
            return false;

        promptTemplate = lightInteractionPrompt;
        return true;
    }

    public bool TryInteractWithHeldItem(PlayerHeldItemInteractionContext context)
    {
        if (!CanUseHeldTorch(context, logFailures: true))
            return false;

        if (stateController.RequestLight())
        {
            Debug.Log($"[WallTorchInteractable] '{stateController.TorchName}' foi acesa.");
            return true;
        }

        return stateController.IsLit;
    }

    private bool CanUseHeldTorch(PlayerHeldItemInteractionContext context, bool logFailures)
    {
        ResolveReferences();

        if (stateController == null)
        {
            if (logFailures)
                Debug.LogWarning($"[WallTorchInteractable] '{gameObject.name}' esta sem WallTorchStateController.", gameObject);

            return false;
        }

        if (stateController.IsLit)
            return false;

        if (context.EquipmentController == null)
        {
            if (logFailures && logInteractionFailures)
                Debug.LogWarning($"[WallTorchInteractable] '{stateController.TorchName}' nao encontrou o equipamento do jogador.", gameObject);

            return false;
        }

        if (!context.EquipmentController.HasActiveTorchEquipped(context.Hand, requireLitTorchInHand))
            return false;

        return true;
    }

    private void ResolveReferences()
    {
        if (stateController == null)
            stateController = GetComponent<WallTorchStateController>();

        if (stateController == null)
            stateController = GetComponentInParent<WallTorchStateController>(true);
    }
}
