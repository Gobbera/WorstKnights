using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Enemy/Beholder Eye Glow Controller")]
public sealed class BeholderEyeGlowController : MonoBehaviour
{
    private const string BaseColorProperty = "_Base_Color";
    private const string UrpBaseColorProperty = "_BaseColor";
    private const string LegacyColorProperty = "_Color";
    private const string EmissionColorProperty = "_EmissionColor";
    private const string EmissionIntensityProperty = "_EmissionIntensity";
    private const string EyeMaterialName = "WK_Bh_Eye";

    [Serializable]
    public struct GlowProfile
    {
        [ColorUsage(false, false)] public Color baseColor;
        [ColorUsage(false, true)] public Color emissionColor;
        [Min(0f)] public float emissionIntensity;
        [Range(0f, 1f)] public float pulseAmount;
        [Min(0f)] public float pulseSpeed;
        [Range(0f, 1f)] public float flickerAmount;
        [Min(0f)] public float flickerSpeed;

        public GlowProfile(
            Color baseColor,
            Color emissionColor,
            float emissionIntensity,
            float pulseAmount = 0f,
            float pulseSpeed = 0f,
            float flickerAmount = 0f,
            float flickerSpeed = 0f)
        {
            this.baseColor = baseColor;
            this.emissionColor = emissionColor;
            this.emissionIntensity = emissionIntensity;
            this.pulseAmount = pulseAmount;
            this.pulseSpeed = pulseSpeed;
            this.flickerAmount = flickerAmount;
            this.flickerSpeed = flickerSpeed;
        }
    }

