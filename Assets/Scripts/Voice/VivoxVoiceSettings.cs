using Unity.Services.Vivox;
using UnityEngine;

[CreateAssetMenu(fileName = "VivoxVoiceSettings", menuName = "Kings Worst Knights/Voice/Vivox Voice Settings")]
public sealed class VivoxVoiceSettings : ScriptableObject
{
    private const int DefaultAudibleDistance = 32;
    private const int DefaultConversationalDistance = 1;
    private const float DefaultFadeIntensity = 1f;
    private const float DefaultPositionUpdateInterval = 0.3f;

    [Header("Initial State")]
    [SerializeField] private VivoxVoiceMode initialMode = VivoxVoiceMode.Positional3D;

    [Header("3D Proximity")]
    [SerializeField] private bool useNativeVivoxPositionalAudio;
    [SerializeField] [Min(1)] private int audibleDistance = DefaultAudibleDistance;
    [SerializeField] [Min(0)] private int conversationalDistance = DefaultConversationalDistance;
    [SerializeField] [Min(0f)] private float audioFadeIntensity = DefaultFadeIntensity;
    [SerializeField] private AudioFadeModel audioFadeModel = AudioFadeModel.InverseByDistance;
    [SerializeField] [Min(0.05f)] private float positionUpdateInterval = DefaultPositionUpdateInterval;
    [SerializeField] private bool allowDirectionalPanning = true;

    public VivoxVoiceMode InitialMode => initialMode;
    public bool UseNativeVivoxPositionalAudio => useNativeVivoxPositionalAudio;
    public int AudibleDistance => Mathf.Max(1, audibleDistance);
    public int ConversationalDistance => Mathf.Clamp(conversationalDistance, 0, AudibleDistance);
    public float AudioFadeIntensity => Mathf.Max(0f, audioFadeIntensity);
    public AudioFadeModel FadeModel => audioFadeModel;
    public float PositionUpdateInterval => Mathf.Max(0.05f, positionUpdateInterval);
    public bool AllowDirectionalPanning => allowDirectionalPanning;

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
    }

    public static VivoxVoiceSettings CreateRuntimeDefaults()
    {
        VivoxVoiceSettings runtimeSettings = CreateInstance<VivoxVoiceSettings>();
        runtimeSettings.hideFlags = HideFlags.HideAndDontSave;
        return runtimeSettings;
    }
}
