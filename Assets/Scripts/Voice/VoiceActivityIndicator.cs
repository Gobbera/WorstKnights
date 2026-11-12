using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class VoiceActivityIndicator : MonoBehaviour
{
    private const string PrefabResourcePath = "UI/VoiceActivityHUD";

    [Header("View")]
    [SerializeField] private MicrophoneActivityGraphic microphoneIcon;

    [Header("Appearance")]
    [SerializeField] private Color idleColor = Color.white;
    [SerializeField] private Color speakingColor = new Color(0.45f, 1f, 0.65f, 1f);
    [SerializeField] private Color mutedColor = new Color(1f, 0.45f, 0.4f, 1f);
    [SerializeField] private Color disconnectedColor = new Color(0.65f, 0.68f, 0.72f, 1f);
    [SerializeField] [Range(0f, 1f)] private float idleOpacity = 0.3f;
    [SerializeField] [Range(0f, 1f)] private float unavailableOpacity = 0.55f;

    [Header("Smoothing")]
    [Tooltip("Visual fade duration in unscaled seconds. Zero applies changes immediately.")]
    [SerializeField] [Min(0f)] private float transitionTime = 0.08f;
    [Tooltip("Short visual hold after speech detection ends. Mute and disconnect bypass the hold.")]
    [SerializeField] [Min(0f)] private float speechReleaseHold = 0.12f;

    private float remainingSpeechHold;
    private bool initialized;

    public static VoiceActivityIndicator EnsureForGameplayScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || GameplaySceneRoot.TryGetActiveSceneRoot() == null)
            return null;

        var existing = FindObjectsByType<VoiceActivityIndicator>(FindObjectsInactive.Include);
        for (int i = 0; i < existing.Length; i++)
            if (existing[i] != null && existing[i].gameObject.scene == scene)
                return existing[i];

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"VoiceActivityIndicator: prefab 'Resources/{PrefabResourcePath}' not found.");
            return null;
        }

        GameObject instance = Instantiate(prefab);
        instance.name = "Voice Activity HUD";
        SceneManager.MoveGameObjectToScene(instance, scene);
        return instance.GetComponent<VoiceActivityIndicator>();
    }

    private void OnEnable()
    {
        remainingSpeechHold = 0f;
        initialized = false;
        if (microphoneIcon == null)
            microphoneIcon = GetComponentInChildren<MicrophoneActivityGraphic>(true);
        Present(ReadLocalActivity(), 0f);
    }

    private void Update()
    {
        Present(ReadLocalActivity(), Time.unscaledDeltaTime);
    }

    private static VoiceActivitySnapshot ReadLocalActivity()
    {
        VivoxVoiceManager manager = VivoxVoiceManager.Instance;
        return manager != null && manager.isActiveAndEnabled ? manager.LocalVoiceActivity : default;
    }

    private void Present(VoiceActivitySnapshot activity, float deltaTime)
    {
        if (microphoneIcon == null) return;
        float dt = Mathf.Max(0f, deltaTime);
        bool idle = activity.State == VoiceActivityState.Idle;
        if (activity.IsSpeaking)
            remainingSpeechHold = Mathf.Max(0f, speechReleaseHold);
        else
            remainingSpeechHold = idle ? Mathf.Max(0f, remainingSpeechHold - dt) : 0f;

        bool speaking = activity.IsSpeaking || (idle && remainingSpeechHold > 0f);
        Color target;
        switch (activity.State)
        {
            case VoiceActivityState.Speaking:
            case VoiceActivityState.Idle:
                target = speaking ? speakingColor : idleColor;
                target.a *= speaking ? 1f : idleOpacity;
                break;
            case VoiceActivityState.Muted:
                target = mutedColor;
                break;
            default:
                target = disconnectedColor;
                target.a *= unavailableOpacity;
                break;
        }

        microphoneIcon.CrossedOut = !idle && !activity.IsSpeaking;
        float blend = !initialized || transitionTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / transitionTime);
        microphoneIcon.color = Color.Lerp(microphoneIcon.color, target, blend);
        initialized = true;
    }
}
