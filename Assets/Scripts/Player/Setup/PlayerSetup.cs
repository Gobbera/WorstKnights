using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Photon.Pun;
using TMPro;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView))]
public class PlayerSetup : MonoBehaviourPun
{
    private const string FirstPersonCameraName = "FP_Camera";
    private const string FirstPersonHandsCameraName = "Hands Camera";
    private const string FirstPersonModelRootName = "FPS_Model";
    private const string FirstPersonUpperBodyRootName = "Separated_UpperBody";
    private const string LegacyFirstPersonUpperBodyRootName = "Separeted_UpperBody";
    private const string FirstPersonViewLayerName = "FirstPersonView";
    private const string WallLayerName = "Wall";
    private const float FirstPersonNearClipPlane = 0.01f;

    private static readonly string[] FirstPersonVisualRootNames =
    {
        FirstPersonModelRootName,
        FirstPersonUpperBodyRootName,
        LegacyFirstPersonUpperBodyRootName
    };

    private static readonly FieldInfo UniversalCameraClearDepthField =
        typeof(UniversalAdditionalCameraData).GetField("m_ClearDepth", BindingFlags.Instance | BindingFlags.NonPublic);

    private enum FirstPersonOverlayDepthMode
    {
        AlwaysOnTop = 0,
        WallLayerStencilBypass = 1
    }

    [SerializeField] private Camera fp_Camera;
    [SerializeField] private GameObject tpPlayerModel;
    [SerializeField] private TextMeshPro nicknameText;
    [SerializeField] private string nickname;
    [SerializeField] private FirstPersonOverlayDepthMode firstPersonOverlayDepthMode = FirstPersonOverlayDepthMode.AlwaysOnTop;
    [SerializeField] private float firstPersonOverlayFarClipPlane = 25f;
    [SerializeField] private bool disableFirstPersonCameraOcclusionCulling = true;
    [SerializeField] private bool stabilizeFirstPersonSkinnedRenderers = true;
    [SerializeField] private Vector3 firstPersonSkinnedMeshMinLocalBoundsSize = new Vector3(1.5f, 1.5f, 1.5f);

    private IPlayerInput playerInput;
    private PlayerHealth playerHealth;
    private PlayerMovement playerMovement;
    private HandEquipmentController handEquipmentController;
    private PlayerEmoteWheelController emoteWheelController;
    private PlayerPerspectiveVisibility perspectiveVisibility;
    private Camera[] playerCameras;
    private Camera firstPersonHandsCamera;
    private static bool warnedMissingFirstPersonViewLayer;
    private static bool warnedMissingUniversalClearDepthField;

    private void Awake()
    {
        EnsureRuntimeCombatComponents();
        CacheReferences();
    }

    private void Start()
    {
        SetupLocalPlayer();
        if (!photonView.IsMine)
            return;

        if (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode)
            photonView.RPC("SetNickname", RpcTarget.AllBuffered, PhotonNetwork.NickName);
        else
            SetNickname(PhotonNetwork.NickName);
    }

    private void CacheReferences()
    {
        playerInput = GetComponent<IPlayerInput>();
        playerHealth = GetComponent<PlayerHealth>();
        playerMovement = GetComponent<PlayerMovement>();
        handEquipmentController = GetComponent<HandEquipmentController>();
        emoteWheelController = GetComponent<PlayerEmoteWheelController>();
        perspectiveVisibility = GetComponent<PlayerPerspectiveVisibility>();
        playerCameras = GetComponentsInChildren<Camera>(true);
        fp_Camera = FindCameraByName(FirstPersonCameraName);
        if (fp_Camera == null && playerCameras.Length > 0)
            fp_Camera = playerCameras[0];
        firstPersonHandsCamera = ResolveFirstPersonHandsCamera();
        tpPlayerModel = GetComponentInChildren<ThirdPersonModel>(true)?.gameObject;
        nicknameText = GetComponentInChildren<TextMeshPro>(true);
    }

