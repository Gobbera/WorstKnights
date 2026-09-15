using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class ShieldGuardHitbox : MonoBehaviour
{
    private const float HitDistancePadding = 0.05f;
    private static readonly List<ShieldGuardHitbox> ActiveHitboxes = new List<ShieldGuardHitbox>();

    [SerializeField] private Collider[] blockColliders = Array.Empty<Collider>();
    [SerializeField] private bool makeBlockCollidersTriggers = true;
    [Header("Guard Timing")]
    [SerializeField] private bool overrideBlockActivationDelay;
    [SerializeField] [Min(0f)] private float blockActivationDelay = 0.5f;
    [Header("Feedback")]
    [FormerlySerializedAs("successfulBlockAudioAnchor")]
    [SerializeField] private Transform successfulBlockFeedbackAnchor;
    [Header("Audio")]
    [SerializeField] private AudioCue successfulBlockAudioCue;
    [SerializeField] private AudioEmitter audioEmitter;
    [Header("Effects")]
    [SerializeField] private GameObject successfulBlockEffectPrefab;
    [SerializeField] [Min(0f)] private float successfulBlockEffectLifetime = 1f;
    [Header("Gizmos")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool drawGizmosOnlyWhenSelected;
    [SerializeField] private Color inactiveGizmoColor = new Color(0.2f, 0.65f, 1f, 0.5f);
    [SerializeField] private Color guardingGizmoColor = new Color(1f, 0.85f, 0.2f, 0.65f);
    [SerializeField] private Color blockingGizmoColor = new Color(0.25f, 1f, 0.45f, 0.85f);

    private bool[] originalColliderTriggerStates = Array.Empty<bool>();
    private PlayerHealth ownerHealth;
    private PlayerMovement ownerMovement;
    private int observedShieldBlockAnimationSequence;
    private bool hasAppliedGuardState;
    private bool hasObservedShieldBlockAnimationSequence;

    public PlayerHealth OwnerHealth
    {
        get
        {
            if (ownerHealth == null)
                ownerHealth = GetComponentInParent<PlayerHealth>();

            return ownerHealth;
        }
    }

    public bool IsGuarding { get; private set; }
    public bool IsBlocking { get; private set; }
    public AudioCue SuccessfulBlockAudioCue => successfulBlockAudioCue;
    public GameObject SuccessfulBlockEffectPrefab => successfulBlockEffectPrefab;

    public bool TryGetBlockActivationDelay(out float activationDelay)
    {
        activationDelay = Mathf.Max(0f, blockActivationDelay);
        return overrideBlockActivationDelay;
    }

    private void Awake()
    {
        EnsureOriginalColliderStateCache();
        ResolveAudioEmitter();
        InitializeObservedShieldBlockSequence();
    }

    private void OnEnable()
    {
        RegisterActiveHitbox();
        InitializeObservedShieldBlockSequence();
    }

    private void Update()
    {
        TryPlaySuccessfulBlockFeedbackFromOwnerSequence();
    }

    private void OnValidate()
    {
        EnsureBlockColliders();
    }

    private void OnDisable()
    {
        UnregisterActiveHitbox();

        if (!hasAppliedGuardState)
            return;

        IsGuarding = false;
        IsBlocking = false;
        ApplyColliderState();
    }

    private void OnDestroy()
    {
        UnregisterActiveHitbox();
    }

    private void OnDrawGizmos()
    {
        if (drawGizmosOnlyWhenSelected)
            return;

        DrawGuardGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmosOnlyWhenSelected)
            return;

        DrawGuardGizmos();
    }

    public void SetGuardState(bool guarding, bool blocking)
    {
        EnsureOriginalColliderStateCache();
        RegisterActiveHitbox();
        hasAppliedGuardState = true;

        IsGuarding = guarding;
        IsBlocking = guarding && blocking;
        InitializeObservedShieldBlockSequence();
        ApplyColliderState();
    }

    public void SetOwnerHealth(PlayerHealth health)
    {
        ownerHealth = health;
        ownerMovement = ownerHealth != null ? ownerHealth.GetComponent<PlayerMovement>() : null;
        InitializeObservedShieldBlockSequence();
    }

    public static bool TryBlockEnemyHit(
        PlayerHealth targetHealth,
        GameObject instigator,
        Vector3 attackOrigin,
        Vector3 targetHitPoint,
        Vector3 fallbackHitDirection,
        float probeRadius,
        out Vector3 shieldHitPoint)
    {
        shieldHitPoint = targetHitPoint;
        if (targetHealth == null || !targetHealth.IsAlive || !targetHealth.CanBeTargetedByEnemy)
            return false;

        Vector3 attackVector = targetHitPoint - attackOrigin;
        float targetDistance = attackVector.magnitude;
        Vector3 hitDirection;
        if (targetDistance > 0.0001f)
        {
            hitDirection = attackVector / targetDistance;
        }
        else if (fallbackHitDirection.sqrMagnitude > 0.0001f)
        {
            hitDirection = fallbackHitDirection.normalized;
            targetDistance = HitDistancePadding;
        }
        else
        {
            return false;
        }

        float closestBlockDistance = float.PositiveInfinity;
        bool hasBlock = false;

        for (int i = ActiveHitboxes.Count - 1; i >= 0; i--)
        {
            ShieldGuardHitbox guardHitbox = ActiveHitboxes[i];
            if (guardHitbox == null)
            {
                ActiveHitboxes.RemoveAt(i);
                continue;
            }

            if (!guardHitbox.CanEvaluateBlockForTarget(targetHealth))
                continue;

            if (!guardHitbox.TryGetFirstBlockPointOnAttack(
                    instigator,
                    attackOrigin,
                    hitDirection,
                    targetDistance,
                    Mathf.Max(0f, probeRadius),
                    out Vector3 candidateHitPoint,
                    out float candidateDistance))
            {
                continue;
            }

            if (candidateDistance > targetDistance + HitDistancePadding
                || candidateDistance >= closestBlockDistance)
            {
                continue;
            }

            closestBlockDistance = candidateDistance;
            shieldHitPoint = candidateHitPoint;
            hasBlock = true;
        }

        return hasBlock;
    }

    public bool CanBlockEnemyHit(GameObject instigator, Vector3 hitPoint, Vector3 hitDirection, Collider hitCollider)
    {
        if (!IsBlocking || !isActiveAndEnabled)
            return false;

        PlayerHealth health = OwnerHealth;
        if (health == null || !health.IsAlive)
            return false;

        return ContainsBlockCollider(hitCollider);
    }

    public bool ContainsBlockCollider(Collider candidate)
    {
        if (candidate == null)
            return false;

        EnsureBlockColliders();
        for (int i = 0; i < blockColliders.Length; i++)
        {
            if (blockColliders[i] == candidate)
                return true;
        }

        return false;
    }

    private void ApplyColliderState()
    {
        if (!hasAppliedGuardState)
            return;

        EnsureBlockColliders();
        bool enableBlockingHitbox = IsBlocking;

        for (int i = 0; i < blockColliders.Length; i++)
        {
            Collider blockCollider = blockColliders[i];
            if (blockCollider == null)
                continue;

            if (enableBlockingHitbox && makeBlockCollidersTriggers)
                blockCollider.isTrigger = true;
            else if (i < originalColliderTriggerStates.Length)
                blockCollider.isTrigger = originalColliderTriggerStates[i];

            blockCollider.enabled = enableBlockingHitbox;
        }
    }

    private void CacheOriginalColliderStates()
    {
        EnsureBlockColliders();

        originalColliderTriggerStates = new bool[blockColliders.Length];
        for (int i = 0; i < blockColliders.Length; i++)
        {
            Collider blockCollider = blockColliders[i];
            originalColliderTriggerStates[i] = blockCollider != null && blockCollider.isTrigger;
        }
    }

    private void EnsureOriginalColliderStateCache()
    {
        EnsureBlockColliders();
        if (originalColliderTriggerStates != null && originalColliderTriggerStates.Length == blockColliders.Length)
            return;

        CacheOriginalColliderStates();
    }

    private void EnsureBlockColliders()
    {
        if (HasUsableBlockCollider())
            return;

        Collider[] ownColliders = GetComponents<Collider>();
        if (ownColliders.Length > 0)
        {
            blockColliders = ownColliders;
            return;
        }

        Collider[] childColliders = GetComponentsInChildren<Collider>(true);
        int count = 0;
        for (int i = 0; i < childColliders.Length; i++)
        {
            Collider childCollider = childColliders[i];
            if (childCollider != null && !childCollider.isTrigger)
                count++;
        }

        if (count <= 0)
            return;

        Collider[] resolvedColliders = new Collider[count];
        int writeIndex = 0;
        for (int i = 0; i < childColliders.Length; i++)
        {
            Collider childCollider = childColliders[i];
            if (childCollider != null && !childCollider.isTrigger)
                resolvedColliders[writeIndex++] = childCollider;
        }

        blockColliders = resolvedColliders;
    }

    private void RegisterActiveHitbox()
    {
        if (!ActiveHitboxes.Contains(this))
            ActiveHitboxes.Add(this);
    }

    private void UnregisterActiveHitbox()
    {
        ActiveHitboxes.Remove(this);
    }

    private void TryPlaySuccessfulBlockFeedbackFromOwnerSequence()
    {
        PlayerMovement movement = ResolveOwnerMovement();
        if (movement == null)
        {
            hasObservedShieldBlockAnimationSequence = false;
            return;
        }

        int currentSequence = movement.ShieldBlockAnimationSequence;
        if (!hasObservedShieldBlockAnimationSequence)
        {
            observedShieldBlockAnimationSequence = currentSequence;
            hasObservedShieldBlockAnimationSequence = true;
            return;
        }

        if (currentSequence == observedShieldBlockAnimationSequence)
            return;

        observedShieldBlockAnimationSequence = currentSequence;
        if (!IsBlocking)
            return;

        PlaySuccessfulBlockFeedback();
    }

    private void PlaySuccessfulBlockFeedback()
    {
        PlaySuccessfulBlockAudio(ResolveSuccessfulBlockFeedbackPosition());
        PlaySuccessfulBlockEffect();
    }

    private void PlaySuccessfulBlockAudio(Vector3 audioPosition)
    {
        if (successfulBlockAudioCue == null)
            return;

        AudioEmitter emitter = ResolveAudioEmitter();
        if (emitter != null)
        {
            emitter.PlayAtPosition(successfulBlockAudioCue, audioPosition);
            return;
        }

        GameAudioService.Play(successfulBlockAudioCue, audioPosition);
    }

    private void PlaySuccessfulBlockEffect()
    {
        if (successfulBlockEffectPrefab == null || successfulBlockFeedbackAnchor == null)
            return;

        GameObject spawnedEffect = Instantiate(successfulBlockEffectPrefab, successfulBlockFeedbackAnchor);
        Transform spawnedTransform = spawnedEffect.transform;
        spawnedTransform.localPosition = Vector3.zero;
        spawnedTransform.localRotation = Quaternion.identity;

        if (!spawnedEffect.activeSelf)
            spawnedEffect.SetActive(true);

        ParticleSystem[] particleSystems = spawnedEffect.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem != null)
                particleSystem.Play(withChildren: true);
        }

        if (successfulBlockEffectLifetime > 0f)
            Destroy(spawnedEffect, successfulBlockEffectLifetime);
    }

    private Vector3 ResolveSuccessfulBlockFeedbackPosition()
    {
        if (successfulBlockFeedbackAnchor != null)
            return successfulBlockFeedbackAnchor.position;

        EnsureBlockColliders();
        for (int i = 0; i < blockColliders.Length; i++)
        {
            Collider blockCollider = blockColliders[i];
            if (blockCollider != null)
                return blockCollider.bounds.center;
        }

        return transform.position;
    }

    private AudioEmitter ResolveAudioEmitter()
    {
        if (audioEmitter != null)
            return audioEmitter;

        audioEmitter = GetComponent<AudioEmitter>();
        if (audioEmitter == null)
            audioEmitter = GetComponentInParent<AudioEmitter>();

        return audioEmitter;
    }

    private PlayerMovement ResolveOwnerMovement()
    {
        if (ownerMovement != null)
            return ownerMovement;

        PlayerHealth health = OwnerHealth;
        if (health != null)
            ownerMovement = health.GetComponent<PlayerMovement>();

        return ownerMovement;
    }

    private void InitializeObservedShieldBlockSequence()
    {
        PlayerMovement movement = ResolveOwnerMovement();
        if (movement == null)
        {
            observedShieldBlockAnimationSequence = 0;
            hasObservedShieldBlockAnimationSequence = false;
            return;
        }

        observedShieldBlockAnimationSequence = movement.ShieldBlockAnimationSequence;
        hasObservedShieldBlockAnimationSequence = true;
    }

    private bool CanEvaluateBlockForTarget(PlayerHealth targetHealth)
    {
        if (!IsBlocking || !isActiveAndEnabled)
            return false;

        PlayerHealth health = OwnerHealth;
        return health != null
            && health == targetHealth
            && health.IsAlive
            && health.CanBeTargetedByEnemy;
    }

    private bool TryGetFirstBlockPointOnAttack(
        GameObject instigator,
        Vector3 attackOrigin,
        Vector3 hitDirection,
        float targetDistance,
        float probeRadius,
        out Vector3 shieldHitPoint,
        out float shieldHitDistance)
    {
        shieldHitPoint = attackOrigin;
        shieldHitDistance = float.PositiveInfinity;
        EnsureBlockColliders();

        bool hasHit = false;
        for (int i = 0; i < blockColliders.Length; i++)
        {
            Collider blockCollider = blockColliders[i];
            if (blockCollider == null || !blockCollider.enabled)
                continue;

            if (!TryGetColliderHitOnAttack(
                    blockCollider,
                    attackOrigin,
                    hitDirection,
                    targetDistance,
                    probeRadius,
                    out Vector3 candidateHitPoint,
                    out float candidateHitDistance))
            {
                continue;
            }

            if (candidateHitDistance >= shieldHitDistance)
                continue;

            if (!CanBlockEnemyHit(instigator, candidateHitPoint, hitDirection, blockCollider))
                continue;

            shieldHitPoint = candidateHitPoint;
            shieldHitDistance = candidateHitDistance;
            hasHit = true;
        }

        return hasHit;
    }

    private static bool TryGetColliderHitOnAttack(
        Collider blockCollider,
        Vector3 attackOrigin,
        Vector3 hitDirection,
        float targetDistance,
        float probeRadius,
        out Vector3 hitPoint,
        out float hitDistance)
    {
        Ray attackRay = new Ray(attackOrigin, hitDirection);
        float maxDistance = targetDistance + HitDistancePadding;
        if (blockCollider.Raycast(attackRay, out RaycastHit raycastHit, maxDistance))
        {
            hitPoint = raycastHit.point;
            hitDistance = raycastHit.distance;
            return true;
        }

        if (probeRadius <= 0.0001f)
        {
            hitPoint = default;
            hitDistance = 0f;
            return false;
        }

        Vector3 boundsCenter = blockCollider.bounds.center;
        float projectedDistance = Mathf.Clamp(Vector3.Dot(boundsCenter - attackOrigin, hitDirection), 0f, targetDistance);
        Vector3 segmentPoint = attackOrigin + hitDirection * projectedDistance;
        Vector3 closestPoint = blockCollider.ClosestPoint(segmentPoint);
        float sqrAllowedDistance = probeRadius * probeRadius;

        if ((closestPoint - segmentPoint).sqrMagnitude > sqrAllowedDistance)
        {
            hitPoint = default;
            hitDistance = 0f;
            return false;
        }

        hitPoint = closestPoint;
        hitDistance = projectedDistance;
        return true;
    }

    private void DrawGuardGizmos()
    {
        if (!drawGizmos)
            return;

        EnsureBlockColliders();

        Color previousColor = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;

        Gizmos.color = ResolveGizmoColor();
        for (int i = 0; i < blockColliders.Length; i++)
            DrawColliderGizmo(blockColliders[i]);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private Color ResolveGizmoColor()
    {
        if (IsBlocking)
            return blockingGizmoColor;

        if (IsGuarding)
            return guardingGizmoColor;

        return inactiveGizmoColor;
    }

    private static void DrawColliderGizmo(Collider blockCollider)
    {
        if (blockCollider == null)
            return;

        switch (blockCollider)
        {
            case BoxCollider boxCollider:
                Gizmos.matrix = Matrix4x4.TRS(
                    boxCollider.transform.position,
                    boxCollider.transform.rotation,
                    boxCollider.transform.lossyScale);
                Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);
                break;

            case SphereCollider sphereCollider:
                Gizmos.matrix = Matrix4x4.TRS(
                    sphereCollider.transform.position,
                    sphereCollider.transform.rotation,
                    sphereCollider.transform.lossyScale);
                Gizmos.DrawWireSphere(sphereCollider.center, sphereCollider.radius);
                break;

            case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.DrawWireMesh(
                    meshCollider.sharedMesh,
                    meshCollider.transform.position,
                    meshCollider.transform.rotation,
                    meshCollider.transform.lossyScale);
                break;

            default:
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.DrawWireCube(blockCollider.bounds.center, blockCollider.bounds.size);
                break;
        }
    }

    private bool HasUsableBlockCollider()
    {
        if (blockColliders == null || blockColliders.Length == 0)
            return false;

        for (int i = 0; i < blockColliders.Length; i++)
        {
            if (blockColliders[i] != null)
                return true;
        }

        return false;
    }
}
