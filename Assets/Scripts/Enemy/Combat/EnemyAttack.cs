using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemySetup), typeof(EnemyHealth))]
public class EnemyAttack : MonoBehaviour
{
    private const int MaxHitColliders = 16;
    private const int ArcGizmoSegments = 28;
    private const string DefaultAttackAnimationClipName = "Attack_01";
    private const string DefaultAttackStatePath = "Base Layer.Attack_01";
    private static readonly string[] WeaponRootCandidateNames =
    {
        "Sword",
        "Weapon",
        "Blade",
        "Hand.R",
        "Hand_R",
        "RightHand",
        "HandR"
    };
    private static readonly string[] WeaponTipCandidateNames =
    {
        "SwordTip",
        "WeaponTip",
        "BladeTip",
        "Tip"
    };

    [Header("Attack")]
    [SerializeField] [Min(0f)] private float attackRange = 1.6f;
    [SerializeField] [Min(0f)] private float attackStartRange = 2.35f;
    [SerializeField] [Min(0f)] private float attackDamage = 12f;
    [SerializeField] [Min(0.05f)] private float attackCooldown = 1.1f;
    [SerializeField] [Min(0f)] private float attackMovementLockDuration;
    [SerializeField] [Range(0f, 1f)] private float attackTurnTrackingEndNormalizedTime = 0.45f;
    [SerializeField] [Min(0f)] private float attackTurnSpeedMultiplier = 1.35f;
    [SerializeField] private string attackAnimationClipName = DefaultAttackAnimationClipName;
    [SerializeField] private string attackStatePath = DefaultAttackStatePath;
    [SerializeField] [Range(0f, 1f)] private float attackHitStartNormalizedTime = 0.24f;
    [SerializeField] [Range(0f, 1f)] private float attackHitEndNormalizedTime = 0.72f;
    [SerializeField] [Range(0f, 1f)] private float damageInterruptWindowEndNormalizedTime = 0.18f;
    [SerializeField] [Min(0.05f)] private float weaponHitRadius = 0.2f;
    [SerializeField] [Min(0.1f)] private float weaponReach = 0.9f;
    [SerializeField] private LayerMask hitMask = Physics.DefaultRaycastLayers;
    [SerializeField] private PlayerDamageAnimationType playerDamageAnimation = PlayerDamageAnimationType.ReactionDamage;
    [SerializeField] private PlayerCameraImpactType playerCameraImpactType = PlayerCameraImpactType.DefaultHit;

    [Header("Forward Arc Hitbox")]
    [SerializeField] private bool useForwardArcHitbox = true;
    [SerializeField] [Min(0f)] private float arcOriginHeight = 1f;
    [SerializeField] [Min(0.1f)] private float arcHitRange = 1.85f;
    [SerializeField] [Range(1f, 180f)] private float arcHitAngle = 120f;

    [Header("Hitbox Preview")]
    [SerializeField] private bool drawHitboxGizmo = true;
    [SerializeField] private Color hitboxGizmoColor = new Color(1f, 0.15f, 0.05f, 0.35f);

    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private Transform weaponRoot;
    [SerializeField] private Transform weaponTip;

    private readonly Collider[] hitBuffer = new Collider[MaxHitColliders];
    private readonly HashSet<PlayerHealth> hitTargetsThisAttack = new HashSet<PlayerHealth>();
    private float nextAttackTime;
    private float attackLockedUntil;
    private float attackStartedAt;
    private float activeAttackDuration;
    private float resolvedAttackAnimationDuration = -1f;
    private int attackStateHash;
    private bool isAlive = true;
    private bool hasPreviousWeaponTip;
    private Vector3 previousWeaponTipPosition;
    private Vector3 cachedAttackForward = Vector3.forward;

    public float AttackRange => attackRange;
    public float AttackStartRange => Mathf.Max(attackRange, attackStartRange);
    public float AttackTurnSpeedMultiplier => Mathf.Max(0f, attackTurnSpeedMultiplier);
    public int AttackSequence { get; private set; }
    public bool IsAttackLocked => IsEffectivelyAlive() && Time.time < attackLockedUntil;
    public bool CanTrackTargetDuringCurrentAttack => IsAttackLocked
        && GetCurrentAttackNormalizedTime() <= Mathf.Clamp01(attackTurnTrackingEndNormalizedTime);

