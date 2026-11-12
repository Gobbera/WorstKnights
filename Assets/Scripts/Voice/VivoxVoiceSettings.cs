using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.Audio;

[System.Serializable]
public struct VoiceDistanceAttenuationPoint
{
    [SerializeField] [Min(0f)] private float distance;
    [SerializeField] [Range(-50f, 0f)] private float decibels;

    public float Distance => float.IsNaN(distance) || float.IsInfinity(distance)
        ? 0f : Mathf.Max(0f, distance);
    public float Decibels => float.IsNaN(decibels) || float.IsInfinity(decibels)
        ? -50f : Mathf.Clamp(decibels, -50f, 0f);

    public VoiceDistanceAttenuationPoint(float distance, float decibels)
    {
        this.distance = distance;
        this.decibels = decibels;
    }
}

[CreateAssetMenu(fileName = "VivoxVoiceSettings", menuName = "Kings Worst Knights/Voice/Vivox Voice Settings")]
public sealed class VivoxVoiceSettings : ScriptableObject
{
    private const int DefaultAudibleDistance = 50;
    private const int DefaultConversationalDistance = 1;
    private const float DefaultFadeIntensity = 1f;
    private const float DefaultPositionUpdateInterval = 0.3f;

    [Header("Initial State")]
    [SerializeField] private VivoxVoiceMode initialMode = VivoxVoiceMode.Positional3D;

    [Header("3D Proximity")]
    [SerializeField] private bool useNativeVivoxPositionalAudio;
    [SerializeField] [Min(1)] private int audibleDistance = DefaultAudibleDistance;
    [SerializeField] [Min(0)] private int conversationalDistance = DefaultConversationalDistance;
    [Tooltip("Native Vivox positional audio only. Simulated Unity output uses the attenuation tables below.")]
    [SerializeField] [Min(0f)] private float audioFadeIntensity = DefaultFadeIntensity;
    [Tooltip("Native Vivox positional audio only. Simulated Unity output uses the attenuation tables below.")]
    [SerializeField] private AudioFadeModel audioFadeModel = AudioFadeModel.InverseByDistance;
    [SerializeField] [Min(0.05f)] private float positionUpdateInterval = DefaultPositionUpdateInterval;
    [SerializeField] private bool allowDirectionalPanning = true;

    [Header("Dynamic Proximity")]
    [Tooltip("Quiet and normal speech use Normal Voice Distance. Only calibrated loud speech extends simulated 3D range to Audible Distance.")]
    [SerializeField] private bool dynamicProximity = true;
    [Tooltip("Range limit shared by quiet, normal and uncalibrated speech. Loud speech can extend it to Audible Distance.")]
    [SerializeField] [Min(0.1f)] private float normalVoiceDistance = 30f;

    [Header("Simulated Proximity Attenuation")]
    [Tooltip("Distance-to-decibel table used by quiet, normal and uncalibrated speech. Values between points are linearly interpolated.")]
    [SerializeField] private VoiceDistanceAttenuationPoint[] normalVoiceAttenuation = CreateNormalAttenuation();
    [Tooltip("Distance-to-decibel table used at maximum calibrated loudness. Values between points are linearly interpolated.")]
    [SerializeField] private VoiceDistanceAttenuationPoint[] loudVoiceAttenuation = CreateLoudAttenuation();

    [Header("Microphone Test")]
    [Tooltip("Local microphone-test playback volume. Does not change transmitted voice or another player's listening gain.")]
    [SerializeField] [Range(0f, 1f)] private float microphoneMonitorVolume = 0.35f;

    [Header("Unity Audio Output")]
    [Tooltip("Routes simulated proximity and 2D voice through Unity AudioSources so environment reverb can affect received speech. Native Vivox positional output remains direct.")]
    [SerializeField] private bool unityVoiceOutput = true;
    [SerializeField] [Range(0f, 1.1f)] private float environmentReverbSend = 1f;
    [SerializeField] private AudioMixerGroup voiceMixerGroup;

    public VivoxVoiceMode InitialMode => initialMode;
    public bool UseNativeVivoxPositionalAudio => useNativeVivoxPositionalAudio;
    public int AudibleDistance => Mathf.Max(1, audibleDistance);
    public int ConversationalDistance => Mathf.Clamp(conversationalDistance, 0, AudibleDistance);
    public float AudioFadeIntensity => Mathf.Max(0f, audioFadeIntensity);
    public AudioFadeModel FadeModel => audioFadeModel;
    public float PositionUpdateInterval => Mathf.Max(0.05f, positionUpdateInterval);
    public bool AllowDirectionalPanning => allowDirectionalPanning;
    public bool UnityVoiceOutput => unityVoiceOutput;
    public float EnvironmentReverbSend => Mathf.Clamp(environmentReverbSend, 0f, 1.1f);
    public AudioMixerGroup VoiceMixerGroup => voiceMixerGroup;
    public bool DynamicProximity => dynamicProximity;
    public float NormalVoiceDistance => SafeValue(normalVoiceDistance, 30f, 0.1f, AudibleDistance);
    public float MicrophoneMonitorVolume => SafeValue(microphoneMonitorVolume, 0.35f, 0f, 1f);

    private static float SafeValue(float value, float fallback, float minimum, float maximum)
        => Mathf.Clamp(float.IsNaN(value) || float.IsInfinity(value) ? fallback : value, minimum, maximum);

