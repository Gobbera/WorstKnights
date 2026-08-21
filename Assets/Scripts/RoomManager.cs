using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class RoomManager : MonoBehaviourPunCallbacks
{
    private const string DefaultPlayerPrefabName = "Player";
    private const string RoomSceneIndexPropertyKey = "mapSceneIndex";
    private const string RoomScenePathPropertyKey = "mapScenePath";
    private const string RoomSceneNamePropertyKey = "mapSceneName";
    private const string RoomNamePlayerPrefsKey = "RoomNameToJoin";
    private const string NicknamePlayerPrefsKey = "PlayerNickname";
    private static readonly Vector3[] FallbackSpawnOffsets =
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(3f, 0f, 0f),
        new Vector3(-3f, 0f, 0f),
        new Vector3(0f, 0f, 3f),
        new Vector3(0f, 0f, -3f),
        new Vector3(3f, 0f, 3f),
        new Vector3(-3f, 0f, -3f)
    };

    public static RoomManager instance;

    [Header("Runtime References")]
    public GameObject player;
    public Transform[] spawnPoints;

    [Header("Optional Legacy UI")]
    public GameObject roomCam;
    public GameObject nameUI;
    public GameObject connectingUI;

    [Header("Connection Defaults")]
    [SerializeField] private string nickname = string.Empty;
    [SerializeField] private string roomNameToJoin = "test";
    [SerializeField] private bool autoConnectOnStart = true;

    [HideInInspector] public int kills;
    [HideInInspector] public int deaths;

    private bool connectionFlowStarted;
    private bool localPlayerSpawned;
    private bool roomJoinRequested;
    private Transform fallbackSpawnPoint;
    private GameObject spawnedPlayerInstance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapGameplayRoomManager()
    {
        if (!ShouldBootstrapForActiveScene())
            return;

        if (FindAnyObjectByType<RoomManager>() != null)
            return;

        GameObject bootstrap = new GameObject(nameof(RoomManager));
        bootstrap.AddComponent<RoomManager>();
        Debug.Log("RoomManager: bootstrap created runtime manager for gameplay scene.");
    }

    private static bool ShouldBootstrapForActiveScene()
    {
        return GameplaySceneRoot.TryGetActiveSceneRoot(createIfMissing: true) != null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        ResolveRuntimeReferences();
        DisableLegacyAutoConnectors();
        RemoveBakedScenePlayers();
    }

    private void Start()
    {
        if (!ShouldBootstrapForActiveScene() || !autoConnectOnStart)
            return;

        ConnectAndJoinRoom();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public void ChangeNickname(string newName)
    {
        nickname = string.IsNullOrWhiteSpace(newName)
            ? GenerateFallbackNickname()
            : newName.Trim();

        PlayerPrefs.SetString(NicknamePlayerPrefsKey, nickname);
        PlayerPrefs.Save();
    }

    public void JoinRoomButtonPressed()
    {
        if (!ShouldBootstrapForActiveScene())
            return;

        ConnectAndJoinRoom();
    }

    public override void OnConnectedToMaster()
    {
        base.OnConnectedToMaster();
        if (!connectionFlowStarted)
            return;

        Debug.Log($"RoomManager: connected to Photon master as '{PhotonNetwork.NickName}'. Continuing room join for '{roomNameToJoin}'.");
        TryJoinSelectedRoom();
    }

    public override void OnJoinedLobby()
    {
        base.OnJoinedLobby();
        if (!connectionFlowStarted)
            return;

        Debug.Log($"RoomManager: joined Photon lobby while loading gameplay scene. Continuing room join for '{roomNameToJoin}'.");
        TryJoinSelectedRoom();
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        Debug.Log($"RoomManager: joined Photon room '{PhotonNetwork.CurrentRoom?.Name}' with {PhotonNetwork.CurrentRoom?.PlayerCount ?? 0} player(s).");
        localPlayerSpawned = false;
        roomJoinRequested = false;
        spawnedPlayerInstance = null;
        GameplaySceneLoadState.ClearPendingSceneLoad();
        HandleJoinedRoom();
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        connectionFlowStarted = false;
        roomJoinRequested = false;
        Debug.LogError($"RoomManager: failed to create room '{roomNameToJoin}' ({returnCode}) {message}");
        RestoreLegacyUiAfterFailure();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        connectionFlowStarted = false;
        roomJoinRequested = false;
        Debug.LogError($"RoomManager: failed to join room '{roomNameToJoin}' ({returnCode}) {message}");
        RestoreLegacyUiAfterFailure();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        base.OnDisconnected(cause);
        connectionFlowStarted = false;
        localPlayerSpawned = false;
        roomJoinRequested = false;
        spawnedPlayerInstance = null;

        if (cause == DisconnectCause.ApplicationQuit || cause == DisconnectCause.DisconnectByClientLogic)
            Debug.Log($"RoomManager: disconnected from Photon ({cause}).");
        else
            Debug.LogWarning($"RoomManager: disconnected from Photon ({cause}).");

        RestoreLegacyUiAfterFailure();
    }

    public void SpawnPlayer()
    {
        if (!PhotonNetwork.InRoom)
        {
            Debug.LogWarning("RoomManager: tried to spawn player while not inside a room.");
            return;
        }

        if (localPlayerSpawned && spawnedPlayerInstance != null)
            return;

        PlayerSetup existingLocalPlayer = FindExistingLocalRuntimePlayer();
        if (existingLocalPlayer != null)
        {
            spawnedPlayerInstance = existingLocalPlayer.gameObject;
            localPlayerSpawned = true;
            return;
        }

        ResolveRuntimeReferences();
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        GetSpawnPose(out spawnPosition, out spawnRotation);

        string activeSceneName = SceneManager.GetActiveScene().name;
        Debug.Log($"RoomManager: spawning local player in scene '{activeSceneName}' at {spawnPosition} with rotation {spawnRotation.eulerAngles}.");

        string prefabName = player != null ? player.name : DefaultPlayerPrefabName;
        spawnedPlayerInstance = PhotonNetwork.Instantiate(prefabName, spawnPosition, spawnRotation);
        localPlayerSpawned = spawnedPlayerInstance != null;

        if (!localPlayerSpawned)
            Debug.LogError($"RoomManager: Photon failed to instantiate player prefab '{prefabName}'.");
    }

    public void SetHashes()
    {
        if (!PhotonNetwork.IsConnectedAndReady || PhotonNetwork.LocalPlayer == null)
            return;

        Hashtable hash = PhotonNetwork.LocalPlayer.CustomProperties;
        hash["kills"] = kills;
        hash["deaths"] = deaths;
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
    }

    private void ConnectAndJoinRoom()
    {
        if (connectionFlowStarted)
            return;

        ResolveRuntimeReferences();
        if (player == null)
        {
            Debug.LogError("RoomManager: Player prefab was not found in Resources/Player.prefab.");
            connectionFlowStarted = false;
            RestoreLegacyUiAfterFailure();
            return;
        }

        SyncLegacyUiConnectingState();

        if (PhotonNetwork.InRoom)
        {
            HandleJoinedRoom();
            return;
        }

        connectionFlowStarted = true;
        roomJoinRequested = false;
        roomNameToJoin = ResolveRoomName();
        nickname = ResolveNickname();
        PhotonNetwork.NickName = nickname;
        Debug.Log($"RoomManager: starting Photon connection flow for room '{roomNameToJoin}' as '{nickname}'. Current state: {PhotonNetwork.NetworkClientState}.");

        if (PhotonNetwork.IsConnectedAndReady)
        {
            TryJoinSelectedRoom();
            return;
        }

        if (!PhotonNetwork.IsConnected)
        {
            PhotonNetwork.ConnectUsingSettings();
            return;
        }

        Debug.Log($"RoomManager: waiting for Photon to become ready before joining '{roomNameToJoin}'. Current state: {PhotonNetwork.NetworkClientState}.");
    }

    private void TryJoinSelectedRoom()
    {
        if (!connectionFlowStarted || PhotonNetwork.InRoom || roomJoinRequested)
            return;

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            Debug.Log($"RoomManager: Photon is not ready to join room yet. Current state: {PhotonNetwork.NetworkClientState}.");
            return;
        }

        if (string.IsNullOrWhiteSpace(roomNameToJoin))
            roomNameToJoin = ResolveRoomName();

        roomJoinRequested = true;
        RoomOptions roomOptions = new RoomOptions
        {
            CustomRoomProperties = new Hashtable
            {
                { RoomSceneIndexPropertyKey, SceneManager.GetActiveScene().buildIndex },
                { RoomScenePathPropertyKey, SceneManager.GetActiveScene().path },
                { RoomSceneNamePropertyKey, SceneManager.GetActiveScene().name }
            },
            CustomRoomPropertiesForLobby = new[] { RoomSceneIndexPropertyKey, RoomScenePathPropertyKey, RoomSceneNamePropertyKey }
        };

        Debug.Log($"RoomManager: joining or creating room '{roomNameToJoin}' for scene index {SceneManager.GetActiveScene().buildIndex}.");
        PhotonNetwork.JoinOrCreateRoom(roomNameToJoin, roomOptions, TypedLobby.Default);
    }

    private void HandleJoinedRoom()
    {
        connectionFlowStarted = false;
        roomJoinRequested = false;
        SyncLegacyUiJoinedState();
        SpawnPlayer();
    }

    private void ResolveRuntimeReferences()
    {
        GameplaySceneRoot sceneRoot = GameplaySceneRoot.TryGetActiveSceneRoot(createIfMissing: true);

        if (player == null)
            player = Resources.Load<GameObject>(DefaultPlayerPrefabName);

        if ((spawnPoints == null || spawnPoints.Length == 0) && sceneRoot != null)
            spawnPoints = sceneRoot.GetSpawnPoints();

        if (roomCam == null && sceneRoot != null)
            roomCam = sceneRoot.GetPrimarySceneCameraObject();

        if ((spawnPoints == null || spawnPoints.Length == 0) && fallbackSpawnPoint == null)
            fallbackSpawnPoint = FindNamedSpawnPoint();

        if (sceneRoot == null)
            Debug.LogWarning($"RoomManager: no GameplaySceneRoot was resolved for scene '{SceneManager.GetActiveScene().name}'.");
    }

    private void DisableLegacyAutoConnectors()
    {
        TestAutoConnector[] autoConnectors = FindObjectsByType<TestAutoConnector>(FindObjectsInactive.Include);
        for (int i = 0; i < autoConnectors.Length; i++)
        {
            if (autoConnectors[i] == null)
                continue;

            autoConnectors[i].enabled = false;
            autoConnectors[i].gameObject.SetActive(false);
        }
    }

    private void RemoveBakedScenePlayers()
    {
        PlayerSetup[] playerSetups = FindObjectsByType<PlayerSetup>(FindObjectsInactive.Include);
        for (int i = 0; i < playerSetups.Length; i++)
        {
            PlayerSetup playerSetup = playerSetups[i];
            if (playerSetup == null)
                continue;

            PhotonView playerView = playerSetup.GetComponent<PhotonView>();
            if (playerView == null || playerView.InstantiationId != 0)
                continue;

            playerSetup.gameObject.SetActive(false);
            Destroy(playerSetup.gameObject);
        }
    }

    private PlayerSetup FindExistingLocalRuntimePlayer()
    {
        PlayerSetup[] playerSetups = FindObjectsByType<PlayerSetup>(FindObjectsInactive.Include);
        for (int i = 0; i < playerSetups.Length; i++)
        {
            PlayerSetup playerSetup = playerSetups[i];
            if (playerSetup == null)
                continue;

            PhotonView playerView = playerSetup.GetComponent<PhotonView>();
            if (playerView == null)
                continue;

            if (playerView.InstantiationId != 0 && playerView.IsMine)
                return playerSetup;
        }

        return null;
    }

    private void GetSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        Transform[] resolvedSpawnPoints = GetResolvedSpawnPoints();
        int slotIndex = 0;

        if (PhotonNetwork.LocalPlayer != null)
            slotIndex = Mathf.Max(PhotonNetwork.LocalPlayer.ActorNumber - 1, 0);

        if (resolvedSpawnPoints.Length > 1)
        {
            Transform spawnPoint = resolvedSpawnPoints[slotIndex % resolvedSpawnPoints.Length];
            spawnPosition = spawnPoint.position;
            spawnRotation = spawnPoint.rotation;
            Debug.Log($"RoomManager: using indexed spawn point '{spawnPoint.name}' ({slotIndex % resolvedSpawnPoints.Length + 1}/{resolvedSpawnPoints.Length}).");
            return;
        }

        if (resolvedSpawnPoints.Length == 1)
        {
            Transform spawnPoint = resolvedSpawnPoints[0];
            Vector3 offset = FallbackSpawnOffsets[slotIndex % FallbackSpawnOffsets.Length];
            spawnPosition = spawnPoint.position + offset;
            spawnRotation = spawnPoint.rotation;
            Debug.Log($"RoomManager: using single spawn point '{spawnPoint.name}' with fallback offset {offset} for slot {slotIndex}.");
            return;
        }

        Vector3 fallbackPosition = fallbackSpawnPoint != null ? fallbackSpawnPoint.position : Vector3.zero;
        Quaternion fallbackRotation = fallbackSpawnPoint != null ? fallbackSpawnPoint.rotation : Quaternion.identity;
        Vector3 fallbackOffset = FallbackSpawnOffsets[slotIndex % FallbackSpawnOffsets.Length];
        spawnPosition = fallbackPosition + fallbackOffset;
        spawnRotation = fallbackRotation;
        Debug.LogWarning($"RoomManager: no configured spawn points were found. Falling back to position {spawnPosition}.");
    }

    private Transform[] GetResolvedSpawnPoints()
    {
        GameplaySceneRoot sceneRoot = GameplaySceneRoot.TryGetActiveSceneRoot(createIfMissing: true);
        if ((spawnPoints == null || spawnPoints.Length == 0) && sceneRoot != null)
            spawnPoints = sceneRoot.GetSpawnPoints();

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int validCount = 0;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] != null)
                    validCount++;
            }

            if (validCount == spawnPoints.Length)
                return spawnPoints;

            Transform[] compacted = new Transform[validCount];
            int compactIndex = 0;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] == null)
                    continue;

                compacted[compactIndex++] = spawnPoints[i];
            }

            spawnPoints = compacted;
            if (spawnPoints.Length > 0)
                return spawnPoints;
        }

        if (fallbackSpawnPoint == null)
            fallbackSpawnPoint = FindNamedSpawnPoint();

        if (fallbackSpawnPoint == null)
            return System.Array.Empty<Transform>();

        spawnPoints = new[] { fallbackSpawnPoint };
        Debug.LogWarning($"RoomManager: scene '{SceneManager.GetActiveScene().name}' is using named fallback spawn point '{fallbackSpawnPoint.name}'.");
        return spawnPoints;
    }

    private Transform FindNamedSpawnPoint()
    {
        Transform[] sceneTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform sceneTransform = sceneTransforms[i];
            if (sceneTransform == null)
                continue;

            if (string.Equals(sceneTransform.name, "SpawnPoint", System.StringComparison.Ordinal))
                return sceneTransform;
        }

        return null;
    }

    private string ResolveRoomName()
    {
        string savedRoomName = PlayerPrefs.GetString(RoomNamePlayerPrefsKey, roomNameToJoin);
        if (string.IsNullOrWhiteSpace(savedRoomName))
            savedRoomName = roomNameToJoin;

        savedRoomName = string.IsNullOrWhiteSpace(savedRoomName) ? "Room-1" : savedRoomName.Trim();
        roomNameToJoin = savedRoomName;
        PlayerPrefs.SetString(RoomNamePlayerPrefsKey, roomNameToJoin);
        PlayerPrefs.Save();
        return roomNameToJoin;
    }

    private string ResolveNickname()
    {
        if (!string.IsNullOrWhiteSpace(nickname))
            return nickname.Trim();

        string savedNickname = PlayerPrefs.GetString(NicknamePlayerPrefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(savedNickname))
        {
            nickname = savedNickname.Trim();
            return nickname;
        }

        nickname = GenerateFallbackNickname();
        PlayerPrefs.SetString(NicknamePlayerPrefsKey, nickname);
        PlayerPrefs.Save();
        return nickname;
    }

    private static string GenerateFallbackNickname()
    {
        return $"Tester_{Random.Range(1000, 9999)}";
    }

    private void SyncLegacyUiConnectingState()
    {
        if (nameUI != null)
            nameUI.SetActive(false);

        if (connectingUI != null)
            connectingUI.SetActive(true);
    }

    private void SyncLegacyUiJoinedState()
    {
        if (roomCam != null)
            roomCam.SetActive(false);

        if (nameUI != null)
            nameUI.SetActive(false);

        if (connectingUI != null)
            connectingUI.SetActive(false);
    }

    private void RestoreLegacyUiAfterFailure()
    {
        if (nameUI != null)
            nameUI.SetActive(true);

        if (connectingUI != null)
            connectingUI.SetActive(false);
    }
}
