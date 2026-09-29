using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Audio/Reverb/Environment Reverb Manager")]
public sealed class EnvironmentReverbManager : MonoBehaviour
{
    private const string TechnicalZoneName = "Environment Reverb Zone";

    [Header("Default")]
    [SerializeField] private EnvironmentReverbProfile defaultProfile;
    [SerializeField] [Min(0f)] private float noProfileFadeOutTime = 0.75f;

    [Header("Listener")]
    [SerializeField] private bool followActiveAudioListener = true;
    [SerializeField] private bool requireActiveAudioListener = true;
    [SerializeField] [Min(0.05f)] private float audioListenerRefreshInterval = 0.5f;

    [Header("Technical Reverb Zone")]
    [SerializeField] [Min(0.01f)] private float technicalZoneMinDistance = 1f;
    [SerializeField] [Min(0.01f)] private float technicalZoneMaxDistance = 2f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging;

    private readonly List<IEnvironmentReverbSource> activeSources = new List<IEnvironmentReverbSource>();

    private EnvironmentReverbProfile targetProfile;
    private EnvironmentReverbSettings currentSettings = EnvironmentReverbSettings.Dry;
    private EnvironmentReverbSettings transitionStartSettings = EnvironmentReverbSettings.Dry;
    private EnvironmentReverbSettings transitionTargetSettings = EnvironmentReverbSettings.Dry;
    private AudioListener cachedAudioListener;
    private AudioReverbZone technicalZone;
    private float nextListenerRefreshTime;
    private float transitionElapsed;
    private float transitionDuration;
    private bool initialized;

    public EnvironmentReverbProfile DefaultProfile => defaultProfile;
    public EnvironmentReverbProfile CurrentProfile => targetProfile;

    private void Awake()
    {
        RefreshAudioListener(force: true);
        EnsureTechnicalZone();
        StartTransition(ResolveTargetProfile(), ResolveCurrentTransitionTime());
        initialized = true;
    }

    private void OnEnable()
    {
        RefreshAudioListener(force: true);
        EnsureTechnicalZone();
    }

    private void Update()
    {
        RefreshAudioListener(force: false);
        AudioReverbZone zone = EnsureTechnicalZone();
        bool canApply = CanApplyReverb();

        EnvironmentReverbProfile nextProfile = canApply ? ResolveTargetProfile() : null;
        if (nextProfile != targetProfile)
            StartTransition(nextProfile, ResolveCurrentTransitionTime());

        UpdateTransition(Time.deltaTime);

        if (zone != null)
        {
            zone.enabled = canApply;
            currentSettings.ApplyTo(zone, technicalZoneMinDistance, technicalZoneMaxDistance);
        }
    }

    private void OnDisable()
    {
        if (technicalZone != null)
            technicalZone.enabled = false;
    }

    public void EnterSource(IEnvironmentReverbSource source)
    {
        if (source == null)
            return;

        activeSources.Remove(source);
        activeSources.Add(source);

        if (verboseLogging)
            Debug.Log($"[EnvironmentReverbManager] Entrou em '{source.DisplayName}'.", SourceAsObject(source));
    }

    public void ExitSource(IEnvironmentReverbSource source)
    {
        if (source == null)
            return;

        activeSources.Remove(source);

        if (verboseLogging)
            Debug.Log($"[EnvironmentReverbManager] Saiu de '{source.DisplayName}'.", SourceAsObject(source));
    }

    private void StartTransition(EnvironmentReverbProfile nextProfile, float fallbackTransitionTime)
    {
        targetProfile = nextProfile;
        transitionStartSettings = initialized ? currentSettings : EnvironmentReverbSettings.Dry;
        transitionTargetSettings = nextProfile != null ? nextProfile.Settings : EnvironmentReverbSettings.Dry;
        transitionDuration = Mathf.Max(0f, fallbackTransitionTime);
        transitionElapsed = 0f;

        if (transitionDuration <= 0f)
            currentSettings = transitionTargetSettings;

        if (verboseLogging)
        {
            string profileName = nextProfile != null ? nextProfile.ProfileId : "Dry";
            Debug.Log($"[EnvironmentReverbManager] Reverb alvo: {profileName}.", this);
        }
    }

