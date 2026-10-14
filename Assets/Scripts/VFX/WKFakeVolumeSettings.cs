using System;
using UnityEngine;

public enum WKFakeVolumeShape
{
    Existing,
    PointSphere,
    SpotCone,
    Cylinder,
    Cross,
    Box
}

public enum WKFakeVolumeColorMode
{
    Fixed,
    HueCycle
}

public enum WKFakeVolumeMotionMode
{
    None,
    Swing,
    Rotate,
    Drift
}

public enum WKFakeVolumeUpdateMode
{
    ScaledTime,
    UnscaledTime
}

[Serializable]
public sealed class WKFakeVolumeSettings
{
    [Header("Shape")]
    public WKFakeVolumeShape shape = WKFakeVolumeShape.Existing;
    [HideInInspector] public bool driveLightTypeFromShape = true;

    [Header("Visual")]
    [ColorUsage(true, true)]
    public Color color = new Color(1f, 0.84f, 0.55f, 1f);
    [Min(0f)] public float colorIntensity = 1f;
    [Range(0f, 1f)] public float opacity = 0.18f;
    [Min(0f)] public float power = 1f;
    [Range(0f, 1f)] public float edge = 0.4f;
    [Min(0f)] public float fadeDistance = 0.4f;
    [Min(0f)] public float extraSoftness;

    [Header("Noise")]
    public bool driveNoise = true;
    [Min(0f)] public float noiseAScale = 20f;
    [Min(0f)] public float noiseBScale = 15f;
    public Vector2 noiseASize = Vector2.one;
    public Vector2 noiseBSize = Vector2.one;
    [Tooltip("Main internal fog movement speed. Increase this to make the volume feel more like moving smoke.")]
    public float noiseASpeed = 0.1f;
    [Tooltip("Secondary internal fog movement speed for layered smoke breakup.")]
    public float noiseBSpeed = 0.2f;
    public bool extraNoise;
    [Min(0f)] public float extraNoiseDensity = 2f;
    public Vector2 extraNoiseSize = Vector2.one;

    [Header("Wind")]
    [Tooltip("Optional shader wind flow. It moves the fog/noise pattern; it is not a physics wind force.")]
    public bool useWind;
    public float windSpeed = 1f;
    [Range(0f, 1f)] public float windVisibility = 1f;

    [Header("Light")]
    public bool driveLights;
    public bool lightsEnabled;
    public bool syncLightColor = true;
    [ColorUsage(true, true)]
    public Color lightColor = new Color(1f, 0.84f, 0.55f, 1f);
    [Min(0f)] public float lightIntensity = 1.5f;
    [Min(0f)] public float lightRange = 10f;
    [Range(1f, 179f)] public float spotAngle = 75f;
    [Range(0f, 179f)] public float innerSpotAngle = 35f;

    [Header("Animation")]
    public WKFakeVolumeUpdateMode updateMode = WKFakeVolumeUpdateMode.ScaledTime;
    public bool randomizeSeed = true;
    public float seed;
    public bool enableFlicker;
    [Min(0.01f)] public float flickerSpeed = 5f;
    [Min(0.01f)] public float flickerResponse = 18f;
    [Min(0f)] public float flickerOpacityAmount = 0.08f;
    [Min(0f)] public float flickerPowerAmount;
    [Min(0f)] public float flickerLightIntensityAmount = 0.35f;

    [Header("Color Cycle")]
    public WKFakeVolumeColorMode colorMode = WKFakeVolumeColorMode.Fixed;
    [Min(0f)] public float colorCycleSpeed = 0.5f;
    [Range(0f, 1f)] public float colorSaturation = 1f;
    [Min(0f)] public float colorValue = 1f;

    [Header("Motion")]
    public WKFakeVolumeMotionMode motionMode = WKFakeVolumeMotionMode.None;
    [Min(0f)] public float motionSpeed = 1f;
    [Min(0f)] public float swingAngle = 15f;
    public Vector3 swingAxes = new Vector3(1f, 1.3f, 0f);
    public Vector3 rotateAxis = Vector3.up;
    [Min(0f)] public float rotateDegreesPerSecond = 30f;
    public Vector3 driftAmplitude = new Vector3(0.05f, 0.02f, 0.05f);

    public void Sanitize()
    {
        driveLightTypeFromShape = true;
        colorIntensity = Mathf.Max(0f, colorIntensity);
        opacity = Mathf.Clamp01(opacity);
        power = Mathf.Max(0f, power);
        edge = Mathf.Clamp01(edge);
        fadeDistance = Mathf.Max(0f, fadeDistance);
        extraSoftness = Mathf.Max(0f, extraSoftness);
        noiseAScale = Mathf.Max(0f, noiseAScale);
        noiseBScale = Mathf.Max(0f, noiseBScale);
        extraNoiseDensity = Mathf.Max(0f, extraNoiseDensity);
        windVisibility = Mathf.Clamp01(windVisibility);
        lightIntensity = Mathf.Max(0f, lightIntensity);
        lightRange = Mathf.Max(0f, lightRange);
        spotAngle = Mathf.Clamp(spotAngle, 1f, 179f);
        innerSpotAngle = Mathf.Clamp(innerSpotAngle, 0f, spotAngle);
        flickerSpeed = Mathf.Max(0.01f, flickerSpeed);
        flickerResponse = Mathf.Max(0.01f, flickerResponse);
        flickerOpacityAmount = Mathf.Max(0f, flickerOpacityAmount);
        flickerPowerAmount = Mathf.Max(0f, flickerPowerAmount);
        flickerLightIntensityAmount = Mathf.Max(0f, flickerLightIntensityAmount);
        colorCycleSpeed = Mathf.Max(0f, colorCycleSpeed);
        colorSaturation = Mathf.Clamp01(colorSaturation);
        colorValue = Mathf.Max(0f, colorValue);
        motionSpeed = Mathf.Max(0f, motionSpeed);
        swingAngle = Mathf.Max(0f, swingAngle);
        rotateDegreesPerSecond = Mathf.Max(0f, rotateDegreesPerSecond);
    }
}
