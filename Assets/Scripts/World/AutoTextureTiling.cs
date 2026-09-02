using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("World/Rendering/Auto Texture Tiling")]
public sealed class AutoTextureTiling : MonoBehaviour
{
    public enum SurfacePlane
    {
        AutoLargestTwo,
        XY,
        XZ,
        ZY
    }

    private const string DefaultTextureTilingProperty = "_Texture_Tiling";
    private const string DefaultTextureOffsetProperty = "_Texture_Offset";
    private const string DefaultTextureRotationProperty = "_Texture_Rotation";
    private const float MinimumSize = 0.0001f;
    private const float ChangeThreshold = 0.000001f;
#if UNITY_EDITOR
    private const double PrefabStagePlayModeTransitionBlockSeconds = 2.0;
    private static double prefabStageRefreshBlockedUntil;
#endif

    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private bool includeChildRenderers = false;
    [SerializeField] private SurfacePlane surfacePlane = SurfacePlane.AutoLargestTwo;
    [SerializeField] private Vector2 textureWorldSize = Vector2.one;
    [SerializeField] private Vector2 tilingMultiplier = Vector2.one;
    [SerializeField] private Vector2 textureOffset = Vector2.zero;
    [SerializeField] private float textureRotationDegrees = 0f;
    [SerializeField] private string textureTilingProperty = DefaultTextureTilingProperty;
    [SerializeField] private string textureOffsetProperty = DefaultTextureOffsetProperty;
    [SerializeField] private string textureRotationProperty = DefaultTextureRotationProperty;
    [SerializeField] private bool updateInPlayMode = false;

    private readonly List<Renderer> renderers = new List<Renderer>();
    private MaterialPropertyBlock propertyBlock;
    private Vector2 lastAppliedTiling = new Vector2(float.NaN, float.NaN);
    private Vector2 lastAppliedOffset = new Vector2(float.NaN, float.NaN);
    private float lastAppliedRotationDegrees = float.NaN;
    private int lastAppliedTilingPropertyId;
    private int lastAppliedOffsetPropertyId;
    private int lastAppliedRotationPropertyId;
    private int lastAppliedRendererHash;
    private int lastAppliedRendererCount = -1;

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void RegisterEditorCallbacks()
    {
        EditorApplication.playModeStateChanged -= HandleEditorPlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandleEditorPlayModeStateChanged;
    }

    private static void HandleEditorPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode ||
            state == PlayModeStateChange.ExitingPlayMode ||
            state == PlayModeStateChange.EnteredEditMode)
        {
            prefabStageRefreshBlockedUntil =
                EditorApplication.timeSinceStartup + PrefabStagePlayModeTransitionBlockSeconds;
        }
    }