    private void UpdateTransition(float deltaTime)
    {
        if (transitionDuration <= 0f)
        {
            currentSettings = transitionTargetSettings;
            return;
        }

        transitionElapsed += Mathf.Max(0f, deltaTime);
        float t = Mathf.Clamp01(transitionElapsed / transitionDuration);
        currentSettings = EnvironmentReverbSettings.Lerp(transitionStartSettings, transitionTargetSettings, t);
    }

    private EnvironmentReverbProfile ResolveTargetProfile()
    {
        IEnvironmentReverbSource source = ResolveTopPrioritySource();
        if (source != null && source.Profile != null)
            return source.Profile;

        return defaultProfile;
    }

    private float ResolveCurrentTransitionTime()
    {
        IEnvironmentReverbSource source = ResolveTopPrioritySource();
        if (source != null && source.HasTransitionTimeOverride)
            return source.TransitionTime;

        EnvironmentReverbProfile profile = source != null ? source.Profile : defaultProfile;
        if (profile != null)
            return profile.TransitionTime;

        return noProfileFadeOutTime;
    }

    private IEnvironmentReverbSource ResolveTopPrioritySource()
    {
        IEnvironmentReverbSource bestSource = null;
        int bestPriority = int.MinValue;

        for (int i = activeSources.Count - 1; i >= 0; i--)
        {
            IEnvironmentReverbSource source = activeSources[i];
            if (source == null || !source.IsReverbSourceActive || source.Profile == null)
            {
                activeSources.RemoveAt(i);
                continue;
            }

            if (bestSource != null && source.Priority <= bestPriority)
                continue;

            bestSource = source;
            bestPriority = source.Priority;
        }

        return bestSource;
    }

    private bool CanApplyReverb()
    {
        if (!requireActiveAudioListener)
            return true;

        return cachedAudioListener != null
            && cachedAudioListener.enabled
            && cachedAudioListener.gameObject.activeInHierarchy;
    }

    private AudioReverbZone EnsureTechnicalZone()
    {
        Transform parent = ResolveTechnicalZoneParent();
        if (parent == null)
            return null;

        if (technicalZone == null)
        {
            Transform existing = parent.Find(TechnicalZoneName);
            if (existing != null)
                technicalZone = existing.GetComponent<AudioReverbZone>();

            if (technicalZone == null)
            {
                GameObject zoneObject = new GameObject(TechnicalZoneName);
                zoneObject.transform.SetParent(parent, false);
                technicalZone = zoneObject.AddComponent<AudioReverbZone>();
            }
        }

        Transform zoneTransform = technicalZone.transform;
        if (zoneTransform.parent != parent)
            zoneTransform.SetParent(parent, false);

        zoneTransform.localPosition = Vector3.zero;
        zoneTransform.localRotation = Quaternion.identity;
        technicalZone.minDistance = Mathf.Max(0.01f, technicalZoneMinDistance);
        technicalZone.maxDistance = Mathf.Max(technicalZone.minDistance, technicalZoneMaxDistance);
        return technicalZone;
    }

    private Transform ResolveTechnicalZoneParent()
    {
        if (followActiveAudioListener && cachedAudioListener != null)
            return cachedAudioListener.transform;

        return transform;
    }

    private void RefreshAudioListener(bool force)
    {
        if (!force && Time.unscaledTime < nextListenerRefreshTime)
            return;

        nextListenerRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, audioListenerRefreshInterval);
        if (cachedAudioListener != null
            && cachedAudioListener.enabled
            && cachedAudioListener.gameObject.activeInHierarchy)
        {
            return;
        }

        cachedAudioListener = null;
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null || !listener.enabled || !listener.gameObject.activeInHierarchy)
                continue;

            cachedAudioListener = listener;
            return;
        }
    }

    private void OnValidate()
    {
        noProfileFadeOutTime = Mathf.Max(0f, noProfileFadeOutTime);
        audioListenerRefreshInterval = Mathf.Max(0.05f, audioListenerRefreshInterval);
        technicalZoneMinDistance = Mathf.Max(0.01f, technicalZoneMinDistance);
        technicalZoneMaxDistance = Mathf.Max(technicalZoneMinDistance, technicalZoneMaxDistance);
    }

    private static Object SourceAsObject(IEnvironmentReverbSource source)
    {
        return source as Object;
    }
}