    private sealed class EyeMaterialSlot
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();
    }

    [Header("References")]
    [SerializeField, HideInInspector] private EnemySetup enemySetup;
    [SerializeField, HideInInspector] private EnemyBrain enemyBrain;
    [SerializeField, HideInInspector] private EnemyHealth enemyHealth;

    [Header("Blend")]
    [SerializeField, Min(0f)] private float blendSpeed = 8f;
    [SerializeField, HideInInspector] private float rendererRefreshInterval = 1f;

    [Header("Eye Fake Volume")]
    [SerializeField] private WKFakeVolumeController[] eyeFakeVolumes = Array.Empty<WKFakeVolumeController>();
    [SerializeField] private bool autoBindEyeFakeVolumes = true;
    [SerializeField] private bool driveEyeFakeVolumeColor = true;
    [SerializeField] private bool driveEyeFakeVolumeIntensity;
    [SerializeField, Min(0f)] private float eyeFakeVolumeIntensityMultiplier = 0.15f;
    [SerializeField, Min(0f)] private float eyeFakeVolumeMinIntensity;
    [SerializeField, Min(0f)] private float eyeFakeVolumeMaxIntensity = 3f;
    [SerializeField] private bool driveEyeFakeVolumeOpacity;
    [SerializeField, Range(0f, 1f)] private float eyeFakeVolumeOpacity = 0.2f;

    [Header("Eye Spot Light")]
    [SerializeField] private Light[] eyeSpotLights = Array.Empty<Light>();
    [SerializeField] private bool autoBindEyeSpotLights = true;
    [SerializeField] private bool eyeSpotLightsEnabled = true;
    [SerializeField] private bool driveEyeSpotLightColor = true;
    [SerializeField] private bool driveEyeSpotLightIntensity = true;
    [SerializeField, Min(0f)] private float eyeSpotLightIntensityMultiplier = 0.12f;
    [SerializeField, Min(0f)] private float eyeSpotLightMinIntensity;
    [SerializeField, Min(0f)] private float eyeSpotLightMaxIntensity = 4f;
    [SerializeField] private bool driveEyeSpotLightRange = true;
    [SerializeField, Min(0f)] private float eyeSpotLightRange = 1.5f;
    [SerializeField] private bool driveEyeSpotLightAngles = true;
    [SerializeField, Range(1f, 179f)] private float eyeSpotLightAngle = 30f;
    [SerializeField, Range(0f, 179f)] private float eyeSpotLightInnerAngle = 21.8f;

    [Header("State Profiles")]
    [SerializeField] private GlowProfile preSpawn = new GlowProfile(
        new Color(0.32f, 0.25f, 0.38f, 1f),
        new Color(0.18f, 0.08f, 0.35f, 1f),
        0.15f);
    [SerializeField] private GlowProfile idle = new GlowProfile(
        new Color(0.42f, 0.34f, 0.5f, 1f),
        new Color(0.28f, 0.12f, 0.55f, 1f),
        0.35f,
        0.08f,
        0.6f);
    [SerializeField] private GlowProfile patrol = new GlowProfile(
        new Color(0.45f, 0.36f, 0.52f, 1f),
        new Color(0.35f, 0.18f, 0.65f, 1f),
        0.6f,
        0.12f,
        0.8f);
    [SerializeField] private GlowProfile alert = new GlowProfile(
        new Color(0.72f, 0.45f, 0.22f, 1f),
        new Color(1f, 0.55f, 0.18f, 1f),
        1.5f,
        0.18f,
        1.25f);
    [SerializeField] private GlowProfile search = new GlowProfile(
        new Color(0.65f, 0.38f, 0.18f, 1f),
        new Color(1f, 0.38f, 0.08f, 1f),
        1.2f,
        0.2f,
        1.1f);
    [SerializeField] private GlowProfile chase = new GlowProfile(
        new Color(0.82f, 0.18f, 0.12f, 1f),
        new Color(1f, 0.08f, 0.02f, 1f),
        3.5f,
        0.28f,
        1.8f);
    [SerializeField] private GlowProfile attack = new GlowProfile(
        new Color(1f, 0.28f, 0.08f, 1f),
        new Color(1f, 0.24f, 0.04f, 1f),
        5f,
        0.35f,
        2.2f);
    [SerializeField] private GlowProfile returnToPatrol = new GlowProfile(
        new Color(0.42f, 0.32f, 0.46f, 1f),
        new Color(0.3f, 0.12f, 0.48f, 1f),
        0.45f,
        0.08f,
        0.7f);
    [SerializeField] private GlowProfile traversal = new GlowProfile(
        new Color(0.58f, 0.28f, 0.5f, 1f),
        new Color(0.75f, 0.18f, 0.85f, 1f),
        1f,
        0.16f,
        1f);
    [SerializeField] private GlowProfile stunned = new GlowProfile(
        new Color(0.18f, 0.42f, 0.75f, 1f),
        new Color(0.1f, 0.55f, 1f, 1f),
        0.25f,
        0.1f,
        1.5f,
        0.85f,
        12f);
    [SerializeField] private GlowProfile dead = new GlowProfile(
        new Color(0.08f, 0.08f, 0.08f, 1f),
        Color.black,
        0f);

    private readonly List<EyeMaterialSlot> eyeMaterialSlots = new List<EyeMaterialSlot>();

    private Color currentBaseColor;
    private Color currentEmissionColor;
    private float currentEmissionIntensity;
    private bool hasCurrentValues;
    private EnemyState previousState = (EnemyState)(-1);
    private float stateStartedAt;
    private float nextRendererRefreshTime;
    private bool overrideActive;
    private GlowProfile overrideProfile;
    private float overrideUntil;
    private int overridePriority;
    private Color moodEyeColor = Color.white;
    private float moodColorBlend;
    private float moodIntensityMultiplier = 1f;

    public int ResolvedEyeMaterialSlotCount => eyeMaterialSlots.Count;

    private void Awake()
    {
        ResolveReferences();
        ResolveEyeVfxTargets();
        RefreshEyeMaterialSlots();
        ForceApplyCurrentProfile();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveEyeVfxTargets();
        RefreshEyeMaterialSlots();
        ForceApplyCurrentProfile();
    }

    private void Update()
    {
        ResolveReferences();

        if (eyeMaterialSlots.Count == 0 && Time.time >= nextRendererRefreshTime)
            RefreshEyeMaterialSlots();

        GlowProfile targetProfile = ResolveActiveProfile();
        EnemyState state = ResolveState();
        if (!overrideActive && state != EnemyState.PreSpawn && state != EnemyState.Spawning
            && state != EnemyState.Stunned && state != EnemyState.Dead)
        {
            targetProfile.baseColor = Color.Lerp(targetProfile.baseColor, moodEyeColor, moodColorBlend);
            targetProfile.emissionColor = Color.Lerp(targetProfile.emissionColor, moodEyeColor, moodColorBlend);
            targetProfile.emissionIntensity *= moodIntensityMultiplier;
        }

        float targetIntensity = ResolveAnimatedIntensity(targetProfile);
        float blend = ResolveBlendFactor(Time.deltaTime);

        if (!hasCurrentValues)
        {
            currentBaseColor = targetProfile.baseColor;
            currentEmissionColor = targetProfile.emissionColor;
            currentEmissionIntensity = targetIntensity;
            hasCurrentValues = true;
        }
        else
        {
            currentBaseColor = Color.Lerp(currentBaseColor, targetProfile.baseColor, blend);
            currentEmissionColor = Color.Lerp(currentEmissionColor, targetProfile.emissionColor, blend);
            currentEmissionIntensity = Mathf.Lerp(currentEmissionIntensity, targetIntensity, blend);
        }

        ApplyEyeGlow(currentBaseColor, currentEmissionColor, currentEmissionIntensity);
    }

    public void SetGlowOverride(
        GlowProfile profile,
        float duration,
        int priority = 0)
    {
        if (overrideActive && priority < overridePriority)
            return;

        overrideProfile = SanitizeProfile(profile);
        overrideUntil = duration > 0f ? Time.time + duration : float.PositiveInfinity;
        overridePriority = priority;
        overrideActive = true;
    }

    public void SetGlowOverride(
        Color baseColor,
        Color emissionColor,
        float emissionIntensity,
        float duration,
        int priority = 0,
        float pulseAmount = 0f,
        float pulseSpeed = 0f,
        float flickerAmount = 0f,
        float flickerSpeed = 0f)
    {
        SetGlowOverride(
            new GlowProfile(
                baseColor,
                emissionColor,
                emissionIntensity,
                pulseAmount,
                pulseSpeed,
                flickerAmount,
                flickerSpeed),
            duration,
            priority);
    }

    public void ClearGlowOverride(int minimumPriority = int.MinValue)
    {
        if (!overrideActive || overridePriority < minimumPriority)
            return;

        overrideActive = false;
        overridePriority = 0;
        overrideUntil = 0f;
    }

    public void SetMoodVisual(Color eyeColor, float colorBlend, float intensityMultiplier)
    {
        moodEyeColor = eyeColor;
        moodColorBlend = Mathf.Clamp01(colorBlend);
        moodIntensityMultiplier = Mathf.Max(0f, intensityMultiplier);
    }

    [ContextMenu("Refresh Eye Material Slots")]
    private void RefreshEyeMaterialSlots()
    {
        eyeMaterialSlots.Clear();
        nextRendererRefreshTime = Time.time + Mathf.Max(0.1f, rendererRefreshInterval);

        Transform root = enemySetup != null && enemySetup.ModelRoot != null
            ? enemySetup.ModelRoot
            : transform;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer targetRenderer = renderers[rendererIndex];
            if (targetRenderer == null)
                continue;

            Material[] sharedMaterials = targetRenderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
            {
                Material material = sharedMaterials[materialIndex];
                if (!IsEyeMaterial(material))
                    continue;

                eyeMaterialSlots.Add(new EyeMaterialSlot
                {
                    Renderer = targetRenderer,
                    MaterialIndex = materialIndex
                });
            }
        }
    }

    [ContextMenu("Auto Bind Eye VFX Targets")]
    private void AutoBindEyeVfxTargets()
    {
        List<WKFakeVolumeController> fakeVolumes = new List<WKFakeVolumeController>();
        GetComponentsInChildren(includeInactive: true, fakeVolumes);
        eyeFakeVolumes = fakeVolumes.ToArray();

        Light[] childLights = GetComponentsInChildren<Light>(true);
        List<Light> spotLights = new List<Light>(childLights.Length);
        for (int i = 0; i < childLights.Length; i++)
        {
            Light targetLight = childLights[i];
            if (targetLight != null && targetLight.type == LightType.Spot)
                spotLights.Add(targetLight);
        }

        eyeSpotLights = spotLights.ToArray();
    }

    private void ForceApplyCurrentProfile()
    {
        GlowProfile profile = ResolveActiveProfile();
        currentBaseColor = profile.baseColor;
        currentEmissionColor = profile.emissionColor;
        currentEmissionIntensity = ResolveAnimatedIntensity(profile);
        hasCurrentValues = true;
        ApplyEyeGlow(currentBaseColor, currentEmissionColor, currentEmissionIntensity);
    }

    private void ResolveReferences()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyBrain == null)
            enemyBrain = enemySetup != null ? enemySetup.EnemyBrain : GetComponent<EnemyBrain>();

        if (enemyHealth == null)
            enemyHealth = enemySetup != null ? enemySetup.EnemyHealth : GetComponent<EnemyHealth>();
    }

    private void ResolveEyeVfxTargets()
    {
        if (autoBindEyeFakeVolumes && (eyeFakeVolumes == null || eyeFakeVolumes.Length == 0))
        {
            List<WKFakeVolumeController> fakeVolumes = new List<WKFakeVolumeController>();
            GetComponentsInChildren(includeInactive: true, fakeVolumes);
            eyeFakeVolumes = fakeVolumes.ToArray();
        }

        if (autoBindEyeSpotLights && (eyeSpotLights == null || eyeSpotLights.Length == 0))
        {
            Light[] childLights = GetComponentsInChildren<Light>(true);
            List<Light> spotLights = new List<Light>(childLights.Length);
            for (int i = 0; i < childLights.Length; i++)
            {
                Light targetLight = childLights[i];
                if (targetLight != null && targetLight.type == LightType.Spot)
                    spotLights.Add(targetLight);
            }

            eyeSpotLights = spotLights.ToArray();
        }
    }

    private GlowProfile ResolveActiveProfile()
    {
        if (overrideActive)
        {
            if (Time.time <= overrideUntil)
                return overrideProfile;

            overrideActive = false;
            overridePriority = 0;
            overrideUntil = 0f;
        }

        EnemyState state = ResolveState();
        if (state != previousState)
        {
            previousState = state;
            stateStartedAt = Time.time;
        }

        return ResolveProfileForState(state);
    }

    private EnemyState ResolveState()
    {
        if (enemyHealth != null && !enemyHealth.IsAlive)
            return EnemyState.Dead;

        return enemyBrain != null ? enemyBrain.CurrentState : EnemyState.Idle;
    }

    private GlowProfile ResolveProfileForState(EnemyState state)
    {
        switch (state)
        {
            case EnemyState.PreSpawn:
            case EnemyState.Spawning:
                return SanitizeProfile(preSpawn);
            case EnemyState.Patrol:
                return SanitizeProfile(patrol);
            case EnemyState.Alert:
                return SanitizeProfile(alert);
            case EnemyState.Search:
            case EnemyState.Reflection:
                return SanitizeProfile(search);
            case EnemyState.Chase:
                return SanitizeProfile(chase);
            case EnemyState.Attack:
                return SanitizeProfile(attack);
            case EnemyState.Return:
                return SanitizeProfile(returnToPatrol);
            case EnemyState.Traversal:
                return SanitizeProfile(traversal);
            case EnemyState.Stunned:
                return SanitizeProfile(stunned);
            case EnemyState.Dead:
                return SanitizeProfile(dead);
            case EnemyState.Idle:
            default:
                return SanitizeProfile(idle);
        }
    }

    private float ResolveAnimatedIntensity(GlowProfile profile)
    {
        float intensity = Mathf.Max(0f, profile.emissionIntensity);
        float elapsed = Mathf.Max(0f, Time.time - stateStartedAt);

        if (profile.pulseAmount > 0f && profile.pulseSpeed > 0f)
        {
            float wave = Mathf.Sin(elapsed * profile.pulseSpeed * Mathf.PI * 2f) * 0.5f + 0.5f;
            float multiplier = Mathf.Lerp(1f - profile.pulseAmount, 1f + profile.pulseAmount, wave);
            intensity *= Mathf.Max(0f, multiplier);
        }

        if (profile.flickerAmount > 0f && profile.flickerSpeed > 0f)
        {
            float flicker = Mathf.PerlinNoise(elapsed * profile.flickerSpeed, 17.381f);
            float multiplier = Mathf.Lerp(1f - profile.flickerAmount, 1f, flicker);
            intensity *= Mathf.Max(0f, multiplier);
        }

        return intensity;
    }

    private float ResolveBlendFactor(float deltaTime)
    {
        if (blendSpeed <= 0f)
            return 1f;

        return 1f - Mathf.Exp(-blendSpeed * Mathf.Max(0f, deltaTime));
    }

    private void ApplyEyeGlow(Color baseColor, Color emissionColor, float emissionIntensity)
    {
        for (int i = eyeMaterialSlots.Count - 1; i >= 0; i--)
        {
            EyeMaterialSlot slot = eyeMaterialSlots[i];
            if (slot.Renderer == null)
            {
                eyeMaterialSlots.RemoveAt(i);
                continue;
            }

            slot.Renderer.GetPropertyBlock(slot.PropertyBlock, slot.MaterialIndex);
            slot.PropertyBlock.SetColor(BaseColorProperty, baseColor);
            slot.PropertyBlock.SetColor(UrpBaseColorProperty, baseColor);
            slot.PropertyBlock.SetColor(LegacyColorProperty, baseColor);
            slot.PropertyBlock.SetColor(EmissionColorProperty, emissionColor);
            slot.PropertyBlock.SetFloat(EmissionIntensityProperty, Mathf.Max(0f, emissionIntensity));
            slot.Renderer.SetPropertyBlock(slot.PropertyBlock, slot.MaterialIndex);
        }

        ApplyEyeVfxTargets(emissionColor, emissionIntensity);
    }

    private void ApplyEyeVfxTargets(Color emissionColor, float emissionIntensity)
    {
        Color tint = ResolveTintColor(emissionColor);
        float outputIntensity = ResolveOutputIntensity(emissionColor, emissionIntensity);

        ApplyEyeFakeVolumes(tint, outputIntensity);
        ApplyEyeSpotLights(tint, outputIntensity);
    }

    private void ApplyEyeFakeVolumes(Color tint, float outputIntensity)
    {
        if (eyeFakeVolumes == null || eyeFakeVolumes.Length == 0)
            return;

        float volumeIntensity = ClampDrivenIntensity(
            outputIntensity * eyeFakeVolumeIntensityMultiplier,
            eyeFakeVolumeMinIntensity,
            eyeFakeVolumeMaxIntensity);

        for (int i = eyeFakeVolumes.Length - 1; i >= 0; i--)
        {
            WKFakeVolumeController targetVolume = eyeFakeVolumes[i];
            if (targetVolume == null || targetVolume.Settings == null)
                continue;

            WKFakeVolumeSettings settings = targetVolume.Settings;
            if (driveEyeFakeVolumeColor)
                settings.color = tint;

            if (driveEyeFakeVolumeIntensity)
                settings.colorIntensity = volumeIntensity;

            if (driveEyeFakeVolumeOpacity)
                settings.opacity = eyeFakeVolumeOpacity;

            targetVolume.ApplyNow();
        }
    }

    private void ApplyEyeSpotLights(Color tint, float outputIntensity)
    {
        if (eyeSpotLights == null || eyeSpotLights.Length == 0)
            return;

        float lightIntensity = ClampDrivenIntensity(
            outputIntensity * eyeSpotLightIntensityMultiplier,
            eyeSpotLightMinIntensity,
            eyeSpotLightMaxIntensity);

        for (int i = eyeSpotLights.Length - 1; i >= 0; i--)
        {
            Light targetLight = eyeSpotLights[i];
            if (targetLight == null)
                continue;

            bool shouldEnable = eyeSpotLightsEnabled
                && (!driveEyeSpotLightIntensity || lightIntensity > 0.0001f);
            if (targetLight.enabled != shouldEnable)
                targetLight.enabled = shouldEnable;

            if (driveEyeSpotLightColor)
                targetLight.color = tint;

            if (driveEyeSpotLightIntensity)
                targetLight.intensity = lightIntensity;

            if (driveEyeSpotLightRange)
                targetLight.range = eyeSpotLightRange;

            if (driveEyeSpotLightAngles && targetLight.type == LightType.Spot)
            {
                targetLight.spotAngle = eyeSpotLightAngle;
                targetLight.innerSpotAngle = Mathf.Clamp(eyeSpotLightInnerAngle, 0f, targetLight.spotAngle);
            }
        }
    }

    private static GlowProfile SanitizeProfile(GlowProfile profile)
    {
        profile.emissionIntensity = Mathf.Max(0f, profile.emissionIntensity);
        profile.pulseAmount = Mathf.Clamp01(profile.pulseAmount);
        profile.pulseSpeed = Mathf.Max(0f, profile.pulseSpeed);
        profile.flickerAmount = Mathf.Clamp01(profile.flickerAmount);
        profile.flickerSpeed = Mathf.Max(0f, profile.flickerSpeed);
        return profile;
    }

    private static bool IsEyeMaterial(Material material)
    {
        if (material == null || string.IsNullOrEmpty(material.name))
            return false;

        return string.Equals(
            material.name.Replace(" (Instance)", string.Empty),
            EyeMaterialName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static Color ResolveTintColor(Color color)
    {
        float maxComponent = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        if (maxComponent <= 1f)
            return color;

        return new Color(
            color.r / maxComponent,
            color.g / maxComponent,
            color.b / maxComponent,
            color.a);
    }

    private static float ResolveOutputIntensity(Color color, float emissionIntensity)
    {
        float maxComponent = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        return Mathf.Max(0f, emissionIntensity) * Mathf.Max(1f, maxComponent);
    }

    private static float ClampDrivenIntensity(float value, float minimum, float maximum)
    {
        float sanitizedMinimum = Mathf.Max(0f, minimum);
        float sanitizedMaximum = Mathf.Max(sanitizedMinimum, maximum);
        return Mathf.Clamp(Mathf.Max(0f, value), sanitizedMinimum, sanitizedMaximum);
    }

    private void OnValidate()
    {
        blendSpeed = Mathf.Max(0f, blendSpeed);
        rendererRefreshInterval = Mathf.Max(0.1f, rendererRefreshInterval);
        eyeFakeVolumeIntensityMultiplier = Mathf.Max(0f, eyeFakeVolumeIntensityMultiplier);
        eyeFakeVolumeMinIntensity = Mathf.Max(0f, eyeFakeVolumeMinIntensity);
        eyeFakeVolumeMaxIntensity = Mathf.Max(eyeFakeVolumeMinIntensity, eyeFakeVolumeMaxIntensity);
        eyeFakeVolumeOpacity = Mathf.Clamp01(eyeFakeVolumeOpacity);
        eyeSpotLightIntensityMultiplier = Mathf.Max(0f, eyeSpotLightIntensityMultiplier);
        eyeSpotLightMinIntensity = Mathf.Max(0f, eyeSpotLightMinIntensity);
        eyeSpotLightMaxIntensity = Mathf.Max(eyeSpotLightMinIntensity, eyeSpotLightMaxIntensity);
        eyeSpotLightRange = Mathf.Max(0f, eyeSpotLightRange);
        eyeSpotLightAngle = Mathf.Clamp(eyeSpotLightAngle, 1f, 179f);
        eyeSpotLightInnerAngle = Mathf.Clamp(eyeSpotLightInnerAngle, 0f, eyeSpotLightAngle);
        preSpawn = SanitizeProfile(preSpawn);
        idle = SanitizeProfile(idle);
        patrol = SanitizeProfile(patrol);
        alert = SanitizeProfile(alert);
        search = SanitizeProfile(search);
        chase = SanitizeProfile(chase);
        attack = SanitizeProfile(attack);
        returnToPatrol = SanitizeProfile(returnToPatrol);
        traversal = SanitizeProfile(traversal);
        stunned = SanitizeProfile(stunned);
        dead = SanitizeProfile(dead);
    }
}