#endif

    private void Reset()
    {
        targetRenderer = GetSupportedRendererOnThisObject();
        textureWorldSize = Vector2.one;
        tilingMultiplier = Vector2.one;
        textureOffset = Vector2.zero;
        textureRotationDegrees = 0f;
        textureOffsetProperty = DefaultTextureOffsetProperty;
        textureRotationProperty = DefaultTextureRotationProperty;
        RefreshTiling();
    }

    private void OnEnable()
    {
        RefreshTiling();
    }

    private void OnValidate()
    {
        SanitizeInputs();
        RefreshTiling();
    }

    private void Update()
    {
        if (!Application.isPlaying || updateInPlayMode)
            RefreshTiling();
    }

    [ContextMenu("Refresh Tiling")]
    public void RefreshTiling()
    {
        SanitizeInputs();

#if UNITY_EDITOR
        if (ShouldSkipPrefabStageRefreshDuringModeSwitch())
            return;
#endif

        CollectRenderers();

        if (renderers.Count == 0)
            return;

        Vector3 projectedSize;
        if (!TryGetProjectedSize(out projectedSize))
            return;

        Vector2 tiling = CalculateTiling(projectedSize);
        if (!IsFinite(tiling))
            return;

        ApplyTextureTransform(tiling, textureOffset, textureRotationDegrees);
    }

    private void CollectRenderers()
    {
        renderers.Clear();

        if (targetRenderer != null)
        {
            TryAddRenderer(targetRenderer);
            return;
        }

        if (includeChildRenderers)
        {
            List<Renderer> childRenderers = new List<Renderer>();
            GetComponentsInChildren(true, childRenderers);

            for (int i = 0; i < childRenderers.Count; i++)
                TryAddRenderer(childRenderers[i]);

            return;
        }

        Renderer rendererOnThisObject = GetSupportedRendererOnThisObject();
        if (rendererOnThisObject != null)
            TryAddRenderer(rendererOnThisObject);
    }

    private Renderer GetSupportedRendererOnThisObject()
    {
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            return meshRenderer;

        return GetComponent<SkinnedMeshRenderer>();
    }

    private void TryAddRenderer(Renderer renderer)
    {
        if (!IsSupportedRenderer(renderer))
            return;

        if (!RendererUsesTilingProperty(renderer))
            return;

        renderers.Add(renderer);
    }

    private bool IsSupportedRenderer(Renderer renderer)
    {
        return renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
    }

    private bool RendererUsesTilingProperty(Renderer renderer)
    {
        if (renderer == null)
            return false;

        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material != null && material.HasProperty(textureTilingProperty))
                return true;
        }

        return false;
    }

    private bool TryGetProjectedSize(out Vector3 projectedSize)
    {
        projectedSize = Vector3.zero;

        Vector3 origin = transform.position;
        Vector3 axisX = GetSafeAxis(transform.right, Vector3.right);
        Vector3 axisY = GetSafeAxis(transform.up, Vector3.up);
        Vector3 axisZ = GetSafeAxis(transform.forward, Vector3.forward);

        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.zero;
        bool hasPoint = false;

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!TryEncapsulateRenderer(renderer, origin, axisX, axisY, axisZ, ref min, ref max, ref hasPoint))
                continue;
        }

        if (!hasPoint)
            return false;

        projectedSize = max - min;
        projectedSize.x = Mathf.Abs(projectedSize.x);
        projectedSize.y = Mathf.Abs(projectedSize.y);
        projectedSize.z = Mathf.Abs(projectedSize.z);
        return IsFinite(projectedSize);
    }

    private bool TryEncapsulateRenderer(
        Renderer renderer,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        ref Vector3 min,
        ref Vector3 max,
        ref bool hasPoint)
    {
        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            return TryEncapsulateLocalBounds(
                meshFilter.transform,
                meshFilter.sharedMesh.bounds,
                origin,
                axisX,
                axisY,
                axisZ,
                ref min,
                ref max,
                ref hasPoint);
        }

        SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
        if (skinnedMeshRenderer != null)
        {
            return TryEncapsulateLocalBounds(
                skinnedMeshRenderer.transform,
                skinnedMeshRenderer.localBounds,
                origin,
                axisX,
                axisY,
                axisZ,
                ref min,
                ref max,
                ref hasPoint);
        }

        return TryEncapsulateWorldBounds(
            renderer.bounds,
            origin,
            axisX,
            axisY,
            axisZ,
            ref min,
            ref max,
            ref hasPoint);
    }

    private bool TryEncapsulateLocalBounds(
        Transform localToWorld,
        Bounds bounds,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        ref Vector3 min,
        ref Vector3 max,
        ref bool hasPoint)
    {
        if (!IsFinite(bounds.center) || !IsFinite(bounds.extents))
            return false;

        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        bool addedPoint = false;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 localPoint = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 worldPoint = localToWorld.TransformPoint(localPoint);
                    addedPoint |= EncapsulateProjectedPoint(worldPoint, origin, axisX, axisY, axisZ, ref min, ref max, ref hasPoint);
                }
            }
        }

        return addedPoint;
    }

    private bool TryEncapsulateWorldBounds(
        Bounds bounds,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        ref Vector3 min,
        ref Vector3 max,
        ref bool hasPoint)
    {
        if (!IsFinite(bounds.center) || !IsFinite(bounds.extents))
            return false;

        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        bool addedPoint = false;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 worldPoint = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    addedPoint |= EncapsulateProjectedPoint(worldPoint, origin, axisX, axisY, axisZ, ref min, ref max, ref hasPoint);
                }
            }
        }

        return addedPoint;
    }

    private static bool EncapsulateProjectedPoint(
        Vector3 worldPoint,
        Vector3 origin,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        ref Vector3 min,
        ref Vector3 max,
        ref bool hasPoint)
    {
        if (!IsFinite(worldPoint))
            return false;

        Vector3 offset = worldPoint - origin;
        Vector3 projectedPoint = new Vector3(
            Vector3.Dot(offset, axisX),
            Vector3.Dot(offset, axisY),
            Vector3.Dot(offset, axisZ));

        if (!IsFinite(projectedPoint))
            return false;

        if (!hasPoint)
        {
            min = projectedPoint;
            max = projectedPoint;
            hasPoint = true;
        }
        else
        {
            min = Vector3.Min(min, projectedPoint);
            max = Vector3.Max(max, projectedPoint);
        }

        return true;
    }

    private Vector2 CalculateTiling(Vector3 projectedSize)
    {
        Vector2 surfaceSize = SelectSurfaceSize(projectedSize);
        float textureWidth = Mathf.Max(MinimumSize, Mathf.Abs(textureWorldSize.x));
        float textureHeight = Mathf.Max(MinimumSize, Mathf.Abs(textureWorldSize.y));

        return new Vector2(
            Mathf.Max(MinimumSize, surfaceSize.x / textureWidth) * tilingMultiplier.x,
            Mathf.Max(MinimumSize, surfaceSize.y / textureHeight) * tilingMultiplier.y);
    }

    private Vector2 SelectSurfaceSize(Vector3 projectedSize)
    {
        SurfacePlane resolvedPlane = surfacePlane;

        if (resolvedPlane == SurfacePlane.AutoLargestTwo)
        {
            if (projectedSize.x <= projectedSize.y && projectedSize.x <= projectedSize.z)
                resolvedPlane = SurfacePlane.ZY;
            else if (projectedSize.y <= projectedSize.x && projectedSize.y <= projectedSize.z)
                resolvedPlane = SurfacePlane.XZ;
            else
                resolvedPlane = SurfacePlane.XY;
        }

        switch (resolvedPlane)
        {
            case SurfacePlane.XZ:
                return new Vector2(projectedSize.x, projectedSize.z);
            case SurfacePlane.ZY:
                return new Vector2(projectedSize.z, projectedSize.y);
            default:
                return new Vector2(projectedSize.x, projectedSize.y);
        }
    }

    private void ApplyTextureTransform(Vector2 tiling, Vector2 offset, float rotationDegrees)
    {
        int tilingPropertyId = Shader.PropertyToID(textureTilingProperty);
        int offsetPropertyId = Shader.PropertyToID(textureOffsetProperty);
        int rotationPropertyId = Shader.PropertyToID(textureRotationProperty);
        int rendererHash = CalculateRendererHash();
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        if (lastAppliedTilingPropertyId == tilingPropertyId &&
            lastAppliedOffsetPropertyId == offsetPropertyId &&
            lastAppliedRotationPropertyId == rotationPropertyId &&
            lastAppliedRendererHash == rendererHash &&
            lastAppliedRendererCount == renderers.Count &&
            (lastAppliedTiling - tiling).sqrMagnitude < ChangeThreshold &&
            (lastAppliedOffset - offset).sqrMagnitude < ChangeThreshold &&
            Mathf.Abs(lastAppliedRotationDegrees - rotationDegrees) < ChangeThreshold)
        {
            return;
        }

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetVector(tilingPropertyId, new Vector4(tiling.x, tiling.y, 0f, 0f));
            propertyBlock.SetVector(offsetPropertyId, new Vector4(offset.x, offset.y, 0f, 0f));
            propertyBlock.SetFloat(rotationPropertyId, rotationDegrees);
            renderer.SetPropertyBlock(propertyBlock);
        }

        lastAppliedTilingPropertyId = tilingPropertyId;
        lastAppliedOffsetPropertyId = offsetPropertyId;
        lastAppliedRotationPropertyId = rotationPropertyId;
        lastAppliedRendererHash = rendererHash;
        lastAppliedRendererCount = renderers.Count;
        lastAppliedTiling = tiling;
        lastAppliedOffset = offset;
        lastAppliedRotationDegrees = rotationDegrees;
    }

