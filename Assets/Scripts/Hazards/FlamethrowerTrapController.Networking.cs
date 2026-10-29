using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

public sealed partial class FlamethrowerTrapController : IOnEventCallback
{
    private const byte TriggerRequestEvent = 72;
    private string networkKey;
    private int stateSequence;

    private void EnsureNetworkKey()
    {
        if (string.IsNullOrEmpty(networkKey))
        {
            string id = SceneNetworkStateIdUtility.BuildSceneObjectId(transform);
            if (!string.IsNullOrEmpty(id)) networkKey = "flamethrower:" + id;
        }
    }

    private bool RequestMasterTrigger()
    {
        EnsureNetworkKey();
        return !string.IsNullOrEmpty(networkKey) && PhotonNetwork.RaiseEvent(TriggerRequestEvent, networkKey,
            new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient }, SendOptions.SendReliable);
    }

    public void OnEvent(EventData evt)
    {
        if (Online && PhotonNetwork.IsMasterClient && evt.Code == TriggerRequestEvent
            && evt.CustomData is string key && key == networkKey)
            TryTriggerTrap();
    }

    private void PublishNetworkState()
    {
        EnsureNetworkKey();
        if (!Online || !PhotonNetwork.IsMasterClient || string.IsNullOrEmpty(networkKey)) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { networkKey, new object[] { stateSequence, activationTime, activationActive } }
        });
    }

    private void RestoreNetworkState()
    {
        EnsureNetworkKey();
        if (Online && !string.IsNullOrEmpty(networkKey)
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(networkKey, out object value))
            ApplyNetworkState(value);
    }

    private void ApplyNetworkState(object value)
    {
        if (value is not object[] state || state.Length != 3 || state[0] is not int sequence
            || state[1] is not double startedAt || state[2] is not bool active
            || sequence <= stateSequence || double.IsNaN(startedAt) || double.IsInfinity(startedAt)) return;
        ApplyActivation(sequence, startedAt, active);
    }

    public override void OnJoinedRoom() { stateSequence = 0; RestoreNetworkState(); }
    public override void OnMasterClientSwitched(Player newMasterClient) => RestoreNetworkState();
    public override void OnRoomPropertiesUpdate(Hashtable changes)
    {
        if (Online && !string.IsNullOrEmpty(networkKey) && changes.TryGetValue(networkKey, out object value))
            ApplyNetworkState(value);
    }
}
