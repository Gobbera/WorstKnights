using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class WKFakeVolumeController : MonoBehaviour
{
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int OpacityProperty = Shader.PropertyToID("_Opacity");
    private static readonly int PowerProperty = Shader.PropertyToID("_Power");
    private static readonly int EdgeProperty = Shader.PropertyToID("_Edg");
    private static readonly int FadeDistanceProperty = Shader.PropertyToID("_Fade_distence");
    private static readonly int ExtraSoftnessProperty = Shader.PropertyToID("_Extra_Softness");
    private static readonly int NoiseAScaleProperty = Shader.PropertyToID("_Nois_A_Scale");
    private static readonly int NoiseBScaleProperty = Shader.PropertyToID("_Nois_B_Scale");
    private static readonly int NoiseASizeProperty = Shader.PropertyToID("_Noise_A_Size");
    private static readonly int NoiseBSizeProperty = Shader.PropertyToID("_Noise_B_Size");
    private static readonly int NoiseASpeedProperty = Shader.PropertyToID("_Noise_A_Speed");
    private static readonly int NoiseBSpeedProperty = Shader.PropertyToID("_Noise_B_Speed");
    private static readonly int ExtraNoiseProperty = Shader.PropertyToID("_Extra_Noise");
    private static readonly int ExtraNoiseDensityProperty = Shader.PropertyToID("_Extra_Noise_Density");
    private static readonly int ExtraNoiseSizeProperty = Shader.PropertyToID("_Extra_Noise_Size");
    private static readonly int UseWindProperty = Shader.PropertyToID("_Use_Wind");
    private static readonly int WindSpeedProperty = Shader.PropertyToID("_Wind_Speed");
    private static readonly int WindVisibilityProperty = Shader.PropertyToID("_Wind_Visibility");
    private static readonly int DirectionProperty = Shader.PropertyToID("_Direction");

#if UNITY_EDITOR
    private const string GeneratedRootName = "WK Fake Volume Visual";
    private const string ConeMeshPath = "Assets/VFX/FakeVolume/Meshes/WK_VolumetricLightShaft.fbx";
    private const string ConeMaterialPath = "Assets/VFX/FakeVolume/Materials/WK_VolumeCone.mat";
    private const string PointOuterMaterialPath = "Assets/VFX/FakeVolume/Materials/WK_VolumePointOuter.mat";
    private const string PointInnerMaterialPath = "Assets/VFX/FakeVolume/Materials/WK_VolumePointInner.mat";
    private const string CookieTexturePath = "Assets/VFX/FakeVolume/Textures/WK_Cookie.png";
#endif

    [Serializable]
    public sealed class ShapeBinding
    {
        public WKFakeVolumeShape shape = WKFakeVolumeShape.Existing;
        public GameObject root;
        public Renderer[] renderers = Array.Empty<Renderer>();
        public Light[] lights = Array.Empty<Light>();
        public Transform motionRoot;
    }

    [Header("Settings")]
    [SerializeField] private WKFakeVolumeSettings settings = new WKFakeVolumeSettings();

    [Header("References")]
    [SerializeField] private Renderer[] volumeRenderers = Array.Empty<Renderer>();
    [SerializeField] private Light[] controlledLights = Array.Empty<Light>();
    [SerializeField] private Transform motionRoot;
    [SerializeField] private ShapeBinding[] shapeBindings = Array.Empty<ShapeBinding>();

    [Header("Authoring")]
    [SerializeField] private bool includeInactiveChildren = true;
    [SerializeField] private bool applyInEditMode = true;
    [SerializeField] private bool autoCreateVisuals = true;
    [SerializeField] private bool createGeneratedLight;
    [SerializeField] private bool disableMonoLimboAnimators = true;
    [SerializeField, HideInInspector] private GameObject generatedVisualRoot;
    [SerializeField, HideInInspector] private WKFakeVolumeShape generatedVisualShape = WKFakeVolumeShape.Existing;

    private readonly List<Renderer> activeRenderers = new List<Renderer>();
    private readonly List<Light> activeLights = new List<Light>();
    private MaterialPropertyBlock propertyBlock;
    private Transform cachedMotionRoot;
    private Vector3 baseMotionLocalPosition;
    private Quaternion baseMotionLocalRotation;
    private bool hasCapturedMotionPose;
    private bool hasRuntimeSeed;
    private float runtimeSeed;
    private float colorOffset;
    private float currentFlicker;
    private float previousTime;
    private bool hasPreviousTime;

    public WKFakeVolumeSettings Settings => settings;

    private void Reset()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && autoCreateVisuals)
        {
            if (settings.shape == WKFakeVolumeShape.Existing)
                settings.shape = WKFakeVolumeShape.SpotCone;

            if (EnsureAuthoringVisuals())
                return;
        }
