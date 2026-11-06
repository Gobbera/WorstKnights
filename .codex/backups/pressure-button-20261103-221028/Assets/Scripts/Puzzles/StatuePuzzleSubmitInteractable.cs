using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[AddComponentMenu("World/Puzzles/Statue Puzzle Submit Interactable")]
public sealed class StatuePuzzleSubmitInteractable : MonoBehaviour, IPlayerInteractable, IPlayerInteractionPromptProvider
{
    [SerializeField] private StatuePuzzleController puzzle;
    [SerializeField] private string interactionPrompt = "[{key}] Conferir placas";
    [Tooltip("Feedback local do acionamento: animacao, som ou movimento da alavanca.")]
    [SerializeField] private UnityEvent onActivated = new UnityEvent();

    public int InteractionPriority => 200;

    public bool TryInteract(PlayerPickupInteractor interactor)
    {
        if (puzzle == null || puzzle.IsSolved)
            return false;

        if (interactor != null && interactor.HandEquipmentController != null)
        {
            if (!interactor.HandEquipmentController.TryRequestStatuePuzzleCheck(puzzle))
                return false;
        }
        else
            puzzle.CheckSolution();
        onActivated.Invoke();
        return true;
    }

    public bool TryGetInteractionPrompt(PlayerInteractionPromptContext context, out string promptTemplate)
    {
        promptTemplate = interactionPrompt;
        return puzzle != null && !puzzle.IsSolved;
    }
}
