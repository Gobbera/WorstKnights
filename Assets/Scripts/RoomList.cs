using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using IEnumerator = System.Collections.IEnumerator;

[DisallowMultipleComponent]
public class RoomList : MonoBehaviourPunCallbacks
{
    private const string RoomSceneIndexPropertyKey = "mapSceneIndex";
    private const string RoomScenePathPropertyKey = "mapScenePath";
    private const string RoomSceneNamePropertyKey = "mapSceneName";
    private const string RoomNamePlayerPrefsKey = "RoomNameToJoin";

    public static RoomList Instance;

    [Header("UI")]
    public Transform roomListParent;
    public GameObject roomListItemPrefab;

    private readonly List<RoomInfo> cachedRoomList = new List<RoomInfo>();
    private string cachedRoomNameToCreate = string.Empty;
    private bool sceneLoadInProgress;
    private string pendingRoomName = string.Empty;
    private int pendingSceneIndex = -1;
    private string pendingScenePath = string.Empty;

    private void Awake()
    {
        Instance = this;
        PhotonNetwork.AutomaticallySyncScene = false;
        GameplaySceneLoadState.ClearPendingSceneLoad();
    }

    private IEnumerator Start()
    {
        if (PhotonNetwork.InRoom)
        {
            Debug.Log("RoomList: leaving active room before refreshing the lobby list.");
            PhotonNetwork.LeaveRoom();
            yield break;
        }

        if (PhotonNetwork.InLobby)
        {
            Debug.Log("RoomList: already connected and inside the lobby.");
            UpdateUI();
            yield break;
        }

        if (PhotonNetwork.IsConnectedAndReady)
        {
            Debug.Log("RoomList: connected to Photon, joining lobby.");
            PhotonNetwork.JoinLobby();
            yield break;
        }

        if (PhotonNetwork.IsConnected)
        {
            Debug.Log("RoomList: connected to Photon in a stale state, disconnecting before reconnecting to the lobby.");
            PhotonNetwork.Disconnect();
            yield return new WaitUntil(() => !PhotonNetwork.IsConnected);
        }

        Debug.Log("RoomList: connecting to Photon for lobby discovery.");
        PhotonNetwork.ConnectUsingSettings();
    }

    public void ChangeRoomToCreateName(string roomName)
    {
        cachedRoomNameToCreate = SanitizeRoomName(roomName);
    }

    public void CreateRoomByIndex(int sceneIndex)
    {
        if (!PrepareRoomTransition(cachedRoomNameToCreate, sceneIndex, out string sanitizedRoomName, out string scenePath))
            return;

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = 16,
            IsVisible = true,
            IsOpen = true,
            CustomRoomProperties = new Hashtable
            {
                { RoomSceneIndexPropertyKey, sceneIndex },
                { RoomScenePathPropertyKey, scenePath },
                { RoomSceneNamePropertyKey, System.IO.Path.GetFileNameWithoutExtension(scenePath) }
            },
            CustomRoomPropertiesForLobby = new[] { RoomSceneIndexPropertyKey, RoomScenePathPropertyKey, RoomSceneNamePropertyKey }
        };

