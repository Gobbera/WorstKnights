using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(25)]
[RequireComponent(typeof(PlayerMovement), typeof(PhotonView))]
public class PlayerMeleeAttack : MonoBehaviour
{
    private const string FirstPersonCameraName = "FP_Camera";
    private const string HandsCameraName = "Hands Camera";
    private const int MaxHitColliders = 16;
    private const float MinimumCameraFeedbackTime = 0.0001f;

    [Serializable]
    public sealed class AttackHitboxWindow
    {
        [SerializeField] [Range(1, MovementConfig.AttackComboStepCount)] private int comboStep = 1;
        [SerializeField] [Min(0)] private int damageStartFrame;
        [SerializeField] [Min(1f)] private float framesPerSecond = 30f;
        [SerializeField] [Min(0.01f)] private float activeDuration = 0.12f;
        [SerializeField] [Min(0.1f)] private float targetDistance = 1.2f;
        [SerializeField] [Min(0.05f)] private float hitRadius = 1f;
        [Header("Impact VFX")]
        [SerializeField] [Tooltip("Angle in degrees sent to hit impact VFX. Triangle A receives this as positive; Triangle B receives it as negative.")] private float impactVfxAttackAngle = 90f;
        [Header("Camera")]
        [SerializeField] private PlayerCameraImpactType playerCameraImpactType = PlayerCameraImpactType.DefaultHit;
        [SerializeField] private bool applyCameraFeedback = true;
        [SerializeField] private bool applyFovFeedbackToHandsCamera = true;
        [SerializeField] [Tooltip("Positive values widen the FOV. Negative values narrow it.")] private float attackFovPercentChange = 8f;
        [SerializeField] [Min(0f)] private float attackFovReachTime = 0.05f;
        [SerializeField] [Min(0f)] private float attackFovHoldTime;
        [SerializeField] [Min(0f)] private float attackFovRelaxTime = 0.16f;
        [SerializeField] private Vector3 attackCameraRotation = new Vector3(-2f, 0f, 0f);

        public AttackHitboxWindow()
        {
        }

        public AttackHitboxWindow(
            int comboStep,
            int damageStartFrame,
            float framesPerSecond,
            float activeDuration,
            float targetDistance,
            float hitRadius)
        {
            this.comboStep = comboStep;
            this.damageStartFrame = damageStartFrame;
            this.framesPerSecond = framesPerSecond;
            this.activeDuration = activeDuration;
            this.targetDistance = targetDistance;
            this.hitRadius = hitRadius;
        }

        public int ComboStep => Mathf.Clamp(comboStep, 1, MovementConfig.AttackComboStepCount);
        public float DamageStartDelay => Mathf.Max(0, damageStartFrame) / Mathf.Max(1f, framesPerSecond);
        public float ActiveDuration => Mathf.Max(0.01f, activeDuration);
        public float TargetDistance => Mathf.Max(0.1f, targetDistance);
        public float HitRadius => Mathf.Max(0.05f, hitRadius);
        public float ImpactVfxAttackAngle => Mathf.Abs(impactVfxAttackAngle);
        public PlayerCameraImpactType PlayerCameraImpactType => playerCameraImpactType;
        public bool ApplyCameraFeedback => applyCameraFeedback;
        public bool ApplyFovFeedbackToHandsCamera => applyFovFeedbackToHandsCamera;
        public float AttackFovPercentChange => attackFovPercentChange;
        public float AttackFovReachTime => Mathf.Max(0f, attackFovReachTime);
        public float AttackFovHoldTime => Mathf.Max(0f, attackFovHoldTime);
        public float AttackFovRelaxTime => Mathf.Max(0f, attackFovRelaxTime);
        public Vector3 AttackCameraRotation => attackCameraRotation;
        public bool HasCameraFeedback => applyCameraFeedback
            && (Mathf.Abs(attackFovPercentChange) > 0.001f || attackCameraRotation.sqrMagnitude > 0.000001f);
    }

