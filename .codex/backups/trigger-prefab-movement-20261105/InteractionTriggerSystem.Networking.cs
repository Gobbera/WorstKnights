using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public sealed partial class InteractionTriggerSystem : IOnEventCallback
{
    private const byte UseRequestEvent = 73;
    private string networkKey;
    private int stateSequence;
    private void EnsureNetworkKey()
    {
        if (!string.IsNullOrEmpty(networkKey)) return;
        string id = SceneNetworkStateIdUtility.BuildSceneObjectId(transform);
        if (!string.IsNullOrEmpty(id)) networkKey = "interaction-trigger:" + id;
    }
    private bool RequestMasterUse(GameObject activator)
    {
        var view = activator != null ? activator.GetComponentInParent<PhotonView>() : null;
        return view != null && view.IsMine && !string.IsNullOrEmpty(networkKey)
            && PhotonNetwork.RaiseEvent(UseRequestEvent, new object[] { networkKey, view.ViewID, stateSequence },
                new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient }, SendOptions.SendReliable);
    }
    public void OnEvent(EventData evt)
    {
        if (!Online || !PhotonNetwork.IsMasterClient || evt.Code != UseRequestEvent || evt.CustomData is not object[] request || request.Length != 3
            || request[0] is not string key || key != networkKey || request[1] is not int viewId || request[2] is not int expected || expected != stateSequence) return;
        var view = PhotonView.Find(viewId);
        if (view == null || mechanism == null || view.OwnerActorNr != evt.Sender || Vector3.Distance(view.transform.position, mechanism.transform.position) > 6f) return;
        TryUse(view.gameObject);
    }
    private void PublishNetworkState(bool used)
    {
        EnsureNetworkKey();
        if (!Online || !PhotonNetwork.IsMasterClient || string.IsNullOrEmpty(networkKey)) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { networkKey, new object[] { stateSequence, IsActive, HasBeenUsed, changedAt, readyAt, used } }
        });
    }
    private void RestoreNetworkState()
    {
        EnsureNetworkKey();
        if (Online && !string.IsNullOrEmpty(networkKey) && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(networkKey, out object value)) ApplyNetworkState(value, true);
    }
    private void ApplyNetworkState(object value, bool initial)
    {
        if (value is not object[] state || state.Length != 6 || state[0] is not int sequence || sequence <= stateSequence
            || state[1] is not bool active || state[2] is not bool used || state[3] is not double changed || state[4] is not double ready || state[5] is not bool
            || double.IsNaN(changed) || double.IsInfinity(changed) || double.IsNaN(ready) || double.IsInfinity(ready)) return;
        stateSequence = sequence;
        IsActive = active;
        HasBeenUsed = used;
        changedAt = changed;
        readyAt = ready;
        mechanism?.SetState(active, !initial, initial, pressDistance, leverAngle);
    }
    public override void OnJoinedRoom() { stateSequence = 0; RestoreNetworkState(); }
    public override void OnMasterClientSwitched(Player newMasterClient) => RestoreNetworkState();
    public override void OnRoomPropertiesUpdate(Hashtable changes)
    {
        if (Online && !string.IsNullOrEmpty(networkKey) && changes.TryGetValue(networkKey, out object value)) ApplyNetworkState(value, false);
    }
}
