using UnityEngine;

[DisallowMultipleComponent]
public sealed class TorchFlameController : MonoBehaviour
{
    private const string DefaultFlameRootName = "VFX";
    private const float MinDuration = 0.01f;

    [Header("References")]
    [SerializeField] private GameObject flameRoot;
    [SerializeField] private Light flameLight;

    [Header("State")]
    [SerializeField] private bool litOnAwake = true;

    [Header("Base Light")]
    [SerializeField] [Min(0f)] private float baseIntensity = 2.2f;
    [SerializeField] [Min(0f)] private float baseRange = 5f;
    [SerializeField] private Color baseColor = new Color(1f, 0.54f, 0.22f, 1f);

    [Header("Flicker")]
    [SerializeField] private bool enableFlicker = true;
    [SerializeField] [Min(0f)] private float intensityVariation = 0.65f;
    [SerializeField] [Min(0f)] private float rangeVariation = 0.45f;
    [SerializeField] [Min(0f)] private float colorVariation = 0.12f;
    [SerializeField] [Min(0.01f)] private float flickerSpeed = 9f;
    [SerializeField] [Min(0.01f)] private float flickerResponse = 18f;

    [Header("Light Movement")]
    [SerializeField] [Min(0f)] private float movementRadius = 0.025f;
    [SerializeField] [Min(0.01f)] private float movementSpeed = 4.5f;

    [Header("Sputter")]
    [SerializeField] private bool enableSputter = true;
    [SerializeField] [Min(0f)] private float sputterChancePerSecond = 0.04f;
    [SerializeField] private Vector2 sputterDuration = new Vector2(0.06f, 0.18f);
    [SerializeField] [Range(0f, 1f)] private float sputterIntensityMultiplier = 0.08f;
    [SerializeField] private bool hideFlameDuringSputter = true;

    private bool isLit;
    private bool presentationVisible = true;
    private bool initialized;
    private ParticleSystem[] particleSystems = System.Array.Empty<ParticleSystem>();
    private Transform lightTransform;
    private Vector3 baseLightLocalPosition;
    private bool hasBaseLightLocalPosition;
    private float flickerSeed;
    private float currentIntensity;
    private float currentRange;
    private Color currentColor;
    private bool particlesActive;
    private bool sputtering;
    private float sputterEndTime;

    public bool IsLit
    {
        get
        {
            EnsureInitialized();
            return isLit;
        }
    }

    private void Reset()
    {
        ResolveReferences();
        PullBaseLightValuesFromLight();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        ResolveReferences();
        SanitizeTuningValues();
        ApplyLightValues(baseIntensity, baseRange, baseColor, resetPosition: true);
    }

    private void Awake()
    {
        flickerSeed = Random.Range(0f, 1000f);
        EnsureInitialized();
        ApplyState();
    }

    private void OnEnable()
    {
        EnsureInitialized();
        ApplyState();
    }

    private void Update()
    {
        if (!isLit || !presentationVisible || flameLight == null)
            return;

        UpdateSputter();
        UpdateLightFlicker();
    }

    public bool ToggleLit()
    {
        SetLit(!IsLit);
        return isLit;
    }

    public void SetLit(bool lit)
    {
        ResolveReferences();
        SanitizeTuningValues();
        bool wasLit = isLit;
        initialized = true;
        isLit = lit;
        if (isLit && !wasLit)
            ResetFlickerState();

        ApplyState();
    }

    public void SetPresentationVisible(bool visible)
    {
        ResolveReferences();
        presentationVisible = visible;
        ApplyState();
    }

    private void EnsureInitialized()
    {
        ResolveReferences();

        if (initialized)
            return;

        isLit = litOnAwake;
        ResetFlickerState();
        initialized = true;
    }

    private void ResolveReferences()
    {
        if (flameRoot == null)
        {
            Transform flameRootTransform = FindChildTransformByName(transform, DefaultFlameRootName);
            if (flameRootTransform != null)
                flameRoot = flameRootTransform.gameObject;
        }

        if (flameLight == null)
            flameLight = GetComponentInChildren<Light>(true);

        lightTransform = flameLight != null ? flameLight.transform : null;
        if (lightTransform != null && !hasBaseLightLocalPosition)
        {
            baseLightLocalPosition = lightTransform.localPosition;
            hasBaseLightLocalPosition = true;
        }

        if (flameRoot != null)
            particleSystems = flameRoot.GetComponentsInChildren<ParticleSystem>(true);
        else
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
    }

    private void ApplyState()
    {
        bool rootActive = presentationVisible;
        if (flameRoot != null && flameRoot.activeSelf != rootActive)
            flameRoot.SetActive(rootActive);

        bool flameActive = isLit && presentationVisible && !ShouldHideFlameForSputter();
        SetParticlesActive(flameActive, clearWhenStopping: !isLit);

        bool lightActive = isLit && presentationVisible;
        if (flameLight != null && flameLight.enabled != lightActive)
            flameLight.enabled = lightActive;

        if (!lightActive)
            ApplyLightValues(0f, baseRange, baseColor, resetPosition: true);
    }

    private void PullBaseLightValuesFromLight()
    {
        if (flameLight == null)
            return;

        baseIntensity = Mathf.Max(0f, flameLight.intensity);
        baseRange = Mathf.Max(0f, flameLight.range);
        baseColor = flameLight.color;
    }

