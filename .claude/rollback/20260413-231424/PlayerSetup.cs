using UnityEngine;
using Photon.Pun;
using TMPro;

public class PlayerSetup : MonoBehaviourPun
{
    [SerializeField] private Camera fp_Camera;
    [SerializeField] private GameObject tpPlayerModel;
    [SerializeField] private TextMeshPro nicknameText;
    [SerializeField] private string nickname;

    private IPlayerMovement playerMovement;
    private IPlayerInput playerInput;

    private void Awake()
    {
        CacheReferences();
    }

    private void Start()
    {
        SetupLocalPlayer();
        if (photonView.IsMine)
        {
            photonView.RPC("SetNickname", RpcTarget.AllBuffered, PhotonNetwork.NickName);
        }
    }

    private void CacheReferences()
    {
        playerMovement = GetComponentInChildren<IPlayerMovement>();
        playerInput = GetComponent<IPlayerInput>();
        fp_Camera = GetComponentInChildren<Camera>(true);
        tpPlayerModel = GetComponentInChildren<ThirdPersonModel>(true)?.gameObject;
        nicknameText = GetComponentInChildren<TextMeshPro>(true);
    }

    private void SetupLocalPlayer()
    {
        bool isMine = photonView.IsMine;

        if (fp_Camera != null)
            fp_Camera.gameObject.SetActive(isMine);

        if (playerInput != null)
            ((MonoBehaviour)playerInput).enabled = isMine;

        if (tpPlayerModel != null)
            tpPlayerModel.SetActive(!isMine);
    }

    [PunRPC]
    public void SetNickname(string _name)
    {
        nickname = _name;
        if (nicknameText != null)
            nicknameText.text = nickname;
    }
}
