using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[DefaultExecutionOrder(80)]
[RequireComponent(typeof(Camera))]
public sealed class DynamicDepthOfFieldController : MonoBehaviour
{
    private const int FocusHitCapacity = 12;

    [Header("References")]
    [SerializeField] private Camera focusCamera;
    [SerializeField] private Volume targetVolume;

    [Header("Focus Targeting")]
    [SerializeField] private bool depthOfFieldEnabled = true;
    [SerializeField] private bool autoFocus = true;
    [SerializeField] private LayerMask focusLayerMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
    [SerializeField] [Min(0f)] private float focusProbeRadius = 0.08f;
    [SerializeField] [Min(0.01f)] private float minFocusDistance = 0.75f;
    [SerializeField] [Min(0.1f)] private float maxFocusDistance = 45f;
    [SerializeField] [Min(0.1f)] private float fallbackFocusDistance = 18f;
    [SerializeField] [Min(0f)] private float focusDeadZone = 0.04f;
    [SerializeField] private bool ignoreOwnHierarchy = true;

    [Header("Focus Motion")]
    [SerializeField] [Min(0f)] private float nearFocusSpeed = 14f;
    [SerializeField] [Min(0f)] private float farFocusSpeed = 8f;
    [SerializeField] [Min(0f)] private float lostFocusSpeed = 4f;
    [SerializeField] private bool useUnscaledTime;

    [Header("Lens")]
    [SerializeField] private DepthOfFieldMode focusMode = DepthOfFieldMode.Bokeh;
    [SerializeField] [Range(1f, 32f)] private float aperture = 5.6f;
    [SerializeField] [Range(1f, 300f)] private float focalLength = 75f;
    [SerializeField] private bool controlBokehShape = true;
    [SerializeField] [Range(3, 9)] private int bladeCount = 5;
    [SerializeField] [Range(0f, 1f)] private float bladeCurvature = 1f;
    [SerializeField] [Range(-180f, 180f)] private float bladeRotation;

    [Header("Gaussian Fallback")]
    [SerializeField] [Min(0.1f)] private float gaussianBlurRange = 15f;
    [SerializeField] [Range(0.5f, 1.5f)] private float gaussianMaxRadius = 1f;
    [SerializeField] private bool highQualityGaussian = true;

    [Header("Debug")]
    [SerializeField] private bool drawFocusRay;

    private readonly RaycastHit[] focusHits = new RaycastHit[FocusHitCapacity];
    private DepthOfField depthOfField;
    private Transform ignoredRoot;
    private float currentFocusDistance;
    private float targetFocusDistance;
    private bool hasCurrentFocusDistance;
    private bool hasFocusTarget;
    private bool warnedMissingVolume;
    private bool warnedMissingProfile;

    public float CurrentFocusDistance => currentFocusDistance;
    public float TargetFocusDistance => targetFocusDistance;
    public bool HasFocusTarget => hasFocusTarget;

    public void SetAutoFocus(bool enabled)
    {
        autoFocus = enabled;
    }

    public void SetFocusDistance(float distance, bool immediate)
    {
        targetFocusDistance = ClampFocusDistance(distance);

        if (immediate || !hasCurrentFocusDistance)
        {
            currentFocusDistance = targetFocusDistance;
            hasCurrentFocusDistance = true;
            ApplyFocusDistance(currentFocusDistance);
        }
    }