#endif

        AutoBind();
        PullCurrentValuesFromTargets();
        ApplyNow();
    }

    private void OnValidate()
    {
        settings.Sanitize();
        CaptureBasePoseIfNeeded(force: false);

        if (!Application.isPlaying && applyInEditMode)
            ApplyNow();
    }

    private void Awake()
    {
        settings.Sanitize();
        InitializeRuntimeOffsets();
        CaptureBasePoseIfNeeded(force: true);
    }

    private void OnEnable()
    {
        settings.Sanitize();
        InitializeRuntimeOffsets();
        CaptureBasePoseIfNeeded(force: false);

        if (disableMonoLimboAnimators)
            DisablePackageAnimators();

        ApplyNow();
    }

    private void Update()
    {
        if (!Application.isPlaying && !applyInEditMode)
            return;

        ApplyNow();
    }

    [ContextMenu("Auto Bind Children")]
    public void AutoBind()
    {
        Renderer[] childRenderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);
        List<Renderer> filteredRenderers = new List<Renderer>(childRenderers.Length);
        for (int i = 0; i < childRenderers.Length; i++)
        {
            Renderer candidate = childRenderers[i];
            if (candidate != null && LooksLikeVolumeRenderer(candidate))
                filteredRenderers.Add(candidate);
        }

        volumeRenderers = filteredRenderers.ToArray();
        controlledLights = GetComponentsInChildren<Light>(includeInactiveChildren);

        if (controlledLights.Length > 0 && motionRoot == null)
            motionRoot = controlledLights[0].transform;
        else if (motionRoot == null)
            motionRoot = transform;

        if (shapeBindings == null || shapeBindings.Length == 0)
            shapeBindings = BuildSingleShapeBinding();

        CaptureBasePoseIfNeeded(force: true);
    }

    [ContextMenu("Pull Current Values From Targets")]
    public void PullCurrentValuesFromTargets()
    {
        Renderer renderer = FindFirstBoundRenderer();
        Material material = FindFirstVolumeMaterial(renderer);
        if (material != null)
        {
            if (material.HasProperty(ColorProperty))
                settings.color = material.GetColor(ColorProperty);
            if (material.HasProperty(OpacityProperty))
                settings.opacity = material.GetFloat(OpacityProperty);
            if (material.HasProperty(PowerProperty))
                settings.power = material.GetFloat(PowerProperty);
            if (material.HasProperty(EdgeProperty))
                settings.edge = material.GetFloat(EdgeProperty);
            if (material.HasProperty(FadeDistanceProperty))
                settings.fadeDistance = material.GetFloat(FadeDistanceProperty);
            if (material.HasProperty(ExtraSoftnessProperty))
                settings.extraSoftness = material.GetFloat(ExtraSoftnessProperty);
            if (material.HasProperty(NoiseAScaleProperty))
                settings.noiseAScale = material.GetFloat(NoiseAScaleProperty);
            if (material.HasProperty(NoiseBScaleProperty))
                settings.noiseBScale = material.GetFloat(NoiseBScaleProperty);
            if (material.HasProperty(NoiseASpeedProperty))
                settings.noiseASpeed = material.GetFloat(NoiseASpeedProperty);
            if (material.HasProperty(NoiseBSpeedProperty))
                settings.noiseBSpeed = material.GetFloat(NoiseBSpeedProperty);
            if (material.HasProperty(ExtraNoiseProperty))
                settings.extraNoise = material.GetFloat(ExtraNoiseProperty) > 0.5f;
            if (material.HasProperty(ExtraNoiseDensityProperty))
                settings.extraNoiseDensity = material.GetFloat(ExtraNoiseDensityProperty);
            if (material.HasProperty(UseWindProperty))
                settings.useWind = material.GetFloat(UseWindProperty) > 0.5f;
            if (material.HasProperty(WindSpeedProperty))
                settings.windSpeed = material.GetFloat(WindSpeedProperty);
            if (material.HasProperty(WindVisibilityProperty))
                settings.windVisibility = material.GetFloat(WindVisibilityProperty);
        }

        Light light = FindFirstBoundLight();
        if (light != null)
        {
            settings.lightColor = light.color;
            settings.lightIntensity = light.intensity;
            settings.lightRange = light.range;
            settings.spotAngle = light.spotAngle;
            settings.innerSpotAngle = light.innerSpotAngle;

            if (material == null)
                settings.color = light.color;

            settings.shape = light.type == LightType.Spot
                ? WKFakeVolumeShape.SpotCone
                : WKFakeVolumeShape.PointSphere;
        }
        else if (renderer != null)
        {
            settings.shape = GuessShapeFromName(renderer.name);
        }

        settings.Sanitize();
        ApplyNow();
    }

    [ContextMenu("Apply Now")]
    public void ApplyNow()
    {
        settings.Sanitize();
        InitializeRuntimeOffsets();
        CaptureBasePoseIfNeeded(force: false);
        ResolveActiveTargets();

        float time = GetTime();
        float deltaTime = ResolveDeltaTime(time);
        float flicker = UpdateFlicker(time, deltaTime);
        Color volumeColor = ResolveVolumeColor(time);

        ApplyShapeState();
        ApplyRendererState(volumeColor, flicker);
        ApplyLightState(volumeColor, flicker);
        ApplyMotionState(time);
    }

    public void SetColor(Color color)
    {
        settings.color = color;
        ApplyNow();
    }

    public void SetOpacity(float opacity)
    {
        settings.opacity = Mathf.Clamp01(opacity);
        ApplyNow();
    }

    public void SetLightIntensity(float intensity)
    {
        settings.lightIntensity = Mathf.Max(0f, intensity);
        ApplyNow();
    }

