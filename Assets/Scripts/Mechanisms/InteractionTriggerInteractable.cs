using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(InteractionTriggerMechanism))]
[AddComponentMenu("World/Mechanisms/Interaction Trigger Interactable")]
public sealed class InteractionTriggerInteractable : MonoBehaviour, IPlayerInteractable, IPlayerInteractionPromptProvider
{
    public int InteractionPriority => 200;
    private InteractionTriggerSystem System => GetComponentInParent<InteractionTriggerSystem>();
    public bool TryInteract(PlayerPickupInteractor interactor)
    {
        var system = System;
        return isActiveAndEnabled && system != null && system.TryUse(interactor != null ? interactor.gameObject : null);
    }
    public bool TryGetInteractionPrompt(PlayerInteractionPromptContext context, out string promptTemplate)
    {
        var system = System;
        promptTemplate = system != null ? system.InteractionPrompt : string.Empty;
        return isActiveAndEnabled && system != null && system.CanInteract;
    }
}
