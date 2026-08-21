using UnityEngine;

[CreateAssetMenu(fileName = "HeadBobProfile", menuName = "Player/Camera/Head Bob Profile")]
public sealed class HeadBobProfile : ScriptableObject
{
    [Header("Global")]
    [SerializeField] [Range(0f, 2f)] private float globalIntensity = 1f;
    [SerializeField] [Min(0f)] private float movementThreshold = 0.08f;
    [SerializeField] [Min(0.01f)] private float stateBlendSharpness = 8f;
    [SerializeField] [Min(0.01f)] private float motionBlendSharpness = 12f;
    [SerializeField] [Min(0f)] private float horizontalAmplitude = 0.012f;
    [SerializeField] [Min(0f)] private float verticalAmplitude = 0.036f;
    [SerializeField] [Min(0f)] private float forwardAmplitude = 0.01f;
    [SerializeField] [Min(0f)] private float pitchAmplitude = 1.15f;
    [SerializeField] [Min(0f)] private float rollAmplitude = 1.05f;

    [Header("Strafe Lean")]
    [SerializeField] private HeadBobStrafeLeanSettings strafeLean = HeadBobStrafeLeanSettings.CreateDefault();

    [Header("States")]
    [SerializeField] private HeadBobStateSettings idle = HeadBobStateSettings.CreateIdle();
    [SerializeField] private HeadBobStateSettings walk = HeadBobStateSettings.CreateWalk();
    [SerializeField] private HeadBobStateSettings sprint = HeadBobStateSettings.CreateSprint();
    [SerializeField] private HeadBobStateSettings crouch = HeadBobStateSettings.CreateCrouch();
    [SerializeField] private HeadBobStateSettings airborne = HeadBobStateSettings.CreateAirborne();

    [Header("Landing Settle")]
    [SerializeField] private HeadBobLandingSettings landing = HeadBobLandingSettings.CreateDefault();

    public float GlobalIntensity => globalIntensity;
    public float MovementThreshold => movementThreshold;
    public float StateBlendSharpness => Mathf.Max(0.01f, stateBlendSharpness);
    public float MotionBlendSharpness => Mathf.Max(0.01f, motionBlendSharpness);
    public HeadBobStrafeLeanSettings StrafeLean => strafeLean;
    public HeadBobLandingSettings Landing => landing;
    public Vector3 PositionAmplitude => new Vector3(horizontalAmplitude, verticalAmplitude, forwardAmplitude);
    public Vector3 RotationAmplitude => new Vector3(pitchAmplitude, 0f, rollAmplitude);

    public HeadBobStateSettings ResolveState(MovementState movementState, bool isGrounded, float planarSpeed)
    {
        if (!isGrounded)
            return airborne;

        if (movementState == MovementState.crouching)
            return crouch;

        if (planarSpeed <= movementThreshold)
            return idle;

        return movementState == MovementState.sprinting ? sprint : walk;
    }

    public float EvaluateSpeedWeight(HeadBobStateSettings settings, float planarSpeed)
    {
        if (settings.ReferenceSpeed <= 0f)
            return 1f;

        float normalizedSpeed = Mathf.Clamp01(planarSpeed / settings.ReferenceSpeed);
        return Mathf.SmoothStep(0f, 1f, normalizedSpeed);
    }
}

[System.Serializable]
public struct HeadBobStrafeLeanSettings
{
    [SerializeField] private bool enabled;
    [SerializeField] [Range(-15f, 15f)] private float angle;
    [SerializeField] [Min(0.01f)] private float blendSharpness;
    [SerializeField] [Range(0f, 0.95f)] private float inputDeadzone;

    public bool Enabled => enabled;
    public float Angle => angle;
    public float BlendSharpness => Mathf.Max(0.01f, blendSharpness);
    public float InputDeadzone => Mathf.Clamp(inputDeadzone, 0f, 0.95f);

    public static HeadBobStrafeLeanSettings CreateDefault()
    {
        HeadBobStrafeLeanSettings settings = default;
        settings.enabled = true;
        settings.angle = -2.5f;
        settings.blendSharpness = 10f;
        settings.inputDeadzone = 0.05f;
        return settings;
    }
}

[System.Serializable]
public struct HeadBobStateSettings
{
    [SerializeField] [Min(0f)] private float referenceSpeed;
    [SerializeField] [Min(0f)] private float intensityMultiplier;
    [SerializeField] [Min(0f)] private float frequency;

    public float ReferenceSpeed => referenceSpeed;
    public float IntensityMultiplier => intensityMultiplier;
    public float Frequency => frequency;

    public static HeadBobStateSettings CreateIdle()
    {
        HeadBobStateSettings settings = default;
        settings.referenceSpeed = 0f;
        settings.intensityMultiplier = 0.14f;
        settings.frequency = 0.85f;
        return settings;
    }

    public static HeadBobStateSettings CreateWalk()
    {
        HeadBobStateSettings settings = default;
        settings.referenceSpeed = 3f;
        settings.intensityMultiplier = 1f;
        settings.frequency = 1.55f;
        return settings;
    }

    public static HeadBobStateSettings CreateSprint()
    {
        HeadBobStateSettings settings = default;
        settings.referenceSpeed = 7f;
        settings.intensityMultiplier = 1.35f;
        settings.frequency = 2.05f;
        return settings;
    }

    public static HeadBobStateSettings CreateCrouch()
    {
        HeadBobStateSettings settings = default;
        settings.referenceSpeed = 1.5f;
        settings.intensityMultiplier = 0.55f;
        settings.frequency = 1.15f;
        return settings;
    }

    public static HeadBobStateSettings CreateAirborne()
    {
        HeadBobStateSettings settings = default;
        settings.referenceSpeed = 0f;
        settings.intensityMultiplier = 0.1f;
        settings.frequency = 0.75f;
        return settings;
    }
}

[System.Serializable]
public struct HeadBobLandingSettings
{
    [SerializeField] private bool enabled;
    [SerializeField] [Min(0f)] private float minLandingSpeed;
    [SerializeField] [Min(0f)] private float fullLandingSpeed;
    [SerializeField] [Min(0f)] private float settleDistance;
    [SerializeField] [Min(0f)] private float settlePitch;
    [SerializeField] [Min(0.01f)] private float recoverySharpness;

    public bool Enabled => enabled;
    public float MinLandingSpeed => minLandingSpeed;
    public float FullLandingSpeed => Mathf.Max(minLandingSpeed + 0.01f, fullLandingSpeed);
    public float SettleDistance => settleDistance;
    public float SettlePitch => settlePitch;
    public float RecoverySharpness => Mathf.Max(0.01f, recoverySharpness);

    public static HeadBobLandingSettings CreateDefault()
    {
        HeadBobLandingSettings settings = default;
        settings.enabled = true;
        settings.minLandingSpeed = 1.25f;
        settings.fullLandingSpeed = 9f;
        settings.settleDistance = 0.028f;
        settings.settlePitch = 1.4f;
        settings.recoverySharpness = 9f;
        return settings;
    }
}