    private void EnsureRuntimeCombatComponents()
    {
        bool addedRuntimeComponent = false;

        if (GetComponent<PlayerHealth>() == null)
        {
            gameObject.AddComponent<PlayerHealth>();
            addedRuntimeComponent = true;
        }

        if (GetComponent<PlayerMeleeAttack>() == null)
        {
            gameObject.AddComponent<PlayerMeleeAttack>();
            addedRuntimeComponent = true;
        }

        if (GetComponent<PlayerKickAttack>() == null)
        {
            gameObject.AddComponent<PlayerKickAttack>();
            addedRuntimeComponent = true;
        }

        if (GetComponent<PlayerRagdollController>() == null)
        {
            gameObject.AddComponent<PlayerRagdollController>();
            addedRuntimeComponent = true;
        }

        if (GetComponent<HandEquipmentController>() == null)
        {
            gameObject.AddComponent<HandEquipmentController>();
            addedRuntimeComponent = true;
        }

        if (GetComponent<PlayerEmoteWheelController>() == null)
        {
            gameObject.AddComponent<PlayerEmoteWheelController>();
            addedRuntimeComponent = true;
        }

        if (GetComponent<PlayerPerspectiveVisibility>() == null)
        {
            gameObject.AddComponent<PlayerPerspectiveVisibility>();
            addedRuntimeComponent = true;
        }

        if (GetComponent<PlayerPickupInteractor>() == null)
        {
            gameObject.AddComponent<PlayerPickupInteractor>();
            addedRuntimeComponent = true;
        }

        if (GetComponent<PlayerAudioController>() == null)
        {
            gameObject.AddComponent<PlayerAudioController>();
            addedRuntimeComponent = true;
        }

        if (!addedRuntimeComponent)
            return;

        PhotonView runtimePhotonView = photonView != null ? photonView : GetComponent<PhotonView>();
        if (runtimePhotonView == null)
            return;

        runtimePhotonView.RefreshRpcMonoBehaviourCache();
        runtimePhotonView.FindObservables(true);
    }

    private void SetupLocalPlayer()
    {
        bool isMine = photonView.IsMine;
        ConfigureFirstPersonRendering(isMine);

        if (playerCameras != null)
        {
            for (int i = 0; i < playerCameras.Length; i++)
            {
                Camera playerCamera = playerCameras[i];
                if (playerCamera == null)
                    continue;

                bool shouldEnableCamera = ShouldEnablePlayerCamera(playerCamera, isMine);
                playerCamera.gameObject.SetActive(shouldEnableCamera);
                ApplyCameraAudioListenerState(playerCamera, shouldEnableCamera);
            }
        }

        if (playerInput != null)
            ((MonoBehaviour)playerInput).enabled = isMine;

        ApplyPerspectiveVisibility(isMine);

        if (!isMine)
            return;

        GameplaySceneRoot.NotifyLocalPlayerReady(this);

        if (playerMovement != null)
            StaminaController.BindLocalPlayer(playerMovement);

        if (playerHealth != null)
            HealthController.BindLocalPlayer(playerHealth);

        if (handEquipmentController != null)
            HandEquipmentUI.BindLocalPlayer(handEquipmentController);

        if (emoteWheelController != null)
            EmoteWheelUI.BindLocalPlayer(emoteWheelController);
    }

    [PunRPC]
    public void SetNickname(string _name)
    {
        nickname = _name;
        if (nicknameText != null)
            nicknameText.text = nickname;
    }

    private Camera FindCameraByName(string cameraName)
    {
        if (playerCameras == null)
            return null;

        for (int i = 0; i < playerCameras.Length; i++)
        {
            Camera playerCamera = playerCameras[i];
            if (playerCamera != null && string.Equals(playerCamera.gameObject.name, cameraName, StringComparison.Ordinal))
                return playerCamera;
        }

        return null;
    }

    private Camera ResolveFirstPersonHandsCamera()
    {
        Camera namedHandsCamera = FindCameraByName(FirstPersonHandsCameraName);
        if (namedHandsCamera != null)
            return namedHandsCamera;

        if (fp_Camera == null || playerCameras == null)
            return null;

        for (int i = 0; i < playerCameras.Length; i++)
        {
            Camera candidateCamera = playerCameras[i];
            if (candidateCamera == null || candidateCamera == fp_Camera)
                continue;

            if (candidateCamera.transform.IsChildOf(fp_Camera.transform))
                return candidateCamera;
        }

        return null;
    }

