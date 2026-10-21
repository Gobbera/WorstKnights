using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Enemy/Beholder Procedural Animation Controller")]
public sealed class BeholderProceduralAnimationController : MonoBehaviour
{
    [Serializable]
    private struct EyeBoneRuntime
    {
        public Transform Bone;
        public Vector3 LocalLookAxis;
    }

    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private Transform[] eyeBones = Array.Empty<Transform>();

    [Header("Eye Tracking")]
    [SerializeField] private bool enableEyeTracking = true;
    [SerializeField] private bool autoResolveEyeBones = true;
    [SerializeField] private string[] eyeBoneNameFragments = { "Center Eye", "Eye.Tentacule" };
    [SerializeField] private string[] excludedEyeBoneNameFragments = { ".Mesh", "_end" };
    [SerializeField] private bool useEndChildAsLookAxis = true;
    [SerializeField] private string endChildSuffix = "_end";
    [SerializeField] private Vector3 fallbackLocalLookAxis = Vector3.forward;
    [SerializeField] [Range(0f, 1f)] private float eyeTrackingWeight = 1f;
    [SerializeField] [Min(0f)] private float acquireSmoothTime = 0.08f;
    [SerializeField] [Min(0f)] private float releaseSmoothTime = 0.16f;
    [SerializeField] [Min(0f)] private float targetHeightFallback = 1.2f;
    [SerializeField] [Min(0f)] private float nearestTargetFallbackRadius = 24f;

    [Header("State Rules")]
    [SerializeField] private bool trackDuringChase = true;
    [SerializeField] private bool trackDuringAttack = true;
    [SerializeField] private bool trackDuringAlert;
    [SerializeField] private bool trackDuringSearch;

    [Header("Debug")]
    [SerializeField] private bool drawEyeTrackingGizmos = true;
    [SerializeField] private Color eyeTrackingGizmoColor = new Color(0.85f, 0.2f, 1f, 0.9f);

    private EyeBoneRuntime[] resolvedEyeBones = Array.Empty<EyeBoneRuntime>();
    private float currentEyeWeight;
    private float currentEyeWeightVelocity;
    private Vector3 currentAimPosition;
    private bool hasCurrentAimPosition;

    private void Awake()
    {
        ResolveReferences();
        CacheEyeBones();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheEyeBones();
    }

