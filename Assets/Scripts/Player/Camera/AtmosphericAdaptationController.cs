using System;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class AtmosphericAdaptationController : MonoBehaviour
{
    private const string FirstPersonCameraName = "FP_Camera";
    private const string AssistLightName = "Visibility Assist Light";
    private const float VolumePriority = 20f;
    private const float LightEndpointPadding = 0.15f;
    private const float PlayerSearchInterval = 0.5f;
    private const float LightRefreshInterval = 1f;

    public enum PreviewMode
    {
        Automatic,
        ForceDark,
        ForceLight,
        ManualBlend
    }

    [Serializable]
    private struct LookSettings
    {
        public Color colorFilter;
        [Range(-30f, 30f)] public float saturationOffset;
        [Range(-20f, 20f)] public float contrastOffset;
        [Range(-0.25f, 0.25f)] public float exposureOffset;
    }

    [Header("Profiles")]
    [SerializeField] private LookSettings darkProfile = new LookSettings
    {
        colorFilter = new Color(0.95f, 0.95f, 1f, 1f),
        saturationOffset = -18f,
        contrastOffset = 12f,
        exposureOffset = -0.25f
    };

    [SerializeField] private LookSettings lightProfile = new LookSettings
    {
        colorFilter = new Color(1f, 0.97f, 0.92f, 1f),
        saturationOffset = 6f,
        contrastOffset = -3f,
        exposureOffset = 0.2f
    };

    [Header("Adaptation")]
    [SerializeField] [Range(0f, 1f)] private float globalIntensity = 0.9f;
    [SerializeField] [Min(0.01f)] private float darkToLightSeconds = 1.5f;
    [SerializeField] [Min(0.01f)] private float lightToDarkSeconds = 2.2f;
    [SerializeField] private bool enableExposure = true;

    [Header("Light Detection")]
    [SerializeField] [Min(0.05f)] private float sampleInterval = 0.25f;
    [SerializeField] [Min(0.5f)] private float maxLightDistance = 12f;
    [SerializeField] [Min(0.01f)] private float intensityForFullLight = 0.25f;
    [SerializeField] private LayerMask occlusionMask = (1 << 0) | (1 << 3) | (1 << 7) | (1 << 9) | (1 << 12);

    [Header("Play Mode Preview")]
    [SerializeField] private PreviewMode previewMode;
    [SerializeField] [Range(0f, 1f)] private float manualBlend;
    [SerializeField] [Range(0f, 1f)] private float detectedLightInfluence;
    [SerializeField] [Range(0f, 1f)] private float currentBlend;
    [SerializeField] [Range(0f, 1f)] private float currentVolumeWeight;
    [SerializeField] private string runtimeStatus = "Waiting for local player";

    private readonly RaycastHit[] occlusionHits = new RaycastHit[24];
    private Light[] cachedLights = Array.Empty<Light>();
    private PlayerSetup localPlayer;
    private Camera playerCamera;
    private Volume runtimeVolume;
    private VolumeProfile runtimeProfile;
    private ColorAdjustments colorAdjustments;
    private float baseExposure;
    private float baseContrast;
    private float baseSaturation;
    private Color baseColorFilter = Color.white;
    private float nextPlayerSearchTime;
    private float nextLightRefreshTime;
    private float nextSampleTime;

    public string RuntimeStatus => runtimeStatus;

    private void OnEnable()
    {
        EnsureColorFilters();
        runtimeStatus = "Waiting for local player";
        nextPlayerSearchTime = 0f;
        nextLightRefreshTime = 0f;
        nextSampleTime = 0f;
    }

    private void OnDisable()
    {
        if (runtimeVolume != null)
            runtimeVolume.weight = 0f;

        currentVolumeWeight = 0f;
        runtimeStatus = "Disabled";
    }

    private void OnDestroy()
    {
        ReleaseRuntimeVolume();
    }

    private void OnValidate()
    {
        EnsureColorFilters();
        globalIntensity = Mathf.Clamp01(globalIntensity);
        darkToLightSeconds = Mathf.Max(0.01f, darkToLightSeconds);
        lightToDarkSeconds = Mathf.Max(0.01f, lightToDarkSeconds);
        sampleInterval = Mathf.Max(0.05f, sampleInterval);
        maxLightDistance = Mathf.Max(0.5f, maxLightDistance);
        intensityForFullLight = Mathf.Max(0.01f, intensityForFullLight);
    }

    private void EnsureColorFilters()
    {
        if (darkProfile.colorFilter == default)
            darkProfile.colorFilter = new Color(0.95f, 0.95f, 1f, 1f);
        if (lightProfile.colorFilter == default)
            lightProfile.colorFilter = new Color(1f, 0.97f, 0.92f, 1f);
    }

    private void Update()
    {
        if (!TryBindLocalPlayer())
            return;

        if (Time.time >= nextSampleTime)
        {
            nextSampleTime = Time.time + sampleInterval;
            detectedLightInfluence = SampleLightInfluence(playerCamera.transform.position);
        }

        switch (previewMode)
        {
            case PreviewMode.ForceDark:
                currentBlend = 0f;
                break;
            case PreviewMode.ForceLight:
                currentBlend = 1f;
                break;
            case PreviewMode.ManualBlend:
                currentBlend = manualBlend;
                break;
            default:
                float duration = detectedLightInfluence > currentBlend ? darkToLightSeconds : lightToDarkSeconds;
                currentBlend = Mathf.MoveTowards(currentBlend, detectedLightInfluence, Time.deltaTime / duration);
                break;
        }

        ApplyLook();
    }

    private bool TryBindLocalPlayer()
    {
        if (localPlayer != null && playerCamera != null && runtimeVolume != null && IsLocalPlayer(localPlayer))
            return true;

        if (runtimeVolume != null)
            runtimeVolume.weight = 0f;

        if (Time.time < nextPlayerSearchTime)
            return false;

        nextPlayerSearchTime = Time.time + PlayerSearchInterval;
        if (runtimeVolume != null || runtimeProfile != null)
            ReleaseRuntimeVolume();

        PlayerSetup[] players = FindObjectsByType<PlayerSetup>(FindObjectsInactive.Exclude);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerSetup candidate = players[i];
            if (!IsLocalPlayer(candidate))
                continue;

            Camera camera = FindFirstPersonCamera(candidate);
            if (camera == null)
                continue;

            if (localPlayer != candidate || playerCamera != camera || runtimeVolume == null)
                BindPlayer(candidate, camera);

            return runtimeVolume != null;
        }

        return false;
    }

    private static bool IsLocalPlayer(PlayerSetup player)
    {
        PhotonView view = player != null ? player.GetComponent<PhotonView>() : null;
        return view != null && (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || view.IsMine);
    }

    private static Camera FindFirstPersonCamera(PlayerSetup player)
    {
        Camera[] cameras = player.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].name == FirstPersonCameraName)
                return cameras[i];
        }

        return null;
    }

    private void BindPlayer(PlayerSetup player, Camera camera)
    {
        ReleaseRuntimeVolume();
        localPlayer = player;
        playerCamera = camera;
        ReadBaseLook(camera.GetComponent<Volume>());

        GameObject volumeObject = new GameObject("AtmosphericAdaptationVolume");
        volumeObject.SetActive(false);
        volumeObject.layer = camera.gameObject.layer;
        volumeObject.transform.SetParent(camera.transform, false);
        runtimeVolume = volumeObject.AddComponent<Volume>();
        runtimeVolume.isGlobal = true;
        runtimeVolume.priority = VolumePriority;
        runtimeVolume.weight = 0f;

        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.name = "Atmospheric Adaptation (Runtime)";
        colorAdjustments = runtimeProfile.Add<ColorAdjustments>(false);
        colorAdjustments.contrast.overrideState = true;
        colorAdjustments.saturation.overrideState = true;
        colorAdjustments.colorFilter.overrideState = true;
        runtimeVolume.sharedProfile = runtimeProfile;
        volumeObject.SetActive(true);
        ValidateCameraVolumeSettings(camera, volumeObject.layer);
        nextSampleTime = 0f;
    }

    private void ValidateCameraVolumeSettings(Camera camera, int volumeLayer)
    {
        UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null || !cameraData.renderPostProcessing)
        {
            runtimeStatus = "FP_Camera post-processing is disabled";
            Debug.LogWarning("[AtmosphericAdaptation] Enable Post Processing on the local FP_Camera.", camera);
        }
        else if ((cameraData.volumeLayerMask.value & (1 << volumeLayer)) == 0)
        {
            runtimeStatus = "FP_Camera Volume Layer Mask excludes the runtime Volume";
            Debug.LogWarning("[AtmosphericAdaptation] Include the FP_Camera layer in its Volume Layer Mask.", camera);
        }
        else
        {
            runtimeStatus = "Active on local FP_Camera";
        }
    }

    private void ReadBaseLook(Volume baseVolume)
    {
        baseExposure = 0f;
        baseContrast = 0f;
        baseSaturation = 0f;
        baseColorFilter = Color.white;

        VolumeProfile profile = baseVolume != null ? baseVolume.sharedProfile : null;
        if (profile == null)
            return;

        if (profile.TryGet(out ColorAdjustments baseColor) && baseColor.active)
        {
            if (baseColor.postExposure.overrideState)
                baseExposure = baseColor.postExposure.value;
            if (baseColor.contrast.overrideState)
                baseContrast = baseColor.contrast.value;
            if (baseColor.saturation.overrideState)
                baseSaturation = baseColor.saturation.value;
            if (baseColor.colorFilter.overrideState)
                baseColorFilter = baseColor.colorFilter.value;
        }
    }

    private void ApplyLook()
    {
        if (runtimeVolume == null || colorAdjustments == null)
            return;

        runtimeVolume.weight = Mathf.MoveTowards(runtimeVolume.weight, globalIntensity, Time.deltaTime);
        currentVolumeWeight = runtimeVolume.weight;
        colorAdjustments.postExposure.overrideState = enableExposure;
        if (enableExposure)
        {
            colorAdjustments.postExposure.value = baseExposure + Mathf.Lerp(
                darkProfile.exposureOffset, lightProfile.exposureOffset, currentBlend);
        }

        colorAdjustments.contrast.value = baseContrast + Mathf.Lerp(
            darkProfile.contrastOffset, lightProfile.contrastOffset, currentBlend);
        colorAdjustments.saturation.value = baseSaturation + Mathf.Lerp(
            darkProfile.saturationOffset, lightProfile.saturationOffset, currentBlend);
        Color filter = Color.Lerp(darkProfile.colorFilter, lightProfile.colorFilter, currentBlend);
        colorAdjustments.colorFilter.value = new Color(
            baseColorFilter.r * filter.r,
            baseColorFilter.g * filter.g,
            baseColorFilter.b * filter.b,
            baseColorFilter.a);
    }

    private float SampleLightInfluence(Vector3 position)
    {
        if (Time.time >= nextLightRefreshTime)
        {
            cachedLights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            nextLightRefreshTime = Time.time + LightRefreshInterval;
        }

        float total = 0f;
        for (int i = 0; i < cachedLights.Length; i++)
        {
            Light light = cachedLights[i];
            if (light == null || !light.isActiveAndEnabled || light.name == AssistLightName ||
                (light.type != LightType.Point && light.type != LightType.Spot) || light.intensity <= 0f)
            {
                continue;
            }

            Vector3 towardLight = light.transform.position - position;
            float distance = towardLight.magnitude;
            float reach = Mathf.Min(light.range, maxLightDistance);
            if (distance >= reach || reach <= 0.01f)
                continue;

            float spotWeight = 1f;
            if (light.type == LightType.Spot)
            {
                float angle = Vector3.Angle(light.transform.forward, -towardLight);
                float outerAngle = light.spotAngle * 0.5f;
                if (angle >= outerAngle)
                    continue;

                float innerAngle = outerAngle * 0.7f;
                spotWeight = Mathf.Clamp01((outerAngle - angle) / Mathf.Max(0.01f, outerAngle - innerAngle));
            }

            if (IsOccluded(light, position, towardLight, distance))
                continue;

            float distanceWeight = 1f - distance / reach;
            float contribution = distanceWeight * distanceWeight * light.intensity * spotWeight / intensityForFullLight;

            total += contribution;
            if (total >= 1f)
                return 1f;
        }

        return Mathf.Clamp01(total);
    }

    private bool IsOccluded(Light light, Vector3 position, Vector3 towardLight, float distance)
    {
        float rayLength = distance - LightEndpointPadding;
        if (rayLength <= 0f || occlusionMask.value == 0)
            return false;

        int hitCount = Physics.RaycastNonAlloc(
            position,
            towardLight / distance,
            occlusionHits,
            rayLength,
            occlusionMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount == occlusionHits.Length)
            return true;

        TorchFlameController sourceTorch = light.GetComponentInParent<TorchFlameController>();
        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = occlusionHits[i].transform;
            if (hitTransform == null || (localPlayer != null && hitTransform.IsChildOf(localPlayer.transform)) ||
                (sourceTorch != null && hitTransform.IsChildOf(sourceTorch.transform)))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void ReleaseRuntimeVolume()
    {
        if (runtimeVolume != null)
        {
            runtimeVolume.weight = 0f;
            Destroy(runtimeVolume.gameObject);
        }

        if (runtimeProfile != null)
            Destroy(runtimeProfile);

        runtimeVolume = null;
        runtimeProfile = null;
        colorAdjustments = null;
        currentVolumeWeight = 0f;
        playerCamera = null;
        localPlayer = null;
    }
}
