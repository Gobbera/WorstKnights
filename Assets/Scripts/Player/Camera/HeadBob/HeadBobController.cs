using Photon.Pun;
using UnityEngine;

[System.Serializable]
public struct DamageCameraImpactProfile
{
    public PlayerCameraImpactType impactType;
    public bool enabled;
    [Min(0f)] public float duration;
    [Min(0f)] public float oscillationCycles;
    [Min(0f)] public float damageAmplitudeMultiplier;
    public Vector3 positionAmplitude;
    public Vector3 rotationAmplitude;

    public static DamageCameraImpactProfile Create(
        PlayerCameraImpactType impactType,
        float duration,
        float oscillationCycles,
        float damageAmplitudeMultiplier,
        Vector3 positionAmplitude,
        Vector3 rotationAmplitude,
        bool enabled = true)
    {
        DamageCameraImpactProfile profile = default;
        profile.impactType = impactType;
        profile.enabled = enabled;
        profile.duration = Mathf.Max(0f, duration);
        profile.oscillationCycles = Mathf.Max(0f, oscillationCycles);
        profile.damageAmplitudeMultiplier = Mathf.Max(0f, damageAmplitudeMultiplier);
        profile.positionAmplitude = positionAmplitude;
        profile.rotationAmplitude = rotationAmplitude;
        return profile;
    }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
[RequireComponent(typeof(Camera))]
public sealed class HeadBobController : MonoBehaviour
{
    private const string DefaultProfileResourcePath = "HeadBobProfile_Default";
    private const float MinimumDeltaTime = 0.0001f;

    [SerializeField] private HeadBobProfile profile;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private bool applyPosition = true;
    [SerializeField] private bool applyRotation = true;
    [Header("Damage Impact")]
    [SerializeField] private DamageCameraImpactProfile[] damageImpactProfiles = null;
    [Header("Crouch Camera")]
    [SerializeField] private bool applyCrouchCameraHeight = true;
    [SerializeField] private bool autoDeriveCrouchCameraOffset = true;
    [SerializeField] private float crouchCameraLocalYOffset = -0.5f;
    [SerializeField] [Min(0f)] private float crouchCameraSmoothTime = 0.08f;

    private PhotonView ownerView;
    private Vector3 standingLocalPosition;
    private Vector3 currentBaseLocalPosition;
    private Vector3 blendedPositionAmplitude;
    private Vector3 blendedRotationAmplitude;
    private Vector3 currentPositionOffset;
    private Vector3 currentRotationOffset;
    private Vector3 landingPositionOffset;
    private Vector3 landingRotationOffset;
    private Vector3 damageImpactPositionAmplitude;
    private Vector3 damageImpactRotationAmplitude;
    private Vector3 damageImpactPositionOffset;
    private Vector3 damageImpactRotationOffset;
    private Vector3 damageImpactPhaseOffset;
    private float currentStrafeLeanRoll;
    private float currentWeight;
    private float blendedFrequency;
    private float blendedStateIntensity;
    private float bobPhase;
    private float crouchCameraHeightVelocity;
    private float damageImpactDuration;
    private float damageImpactRemainingTime;
    private float damageImpactOscillationCycles;
    private float lowestAirborneVerticalSpeed;
    private bool hasCachedBasePose;
    private bool wasGrounded = true;

    private void Reset()
    {
        CacheReferences();
        ResolveProfile();
    }

    private void Awake()
    {
        CacheReferences();
        ResolveProfile();
        CacheBasePose();
    }

    private void Start()
    {
        CacheBasePose();

        if (playerMovement != null)
        {
            wasGrounded = playerMovement.IsGrounded;
            lowestAirborneVerticalSpeed = playerMovement.VerticalVelocity;
        }
    }

    public void PlayDamageImpact(PlayerCameraImpactType impactType, float damageAmount)
    {
        if (!HasLocalAuthority() || impactType == PlayerCameraImpactType.None)
            return;

        if (!TryResolveDamageImpactProfile(impactType, out DamageCameraImpactProfile profile) || !profile.enabled)
            return;

        float amplitudeScale = 1f + Mathf.Max(0f, damageAmount) * Mathf.Max(0f, profile.damageAmplitudeMultiplier);
        damageImpactPositionAmplitude = profile.positionAmplitude * amplitudeScale;
        damageImpactRotationAmplitude = profile.rotationAmplitude * amplitudeScale;
        damageImpactDuration = Mathf.Max(MinimumDeltaTime, profile.duration);
        damageImpactRemainingTime = damageImpactDuration;
        damageImpactOscillationCycles = Mathf.Max(0.25f, profile.oscillationCycles);
        damageImpactPhaseOffset = new Vector3(
            Random.Range(-Mathf.PI, Mathf.PI),
            Random.Range(-Mathf.PI, Mathf.PI),
            Random.Range(-Mathf.PI, Mathf.PI));
    }

