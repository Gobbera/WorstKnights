using System;
using System.Collections.Generic;
using System.Globalization;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Platforms/Platform Controller")]
public class PlatformController : MonoBehaviourPunCallbacks
{
    private const string RoomPropertyKeyPrefix = "platform:";
    private const double TimeEpsilon = 0.0001d;

    public enum PlatformMotionMode
    {
        Static = 0,
        PingPong = 1,
        OneWay = 2
    }

    public enum PlatformAxis
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    public enum PlatformDirection
    {
        Positive = 0,
        Negative = 1
    }

    public enum PlatformMovementDirectionMode
    {
        Axis = 0,
        Diagonal = 1
    }

    public enum PlatformActivationMode
    {
        AlwaysActive = 0,
        PlayerOnTop = 1,
        SignalSource = 2
    }

    public enum PlatformSignalRequirementMode
    {
        Any = 0,
        All = 1
    }

    [Serializable]
    public class PlatformMovementPoint
    {
        public PlatformMovementDirectionMode directionMode = PlatformMovementDirectionMode.Axis;
        public PlatformAxis axis = PlatformAxis.X;
        public PlatformDirection direction = PlatformDirection.Positive;
        public Vector3 diagonalDirection = new Vector3(1f, 0f, 1f);
        [Min(0f)] public float distance = 2f;
        [Min(0f)] public float speed = 1f;
    }

    private readonly struct PlatformStateSnapshot
    {
        public PlatformStateSnapshot(
            int stateSequence,
            double movementElapsedTime,
            double movementReferenceTime,
            bool movementActive,
            bool breakTriggered,
            bool isBroken,
            double breakExecuteTime,
            double respawnExecuteTime)
        {
            StateSequence = stateSequence;
            MovementElapsedTime = movementElapsedTime;
            MovementReferenceTime = movementReferenceTime;
            MovementActive = movementActive;
            BreakTriggered = breakTriggered;
            IsBroken = isBroken;
            BreakExecuteTime = breakExecuteTime;
            RespawnExecuteTime = respawnExecuteTime;
        }

        public int StateSequence { get; }
        public double MovementElapsedTime { get; }
        public double MovementReferenceTime { get; }
        public bool MovementActive { get; }
        public bool BreakTriggered { get; }
        public bool IsBroken { get; }
        public double BreakExecuteTime { get; }
        public double RespawnExecuteTime { get; }
    }

    [Header("Identity")]
    [SerializeField] private string platformName = "Platform";
    [SerializeField] private Transform movingPart;
    [SerializeField] [HideInInspector] private string networkSceneId = string.Empty;
    [SerializeField] private bool prototypeLocalOnly;
    [Header("Motion")]
    [SerializeField] private PlatformMotionMode motionMode = PlatformMotionMode.PingPong;
    [SerializeField] private List<PlatformMovementPoint> movementPoints = new List<PlatformMovementPoint>();
    [SerializeField] private bool carryPlayers = true;
    [Header("Activation")]
    [SerializeField] private PlatformActivationMode activationMode = PlatformActivationMode.AlwaysActive;
    [SerializeField] private DoorSignalSource[] activationSignals = Array.Empty<DoorSignalSource>();
    [SerializeField] private PlatformSignalRequirementMode signalRequirement = PlatformSignalRequirementMode.Any;
    [SerializeField] [HideInInspector] private PlatformAxis movementAxis = PlatformAxis.X;
    [SerializeField] [HideInInspector] private PlatformDirection movementDirection = PlatformDirection.Positive;
    [SerializeField] [HideInInspector] [Min(0f)] private float movementDistance = 2f;
    [SerializeField] [HideInInspector] [Min(0f)] private float movementSpeed = 1f;
    [SerializeField] [HideInInspector] private bool legacyMotionSettingsMigrated;
    [Header("Breakable")]
    [SerializeField] private bool breakable;
    [SerializeField] [Min(0f)] private float breakDelay = 0.5f;
    [SerializeField] [Min(0.05f)] private float topTriggerHeight = 0.2f;
    [SerializeField] private bool respawns = true;
    [SerializeField] [Min(0f)] private float respawnDelay = 3f;
    [SerializeField] private LayerMask playerDetectionMask = Physics.DefaultRaycastLayers;

    private const int MaxOverlapHits = 16;
    private static readonly Vector3 DefaultDiagonalDirection = new Vector3(1f, 0f, 1f);

