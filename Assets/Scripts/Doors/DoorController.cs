using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Doors/Door Controller")]
public class DoorController : MonoBehaviourPunCallbacks, IPlayerInteractable
{
    private const string RoomPropertyKeyPrefix = "door:";

    public enum DoorStartupState
    {
        StartsOpen = 0,
        StartsClosed = 1,
        StartsLocked = 2
    }

    public enum DoorLockMode
    {
        None = 0,
        KeyItem = 1,
        Passcode = 2,
        SignalSource = 3
    }

    public enum DoorMotionMode
    {
        Rotate = 0,
        Slide = 1,
        Destroy = 2
    }

    public enum DoorSignalRequirementMode
    {
        Any = 0,
        All = 1
    }

    [Header("Identity")]
    [SerializeField] private string doorName = "Door";
    [SerializeField] private Transform movingPart;
    [SerializeField] private new PhotonView photonView;
    [SerializeField] [HideInInspector] private string networkSceneId = string.Empty;
    [SerializeField] private bool prototypeLocalOnly;
    [Header("Startup State")]
    [SerializeField] private DoorStartupState startupState = DoorStartupState.StartsOpen;
    [SerializeField] private bool autoOpenOnUnlock = true;
    [SerializeField] private bool closeWhenSignalTurnsOff = true;
    [SerializeField] private bool relockWhenSignalTurnsOff = true;
    [Header("Motion")]
    [SerializeField] private DoorMotionMode motionMode = DoorMotionMode.Rotate;
    [SerializeField] private Transform rotatePivot;
    [SerializeField] private bool openAwayFromInteractor;
    [SerializeField] private Vector3 openLocalEulerAngles = new Vector3(0f, 90f, 0f);
    [SerializeField] private Vector3 openLocalPositionOffset = Vector3.zero;
    [SerializeField] [Min(0f)] private float moveDuration = 0.35f;
    [SerializeField] [Min(0f)] private float destroyDelay;
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Header("Lock")]
    [SerializeField] private DoorLockMode lockMode = DoorLockMode.None;
    [SerializeField] private ItemDefinition requiredKeyItem;
    [SerializeField] private string requiredPasscode = "1234";
    [SerializeField] private DoorSignalSource[] requiredSignals = Array.Empty<DoorSignalSource>();
    [SerializeField] private DoorSignalRequirementMode signalRequirement = DoorSignalRequirementMode.Any;
    [SerializeField] private bool stayOpenAfterFirstSignalOpen;

    private Vector3 closedLocalPosition;
    private Quaternion closedLocalRotation;
    private Vector3 closedPivotLocalPosition;
    private Quaternion closedPivotLocalRotation;
    private Vector3 openLocalPosition;
    private Quaternion openLocalRotation;
    private float openProgress;
    private int openRotationDirection = 1;
    private bool hasClosedPivotPose;
    private bool subscribedSignals;
    private bool isOpen;
    private bool isLocked;
    private bool destroyArmed;
    private bool destroyExecuted;
    private bool signalPermanentOpenLatched;
    private float destroyExecuteTime = float.NegativeInfinity;

    public int InteractionPriority => 100;
    public string DisplayName => string.IsNullOrWhiteSpace(doorName) ? gameObject.name : doorName;
    public Transform MovingPart => movingPart;
    public bool IsOpen => isOpen;
    public bool IsLocked => isLocked;
    public bool SupportsPasscodeEntry => UsesLockConfiguration && lockMode == DoorLockMode.Passcode;
    public event Action<DoorController> Opened;
    public event Action<DoorController> Closed;
    public event Action<DoorController> Locked;
    public event Action<DoorController> Unlocked;

    private void Reset()
    {
        startupState = DoorStartupState.StartsOpen;

        if (movingPart == null)
            movingPart = transform;

        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        EnsureNetworkSceneId();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
        CaptureClosedPose();

        isOpen = startupState == DoorStartupState.StartsOpen;
        isLocked = startupState == DoorStartupState.StartsLocked;
        signalPermanentOpenLatched = false;

        if (motionMode == DoorMotionMode.Destroy && isOpen)
            ArmDestroyMotion();
        else
            ApplyDoorPoseInstant(isOpen ? 1f : 0f);
    }

    public override void OnEnable()
    {
        base.OnEnable();
        SubscribeSignalSources();
    }

    private void Start()
    {
        EvaluateSignalDrivenState();
        TryApplyRoomSyncedState();
    }

    private void Update()
    {
        UpdateDoorMotion();
    }

    public override void OnDisable()
    {
        UnsubscribeSignalSources();
        base.OnDisable();
    }

    public bool TryInteract(PlayerPickupInteractor interactor)
    {
        ResolveReferences();

        if (UsesLockConfiguration && lockMode == DoorLockMode.SignalSource)
            EvaluateSignalDrivenState();

        if (HasLatchedPermanentSignalOpenState)
            return true;

        if (isLocked)
            return TryHandleLockedInteraction(interactor);

        return RequestSetDoorState(!isOpen, isLocked, interactor);
    }

    public bool TrySubmitPasscode(string submittedPasscode, PlayerPickupInteractor interactor = null)
    {
        if (!UsesLockConfiguration || lockMode != DoorLockMode.Passcode || !isLocked)
            return false;

        string expectedPasscode = NormalizePasscode(requiredPasscode);
        if (string.IsNullOrEmpty(expectedPasscode))
        {
            Debug.LogWarning($"[DoorController] '{DisplayName}' esta com lock de senha sem uma senha configurada.", gameObject);
            return false;
        }

        if (!string.Equals(NormalizePasscode(submittedPasscode), expectedPasscode, StringComparison.Ordinal))
        {
            Debug.Log($"[DoorController] Senha incorreta para '{DisplayName}'.");
            return false;
        }

        return RequestSetDoorState(autoOpenOnUnlock, false, interactor);
    }

    public bool RequestOpen()
    {
        return RequestSetDoorState(true, isLocked);
    }

    public bool RequestClose()
    {
        return RequestSetDoorState(false, isLocked);
    }

    public bool RequestLock()
    {
        return RequestSetDoorState(isOpen, true);
    }

    public bool RequestUnlock()
    {
        return RequestSetDoorState(isOpen, false);
    }

    private bool TryHandleLockedInteraction(PlayerPickupInteractor interactor)
    {
        switch (ResolveEffectiveLockMode())
        {
            case DoorLockMode.None:
                return RequestSetDoorState(autoOpenOnUnlock, false, interactor);
            case DoorLockMode.KeyItem:
                return TryUnlockWithKey(interactor);
            case DoorLockMode.Passcode:
                if (interactor == null)
                    return false;

                interactor.BeginPasscodeEntry(this);
                return true;
            case DoorLockMode.SignalSource:
                Debug.Log($"[DoorController] '{DisplayName}' permanece trancada aguardando o sinal correto.");
                return false;
            default:
                return false;
        }
    }

    private bool TryUnlockWithKey(PlayerPickupInteractor interactor)
    {
        if (requiredKeyItem == null)
        {
            Debug.LogWarning($"[DoorController] '{DisplayName}' esta trancada por chave, mas nenhuma chave foi configurada.", gameObject);
            return false;
        }

        HandEquipmentController equipmentController = interactor != null ? interactor.HandEquipmentController : null;
        if (equipmentController == null || !equipmentController.HasItem(requiredKeyItem))
        {
            Debug.Log($"[DoorController] '{DisplayName}' requer a chave '{requiredKeyItem.ItemName}'.");
            return false;
        }

        return RequestSetDoorState(autoOpenOnUnlock, false, interactor);
    }

    private bool RequestSetDoorState(bool targetOpen, bool targetLocked, PlayerPickupInteractor interactor = null)
    {
        NormalizeRequestedDoorState(ref targetOpen, ref targetLocked);

        int targetOpenRotationDirection = targetOpen
            ? ResolveOpenRotationDirection(interactor)
            : openRotationDirection;
        bool rotationDirectionChanged = targetOpen
            && motionMode == DoorMotionMode.Rotate
            && openRotationDirection != targetOpenRotationDirection;

        if (isOpen == targetOpen && isLocked == targetLocked && !rotationDirectionChanged)
            return false;

        if (ShouldUsePhotonViewSync())
        {
            photonView.RPC(
                nameof(RpcApplyDoorStateWithDirection),
                RpcTarget.AllBufferedViaServer,
                targetOpen,
                targetLocked,
                targetOpenRotationDirection);
            return true;
        }

        if (ShouldUseRoomPropertySync())
        {
            ApplyDoorState(targetOpen, targetLocked, targetOpenRotationDirection);
            PublishRoomSyncedState(targetOpen, targetLocked, openRotationDirection);
            return true;
        }

        return ApplyDoorState(targetOpen, targetLocked, targetOpenRotationDirection);
    }

    private bool ApplyDoorState(bool targetOpen, bool targetLocked)
    {
        int targetOpenRotationDirection = targetOpen ? 1 : openRotationDirection;
        return ApplyDoorState(targetOpen, targetLocked, targetOpenRotationDirection);
    }

    private bool ApplyDoorState(bool targetOpen, bool targetLocked, int targetOpenRotationDirection)
    {
        bool rotationDirectionChanged = targetOpen
            && motionMode == DoorMotionMode.Rotate
            && SetOpenRotationDirection(targetOpenRotationDirection);

        if (isOpen == targetOpen && isLocked == targetLocked && !rotationDirectionChanged)
            return false;

        bool wasOpen = isOpen;
        bool wasLocked = isLocked;
        isOpen = targetOpen;
        isLocked = targetLocked;

        if (rotationDirectionChanged && wasOpen == isOpen)
            ApplyDoorPose(moveCurve != null ? moveCurve.Evaluate(openProgress) : openProgress);

        if (ShouldLatchPermanentSignalOpen(targetOpen, targetLocked))
            signalPermanentOpenLatched = true;

        if (motionMode == DoorMotionMode.Destroy)
        {
            if (isOpen)
            {
                if (!destroyExecuted || !wasOpen)
                    ArmDestroyMotion();
            }
            else
            {
                DisarmDestroyMotion();
            }
        }

        NotifyStateChanged(wasOpen, wasLocked);
        return true;
    }

    private void EvaluateSignalDrivenState()
    {
        if (ResolveEffectiveLockMode() != DoorLockMode.SignalSource)
            return;

        if (HasLatchedPermanentSignalOpenState)
        {
            ApplyDoorState(true, false);
            return;
        }

        bool signalsSatisfied = AreRequiredSignalsSatisfied();
        if (signalsSatisfied)
        {
            bool targetOpen = autoOpenOnUnlock ? true : isOpen;
            ApplyDoorState(targetOpen, false);
            return;
        }

        bool targetLocked = relockWhenSignalTurnsOff ? true : isLocked;
        bool targetOpenWhenInactive = closeWhenSignalTurnsOff ? false : isOpen;
        ApplyDoorState(targetOpenWhenInactive, targetLocked);
    }

    private bool AreRequiredSignalsSatisfied()
    {
        if (requiredSignals == null || requiredSignals.Length == 0)
            return false;

        int validSignalCount = 0;
        int activeSignalCount = 0;

        for (int i = 0; i < requiredSignals.Length; i++)
        {
            DoorSignalSource signalSource = requiredSignals[i];
            if (signalSource == null)
                continue;

            validSignalCount++;
            if (signalSource.IsActive)
                activeSignalCount++;
        }

        if (validSignalCount == 0)
            return false;

        if (signalRequirement == DoorSignalRequirementMode.All)
            return activeSignalCount == validSignalCount;

        return activeSignalCount > 0;
    }

    private void SubscribeSignalSources()
    {
        if (subscribedSignals || requiredSignals == null)
            return;

        for (int i = 0; i < requiredSignals.Length; i++)
        {
            DoorSignalSource signalSource = requiredSignals[i];
            if (signalSource == null)
                continue;

            signalSource.StateChanged += HandleSignalStateChanged;
        }

        subscribedSignals = true;
    }

    private void UnsubscribeSignalSources()
    {
        if (!subscribedSignals || requiredSignals == null)
            return;

        for (int i = 0; i < requiredSignals.Length; i++)
        {
            DoorSignalSource signalSource = requiredSignals[i];
            if (signalSource == null)
                continue;

            signalSource.StateChanged -= HandleSignalStateChanged;
        }

        subscribedSignals = false;
    }

    private void HandleSignalStateChanged(DoorSignalSource signalSource, bool isActive)
    {
        EvaluateSignalDrivenState();
    }

    private void ResolveReferences()
    {
        if (movingPart == null)
            movingPart = transform;

        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (requiredSignals == null)
            requiredSignals = Array.Empty<DoorSignalSource>();

        if (moveCurve == null)
            moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        EnsureNetworkSceneId();
    }

    private void CaptureClosedPose()
    {
        if (movingPart == null)
            return;

        closedLocalPosition = movingPart.localPosition;
        closedLocalRotation = movingPart.localRotation;
        CaptureClosedPivotPose();

        switch (motionMode)
        {
            case DoorMotionMode.Slide:
                openLocalPosition = closedLocalPosition + openLocalPositionOffset;
                openLocalRotation = closedLocalRotation;
                break;
            case DoorMotionMode.Destroy:
                openLocalPosition = closedLocalPosition;
                openLocalRotation = closedLocalRotation;
                break;
            default:
                ResolveRotateOpenPose(openRotationDirection, out openLocalPosition, out openLocalRotation);
                break;
        }
    }

    private void UpdateDoorMotion()
    {
        if (motionMode == DoorMotionMode.Destroy)
        {
            UpdateDestroyMotion();
            return;
        }

        if (movingPart == null)
            return;

        float targetProgress = isOpen ? 1f : 0f;
        if (Mathf.Abs(openProgress - targetProgress) <= 0.0001f)
        {
            openProgress = targetProgress;
            return;
        }

        if (moveDuration <= 0.0001f)
        {
            ApplyDoorPoseInstant(targetProgress);
            return;
        }

        openProgress = Mathf.MoveTowards(openProgress, targetProgress, Time.deltaTime / moveDuration);
        float evaluatedProgress = moveCurve != null ? moveCurve.Evaluate(openProgress) : openProgress;
        ApplyDoorPose(evaluatedProgress);
    }

    private void ApplyDoorPoseInstant(float targetProgress)
    {
        openProgress = Mathf.Clamp01(targetProgress);
        float evaluatedProgress = moveCurve != null ? moveCurve.Evaluate(openProgress) : openProgress;
        ApplyDoorPose(evaluatedProgress);
    }

    private void ApplyDoorPose(float evaluatedProgress)
    {
        if (movingPart == null)
            return;

        if (motionMode == DoorMotionMode.Rotate)
        {
            ApplyRotateDoorPose(evaluatedProgress);
            return;
        }

        movingPart.localPosition = Vector3.LerpUnclamped(closedLocalPosition, openLocalPosition, evaluatedProgress);
        movingPart.localRotation = Quaternion.SlerpUnclamped(closedLocalRotation, openLocalRotation, evaluatedProgress);
    }

    private void ApplyRotateDoorPose(float evaluatedProgress)
    {
        ResolveRotatePose(evaluatedProgress, openRotationDirection, out Vector3 targetLocalPosition, out Quaternion targetLocalRotation);
        movingPart.localPosition = targetLocalPosition;
        movingPart.localRotation = targetLocalRotation;
    }

    private void ResolveRotateOpenPose(int rotationDirection, out Vector3 targetLocalPosition, out Quaternion targetLocalRotation)
    {
        ResolveRotatePose(1f, rotationDirection, out targetLocalPosition, out targetLocalRotation);
    }

    private void ResolveRotatePose(float evaluatedProgress, int rotationDirection, out Vector3 targetLocalPosition, out Quaternion targetLocalRotation)
    {
        Quaternion targetRotationDelta = Quaternion.Euler(openLocalEulerAngles * NormalizeOpenRotationDirection(rotationDirection));
        Quaternion rawRotationDelta = Quaternion.SlerpUnclamped(Quaternion.identity, targetRotationDelta, evaluatedProgress);

        if (!hasClosedPivotPose)
        {
            targetLocalPosition = closedLocalPosition + (openLocalPositionOffset * evaluatedProgress);
            targetLocalRotation = closedLocalRotation * rawRotationDelta;
            return;
        }

        Quaternion pivotSpaceRotation = closedPivotLocalRotation * rawRotationDelta * Quaternion.Inverse(closedPivotLocalRotation);
        Vector3 closedOffsetFromPivot = closedLocalPosition - closedPivotLocalPosition;
        targetLocalPosition = closedPivotLocalPosition + pivotSpaceRotation * closedOffsetFromPivot + (openLocalPositionOffset * evaluatedProgress);
        targetLocalRotation = pivotSpaceRotation * closedLocalRotation;
    }

    private void CaptureClosedPivotPose()
    {
        hasClosedPivotPose = false;

        if (movingPart == null || rotatePivot == null)
            return;

        Transform parentTransform = movingPart.parent;

        if (parentTransform != null)
        {
            closedPivotLocalPosition = parentTransform.InverseTransformPoint(rotatePivot.position);
            closedPivotLocalRotation = Quaternion.Inverse(parentTransform.rotation) * rotatePivot.rotation;
        }
        else
        {
            closedPivotLocalPosition = rotatePivot.position;
            closedPivotLocalRotation = rotatePivot.rotation;
        }

        hasClosedPivotPose = true;
    }

    private bool SetOpenRotationDirection(int targetOpenRotationDirection)
    {
        int normalizedDirection = NormalizeOpenRotationDirection(targetOpenRotationDirection);
        if (openRotationDirection == normalizedDirection)
            return false;

        openRotationDirection = normalizedDirection;

        if (motionMode == DoorMotionMode.Rotate)
            ResolveRotateOpenPose(openRotationDirection, out openLocalPosition, out openLocalRotation);

        return true;
    }

    private int ResolveOpenRotationDirection(PlayerPickupInteractor interactor)
    {
        if (!openAwayFromInteractor
            || motionMode != DoorMotionMode.Rotate
            || interactor == null
            || movingPart == null)
        {
            return 1;
        }

        Vector3 interactorLocalPosition = WorldToMotionSpace(interactor.transform.position);
        Vector3 closedDoorNormal = closedLocalRotation * Vector3.forward;
        if (closedDoorNormal.sqrMagnitude <= 0.0001f)
            return ResolveOpenRotationDirectionByDistance(interactorLocalPosition);

        closedDoorNormal.Normalize();
        float interactorSide = Vector3.Dot(interactorLocalPosition - closedLocalPosition, closedDoorNormal);
        if (Mathf.Abs(interactorSide) <= 0.001f)
            return ResolveOpenRotationDirectionByDistance(interactorLocalPosition);

        ResolveRotateOpenPose(1, out Vector3 defaultOpenLocalPosition, out _);
        ResolveRotateOpenPose(-1, out Vector3 invertedOpenLocalPosition, out _);

        float desiredSideSign = -Mathf.Sign(interactorSide);
        float defaultSideScore = Vector3.Dot(defaultOpenLocalPosition - closedLocalPosition, closedDoorNormal) * desiredSideSign;
        float invertedSideScore = Vector3.Dot(invertedOpenLocalPosition - closedLocalPosition, closedDoorNormal) * desiredSideSign;

        if (Mathf.Abs(defaultSideScore - invertedSideScore) > 0.001f)
            return defaultSideScore > invertedSideScore ? 1 : -1;

        return ResolveOpenRotationDirectionByDistance(interactorLocalPosition);
    }

    private int ResolveOpenRotationDirectionByDistance(Vector3 interactorLocalPosition)
    {
        ResolveRotateOpenPose(1, out Vector3 defaultOpenLocalPosition, out _);
        ResolveRotateOpenPose(-1, out Vector3 invertedOpenLocalPosition, out _);

        float defaultDistance = (defaultOpenLocalPosition - interactorLocalPosition).sqrMagnitude;
        float invertedDistance = (invertedOpenLocalPosition - interactorLocalPosition).sqrMagnitude;
        return invertedDistance > defaultDistance ? -1 : 1;
    }

    private Vector3 WorldToMotionSpace(Vector3 worldPosition)
    {
        Transform parentTransform = movingPart != null ? movingPart.parent : null;
        return parentTransform != null ? parentTransform.InverseTransformPoint(worldPosition) : worldPosition;
    }

    private static int NormalizeOpenRotationDirection(int rotationDirection)
    {
        return rotationDirection < 0 ? -1 : 1;
    }

    private void UpdateDestroyMotion()
    {
        if (!destroyArmed || destroyExecuted)
            return;

        if (destroyDelay > 0f && Time.time < destroyExecuteTime)
            return;

        ExecuteDestroyMotion();
    }

    private void ArmDestroyMotion()
    {
        if (destroyExecuted)
            return;

        destroyArmed = true;
        destroyExecuteTime = Time.time + Mathf.Max(0f, destroyDelay);

        if (destroyDelay <= 0f)
            ExecuteDestroyMotion();
    }

    private void DisarmDestroyMotion()
    {
        destroyArmed = false;
        destroyExecuteTime = float.NegativeInfinity;
    }

    private void ExecuteDestroyMotion()
    {
        if (destroyExecuted)
            return;

        destroyExecuted = true;
        destroyArmed = false;

        DisableDoorColliders();

        GameObject destroyTarget = ResolveDestroyTargetObject();
        if (destroyTarget == null)
        {
            enabled = false;
            return;
        }

        if (Application.isPlaying)
            Destroy(destroyTarget);
        else
            DestroyImmediate(destroyTarget);

        if (destroyTarget != gameObject)
            enabled = false;
    }

    private GameObject ResolveDestroyTargetObject()
    {
        if (movingPart != null)
            return movingPart.gameObject;

        return gameObject;
    }

    private void DisableDoorColliders()
    {
        Collider[] doorColliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < doorColliders.Length; i++)
        {
            if (doorColliders[i] != null)
                doorColliders[i].enabled = false;
        }
    }

    private void NotifyStateChanged(bool wasOpen, bool wasLocked)
    {
        if (wasLocked && !isLocked)
            Unlocked?.Invoke(this);

        if (!wasOpen && isOpen)
            Opened?.Invoke(this);
        else if (wasOpen && !isOpen)
            Closed?.Invoke(this);

        if (!wasLocked && isLocked)
            Locked?.Invoke(this);
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

        string propertyKey = BuildRoomPropertyKey();
        if (!propertiesThatChanged.TryGetValue(propertyKey, out object propertyValue))
            return;

        if (!TryDecodeDoorState(
            propertyValue,
            out bool targetOpen,
            out bool targetLocked,
            out bool permanentSignalOpenLatched,
            out int targetOpenRotationDirection))
        {
            return;
        }

        ApplyRoomSyncedState(targetOpen, targetLocked, permanentSignalOpenLatched, targetOpenRotationDirection);
    }

    private void PublishRoomSyncedState(bool targetOpen, bool targetLocked, int targetOpenRotationDirection)
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        Hashtable roomState = new Hashtable
        {
            { BuildRoomPropertyKey(), EncodeDoorState(targetOpen, targetLocked, signalPermanentOpenLatched, targetOpenRotationDirection) }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomState);
    }

    private void TryApplyRoomSyncedState()
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(BuildRoomPropertyKey(), out object propertyValue))
            return;

        if (!TryDecodeDoorState(
            propertyValue,
            out bool targetOpen,
            out bool targetLocked,
            out bool permanentSignalOpenLatched,
            out int targetOpenRotationDirection))
        {
            return;
        }

        ApplyRoomSyncedState(targetOpen, targetLocked, permanentSignalOpenLatched, targetOpenRotationDirection);
    }

    private void ApplyRoomSyncedState(bool targetOpen, bool targetLocked, bool permanentSignalOpenLatched, int targetOpenRotationDirection)
    {
        signalPermanentOpenLatched = permanentSignalOpenLatched;
        ApplyDoorState(targetOpen, targetLocked, targetOpenRotationDirection);
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

    private static int EncodeDoorState(bool targetOpen, bool targetLocked, bool permanentSignalOpenLatched, int targetOpenRotationDirection)
    {
        int encodedState = 0;
        if (targetOpen)
            encodedState |= 1;

        if (targetLocked)
            encodedState |= 1 << 1;

        if (permanentSignalOpenLatched)
            encodedState |= 1 << 2;

        if (NormalizeOpenRotationDirection(targetOpenRotationDirection) < 0)
            encodedState |= 1 << 3;

        return encodedState;
    }

    private static bool TryDecodeDoorState(
        object propertyValue,
        out bool targetOpen,
        out bool targetLocked,
        out bool permanentSignalOpenLatched,
        out int targetOpenRotationDirection)
    {
        targetOpen = false;
        targetLocked = false;
        permanentSignalOpenLatched = false;
        targetOpenRotationDirection = 1;

        if (propertyValue is not int encodedState)
            return false;

        targetOpen = (encodedState & 1) != 0;
        targetLocked = (encodedState & (1 << 1)) != 0;
        permanentSignalOpenLatched = (encodedState & (1 << 2)) != 0;
        targetOpenRotationDirection = (encodedState & (1 << 3)) != 0 ? -1 : 1;
        return true;
    }

    private void NormalizeRequestedDoorState(ref bool targetOpen, ref bool targetLocked)
    {
        if (!HasLatchedPermanentSignalOpenState)
            return;

        targetOpen = true;
        targetLocked = false;
    }

    private DoorLockMode ResolveEffectiveLockMode()
    {
        return UsesLockConfiguration ? lockMode : DoorLockMode.None;
    }

    private bool UsesLockConfiguration => startupState == DoorStartupState.StartsLocked;
    private bool UsesSignalSourceLock => UsesLockConfiguration && lockMode == DoorLockMode.SignalSource;
    private bool HasLatchedPermanentSignalOpenState => UsesSignalSourceLock && stayOpenAfterFirstSignalOpen && signalPermanentOpenLatched;

    private bool ShouldLatchPermanentSignalOpen(bool targetOpen, bool targetLocked)
    {
        return UsesSignalSourceLock
            && stayOpenAfterFirstSignalOpen
            && targetOpen
            && !targetLocked;
    }

    private static string NormalizePasscode(string submittedPasscode)
    {
        return string.IsNullOrWhiteSpace(submittedPasscode)
            ? string.Empty
            : submittedPasscode.Trim();
    }

    [PunRPC]
    private void RpcApplyDoorState(bool targetOpen, bool targetLocked)
    {
        ApplyDoorState(targetOpen, targetLocked);
    }

    [PunRPC]
    private void RpcApplyDoorStateWithDirection(bool targetOpen, bool targetLocked, int targetOpenRotationDirection)
    {
        ApplyDoorState(targetOpen, targetLocked, targetOpenRotationDirection);
    }

    [ContextMenu("Open Door")]
    private void ContextOpenDoor()
    {
        RequestSetDoorState(true, isLocked);
    }

    [ContextMenu("Close Door")]
    private void ContextCloseDoor()
    {
        RequestSetDoorState(false, isLocked);
    }

    [ContextMenu("Unlock Door")]
    private void ContextUnlockDoor()
    {
        RequestSetDoorState(isOpen, false);
    }

    [ContextMenu("Lock Door")]
    private void ContextLockDoor()
    {
        RequestSetDoorState(isOpen, true);
    }

    private void OnDrawGizmosSelected()
    {
        if (motionMode != DoorMotionMode.Rotate || movingPart == null || rotatePivot == null)
            return;

        Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.9f);
        Gizmos.DrawLine(rotatePivot.position, movingPart.position);
        Gizmos.DrawWireSphere(rotatePivot.position, 0.08f);
    }
}

internal static class SceneNetworkStateIdUtility
{
    public static string BuildSceneObjectId(Transform targetTransform)
    {
        if (targetTransform == null)
            return string.Empty;

        var scene = targetTransform.gameObject.scene;
        if (!scene.IsValid() || string.IsNullOrWhiteSpace(scene.name) || string.IsNullOrWhiteSpace(scene.path))
            return string.Empty;

        string sceneName = scene.name;

        return $"{sceneName}:{BuildStableHierarchyPath(targetTransform)}";
    }

    private static string BuildStableHierarchyPath(Transform targetTransform)
    {
        if (targetTransform == null)
            return string.Empty;

        List<string> pathSegments = new List<string>();
        Transform current = targetTransform;
        while (current != null)
        {
            pathSegments.Add($"{current.name}[{current.GetSiblingIndex()}]");
            current = current.parent;
        }

        pathSegments.Reverse();
        return string.Join("/", pathSegments);
    }
}