    private void LateUpdate()
    {
        if (!HasLocalAuthority())
        {
            ResetPose();
            return;
        }

        if (playerMovement == null)
        {
            ResetPose();
            return;
        }

        ResolveProfile();
        CacheBasePose();
        float deltaTime = Mathf.Max(Time.deltaTime, MinimumDeltaTime);
        UpdateBaseCameraHeight(deltaTime);

        if (profile == null)
        {
            currentStrafeLeanRoll = 0f;
            ApplyPose(Vector3.zero, Vector3.zero);
            return;
        }

        float planarSpeed = playerMovement.PlanarSpeed;
        bool isGrounded = playerMovement.IsGrounded;
        HeadBobStateSettings settings = profile.ResolveState(playerMovement.CurrentState, isGrounded, planarSpeed);

        UpdateStateBlend(settings, deltaTime);

        float targetWeight = profile.GlobalIntensity * profile.EvaluateSpeedWeight(settings, planarSpeed);
        currentWeight = Damp(currentWeight, targetWeight, profile.StateBlendSharpness, deltaTime);

        bobPhase = Mathf.Repeat(bobPhase + blendedFrequency * deltaTime * Mathf.PI * 2f, Mathf.PI * 2f);

        Vector3 targetPositionOffset = Vector3.Scale(profile.PositionAmplitude, EvaluatePositionWave()) * (currentWeight * blendedStateIntensity);
        Vector3 targetRotationOffset = Vector3.Scale(profile.RotationAmplitude, EvaluateRotationWave()) * (currentWeight * blendedStateIntensity);

        currentPositionOffset = Damp(currentPositionOffset, targetPositionOffset, profile.MotionBlendSharpness, deltaTime);
        currentRotationOffset = Damp(currentRotationOffset, targetRotationOffset, profile.MotionBlendSharpness, deltaTime);

        UpdateLandingSettle(isGrounded, deltaTime);
        UpdateDamageImpact(deltaTime);
        UpdateStrafeLean(deltaTime);

        Vector3 finalRotationOffset = currentRotationOffset + landingRotationOffset + damageImpactRotationOffset;
        finalRotationOffset.z += currentStrafeLeanRoll;

        ApplyPose(
            currentPositionOffset + landingPositionOffset + damageImpactPositionOffset,
            finalRotationOffset);
    }

    private void OnDisable()
    {
        ResetPose();
    }

    private void CacheReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

        if (ownerView == null)
            ownerView = GetComponentInParent<PhotonView>();
    }

    private void ResolveProfile()
    {
        if (profile == null)
            profile = Resources.Load<HeadBobProfile>(DefaultProfileResourcePath);
    }

    private bool HasLocalAuthority()
    {
        return ownerView == null || ownerView.IsMine;
    }

    private void CacheBasePose()
    {
        if (hasCachedBasePose)
            return;

        standingLocalPosition = transform.localPosition;
        currentBaseLocalPosition = standingLocalPosition;
        hasCachedBasePose = true;
    }

    private void UpdateBaseCameraHeight(float deltaTime)
    {
        if (!hasCachedBasePose)
            return;

        if (!applyCrouchCameraHeight || playerMovement == null)
        {
            currentBaseLocalPosition = standingLocalPosition;
            crouchCameraHeightVelocity = 0f;
            return;
        }

        Vector3 targetBaseLocalPosition = standingLocalPosition;
        targetBaseLocalPosition.y += ResolveCrouchCameraTargetYOffset();

        float safeSmoothTime = Mathf.Max(0f, crouchCameraSmoothTime);
        if (safeSmoothTime <= MinimumDeltaTime)
        {
            currentBaseLocalPosition = targetBaseLocalPosition;
            crouchCameraHeightVelocity = 0f;
            return;
        }

        currentBaseLocalPosition.x = targetBaseLocalPosition.x;
        currentBaseLocalPosition.z = targetBaseLocalPosition.z;
        currentBaseLocalPosition.y = Mathf.SmoothDamp(
            currentBaseLocalPosition.y,
            targetBaseLocalPosition.y,
            ref crouchCameraHeightVelocity,
            safeSmoothTime,
            Mathf.Infinity,
            deltaTime);
    }