    private void Reset()
    {
        focusCamera = GetComponent<Camera>();
        targetVolume = GetComponent<Volume>();
        focusLayerMask = BuildDefaultFocusLayerMask();
    }

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        PrepareDepthOfField();
        ResetFocusState();
    }

    private void LateUpdate()
    {
        if (!PrepareDepthOfField())
            return;

        ApplyDepthOfFieldSettings();

        if (!autoFocus)
        {
            ApplyFocusDistance(currentFocusDistance);
            return;
        }

        bool foundFocusTarget = TryResolveFocusDistance(out float resolvedFocusDistance);
        hasFocusTarget = foundFocusTarget;
        targetFocusDistance = resolvedFocusDistance;

        if (!hasCurrentFocusDistance)
        {
            currentFocusDistance = targetFocusDistance;
            hasCurrentFocusDistance = true;
        }
        else
        {
            currentFocusDistance = SmoothFocusDistance(
                currentFocusDistance,
                targetFocusDistance,
                foundFocusTarget,
                useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
        }

        ApplyFocusDistance(currentFocusDistance);
    }

    private void OnValidate()
    {
        minFocusDistance = Mathf.Max(0.01f, minFocusDistance);
        maxFocusDistance = Mathf.Max(minFocusDistance + 0.01f, maxFocusDistance);
        fallbackFocusDistance = ClampFocusDistance(fallbackFocusDistance);
        focusProbeRadius = Mathf.Max(0f, focusProbeRadius);
        focusDeadZone = Mathf.Max(0f, focusDeadZone);
        nearFocusSpeed = Mathf.Max(0f, nearFocusSpeed);
        farFocusSpeed = Mathf.Max(0f, farFocusSpeed);
        lostFocusSpeed = Mathf.Max(0f, lostFocusSpeed);
        gaussianBlurRange = Mathf.Max(0.1f, gaussianBlurRange);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawFocusRay)
            return;

        Camera cameraForGizmos = focusCamera != null ? focusCamera : GetComponent<Camera>();
        if (cameraForGizmos == null)
            return;

        float distance = Application.isPlaying && hasCurrentFocusDistance
            ? currentFocusDistance
            : fallbackFocusDistance;
        Vector3 origin = cameraForGizmos.transform.position;
        Vector3 focusPoint = origin + cameraForGizmos.transform.forward * distance;

        Gizmos.color = hasFocusTarget ? Color.green : Color.yellow;
        Gizmos.DrawLine(origin, focusPoint);
        Gizmos.DrawWireSphere(focusPoint, Mathf.Max(0.05f, focusProbeRadius));
    }

    private void CacheReferences()
    {
        if (focusCamera == null)
            focusCamera = GetComponent<Camera>();

        if (targetVolume == null)
            targetVolume = GetComponent<Volume>();

        ignoredRoot = transform.root;
    }

    private bool PrepareDepthOfField()
    {
        if (depthOfField != null)
            return true;

        if (targetVolume == null)
        {
            if (!warnedMissingVolume)
            {
                Debug.LogWarning("DynamicDepthOfFieldController: missing Volume reference.", this);
                warnedMissingVolume = true;
            }

            return false;
        }

        VolumeProfile runtimeProfile = targetVolume.profile;
        if (runtimeProfile == null)
        {
            if (!warnedMissingProfile)
            {
                Debug.LogWarning("DynamicDepthOfFieldController: Volume has no profile.", this);
                warnedMissingProfile = true;
            }

            return false;
        }

        if (!runtimeProfile.TryGet(out depthOfField))
            depthOfField = runtimeProfile.Add<DepthOfField>(true);

        ResetFocusState();
        ApplyDepthOfFieldSettings();
        return depthOfField != null;
    }

    private void ResetFocusState()
    {
        float initialDistance = depthOfField != null
            ? depthOfField.focusDistance.value
            : fallbackFocusDistance;

        currentFocusDistance = ClampFocusDistance(initialDistance);
        targetFocusDistance = currentFocusDistance;
        hasCurrentFocusDistance = true;
        hasFocusTarget = false;
    }

    private bool TryResolveFocusDistance(out float focusDistance)
    {
        if (TryGetFocusHitDistance(out float hitDistance))
        {
            focusDistance = ClampFocusDistance(hitDistance);
            return true;
        }

        focusDistance = ClampFocusDistance(fallbackFocusDistance);
        return false;
    }

    private bool TryGetFocusHitDistance(out float distance)
    {
        distance = 0f;

        if (focusCamera == null || focusLayerMask.value == 0)
            return false;

        Ray focusRay = focusCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int hitCount = focusProbeRadius > 0f
            ? Physics.SphereCastNonAlloc(
                focusRay,
                focusProbeRadius,
                focusHits,
                maxFocusDistance,
                focusLayerMask,
                triggerInteraction)
            : Physics.RaycastNonAlloc(
                focusRay,
                focusHits,
                maxFocusDistance,
                focusLayerMask,
                triggerInteraction);

        bool foundHit = false;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = focusHits[i];
            if (hit.collider == null)
                continue;

            if (ignoreOwnHierarchy
                && ignoredRoot != null
                && hit.collider.transform.root == ignoredRoot)
            {
                continue;
            }

            float hitDistance = hit.distance > 0f
                ? hit.distance
                : Vector3.Distance(focusRay.origin, hit.point);
            if (hitDistance < closestDistance)
            {
                closestDistance = hitDistance;
                foundHit = true;
            }
        }

        if (!foundHit)
            return false;

        distance = closestDistance;
        return true;
    }

    private float SmoothFocusDistance(float current, float target, bool foundTarget, float deltaTime)
    {
        float delta = Mathf.Abs(target - current);
        if (delta <= focusDeadZone)
            return current;

        float speed = foundTarget
            ? (target < current ? nearFocusSpeed : farFocusSpeed)
            : lostFocusSpeed;
        if (speed <= 0f || deltaTime <= 0f)
            return current;

        float t = 1f - Mathf.Exp(-speed * deltaTime);
        return Mathf.Lerp(current, target, Mathf.Clamp01(t));
    }

    private void ApplyDepthOfFieldSettings()
    {
        if (depthOfField == null)
            return;

        depthOfField.active = depthOfFieldEnabled;
        depthOfField.mode.overrideState = true;
        depthOfField.mode.value = focusMode;
        depthOfField.focusDistance.overrideState = true;
        depthOfField.aperture.overrideState = true;
        depthOfField.aperture.value = aperture;
        depthOfField.focalLength.overrideState = true;
        depthOfField.focalLength.value = focalLength;

        if (controlBokehShape)
        {
            depthOfField.bladeCount.overrideState = true;
            depthOfField.bladeCount.value = bladeCount;
            depthOfField.bladeCurvature.overrideState = true;
            depthOfField.bladeCurvature.value = bladeCurvature;
            depthOfField.bladeRotation.overrideState = true;
            depthOfField.bladeRotation.value = bladeRotation;
        }

        depthOfField.gaussianStart.overrideState = true;
        depthOfField.gaussianEnd.overrideState = true;
        depthOfField.gaussianMaxRadius.overrideState = true;
        depthOfField.gaussianMaxRadius.value = gaussianMaxRadius;
        depthOfField.highQualitySampling.overrideState = true;
        depthOfField.highQualitySampling.value = highQualityGaussian;
    }

    private void ApplyFocusDistance(float focusDistance)
    {
        if (depthOfField == null)
            return;

        float clampedDistance = ClampFocusDistance(focusDistance);
        depthOfField.focusDistance.value = clampedDistance;

        if (focusMode == DepthOfFieldMode.Gaussian)
        {
            depthOfField.gaussianStart.value = clampedDistance;
            depthOfField.gaussianEnd.value = clampedDistance + gaussianBlurRange;
        }
    }

    private float ClampFocusDistance(float distance)
    {
        return Mathf.Clamp(distance, minFocusDistance, maxFocusDistance);
    }

    private static LayerMask BuildDefaultFocusLayerMask()
    {
        int mask = Physics.DefaultRaycastLayers;
        mask = RemoveLayer(mask, "UI");
        mask = RemoveLayer(mask, "Hands");
        mask = RemoveLayer(mask, "FirstPersonView");
        mask = RemoveLayer(mask, "Player");
        return mask;
    }

    private static int RemoveLayer(int mask, string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? mask & ~(1 << layer) : mask;
    }
}