    private void SanitizeTuningValues()
    {
        baseIntensity = Mathf.Max(0f, baseIntensity);
        baseRange = Mathf.Max(0f, baseRange);
        intensityVariation = Mathf.Max(0f, intensityVariation);
        rangeVariation = Mathf.Max(0f, rangeVariation);
        colorVariation = Mathf.Max(0f, colorVariation);
        flickerSpeed = Mathf.Max(0.01f, flickerSpeed);
        flickerResponse = Mathf.Max(0.01f, flickerResponse);
        movementRadius = Mathf.Max(0f, movementRadius);
        movementSpeed = Mathf.Max(0.01f, movementSpeed);
        sputterChancePerSecond = Mathf.Max(0f, sputterChancePerSecond);

        if (sputterDuration.x < MinDuration)
            sputterDuration.x = MinDuration;
        if (sputterDuration.y < sputterDuration.x)
            sputterDuration.y = sputterDuration.x;
    }

    private void ResetFlickerState()
    {
        currentIntensity = Mathf.Max(0f, baseIntensity);
        currentRange = Mathf.Max(0f, baseRange);
        currentColor = baseColor;
        sputtering = false;
        sputterEndTime = 0f;
        ApplyLightValues(currentIntensity, currentRange, currentColor, resetPosition: true);
    }

    private void UpdateSputter()
    {
        if (!enableSputter)
        {
            if (sputtering)
            {
                sputtering = false;
                ApplyState();
            }

            return;
        }

        if (sputtering)
        {
            if (Time.time < sputterEndTime)
                return;

            sputtering = false;
            ApplyState();
            return;
        }

        float chance = sputterChancePerSecond * Time.deltaTime;
        if (chance <= 0f || Random.value > chance)
            return;

        sputtering = true;
        sputterEndTime = Time.time + Random.Range(sputterDuration.x, sputterDuration.y);
        ApplyState();
    }

    private void UpdateLightFlicker()
    {
        float targetIntensity = baseIntensity;
        float targetRange = baseRange;
        Color targetColor = baseColor;
        Vector3 targetPosition = baseLightLocalPosition;

        if (enableFlicker)
        {
            float time = Time.time;
            float slowNoise = Mathf.PerlinNoise(flickerSeed, time * flickerSpeed);
            float fastNoise = Mathf.PerlinNoise(flickerSeed + 17.31f, time * flickerSpeed * 2.4f);
            float flicker = (((slowNoise * 0.72f) + (fastNoise * 0.28f)) - 0.5f) * 2f;
            float warmth = Mathf.Clamp01(0.5f + flicker * 0.5f);

            targetIntensity = Mathf.Max(0f, baseIntensity + flicker * intensityVariation);
            targetRange = Mathf.Max(0f, baseRange + flicker * rangeVariation);
            targetColor = Color.Lerp(
                new Color(
                    Mathf.Max(0f, baseColor.r - colorVariation * 0.15f),
                    Mathf.Max(0f, baseColor.g - colorVariation * 0.1f),
                    Mathf.Max(0f, baseColor.b - colorVariation * 0.35f),
                    baseColor.a),
                new Color(
                    Mathf.Min(1f, baseColor.r + colorVariation * 0.2f),
                    Mathf.Min(1f, baseColor.g + colorVariation * 0.45f),
                    Mathf.Min(1f, baseColor.b + colorVariation * 0.12f),
                    baseColor.a),
                warmth);

            if (movementRadius > 0f)
            {
                float x = (Mathf.PerlinNoise(flickerSeed + 41.7f, time * movementSpeed) - 0.5f) * 2f;
                float y = (Mathf.PerlinNoise(flickerSeed + 83.2f, time * movementSpeed * 1.3f) - 0.5f) * 2f;
                float z = (Mathf.PerlinNoise(flickerSeed + 126.6f, time * movementSpeed * 0.9f) - 0.5f) * 2f;
                targetPosition = baseLightLocalPosition + new Vector3(x, y, z) * movementRadius;
            }
        }

        if (sputtering)
            targetIntensity *= sputterIntensityMultiplier;

        float lerp = 1f - Mathf.Exp(-flickerResponse * Time.deltaTime);
        currentIntensity = Mathf.Lerp(currentIntensity, targetIntensity, lerp);
        currentRange = Mathf.Lerp(currentRange, targetRange, lerp);
        currentColor = Color.Lerp(currentColor, targetColor, lerp);

        ApplyLightValues(currentIntensity, currentRange, currentColor, resetPosition: false);
        if (lightTransform != null)
            lightTransform.localPosition = Vector3.Lerp(lightTransform.localPosition, targetPosition, lerp);
    }

    private void ApplyLightValues(float intensity, float range, Color color, bool resetPosition)
    {
        if (flameLight != null)
        {
            flameLight.intensity = Mathf.Max(0f, intensity);
            flameLight.range = Mathf.Max(0f, range);
            flameLight.color = color;
        }

        if (resetPosition && lightTransform != null)
            lightTransform.localPosition = baseLightLocalPosition;
    }

    private bool ShouldHideFlameForSputter()
    {
        return sputtering && hideFlameDuringSputter;
    }

    private void SetParticlesActive(bool active, bool clearWhenStopping)
    {
        if (particleSystems == null)
            particleSystems = System.Array.Empty<ParticleSystem>();

        if (particlesActive == active && !(clearWhenStopping && !active))
            return;

        particlesActive = active;
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
                continue;

            if (active)
                particleSystem.Play(withChildren: true);
            else
                particleSystem.Stop(withChildren: true, clearWhenStopping ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private static Transform FindChildTransformByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        Transform[] childTransforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform childTransform = childTransforms[i];
            if (childTransform != null && string.Equals(childTransform.name, targetName, System.StringComparison.Ordinal))
                return childTransform;
        }

        return null;
    }
}