    private float ResolveCrouchCameraTargetYOffset()
    {
        if (playerMovement == null || playerMovement.CurrentState != MovementState.crouching)
            return 0f;

        if (!autoDeriveCrouchCameraOffset)
            return crouchCameraLocalYOffset;

        MovementConfig movementConfig = playerMovement.Config;
        if (movementConfig == null)
            return crouchCameraLocalYOffset;

        float standingHeight = Mathf.Max(0f, movementConfig.playerHeight);
        float crouchScale = Mathf.Clamp(movementConfig.crouchYScale, 0.1f, 1f);
        float heightDelta = standingHeight - (standingHeight * crouchScale);
        return -heightDelta * 0.5f;
    }

    private void UpdateStateBlend(HeadBobStateSettings settings, float deltaTime)
    {
        blendedFrequency = Damp(blendedFrequency, settings.Frequency, profile.StateBlendSharpness, deltaTime);
        blendedStateIntensity = Damp(blendedStateIntensity, settings.IntensityMultiplier, profile.StateBlendSharpness, deltaTime);
    }

    private void UpdateLandingSettle(bool isGrounded, float deltaTime)
    {
        if (!isGrounded)
            lowestAirborneVerticalSpeed = Mathf.Min(lowestAirborneVerticalSpeed, playerMovement.VerticalVelocity);
        else if (!wasGrounded)
            TriggerLandingSettle(-lowestAirborneVerticalSpeed);

        HeadBobLandingSettings landing = profile.Landing;
        float settleSharpness = landing.RecoverySharpness;
        landingPositionOffset = Damp(landingPositionOffset, Vector3.zero, settleSharpness, deltaTime);
        landingRotationOffset = Damp(landingRotationOffset, Vector3.zero, settleSharpness, deltaTime);

        if (isGrounded)
            lowestAirborneVerticalSpeed = 0f;

        wasGrounded = isGrounded;
    }

    private void TriggerLandingSettle(float landingSpeed)
    {
        HeadBobLandingSettings landing = profile.Landing;
        if (!landing.Enabled || landingSpeed < landing.MinLandingSpeed)
            return;

        float strength = Mathf.InverseLerp(landing.MinLandingSpeed, landing.FullLandingSpeed, landingSpeed);
        landingPositionOffset += Vector3.down * (landing.SettleDistance * strength);
        landingRotationOffset += Vector3.right * (landing.SettlePitch * strength);
    }

    private void UpdateDamageImpact(float deltaTime)
    {
        if (damageImpactRemainingTime <= 0f)
        {
            damageImpactPositionOffset = Vector3.zero;
            damageImpactRotationOffset = Vector3.zero;
            return;
        }

        damageImpactRemainingTime = Mathf.Max(0f, damageImpactRemainingTime - deltaTime);
        float elapsedTime = damageImpactDuration - damageImpactRemainingTime;
        float normalizedTime = damageImpactDuration > MinimumDeltaTime
            ? Mathf.Clamp01(elapsedTime / damageImpactDuration)
            : 1f;
        float envelope = 1f - normalizedTime;
        envelope *= envelope;

        float oscillationPhase = normalizedTime * damageImpactOscillationCycles * Mathf.PI * 2f;
        Vector3 wave = new Vector3(
            Mathf.Sin(oscillationPhase + damageImpactPhaseOffset.x),
            Mathf.Sin((oscillationPhase * 1.17f) + damageImpactPhaseOffset.y),
            Mathf.Sin((oscillationPhase * 1.31f) + damageImpactPhaseOffset.z));

        Vector3 positionWave = new Vector3(wave.x, -Mathf.Abs(wave.y), -Mathf.Abs(wave.z));
        Vector3 rotationWave = new Vector3(-Mathf.Abs(wave.y), wave.x, wave.z);
        damageImpactPositionOffset = Vector3.Scale(damageImpactPositionAmplitude, positionWave) * envelope;
        damageImpactRotationOffset = Vector3.Scale(damageImpactRotationAmplitude, rotationWave) * envelope;

        if (damageImpactRemainingTime > 0f)
            return;

        damageImpactPositionAmplitude = Vector3.zero;
        damageImpactRotationAmplitude = Vector3.zero;
        damageImpactPositionOffset = Vector3.zero;
        damageImpactRotationOffset = Vector3.zero;
    }

