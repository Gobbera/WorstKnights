using Photon.Pun;
using UnityEngine;

public partial class HandEquipmentController
{
    public WorldPickupItem GetActivePickup(HandType hand)
    {
        int index = GetActiveSlotIndex(hand);
        return IsValidSlotIndex(index) ? GetPickupSources(hand)[index] : null;
    }

    public bool TryPlaceActiveStatuePlate(HandType hand, StatuePuzzleSlot target)
    {
        ResolveReferences();
        if (!HasLocalAuthority() || target == null)
            return false;

        WorldPickupItem pickup = GetActivePickup(hand);
        StatuePuzzlePlate plate = pickup != null ? pickup.GetComponent<StatuePuzzlePlate>() : null;
        if (!target.CanPlace(plate))
            return false;

        int slotIndex = GetActiveSlotIndex(hand);
        if (ShouldUseNetworkSync())
        {
            if (string.IsNullOrEmpty(target.NetworkSceneId) || string.IsNullOrEmpty(pickup.NetworkSceneId))
                return false;
            photonView.RPC(nameof(RpcPlaceStatuePlate), RpcTarget.AllBufferedViaServer,
                pickup.NetworkSceneId, (int)hand, slotIndex, target.NetworkSceneId);
            return true;
        }
        return ApplyStatuePlatePlacement(pickup, hand, slotIndex, target);
    }

    private bool ApplyStatuePlatePlacement(WorldPickupItem pickup, HandType hand, int slotIndex, StatuePuzzleSlot target)
    {
        if (!IsValidSlotIndex(slotIndex) || pickup == null || target == null
            || GetPickupSources(hand)[slotIndex] != pickup
            || !target.TryPlacePlate(pickup.GetComponent<StatuePuzzlePlate>()))
            return false;

        ClearSlotReference(hand, slotIndex, "Placa posicionada na base da estatua.");
        return true;
    }

    public bool TryRequestStatuePuzzleCheck(StatuePuzzleController puzzle)
    {
        ResolveReferences();
        if (!HasLocalAuthority() || puzzle == null || puzzle.IsSolved)
            return false;

        if (ShouldUseNetworkSync())
        {
            if (string.IsNullOrEmpty(puzzle.NetworkSceneId))
                return false;
            photonView.RPC(nameof(RpcCheckStatuePuzzle), RpcTarget.AllBufferedViaServer, puzzle.NetworkSceneId);
        }
        else
            puzzle.ApplyCheckSolution(GetComponent<PlayerHealth>());
        return true;
    }

    [PunRPC]
    private void RpcCheckStatuePuzzleFromTrigger(string puzzleId, int submittingViewId, PhotonMessageInfo info)
    {
        if (!IsInventoryActionAuthorized(info)) return;
        var actor = submittingViewId != 0 ? PhotonView.Find(submittingViewId) : null;
        StatuePuzzleController.FindByNetworkSceneId(puzzleId)?.ApplyCheckSolution(actor != null ? actor.GetComponent<PlayerHealth>() : null);
    }

    public bool TryRequestTriggeredStatuePuzzleCheck(StatuePuzzleController puzzle, PlayerHealth submittingPlayer)
    {
        ResolveReferences();
        if (!HasLocalAuthority() || puzzle == null || puzzle.IsSolved) return false;
        if (!ShouldUseNetworkSync()) { puzzle.ApplyCheckSolution(submittingPlayer); return true; }
        if (!PhotonNetwork.IsMasterClient || string.IsNullOrEmpty(puzzle.NetworkSceneId)) return false;
        var actor = submittingPlayer != null ? submittingPlayer.GetComponent<PhotonView>() : null;
        photonView.RPC(nameof(RpcCheckStatuePuzzleFromTrigger), RpcTarget.AllBufferedViaServer, puzzle.NetworkSceneId, actor != null ? actor.ViewID : 0);
        return true;
    }

    [PunRPC]
    private void RpcPlaceStatuePlate(string pickupId, int handValue, int slotIndex, string targetId, PhotonMessageInfo info)
    {
        if (!IsInventoryActionAuthorized(info) || (handValue != (int)HandType.Left && handValue != (int)HandType.Right))
            return;

        if (WorldPickupItem.TryFindByNetworkSceneId(pickupId, out WorldPickupItem pickup))
            ApplyStatuePlatePlacement(pickup, (HandType)handValue, slotIndex, StatuePuzzleSlot.FindByNetworkSceneId(targetId));
    }

    [PunRPC]
    private void RpcCheckStatuePuzzle(string puzzleId, PhotonMessageInfo info)
    {
        if (IsInventoryActionAuthorized(info))
            StatuePuzzleController.FindByNetworkSceneId(puzzleId)?.ApplyCheckSolution(GetComponent<PlayerHealth>());
    }
}
