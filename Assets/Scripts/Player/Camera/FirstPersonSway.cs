using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(75)]
public sealed class FirstPersonSway : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform targetRoot;
    [SerializeField] private PhotonView photonView;

    [Header("Position Sway")]
    [SerializeField] private bool applyPosition = true;
    [SerializeField] private Vector2 positionMultiplier = new Vector2(0.0018f, 0.0014f);
    [SerializeField] private Vector2 maxPositionOffset = new Vector2(0.025f, 0.018f);
    [SerializeField] [Min(0f)] private float positionSmoothTime = 0.06f;

    [Header("Rotation Sway")]
    [SerializeField] private bool applyRotation = true;
    [SerializeField] private Vector3 rotationMultiplier = new Vector3(0.08f, 0.1f, 0.06f);
    [SerializeField] private Vector3 maxRotationOffset = new Vector3(2.5f, 3.5f, 2f);
    [SerializeField] [Min(0f)] private float rotationSmoothTime = 0.07f;

    [Header("Input")]
    [SerializeField] private bool requireLockedCursor = true;

    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 currentPositionOffset;
    private Vector3 positionVelocity;
    private Vector3 currentRotationOffset;
    private Vector3 rotationVelocity;
    private bool hasBasePose;

    private void Reset()
    {
        CacheReferences();
    }

    private void Awake()
    {
        CacheReferences();
        CacheBasePose();
    }

    private void OnEnable()
    {
        CacheReferences();
        CacheBasePose();
    }

    private void LateUpdate()
    {
        CacheReferences();

        if (targetRoot == null)
            return;

        if (!hasBasePose)
            CacheBasePose();

        if (!HasLocalAuthority())
        {
            ResetOffsets(Time.deltaTime);
            ApplySwayPose();
            return;
        }

        Vector2 mouseDelta = ResolveMouseDelta();
        Vector3 targetPositionOffset = applyPosition ? ResolveTargetPositionOffset(mouseDelta) : Vector3.zero;
        Vector3 targetRotationOffset = applyRotation ? ResolveTargetRotationOffset(mouseDelta) : Vector3.zero;
        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);

        currentPositionOffset = SmoothVector(currentPositionOffset, targetPositionOffset, ref positionVelocity, positionSmoothTime, deltaTime);
        currentRotationOffset = SmoothVector(currentRotationOffset, targetRotationOffset, ref rotationVelocity, rotationSmoothTime, deltaTime);

        ApplySwayPose();
    }

    private void OnDisable()
    {
        if (targetRoot == null || !hasBasePose)
            return;

        currentPositionOffset = Vector3.zero;
        currentRotationOffset = Vector3.zero;
        positionVelocity = Vector3.zero;
        rotationVelocity = Vector3.zero;
        ApplySwayPose();
    }

    private void CacheReferences()
    {
        if (targetRoot == null)
            targetRoot = transform;

        if (photonView == null)
            photonView = GetComponentInParent<PhotonView>();
    }

    private void CacheBasePose()
    {
        if (targetRoot == null)
            return;

        baseLocalPosition = targetRoot.localPosition;
        baseLocalRotation = targetRoot.localRotation;
        hasBasePose = true;
    }

    private Vector2 ResolveMouseDelta()
    {
        if (requireLockedCursor && Cursor.lockState != CursorLockMode.Locked)
            return Vector2.zero;

        if (Mouse.current == null)
            return Vector2.zero;

        return Mouse.current.delta.ReadValue();
    }

    private Vector3 ResolveTargetPositionOffset(Vector2 mouseDelta)
    {
        float x = Mathf.Clamp(-mouseDelta.x * positionMultiplier.x, -maxPositionOffset.x, maxPositionOffset.x);
        float y = Mathf.Clamp(-mouseDelta.y * positionMultiplier.y, -maxPositionOffset.y, maxPositionOffset.y);
        return new Vector3(x, y, 0f);
    }

    private Vector3 ResolveTargetRotationOffset(Vector2 mouseDelta)
    {
        float pitch = Mathf.Clamp(mouseDelta.y * rotationMultiplier.x, -maxRotationOffset.x, maxRotationOffset.x);
        float yaw = Mathf.Clamp(-mouseDelta.x * rotationMultiplier.y, -maxRotationOffset.y, maxRotationOffset.y);
        float roll = Mathf.Clamp(mouseDelta.x * rotationMultiplier.z, -maxRotationOffset.z, maxRotationOffset.z);
        return new Vector3(pitch, yaw, roll);
    }

    private void ResetOffsets(float deltaTime)
    {
        float safeDeltaTime = Mathf.Max(deltaTime, 0.0001f);
        currentPositionOffset = SmoothVector(currentPositionOffset, Vector3.zero, ref positionVelocity, positionSmoothTime, safeDeltaTime);
        currentRotationOffset = SmoothVector(currentRotationOffset, Vector3.zero, ref rotationVelocity, rotationSmoothTime, safeDeltaTime);
    }

    private void ApplySwayPose()
    {
        targetRoot.localPosition = baseLocalPosition + currentPositionOffset;
        targetRoot.localRotation = baseLocalRotation * Quaternion.Euler(currentRotationOffset);
    }

    private bool HasLocalAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private static Vector3 SmoothVector(Vector3 current, Vector3 target, ref Vector3 velocity, float smoothTime, float deltaTime)
    {
        if (smoothTime <= 0f)
        {
            velocity = Vector3.zero;
            return target;
        }

        return Vector3.SmoothDamp(current, target, ref velocity, smoothTime, Mathf.Infinity, deltaTime);
    }
}