    [Header("Attack")]
    [SerializeField] [Min(0f)] private float attackDamage = 25f;
    [SerializeField] private List<AttackHitboxWindow> hitboxWindows = new List<AttackHitboxWindow>
    {
        new AttackHitboxWindow(1, 0, 30f, 0.12f, 1.2f, 1f),
        new AttackHitboxWindow(2, 0, 30f, 0.12f, 1.25f, 1f),
        new AttackHitboxWindow(3, 0, 30f, 0.14f, 1.3f, 1.05f)
    };
    [SerializeField] [HideInInspector] [Min(0.1f)] private float hitRadius = 1f;
    [SerializeField] [HideInInspector] [Min(0.1f)] private float hitDistance = 1.2f;
    [SerializeField] [Min(0f)] private float attackOriginHeight = 1.2f;
    [SerializeField] private bool useVerticalCameraAim = true;
    [SerializeField] private LayerMask hitMask = Physics.DefaultRaycastLayers;

    [Header("Hitbox Preview")]
    [SerializeField] private bool drawHitboxGizmo = true;
    [SerializeField] [Range(1, MovementConfig.AttackComboStepCount)] private int previewComboStep = 1;
    [SerializeField] private Color hitboxGizmoColor = new Color(1f, 0.15f, 0.05f, 0.35f);

    [Header("References")]
    [SerializeField] private Transform attackOrigin;
    [SerializeField] private Transform attackDirection;

    private readonly Collider[] hitBuffer = new Collider[MaxHitColliders];
    private readonly HashSet<Component> processedTargets = new HashSet<Component>();

