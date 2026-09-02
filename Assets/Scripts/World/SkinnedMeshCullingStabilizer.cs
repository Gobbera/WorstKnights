using UnityEngine;

[DisallowMultipleComponent]
public sealed class SkinnedMeshCullingStabilizer : MonoBehaviour
{
    [SerializeField] private bool applyOnAwake = true;
    [SerializeField] private bool includeInactive = true;
    [SerializeField] private bool updateWhenOffscreen = true;
    [SerializeField] private bool disableDynamicOcclusion = true;
    [SerializeField] private bool expandLocalBounds = true;
    [SerializeField] private Vector3 minimumLocalBoundsSize = new Vector3(3f, 3f, 3f);
    [SerializeField] private Vector3 fallbackLocalBoundsCenter = Vector3.zero;
    [SerializeField] private bool logAppliedRenderers;

    private void Awake()
    {
        if (applyOnAwake)
            Apply();
    }

    private void Reset()
    {
        Apply();
    }

    private void OnValidate()
    {
        minimumLocalBoundsSize = new Vector3(
            Mathf.Max(0f, minimumLocalBoundsSize.x),
            Mathf.Max(0f, minimumLocalBoundsSize.y),
            Mathf.Max(0f, minimumLocalBoundsSize.z));
    }

    [ContextMenu("Apply Skinned Mesh Culling Stabilizer")]
    public void Apply()
    {
        SkinnedMeshRenderer[] skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive);
        for (int i = 0; i < skinnedRenderers.Length; i++)
            Apply(skinnedRenderers[i]);

        if (logAppliedRenderers)
            Debug.Log($"SkinnedMeshCullingStabilizer: applied to {skinnedRenderers.Length} skinned renderers under {name}.", this);
    }

    private void Apply(SkinnedMeshRenderer skinnedRenderer)
    {
        if (skinnedRenderer == null)
            return;

        if (updateWhenOffscreen)
            skinnedRenderer.updateWhenOffscreen = true;

        if (disableDynamicOcclusion)
            skinnedRenderer.allowOcclusionWhenDynamic = false;

        if (expandLocalBounds)
            EnsureMinimumLocalBounds(skinnedRenderer);
    }

    private void EnsureMinimumLocalBounds(SkinnedMeshRenderer skinnedRenderer)
    {
        Bounds bounds = skinnedRenderer.localBounds;
        if (!IsFiniteBounds(bounds) || bounds.size.sqrMagnitude <= 0.000001f)
        {
            skinnedRenderer.localBounds = new Bounds(fallbackLocalBoundsCenter, minimumLocalBoundsSize);
            return;
        }

        Vector3 currentSize = bounds.size;
        Vector3 targetSize = Vector3.Max(currentSize, minimumLocalBoundsSize);
        if ((targetSize - currentSize).sqrMagnitude <= 0.000001f)
            return;

        bounds.size = targetSize;
        skinnedRenderer.localBounds = bounds;
    }

    private static bool IsFiniteBounds(Bounds bounds)
    {
        return IsFiniteVector(bounds.center) && IsFiniteVector(bounds.size);
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFiniteFloat(value.x) && IsFiniteFloat(value.y) && IsFiniteFloat(value.z);
    }

    private static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