    private readonly Collider[] overlapHits = new Collider[MaxOverlapHits];
    private readonly HashSet<Rigidbody> carriedPassengerBodies = new HashSet<Rigidbody>();
    private readonly List<Vector3> movementPathPoints = new List<Vector3>();
    private readonly List<float> movementSegmentSpeeds = new List<float>();
    private readonly List<double> movementSegmentDurations = new List<double>();

    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Vector3 previousWorldPosition;
    private bool initializedWorldPosition;
    private bool breakTriggered;
    private bool isBroken;
    private double breakExecuteTime = double.PositiveInfinity;
    private double respawnExecuteTime = double.PositiveInfinity;
    private double movementElapsedTime;
    private double movementReferenceTime;
    private bool movementActive;
    private double totalForwardDuration;
    private int lastAppliedStateSequence;
    private Collider[] cachedColliders = Array.Empty<Collider>();
    private bool[] cachedColliderEnabledStates = Array.Empty<bool>();
    private Renderer[] cachedRenderers = Array.Empty<Renderer>();
    private bool[] cachedRendererEnabledStates = Array.Empty<bool>();
    private Rigidbody movingRigidbody;

    public string DisplayName => string.IsNullOrWhiteSpace(platformName) ? gameObject.name : platformName;
    public bool IsBreakable => breakable;
    public bool IsBroken => isBroken;

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
        if (movingPart == null)
            movingPart = transform;