#if UNITY_EDITOR
    public bool EnsureAuthoringVisuals(bool forceRebuild = false)
    {
        if (Application.isPlaying || !autoCreateVisuals)
            return false;

        WKFakeVolumeShape targetShape = ResolveAuthoringShape();
        if (generatedVisualRoot == null)
            generatedVisualRoot = FindGeneratedVisualRoot();

        bool hasGeneratedRoot = generatedVisualRoot != null;
        bool hasExistingVolumeRenderer = FindFirstBoundRenderer() != null;
        if (!forceRebuild && !hasGeneratedRoot && hasExistingVolumeRenderer)
            return false;

        bool needsRebuild = forceRebuild
            || generatedVisualRoot == null
            || generatedVisualShape != targetShape
            || !GeneratedRootHasRenderer()
            || GeneratedRootHasLight() != createGeneratedLight;

        if (needsRebuild)
            RebuildGeneratedVisuals(targetShape);

        BindGeneratedVisuals(targetShape);
        settings.shape = targetShape;
        settings.Sanitize();
        CaptureBasePoseIfNeeded(force: true);
        ApplyNow();
        EditorUtility.SetDirty(this);

        return needsRebuild;
    }

    public bool HasGeneratedAuthoringVisuals()
    {
        return generatedVisualRoot != null;
    }
#endif

