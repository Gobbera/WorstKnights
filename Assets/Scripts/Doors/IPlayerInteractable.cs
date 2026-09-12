using UnityEngine;

public interface IPlayerInteractable
{
    int InteractionPriority { get; }

    bool TryInteract(PlayerPickupInteractor interactor);
}

public readonly struct PlayerInteractionPromptContext
{
    public PlayerInteractionPromptContext(PlayerPickupInteractor interactor, KeyCode interactKey, string interactKeyText)
    {
        Interactor = interactor;
        InteractKey = interactKey;
        InteractKeyText = interactKeyText;
    }

    public PlayerPickupInteractor Interactor { get; }
    public KeyCode InteractKey { get; }
    public string InteractKeyText { get; }
}

public readonly struct PlayerInteractionPromptPlacement
{
    public PlayerInteractionPromptPlacement(Transform anchor, Vector3 localOffset)
    {
        Anchor = anchor;
        LocalOffset = localOffset;
    }

    public Transform Anchor { get; }
    public Vector3 LocalOffset { get; }
    public bool HasAnchor => Anchor != null;

    public Vector3 ResolveWorldPosition()
    {
        return Anchor != null ? Anchor.TransformPoint(LocalOffset) : Vector3.zero;
    }
}

public readonly struct PlayerHeldItemInteractionContext
{
    public PlayerHeldItemInteractionContext(
        PlayerPickupInteractor interactor,
        HandEquipmentController equipmentController,
        HandType hand,
        ItemDefinition itemDefinition,
        KeyCode actionKey,
        string actionKeyText)
    {
        Interactor = interactor;
        EquipmentController = equipmentController;
        Hand = hand;
        ItemDefinition = itemDefinition;
        ActionKey = actionKey;
        ActionKeyText = actionKeyText;
    }

    public PlayerPickupInteractor Interactor { get; }
    public HandEquipmentController EquipmentController { get; }
    public HandType Hand { get; }
    public ItemDefinition ItemDefinition { get; }
    public KeyCode ActionKey { get; }
    public string ActionKeyText { get; }
}

public interface IPlayerInteractionPromptProvider
{
    bool TryGetInteractionPrompt(PlayerInteractionPromptContext context, out string promptTemplate);
}

public interface IPlayerInteractionPromptPlacementProvider
{
    bool TryGetInteractionPromptPlacement(PlayerInteractionPromptContext context, out PlayerInteractionPromptPlacement placement);
}

public interface IPlayerHeldItemInteractionProvider
{
    bool TryGetHeldItemInteractionPrompt(PlayerHeldItemInteractionContext context, out string promptTemplate);
    bool TryInteractWithHeldItem(PlayerHeldItemInteractionContext context);
}

public static class PlayerInteractionPrompt
{
    public const string KeyPlaceholder = "{key}";
    public const string DefaultTemplate = "Press [{key}] to interact";

    public static string ResolveTemplate(string promptTemplate)
    {
        return string.IsNullOrWhiteSpace(promptTemplate)
            ? DefaultTemplate
            : promptTemplate.Trim();
    }

    public static string Format(string promptTemplate, string interactKeyText)
    {
        string resolvedTemplate = ResolveTemplate(promptTemplate);
        string resolvedKeyText = string.IsNullOrWhiteSpace(interactKeyText) ? "?" : interactKeyText.Trim();
        return resolvedTemplate
            .Replace(KeyPlaceholder, resolvedKeyText)
            .Replace("{KEY}", resolvedKeyText)
            .Replace("{Key}", resolvedKeyText);
    }
}