    private void LateUpdate()
    {
        ResolveReferences();

        if (!enableEyeTracking || resolvedEyeBones.Length == 0)
        {
            currentEyeWeight = 0f;
            hasCurrentAimPosition = false;
            return;
        }

        PlayerHealth target = ShouldTrackEyes() ? ResolveEyeTrackingTarget() : null;
        bool hasTarget = target != null;
        if (hasTarget)
        {
            currentAimPosition = ResolveAimPosition(target);
            hasCurrentAimPosition = true;
        }

        float desiredWeight = hasTarget ? Mathf.Clamp01(eyeTrackingWeight) : 0f;
        currentEyeWeight = SmoothTowards(currentEyeWeight, desiredWeight, desiredWeight > currentEyeWeight ? acquireSmoothTime : releaseSmoothTime);
        if (!hasTarget && currentEyeWeight <= 0.001f)
            hasCurrentAimPosition = false;

        ApplyEyeTracking();
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

    private void CacheEyeBones()
    {
        List<Transform> bones = new List<Transform>();

        if (eyeBones != null)
        {
            for (int i = 0; i < eyeBones.Length; i++)
                AddUniqueBone(bones, eyeBones[i], validateAutoCandidate: false);
        }

        if (autoResolveEyeBones)
        {
            Transform searchRoot = enemySetup != null && enemySetup.ModelRoot != null
                ? enemySetup.ModelRoot
                : transform;
            Transform[] descendants = searchRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
                AddUniqueBone(bones, descendants[i], validateAutoCandidate: true);
        }

        resolvedEyeBones = new EyeBoneRuntime[bones.Count];
        for (int i = 0; i < bones.Count; i++)
        {
            Transform bone = bones[i];
            resolvedEyeBones[i] = new EyeBoneRuntime
            {
                Bone = bone,
                LocalLookAxis = ResolveLocalLookAxis(bone)
            };
        }
    }

    private void AddUniqueBone(List<Transform> bones, Transform candidate, bool validateAutoCandidate)
    {
        if (candidate == null)
            return;

        if (validateAutoCandidate && !IsAutoEyeBoneCandidate(candidate))
            return;

        if (bones.Contains(candidate))
            return;

        bones.Add(candidate);
    }

    private bool IsAutoEyeBoneCandidate(Transform candidate)
    {
        if (candidate == null || !ContainsAny(candidate.name, eyeBoneNameFragments))
            return false;

        if (ContainsAny(candidate.name, excludedEyeBoneNameFragments))
            return false;

        return candidate.GetComponent<Renderer>() == null
            && candidate.GetComponent<MeshFilter>() == null
            && candidate.GetComponent<SkinnedMeshRenderer>() == null;
    }

    private Vector3 ResolveLocalLookAxis(Transform bone)
    {
        Vector3 fallbackAxis = fallbackLocalLookAxis.sqrMagnitude > 0.0001f
            ? fallbackLocalLookAxis.normalized
            : Vector3.forward;

        if (!useEndChildAsLookAxis || bone == null)
            return fallbackAxis;

        Transform endChild = FindEndChild(bone);
        if (endChild == null)
            return fallbackAxis;

        Vector3 worldAxis = endChild.position - bone.position;
        if (worldAxis.sqrMagnitude <= 0.0001f)
            return fallbackAxis;

        return bone.InverseTransformDirection(worldAxis.normalized).normalized;
    }

    private Transform FindEndChild(Transform bone)
    {
        if (bone == null || string.IsNullOrWhiteSpace(endChildSuffix))
            return null;

        string exactName = bone.name + endChildSuffix;
        Transform[] descendants = bone.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < descendants.Length; i++)
        {
            Transform descendant = descendants[i];
            if (descendant != null && string.Equals(descendant.name, exactName, StringComparison.Ordinal))
                return descendant;
        }

        for (int i = 0; i < descendants.Length; i++)
        {
            Transform descendant = descendants[i];
            if (descendant != null && descendant != bone && descendant.name.EndsWith(endChildSuffix, StringComparison.Ordinal))
                return descendant;
        }

        return null;
    }

    private bool ShouldTrackEyes()
    {
        if (enemyHealth != null && !enemyHealth.IsAlive)
            return false;

        EnemyState state = enemyBrain != null ? enemyBrain.CurrentState : EnemyState.Idle;
        return (trackDuringChase && state == EnemyState.Chase)
            || (trackDuringAttack && state == EnemyState.Attack)
            || (trackDuringAlert && state == EnemyState.Alert)
            || (trackDuringSearch && state == EnemyState.Search);
    }

    private PlayerHealth ResolveEyeTrackingTarget()
    {
        PlayerHealth currentTarget = enemyBrain != null ? enemyBrain.CurrentTarget : null;
        if (IsValidTarget(currentTarget))
            return currentTarget;

        if ((enemyBrain != null && enemyBrain.UseLastSeenSearchFlow)
            || (enemySetup != null && !enemySetup.HasAuthority))
            return null;

        if (nearestTargetFallbackRadius <= 0f)
            return null;

        EnemyTargetRegistry.PruneInvalidTargets();
        if (EnemyTargetRegistry.Count == 0)
            EnemyTargetRegistry.RefreshFromScene();

        PlayerHealth nearestTarget = null;
        float nearestDistanceSqr = nearestTargetFallbackRadius * nearestTargetFallbackRadius;
        Vector3 origin = transform.position;

        for (int i = 0; i < EnemyTargetRegistry.Count; i++)
        {
            PlayerHealth candidate = EnemyTargetRegistry.GetTarget(i);
            if (!IsValidTarget(candidate))
                continue;

            float distanceSqr = Vector3.ProjectOnPlane(candidate.transform.position - origin, Vector3.up).sqrMagnitude;
            if (distanceSqr > nearestDistanceSqr)
                continue;

            nearestDistanceSqr = distanceSqr;
            nearestTarget = candidate;
        }

        return nearestTarget;
    }

