using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[AddComponentMenu("World/Puzzles/Statue Puzzle Controller")]
public sealed class StatuePuzzleController : MonoBehaviour
{
    [Tooltip("Exatamente quatro bases, uma para cada identidade.")]
    [SerializeField] private StatuePuzzleSlot[] slots = new StatuePuzzleSlot[4];
    [SerializeField] private bool lockPlatesWhenSolved = true;
    [Tooltip("No multiplayer, apenas o Master Client dispara os outputs de gameplay.")]
    [SerializeField] private bool eventsOnAuthorityOnly = true;
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
    public string NetworkSceneId => string.IsNullOrEmpty(networkSceneId)
        ? networkSceneId = SceneNetworkStateIdUtility.BuildSceneObjectId(transform) : networkSceneId;

    private void Awake() => _ = NetworkSceneId;

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
            return;

        Debug.Log(correct ? "[StatuePuzzle] Puzzle resolvido." : "[StatuePuzzle] Combinacao incorreta ou incompleta.", this);
        if (correct)
            onPuzzleSolved.Invoke();
        else
            onPuzzleFailed.Invoke();
    }

    public static StatuePuzzleController FindByNetworkSceneId(string id)
    {
        foreach (StatuePuzzleController puzzle in FindObjectsByType<StatuePuzzleController>(FindObjectsInactive.Include))
            if (puzzle.NetworkSceneId == id)
                return puzzle;
        return null;
    }
}
