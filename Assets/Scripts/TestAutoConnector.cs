using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class TestAutoConnector : MonoBehaviourPunCallbacks
{
    [Header("Ativar este conector automaticamente na cena de teste")]
    public bool autoConnect = true;

    void Start()
    {
        if (GameplaySceneRoot.IsActiveGameplayScene())
        {
            if (RoomManager.instance == null)
            {
                Debug.LogWarning("TestAutoConnector: RoomManager was missing in gameplay scene. Creating fallback runtime manager.");
                GameObject bootstrap = new GameObject(nameof(RoomManager));
                bootstrap.AddComponent<RoomManager>();
            }

            enabled = false;
            gameObject.SetActive(false);
            return;
        }

        if (RoomManager.instance != null)
        {
            enabled = false;
            return;
        }

        if (!autoConnect) return;

        if (!PhotonNetwork.IsConnected)
        {
            Debug.Log("AutoConnect: conectando ao Photon...");
            PhotonNetwork.ConnectUsingSettings();
        }
        else
        {
            JoinTestRoom();
        }
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("AutoConnect: conectado ao Master!");
        JoinTestRoom();
    }

    void JoinTestRoom()
    {
        PhotonNetwork.NickName = "Tester_" + Random.Range(1000, 9999);
        PhotonNetwork.JoinOrCreateRoom("TestRoom", new RoomOptions { MaxPlayers = 8 }, TypedLobby.Default);
        Debug.Log("AutoConnect: entrando/criando sala TestRoom...");
    }

    public override void OnJoinedRoom()
    {
        Debug.Log("AutoConnect: entrou na sala TestRoom!");
    }
}