    private void Awake()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        resolvedAttackAnimationDuration = ResolveAttackAnimationDuration();
        RefreshAttackStateHash();
        ResolveWeaponTransforms();
    }

    public void SetAliveState(bool alive)
    {
        isAlive = alive;
        if (!alive)
            CancelCurrentAttack();
    }

    public bool TryAttack(PlayerHealth target, Vector3 planarOffset)
    {
        if (!IsEffectivelyAlive() || target == null || Time.time < nextAttackTime)
            return false;

        float now = Time.time;
        activeAttackDuration = GetAttackMovementLockDuration();
        attackStartedAt = now;
        nextAttackTime = now + Mathf.Max(0.05f, attackCooldown);
        attackLockedUntil = now + activeAttackDuration;
        AttackSequence++;
        cachedAttackForward = planarOffset.sqrMagnitude > 0.0001f
            ? planarOffset.normalized
            : transform.forward;
        hitTargetsThisAttack.Clear();
        hasPreviousWeaponTip = false;
        ResolveWeaponTransforms();

        return true;
    }

    public void CancelCurrentAttack()
    {
        nextAttackTime = 0f;
        attackLockedUntil = 0f;
        attackStartedAt = 0f;
        activeAttackDuration = 0f;
        ResetActiveAttack();
    }

    public void ApplyReplicatedAttackSequence(int sequence)
    {
        AttackSequence = sequence;
    }

    public bool CanDamageInterruptCurrentAttack(Animator animator = null)
    {
        animator ??= enemySetup != null ? enemySetup.Animator : GetComponentInChildren<Animator>(true);
        if (!TryGetActiveAttackStateInfo(animator, out AnimatorStateInfo attackStateInfo))
            return true;

        float interruptWindowEnd = Mathf.Clamp01(damageInterruptWindowEndNormalizedTime);
        float normalizedTime = Mathf.Repeat(attackStateInfo.normalizedTime, 1f);
        return normalizedTime <= interruptWindowEnd;
    }

    private void LateUpdate()
    {
        if (!CanDealDamageLocally() || !IsEffectivelyAlive())
        {
            CancelCurrentAttack();
            return;
        }

        if (!IsAttackLocked)
        {
            ResetActiveAttack();
            return;
        }

        if (!IsAttackHitWindowActive())
        {
            hasPreviousWeaponTip = false;
            return;
        }

        TryDealContactDamage();
    }

    private float GetAttackMovementLockDuration()
    {
        if (attackMovementLockDuration > 0f)
            return attackMovementLockDuration;

        if (resolvedAttackAnimationDuration < 0f)
            resolvedAttackAnimationDuration = ResolveAttackAnimationDuration();

        if (resolvedAttackAnimationDuration > 0f)
            return resolvedAttackAnimationDuration;

        return Mathf.Max(0.05f, attackCooldown);
    }

    private void TryDealContactDamage()
    {
        if (!CanContinueActiveAttack())
            return;

        ResolveWeaponTransforms();
        TryDealForwardArcDamage();

        if (!CanContinueActiveAttack())
            return;

        if (!TryGetWeaponSegment(out Vector3 weaponBasePosition, out Vector3 weaponTipPosition, out Vector3 hitDirection))
            return;

        ApplyContactDamageAlongSegment(weaponBasePosition, weaponTipPosition, hitDirection);

        if (hasPreviousWeaponTip)
            ApplyContactDamageAlongSegment(previousWeaponTipPosition, weaponTipPosition, hitDirection);

        previousWeaponTipPosition = weaponTipPosition;
        hasPreviousWeaponTip = true;
    }

    private void TryDealForwardArcDamage()
    {
        if (!useForwardArcHitbox || !CanContinueActiveAttack())
            return;

        Vector3 origin = ResolveForwardArcOrigin();
        Vector3 attackForward = ResolveForwardArcForward();
        float hitRange = Mathf.Max(0.1f, arcHitRange);
        float minForwardDot = Mathf.Cos(Mathf.Clamp(arcHitAngle, 1f, 180f) * 0.5f * Mathf.Deg2Rad);

        int hitCount = Physics.OverlapSphereNonAlloc(
            origin,
            hitRange,
            hitBuffer,
            DestructibleDebrisCollision.ExcludeDebrisLayer(hitMask.value),
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            if (!CanContinueActiveAttack())
                return;

            Collider hitCollider = hitBuffer[i];
            if (hitCollider == null)
                continue;

            PlayerHealth playerHealth = hitCollider.GetComponentInParent<PlayerHealth>();
            if (playerHealth == null || !playerHealth.IsAlive || !playerHealth.CanBeTargetedByEnemy)
                continue;

            Vector3 planarOffset = Vector3.ProjectOnPlane(playerHealth.transform.position - origin, Vector3.up);
            if (!IsInsideForwardArc(planarOffset, attackForward, hitRange, minForwardDot))
                continue;

            if (!hitTargetsThisAttack.Add(playerHealth))
                continue;

            Vector3 hitPoint = ResolveForwardArcHitPoint(origin, attackForward, hitRange, hitCollider);
            Vector3 hitDirection = planarOffset.sqrMagnitude > 0.0001f
                ? planarOffset.normalized
                : attackForward;

            if (!CanContinueActiveAttack())
                return;

            playerHealth.ReceiveEnemyDamage(
                attackDamage,
                gameObject,
                hitPoint,
                hitDirection,
                playerDamageAnimation,
                playerCameraImpactType);
        }
    }

    private void ApplyContactDamageAlongSegment(Vector3 segmentStart, Vector3 segmentEnd, Vector3 hitDirection)
    {
        if (!CanContinueActiveAttack())
            return;

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            segmentStart,
            segmentEnd,
            Mathf.Max(0.05f, weaponHitRadius),
            hitBuffer,
            DestructibleDebrisCollision.ExcludeDebrisLayer(hitMask.value),
            QueryTriggerInteraction.Ignore);

        Vector3 hitOrigin = Vector3.Lerp(segmentStart, segmentEnd, 0.5f);
        for (int i = 0; i < hitCount; i++)
        {
            if (!CanContinueActiveAttack())
                return;

            Collider hitCollider = hitBuffer[i];
            if (hitCollider == null)
                continue;

            PlayerHealth playerHealth = hitCollider.GetComponentInParent<PlayerHealth>();
            if (playerHealth == null || !playerHealth.IsAlive || !playerHealth.CanBeTargetedByEnemy)
                continue;

            if (!hitTargetsThisAttack.Add(playerHealth))
                continue;

            Vector3 hitPoint = hitCollider.ClosestPoint(hitOrigin);

            if (!CanContinueActiveAttack())
                return;

            playerHealth.ReceiveEnemyDamage(
                attackDamage,
                gameObject,
                hitPoint,
                hitDirection,
                playerDamageAnimation,
                playerCameraImpactType);
        }
    }

    private bool TryGetWeaponSegment(out Vector3 weaponBasePosition, out Vector3 weaponTipPosition, out Vector3 hitDirection)
    {
        Transform resolvedWeaponRoot = weaponRoot != null
            ? weaponRoot
            : (enemySetup != null ? enemySetup.AttackOrigin : transform);

        if (resolvedWeaponRoot == null)
        {
            weaponBasePosition = transform.position;
            weaponTipPosition = transform.position;
            hitDirection = transform.forward;
            return false;
        }

        weaponBasePosition = resolvedWeaponRoot.position;
        if (weaponTip != null)
            weaponTipPosition = weaponTip.position;
        else
            weaponTipPosition = weaponBasePosition + ResolveFallbackWeaponDirection(resolvedWeaponRoot) * Mathf.Max(0.1f, weaponReach);

        Vector3 bladeVector = weaponTipPosition - weaponBasePosition;
        hitDirection = bladeVector.sqrMagnitude > 0.0001f
            ? bladeVector.normalized
            : ResolveFallbackWeaponDirection(resolvedWeaponRoot);
        return true;
    }

    private Vector3 ResolveForwardArcOrigin()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        Transform resolvedAttackOrigin = enemySetup != null ? enemySetup.AttackOrigin : null;
        if (resolvedAttackOrigin != null && resolvedAttackOrigin != transform)
            return resolvedAttackOrigin.position;

        return transform.position + Vector3.up * Mathf.Max(0f, arcOriginHeight);
    }

    private Vector3 ResolveForwardArcForward()
    {
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.0001f)
            return forward.normalized;

        forward = Vector3.ProjectOnPlane(cachedAttackForward, Vector3.up);
        if (forward.sqrMagnitude > 0.0001f)
            return forward.normalized;

        return Vector3.forward;
    }

    private static bool IsInsideForwardArc(Vector3 planarOffset, Vector3 attackForward, float hitRange, float minForwardDot)
    {
        if (planarOffset.sqrMagnitude <= 0.0001f)
            return true;

        float safeHitRange = Mathf.Max(0.1f, hitRange);
        if (planarOffset.sqrMagnitude > safeHitRange * safeHitRange)
            return false;

        return Vector3.Dot(attackForward, planarOffset.normalized) >= minForwardDot;
    }

    private static Vector3 ResolveForwardArcHitPoint(Vector3 origin, Vector3 attackForward, float hitRange, Collider hitCollider)
    {
        if (hitCollider == null)
            return origin + attackForward * hitRange;

        Vector3 sampleCenter = origin + attackForward * (Mathf.Max(0.1f, hitRange) * 0.5f);
        Vector3 closestPoint = hitCollider.ClosestPoint(sampleCenter);
        return IsFiniteVector(closestPoint) ? closestPoint : sampleCenter;
    }

    private Vector3 ResolveFallbackWeaponDirection(Transform resolvedWeaponRoot)
    {
        Vector3 fallbackDirection = cachedAttackForward;
        Vector3 planarForward = Vector3.ProjectOnPlane(fallbackDirection, Vector3.up);
        if (planarForward.sqrMagnitude > 0.0001f)
            return planarForward.normalized;

        if (resolvedWeaponRoot != null && resolvedWeaponRoot.parent != null)
        {
            Vector3 armDirection = resolvedWeaponRoot.position - resolvedWeaponRoot.parent.position;
            if (armDirection.sqrMagnitude > 0.0001f)
                return armDirection.normalized;
        }

        Vector3 selfForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (selfForward.sqrMagnitude > 0.0001f)
            return selfForward.normalized;

        return Vector3.forward;
    }

    private bool IsAttackHitWindowActive()
    {
        Animator animator = enemySetup != null ? enemySetup.Animator : GetComponentInChildren<Animator>(true);
        if (animator == null)
            return true;

        return TryGetActiveAttackStateInfo(animator, out AnimatorStateInfo attackStateInfo)
            && IsWithinHitWindow(attackStateInfo.normalizedTime);
    }

    private bool MatchesAttackState(AnimatorStateInfo stateInfo)
    {
        if (attackStateHash == 0)
            RefreshAttackStateHash();

        return stateInfo.fullPathHash == attackStateHash
            || stateInfo.IsName(string.IsNullOrWhiteSpace(attackStatePath) ? DefaultAttackStatePath : attackStatePath.Trim());
    }

    private bool IsWithinHitWindow(float normalizedTime)
    {
        float wrappedTime = Mathf.Repeat(normalizedTime, 1f);
        float hitWindowStart = Mathf.Clamp01(attackHitStartNormalizedTime);
        float hitWindowEnd = Mathf.Clamp01(Mathf.Max(attackHitStartNormalizedTime, attackHitEndNormalizedTime));
        return wrappedTime >= hitWindowStart && wrappedTime <= hitWindowEnd;
    }

    private bool TryGetActiveAttackStateInfo(Animator animator, out AnimatorStateInfo attackStateInfo)
    {
        attackStateInfo = default;
        if (animator == null)
            return false;

        AnimatorStateInfo currentStateInfo = animator.GetCurrentAnimatorStateInfo(0);
        if (MatchesAttackState(currentStateInfo))
        {
            attackStateInfo = currentStateInfo;
            return true;
        }

        if (!animator.IsInTransition(0))
            return false;

        AnimatorStateInfo nextStateInfo = animator.GetNextAnimatorStateInfo(0);
        if (!MatchesAttackState(nextStateInfo))
            return false;

        attackStateInfo = nextStateInfo;
        return true;
    }

    private bool CanDealDamageLocally()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        return enemySetup == null || enemySetup.HasAuthority;
    }

    private bool CanContinueActiveAttack()
    {
        return CanDealDamageLocally() && IsAttackLocked && IsEffectivelyAlive();
    }

    private bool IsEffectivelyAlive()
    {
        if (!isAlive)
            return false;

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        return enemyHealth == null || enemyHealth.IsAlive;
    }

    private float GetCurrentAttackNormalizedTime()
    {
        if (!IsAttackLocked || activeAttackDuration <= 0.0001f)
            return 1f;

        return Mathf.Clamp01((Time.time - attackStartedAt) / activeAttackDuration);
    }

    private void ResetActiveAttack()
    {
        hitTargetsThisAttack.Clear();
        hasPreviousWeaponTip = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawHitboxGizmo || !useForwardArcHitbox)
            return;

        Vector3 origin = ResolveForwardArcOrigin();
        Vector3 forward = ResolveForwardArcForward();
        DrawForwardArcGizmo(origin, forward, Mathf.Max(0.1f, arcHitRange), Mathf.Clamp(arcHitAngle, 1f, 180f));
    }

    private void DrawForwardArcGizmo(Vector3 origin, Vector3 forward, float range, float angle)
    {
        float halfAngle = angle * 0.5f;
        Vector3 leftDirection = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
        Vector3 rightDirection = Quaternion.AngleAxis(halfAngle, Vector3.up) * forward;

#if UNITY_EDITOR
        Handles.color = hitboxGizmoColor;
        Handles.DrawSolidArc(origin, Vector3.up, leftDirection, angle, range);
        Handles.color = new Color(hitboxGizmoColor.r, hitboxGizmoColor.g, hitboxGizmoColor.b, 1f);
        Handles.DrawWireArc(origin, Vector3.up, leftDirection, angle, range);
#endif

        Gizmos.color = new Color(hitboxGizmoColor.r, hitboxGizmoColor.g, hitboxGizmoColor.b, 1f);
        Gizmos.DrawLine(origin, origin + leftDirection * range);
        Gizmos.DrawLine(origin, origin + rightDirection * range);
        Gizmos.DrawLine(origin, origin + forward * range);

        Vector3 previousPoint = origin + leftDirection * range;
        for (int i = 1; i <= ArcGizmoSegments; i++)
        {
            float t = i / (float)ArcGizmoSegments;
            Vector3 direction = Quaternion.AngleAxis(Mathf.Lerp(-halfAngle, halfAngle, t), Vector3.up) * forward;
            Vector3 currentPoint = origin + direction * range;
            Gizmos.DrawLine(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z));
    }

    private void RefreshAttackStateHash()
    {
        string requestedStatePath = string.IsNullOrWhiteSpace(attackStatePath)
            ? DefaultAttackStatePath
            : attackStatePath.Trim();

        attackStateHash = Animator.StringToHash(requestedStatePath);
    }

    private float ResolveAttackAnimationDuration()
    {
        Animator animator = enemySetup != null ? enemySetup.Animator : GetComponentInChildren<Animator>(true);
        RuntimeAnimatorController runtimeAnimatorController = animator != null ? animator.runtimeAnimatorController : null;
        if (runtimeAnimatorController == null)
            return 0f;

        string requestedClipName = string.IsNullOrWhiteSpace(attackAnimationClipName)
            ? DefaultAttackAnimationClipName
            : attackAnimationClipName.Trim();

        AnimationClip[] animationClips = runtimeAnimatorController.animationClips;
        for (int i = 0; i < animationClips.Length; i++)
        {
            AnimationClip animationClip = animationClips[i];
            if (animationClip == null)
                continue;

            if (animationClip.name.IndexOf(requestedClipName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return Mathf.Max(0.05f, animationClip.length);
        }

        return 0f;
    }

    private void ResolveWeaponTransforms()
    {
        Transform searchRoot = enemySetup != null ? enemySetup.ModelRoot : transform;
        if (searchRoot == null)
            searchRoot = transform;

        if (weaponTip == null)
            weaponTip = FindBestNamedChild(searchRoot, WeaponTipCandidateNames);

        if (weaponRoot == null)
        {
            weaponRoot = FindBestNamedChild(searchRoot, WeaponRootCandidateNames);
            if (weaponRoot == null && weaponTip != null)
                weaponRoot = weaponTip.parent;
        }

        if (weaponRoot == null)
            weaponRoot = enemySetup != null ? enemySetup.AttackOrigin : transform;
    }

    private static Transform FindBestNamedChild(Transform root, string[] candidateNames)
    {
        if (root == null || candidateNames == null || candidateNames.Length == 0)
            return null;

        Transform[] childTransforms = root.GetComponentsInChildren<Transform>(true);
        for (int candidateIndex = 0; candidateIndex < candidateNames.Length; candidateIndex++)
        {
            string candidateName = candidateNames[candidateIndex];
            for (int i = 0; i < childTransforms.Length; i++)
            {
                Transform childTransform = childTransforms[i];
                if (childTransform != null && string.Equals(childTransform.name, candidateName, System.StringComparison.OrdinalIgnoreCase))
                    return childTransform;
            }
        }

        for (int candidateIndex = 0; candidateIndex < candidateNames.Length; candidateIndex++)
        {
            string candidateName = candidateNames[candidateIndex];
            for (int i = 0; i < childTransforms.Length; i++)
            {
                Transform childTransform = childTransforms[i];
                if (childTransform == null)
                    continue;

                if (childTransform.name.IndexOf(candidateName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return childTransform;
            }
        }

        return null;
    }
}