    private PlayerMovement playerMovement;
    private PlayerMovementFovFeedback movementFovFeedback;
    private PlayerHealth playerHealth;
    private PhotonView photonView;
    private int lastAttackSequence;
    private AttackHitboxWindow activeHitboxWindow;
    private float activeHitboxStartTime;
    private float activeHitboxEndTime;
    private bool hasActiveHitboxWindow;
    private Camera attackFeedbackCamera;
    private Camera attackFeedbackHandsCamera;
    private AttackHitboxWindow activeCameraFeedbackWindow;
    private ItemDefinition nextAttackItem;
    private float activeAttackDamage;
    private float attackFeedbackCameraBaseFov;
    private float attackFeedbackHandsCameraBaseFov;
    private float attackCameraFeedbackStartTime;
    private float attackCameraFeedbackPeakTime;
    private float attackCameraFeedbackRelaxStartTime;
    private float attackCameraFeedbackEndTime;
    private float attackCameraFeedbackStartWeight;
    private float attackCameraFeedbackWeight;
    private bool hasAttackCameraFeedback;
    private bool hasAttackFeedbackCameraBaseFov;
    private bool hasAttackFeedbackHandsCameraBaseFov;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        movementFovFeedback = GetComponent<PlayerMovementFovFeedback>();
        playerHealth = GetComponent<PlayerHealth>();
        photonView = GetComponent<PhotonView>();
        ResolveAttackTransforms();
    }

    private void OnEnable()
    {
        lastAttackSequence = playerMovement != null ? playerMovement.AttackAnimationSequence : 0;
        activeAttackDamage = Mathf.Max(0f, attackDamage);
        nextAttackItem = null;
        CancelActiveHitboxWindow();
    }

    private void Update()
    {
        if (photonView != null && !photonView.IsMine)
            return;

        if (playerMovement == null)
            return;

        if (playerMovement.AttackAnimationSequence != lastAttackSequence)
        {
            lastAttackSequence = playerMovement.AttackAnimationSequence;
            ScheduleHitboxWindow(playerMovement.AttackComboStep);
            TriggerAttackCameraFeedback(activeHitboxWindow);
        }

        UpdateActiveHitboxWindow();
    }

    private void LateUpdate()
    {
        if (photonView != null && !photonView.IsMine)
        {
            ResetAttackCameraFeedback();
            return;
        }

        UpdateAttackCameraFeedback();
    }

    private void OnDisable()
    {
        nextAttackItem = null;
        ResetAttackCameraFeedback();
    }

    public void SetNextAttackItem(ItemDefinition itemDefinition)
    {
        nextAttackItem = itemDefinition;
    }

    private void ScheduleHitboxWindow(int comboStep)
    {
        activeHitboxWindow = ResolveHitboxWindow(comboStep);
        if (activeHitboxWindow == null)
        {
            nextAttackItem = null;
            CancelActiveHitboxWindow();
            return;
        }

        activeAttackDamage = ResolveAttackDamage(nextAttackItem);
        nextAttackItem = null;

        float now = Time.time;
        activeHitboxStartTime = now + activeHitboxWindow.DamageStartDelay;
        activeHitboxEndTime = activeHitboxStartTime + activeHitboxWindow.ActiveDuration;
        hasActiveHitboxWindow = true;
        processedTargets.Clear();
    }

    private void UpdateActiveHitboxWindow()
    {
        if (!hasActiveHitboxWindow || activeHitboxWindow == null)
            return;

        float now = Time.time;
        if (now > activeHitboxEndTime)
        {
            CancelActiveHitboxWindow();
            return;
        }

        if (now < activeHitboxStartTime)
            return;

        TryHitTargets(activeHitboxWindow);
    }

    private void CancelActiveHitboxWindow()
    {
        hasActiveHitboxWindow = false;
        activeHitboxWindow = null;
        activeHitboxStartTime = 0f;
        activeHitboxEndTime = 0f;
        processedTargets.Clear();
    }

    private void TriggerAttackCameraFeedback(AttackHitboxWindow hitboxWindow)
    {
        if (!HasConfiguredAttackCameraFeedback(hitboxWindow))
        {
            ResetAttackCameraFeedback();
            return;
        }

        activeCameraFeedbackWindow = hitboxWindow;
        ResolveAttackCameraFeedbackTargets();
        if (attackFeedbackCamera == null)
            return;

        if (!hasAttackFeedbackCameraBaseFov)
        {
            attackFeedbackCameraBaseFov = attackFeedbackCamera.fieldOfView;
            hasAttackFeedbackCameraBaseFov = true;
        }

        if (!hitboxWindow.ApplyFovFeedbackToHandsCamera)
            RestoreAttackFeedbackHandsCameraFov();

        if (hitboxWindow.ApplyFovFeedbackToHandsCamera && attackFeedbackHandsCamera != null && !hasAttackFeedbackHandsCameraBaseFov)
        {
            attackFeedbackHandsCameraBaseFov = attackFeedbackHandsCamera.fieldOfView;
            hasAttackFeedbackHandsCameraBaseFov = true;
        }

        float now = Time.time;
        float reachTime = hitboxWindow.AttackFovReachTime;
        float holdTime = hitboxWindow.AttackFovHoldTime;
        float relaxTime = hitboxWindow.AttackFovRelaxTime;

        attackCameraFeedbackStartTime = now;
        attackCameraFeedbackPeakTime = now + reachTime;
        attackCameraFeedbackRelaxStartTime = attackCameraFeedbackPeakTime + holdTime;
        attackCameraFeedbackEndTime = attackCameraFeedbackRelaxStartTime + relaxTime;
        attackCameraFeedbackStartWeight = attackCameraFeedbackWeight;
        hasAttackCameraFeedback = true;
    }

    private void UpdateAttackCameraFeedback()
    {
        if (!hasAttackCameraFeedback)
            return;

        ResolveAttackCameraFeedbackTargets();
        if (attackFeedbackCamera == null)
        {
            ResetAttackCameraFeedback();
            return;
        }

        float now = Time.time;
        if (now >= attackCameraFeedbackEndTime)
        {
            attackCameraFeedbackWeight = 0f;
            ApplyAttackCameraFeedback(attackCameraFeedbackWeight);
            ClearAttackCameraFeedbackState();
            return;
        }

        attackCameraFeedbackWeight = EvaluateAttackCameraFeedbackWeight(now);
        ApplyAttackCameraFeedback(attackCameraFeedbackWeight);
    }

    private float EvaluateAttackCameraFeedbackWeight(float now)
    {
        if (now < attackCameraFeedbackPeakTime)
        {
            float reachDuration = Mathf.Max(0f, attackCameraFeedbackPeakTime - attackCameraFeedbackStartTime);
            if (reachDuration <= MinimumCameraFeedbackTime)
                return 1f;

            float normalizedReachTime = Mathf.Clamp01((now - attackCameraFeedbackStartTime) / reachDuration);
            return Mathf.Lerp(attackCameraFeedbackStartWeight, 1f, SmoothStep01(normalizedReachTime));
        }

        if (now < attackCameraFeedbackRelaxStartTime)
            return 1f;

        float relaxDuration = Mathf.Max(0f, attackCameraFeedbackEndTime - attackCameraFeedbackRelaxStartTime);
        if (relaxDuration <= MinimumCameraFeedbackTime)
            return 0f;

        float normalizedRelaxTime = Mathf.Clamp01((now - attackCameraFeedbackRelaxStartTime) / relaxDuration);
        return Mathf.Lerp(1f, 0f, SmoothStep01(normalizedRelaxTime));
    }

    private void ApplyAttackCameraFeedback(float weight)
    {
        AttackHitboxWindow hitboxWindow = activeCameraFeedbackWindow;
        if (hitboxWindow == null)
            return;

        float safeWeight = Mathf.Clamp01(weight);

        if (TryResolveAttackFeedbackBaseFov(useHandsCamera: false, out float cameraBaseFov))
        {
            attackFeedbackCamera.fieldOfView = ResolveAttackFeedbackFov(
                cameraBaseFov,
                hitboxWindow.AttackFovPercentChange,
                safeWeight);
        }

        if (hitboxWindow.ApplyFovFeedbackToHandsCamera
            && TryResolveAttackFeedbackBaseFov(useHandsCamera: true, out float handsCameraBaseFov))
        {
            attackFeedbackHandsCamera.fieldOfView = ResolveAttackFeedbackFov(
                handsCameraBaseFov,
                hitboxWindow.AttackFovPercentChange,
                safeWeight);
        }

        if (attackFeedbackCamera == null)
            return;

        Vector3 cameraRotation = hitboxWindow.AttackCameraRotation;
        if (cameraRotation.sqrMagnitude <= 0.000001f)
            return;

        attackFeedbackCamera.transform.localRotation *= Quaternion.Euler(cameraRotation * safeWeight);
    }

    private float ResolveAttackFeedbackFov(float baseFov, float percentChange, float weight)
    {
        float safeBaseFov = Mathf.Clamp(baseFov, 1f, 179f);
        float targetFov = Mathf.Clamp(safeBaseFov * (1f + percentChange * 0.01f), 1f, 179f);
        return Mathf.Lerp(safeBaseFov, targetFov, weight);
    }

    private void ResetAttackCameraFeedback()
    {
        RestoreAttackFeedbackCameraFov();
        RestoreAttackFeedbackHandsCameraFov();

        ClearAttackCameraFeedbackState();
    }

    private void RestoreAttackFeedbackCameraFov()
    {
        if (attackFeedbackCamera == null)
            return;

        if (TryResolveMovementFov(useHandsCamera: false, out float movementFov))
        {
            attackFeedbackCamera.fieldOfView = movementFov;
            return;
        }

        if (hasAttackFeedbackCameraBaseFov)
            attackFeedbackCamera.fieldOfView = attackFeedbackCameraBaseFov;
    }

    private void RestoreAttackFeedbackHandsCameraFov()
    {
        if (attackFeedbackHandsCamera == null)
            return;

        if (TryResolveMovementFov(useHandsCamera: true, out float movementFov))
        {
            attackFeedbackHandsCamera.fieldOfView = movementFov;
            attackFeedbackHandsCameraBaseFov = 0f;
            hasAttackFeedbackHandsCameraBaseFov = false;
            return;
        }

        if (hasAttackFeedbackHandsCameraBaseFov && attackFeedbackHandsCamera != null)
            attackFeedbackHandsCamera.fieldOfView = attackFeedbackHandsCameraBaseFov;

        attackFeedbackHandsCameraBaseFov = 0f;
        hasAttackFeedbackHandsCameraBaseFov = false;
    }

    private void ClearAttackCameraFeedbackState()
    {
        hasAttackCameraFeedback = false;
        activeCameraFeedbackWindow = null;
        attackCameraFeedbackStartTime = 0f;
        attackCameraFeedbackPeakTime = 0f;
        attackCameraFeedbackRelaxStartTime = 0f;
        attackCameraFeedbackEndTime = 0f;
        attackCameraFeedbackStartWeight = 0f;
        attackCameraFeedbackWeight = 0f;
        attackFeedbackCameraBaseFov = 0f;
        attackFeedbackHandsCameraBaseFov = 0f;
        hasAttackFeedbackCameraBaseFov = false;
        hasAttackFeedbackHandsCameraBaseFov = false;
    }

    private bool HasConfiguredAttackCameraFeedback(AttackHitboxWindow hitboxWindow)
    {
        return hitboxWindow != null && hitboxWindow.HasCameraFeedback;
    }

    private bool TryResolveAttackFeedbackBaseFov(bool useHandsCamera, out float fov)
    {
        if (TryResolveMovementFov(useHandsCamera, out fov))
            return true;

        if (useHandsCamera)
        {
            fov = attackFeedbackHandsCameraBaseFov;
            return hasAttackFeedbackHandsCameraBaseFov && attackFeedbackHandsCamera != null;
        }

        fov = attackFeedbackCameraBaseFov;
        return hasAttackFeedbackCameraBaseFov && attackFeedbackCamera != null;
    }

    private bool TryResolveMovementFov(bool useHandsCamera, out float fov)
    {
        if (movementFovFeedback == null)
            movementFovFeedback = GetComponent<PlayerMovementFovFeedback>();

        if (movementFovFeedback == null || !movementFovFeedback.isActiveAndEnabled)
        {
            fov = 0f;
            return false;
        }

        return useHandsCamera
            ? movementFovFeedback.TryGetCurrentHandsFov(out fov)
            : movementFovFeedback.TryGetCurrentFirstPersonFov(out fov);
    }

    private void ResolveAttackCameraFeedbackTargets()
    {
        if (attackFeedbackCamera == null)
            attackFeedbackCamera = FindCameraByName(FirstPersonCameraName);

        if (attackFeedbackHandsCamera == null)
            attackFeedbackHandsCamera = FindCameraByName(HandsCameraName);
    }

    private static float SmoothStep01(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * (3f - 2f * t);
    }

    private AttackHitboxWindow ResolveHitboxWindow(int comboStep)
    {
        int safeComboStep = Mathf.Clamp(comboStep, 1, MovementConfig.AttackComboStepCount);
        if (hitboxWindows != null)
        {
            for (int i = 0; i < hitboxWindows.Count; i++)
            {
                AttackHitboxWindow hitboxWindow = hitboxWindows[i];
                if (hitboxWindow != null && hitboxWindow.ComboStep == safeComboStep)
                    return hitboxWindow;
            }
        }

        return new AttackHitboxWindow(safeComboStep, 0, 30f, 0.12f, hitDistance, hitRadius);
    }

    private float ResolveAttackDamage(ItemDefinition attackItem)
    {
        if (attackItem != null && IsWeaponUseType(attackItem.UseType))
            return Mathf.Max(0f, attackItem.BaseDamage);

        return Mathf.Max(0f, attackDamage);
    }

    private static bool IsWeaponUseType(ItemUseType useType)
    {
        return useType == ItemUseType.Weapon
            || useType == ItemUseType.MeleeWeapon;
    }

    private void TryHitTargets(AttackHitboxWindow hitboxWindow)
    {
        ResolveAttackTransforms();

        Vector3 origin = ResolveAttackOrigin();
        Vector3 attackForward = ResolveAttackForward();

        Vector3 hitCenter = origin + attackForward * hitboxWindow.TargetDistance;
        float hitProbeDistance = Mathf.Max(0.05f, hitboxWindow.TargetDistance + hitboxWindow.HitRadius);
        int hitCount = Physics.OverlapSphereNonAlloc(
            hitCenter,
            hitboxWindow.HitRadius,
            hitBuffer,
            DestructibleDebrisCollision.ExcludeDebrisLayer(hitMask.value),
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = hitBuffer[i];
            if (hitCollider == null)
                continue;

            IDamageable damageable = hitCollider.GetComponentInParent<IDamageable>();
            IMeleeImpactReceiver impactReceiver = hitCollider.GetComponentInParent<IMeleeImpactReceiver>();
            Component targetComponent = damageable as Component ?? impactReceiver as Component;
            if (targetComponent == null)
                continue;

            PlayerHealth targetPlayerHealth = damageable as PlayerHealth;
            if ((damageable != null && !damageable.IsAlive) || IsSelfTarget(targetComponent, targetPlayerHealth))
                continue;

            if (!processedTargets.Add(targetComponent))
                continue;

            Vector3 resolvedHitPoint = ResolveHitPoint(origin, hitCenter, attackForward, hitProbeDistance, hitCollider);
            DamageInfo damageInfo = new DamageInfo(
                activeAttackDamage,
                gameObject,
                CombatAlignment.Player,
                resolvedHitPoint,
                attackForward,
                PlayerDamageAnimationType.None,
                playerCameraImpact: hitboxWindow.PlayerCameraImpactType,
                impactVfxAttackAngle: hitboxWindow.ImpactVfxAttackAngle,
                hasImpactVfxAttackAngle: true);

            if (damageable is EnemyHealth enemyHealth)
            {
                enemyHealth.ReceivePlayerDamage(damageInfo);
                impactReceiver?.ReceiveMeleeImpact(damageInfo, hitCollider);
                continue;
            }

            if (targetPlayerHealth != null)
            {
                DamageInfo playerDamageInfo = new DamageInfo(
                    activeAttackDamage,
                    gameObject,
                    CombatAlignment.Neutral,
                    resolvedHitPoint,
                    attackForward,
                    PlayerDamageAnimationType.ReactionDamage,
                    hitboxWindow.PlayerCameraImpactType,
                    hitboxWindow.ImpactVfxAttackAngle,
                    hasImpactVfxAttackAngle: true);

                targetPlayerHealth.ReceiveDamage(playerDamageInfo);
                impactReceiver?.ReceiveMeleeImpact(playerDamageInfo, hitCollider);
                continue;
            }

            if (damageable != null)
                damageable.ApplyDamage(damageInfo);

            impactReceiver?.ReceiveMeleeImpact(damageInfo, hitCollider);
        }
    }

    private bool IsSelfTarget(Component targetComponent, PlayerHealth targetPlayerHealth)
    {
        if (targetComponent != null && targetComponent.transform.IsChildOf(transform))
            return true;

        if (targetPlayerHealth == null)
            return false;

        if (playerHealth != null && targetPlayerHealth == playerHealth)
            return true;

        PhotonView targetPhotonView = targetPlayerHealth.GetComponent<PhotonView>();
        return photonView != null
            && targetPhotonView != null
            && photonView.ViewID != 0
            && targetPhotonView.ViewID == photonView.ViewID;
    }

    private static Vector3 ResolveHitPoint(
        Vector3 origin,
        Vector3 hitCenter,
        Vector3 fallbackDirection,
        float hitProbeDistance,
        Collider hitCollider)
    {
        if (hitCollider == null)
            return hitCenter;

        Vector3 rayDirection = hitCenter - origin;
        float rayDistance = rayDirection.magnitude;
        if (rayDistance <= 0.0001f)
        {
            rayDirection = fallbackDirection.sqrMagnitude > 0.0001f ? fallbackDirection.normalized : Vector3.forward;
            rayDistance = Mathf.Max(0.05f, hitProbeDistance);
        }
        else
        {
            rayDirection /= rayDistance;
        }

        if (hitCollider.Raycast(new Ray(origin, rayDirection), out RaycastHit raycastHit, Mathf.Max(rayDistance, hitProbeDistance)))
            return raycastHit.point;

        Vector3 closestFromCenter = hitCollider.ClosestPoint(hitCenter);
        if (IsFiniteVector(closestFromCenter))
            return closestFromCenter;

        Vector3 closestFromOrigin = hitCollider.ClosestPoint(origin);
        if (IsFiniteVector(closestFromOrigin))
            return closestFromOrigin;

        return hitCenter;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z));
    }

    private void ResolveAttackTransforms()
    {
        if (attackOrigin != null && attackDirection != null)
            return;

        Camera firstPersonCamera = FindCameraByName(FirstPersonCameraName);
        if (firstPersonCamera != null)
        {
            attackOrigin = firstPersonCamera.transform;
            attackDirection = firstPersonCamera.transform;
            return;
        }

        attackOrigin = transform;
        attackDirection = transform;
    }

    private Vector3 ResolveAttackOrigin()
    {
        if (attackOrigin != null)
            return attackOrigin.position;

        Transform firstPersonCamera = FindFirstPersonCameraTransform();
        if (firstPersonCamera != null)
            return firstPersonCamera.position;

        return transform.position + Vector3.up * attackOriginHeight;
    }

    private Vector3 ResolveAttackForward()
    {
        Transform resolvedDirection = attackDirection != null ? attackDirection : FindFirstPersonCameraTransform();
        Vector3 forward = resolvedDirection != null ? resolvedDirection.forward : transform.forward;
        if (!useVerticalCameraAim)
            forward = Vector3.ProjectOnPlane(forward, Vector3.up);

        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = useVerticalCameraAim
                ? transform.forward
                : Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }

        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;

        return forward.normalized;
    }

    private Transform FindFirstPersonCameraTransform()
    {
        Camera firstPersonCamera = FindCameraByName(FirstPersonCameraName);
        return firstPersonCamera != null ? firstPersonCamera.transform : null;
    }

    private Camera FindCameraByName(string cameraName)
    {
        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera playerCamera = cameras[i];
            if (playerCamera == null)
                continue;

            if (string.Equals(playerCamera.gameObject.name, cameraName, StringComparison.Ordinal))
                return playerCamera;
        }

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawHitboxGizmo)
            return;

        AttackHitboxWindow hitboxWindow = ResolveHitboxWindow(previewComboStep);
        if (hitboxWindow == null)
            return;

        Vector3 origin = ResolveAttackOrigin();
        Vector3 attackForward = ResolveAttackForward();

        Gizmos.color = hitboxGizmoColor;
        Gizmos.DrawSphere(origin + attackForward * hitboxWindow.TargetDistance, hitboxWindow.HitRadius);
        Gizmos.color = new Color(hitboxGizmoColor.r, hitboxGizmoColor.g, hitboxGizmoColor.b, 1f);
        Gizmos.DrawWireSphere(origin + attackForward * hitboxWindow.TargetDistance, hitboxWindow.HitRadius);
    }
}
