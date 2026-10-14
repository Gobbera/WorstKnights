using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[DefaultExecutionOrder(90)]
public sealed class PlayerPostProcessingFeedbackController : MonoBehaviour
{
    private const float MinDamageFlashIntensity = 0.04f;
    private const float LowStaminaPreCriticalWeight = 0.15f;
    private const float FeedbackVolumePriority = 50f;
    private const string FeedbackVolumeName = "PostProcessingFeedbackVolume";

    [Header("References")]
    [SerializeField] private Volume baseVolume;
    [SerializeField] private Volume feedbackVolume;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerMovement playerMovement;

    [Header("Damage Flash")]
    [SerializeField] private Color damageColor = new Color(0.75f, 0.02f, 0.02f, 1f);
    [SerializeField] [Min(0.1f)] private float damageForMaxFlash = 45f;
    [SerializeField] [Range(0f, 0.5f)] private float maxDamageFlashIntensity = 0.22f;
    [SerializeField] [Min(0f)] private float damageFlashFadeSpeed = 1.15f;

    [Header("Low Health Pulse")]
    [SerializeField] [Range(0.01f, 1f)] private float lowHealthThreshold = 0.3f;
    [SerializeField] private Color lowHealthColor = new Color(0.75f, 0.02f, 0.02f, 1f);
    [SerializeField] [Range(0f, 1f)] private float lowHealthPulseTargetIntensity = 0.6f;
    [SerializeField] [Range(0f, 1f)] private float lowHealthPulseTargetSmoothness = 0.6f;
    [SerializeField] [Min(0.1f)] private float lowHealthPulseSpeed = 1.15f;

    [Header("Low Health Slow Pulse")]
    [SerializeField] [Range(0f, 1f)] private float lowHealthSlowPulseTargetIntensity = 0.72f;
    [SerializeField] [Range(0f, 1f)] private float lowHealthSlowPulseTargetSmoothness = 0.72f;
    [SerializeField] [Min(0.01f)] private float lowHealthSlowPulseSpeed = 0.18f;

    [Header("Low Stamina Vignette")]
    [SerializeField] [Range(0.01f, 1f)] private float lowStaminaStartThreshold = 0.5f;
    [SerializeField] [Range(0f, 1f)] private float lowStaminaCriticalThreshold = 0.25f;
    [SerializeField] private Color lowStaminaColor = Color.black;
    [SerializeField] [Range(0f, 1f)] private float lowStaminaTargetIntensity = 0.62f;
    [SerializeField] [Range(0f, 1f)] private float lowStaminaTargetSmoothness = 0.68f;

    private Vignette feedbackVignette;
    private Vignette baseVignette;
    private VolumeProfile createdFeedbackProfile;
    private PhotonView ownerPhotonView;
    private PlayerHealth subscribedHealth;
    private float damageFlashIntensity;
    private bool warnedMissingBaseVolume;
    private bool warnedMissingFeedbackVolume;

    private void Reset()
    {
        baseVolume = GetComponent<Volume>();
        feedbackVolume = ResolveFeedbackVolumeFromChildren();
        playerHealth = GetComponentInParent<PlayerHealth>();
        playerMovement = GetComponentInParent<PlayerMovement>();
        ownerPhotonView = GetComponentInParent<PhotonView>();
    }

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        ClearFeedbackVolume();

        if (!HasLocalAuthority())
            return;

