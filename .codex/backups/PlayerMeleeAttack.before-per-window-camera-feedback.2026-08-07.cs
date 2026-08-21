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

    [Header("Camera")]
    [SerializeField] private PlayerCameraImpactType playerCameraImpactType = PlayerCameraImpactType.DefaultHit;
    [SerializeField] private bool applyAttackCameraFeedback = true;
    [SerializeField] private bool applyFovFeedbackToHandsCamera = true;
    [SerializeField] private float attackFovPercentChange = 8f;
    [SerializeField] [Min(0f)] private float attackFovReachTime = 0.05f;
    [SerializeField] [Min(0f)] private float attackFovRelaxTime = 0.16f;
    [SerializeField] private float attackCameraHorizontalRotation;
    [SerializeField] private float attackCameraVerticalRotation = -2f;

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
    private PhotonView photonView;
    private int lastAttackSequence;
    private AttackHitboxWindow activeHitboxWindow;
    private float activeHitboxStartTime;
    private float activeHitboxEndTime;
    private bool hasActiveHitboxWindow;
    private Camera attackFeedbackCamera;
    private Camera attackFeedbackHandsCamera;
    private float attackFeedbackCameraBaseFov;
    private float attackFeedbackHandsCameraBaseFov;
    private float attackCameraFeedbackStartTime;
    private float attackCameraFeedbackPeakTime;
    private float attackCameraFeedbackEndTime;
    private float attackCameraFeedbackStartWeight;
    private float attackCameraFeedbackWeight;
    private bool hasAttackCameraFeedback;
    private bool hasAttackFeedbackCameraBaseFov;
    private bool hasAttackFeedbackHandsCameraBaseFov;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        photonView = GetComponent<PhotonView>();
        ResolveAttackTransforms();
    }

    private void OnEnable()
    {
        lastAttackSequence = playerMovement != null ? playerMovement.AttackAnimationSequence : 0;
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
            TriggerAttackCameraFeedback();
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
        ResetAttackCameraFeedback();
    }

    private void ScheduleHitboxWindow(int comboStep)
    {
        activeHitboxWindow = ResolveHitboxWindow(comboStep);
        if (activeHitboxWindow == null)
        {
            CancelActiveHitboxWindow();
            return;
        }

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

    private void TriggerAttackCameraFeedback()
    {
        if (!HasConfiguredAttackCameraFeedback())
        {
            ResetAttackCameraFeedback();
            return;
        }

        ResolveAttackCameraFeedbackTargets();
        if (attackFeedbackCamera == null)
            return;

        if (!hasAttackFeedbackCameraBaseFov)
        {
            attackFeedbackCameraBaseFov = attackFeedbackCamera.fieldOfView;
            hasAttackFeedbackCameraBaseFov = true;
        }

        if (applyFovFeedbackToHandsCamera && attackFeedbackHandsCamera != null && !hasAttackFeedbackHandsCameraBaseFov)
        {
            attackFeedbackHandsCameraBaseFov = attackFeedbackHandsCamera.fieldOfView;
            hasAttackFeedbackHandsCameraBaseFov = true;
        }

        float now = Time.time;
        float reachTime = Mathf.Max(0f, attackFovReachTime);
        float relaxTime = Mathf.Max(0f, attackFovRelaxTime);

        attackCameraFeedbackStartTime = now;
        attackCameraFeedbackPeakTime = now + reachTime;
        attackCameraFeedbackEndTime = attackCameraFeedbackPeakTime + relaxTime;
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

        float relaxDuration = Mathf.Max(0f, attackCameraFeedbackEndTime - attackCameraFeedbackPeakTime);
        if (relaxDuration <= MinimumCameraFeedbackTime)
            return 0f;

        float normalizedRelaxTime = Mathf.Clamp01((now - attackCameraFeedbackPeakTime) / relaxDuration);
        return Mathf.Lerp(1f, 0f, SmoothStep01(normalizedRelaxTime));
    }

    private void ApplyAttackCameraFeedback(float weight)
    {
        float safeWeight = Mathf.Clamp01(weight);

        if (hasAttackFeedbackCameraBaseFov && attackFeedbackCamera != null)
            attackFeedbackCamera.fieldOfView = ResolveAttackFeedbackFov(attackFeedbackCameraBaseFov, safeWeight);

        if (hasAttackFeedbackHandsCameraBaseFov && attackFeedbackHandsCamera != null)
            attackFeedbackHandsCamera.fieldOfView = ResolveAttackFeedbackFov(attackFeedbackHandsCameraBaseFov, safeWeight);

        if (attackFeedbackCamera == null)
            return;

        if (Mathf.Abs(attackCameraHorizontalRotation) <= 0.001f && Mathf.Abs(attackCameraVerticalRotation) <= 0.001f)
            return;

        attackFeedbackCamera.transform.localRotation *= Quaternion.Euler(
            attackCameraVerticalRotation * safeWeight,
            attackCameraHorizontalRotation * safeWeight,
            0f);
    }

    private float ResolveAttackFeedbackFov(float baseFov, float weight)
    {
        float targetFov = baseFov * (1f + attackFovPercentChange * 0.01f);
        return Mathf.Clamp(Mathf.Lerp(baseFov, targetFov, weight), 1f, 179f);
    }

    private void ResetAttackCameraFeedback()
    {
        if (hasAttackFeedbackCameraBaseFov && attackFeedbackCamera != null)
            attackFeedbackCamera.fieldOfView = attackFeedbackCameraBaseFov;

        if (hasAttackFeedbackHandsCameraBaseFov && attackFeedbackHandsCamera != null)
            attackFeedbackHandsCamera.fieldOfView = attackFeedbackHandsCameraBaseFov;

        ClearAttackCameraFeedbackState();
    }

    private void ClearAttackCameraFeedbackState()
    {
        hasAttackCameraFeedback = false;
        attackCameraFeedbackStartTime = 0f;
        attackCameraFeedbackPeakTime = 0f;
        attackCameraFeedbackEndTime = 0f;
        attackCameraFeedbackStartWeight = 0f;
        attackCameraFeedbackWeight = 0f;
        attackFeedbackCameraBaseFov = 0f;
        attackFeedbackHandsCameraBaseFov = 0f;
        hasAttackFeedbackCameraBaseFov = false;
        hasAttackFeedbackHandsCameraBaseFov = false;
    }

    private bool HasConfiguredAttackCameraFeedback()
    {
        return applyAttackCameraFeedback
            && (Mathf.Abs(attackFovPercentChange) > 0.001f
                || Mathf.Abs(attackCameraHorizontalRotation) > 0.001f
                || Mathf.Abs(attackCameraVerticalRotation) > 0.001f);
    }

    private void ResolveAttackCameraFeedbackTargets()
    {
        if (attackFeedbackCamera == null)
            attackFeedbackCamera = FindCameraByName(FirstPersonCameraName);

        if (applyFovFeedbackToHandsCamera && attackFeedbackHandsCamera == null)
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
            hitMask,
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

            if ((damageable != null && !damageable.IsAlive) || targetComponent.transform.IsChildOf(transform))
                continue;

            if (!processedTargets.Add(targetComponent))
                continue;

            Vector3 resolvedHitPoint = ResolveHitPoint(origin, hitCenter, attackForward, hitProbeDistance, hitCollider);
            DamageInfo damageInfo = new DamageInfo(
                attackDamage,
                gameObject,
                CombatAlignment.Player,
                resolvedHitPoint,
                attackForward,
                PlayerDamageAnimationType.None,
                playerCameraImpactType);

            if (damageable is EnemyHealth enemyHealth)
            {
                enemyHealth.ReceivePlayerDamage(damageInfo);
                continue;
            }

            if (damageable is PlayerHealth playerHealth)
            {
                playerHealth.ReceiveDamage(damageInfo);
                continue;
            }

            if (damageable != null)
                damageable.ApplyDamage(damageInfo);

            impactReceiver?.ReceiveMeleeImpact(damageInfo, hitCollider);
        }
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
