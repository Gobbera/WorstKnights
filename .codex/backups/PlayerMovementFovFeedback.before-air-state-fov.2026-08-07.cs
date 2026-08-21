using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(20)]
[RequireComponent(typeof(PlayerMovement), typeof(PhotonView))]
public sealed class PlayerMovementFovFeedback : MonoBehaviour
{
    private const string FirstPersonCameraName = "FP_Camera";
    private const string HandsCameraName = "Hands Camera";
    private const float MinimumFovTransitionTime = 0.0001f;

    [Serializable]
    public sealed class MovementFovStateSettings
    {
        [SerializeField] private MovementState state = MovementState.idle;
        [SerializeField] private bool enabled = true;
        [SerializeField] [Tooltip("Positive values widen the FOV. Negative values narrow it.")] private float fovPercentChange;
        [SerializeField] [Min(0f)] private float fovReachTime = 0.12f;
        [SerializeField] [Min(0f)] private float fovRelaxTime = 0.18f;

        public MovementFovStateSettings()
        {
        }

        public MovementFovStateSettings(
            MovementState state,
            float fovPercentChange,
            float fovReachTime,
            float fovRelaxTime,
            bool enabled = true)
        {
            this.state = state;
            this.enabled = enabled;
            this.fovPercentChange = fovPercentChange;
            this.fovReachTime = fovReachTime;
            this.fovRelaxTime = fovRelaxTime;
        }

        public MovementState State => state;
        public bool Enabled => enabled;
        public float FovPercentChange => enabled ? fovPercentChange : 0f;
        public float FovReachTime => Mathf.Max(0f, fovReachTime);
        public float FovRelaxTime => Mathf.Max(0f, fovRelaxTime);
    }

    [Header("Camera")]
    [SerializeField] private bool applyFovFeedback = true;
    [SerializeField] private bool applyFovFeedbackToHandsCamera = true;

    [Header("States")]
    [SerializeField] private List<MovementFovStateSettings> stateFovSettings = new List<MovementFovStateSettings>
    {
        new MovementFovStateSettings(MovementState.idle, 0f, 0.12f, 0.16f),
        new MovementFovStateSettings(MovementState.walking, 2f, 0.12f, 0.16f),
        new MovementFovStateSettings(MovementState.sprinting, 8f, 0.18f, 0.22f)
    };

    private PlayerMovement playerMovement;
    private PhotonView photonView;
    private Camera firstPersonCamera;
    private Camera handsCamera;
    private MovementFovStateSettings activeSettings;
    private MovementFovStateSettings transitionTargetSettings;
    private MovementState lastDesiredState;
    private float firstPersonBaseFov;
    private float handsBaseFov;
    private float currentFirstPersonFov;
    private float currentHandsFov;
    private float currentFovPercent;
    private float transitionStartPercent;
    private float transitionTargetPercent;
    private float transitionStartTime;
    private float transitionEndTime;
    private float lastDesiredTargetPercent;
    private bool hasFirstPersonBaseFov;
    private bool hasHandsBaseFov;
    private bool hasCurrentFirstPersonFov;
    private bool hasCurrentHandsFov;
    private bool hasStateSample;
    private bool hasTransition;

    public bool TryGetCurrentFirstPersonFov(out float fov)
    {
        fov = currentFirstPersonFov;
        return hasCurrentFirstPersonFov;
    }

