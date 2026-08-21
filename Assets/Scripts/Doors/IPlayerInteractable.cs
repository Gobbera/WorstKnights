public interface IPlayerInteractable
{
    int InteractionPriority { get; }

    bool TryInteract(PlayerPickupInteractor interactor);
}