#if UNITY_EDITOR
    private WKFakeVolumeShape ResolveAuthoringShape()
    {
        if (settings.shape != WKFakeVolumeShape.Existing)
            return settings.shape;

        if (generatedVisualShape != WKFakeVolumeShape.Existing)
            return generatedVisualShape;

        return WKFakeVolumeShape.SpotCone;
    }

    private GameObject FindGeneratedVisualRoot()
    {
        Transform generatedRootTransform = transform.Find(GeneratedRootName);
        return generatedRootTransform != null ? generatedRootTransform.gameObject : null;
    }

    private bool GeneratedRootHasRenderer()
    {
        if (generatedVisualRoot == null)
            return false;

        return generatedVisualRoot.GetComponentInChildren<Renderer>(includeInactiveChildren) != null;
    }

    private bool GeneratedRootHasLight()
    {
        if (generatedVisualRoot == null)
            return false;

        return generatedVisualRoot.GetComponentInChildren<Light>(includeInactiveChildren) != null;
    }

    private void RebuildGeneratedVisuals(WKFakeVolumeShape targetShape)
    {
        if (generatedVisualRoot != null)
            Undo.DestroyObjectImmediate(generatedVisualRoot);

        generatedVisualRoot = CreateAuthoringChild(GeneratedRootName, transform);
        generatedVisualRoot.transform.localPosition = Vector3.zero;
        generatedVisualRoot.transform.localRotation = Quaternion.identity;
        generatedVisualRoot.transform.localScale = GetDefaultGeneratedRootScale(targetShape);
        generatedVisualShape = targetShape;

        switch (targetShape)
        {
            case WKFakeVolumeShape.PointSphere:
                BuildPointSphereVisuals(generatedVisualRoot.transform);
                break;
            case WKFakeVolumeShape.Box:
                BuildBoxVisuals(generatedVisualRoot.transform);
                break;
            case WKFakeVolumeShape.Cylinder:
                BuildCylinderVisuals(generatedVisualRoot.transform);
                break;
            case WKFakeVolumeShape.Cross:
                BuildCrossVisuals(generatedVisualRoot.transform);
                break;
            case WKFakeVolumeShape.SpotCone:
            default:
                BuildSpotConeVisuals(generatedVisualRoot.transform);
                break;
        }
    }

    private void BindGeneratedVisuals(WKFakeVolumeShape targetShape)
    {
        if (generatedVisualRoot == null)
            return;

        Renderer[] generatedRenderers = generatedVisualRoot.GetComponentsInChildren<Renderer>(includeInactiveChildren);
        Light[] generatedLights = generatedVisualRoot.GetComponentsInChildren<Light>(includeInactiveChildren);
        Transform generatedTransform = generatedVisualRoot.transform;

        volumeRenderers = generatedRenderers;
        controlledLights = generatedLights;
        motionRoot = generatedTransform;
        shapeBindings = new[]
        {
            new ShapeBinding
            {
                shape = targetShape,
                root = generatedVisualRoot,
                renderers = generatedRenderers,
                lights = generatedLights,
                motionRoot = generatedTransform
            }
        };
    }

    private void BuildSpotConeVisuals(Transform root)
    {
        GameObject cone = CreateAuthoringChild("Volume Cone", root);
        ConfigureMeshRenderer(
            cone,
            LoadMeshAsset(ConeMeshPath),
            LoadMaterialAsset(ConeMaterialPath));

        if (createGeneratedLight)
        {
            settings.driveLights = true;
            settings.lightsEnabled = true;

            GameObject lightObject = CreateAuthoringChild("Spot Light", root);
            lightObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Light light = ConfigureGeneratedLight(lightObject, LightType.Spot);
            light.cookie = LoadTextureAsset(CookieTexturePath);
            light.cookieSize2D = new Vector2(10f, 10f);
        }
    }

    private void BuildPointSphereVisuals(Transform root)
    {
        Mesh sphereMesh = LoadBuiltinMesh("Sphere.fbx");

        GameObject outer = CreateAuthoringChild("Sphere Outer", root);
        ConfigureMeshRenderer(
            outer,
            sphereMesh,
            LoadMaterialAsset(PointOuterMaterialPath));

        GameObject inner = CreateAuthoringChild("Sphere Inner", root);
        inner.transform.localScale = Vector3.one * 0.67473f;
        ConfigureMeshRenderer(
            inner,
            sphereMesh,
            LoadMaterialAsset(PointInnerMaterialPath));

        if (createGeneratedLight)
        {
            settings.driveLights = true;
            settings.lightsEnabled = true;

            GameObject lightObject = CreateAuthoringChild("Point Light", root);
            ConfigureGeneratedLight(lightObject, LightType.Point);
        }
    }

    private void BuildBoxVisuals(Transform root)
    {
        GameObject box = CreateAuthoringChild("Volume Box", root);
        ConfigureMeshRenderer(
            box,
            LoadBuiltinMesh("Cube.fbx"),
            LoadMaterialAsset(ConeMaterialPath));

        if (createGeneratedLight)
        {
            settings.driveLights = true;
            settings.lightsEnabled = true;

            GameObject lightObject = CreateAuthoringChild("Point Light", root);
            ConfigureGeneratedLight(lightObject, LightType.Point);
        }
    }

    private void BuildCylinderVisuals(Transform root)
    {
        GameObject cylinder = CreateAuthoringChild("Volume Cylinder", root);
        ConfigureMeshRenderer(
            cylinder,
            LoadBuiltinMesh("Cylinder.fbx"),
            LoadMaterialAsset(ConeMaterialPath));

        if (createGeneratedLight)
        {
            settings.driveLights = true;
            settings.lightsEnabled = true;

            GameObject lightObject = CreateAuthoringChild("Point Light", root);
            ConfigureGeneratedLight(lightObject, LightType.Point);
        }
    }

    private void BuildCrossVisuals(Transform root)
    {
        Mesh planeMesh = LoadBuiltinMesh("Plane.fbx");
        Material material = LoadMaterialAsset(ConeMaterialPath);

        GameObject planeA = CreateAuthoringChild("Cross Plane A", root);
        planeA.transform.localRotation = Quaternion.Euler(90f, 0f, -90f);
        planeA.transform.localScale = new Vector3(0.1f, 0.1f, 0.5f);
        ConfigureMeshRenderer(planeA, planeMesh, material);

        GameObject planeB = CreateAuthoringChild("Cross Plane B", root);
        planeB.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        planeB.transform.localScale = new Vector3(0.1f, 0.1f, 0.5f);
        ConfigureMeshRenderer(planeB, planeMesh, material);

        if (createGeneratedLight)
        {
            settings.driveLights = true;
            settings.lightsEnabled = true;

            GameObject lightObject = CreateAuthoringChild("Point Light", root);
            ConfigureGeneratedLight(lightObject, LightType.Point);
        }
    }

    private GameObject CreateAuthoringChild(string childName, Transform parent)
    {
        GameObject child = new GameObject(childName);
        Undo.RegisterCreatedObjectUndo(child, "Create WK Fake Volume Visuals");
        child.layer = gameObject.layer;
        child.transform.SetParent(parent, false);
        return child;
    }

    private MeshRenderer ConfigureMeshRenderer(GameObject target, Mesh mesh, Material material)
    {
        MeshFilter meshFilter = target.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = target.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = true;
        return meshRenderer;
    }

    private Light ConfigureGeneratedLight(GameObject target, LightType lightType)
    {
        Light light = target.AddComponent<Light>();
        light.type = lightType;
        light.color = settings.syncLightColor ? settings.color : settings.lightColor;
        light.intensity = settings.lightIntensity;
        light.range = settings.lightRange;
        light.spotAngle = settings.spotAngle;
        light.innerSpotAngle = Mathf.Clamp(settings.innerSpotAngle, 0f, settings.spotAngle);
        light.shadows = LightShadows.None;
        light.bounceIntensity = 0f;
        return light;
    }

    private static Vector3 GetDefaultGeneratedRootScale(WKFakeVolumeShape shape)
    {
        return shape == WKFakeVolumeShape.PointSphere ? Vector3.one * 2f : Vector3.one;
    }

    private static Mesh LoadBuiltinMesh(string meshName)
    {
        return Resources.GetBuiltinResource<Mesh>(meshName);
    }

    private static Mesh LoadMeshAsset(string path)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null)
            return mesh;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Mesh candidate)
                return candidate;
        }

        return null;
    }

    private static Material LoadMaterialAsset(string path)
    {
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    private static Texture LoadTextureAsset(string path)
    {
        return AssetDatabase.LoadAssetAtPath<Texture>(path);
    }
#endif

    private ShapeBinding[] BuildSingleShapeBinding()
    {
        WKFakeVolumeShape guessedShape = settings.shape;
        if (guessedShape == WKFakeVolumeShape.Existing)
        {
            Renderer renderer = volumeRenderers != null && volumeRenderers.Length > 0 ? volumeRenderers[0] : null;
            Light light = controlledLights != null && controlledLights.Length > 0 ? controlledLights[0] : null;
            if (light != null)
                guessedShape = light.type == LightType.Spot ? WKFakeVolumeShape.SpotCone : WKFakeVolumeShape.PointSphere;
            else if (renderer != null)
                guessedShape = GuessShapeFromName(renderer.name);
        }

        return new[]
        {
            new ShapeBinding
            {
                shape = guessedShape,
                root = gameObject,
                renderers = volumeRenderers,
                lights = controlledLights,
                motionRoot = motionRoot
            }
        };
    }

    private void ResolveActiveTargets()
    {
        activeRenderers.Clear();
        activeLights.Clear();

        ShapeBinding binding = FindSelectedShapeBinding();
        if (binding != null)
        {
            AddRenderers(binding.renderers, activeRenderers);
            AddLights(binding.lights, activeLights);
        }

        if (activeRenderers.Count == 0)
            AddRenderers(volumeRenderers, activeRenderers);

        if (activeLights.Count == 0)
            AddLights(controlledLights, activeLights);
    }

    private ShapeBinding FindSelectedShapeBinding()
    {
        if (shapeBindings == null || shapeBindings.Length == 0 || settings.shape == WKFakeVolumeShape.Existing)
            return null;

        for (int i = 0; i < shapeBindings.Length; i++)
        {
            ShapeBinding binding = shapeBindings[i];
            if (binding != null && binding.shape == settings.shape)
                return binding;
        }

        return null;
    }

    private void ApplyShapeState()
    {
        if (shapeBindings == null || shapeBindings.Length == 0 || settings.shape == WKFakeVolumeShape.Existing)
            return;

        for (int i = 0; i < shapeBindings.Length; i++)
        {
            ShapeBinding binding = shapeBindings[i];
            if (binding == null || binding.root == null)
                continue;

            bool shouldBeActive = binding.shape == settings.shape;
            if (binding.root.activeSelf != shouldBeActive)
                binding.root.SetActive(shouldBeActive);
        }
    }

    private void ApplyRendererState(Color volumeColor, float flicker)
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        float opacity = Mathf.Clamp01(settings.opacity + flicker * settings.flickerOpacityAmount);
        float power = Mathf.Max(0f, settings.power + flicker * settings.flickerPowerAmount);

        for (int i = 0; i < activeRenderers.Count; i++)
        {
            Renderer renderer = activeRenderers[i];
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(ColorProperty, volumeColor);
            propertyBlock.SetFloat(OpacityProperty, opacity);
            propertyBlock.SetFloat(PowerProperty, power);
            propertyBlock.SetFloat(EdgeProperty, settings.edge);
            propertyBlock.SetFloat(FadeDistanceProperty, settings.fadeDistance);
            propertyBlock.SetFloat(ExtraSoftnessProperty, settings.extraSoftness);

            if (settings.driveNoise)
            {
                bool normalizeLegacyConeWindScale = settings.useWind && UsesConeVolumeShader(renderer);
                propertyBlock.SetFloat(NoiseAScaleProperty, ResolveConeWindNoiseScale(settings.noiseAScale, 1000f, normalizeLegacyConeWindScale));
                propertyBlock.SetFloat(NoiseBScaleProperty, ResolveConeWindNoiseScale(settings.noiseBScale, 1500f, normalizeLegacyConeWindScale));
                propertyBlock.SetVector(NoiseASizeProperty, ToVector4(settings.noiseASize));
                propertyBlock.SetVector(NoiseBSizeProperty, ToVector4(settings.noiseBSize));
                propertyBlock.SetFloat(NoiseASpeedProperty, settings.noiseASpeed);
                propertyBlock.SetFloat(NoiseBSpeedProperty, settings.noiseBSpeed);
                propertyBlock.SetFloat(ExtraNoiseProperty, settings.extraNoise ? 1f : 0f);
                propertyBlock.SetFloat(ExtraNoiseDensityProperty, settings.extraNoiseDensity);
                propertyBlock.SetVector(ExtraNoiseSizeProperty, ToVector4(settings.extraNoiseSize));
            }

            propertyBlock.SetFloat(UseWindProperty, settings.useWind ? 1f : 0f);
            propertyBlock.SetFloat(WindSpeedProperty, settings.windSpeed);
            propertyBlock.SetFloat(WindVisibilityProperty, settings.windVisibility);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    private void ApplyLightState(Color volumeColor, float flicker)
    {
        if (!settings.driveLights)
            return;

        float intensity = Mathf.Max(0f, settings.lightIntensity + flicker * settings.flickerLightIntensityAmount);
        for (int i = 0; i < activeLights.Count; i++)
        {
            Light targetLight = activeLights[i];
            if (targetLight == null)
                continue;

            if (targetLight.enabled != settings.lightsEnabled)
                targetLight.enabled = settings.lightsEnabled;

            if (settings.driveLightTypeFromShape)
                ApplyLightTypeFromShape(targetLight);

            if (settings.syncLightColor)
                targetLight.color = volumeColor;
            else
                targetLight.color = settings.lightColor;

            targetLight.intensity = intensity;
            targetLight.range = settings.lightRange;

            if (targetLight.type == LightType.Spot)
            {
                targetLight.spotAngle = settings.spotAngle;
                targetLight.innerSpotAngle = Mathf.Clamp(settings.innerSpotAngle, 0f, targetLight.spotAngle);
            }
        }
    }

    private void ApplyLightTypeFromShape(Light targetLight)
    {
        switch (settings.shape)
        {
            case WKFakeVolumeShape.PointSphere:
            case WKFakeVolumeShape.Box:
            case WKFakeVolumeShape.Cylinder:
            case WKFakeVolumeShape.Cross:
                targetLight.type = LightType.Point;
                break;
            case WKFakeVolumeShape.SpotCone:
                targetLight.type = LightType.Spot;
                break;
        }
    }

    private void ApplyMotionState(float time)
    {
        Transform targetMotionRoot = ResolveMotionRoot();
        if (targetMotionRoot == null)
            return;

        float t = (time + runtimeSeed) * settings.motionSpeed;
        switch (settings.motionMode)
        {
            case WKFakeVolumeMotionMode.None:
                targetMotionRoot.localPosition = baseMotionLocalPosition;
                targetMotionRoot.localRotation = baseMotionLocalRotation;
                break;
            case WKFakeVolumeMotionMode.Swing:
                float angleX = Mathf.Sin(t * Mathf.Max(0f, settings.swingAxes.x)) * settings.swingAngle;
                float angleY = Mathf.Cos(t * Mathf.Max(0f, settings.swingAxes.y)) * settings.swingAngle;
                float angleZ = Mathf.Sin(t * Mathf.Max(0f, settings.swingAxes.z)) * settings.swingAngle;
                targetMotionRoot.localRotation = baseMotionLocalRotation * Quaternion.Euler(angleX, angleY, angleZ);
                break;
            case WKFakeVolumeMotionMode.Rotate:
                Vector3 rotateAxis = settings.rotateAxis.sqrMagnitude > 0.0001f ? settings.rotateAxis.normalized : Vector3.up;
                float angle = time * settings.rotateDegreesPerSecond * settings.motionSpeed;
                targetMotionRoot.localRotation = baseMotionLocalRotation * Quaternion.AngleAxis(angle, rotateAxis);
                break;
            case WKFakeVolumeMotionMode.Drift:
                float driftX = Mathf.Sin(t) * settings.driftAmplitude.x;
                float driftY = Mathf.Sin(t * 1.31f + 2.4f) * settings.driftAmplitude.y;
                float driftZ = Mathf.Cos(t * 0.87f + 1.1f) * settings.driftAmplitude.z;
                targetMotionRoot.localPosition = baseMotionLocalPosition + new Vector3(driftX, driftY, driftZ);
                break;
        }
    }

    private Color ResolveVolumeColor(float time)
    {
        Color resolvedColor = settings.color;
        if (settings.colorMode == WKFakeVolumeColorMode.HueCycle)
        {
            float hue = Mathf.Repeat(time * settings.colorCycleSpeed + colorOffset, 1f);
            resolvedColor = Color.HSVToRGB(hue, settings.colorSaturation, settings.colorValue);
            resolvedColor.a = settings.color.a;
        }

        return new Color(
            resolvedColor.r * settings.colorIntensity,
            resolvedColor.g * settings.colorIntensity,
            resolvedColor.b * settings.colorIntensity,
            resolvedColor.a);
    }

    private float UpdateFlicker(float time, float deltaTime)
    {
        if (!settings.enableFlicker)
        {
            currentFlicker = 0f;
            return currentFlicker;
        }

        float targetFlicker = (Mathf.PerlinNoise(runtimeSeed, time * settings.flickerSpeed) - 0.5f) * 2f;
        float response = Application.isPlaying
            ? 1f - Mathf.Exp(-settings.flickerResponse * Mathf.Max(0f, deltaTime))
            : 1f;
        currentFlicker = Mathf.Lerp(currentFlicker, targetFlicker, response);
        return currentFlicker;
    }

    private float ResolveDeltaTime(float time)
    {
        if (!hasPreviousTime)
        {
            previousTime = time;
            hasPreviousTime = true;
            return 0f;
        }

        float deltaTime = Mathf.Max(0f, time - previousTime);
        previousTime = time;
        return deltaTime;
    }

    private float GetTime()
    {
        if (Application.isPlaying)
            return settings.updateMode == WKFakeVolumeUpdateMode.UnscaledTime ? Time.unscaledTime : Time.time;

        return Time.realtimeSinceStartup;
    }

    private void InitializeRuntimeOffsets()
    {
        if (hasRuntimeSeed)
            return;

        runtimeSeed = settings.randomizeSeed ? UnityEngine.Random.Range(0f, 1000f) : settings.seed;
        colorOffset = settings.randomizeSeed ? UnityEngine.Random.Range(0f, 1f) : Mathf.Repeat(settings.seed * 0.0137f, 1f);
        hasRuntimeSeed = true;
    }

    private void CaptureBasePoseIfNeeded(bool force)
    {
        Transform resolvedMotionRoot = ResolveMotionRoot();
        if (resolvedMotionRoot != null && (force || !hasCapturedMotionPose || cachedMotionRoot != resolvedMotionRoot))
        {
            cachedMotionRoot = resolvedMotionRoot;
            baseMotionLocalPosition = resolvedMotionRoot.localPosition;
            baseMotionLocalRotation = resolvedMotionRoot.localRotation;
            hasCapturedMotionPose = true;
        }
    }

    private Transform ResolveMotionRoot()
    {
        ShapeBinding binding = FindSelectedShapeBinding();
        if (binding != null && binding.motionRoot != null)
            return binding.motionRoot;

        return motionRoot != null ? motionRoot : transform;
    }

    private void DisablePackageAnimators()
    {
        Behaviour[] behaviours = GetComponentsInChildren<Behaviour>(includeInactiveChildren);
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour == this)
                continue;

            string fullName = behaviour.GetType().FullName;
            if (fullName == "MonoLimboStudio.LightFlicker"
                || fullName == "MonoLimboStudio.PartyLightController"
                || fullName == "MonoLimboStudio.PartyLightSwing")
            {
                behaviour.enabled = false;
            }
        }
    }

    private Renderer FindFirstBoundRenderer()
    {
        if (volumeRenderers != null)
        {
            for (int i = 0; i < volumeRenderers.Length; i++)
            {
                if (volumeRenderers[i] != null)
                    return volumeRenderers[i];
            }
        }

        Renderer[] childRenderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);
        for (int i = 0; i < childRenderers.Length; i++)
        {
            if (LooksLikeVolumeRenderer(childRenderers[i]))
                return childRenderers[i];
        }

        return null;
    }

    private Light FindFirstBoundLight()
    {
        if (controlledLights != null)
        {
            for (int i = 0; i < controlledLights.Length; i++)
            {
                if (controlledLights[i] != null)
                    return controlledLights[i];
            }
        }

        return GetComponentInChildren<Light>(includeInactiveChildren);
    }

    private static Material FindFirstVolumeMaterial(Renderer renderer)
    {
        if (renderer == null)
            return null;

        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material != null && IsVolumeMaterial(material))
                return material;
        }

        return null;
    }

    private static bool LooksLikeVolumeRenderer(Renderer renderer)
    {
        return FindFirstVolumeMaterial(renderer) != null;
    }

    private static bool UsesConeVolumeShader(Renderer renderer)
    {
        Material material = FindFirstVolumeMaterial(renderer);
        return material != null && material.HasProperty(DirectionProperty);
    }

    private static bool IsVolumeMaterial(Material material)
    {
        return material.HasProperty(OpacityProperty)
            || material.HasProperty(NoiseASpeedProperty)
            || material.HasProperty(WindSpeedProperty);
    }

    private static void AddRenderers(Renderer[] renderers, List<Renderer> output)
    {
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null && !output.Contains(renderer))
                output.Add(renderer);
        }
    }

    private static void AddLights(Light[] lights, List<Light> output)
    {
        if (lights == null)
            return;

        for (int i = 0; i < lights.Length; i++)
        {
            Light targetLight = lights[i];
            if (targetLight != null && !output.Contains(targetLight))
                output.Add(targetLight);
        }
    }

    private static WKFakeVolumeShape GuessShapeFromName(string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
            return WKFakeVolumeShape.Existing;

        string lowerName = targetName.ToLowerInvariant();
        if (lowerName.Contains("cone") || lowerName.Contains("spot"))
            return WKFakeVolumeShape.SpotCone;
        if (lowerName.Contains("cylinder"))
            return WKFakeVolumeShape.Cylinder;
        if (lowerName.Contains("cross") || lowerName.Contains("plane"))
            return WKFakeVolumeShape.Cross;
        if (lowerName.Contains("box") || lowerName.Contains("cube"))
            return WKFakeVolumeShape.Box;
        if (lowerName.Contains("sphere") || lowerName.Contains("point") || lowerName.Contains("vp"))
            return WKFakeVolumeShape.PointSphere;

        return WKFakeVolumeShape.Existing;
    }

    private static Vector4 ToVector4(Vector2 value)
    {
        return new Vector4(value.x, value.y, 0f, 0f);
    }

    private static float ResolveConeWindNoiseScale(float scale, float legacyMultiplier, bool normalizeLegacyConeWindScale)
    {
        if (!normalizeLegacyConeWindScale || scale <= 0f || scale >= 1f)
            return scale;

        return scale * legacyMultiplier;
    }
}