        if (PrepareFeedbackVignette())
            SubscribeToPlayerHealth();
    }

    private void OnDisable()
    {
        UnsubscribeFromPlayerHealth();
        damageFlashIntensity = 0f;
        ClearFeedbackVolume();
    }

    private void OnDestroy()
    {
        if (createdFeedbackProfile != null)
            Destroy(createdFeedbackProfile);
    }

    private void LateUpdate()
    {
        if (!HasLocalAuthority())
        {
            ClearFeedbackVolume();
            return;
        }

        if (!PrepareFeedbackVignette())
            return;

        float deltaTime = Time.deltaTime;
        damageFlashIntensity = Mathf.MoveTowards(
            damageFlashIntensity,
            0f,
            damageFlashFadeSpeed * deltaTime);

        float damageFlashWeight = ResolveDamageFlashWeight();
        float lowHealthPulseWeight = CalculateLowHealthPulseWeight();
        float lowHealthSlowPulseWeight = CalculateLowHealthSlowPulseWeight();
        float lowStaminaWeight = CalculateLowStaminaWeight();
        float lowHealthWeight = Mathf.Clamp01(lowHealthPulseWeight + lowHealthSlowPulseWeight);
        float feedbackWeight = Mathf.Clamp01(damageFlashWeight + lowHealthWeight + lowStaminaWeight);

        if (feedbackWeight <= 0.001f)
        {
            ClearFeedbackVolume();
            return;
        }

        ApplyFeedbackVignette(damageFlashWeight, lowHealthPulseWeight, lowHealthSlowPulseWeight, lowStaminaWeight, feedbackWeight);
    }

    private void OnValidate()
    {
        damageForMaxFlash = Mathf.Max(0.1f, damageForMaxFlash);
        maxDamageFlashIntensity = Mathf.Max(0f, maxDamageFlashIntensity);
        damageFlashFadeSpeed = Mathf.Max(0f, damageFlashFadeSpeed);
        lowHealthThreshold = Mathf.Clamp(lowHealthThreshold, 0.01f, 1f);
        lowHealthPulseTargetIntensity = Mathf.Clamp01(lowHealthPulseTargetIntensity);
        lowHealthPulseTargetSmoothness = Mathf.Clamp01(lowHealthPulseTargetSmoothness);
        lowHealthPulseSpeed = Mathf.Max(0.1f, lowHealthPulseSpeed);
        lowHealthSlowPulseTargetIntensity = Mathf.Clamp01(lowHealthSlowPulseTargetIntensity);
        lowHealthSlowPulseTargetSmoothness = Mathf.Clamp01(lowHealthSlowPulseTargetSmoothness);
        lowHealthSlowPulseSpeed = Mathf.Max(0.01f, lowHealthSlowPulseSpeed);
        lowStaminaStartThreshold = Mathf.Clamp(lowStaminaStartThreshold, 0.01f, 1f);
        lowStaminaCriticalThreshold = Mathf.Clamp(lowStaminaCriticalThreshold, 0f, lowStaminaStartThreshold);
        lowStaminaTargetIntensity = Mathf.Clamp01(lowStaminaTargetIntensity);
        lowStaminaTargetSmoothness = Mathf.Clamp01(lowStaminaTargetSmoothness);
    }

    private void CacheReferences()
    {
        if (baseVolume == null)
            baseVolume = GetComponent<Volume>();

        if (feedbackVolume == null)
            feedbackVolume = ResolveFeedbackVolumeFromChildren();

        if (playerHealth == null)
            playerHealth = GetComponentInParent<PlayerHealth>();

        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

        if (ownerPhotonView == null)
            ownerPhotonView = GetComponentInParent<PhotonView>();
    }

    private bool HasLocalAuthority()
    {
        if (ownerPhotonView == null)
            ownerPhotonView = GetComponentInParent<PhotonView>();

        if (ownerPhotonView != null && !ownerPhotonView.IsMine)
            return false;

        return playerHealth == null || playerHealth.IsLocallyOwned;
    }

    private void SubscribeToPlayerHealth()
    {
        if (subscribedHealth == playerHealth)
            return;

        UnsubscribeFromPlayerHealth();

        if (playerHealth == null || !HasLocalAuthority())
            return;

        subscribedHealth = playerHealth;
        subscribedHealth.Damaged += HandlePlayerDamaged;
    }

    private void UnsubscribeFromPlayerHealth()
    {
        if (subscribedHealth == null)
            return;

        subscribedHealth.Damaged -= HandlePlayerDamaged;
        subscribedHealth = null;
    }

    private void HandlePlayerDamaged(PlayerHealth health, DamageInfo damageInfo)
    {
        if (maxDamageFlashIntensity <= 0.001f)
            return;

        float minFlash = Mathf.Min(MinDamageFlashIntensity, maxDamageFlashIntensity);
        float scaledDamage = Mathf.Clamp01(damageInfo.Amount / Mathf.Max(0.1f, damageForMaxFlash));
        float targetFlash = Mathf.Lerp(minFlash, maxDamageFlashIntensity, scaledDamage);
        damageFlashIntensity = Mathf.Max(damageFlashIntensity, targetFlash);
    }

    private bool PrepareFeedbackVignette()
    {
        if (feedbackVignette != null)
            return true;

        if (feedbackVolume == null)
        {
            if (!warnedMissingFeedbackVolume)
            {
                Debug.LogWarning("PlayerPostProcessingFeedbackController: missing feedback Volume reference.", this);
                warnedMissingFeedbackVolume = true;
            }

            return false;
        }

        feedbackVolume.isGlobal = true;
        feedbackVolume.priority = Mathf.Max(feedbackVolume.priority, FeedbackVolumePriority);

        VolumeProfile runtimeProfile = feedbackVolume.profile;
        if (runtimeProfile == null)
        {
            createdFeedbackProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            createdFeedbackProfile.name = "Runtime Player Post Processing Feedback";
            createdFeedbackProfile.hideFlags = HideFlags.DontSave;
            feedbackVolume.profile = createdFeedbackProfile;
            runtimeProfile = createdFeedbackProfile;
        }

        if (!runtimeProfile.TryGet(out feedbackVignette))
            feedbackVignette = runtimeProfile.Add<Vignette>(true);

        ConfigureFeedbackVignetteOverrides();
        ClearFeedbackVolume();
        return feedbackVignette != null;
    }

    private void ConfigureFeedbackVignetteOverrides()
    {
        if (feedbackVignette == null)
            return;

        feedbackVignette.active = true;
        feedbackVignette.color.overrideState = true;
        feedbackVignette.intensity.overrideState = true;
        feedbackVignette.smoothness.overrideState = true;
    }

    private void ApplyFeedbackVignette(
        float damageFlashWeight,
        float lowHealthPulseWeight,
        float lowHealthSlowPulseWeight,
        float lowStaminaWeight,
        float feedbackWeight)
    {
        ReadBaseVignetteState(out _, out float baseIntensity, out float baseSmoothness);
        ConfigureFeedbackVignetteOverrides();

        float lowHealthWeight = Mathf.Clamp01(lowHealthPulseWeight + lowHealthSlowPulseWeight);
        float totalSourceWeight = Mathf.Max(0.001f, damageFlashWeight + lowHealthWeight + lowStaminaWeight);
        float damageBlend = damageFlashWeight / totalSourceWeight;
        float lowHealthBlend = lowHealthWeight / totalSourceWeight;
        float lowStaminaBlend = lowStaminaWeight / totalSourceWeight;
        float slowPulseBlend = Mathf.Clamp01(lowHealthSlowPulseWeight);
        float damageTargetIntensity = Mathf.Clamp01(baseIntensity + maxDamageFlashIntensity);
        float damageTargetSmoothness = Mathf.Clamp01(baseSmoothness + maxDamageFlashIntensity * 0.35f);
        float heartbeatTargetIntensity = Mathf.Clamp01(Mathf.Max(baseIntensity, lowHealthPulseTargetIntensity));
        float heartbeatTargetSmoothness = Mathf.Clamp01(Mathf.Max(baseSmoothness, lowHealthPulseTargetSmoothness));
        float slowTargetIntensity = Mathf.Clamp01(Mathf.Max(baseIntensity, lowHealthSlowPulseTargetIntensity));
        float slowTargetSmoothness = Mathf.Clamp01(Mathf.Max(baseSmoothness, lowHealthSlowPulseTargetSmoothness));
        float lowHealthTargetIntensity = Mathf.Lerp(heartbeatTargetIntensity, slowTargetIntensity, slowPulseBlend);
        float lowHealthTargetSmoothness = Mathf.Lerp(heartbeatTargetSmoothness, slowTargetSmoothness, slowPulseBlend);
        float lowStaminaTargetIntensityValue = Mathf.Clamp01(Mathf.Max(baseIntensity, lowStaminaTargetIntensity));
        float lowStaminaTargetSmoothnessValue = Mathf.Clamp01(Mathf.Max(baseSmoothness, lowStaminaTargetSmoothness));

        feedbackVolume.weight = feedbackWeight;
        feedbackVignette.color.value =
            damageColor * damageBlend +
            lowHealthColor * lowHealthBlend +
            lowStaminaColor * lowStaminaBlend;
        feedbackVignette.intensity.value =
            damageTargetIntensity * damageBlend +
            lowHealthTargetIntensity * lowHealthBlend +
            lowStaminaTargetIntensityValue * lowStaminaBlend;
        feedbackVignette.smoothness.value =
            damageTargetSmoothness * damageBlend +
            lowHealthTargetSmoothness * lowHealthBlend +
            lowStaminaTargetSmoothnessValue * lowStaminaBlend;
    }

    private void ClearFeedbackVolume()
    {
        if (feedbackVolume != null)
            feedbackVolume.weight = 0f;
    }

    private void ReadBaseVignetteState(out Color baseColor, out float baseIntensity, out float baseSmoothness)
    {
        baseColor = Color.black;
        baseIntensity = 0f;
        baseSmoothness = 0.3f;

        if (baseVolume == null)
        {
            if (!warnedMissingBaseVolume)
            {
                Debug.LogWarning("PlayerPostProcessingFeedbackController: missing base Volume reference. Feedback will use default Vignette baseline.", this);
                warnedMissingBaseVolume = true;
            }

            return;
        }

        VolumeProfile baseProfile = baseVolume.profile;
        if (baseProfile == null || !baseProfile.TryGet(out baseVignette) || baseVignette == null)
            return;

        baseColor = baseVignette.color.value;
        baseSmoothness = baseVignette.smoothness.value;
        baseIntensity = baseVignette.active ? baseVignette.intensity.value : 0f;
    }

    private Volume ResolveFeedbackVolumeFromChildren()
    {
        Volume fallbackVolume = null;
        Volume[] childVolumes = GetComponentsInChildren<Volume>(true);
        for (int i = 0; i < childVolumes.Length; i++)
        {
            Volume childVolume = childVolumes[i];
            if (childVolume == null || childVolume == baseVolume)
                continue;

            if (childVolume.gameObject.name == FeedbackVolumeName)
                return childVolume;

            fallbackVolume ??= childVolume;
        }

        return fallbackVolume;
    }

    private float ResolveDamageFlashWeight()
    {
        if (maxDamageFlashIntensity <= 0.001f)
            return 0f;

        return Mathf.Clamp01(damageFlashIntensity / maxDamageFlashIntensity);
    }

    private float CalculateLowHealthPulseWeight()
    {
        if (playerHealth == null || !playerHealth.IsAlive || playerHealth.MaxHealth <= 0.001f)
            return 0f;

        float normalizedHealth = Mathf.Clamp01(playerHealth.CurrentHealth / playerHealth.MaxHealth);
        if (normalizedHealth > lowHealthThreshold)
            return 0f;

        float severity = Mathf.InverseLerp(lowHealthThreshold, 0f, normalizedHealth);
        float heartbeat = EvaluateHeartbeatPulse(Time.time * lowHealthPulseSpeed);
        return severity * heartbeat;
    }

    private float CalculateLowHealthSlowPulseWeight()
    {
        if (playerHealth == null || !playerHealth.IsAlive || playerHealth.MaxHealth <= 0.001f)
            return 0f;

        float normalizedHealth = Mathf.Clamp01(playerHealth.CurrentHealth / playerHealth.MaxHealth);
        if (normalizedHealth > lowHealthThreshold)
            return 0f;

        float severity = Mathf.InverseLerp(lowHealthThreshold, 0f, normalizedHealth);
        float pulse = (Mathf.Sin(Time.time * lowHealthSlowPulseSpeed * Mathf.PI * 2f - Mathf.PI * 0.5f) + 1f) * 0.5f;
        pulse = pulse * pulse * (3f - 2f * pulse);
        return severity * pulse;
    }

    private float CalculateLowStaminaWeight()
    {
        if (playerMovement == null || playerMovement.MaxStamina <= 0.001f)
            return 0f;

        float normalizedStamina = Mathf.Clamp01(playerMovement.StaminaNormalized);
        float startThreshold = Mathf.Max(0.01f, lowStaminaStartThreshold);
        float criticalThreshold = Mathf.Clamp(lowStaminaCriticalThreshold, 0f, startThreshold);

        if (normalizedStamina >= startThreshold)
            return 0f;

        if (normalizedStamina > criticalThreshold)
        {
            float earlyWeight = InverseLerpDescending(startThreshold, criticalThreshold, normalizedStamina);
            return Smooth01(earlyWeight) * LowStaminaPreCriticalWeight;
        }

        if (criticalThreshold <= 0.001f)
            return 1f;

        float criticalWeight = InverseLerpDescending(criticalThreshold, 0f, normalizedStamina);
        return Mathf.Lerp(LowStaminaPreCriticalWeight, 1f, Smooth01(criticalWeight));
    }

    private float EvaluateHeartbeatPulse(float time)
    {
        float phase = Mathf.Repeat(time, 1f);
        float firstBeat = GaussianPulse(phase, 0.08f, 0.045f);
        float secondBeat = GaussianPulse(phase, 0.23f, 0.06f) * 0.65f;
        return Mathf.Clamp01(Mathf.Max(firstBeat, secondBeat));
    }

    private float GaussianPulse(float phase, float center, float width)
    {
        float distance = (phase - center) / Mathf.Max(0.001f, width);
        return Mathf.Exp(-distance * distance);
    }

    private static float InverseLerpDescending(float upper, float lower, float value)
    {
        return Mathf.Clamp01((upper - value) / Mathf.Max(0.001f, upper - lower));
    }

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

}