    public float EvaluateSimulatedAttenuationDecibels(float distance, float loudBlend)
    {
        float normal = EvaluateAttenuation(normalVoiceAttenuation, distance, NormalVoiceDistance);
        float loud = EvaluateAttenuation(loudVoiceAttenuation, distance, AudibleDistance);
        return Mathf.Lerp(normal, loud, Mathf.Clamp01(loudBlend));
    }

    private static float EvaluateAttenuation(VoiceDistanceAttenuationPoint[] points,
        float distance, float cutoffDistance)
    {
        float safeDistance = float.IsNaN(distance) || float.IsInfinity(distance)
            ? cutoffDistance : Mathf.Max(0f, distance);
        if (points == null || points.Length == 0)
            return Mathf.Lerp(0f, -50f, Mathf.InverseLerp(0f, cutoffDistance, safeDistance));

        bool hasLower = false, hasUpper = false;
        float lowerDistance = float.NegativeInfinity, upperDistance = float.PositiveInfinity;
        float lowerDecibels = 0f, upperDecibels = -50f;
        for (int i = 0; i < points.Length; i++)
        {
            float pointDistance = points[i].Distance;
            if (pointDistance <= safeDistance && pointDistance >= lowerDistance)
            {
                hasLower = true;
                lowerDistance = pointDistance;
                lowerDecibels = points[i].Decibels;
            }
            if (pointDistance >= safeDistance && pointDistance <= upperDistance)
            {
                hasUpper = true;
                upperDistance = pointDistance;
                upperDecibels = points[i].Decibels;
            }
        }

        if (!hasLower) return upperDecibels;
        if (hasUpper)
        {
            if (Mathf.Abs(upperDistance - lowerDistance) <= 0.0001f) return lowerDecibels;
            return Mathf.Lerp(lowerDecibels, upperDecibels,
                Mathf.InverseLerp(lowerDistance, upperDistance, safeDistance));
        }

        float safeCutoff = Mathf.Max(lowerDistance + 0.0001f, cutoffDistance);
        return Mathf.Lerp(lowerDecibels, -50f,
            Mathf.InverseLerp(lowerDistance, safeCutoff, safeDistance));
    }

    private static VoiceDistanceAttenuationPoint[] CreateNormalAttenuation() => new[]
    {
        new VoiceDistanceAttenuationPoint(1f, 0f),
        new VoiceDistanceAttenuationPoint(2f, -1f),
        new VoiceDistanceAttenuationPoint(3f, -3f),
        new VoiceDistanceAttenuationPoint(5f, -4f),
        new VoiceDistanceAttenuationPoint(7f, -5f),
        new VoiceDistanceAttenuationPoint(8f, -8f),
        new VoiceDistanceAttenuationPoint(10f, -10f),
        new VoiceDistanceAttenuationPoint(12f, -13f),
        new VoiceDistanceAttenuationPoint(13f, -15f),
        new VoiceDistanceAttenuationPoint(14f, -16f),
        new VoiceDistanceAttenuationPoint(15f, -20f),
        new VoiceDistanceAttenuationPoint(20f, -25f),
        new VoiceDistanceAttenuationPoint(25f, -50f)
    };

    private static VoiceDistanceAttenuationPoint[] CreateLoudAttenuation() => new[]
    {
        new VoiceDistanceAttenuationPoint(1f, 0f),
        new VoiceDistanceAttenuationPoint(2f, 0f),
        new VoiceDistanceAttenuationPoint(3f, -1f),
        new VoiceDistanceAttenuationPoint(5f, -1f),
        new VoiceDistanceAttenuationPoint(7f, -2f),
        new VoiceDistanceAttenuationPoint(8f, -3f),
        new VoiceDistanceAttenuationPoint(10f, -4f),
        new VoiceDistanceAttenuationPoint(12f, -4f),
        new VoiceDistanceAttenuationPoint(13f, -5f),
        new VoiceDistanceAttenuationPoint(14f, -5f),
        new VoiceDistanceAttenuationPoint(15f, -6f),
        new VoiceDistanceAttenuationPoint(20f, -9f),
        new VoiceDistanceAttenuationPoint(25f, -10f),
        new VoiceDistanceAttenuationPoint(30f, -13f),
        new VoiceDistanceAttenuationPoint(35f, -15f),
        new VoiceDistanceAttenuationPoint(40f, -20f),
        new VoiceDistanceAttenuationPoint(45f, -25f)
    };

    public Channel3DProperties CreateChannel3DProperties()
    {
        return new Channel3DProperties(
            AudibleDistance,
            ConversationalDistance,
            AudioFadeIntensity,
            audioFadeModel);
    }

    private void OnValidate()
    {
        audibleDistance = Mathf.Max(1, audibleDistance);
        conversationalDistance = Mathf.Clamp(conversationalDistance, 0, audibleDistance);
        audioFadeIntensity = Mathf.Max(0f, audioFadeIntensity);
        positionUpdateInterval = Mathf.Max(0.05f, positionUpdateInterval);
        environmentReverbSend = Mathf.Clamp(environmentReverbSend, 0f, 1.1f);
        normalVoiceDistance = NormalVoiceDistance;
        microphoneMonitorVolume = MicrophoneMonitorVolume;
    }

    public static VivoxVoiceSettings CreateRuntimeDefaults()
    {
        VivoxVoiceSettings runtimeSettings = CreateInstance<VivoxVoiceSettings>();
        runtimeSettings.hideFlags = HideFlags.HideAndDontSave;
        return runtimeSettings;
    }
}
