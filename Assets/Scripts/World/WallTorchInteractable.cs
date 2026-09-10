using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Torches/Wall Torch Interactable")]
public sealed class WallTorchInteractable : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private WallTorchStateController stateController;
    [SerializeField] private bool requireLitTorchInHand = true;
    [SerializeField] private bool consumeInteractionWhenAlreadyLit;
    [SerializeField] private bool logInteractionFailures = true;
    [SerializeField] private int interactionPriority = 150;

    public int InteractionPriority => interactionPriority;

    private void Reset()
    {
        ResolveReferences();
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

        HandEquipmentController equipmentController = interactor != null ? interactor.HandEquipmentController : null;
        if (equipmentController == null)
        {
            if (logInteractionFailures)
                Debug.LogWarning($"[WallTorchInteractable] '{stateController.TorchName}' nao encontrou o equipamento do jogador.", gameObject);

            return false;
        }

        if (!equipmentController.HasActiveTorchEquipped(requireLitTorchInHand))
        {
            if (logInteractionFailures)
            {
                string requirement = requireLitTorchInHand ? "uma tocha acesa na mao" : "uma tocha na mao";
                Debug.Log($"[WallTorchInteractable] '{stateController.TorchName}' precisa de {requirement} para ser acesa.");
            }

            return false;
        }

        if (stateController.RequestLight())
        {
            Debug.Log($"[WallTorchInteractable] '{stateController.TorchName}' foi acesa.");
            return true;
        }

        return stateController.IsLit;
    }

    private void ResolveReferences()
    {
        if (stateController == null)
            stateController = GetComponent<WallTorchStateController>();

        if (stateController == null)
            stateController = GetComponentInParent<WallTorchStateController>(true);
    }
}