#if UNITY_EDITOR
    private bool ShouldSkipPrefabStageRefreshDuringModeSwitch()
    {
        if (Application.isPlaying)
            return false;

        if (PrefabStageUtility.GetPrefabStage(gameObject) == null)
            return false;

        return EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.timeSinceStartup < prefabStageRefreshBlockedUntil;
    }
#endif

    private int CalculateRendererHash()
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                hash = hash * 31 + (renderer != null ? renderer.GetHashCode() : 0);
            }

            return hash;
        }
    }

    private void SanitizeInputs()
    {
        textureWorldSize.x = SanitizePositive(textureWorldSize.x, 1f);
        textureWorldSize.y = SanitizePositive(textureWorldSize.y, 1f);
        tilingMultiplier.x = SanitizePositive(tilingMultiplier.x, 1f);
        tilingMultiplier.y = SanitizePositive(tilingMultiplier.y, 1f);
        textureOffset.x = SanitizeFinite(textureOffset.x, 0f);
        textureOffset.y = SanitizeFinite(textureOffset.y, 0f);
        textureRotationDegrees = SanitizeFinite(textureRotationDegrees, 0f);

        if (string.IsNullOrWhiteSpace(textureTilingProperty))
            textureTilingProperty = DefaultTextureTilingProperty;

        if (string.IsNullOrWhiteSpace(textureOffsetProperty))
            textureOffsetProperty = DefaultTextureOffsetProperty;

        if (string.IsNullOrWhiteSpace(textureRotationProperty))
            textureRotationProperty = DefaultTextureRotationProperty;
    }

    private static float SanitizePositive(float value, float fallback)
    {
        if (!IsFinite(value))
            return fallback;

        return Mathf.Max(MinimumSize, value);
    }

    private static float SanitizeFinite(float value, float fallback)
    {
        return IsFinite(value) ? value : fallback;
    }

    private static Vector3 GetSafeAxis(Vector3 axis, Vector3 fallback)
    {
        if (!IsFinite(axis))
            return fallback;

        float length = axis.magnitude;
        if (length < MinimumSize || !IsFinite(length))
            return fallback;

        return axis / length;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector2 value)
    {
        return IsFinite(value.x) && IsFinite(value.y);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