    private bool ShouldEnablePlayerCamera(Camera playerCamera, bool isMine)
    {
        if (!isMine || playerCamera == null)
            return false;

        if (playerCamera == fp_Camera)
            return true;

        if (firstPersonHandsCamera != null && playerCamera == firstPersonHandsCamera)
            return true;

        return fp_Camera != null
            && playerCamera != fp_Camera
            && playerCamera.transform.IsChildOf(fp_Camera.transform);
    }

    private void ApplyCameraAudioListenerState(Camera playerCamera, bool shouldEnableCamera)
    {
        if (playerCamera == null)
            return;

        AudioListener listener = playerCamera.GetComponent<AudioListener>();
        if (listener == null)
            return;

        listener.enabled = shouldEnableCamera && playerCamera == fp_Camera;
    }

    private void ConfigureFirstPersonRendering(bool isMine)
    {
        if (!isMine)
            return;

        if (fp_Camera == null)
            fp_Camera = FindCameraByName(FirstPersonCameraName);

        if (firstPersonHandsCamera == null)
            firstPersonHandsCamera = ResolveFirstPersonHandsCamera();

        int firstPersonLayer = LayerMask.NameToLayer(FirstPersonViewLayerName);
        if (firstPersonLayer < 0)
        {
            if (!warnedMissingFirstPersonViewLayer)
            {
                Debug.LogWarning($"PlayerSetup: layer '{FirstPersonViewLayerName}' nao encontrada. Crie essa layer para separar maos/armas da camera principal.", gameObject);
                warnedMissingFirstPersonViewLayer = true;
            }

            return;
        }

        int firstPersonMask = 1 << firstPersonLayer;
        bool useWallOnlyBypass = ShouldUseWallLayerStencilBypass();
        bool clearOverlayDepth = !useWallOnlyBypass;
        ConfigureFirstPersonVisualRoots(firstPersonLayer);

        if (fp_Camera != null)
        {
            fp_Camera.cullingMask &= ~firstPersonMask;
            fp_Camera.nearClipPlane = Mathf.Min(fp_Camera.nearClipPlane, FirstPersonNearClipPlane);
            if (disableFirstPersonCameraOcclusionCulling)
                fp_Camera.useOcclusionCulling = false;
        }

        if (firstPersonHandsCamera != null)
        {
            firstPersonHandsCamera.cullingMask = firstPersonMask;
            firstPersonHandsCamera.clearFlags = clearOverlayDepth ? CameraClearFlags.Depth : CameraClearFlags.Nothing;
            firstPersonHandsCamera.nearClipPlane = FirstPersonNearClipPlane;
            firstPersonHandsCamera.farClipPlane = Mathf.Max(firstPersonOverlayFarClipPlane, firstPersonHandsCamera.nearClipPlane + 0.1f);
            firstPersonHandsCamera.useOcclusionCulling = false;

            if (fp_Camera != null)
            {
                firstPersonHandsCamera.fieldOfView = fp_Camera.fieldOfView;
                firstPersonHandsCamera.depth = fp_Camera.depth + 1f;
            }

            ApplyCameraAudioListenerState(firstPersonHandsCamera, shouldEnableCamera: true);
        }

        ConfigureUniversalCameraStack(fp_Camera, firstPersonHandsCamera, clearOverlayDepth);
    }

    private bool ShouldUseWallLayerStencilBypass()
    {
        return firstPersonOverlayDepthMode == FirstPersonOverlayDepthMode.WallLayerStencilBypass
            && LayerMask.NameToLayer(WallLayerName) >= 0;
    }

    private void ConfigureFirstPersonVisualRoots(int firstPersonLayer)
    {
        HashSet<Transform> configuredRoots = new HashSet<Transform>();
        for (int i = 0; i < FirstPersonVisualRootNames.Length; i++)
        {
            Transform visualRoot = FindTransformByName(FirstPersonVisualRootNames[i]);
            if (visualRoot == null || !configuredRoots.Add(visualRoot))
                continue;

            SetLayerRecursively(visualRoot, firstPersonLayer);
            StabilizeFirstPersonSkinnedRenderers(visualRoot);
        }
    }

