using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Puzzles/Statue Puzzle Slot")]
public sealed class StatuePuzzleSlot : MonoBehaviour, IPlayerInteractable,
    IPlayerInteractionPromptProvider, IPlayerInteractionPromptPlacementProvider, IPlayerHeldItemInteractionProvider
{
    [SerializeField] private StatueIdentity expectedIdentity;
    [SerializeField] private Transform platePivot;
    [SerializeField] private StatuePuzzleController puzzle;
    [Header("Placement Pose")]
    [Tooltip("Ajuste fino em relacao ao Plate_Pivot; nao altera o proprio pivot.")]
    [SerializeField] private Vector3 placementOffset;
    [Tooltip("Inclinacao da superficie da base, aplicada antes da orientacao do modelo da placa.")]
    [SerializeField] private Vector3 surfaceEulerAngles;

    private StatuePuzzlePlate placedPlate;
    private string networkSceneId;

    public int InteractionPriority => 210;
    public StatueIdentity ExpectedIdentity => expectedIdentity;
    public Transform PlatePivot => platePivot;
    public StatuePuzzleController Puzzle => puzzle;
    public Vector3 PlacementOffset => placementOffset;
    public bool IsLocked => puzzle != null && puzzle.IsSolved && puzzle.LockPlatesWhenSolved;
    public string NetworkSceneId => string.IsNullOrEmpty(networkSceneId)
        ? networkSceneId = SceneNetworkStateIdUtility.BuildSceneObjectId(transform) : networkSceneId;
    public StatuePuzzlePlate CurrentPlate => placedPlate != null && platePivot != null
        && placedPlate.Pickup.IsPlacedAtAnchor(platePivot) ? placedPlate : null;
    public bool IsCorrect => CurrentPlate != null && CurrentPlate.Identity == expectedIdentity;

    private void Awake()
    {
        ResolvePivot();
        _ = NetworkSceneId;
    }

    private void Reset() => ResolvePivot();

    private void ResolvePivot()
    {
        if (platePivot != null)
            return;

        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child.name == "Plate_Pivot")
            {
                platePivot = child;
                break;
            }
    }

    public bool CanPlace(StatuePuzzlePlate plate)
    {
        return isActiveAndEnabled && !IsLocked && platePivot != null && CurrentPlate == null
            && plate != null && !plate.Pickup.IsPresentationClone;
    }

    public bool TryPlacePlate(StatuePuzzlePlate plate)
    {
        if (!CanPlace(plate))
            return false;

        plate.Pickup.PlaceAtAnchor(platePivot, placementOffset, Quaternion.Euler(surfaceEulerAngles) * plate.PlacementRotation);
        placedPlate = plate;
        return true;
    }

    public bool TryInteract(PlayerPickupInteractor interactor)
    {
        HandEquipmentController equipment = interactor != null ? interactor.HandEquipmentController : null;
        if (equipment == null || IsLocked)
            return false;

        if (CurrentPlate != null)
            return equipment.TryEquipWorldItem(CurrentPlate.Pickup);

        return equipment.TryPlaceActiveStatuePlate(HandType.Right, this)
            || equipment.TryPlaceActiveStatuePlate(HandType.Left, this);
    }

    public bool TryGetInteractionPrompt(PlayerInteractionPromptContext context, out string promptTemplate)
    {
        promptTemplate = string.Empty;
        if (IsLocked || platePivot == null)
            return false;
        if (CurrentPlate != null)
            promptTemplate = "[{key}] Retirar placa";
        else
        {
            HandEquipmentController equipment = context.Interactor != null ? context.Interactor.HandEquipmentController : null;
            if (HasActivePlate(equipment, HandType.Right) || HasActivePlate(equipment, HandType.Left))
                promptTemplate = "[{key}] Posicionar placa";
        }
        return !string.IsNullOrEmpty(promptTemplate);
    }

    private bool HasActivePlate(HandEquipmentController equipment, HandType hand)
    {
        WorldPickupItem pickup = equipment != null ? equipment.GetActivePickup(hand) : null;
        return pickup != null && CanPlace(pickup.GetComponent<StatuePuzzlePlate>());
    }

    public bool TryGetInteractionPromptPlacement(PlayerInteractionPromptContext context, out PlayerInteractionPromptPlacement placement)
    {
        placement = new PlayerInteractionPromptPlacement(platePivot, new Vector3(0f, 0.15f, 0f));
        return platePivot != null;
    }

    public bool TryGetHeldItemInteractionPrompt(PlayerHeldItemInteractionContext context, out string promptTemplate)
    {
        promptTemplate = "[{key}] Posicionar placa";
        return HasActivePlate(context.EquipmentController, context.Hand);
    }

    public bool TryInteractWithHeldItem(PlayerHeldItemInteractionContext context)
    {
        return context.EquipmentController != null
            && context.EquipmentController.TryPlaceActiveStatuePlate(context.Hand, this);
    }

    public static StatuePuzzleSlot FindByNetworkSceneId(string id)
    {
        foreach (StatuePuzzleSlot slot in FindObjectsByType<StatuePuzzleSlot>(FindObjectsInactive.Include))
            if (slot.NetworkSceneId == id)
                return slot;
        return null;
    }
}