    private Vector3 ResolveAimPosition(PlayerHealth target)
    {
        if (target == null)
            return transform.position;

        CapsuleCollider capsule = target.GetComponentInChildren<CapsuleCollider>();
        if (capsule != null)
            return capsule.bounds.center;

        Collider targetCollider = target.GetComponentInChildren<Collider>();
        if (targetCollider != null)
            return targetCollider.bounds.center;

        return target.transform.position + Vector3.up * Mathf.Max(0f, targetHeightFallback);
    }

    private void ApplyEyeTracking()
    {
        float weight = Mathf.Clamp01(currentEyeWeight);
        if (!hasCurrentAimPosition || weight <= 0.001f)
            return;

        for (int i = 0; i < resolvedEyeBones.Length; i++)
        {
            EyeBoneRuntime eyeBone = resolvedEyeBones[i];
            if (eyeBone.Bone == null)
                continue;

            Vector3 toTarget = currentAimPosition - eyeBone.Bone.position;
            if (toTarget.sqrMagnitude <= 0.0001f)
                continue;

            Quaternion animatedWorldRotation = eyeBone.Bone.rotation;
            Vector3 currentLookDirection = animatedWorldRotation * eyeBone.LocalLookAxis;
            Quaternion lookDelta = Quaternion.FromToRotation(currentLookDirection, toTarget.normalized);
            Quaternion targetRotation = lookDelta * animatedWorldRotation;
            eyeBone.Bone.rotation = Quaternion.Slerp(animatedWorldRotation, targetRotation, weight);
        }
    }

    private float SmoothTowards(float current, float target, float smoothTime)
    {
        if (smoothTime <= 0f)
        {
            currentEyeWeightVelocity = 0f;
            return target;
        }

        return Mathf.SmoothDamp(
            current,
            target,
            ref currentEyeWeightVelocity,
            smoothTime,
            Mathf.Infinity,
            Time.deltaTime);
    }

    private static bool IsValidTarget(PlayerHealth target)
    {
        return target != null && target.CanBeTargetedByEnemy;
    }

    private static bool ContainsAny(string value, string[] fragments)
    {
        if (string.IsNullOrEmpty(value) || fragments == null)
            return false;

        for (int i = 0; i < fragments.Length; i++)
        {
            string fragment = fragments[i];
            if (!string.IsNullOrWhiteSpace(fragment)
                && value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void OnValidate()
    {
        if (eyeBones == null)
            eyeBones = Array.Empty<Transform>();

        if (eyeBoneNameFragments == null || eyeBoneNameFragments.Length == 0)
            eyeBoneNameFragments = new[] { "Center Eye", "Eye.Tentacule" };

        if (excludedEyeBoneNameFragments == null)
            excludedEyeBoneNameFragments = Array.Empty<string>();

        if (fallbackLocalLookAxis.sqrMagnitude <= 0.0001f)
            fallbackLocalLookAxis = Vector3.forward;

        eyeTrackingWeight = Mathf.Clamp01(eyeTrackingWeight);
        acquireSmoothTime = Mathf.Max(0f, acquireSmoothTime);
        releaseSmoothTime = Mathf.Max(0f, releaseSmoothTime);
        targetHeightFallback = Mathf.Max(0f, targetHeightFallback);
        nearestTargetFallbackRadius = Mathf.Max(0f, nearestTargetFallbackRadius);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawEyeTrackingGizmos || !hasCurrentAimPosition || resolvedEyeBones == null)
            return;

        Gizmos.color = eyeTrackingGizmoColor;
        for (int i = 0; i < resolvedEyeBones.Length; i++)
        {
            Transform bone = resolvedEyeBones[i].Bone;
            if (bone != null)
                Gizmos.DrawLine(bone.position, currentAimPosition);
        }
    }
}