        EnsureMovementPointListInitialized(forceDefaultPoint: true);
        EnsureNetworkSceneId();
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureMovementPointListInitialized();
        CacheRuntimeState();
        ResetPlatformStateForRuntime(GetCurrentSimulationTime());
    }

    private void Start()
    {
        TryApplyRoomSyncedState();
        EnsureRoomStateInitialized();
        ApplyMotionPoseForCurrentState(GetCurrentSimulationTime());
        ResetWorldPositionTracking();
    }

    private void OnValidate()
    {
        ResolveReferences();
        EnsureMovementPointListInitialized();
        ClampConfigurationValues();
        EnsureNetworkSceneId();
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        TryApplyRoomSyncedState();
        EnsureRoomStateInitialized();
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);

        if (!ShouldUseRoomPropertySync() || propertiesThatChanged == null)
            return;

        string propertyKey = BuildRoomPropertyKey();
        if (!propertiesThatChanged.TryGetValue(propertyKey, out object propertyValue))
            return;

        if (!TryDecodeStateSnapshot(propertyValue, out PlatformStateSnapshot snapshot))
            return;

        ApplyRoomSyncedState(snapshot);
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        base.OnMasterClientSwitched(newMasterClient);

        if (!ShouldUseRoomPropertySync())
            return;

        TryApplyRoomSyncedState();
        EnsureRoomStateInitialized();
    }

    private void FixedUpdate()
    {
        ResolveReferences();
        double now = GetCurrentSimulationTime();

        if (ShouldRunAuthoritativeStateLocally())
        {
            bool stateChanged = UpdateAuthoritativeState(now);
            if (stateChanged && ShouldUseRoomPropertySync())
                PublishRoomSyncedState();
        }

        UpdateMotionAndCarry(now);
    }

    public override void OnDisable()
    {
        carriedPassengerBodies.Clear();
        base.OnDisable();
    }

    private void ResolveReferences()
    {
        if (movingPart == null)
            movingPart = transform;

        if (movingPart != null)
            movingRigidbody = movingPart.GetComponent<Rigidbody>();
        else
            movingRigidbody = null;

        if (activationSignals == null)
            activationSignals = Array.Empty<DoorSignalSource>();
    }

    private void CacheRuntimeState()
    {
        if (movingPart == null)
            return;

        initialLocalPosition = movingPart.localPosition;
        initialLocalRotation = movingPart.localRotation;
        cachedColliders = GetComponentsInChildren<Collider>(true);
        cachedColliderEnabledStates = new bool[cachedColliders.Length];
        for (int i = 0; i < cachedColliders.Length; i++)
            cachedColliderEnabledStates[i] = cachedColliders[i] != null && cachedColliders[i].enabled;

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        cachedRendererEnabledStates = new bool[cachedRenderers.Length];
        for (int i = 0; i < cachedRenderers.Length; i++)
            cachedRendererEnabledStates[i] = cachedRenderers[i] != null && cachedRenderers[i].enabled;

        RebuildMovementPath();
    }

    private void ResetPlatformStateForRuntime(double now)
    {
        isBroken = false;
        breakTriggered = false;
        breakExecuteTime = double.PositiveInfinity;
        respawnExecuteTime = double.PositiveInfinity;
        carriedPassengerBodies.Clear();
        ResetMovementTraversalState(now, rebuildPath: true);

        RestoreVisualAndCollisionState();
        RestoreInitialTransform();
    }

    private void ClampConfigurationValues()
    {
        movementDistance = Mathf.Max(0f, movementDistance);
        movementSpeed = Mathf.Max(0f, movementSpeed);
        breakDelay = Mathf.Max(0f, breakDelay);
        topTriggerHeight = Mathf.Max(0.05f, topTriggerHeight);
        respawnDelay = Mathf.Max(0f, respawnDelay);

        if (movementPoints != null)
        {
            for (int i = 0; i < movementPoints.Count; i++)
            {
                if (movementPoints[i] == null)
                    movementPoints[i] = CreateDefaultMovementPoint();

                movementPoints[i].distance = Mathf.Max(0f, movementPoints[i].distance);
                movementPoints[i].speed = Mathf.Max(0f, movementPoints[i].speed);

                if (movementPoints[i].directionMode == PlatformMovementDirectionMode.Diagonal
                    && movementPoints[i].diagonalDirection.sqrMagnitude <= 0.0001f)
                {
                    movementPoints[i].diagonalDirection = DefaultDiagonalDirection;
                }
            }
        }

        if (activationSignals == null)
            activationSignals = Array.Empty<DoorSignalSource>();
    }

    private void UpdateMotionAndCarry(double now)
    {
        Vector3 worldPositionBeforeMove = GetCurrentWorldPosition();
        ApplyMotionPoseForCurrentState(now);
        Vector3 worldPositionAfterMove = GetCurrentWorldPosition();

        if (!initializedWorldPosition)
        {
            previousWorldPosition = worldPositionAfterMove;
            initializedWorldPosition = true;
            return;
        }

        Vector3 worldDelta = worldPositionAfterMove - worldPositionBeforeMove;
        previousWorldPosition = worldPositionAfterMove;

        if (isBroken || !carryPlayers)
            return;

        CarryPlayersOnTop(worldDelta);
    }

    private bool UpdateAuthoritativeState(double now)
    {
        bool stateChanged = false;

        if (!isBroken)
        {
            stateChanged |= UpdateMovementActivationState(now);

            if (breakable && !breakTriggered && IsAnyPlayerStandingOnTop())
                stateChanged |= ScheduleBreak(now);
        }

        stateChanged |= UpdateBreakableState(now);
        return stateChanged;
    }

    private bool UpdateMovementActivationState(double now)
    {
        if (!HasValidMovementPath() || isBroken)
            return SetMovementActive(false, now);

        double currentElapsedTime = GetCurrentMovementElapsedTime(now);
        if (motionMode == PlatformMotionMode.OneWay && currentElapsedTime + TimeEpsilon >= totalForwardDuration)
        {
            bool clampedElapsed = SetMovementElapsed(now, totalForwardDuration);
            bool stoppedMovement = SetMovementActive(false, now);
            return clampedElapsed || stoppedMovement;
        }

        bool desiredMovementActive;
        if (motionMode == PlatformMotionMode.OneWay)
        {
            bool hasStarted = movementActive || movementElapsedTime > TimeEpsilon;
            desiredMovementActive = hasStarted || IsMovementActivationSatisfied();
        }
        else
        {
            desiredMovementActive = IsMovementActivationSatisfied();
        }

        return SetMovementActive(desiredMovementActive, now);
    }

    private bool ScheduleBreak(double now)
    {
        if (breakTriggered || isBroken)
            return false;

        breakTriggered = true;
        breakExecuteTime = now + Math.Max(0d, breakDelay);

        if (breakDelay <= 0f)
            return BreakPlatform(now);

        return true;
    }

    private bool UpdateBreakableState(double now)
    {
        bool stateChanged = false;

        if (breakTriggered && !isBroken && now + TimeEpsilon >= breakExecuteTime)
            stateChanged |= BreakPlatform(now);

        if (isBroken && respawns && now + TimeEpsilon >= respawnExecuteTime)
            stateChanged |= RespawnPlatform(now);

        return stateChanged;
    }

    private bool BreakPlatform(double now)
    {
        if (isBroken)
            return false;

        SetMovementActive(false, now);
        isBroken = true;
        breakTriggered = false;
        breakExecuteTime = double.PositiveInfinity;
        DisableVisualAndCollisionState();
        carriedPassengerBodies.Clear();

        if (respawns)
            respawnExecuteTime = now + Math.Max(0d, respawnDelay);
        else
            respawnExecuteTime = double.PositiveInfinity;

        ResetWorldPositionTracking();
        return true;
    }

    private bool RespawnPlatform(double now)
    {
        if (!isBroken)
            return false;

        isBroken = false;
        breakTriggered = false;
        breakExecuteTime = double.PositiveInfinity;
        respawnExecuteTime = double.PositiveInfinity;
        ResetMovementTraversalState(now, rebuildPath: true);
        RestoreVisualAndCollisionState();
        RestoreInitialTransform();
        carriedPassengerBodies.Clear();
        ResetWorldPositionTracking();
        return true;
    }

    private void ApplyMotionPoseForCurrentState(double now)
    {
        if (movingPart == null)
            return;

        if (isBroken)
            return;

        Vector3 targetLocalPosition = initialLocalPosition;
        if (HasValidMovementPath())
            targetLocalPosition = EvaluateLocalPositionAtTime(GetCurrentMovementElapsedTime(now));

        ApplyLocalPosition(targetLocalPosition);
    }

    private Vector3 EvaluateLocalPositionAtTime(double elapsedTime)
    {
        if (movementPathPoints.Count == 0)
            return initialLocalPosition;

        if (movementPathPoints.Count == 1 || totalForwardDuration <= TimeEpsilon)
            return movementPathPoints[0];

        if (motionMode == PlatformMotionMode.OneWay)
            return EvaluateForwardPathPosition(ClampOneWayElapsedTime(elapsedTime));

        double cycleDuration = totalForwardDuration * 2d;
        if (cycleDuration <= TimeEpsilon)
            return movementPathPoints[0];

        double cycleTime = elapsedTime % cycleDuration;
        if (cycleTime < 0d)
            cycleTime += cycleDuration;

        if (cycleTime <= totalForwardDuration)
            return EvaluateForwardPathPosition(cycleTime);

        double backwardTime = cycleTime - totalForwardDuration;
        return EvaluateForwardPathPosition(Math.Max(0d, totalForwardDuration - backwardTime));
    }

    private Vector3 EvaluateForwardPathPosition(double elapsedTime)
    {
        double remainingTime = Math.Max(0d, elapsedTime);

        for (int i = 0; i < movementSegmentDurations.Count; i++)
        {
            double segmentDuration = movementSegmentDurations[i];
            Vector3 segmentStart = movementPathPoints[i];
            Vector3 segmentEnd = movementPathPoints[i + 1];

            if (segmentDuration <= TimeEpsilon)
            {
                if (remainingTime <= TimeEpsilon)
                    return segmentStart;

                remainingTime -= segmentDuration;
                continue;
            }

            if (remainingTime <= segmentDuration)
            {
                float segmentProgress = Mathf.Clamp01((float)(remainingTime / segmentDuration));
                return Vector3.LerpUnclamped(segmentStart, segmentEnd, segmentProgress);
            }

            remainingTime -= segmentDuration;
        }

        return movementPathPoints[movementPathPoints.Count - 1];
    }

    private double GetCurrentMovementElapsedTime(double now)
    {
        double effectiveElapsedTime = movementElapsedTime;
        if (movementActive)
            effectiveElapsedTime += Math.Max(0d, now - movementReferenceTime);

        return motionMode == PlatformMotionMode.OneWay
            ? ClampOneWayElapsedTime(effectiveElapsedTime)
            : Math.Max(0d, effectiveElapsedTime);
    }

    private bool SetMovementActive(bool active, double now)
    {
        if (movementActive == active)
            return false;

        movementElapsedTime = GetCurrentMovementElapsedTime(now);
        movementReferenceTime = now;
        movementActive = active;
        return true;
    }

    private bool SetMovementElapsed(double now, double targetElapsedTime)
    {
        double clampedElapsedTime = motionMode == PlatformMotionMode.OneWay
            ? ClampOneWayElapsedTime(targetElapsedTime)
            : Math.Max(0d, targetElapsedTime);

        if (Math.Abs(movementElapsedTime - clampedElapsedTime) <= TimeEpsilon && Math.Abs(movementReferenceTime - now) <= TimeEpsilon)
            return false;

        movementElapsedTime = clampedElapsedTime;
        movementReferenceTime = now;
        return true;
    }

    private double ClampOneWayElapsedTime(double elapsedTime)
    {
        if (totalForwardDuration <= TimeEpsilon)
            return 0d;

        return Math.Max(0d, Math.Min(totalForwardDuration, elapsedTime));
    }

    private bool IsMovementActivationSatisfied()
    {
        switch (activationMode)
        {
            case PlatformActivationMode.PlayerOnTop:
                return IsAnyPlayerStandingOnTop();
            case PlatformActivationMode.SignalSource:
                return AreActivationSignalsSatisfied();
            default:
                return true;
        }
    }

    private bool AreActivationSignalsSatisfied()
    {
        if (activationSignals == null || activationSignals.Length == 0)
            return false;

        int validSignalCount = 0;
        int activeSignalCount = 0;

        for (int i = 0; i < activationSignals.Length; i++)
        {
            DoorSignalSource signalSource = activationSignals[i];
            if (signalSource == null)
                continue;

            validSignalCount++;
            if (signalSource.IsActive)
                activeSignalCount++;
        }

        if (validSignalCount == 0)
            return false;

        if (signalRequirement == PlatformSignalRequirementMode.All)
            return activeSignalCount == validSignalCount;

        return activeSignalCount > 0;
    }

    private void ApplyLocalPosition(Vector3 targetLocalPosition)
    {
        if (movingRigidbody != null && movingPart == transform && movingRigidbody.isKinematic)
        {
            Vector3 targetWorldPosition = movingPart.parent != null
                ? movingPart.parent.TransformPoint(targetLocalPosition)
                : targetLocalPosition;
            movingRigidbody.MovePosition(targetWorldPosition);
            return;
        }

        movingPart.localPosition = targetLocalPosition;
    }

    private void EnsureMovementPointListInitialized(bool forceDefaultPoint = false)
    {
        if (movementPoints == null)
            movementPoints = new List<PlatformMovementPoint>();

        if (!legacyMotionSettingsMigrated)
        {
            if (movementPoints.Count == 0)
                movementPoints.Add(CreateMovementPointFromLegacyConfig());

            legacyMotionSettingsMigrated = true;
        }

        if (forceDefaultPoint && movementPoints.Count == 0)
            movementPoints.Add(CreateDefaultMovementPoint());
    }

    private PlatformMovementPoint CreateMovementPointFromLegacyConfig()
    {
        return new PlatformMovementPoint
        {
            directionMode = PlatformMovementDirectionMode.Axis,
            axis = movementAxis,
            direction = movementDirection,
            diagonalDirection = DefaultDiagonalDirection,
            distance = movementDistance,
            speed = movementSpeed
        };
    }

    private static PlatformMovementPoint CreateDefaultMovementPoint()
    {
        return new PlatformMovementPoint
        {
            directionMode = PlatformMovementDirectionMode.Axis,
            axis = PlatformAxis.X,
            direction = PlatformDirection.Positive,
            diagonalDirection = DefaultDiagonalDirection,
            distance = 2f,
            speed = 1f
        };
    }

    private void RebuildMovementPath()
    {
        BuildMovementPath(initialLocalPosition, movementPathPoints, movementSegmentSpeeds, movementSegmentDurations);
    }

    private void ResetMovementTraversalState(double now, bool rebuildPath)
    {
        if (rebuildPath)
            RebuildMovementPath();

        movementElapsedTime = 0d;
        movementReferenceTime = now;
        movementActive = false;
    }

    private void BuildMovementPath(
        Vector3 originLocalPosition,
        List<Vector3> localPathPoints,
        List<float> segmentSpeeds,
        List<double> segmentDurations)
    {
        localPathPoints.Clear();
        segmentSpeeds?.Clear();
        segmentDurations?.Clear();
        localPathPoints.Add(originLocalPosition);
        totalForwardDuration = 0d;

        if (movementPoints == null)
            return;

        Vector3 currentPoint = originLocalPosition;
        for (int i = 0; i < movementPoints.Count; i++)
        {
            PlatformMovementPoint movementPoint = movementPoints[i];
            if (movementPoint == null)
                continue;

            float segmentDistance = Mathf.Max(0f, movementPoint.distance);
            if (segmentDistance <= 0f)
                continue;

            currentPoint += GetMovementDirectionVector(movementPoint) * segmentDistance;
            localPathPoints.Add(currentPoint);

            float segmentSpeed = Mathf.Max(0f, movementPoint.speed);
            segmentSpeeds?.Add(segmentSpeed);

            double segmentDuration = segmentSpeed > 0f
                ? segmentDistance / segmentSpeed
                : double.PositiveInfinity;
            segmentDurations?.Add(segmentDuration);
            if (!double.IsInfinity(segmentDuration))
                totalForwardDuration += segmentDuration;
        }
    }

    private Vector3 GetMovementDirectionVector(PlatformMovementPoint movementPoint)
    {
        if (movementPoint == null)
            return Vector3.right;

        if (movementPoint.directionMode == PlatformMovementDirectionMode.Diagonal)
        {
            Vector3 diagonalDirection = movementPoint.diagonalDirection.sqrMagnitude <= 0.0001f
                ? DefaultDiagonalDirection
                : movementPoint.diagonalDirection;
            return diagonalDirection.normalized;
        }

        return GetAxisDirectionVector(movementPoint.axis, movementPoint.direction);
    }

    private Vector3 GetAxisDirectionVector(PlatformAxis axis, PlatformDirection direction)
    {
        float directionSign = direction == PlatformDirection.Positive ? 1f : -1f;
        switch (axis)
        {
            case PlatformAxis.Y:
                return Vector3.up * directionSign;
            case PlatformAxis.Z:
                return Vector3.forward * directionSign;
            default:
                return Vector3.right * directionSign;
        }
    }

    private void DisableVisualAndCollisionState()
    {
        for (int i = 0; i < cachedColliders.Length; i++)
        {
            if (cachedColliders[i] != null)
                cachedColliders[i].enabled = false;
        }

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] != null)
                cachedRenderers[i].enabled = false;
        }
    }

    private void RestoreVisualAndCollisionState()
    {
        for (int i = 0; i < cachedColliders.Length; i++)
        {
            if (cachedColliders[i] != null)
                cachedColliders[i].enabled = cachedColliderEnabledStates[i];
        }

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] != null)
                cachedRenderers[i].enabled = cachedRendererEnabledStates[i];
        }
    }

    private void RestoreInitialTransform()
    {
        if (movingPart == null)
            return;

        if (movingRigidbody != null && movingPart == transform && movingRigidbody.isKinematic)
        {
            Vector3 targetWorldPosition = movingPart.parent != null
                ? movingPart.parent.TransformPoint(initialLocalPosition)
                : initialLocalPosition;
            Quaternion targetWorldRotation = movingPart.parent != null
                ? movingPart.parent.rotation * initialLocalRotation
                : initialLocalRotation;

            movingRigidbody.position = targetWorldPosition;
            movingRigidbody.rotation = targetWorldRotation;
        }
        else
        {
            movingPart.localPosition = initialLocalPosition;
            movingPart.localRotation = initialLocalRotation;
        }
    }

    private void CarryPlayersOnTop(Vector3 worldDelta)
    {
        if (worldDelta.sqrMagnitude <= 0.0000001f)
            return;

        if (!TryGetTopTriggerVolume(out Vector3 volumeCenter, out Vector3 volumeExtents))
            return;

        carriedPassengerBodies.Clear();
        int hitCount = Physics.OverlapBoxNonAlloc(
            volumeCenter,
            volumeExtents,
            overlapHits,
            Quaternion.identity,
            playerDetectionMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapHits[i];
            overlapHits[i] = null;
            if (hit == null || IsSelfCollider(hit))
                continue;

            PlayerSetup playerSetup = hit.GetComponentInParent<PlayerSetup>();
            if (playerSetup == null)
                continue;

            Rigidbody passengerRigidbody = playerSetup.GetComponent<Rigidbody>();
            if (passengerRigidbody == null || passengerRigidbody.isKinematic || !carriedPassengerBodies.Add(passengerRigidbody))
                continue;

            passengerRigidbody.MovePosition(passengerRigidbody.position + worldDelta);
        }
    }

    private bool IsAnyPlayerStandingOnTop()
    {
        if (!TryGetTopTriggerVolume(out Vector3 volumeCenter, out Vector3 volumeExtents))
            return false;

        int hitCount = Physics.OverlapBoxNonAlloc(
            volumeCenter,
            volumeExtents,
            overlapHits,
            Quaternion.identity,
            playerDetectionMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapHits[i];
            overlapHits[i] = null;
            if (hit == null || IsSelfCollider(hit))
                continue;

            if (hit.GetComponentInParent<PlayerSetup>() != null)
                return true;
        }

        return false;
    }

    private bool TryGetTopTriggerVolume(out Vector3 volumeCenter, out Vector3 volumeExtents)
    {
        volumeCenter = Vector3.zero;
        volumeExtents = Vector3.zero;

        if (!TryGetPlatformWorldBounds(out Bounds platformBounds))
            return false;

        float extentY = Mathf.Max(0.025f, topTriggerHeight * 0.5f);
        volumeExtents = new Vector3(
            Mathf.Max(0.05f, platformBounds.extents.x * 0.95f),
            extentY,
            Mathf.Max(0.05f, platformBounds.extents.z * 0.95f));
        volumeCenter = new Vector3(
            platformBounds.center.x,
            platformBounds.max.y + extentY,
            platformBounds.center.z);
        return true;
    }

    private bool TryGetPlatformWorldBounds(out Bounds platformBounds)
    {
        platformBounds = default;
        bool hasBounds = false;
        Collider[] collidersToCheck = cachedColliders.Length > 0 ? cachedColliders : GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < collidersToCheck.Length; i++)
        {
            Collider cachedCollider = collidersToCheck[i];
            if (cachedCollider == null || !cachedCollider.enabled)
                continue;

            if (!hasBounds)
            {
                platformBounds = cachedCollider.bounds;
                hasBounds = true;
            }
            else
            {
                platformBounds.Encapsulate(cachedCollider.bounds);
            }
        }

        if (hasBounds)
            return true;

        Renderer[] renderersToCheck = cachedRenderers.Length > 0 ? cachedRenderers : GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderersToCheck.Length; i++)
        {
            Renderer cachedRenderer = renderersToCheck[i];
            if (cachedRenderer == null || !cachedRenderer.enabled)
                continue;

            if (!hasBounds)
            {
                platformBounds = cachedRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                platformBounds.Encapsulate(cachedRenderer.bounds);
            }
        }

        return hasBounds;
    }

    private bool IsSelfCollider(Collider candidate)
    {
        return candidate != null && candidate.transform.IsChildOf(transform);
    }

    private Vector3 GetCurrentWorldPosition()
    {
        return movingPart != null ? movingPart.position : transform.position;
    }

    private void ResetWorldPositionTracking()
    {
        previousWorldPosition = GetCurrentWorldPosition();
        initializedWorldPosition = true;
    }

    private bool HasValidMovementPath()
    {
        return motionMode != PlatformMotionMode.Static
            && movementPathPoints.Count > 1
            && movementSegmentSpeeds.Count > 0
            && totalForwardDuration > TimeEpsilon;
    }

    private bool ShouldRunAuthoritativeStateLocally()
    {
        return !ShouldUseRoomPropertySync() || PhotonNetwork.IsMasterClient;
    }

    private bool ShouldUseRoomPropertySync()
    {
        return !prototypeLocalOnly
            && PhotonNetwork.InRoom
            && !PhotonNetwork.OfflineMode;
    }

    private double GetCurrentSimulationTime()
    {
        return ShouldUseRoomPropertySync()
            ? PhotonNetwork.Time
            : Time.timeAsDouble;
    }

    private void EnsureRoomStateInitialized()
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(BuildRoomPropertyKey(), out object propertyValue)
            && TryDecodeStateSnapshot(propertyValue, out PlatformStateSnapshot snapshot))
        {
            ApplyRoomSyncedState(snapshot);
            return;
        }

        if (!PhotonNetwork.IsMasterClient)
            return;

        PublishRoomSyncedState();
    }

    private void TryApplyRoomSyncedState()
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(BuildRoomPropertyKey(), out object propertyValue))
            return;

        if (!TryDecodeStateSnapshot(propertyValue, out PlatformStateSnapshot snapshot))
            return;

        ApplyRoomSyncedState(snapshot);
    }

    private void PublishRoomSyncedState()
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        int nextSequence = Math.Max(lastAppliedStateSequence, 0) + 1;
        lastAppliedStateSequence = nextSequence;

        PlatformStateSnapshot snapshot = new PlatformStateSnapshot(
            nextSequence,
            movementElapsedTime,
            movementReferenceTime,
            movementActive,
            breakTriggered,
            isBroken,
            breakExecuteTime,
            respawnExecuteTime);

        Hashtable roomState = new Hashtable
        {
            { BuildRoomPropertyKey(), EncodeStateSnapshot(snapshot) }
        };

        PhotonNetwork.CurrentRoom.SetCustomProperties(roomState);
    }

    private void ApplyRoomSyncedState(PlatformStateSnapshot snapshot)
    {
        if (snapshot.StateSequence <= lastAppliedStateSequence)
            return;

        bool wasBroken = isBroken;

        lastAppliedStateSequence = snapshot.StateSequence;
        movementElapsedTime = motionMode == PlatformMotionMode.OneWay
            ? ClampOneWayElapsedTime(snapshot.MovementElapsedTime)
            : Math.Max(0d, snapshot.MovementElapsedTime);
        movementReferenceTime = snapshot.MovementReferenceTime;
        movementActive = snapshot.MovementActive && !snapshot.IsBroken;
        breakTriggered = snapshot.BreakTriggered;
        isBroken = snapshot.IsBroken;
        breakExecuteTime = snapshot.BreakExecuteTime;
        respawnExecuteTime = snapshot.RespawnExecuteTime;

        if (!wasBroken && isBroken)
        {
            DisableVisualAndCollisionState();
            carriedPassengerBodies.Clear();
        }
        else if (wasBroken && !isBroken)
        {
            RestoreVisualAndCollisionState();
            carriedPassengerBodies.Clear();
        }

        ApplyMotionPoseForCurrentState(GetCurrentSimulationTime());
        ResetWorldPositionTracking();
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

    private string EncodeStateSnapshot(PlatformStateSnapshot snapshot)
    {
        return string.Join(
            "|",
            snapshot.StateSequence.ToString(CultureInfo.InvariantCulture),
            snapshot.MovementElapsedTime.ToString("R", CultureInfo.InvariantCulture),
            snapshot.MovementReferenceTime.ToString("R", CultureInfo.InvariantCulture),
            snapshot.MovementActive ? "1" : "0",
            snapshot.BreakTriggered ? "1" : "0",
            snapshot.IsBroken ? "1" : "0",
            EncodeDouble(snapshot.BreakExecuteTime),
            EncodeDouble(snapshot.RespawnExecuteTime));
    }

    private bool TryDecodeStateSnapshot(object propertyValue, out PlatformStateSnapshot snapshot)
    {
        snapshot = default;

        if (!(propertyValue is string encodedState) || string.IsNullOrWhiteSpace(encodedState))
            return false;

        string[] tokens = encodedState.Split('|');
        if (tokens.Length != 8)
            return false;

        if (!int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stateSequence)
            || !double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double movementElapsed)
            || !double.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double movementReference)
            || !TryDecodeBool(tokens[3], out bool snapshotMovementActive)
            || !TryDecodeBool(tokens[4], out bool snapshotBreakTriggered)
            || !TryDecodeBool(tokens[5], out bool snapshotIsBroken)
            || !TryDecodeDouble(tokens[6], out double snapshotBreakExecuteTime)
            || !TryDecodeDouble(tokens[7], out double snapshotRespawnExecuteTime))
        {
            return false;
        }

        snapshot = new PlatformStateSnapshot(
            stateSequence,
            movementElapsed,
            movementReference,
            snapshotMovementActive,
            snapshotBreakTriggered,
            snapshotIsBroken,
            snapshotBreakExecuteTime,
            snapshotRespawnExecuteTime);
        return true;
    }

    private static string EncodeDouble(double value)
    {
        if (double.IsPositiveInfinity(value))
            return "INF";

        if (double.IsNegativeInfinity(value))
            return "-INF";

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool TryDecodeDouble(string value, out double decodedValue)
    {
        if (string.Equals(value, "INF", StringComparison.Ordinal))
        {
            decodedValue = double.PositiveInfinity;
            return true;
        }

        if (string.Equals(value, "-INF", StringComparison.Ordinal))
        {
            decodedValue = double.NegativeInfinity;
            return true;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decodedValue);
    }

    private static bool TryDecodeBool(string value, out bool result)
    {
        switch (value)
        {
            case "0":
                result = false;
                return true;

            case "1":
                result = true;
                return true;

            default:
                result = false;
                return false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        ResolveReferences();

        if (movingPart != null && motionMode != PlatformMotionMode.Static)
        {
            List<Vector3> previewPathPoints = new List<Vector3>();
            BuildMovementPath(GetPreviewPathOriginLocalPosition(), previewPathPoints, null, null);

            if (previewPathPoints.Count > 1)
            {
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
                for (int i = 0; i < previewPathPoints.Count; i++)
                {
                    Vector3 worldPoint = TransformLocalPathPointToWorld(previewPathPoints[i]);
                    Gizmos.DrawWireSphere(worldPoint, 0.08f);

                    if (i >= previewPathPoints.Count - 1)
                        continue;

                    Vector3 nextWorldPoint = TransformLocalPathPointToWorld(previewPathPoints[i + 1]);
                    Gizmos.DrawLine(worldPoint, nextWorldPoint);
                }
            }
        }

        if (!breakable || !TryGetTopTriggerVolume(out Vector3 volumeCenter, out Vector3 volumeExtents))
            return;

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.25f);
        Gizmos.DrawCube(volumeCenter, volumeExtents * 2f);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(volumeCenter, volumeExtents * 2f);
    }

    private Vector3 GetPreviewPathOriginLocalPosition()
    {
        if (movingPart == null)
            return transform.localPosition;

        return Application.isPlaying ? initialLocalPosition : movingPart.localPosition;
    }

    private Vector3 TransformLocalPathPointToWorld(Vector3 localPathPoint)
    {
        if (movingPart != null && movingPart.parent != null)
            return movingPart.parent.TransformPoint(localPathPoint);

        return localPathPoint;
    }
}
