using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class RoomList : MonoBehaviourPunCallbacks
{
    private const string RoomSceneIndexPropertyKey = "mapSceneIndex";
    private const string RoomScenePathPropertyKey = "mapScenePath";
    private const string RoomNamePlayerPrefsKey = "RoomNameToJoin";

    public static RoomList Instance;

    [Header("UI")]
    public Transform roomListParent;
    public GameObject roomListItemPrefab;

    private readonly List<RoomInfo> cachedRoomList = new List<RoomInfo>();
    private string cachedRoomNameToCreate = string.Empty;

    private void Awake()
    {
        Instance = this;
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
        JoinRoomByName(cachedRoomNameToCreate, sceneIndex);
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
        Debug.Log("RoomList: joined Photon lobby.");
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

        Debug.LogWarning($"RoomList: disconnected from Photon ({cause}).");
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        for (int i = 0; i < roomList.Count; i++)
        {
            RoomInfo room = roomList[i];
            int existingIndex = FindRoomIndex(room.Name);

            if (room.RemovedFromList)
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
    }

    public void JoinRoomByName(string roomName, int sceneIndex)
    {
        string sanitizedRoomName = SanitizeRoomName(roomName);
        string scenePath = ResolveScenePath(sceneIndex);
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            Debug.LogError($"RoomList: scene index {sceneIndex} nao e valido nas Build Settings.");
            return;
        }

        Debug.Log($"RoomList: loading gameplay scene index {sceneIndex} ('{scenePath}') for room '{sanitizedRoomName}'.");
        PlayerPrefs.SetString(RoomNamePlayerPrefsKey, sanitizedRoomName);
        PlayerPrefs.Save();
        GameplaySceneLoadState.MarkPendingSceneLoad(scenePath);

        gameObject.SetActive(false);
        SceneManager.LoadScene(sceneIndex);
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
            playerCountLabel.text = $"{room.PlayerCount}/16";
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
        if (room?.CustomProperties == null || !room.CustomProperties.TryGetValue(RoomScenePathPropertyKey, out object scenePathObject))
            return false;

        scenePath = scenePathObject as string;
        return !string.IsNullOrWhiteSpace(scenePath);
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