    private void StabilizeFirstPersonSkinnedRenderers(Transform visualRoot)
    {
        if (!stabilizeFirstPersonSkinnedRenderers || visualRoot == null)
            return;

        SkinnedMeshRenderer[] skinnedRenderers = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedRenderers.Length; i++)
        {
            SkinnedMeshRenderer skinnedRenderer = skinnedRenderers[i];
            if (skinnedRenderer == null)
                continue;

            skinnedRenderer.updateWhenOffscreen = true;
            EnsureFirstPersonSkinnedRendererBounds(skinnedRenderer);
        }
    }

    private void EnsureFirstPersonSkinnedRendererBounds(SkinnedMeshRenderer skinnedRenderer)
    {
        Bounds bounds = skinnedRenderer.localBounds;
        Vector3 minimumSize = new Vector3(
            Mathf.Max(0f, firstPersonSkinnedMeshMinLocalBoundsSize.x),
            Mathf.Max(0f, firstPersonSkinnedMeshMinLocalBoundsSize.y),
            Mathf.Max(0f, firstPersonSkinnedMeshMinLocalBoundsSize.z));

        if (!IsFiniteBounds(bounds))
        {
            skinnedRenderer.localBounds = new Bounds(Vector3.zero, minimumSize);
            return;
        }

        Vector3 currentSize = bounds.size;
        Vector3 targetSize = Vector3.Max(currentSize, minimumSize);
        if ((targetSize - currentSize).sqrMagnitude <= 0.000001f)
            return;

        bounds.size = targetSize;
        skinnedRenderer.localBounds = bounds;
    }

    private void ConfigureUniversalCameraStack(Camera baseCamera, Camera overlayCamera, bool clearOverlayDepth)
    {
        if (baseCamera == null || overlayCamera == null || baseCamera == overlayCamera)
            return;

        UniversalAdditionalCameraData baseCameraData = baseCamera.GetComponent<UniversalAdditionalCameraData>();
        UniversalAdditionalCameraData overlayCameraData = overlayCamera.GetComponent<UniversalAdditionalCameraData>();
        if (baseCameraData == null || overlayCameraData == null)
            return;

        baseCameraData.renderType = CameraRenderType.Base;
        overlayCameraData.renderType = CameraRenderType.Overlay;
        SetUniversalOverlayClearDepth(overlayCameraData, clearOverlayDepth);

        List<Camera> cameraStack = baseCameraData.cameraStack;
        if (cameraStack == null)
            return;

        cameraStack.RemoveAll(camera => camera == null || camera == overlayCamera);
        cameraStack.Add(overlayCamera);
    }

    private void SetUniversalOverlayClearDepth(UniversalAdditionalCameraData overlayCameraData, bool clearDepth)
    {
        if (overlayCameraData == null)
            return;

        if (UniversalCameraClearDepthField != null)
        {
            UniversalCameraClearDepthField.SetValue(overlayCameraData, clearDepth);
            return;
        }

        if (!warnedMissingUniversalClearDepthField)
        {
            Debug.LogWarning("PlayerSetup: nao foi possivel ajustar o clearDepth da Hands Camera. Verifique o valor manualmente no Universal Additional Camera Data.", gameObject);
            warnedMissingUniversalClearDepthField = true;
        }
    }

    private Transform FindTransformByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform childTransform = childTransforms[i];
            if (childTransform != null && string.Equals(childTransform.name, objectName, StringComparison.Ordinal))
                return childTransform;
        }

        return null;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null || layer < 0)
            return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private static bool IsFiniteBounds(Bounds bounds)
    {
        return IsFiniteVector(bounds.center) && IsFiniteVector(bounds.size);
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFiniteFloat(value.x) && IsFiniteFloat(value.y) && IsFiniteFloat(value.z);
    }

    private static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void ApplyPerspectiveVisibility(bool isMine)
    {
        if (perspectiveVisibility == null)
            perspectiveVisibility = GetComponent<PlayerPerspectiveVisibility>();

        if (perspectiveVisibility != null)
        {
            perspectiveVisibility.Apply(isMine);
            return;
        }

        if (tpPlayerModel != null)
            tpPlayerModel.SetActive(!isMine);
    }
}
