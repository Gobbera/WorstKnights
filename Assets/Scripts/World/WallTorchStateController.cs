using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Torches/Wall Torch State Controller")]
public sealed class WallTorchStateController : MonoBehaviourPunCallbacks
{
    private const string RoomPropertyKeyPrefix = "wall-torch:";

    [Header("Identity")]
    [SerializeField] private string torchName = "Wall Torch";
    [SerializeField] private TorchFlameController flameController;
    [SerializeField] private new PhotonView photonView;
    [SerializeField] [HideInInspector] private string networkSceneId = string.Empty;
    [SerializeField] private bool prototypeLocalOnly;

    [Header("Startup State")]
    [SerializeField] private bool startsLit;

    private bool isLit;

    public event Action<WallTorchStateController, bool> LitStateChanged;

    public bool IsLit => isLit;
    public string TorchName => string.IsNullOrWhiteSpace(torchName) ? gameObject.name : torchName;

    private string NetworkSceneId
    {
        get
        {
            EnsureNetworkSceneId();
            return networkSceneId;
        }
    }

    private void Reset()
    {
        ResolveReferences();
        EnsureNetworkSceneId();
    }

    private void OnValidate()
    {
        ResolveReferences();

        if (!Application.isPlaying && flameController != null)
            flameController.SetLit(startsLit);
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureNetworkSceneId();
        ApplyLitState(startsLit, notify: false);
    }

    private void Start()
    {
        ApplyLitState(isLit, notify: false);
        TryApplyRoomSyncedState();
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

        if (propertyValue is bool lit)
            ApplyLitState(lit);
    }

    public bool RequestLight()
    {
        return RequestSetLit(true);
    }

    public bool RequestExtinguish()
    {
        return RequestSetLit(false);
    }

    public bool RequestSetLit(bool lit)
    {
        ResolveReferences();

        if (isLit == lit)
            return false;

        if (ShouldUsePhotonViewSync())
        {
            photonView.RPC(nameof(RpcApplyLitState), RpcTarget.AllBufferedViaServer, lit);
            return true;
        }

        if (ShouldUseRoomPropertySync())
        {
            ApplyLitState(lit);
            PublishRoomSyncedState(lit);
            return true;
        }

        return ApplyLitState(lit);
    }

    private bool ApplyLitState(bool lit, bool notify = true)
    {
        bool wasLit = isLit;
        isLit = lit;

        if (flameController == null)
            ResolveReferences();

        if (flameController != null)
        {
            flameController.SetLit(isLit);
        }
        else
        {
            Debug.LogWarning($"[WallTorchStateController] '{TorchName}' esta sem TorchFlameController.", gameObject);
        }

        if (wasLit == isLit)
            return false;

        if (notify)
            LitStateChanged?.Invoke(this, isLit);

        return true;
    }

    private void ResolveReferences()
    {
        if (flameController == null)
            flameController = GetComponentInChildren<TorchFlameController>(true);

        if (photonView == null)
            photonView = GetComponent<PhotonView>();
    }

    private bool ShouldUsePhotonViewSync()
    {
        return !prototypeLocalOnly
            && photonView != null
            && photonView.ViewID != 0
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

    private void PublishRoomSyncedState(bool lit)
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        Hashtable roomState = new Hashtable
        {
            { BuildRoomPropertyKey(), lit }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomState);
    }

    private void TryApplyRoomSyncedState()
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(BuildRoomPropertyKey(), out object propertyValue))
            return;

        if (propertyValue is bool lit)
            ApplyLitState(lit);
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
    private void RpcApplyLitState(bool lit)
    {
        ApplyLitState(lit);
    }

    [ContextMenu("Light Torch")]
    private void ContextLight()
    {
        RequestLight();
    }

    [ContextMenu("Extinguish Torch")]
    private void ContextExtinguish()
    {
        RequestExtinguish();
    }

    [ContextMenu("Toggle Torch")]
    private void ContextToggle()
    {
        RequestSetLit(!isLit);
    }
}