    public bool TryGetCurrentHandsFov(out float fov)
    {
        fov = currentHandsFov;
        return hasCurrentHandsFov;
    }

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        ResetRuntimeState();
    }

    private void Update()
    {
        if (!HasLocalAuthority())
        {
            ResetFov();
            return;
        }

        UpdateMovementFovState();
        ApplyMovementFov();
    }

    private void LateUpdate()
    {
        if (!HasLocalAuthority())
            return;

        ApplyMovementFov();
    }

    private void OnDisable()
    {
        ResetFov();
    }

    private void CacheReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (photonView == null)
            photonView = GetComponent<PhotonView>();
    }

    private void UpdateMovementFovState()
    {
        if (!applyFovFeedback || playerMovement == null)
        {
            SetImmediate(null, 0f);
            return;
        }

        MovementState desiredState = ResolveDesiredState();
        MovementFovStateSettings desiredSettings = ResolveStateSettings(desiredState);
        float desiredTargetPercent = desiredSettings != null ? desiredSettings.FovPercentChange : 0f;

        if (!hasStateSample)
        {
            SetImmediate(desiredSettings, desiredTargetPercent);
            hasStateSample = true;
            lastDesiredState = desiredState;
            lastDesiredTargetPercent = desiredTargetPercent;
        }
        else if (desiredState != lastDesiredState || !Mathf.Approximately(desiredTargetPercent, lastDesiredTargetPercent))
        {
            RequestTransition(desiredSettings, desiredTargetPercent);
            lastDesiredState = desiredState;
            lastDesiredTargetPercent = desiredTargetPercent;
        }

        UpdateTransition();
    }

    private MovementState ResolveDesiredState()
    {
        MovementState state = playerMovement.CurrentState;
        switch (state)
        {
            case MovementState.idle:
            case MovementState.walking:
            case MovementState.sprinting:
                return state;
            default:
                return MovementState.idle;
        }
    }

    private void RequestTransition(MovementFovStateSettings targetSettings, float targetPercent)
    {
        StartTransition(targetSettings, targetPercent);
    }

    private void StartTransition(MovementFovStateSettings targetSettings, float targetPercent)
    {
        if (Mathf.Abs(currentFovPercent - targetPercent) <= 0.001f)
        {
            SetImmediate(targetSettings, targetPercent);
            return;
        }

        MovementFovStateSettings sourceSettings = transitionTargetSettings ?? activeSettings;
        float duration = ResolveTransitionDuration(sourceSettings, targetSettings, targetPercent);
        if (duration <= MinimumFovTransitionTime)
        {
            SetImmediate(targetSettings, targetPercent);
            return;
        }

        transitionStartPercent = currentFovPercent;
        transitionTargetPercent = targetPercent;
        transitionStartTime = Time.time;
        transitionEndTime = transitionStartTime + duration;
        transitionTargetSettings = targetSettings;
        hasTransition = true;
    }

    private float ResolveTransitionDuration(
        MovementFovStateSettings sourceSettings,
        MovementFovStateSettings targetSettings,
        float targetPercent)
    {
        bool isMovingAwayFromBase = Mathf.Abs(targetPercent) > Mathf.Abs(currentFovPercent);
        if (isMovingAwayFromBase && targetSettings != null)
            return targetSettings.FovReachTime;

        if (sourceSettings != null)
            return sourceSettings.FovRelaxTime;

        return targetSettings != null ? targetSettings.FovReachTime : 0f;
    }

    private void UpdateTransition()
    {
        if (!hasTransition)
            return;

        if (Time.time >= transitionEndTime)
        {
            CompleteTransition();
            return;
        }

        float normalizedTime = Mathf.Clamp01((Time.time - transitionStartTime) / (transitionEndTime - transitionStartTime));
        currentFovPercent = Mathf.Lerp(transitionStartPercent, transitionTargetPercent, SmoothStep01(normalizedTime));
    }

    private void CompleteTransition()
    {
        currentFovPercent = transitionTargetPercent;
        activeSettings = transitionTargetSettings;
        transitionTargetSettings = null;
        hasTransition = false;
    }

    private void SetImmediate(MovementFovStateSettings settings, float targetPercent)
    {
        activeSettings = settings;
        transitionTargetSettings = null;
        currentFovPercent = targetPercent;
        transitionStartPercent = targetPercent;
        transitionTargetPercent = targetPercent;
        hasTransition = false;
    }

    private MovementFovStateSettings ResolveStateSettings(MovementState state)
    {
        if (stateFovSettings == null)
            return null;

        for (int i = 0; i < stateFovSettings.Count; i++)
        {
            MovementFovStateSettings settings = stateFovSettings[i];
            if (settings != null && settings.State == state)
                return settings;
        }

        return null;
    }

    private void ApplyMovementFov()
    {
        ResolveCameras();

        if (firstPersonCamera == null)
            return;

        CacheBaseFovs();

        if (hasFirstPersonBaseFov)
        {
            currentFirstPersonFov = ResolveFov(firstPersonBaseFov, currentFovPercent);
            hasCurrentFirstPersonFov = true;
            firstPersonCamera.fieldOfView = currentFirstPersonFov;
        }

        if (applyFovFeedbackToHandsCamera && hasHandsBaseFov && handsCamera != null)
        {
            currentHandsFov = ResolveFov(handsBaseFov, currentFovPercent);
            hasCurrentHandsFov = true;
            handsCamera.fieldOfView = currentHandsFov;
        }
        else
        {
            currentHandsFov = 0f;
            hasCurrentHandsFov = false;
        }
    }

    private void ResetFov()
    {
        if (hasFirstPersonBaseFov && firstPersonCamera != null)
            firstPersonCamera.fieldOfView = firstPersonBaseFov;

        if (hasHandsBaseFov && handsCamera != null)
            handsCamera.fieldOfView = handsBaseFov;

        ResetRuntimeState();
    }

    private void ResetRuntimeState()
    {
        activeSettings = null;
        transitionTargetSettings = null;
        lastDesiredState = MovementState.idle;
        currentFirstPersonFov = 0f;
        currentHandsFov = 0f;
        currentFovPercent = 0f;
        transitionStartPercent = 0f;
        transitionTargetPercent = 0f;
        transitionStartTime = 0f;
        transitionEndTime = 0f;
        lastDesiredTargetPercent = 0f;
        hasCurrentFirstPersonFov = false;
        hasCurrentHandsFov = false;
        hasStateSample = false;
        hasTransition = false;
    }

    private void ResolveCameras()
    {
        if (firstPersonCamera == null)
            firstPersonCamera = FindCameraByName(FirstPersonCameraName);

        if (handsCamera == null)
            handsCamera = FindCameraByName(HandsCameraName);
    }

    private void CacheBaseFovs()
    {
        if (!hasFirstPersonBaseFov && firstPersonCamera != null)
        {
            firstPersonBaseFov = firstPersonCamera.fieldOfView;
            hasFirstPersonBaseFov = true;
        }

        if (!hasHandsBaseFov && handsCamera != null)
        {
            handsBaseFov = handsCamera.fieldOfView;
            hasHandsBaseFov = true;
        }
    }

    private Camera FindCameraByName(string cameraName)
    {
        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera playerCamera = cameras[i];
            if (playerCamera == null)
                continue;

            if (string.Equals(playerCamera.gameObject.name, cameraName, StringComparison.Ordinal))
                return playerCamera;
        }

        return null;
    }

    private bool HasLocalAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private static float ResolveFov(float baseFov, float percentChange)
    {
        float safeBaseFov = Mathf.Clamp(baseFov, 1f, 179f);
        return Mathf.Clamp(safeBaseFov * (1f + percentChange * 0.01f), 1f, 179f);
    }

    private static float SmoothStep01(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * (3f - 2f * t);
    }
}