    private void UpdateStrafeLean(float deltaTime)
    {
        HeadBobStrafeLeanSettings settings = profile.StrafeLean;
        float targetRoll = 0f;

        if (settings.Enabled && playerMovement != null)
        {
            float lateralInput = playerMovement.AnimationInput.x;
            float absoluteLateralInput = Mathf.Abs(lateralInput);
            float inputDeadzone = settings.InputDeadzone;

            if (absoluteLateralInput > inputDeadzone)
            {
                float normalizedLateralInput = Mathf.InverseLerp(inputDeadzone, 1f, absoluteLateralInput);
                targetRoll = Mathf.Sign(lateralInput) * normalizedLateralInput * settings.Angle * Mathf.Clamp01(currentWeight);
            }
        }

        currentStrafeLeanRoll = Damp(currentStrafeLeanRoll, targetRoll, settings.BlendSharpness, deltaTime);
    }

    private Vector3 EvaluatePositionWave()
    {
        float lateral = Mathf.Sin(bobPhase);
        float vertical = -Mathf.Abs(Mathf.Cos(bobPhase));
        float forward = vertical * 0.45f;
        return new Vector3(lateral, vertical, forward);
    }

    private Vector3 EvaluateRotationWave()
    {
        float lateral = Mathf.Sin(bobPhase);
        float vertical = -Mathf.Abs(Mathf.Cos(bobPhase));
        return new Vector3(vertical, lateral * 0.12f, lateral);
    }

    private void ApplyPose(Vector3 positionOffset, Vector3 rotationOffset)
    {
        if (applyPosition)
            transform.localPosition = currentBaseLocalPosition + positionOffset;

        if (applyRotation)
            transform.localRotation = transform.localRotation * Quaternion.Euler(rotationOffset);
    }

    private void ResetPose()
    {
        if (!hasCachedBasePose)
            return;

        currentBaseLocalPosition = standingLocalPosition;
        crouchCameraHeightVelocity = 0f;
        currentPositionOffset = Vector3.zero;
        currentRotationOffset = Vector3.zero;
        landingPositionOffset = Vector3.zero;
        landingRotationOffset = Vector3.zero;
        damageImpactPositionAmplitude = Vector3.zero;
        damageImpactRotationAmplitude = Vector3.zero;
        damageImpactPositionOffset = Vector3.zero;
        damageImpactRotationOffset = Vector3.zero;
        damageImpactDuration = 0f;
        damageImpactRemainingTime = 0f;
        damageImpactOscillationCycles = 0f;
        currentStrafeLeanRoll = 0f;
        currentWeight = 0f;
        blendedFrequency = 0f;
        blendedStateIntensity = 0f;

        if (applyPosition)
            transform.localPosition = standingLocalPosition;
    }

    private static float Damp(float current, float target, float sharpness, float deltaTime)
    {
        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, sharpness) * deltaTime);
        return Mathf.Lerp(current, target, t);
    }

    private static Vector3 Damp(Vector3 current, Vector3 target, float sharpness, float deltaTime)
    {
        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, sharpness) * deltaTime);
        return Vector3.Lerp(current, target, t);
    }

    private bool TryResolveDamageImpactProfile(PlayerCameraImpactType impactType, out DamageCameraImpactProfile profile)
    {
        DamageCameraImpactProfile[] resolvedProfiles = damageImpactProfiles;
        if (resolvedProfiles == null || resolvedProfiles.Length == 0)
            resolvedProfiles = CreateDefaultDamageImpactProfiles();

        for (int i = 0; i < resolvedProfiles.Length; i++)
        {
            if (resolvedProfiles[i].impactType == impactType)
            {
                profile = resolvedProfiles[i];
                return true;
            }
        }

        profile = default;
        return false;
    }

    private static DamageCameraImpactProfile[] CreateDefaultDamageImpactProfiles()
    {
        return new[]
        {
            DamageCameraImpactProfile.Create(
                PlayerCameraImpactType.DefaultHit,
                0.22f,
                2.35f,
                0.0125f,
                new Vector3(0.018f, 0.012f, 0.026f),
                new Vector3(2.1f, 0.8f, 1.5f)),
            DamageCameraImpactProfile.Create(
                PlayerCameraImpactType.HeavyHit,
                0.32f,
                3.1f,
                0.018f,
                new Vector3(0.03f, 0.02f, 0.04f),
                new Vector3(3.8f, 1.5f, 2.7f))
        };
    }
}
