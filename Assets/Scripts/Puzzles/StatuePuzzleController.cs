using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[AddComponentMenu("World/Puzzles/Statue Puzzle Controller")]
public sealed class StatuePuzzleController : MonoBehaviour, IInteractionTriggerTarget
{
    [Tooltip("Exactly four slots, one for each statue identity.")]
    [SerializeField] private StatuePuzzleSlot[] slots = new StatuePuzzleSlot[4];
    [SerializeField] private bool lockPlatesWhenSolved = true;
    [Tooltip("In multiplayer, only the Master Client invokes gameplay outputs.")]
    [SerializeField] private bool eventsOnAuthorityOnly = true;
    [Header("Interaction Trigger")]
    [Tooltip("Traps whose active cycles and cooldowns block new attempts and mechanism release.")]
    [SerializeField] private FlamethrowerTrapController[] activationCooldownSources = new FlamethrowerTrapController[0];
    private bool triggerCheckPending;
    [Header("Outputs")]
    [SerializeField] private UnityEvent onPuzzleSolved = new UnityEvent();
    [SerializeField] private UnityEvent onPuzzleFailed = new UnityEvent();

    private string networkSceneId;
    public bool IsSolved { get; private set; }
    public PlayerHealth SubmittingPlayer { get; private set; }
    public bool LockPlatesWhenSolved => lockPlatesWhenSolved;
    public IReadOnlyList<StatuePuzzleSlot> Slots => slots;
    public UnityEvent OnPuzzleSolved => onPuzzleSolved;
    public UnityEvent OnPuzzleFailed => onPuzzleFailed;
    public event System.Action SolutionChecked;
    public bool CanTrigger => isActiveAndEnabled && !IsSolved && !IsTriggerBusy;
    public bool IsTriggerBusy
    {
        get
        {
            if (triggerCheckPending) return true;
            if (activationCooldownSources != null)
                foreach (var source in activationCooldownSources)
                    if (source != null && source.isActiveAndEnabled && source.IsCycleActive) return true;
            return false;
        }
    }
    public string NetworkSceneId => string.IsNullOrEmpty(networkSceneId)
        ? networkSceneId = SceneNetworkStateIdUtility.BuildSceneObjectId(transform) : networkSceneId;

    private void Awake() => _ = NetworkSceneId;

    public void ReceiveTrigger(InteractionTriggerContext context)
    {
        if (!context.TriggersAction || !CanTrigger) return;
        if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode)
        {
            ApplyCheckSolution(context.Player);
            return;
        }
        // Keep checks ordered with plate-placement RPCs through the server.
        if (!PhotonNetwork.IsMasterClient) return;
        triggerCheckPending = true;
        foreach (var equipment in FindObjectsByType<HandEquipmentController>())
            if (equipment.TryRequestTriggeredStatuePuzzleCheck(this, context.Player)) return;
        triggerCheckPending = false;
        Debug.LogWarning("[StatuePuzzle] No local inventory is ready to submit the puzzle check.", this);
    }

    // Signature is intentionally void so buttons, levers and UnityEvents can call it.
    [ContextMenu("Check Solution")]
    public void CheckSolution()
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            foreach (HandEquipmentController equipment in FindObjectsByType<HandEquipmentController>())
                if (equipment.TryRequestStatuePuzzleCheck(this))
                    return;
            return;
        }
        PlayerHealth localPlayer = null;
        foreach (PlayerHealth player in FindObjectsByType<PlayerHealth>())
            if (player.IsLocallyOwned)
            {
                localPlayer = player;
                break;
            }
        ApplyCheckSolution(localPlayer);
    }

    public bool HasCorrectArrangement()
    {
        if (slots == null || slots.Length != 4)
            return false;

        var identities = new HashSet<StatueIdentity>();
        foreach (StatuePuzzleSlot slot in slots)
            if (slot == null || !slot.isActiveAndEnabled || slot.Puzzle != this
                || !System.Enum.IsDefined(typeof(StatueIdentity), slot.ExpectedIdentity)
                || !identities.Add(slot.ExpectedIdentity) || !slot.IsCorrect)
                return false;
        return true;
    }

    public void ApplyCheckSolution(PlayerHealth submittingPlayer = null)
    {
        triggerCheckPending = false;
        if (IsSolved)
            return;

        SubmittingPlayer = submittingPlayer;
        bool correct = HasCorrectArrangement();
        if (correct)
        {
            IsSolved = true;
            if (lockPlatesWhenSolved)
                foreach (StatuePuzzleSlot slot in slots)
                    slot.CurrentPlate.Pickup.SetPickupAvailable(false);
        }

        if (eventsOnAuthorityOnly && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
        {
            SolutionChecked?.Invoke();
            return;
        }

        Debug.Log(correct ? "[StatuePuzzle] Puzzle solved." : "[StatuePuzzle] Incorrect or incomplete arrangement.", this);
        if (correct)
            onPuzzleSolved.Invoke();
        else
            onPuzzleFailed.Invoke();
        SolutionChecked?.Invoke();
    }

    public static StatuePuzzleController FindByNetworkSceneId(string id)
    {
        foreach (StatuePuzzleController puzzle in FindObjectsByType<StatuePuzzleController>(FindObjectsInactive.Include))
            if (puzzle.NetworkSceneId == id)
                return puzzle;
        return null;
    }
}
