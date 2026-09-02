using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Sistema de look do player.
///
/// Responsabilidades:
/// - ler input de mouse
/// - acumular yaw/pitch do owner local
/// - aplicar a rotacao da camera em modo direto ou dirigido
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Camera))]
public class MouseLook : MonoBehaviour
{
    public static MouseLook instance;
    private const float BaseSensitivity = 0.17f;

    [Header("Input Settings")]
    [SerializeField] private Vector2 sensitivity = new Vector2(0.17f, 0.17f);
    [SerializeField] private Vector2 smoothing = new Vector2(3f, 3f);
    [SerializeField] private bool lockCursor = true;

    [Header("Pitch Limits")]
    [SerializeField] [Range(0f, 89f)] private float pitchUpLimit = 89f;
    [SerializeField] [Range(0f, 89f)] private float pitchDownLimit = 89f;
    [SerializeField] private Transform characterBody;

    private Vector2 smoothMouse;
    private Vector2 mouseDelta;
    private Vector3 baseCameraLocalEulerAngles;
    private PhotonView photonView;
    private PlayerMovement playerMovement;
    private float viewPitch;
    private float viewYaw;
    private bool lookInputBlocked;

    [HideInInspector]
    public bool scoped;

    public float ViewPitch => viewPitch;
    public float ViewYaw => viewYaw;

    private void Awake()
    {
        CacheReferences();
        if (HasLocalAuthority())
            instance = this;
    }

    private void OnEnable()
    {
        CacheReferences();
        if (HasLocalAuthority())
            instance = this;
    }

    private void OnDisable()
    {
        if (instance == this)
            instance = null;
    }

    private void Start()
    {
        baseCameraLocalEulerAngles = transform.localEulerAngles;
        viewPitch = 0f;
        viewYaw = EnsureCharacterBody() ? characterBody.eulerAngles.y : 0f;

        if (lockCursor && HasLocalAuthority())
            LockCursor();

        if (UsesDrivenLookController())
            ApplyDrivenLookRotation(0f);
        else
            ApplyLookRotation();
    }

    private void Update()
    {
        if (!HasLocalAuthority() || Mouse.current == null)
            return;

        if (lookInputBlocked)
        {
            mouseDelta = Vector2.zero;
            smoothMouse = Vector2.zero;
            return;
        }

        mouseDelta = Mouse.current.delta.ReadValue();
        mouseDelta = Vector2.Scale(mouseDelta, new Vector2(sensitivity.x * smoothing.x, sensitivity.y * smoothing.y));

        float smoothingX = Mathf.Max(0.0001f, smoothing.x);
        float smoothingY = Mathf.Max(0.0001f, smoothing.y);
        smoothMouse.x = Mathf.Lerp(smoothMouse.x, mouseDelta.x, 1f / smoothingX);
        smoothMouse.y = Mathf.Lerp(smoothMouse.y, mouseDelta.y, 1f / smoothingY);

        viewYaw += smoothMouse.x;
        viewPitch = Mathf.Clamp(viewPitch + smoothMouse.y, -pitchDownLimit, pitchUpLimit);

        if (!UsesDrivenLookController())
            ApplyLookRotation();
    }

    private void ApplyLookRotation()
    {
        if (EnsureCharacterBody())
            characterBody.rotation = Quaternion.Euler(0f, viewYaw, 0f);

        ApplyDrivenLookRotation(0f);
    }

    public void ApplyDrivenLookRotation(float localYawOffset)
    {
        Vector3 cameraEulerAngles = baseCameraLocalEulerAngles;
        cameraEulerAngles.x = -viewPitch;
        cameraEulerAngles.y += localYawOffset;
        transform.localRotation = Quaternion.Euler(cameraEulerAngles);
    }

    public void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
    }

    public void SetLookInputBlocked(bool blocked)
    {
        lookInputBlocked = blocked;
        if (!blocked)
            return;

        mouseDelta = Vector2.zero;
        smoothMouse = Vector2.zero;
    }

    public void SetSensitivity(float value)
    {
        float actual = BaseSensitivity * value;
        sensitivity = new Vector2(actual, actual);
    }

    private void CacheReferences()
    {
        PhotonView parentPhotonView = GetComponentInParent<PhotonView>();
        if (parentPhotonView != null)
            photonView = parentPhotonView;

        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

        if (HasValidTransform(characterBody))
            return;

        characterBody = null;

        if (playerMovement != null)
        {
            if (HasValidTransform(playerMovement.transform))
                characterBody = playerMovement.transform;
        }
        else if (photonView != null)
            characterBody = photonView.transform;
        else if (HasValidTransform(transform.root))
            characterBody = transform.root;
    }

    private bool UsesDrivenLookController()
    {
        return playerMovement != null && playerMovement.UsesDrivenLookControl;
    }

    private bool HasLocalAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private bool EnsureCharacterBody()
    {
        if (HasValidTransform(characterBody))
            return true;

        CacheReferences();
        return HasValidTransform(characterBody);
    }

    private static bool HasValidTransform(Transform candidate)
    {
        if (candidate == null)
            return false;

        try
        {
            _ = candidate.gameObject;
            return true;
        }
        catch (MissingReferenceException)
        {
            return false;
        }
    }
}
