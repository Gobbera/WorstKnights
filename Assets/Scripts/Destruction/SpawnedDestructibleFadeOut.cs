using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class SpawnedDestructibleFadeOut : MonoBehaviour
{
    private const string FadeAlphaProperty = "_FadeAlpha";
    private const string AlphaClipProperty = "_AlphaClip";
    private const string CutoffProperty = "_Cutoff";
    private const string AlphaTestKeyword = "_ALPHATEST_ON";

    private readonly List<MaterialFadeState> materialStates = new List<MaterialFadeState>();

    private GameObject targetRoot;
    private float fadeStartTime;
    private float destroyTime;
    private bool materialsPrepared;

    public void Initialize(GameObject root, float lifetime, float fadeDuration)
    {
        targetRoot = root != null ? root : gameObject;

        float safeLifetime = Mathf.Max(0f, lifetime);
        float safeFadeDuration = Mathf.Min(Mathf.Max(0f, fadeDuration), safeLifetime);
        destroyTime = Time.time + safeLifetime;
        fadeStartTime = destroyTime - safeFadeDuration;
        materialsPrepared = false;
        materialStates.Clear();

        if (safeLifetime <= 0f)
            enabled = false;
    }

    private void Update()
    {
        float now = Time.time;
        if (now >= destroyTime)
        {
            Destroy(targetRoot != null ? targetRoot : gameObject);
            return;
        }

        if (now < fadeStartTime)
            return;

        if (!materialsPrepared)
            PrepareMaterials();

        float fadeDuration = Mathf.Max(0.0001f, destroyTime - fadeStartTime);
        float fadeProgress = Mathf.Clamp01((now - fadeStartTime) / fadeDuration);
        ApplyFade(1f - fadeProgress);
    }

    private void PrepareMaterials()
    {
        materialsPrepared = true;

        GameObject root = targetRoot != null ? targetRoot : gameObject;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null)
                continue;

            Material[] materials = renderer.materials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                    continue;

                if (material.HasProperty(FadeAlphaProperty))
                {
                    float initialFadeAlpha = ResolveInitialFadeAlpha(material);
                    material.SetFloat(FadeAlphaProperty, initialFadeAlpha);
                    ConfigureMaterialForShaderFade(material);
                    materialStates.Add(MaterialFadeState.ForFloat(
                        material,
                        FadeAlphaProperty,
                        initialFadeAlpha));
                    continue;
                }

                if (!TryGetColorProperty(material, out string colorProperty))
                    continue;

                ConfigureMaterialForTransparency(material);
                materialStates.Add(MaterialFadeState.ForColor(material, colorProperty, material.GetColor(colorProperty)));
            }
        }
    }

    private void ApplyFade(float alphaMultiplier)
    {
        for (int i = 0; i < materialStates.Count; i++)
        {
            MaterialFadeState state = materialStates[i];
            if (state.Material == null)
                continue;

            if (state.UsesFloat)
            {
                state.Material.SetFloat(state.PropertyName, state.InitialFloat * alphaMultiplier);
                continue;
            }

            Color color = state.InitialColor;
            color.a *= alphaMultiplier;
            state.Material.SetColor(state.PropertyName, color);
        }
    }

    private static bool TryGetColorProperty(Material material, out string colorProperty)
    {
        if (material.HasProperty("_Base_Color"))
        {
            colorProperty = "_Base_Color";
            return true;
        }

        if (material.HasProperty("_BaseColor"))
        {
            colorProperty = "_BaseColor";
            return true;
        }

        if (material.HasProperty("_Color"))
        {
            colorProperty = "_Color";
            return true;
        }

        colorProperty = string.Empty;
        return false;
    }

    private static float ResolveInitialFadeAlpha(Material material)
    {
        float value = material.GetFloat(FadeAlphaProperty);
        return value > 0.0001f ? Mathf.Clamp01(value) : 1f;
    }

    private static void ConfigureMaterialForShaderFade(Material material)
    {
        if (material.HasProperty(AlphaClipProperty))
            material.SetFloat(AlphaClipProperty, 1f);

        if (material.HasProperty(CutoffProperty))
            material.SetFloat(CutoffProperty, 0f);

        material.EnableKeyword(AlphaTestKeyword);
    }

    private static void ConfigureMaterialForTransparency(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);

        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);

        if (material.HasProperty("_AlphaClip"))
            material.SetFloat("_AlphaClip", 0f);

        if (material.HasProperty("_Mode"))
            material.SetFloat("_Mode", 2f);

        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);

        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private readonly struct MaterialFadeState
    {
        public readonly Material Material;
        public readonly string PropertyName;
        public readonly Color InitialColor;
        public readonly float InitialFloat;
        public readonly bool UsesFloat;

        private MaterialFadeState(
            Material material,
            string propertyName,
            Color initialColor,
            float initialFloat,
            bool usesFloat)
        {
            Material = material;
            PropertyName = propertyName;
            InitialColor = initialColor;
            InitialFloat = initialFloat;
            UsesFloat = usesFloat;
        }

        public static MaterialFadeState ForColor(Material material, string colorProperty, Color initialColor)
        {
            return new MaterialFadeState(material, colorProperty, initialColor, 1f, false);
        }

        public static MaterialFadeState ForFloat(Material material, string floatProperty, float initialFloat)
        {
            return new MaterialFadeState(material, floatProperty, default, initialFloat, true);
        }
    }
}
