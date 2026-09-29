using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class BoxReverbZoneManager : MonoBehaviour
{
    private const string ManagerName = "Box Reverb Zone Manager";
    private const string TechnicalZoneName = "Box Reverb Technical Zone";
    private const float ListenerRefreshInterval = 0.5f;
    private const float TechnicalZoneMinDistance = 1f;
    private const float TechnicalZoneMaxDistance = 2f;
    private const float ExitTransitionTime = 0.75f;

    private static readonly List<BoxReverbZoneManager> Managers = new List<BoxReverbZoneManager>();

    private readonly List<BoxReverbZone> activeZones = new List<BoxReverbZone>();

    private BoxReverbZone targetZone;
    private AudioReverbPreset targetPreset = AudioReverbPreset.Off;
    private EnvironmentReverbSettings currentSettings = EnvironmentReverbSettings.Dry;
    private EnvironmentReverbSettings transitionStartSettings = EnvironmentReverbSettings.Dry;
    private EnvironmentReverbSettings transitionTargetSettings = EnvironmentReverbSettings.Dry;
    private AudioListener cachedAudioListener;
    private AudioReverbZone technicalZone;
    private float nextListenerRefreshTime;
    private float targetWetScale;
    private float targetTransitionTime;
    private float transitionElapsed;
    private float transitionDuration;
    private bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Managers.Clear();
    }

    public static BoxReverbZoneManager GetOrCreateForScene(GameObject context)
    {
        Scene scene = context != null ? context.scene : SceneManager.GetActiveScene();

        for (int i = Managers.Count - 1; i >= 0; i--)
        {
            BoxReverbZoneManager manager = Managers[i];
            if (manager == null)
            {
                Managers.RemoveAt(i);
                continue;
            }

            if (manager.gameObject.scene == scene)
                return manager;
        }

        BoxReverbZoneManager[] existingManagers = FindObjectsByType<BoxReverbZoneManager>(FindObjectsInactive.Include);
        for (int i = 0; i < existingManagers.Length; i++)
        {
            BoxReverbZoneManager manager = existingManagers[i];
            if (manager == null || manager.gameObject.scene != scene)
                continue;

            if (!Managers.Contains(manager))
                Managers.Add(manager);

            return manager;
        }

        GameObject managerObject = new GameObject(ManagerName);
        if (scene.IsValid())
            SceneManager.MoveGameObjectToScene(managerObject, scene);

        BoxReverbZoneManager createdManager = managerObject.AddComponent<BoxReverbZoneManager>();
        if (!Managers.Contains(createdManager))
            Managers.Add(createdManager);

        return createdManager;
    }

    private void Awake()
    {
        if (!Managers.Contains(this))
            Managers.Add(this);

        RefreshAudioListener(force: true);
        EnsureTechnicalZone();
        StartTransition(null, ExitTransitionTime);
        initialized = true;
    }

    private void Update()
    {
        RefreshAudioListener(force: false);

        BoxReverbZone nextZone = ResolveTopPriorityZone();
        AudioReverbPreset nextPreset = nextZone != null ? nextZone.Preset : AudioReverbPreset.Off;
        float nextWetScale = nextZone != null ? nextZone.WetScale : 0f;
        float nextTransitionTime = nextZone != null ? nextZone.TransitionTime : ExitTransitionTime;

        if (nextZone != targetZone
            || nextPreset != targetPreset
            || !Mathf.Approximately(nextWetScale, targetWetScale)
            || !Mathf.Approximately(nextTransitionTime, targetTransitionTime))
        {
            StartTransition(nextZone, nextTransitionTime);
        }

        UpdateTransition(Time.deltaTime);
        ApplyCurrentSettings();
    }

    private void OnDestroy()
    {
        Managers.Remove(this);

        if (technicalZone != null)
            Destroy(technicalZone.gameObject);
    }

    public void EnterZone(BoxReverbZone zone)
    {
        if (zone == null)
            return;

        activeZones.Remove(zone);
        activeZones.Add(zone);
    }

    public void ExitZone(BoxReverbZone zone)
    {
        if (zone == null)
            return;

        activeZones.Remove(zone);
    }

    private void StartTransition(BoxReverbZone nextZone, float transitionTime)
    {
        targetZone = nextZone;
        targetPreset = nextZone != null ? nextZone.Preset : AudioReverbPreset.Off;
        targetWetScale = nextZone != null ? Mathf.Clamp01(nextZone.WetScale) : 0f;
        targetTransitionTime = Mathf.Max(0f, transitionTime);
        transitionStartSettings = initialized ? currentSettings : EnvironmentReverbSettings.Dry;
        transitionTargetSettings = ResolveTargetSettings(nextZone);
        transitionDuration = targetTransitionTime;
        transitionElapsed = 0f;

        if (transitionDuration <= 0f)
            currentSettings = transitionTargetSettings;
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

    private void ApplyCurrentSettings()
    {
        AudioReverbZone zone = EnsureTechnicalZone();
        if (zone == null)
            return;

        bool hasListener = cachedAudioListener != null
            && cachedAudioListener.enabled
            && cachedAudioListener.gameObject.activeInHierarchy;

        zone.enabled = hasListener;
        if (hasListener)
            currentSettings.ApplyTo(zone, TechnicalZoneMinDistance, TechnicalZoneMaxDistance);
    }

    private EnvironmentReverbSettings ResolveTargetSettings(BoxReverbZone zone)
    {
        if (zone == null || (int)zone.Preset == 0 || zone.WetScale <= 0f)
            return EnvironmentReverbSettings.Dry;

        return EnvironmentReverbSettings.FromPreset(zone.Preset).WithWetScale(zone.WetScale);
    }

    private BoxReverbZone ResolveTopPriorityZone()
    {
        BoxReverbZone bestZone = null;
        int bestPriority = int.MinValue;

        for (int i = activeZones.Count - 1; i >= 0; i--)
        {
            BoxReverbZone zone = activeZones[i];
            if (zone == null || !zone.IsZoneActive)
            {
                activeZones.RemoveAt(i);
                continue;
            }

            if (bestZone != null && zone.Priority <= bestPriority)
                continue;

            bestZone = zone;
            bestPriority = zone.Priority;
        }

        return bestZone;
    }

    private AudioReverbZone EnsureTechnicalZone()
    {
        Transform parent = cachedAudioListener != null ? cachedAudioListener.transform : transform;
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
        technicalZone.minDistance = TechnicalZoneMinDistance;
        technicalZone.maxDistance = TechnicalZoneMaxDistance;
        return technicalZone;
    }

    private void RefreshAudioListener(bool force)
    {
        if (!force && Time.unscaledTime < nextListenerRefreshTime)
            return;

        nextListenerRefreshTime = Time.unscaledTime + ListenerRefreshInterval;
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
}
