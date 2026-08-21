using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Doors/Door Signal Source")]
public class DoorSignalSource : MonoBehaviourPunCallbacks
{
    private const string RoomPropertyKeyPrefix = "door-signal:";

    [SerializeField] private string signalName = "Signal";
    [SerializeField] private bool startsActive;
    [SerializeField] private new PhotonView photonView;
    [SerializeField] [HideInInspector] private string networkSceneId = string.Empty;
    [SerializeField] private bool prototypeLocalOnly;

    public event Action<DoorSignalSource, bool> StateChanged;

    public bool IsActive { get; private set; }
    public string SignalName => string.IsNullOrWhiteSpace(signalName) ? gameObject.name : signalName;

    private void Reset()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        EnsureNetworkSceneId();
    }

    private void Awake()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        IsActive = startsActive;
        EnsureNetworkSceneId();
    }

    private void Start()
    {
        TryApplyRoomSyncedState();
    }

    public bool Activate()
    {
        return RequestSetSignalState(true);
    }

    public bool Deactivate()
    {
        return RequestSetSignalState(false);
    }

    public bool Toggle()
    {
        return RequestSetSignalState(!IsActive);
    }

    public bool RequestSetSignalState(bool isActive)
    {
        if (IsActive == isActive)
            return false;

        if (ShouldUsePhotonViewSync())
        {
            photonView.RPC(nameof(RpcApplySignalState), RpcTarget.AllBufferedViaServer, isActive);
            return true;
        }

        if (ShouldUseRoomPropertySync())
        {
            ApplySignalState(isActive);
            PublishRoomSyncedState(isActive);
            return true;
        }

        return ApplySignalState(isActive);
    }

    private bool ApplySignalState(bool isActive)
    {
        if (IsActive == isActive)
            return false;

        IsActive = isActive;
        StateChanged?.Invoke(this, IsActive);
        return true;
    }

    private bool ShouldUsePhotonViewSync()
    {
        return !prototypeLocalOnly
            && photonView != null
            && PhotonNetwork.InRoom
            && !PhotonNetwork.OfflineMode;
    }

    private bool ShouldUseRoomPropertySync()
    {
        return !prototypeLocalOnly
            && (photonView == null || photonView.ViewID == 0)
            && PhotonNetwork.InRoom
            && !PhotonNetwork.OfflineMode
            && !string.IsNullOrWhiteSpace(NetworkSceneId);
    }

    private string NetworkSceneId
    {
        get
        {
            EnsureNetworkSceneId();
            return networkSceneId;
        }
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        TryApplyRoomSyncedState();
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);

        if (!ShouldUseRoomPropertySync() || propertiesThatChanged == null)
            return;

        if (!propertiesThatChanged.TryGetValue(BuildRoomPropertyKey(), out object propertyValue))
            return;

        if (propertyValue is not bool isActive)
            return;

        ApplySignalState(isActive);
    }

    private void PublishRoomSyncedState(bool isActive)
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        Hashtable roomState = new Hashtable
        {
            { BuildRoomPropertyKey(), isActive }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomState);
    }

    private void TryApplyRoomSyncedState()
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(BuildRoomPropertyKey(), out object propertyValue))
            return;

        if (propertyValue is not bool isActive)
            return;

        ApplySignalState(isActive);
    }

    private string BuildRoomPropertyKey()
    {
        return RoomPropertyKeyPrefix + NetworkSceneId;
    }

    private void EnsureNetworkSceneId()
    {
        if (!string.IsNullOrWhiteSpace(networkSceneId))
            return;

        networkSceneId = SceneNetworkStateIdUtility.BuildSceneObjectId(transform);
    }

    [PunRPC]
    private void RpcApplySignalState(bool isActive)
    {
        ApplySignalState(isActive);
    }

    [ContextMenu("Activate Signal")]
    private void ContextActivateSignal()
    {
        RequestSetSignalState(true);
    }

    [ContextMenu("Deactivate Signal")]
    private void ContextDeactivateSignal()
    {
        RequestSetSignalState(false);
    }

    [ContextMenu("Toggle Signal")]
    private void ContextToggleSignal()
    {
        RequestSetSignalState(!IsActive);
    }
}