        Debug.Log($"RoomList: creating visible Photon room '{sanitizedRoomName}' for scene index {sceneIndex} ('{scenePath}'). Region='{PhotonNetwork.CloudRegion}', AppVersion='{PhotonNetwork.AppVersion}'.");
        if (!PhotonNetwork.CreateRoom(sanitizedRoomName, roomOptions, TypedLobby.Default))
            ResetPendingRoomTransition($"RoomList: failed to send create-room request for '{sanitizedRoomName}'. Current state: {PhotonNetwork.NetworkClientState}.");
    }

    public override void OnConnectedToMaster()
    {
        base.OnConnectedToMaster();
        Debug.Log("RoomList: connected to Photon master, joining lobby.");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        base.OnJoinedLobby();
        Debug.Log($"RoomList: joined Photon lobby in region '{PhotonNetwork.CloudRegion}' with AppVersion '{PhotonNetwork.AppVersion}'.");
    }

    public override void OnCreatedRoom()
    {
        base.OnCreatedRoom();
        Debug.Log($"RoomList: created Photon room '{PhotonNetwork.CurrentRoom?.Name}'. Visible={PhotonNetwork.CurrentRoom?.IsVisible}, Open={PhotonNetwork.CurrentRoom?.IsOpen}, Lobby='{PhotonNetwork.CurrentLobby?.Name ?? "default"}'.");
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();

        if (!sceneLoadInProgress)
            return;

        LoadPendingGameplayScene();
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        base.OnCreateRoomFailed(returnCode, message);
        ResetPendingRoomTransition($"RoomList: failed to create room '{pendingRoomName}' ({returnCode}) {message}");
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        base.OnJoinRoomFailed(returnCode, message);
        ResetPendingRoomTransition($"RoomList: failed to join room '{pendingRoomName}' ({returnCode}) {message}");
    }

    public override void OnLeftRoom()
    {
        base.OnLeftRoom();
        if (!PhotonNetwork.IsConnected)
            return;

        Debug.Log("RoomList: left Photon room, joining lobby.");
        PhotonNetwork.JoinLobby();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        base.OnDisconnected(cause);

        if (cause == DisconnectCause.ApplicationQuit || cause == DisconnectCause.DisconnectByClientLogic)
        {
            Debug.Log($"RoomList: disconnected from Photon ({cause}).");
            return;
        }

        ResetPendingSceneLoadState();
        Debug.LogWarning($"RoomList: disconnected from Photon ({cause}).");
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        for (int i = 0; i < roomList.Count; i++)
        {
            RoomInfo room = roomList[i];
            int existingIndex = FindRoomIndex(room.Name);

            if (room.RemovedFromList || !room.IsVisible || !room.IsOpen)
            {
                if (existingIndex >= 0)
                    cachedRoomList.RemoveAt(existingIndex);

                continue;
            }

            if (existingIndex >= 0)
            {
                cachedRoomList[existingIndex] = room;
                continue;
            }

            cachedRoomList.Add(room);
        }

        UpdateUI();
        Debug.Log($"RoomList: received {roomList.Count} Photon room update(s); cached {cachedRoomList.Count} room(s). Region='{PhotonNetwork.CloudRegion}', AppVersion='{PhotonNetwork.AppVersion}'.");
    }

    public void JoinRoomByName(string roomName, int sceneIndex)
    {
        if (!PrepareRoomTransition(roomName, sceneIndex, out string sanitizedRoomName, out string scenePath))
            return;

        Debug.Log($"RoomList: joining Photon room '{sanitizedRoomName}' before loading scene index {sceneIndex} ('{scenePath}'). Region='{PhotonNetwork.CloudRegion}', AppVersion='{PhotonNetwork.AppVersion}'.");
        if (!PhotonNetwork.JoinRoom(sanitizedRoomName))
            ResetPendingRoomTransition($"RoomList: failed to send join-room request for '{sanitizedRoomName}'. Current state: {PhotonNetwork.NetworkClientState}.");
    }

    private bool PrepareRoomTransition(string roomName, int sceneIndex, out string sanitizedRoomName, out string scenePath)
    {
        sanitizedRoomName = string.Empty;
        scenePath = string.Empty;

        if (sceneLoadInProgress)
        {
            Debug.LogWarning("RoomList: ignored room request because a gameplay scene is already loading.");
            return false;
        }

        sanitizedRoomName = SanitizeRoomName(roomName);
        scenePath = ResolveScenePath(sceneIndex);
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            Debug.LogError($"RoomList: scene index {sceneIndex} nao e valido nas Build Settings.");
            return false;
        }

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            Debug.LogWarning($"RoomList: Photon is not ready for room operations yet. Current state: {PhotonNetwork.NetworkClientState}.");
            return false;
        }

        sceneLoadInProgress = true;
        pendingRoomName = sanitizedRoomName;
        pendingSceneIndex = sceneIndex;
        pendingScenePath = scenePath;
        PlayerPrefs.SetString(RoomNamePlayerPrefsKey, sanitizedRoomName);
        PlayerPrefs.Save();
        GameplaySceneLoadState.MarkPendingSceneLoad(scenePath);
        return true;
    }

    private void LoadPendingGameplayScene()
    {
        int sceneIndex = ResolveJoinedRoomSceneIndex();
        string scenePath = ResolveScenePath(sceneIndex);
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            ResetPendingRoomTransition($"RoomList: joined room '{PhotonNetwork.CurrentRoom?.Name}', but could not resolve its gameplay scene.");
            return;
        }

        PlayerPrefs.SetString(RoomNamePlayerPrefsKey, PhotonNetwork.CurrentRoom?.Name ?? pendingRoomName);
        PlayerPrefs.Save();
        GameplaySceneLoadState.MarkPendingSceneLoad(scenePath);

        if (PhotonNetwork.IsMasterClient)
            PhotonNetwork.AutomaticallySyncScene = true;

        Debug.Log($"RoomList: joined Photon room '{PhotonNetwork.CurrentRoom?.Name}' with {PhotonNetwork.CurrentRoom?.PlayerCount ?? 0} player(s). Loading synced scene index {sceneIndex} ('{scenePath}').");
        gameObject.SetActive(false);
        PhotonNetwork.LoadLevel(sceneIndex);
    }

    private int ResolveJoinedRoomSceneIndex()
    {
        Hashtable roomProperties = PhotonNetwork.CurrentRoom?.CustomProperties;
        if (roomProperties != null)
        {
            if (TryResolveScenePath(roomProperties, out string roomScenePath))
            {
                int sceneIndexFromPath = SceneUtility.GetBuildIndexByScenePath(roomScenePath);
                if (sceneIndexFromPath >= 0)
                    return sceneIndexFromPath;

                Debug.LogWarning($"RoomList: joined room '{PhotonNetwork.CurrentRoom?.Name}' references scene path '{roomScenePath}', but it is not in local Build Settings.");
            }

            if (TryResolveSceneIndex(roomProperties, out int roomSceneIndex) && IsValidBuildSceneIndex(roomSceneIndex))
                return roomSceneIndex;
        }

        if (!string.IsNullOrWhiteSpace(pendingScenePath))
        {
            int pendingIndexFromPath = SceneUtility.GetBuildIndexByScenePath(pendingScenePath);
            if (pendingIndexFromPath >= 0)
                return pendingIndexFromPath;
        }

        if (IsValidBuildSceneIndex(pendingSceneIndex))
            return pendingSceneIndex;

        return ResolveDefaultGameplaySceneIndex();
    }

    private void ResetPendingRoomTransition(string errorMessage)
    {
        Debug.LogError(errorMessage, this);
        ResetPendingSceneLoadState();

        if (gameObject != null)
            gameObject.SetActive(true);
    }

    private void ResetPendingSceneLoadState()
    {
        sceneLoadInProgress = false;
        pendingRoomName = string.Empty;
        pendingSceneIndex = -1;
        pendingScenePath = string.Empty;
        GameplaySceneLoadState.ClearPendingSceneLoad();
    }

    private void UpdateUI()
    {
        if (roomListParent == null || roomListItemPrefab == null)
        {
            Debug.LogError("RoomList: room list UI is missing its parent Transform or room item prefab reference.", this);
            return;
        }

        foreach (Transform roomItem in roomListParent)
            Destroy(roomItem.gameObject);

        for (int i = 0; i < cachedRoomList.Count; i++)
        {
            RoomInfo room = cachedRoomList[i];
            GameObject roomItem = Instantiate(roomListItemPrefab, roomListParent);

            TextMeshProUGUI nameLabel = roomItem.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI playerCountLabel = roomItem.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
            RoomItemButton roomButton = roomItem.GetComponent<RoomItemButton>();

            nameLabel.text = room.Name;
            int maxPlayers = room.MaxPlayers > 0 ? room.MaxPlayers : 16;
            playerCountLabel.text = $"{room.PlayerCount}/{maxPlayers}";
            roomButton.RoomName = room.Name;
            roomButton.SceneIndex = ResolveSceneIndex(room);
        }
    }

    private int FindRoomIndex(string roomName)
    {
        for (int i = 0; i < cachedRoomList.Count; i++)
        {
            if (cachedRoomList[i].Name == roomName)
                return i;
        }

        return -1;
    }

    private static int ResolveSceneIndex(RoomInfo room)
    {
        if (TryResolveRoomScenePath(room, out string scenePath))
        {
            int sceneIndexFromPath = SceneUtility.GetBuildIndexByScenePath(scenePath);
            if (sceneIndexFromPath >= 0)
                return sceneIndexFromPath;

            Debug.LogWarning($"RoomList: room '{room.Name}' references gameplay scene path '{scenePath}', but this path is not present in the local Build Settings.");
        }

        object sceneIndexObject;
        if (room.CustomProperties != null && room.CustomProperties.TryGetValue(RoomSceneIndexPropertyKey, out sceneIndexObject))
        {
            if (sceneIndexObject is int sceneIndex && IsValidBuildSceneIndex(sceneIndex))
                return sceneIndex;

            Debug.LogWarning($"RoomList: room '{room.Name}' exposed an invalid scene index '{sceneIndexObject}'. Falling back to the default gameplay scene.");
        }

        return ResolveDefaultGameplaySceneIndex();
    }

    private static bool TryResolveRoomScenePath(RoomInfo room, out string scenePath)
    {
        scenePath = string.Empty;
        if (room?.CustomProperties == null || !TryResolveScenePath(room.CustomProperties, out scenePath))
            return false;

        return true;
    }

    private static bool TryResolveScenePath(Hashtable customProperties, out string scenePath)
    {
        scenePath = string.Empty;
        if (customProperties == null || !customProperties.TryGetValue(RoomScenePathPropertyKey, out object scenePathObject))
            return false;

        scenePath = scenePathObject as string;
        return !string.IsNullOrWhiteSpace(scenePath);
    }

    private static bool TryResolveSceneIndex(Hashtable customProperties, out int sceneIndex)
    {
        sceneIndex = -1;
        if (customProperties == null || !customProperties.TryGetValue(RoomSceneIndexPropertyKey, out object sceneIndexObject))
            return false;

        if (sceneIndexObject is int intSceneIndex)
        {
            sceneIndex = intSceneIndex;
            return true;
        }

        if (sceneIndexObject is byte byteSceneIndex)
        {
            sceneIndex = byteSceneIndex;
            return true;
        }

        return false;
    }

    private static string ResolveScenePath(int sceneIndex)
    {
        return IsValidBuildSceneIndex(sceneIndex)
            ? SceneUtility.GetScenePathByBuildIndex(sceneIndex)
            : string.Empty;
    }

    private static bool IsValidBuildSceneIndex(int sceneIndex)
    {
        return sceneIndex >= 0 && sceneIndex < SceneManager.sceneCountInBuildSettings;
    }

    private static int ResolveDefaultGameplaySceneIndex()
    {
        for (int i = 1; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            if (!string.IsNullOrWhiteSpace(SceneUtility.GetScenePathByBuildIndex(i)))
                return i;
        }

        return 0;
    }

    private static string SanitizeRoomName(string roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName))
            return "Room-1";

        return roomName.Trim();
    }
}
