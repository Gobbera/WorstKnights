using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum PlayerMeleeAttackShape
{
    Ball = 0,
    Arc = 1,
    Box = 2
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(25)]
[RequireComponent(typeof(PlayerMovement), typeof(PhotonView), typeof(AudioEmitter))]
public class PlayerMeleeAttack : MonoBehaviour
{
    private const string FirstPersonCameraName = "FP_Camera";
    private const string HandsCameraName = "Hands Camera";
    private const string AttackAudioAnchorName = "AttackAudioAnchor";
    private const int MaxHitColliders = 16;
    private const int ArcGizmoSegments = 28;
    private const float MinimumCameraFeedbackTime = 0.0001f;

    [Serializable]
    public sealed class AttackConfig
    {
        [Header("Combo")]
        [SerializeField] [Range(1, MovementConfig.AttackComboStepCount)] private int comboStep = 1;
        [SerializeField] private PlayerMeleeAttackShape hitShape = PlayerMeleeAttackShape.Ball;
        [Header("Hitbox")]
        [SerializeField] [Min(0)] private int damageStartFrame;
        [SerializeField] [Min(1f)] private float framesPerSecond = 30f;
        [SerializeField] [Min(0.01f)] private float activeDuration = 0.12f;
        [SerializeField] [Min(0.1f)] private float targetDistance = 1.2f;
        [SerializeField] [Min(0.05f)] private float hitRadius = 1f;
        [Header("Arc Shape")]
        [SerializeField] [Min(0.1f)] private float arcRange = 1.6f;
        [SerializeField] [Range(1f, 180f)] private float arcAngle = 120f;
        [SerializeField] [Min(0f)] private float arcHalfHeight = 1.2f;
        [Header("Box Shape")]
        [SerializeField] [Min(0f)] private float boxCenterDistance = 1.2f;
        [SerializeField] private Vector3 boxSize = new Vector3(0.8f, 0.8f, 0.8f);
        [Header("Audio")]
        [SerializeField] private AudioCue swingAudioCue;
        [SerializeField] [Min(0f)] private float swingAudioDelay;
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

        public AttackConfig()
        {
        }

        public AttackConfig(
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
            arcRange = Mathf.Max(0.1f, targetDistance + hitRadius);
            boxCenterDistance = targetDistance;
            boxSize = Vector3.one * Mathf.Max(0.1f, hitRadius * 2f);
        }

        public int ComboStep => Mathf.Clamp(comboStep, 1, MovementConfig.AttackComboStepCount);
        public PlayerMeleeAttackShape HitShape => hitShape;
        public float DamageStartDelay => Mathf.Max(0, damageStartFrame) / Mathf.Max(1f, framesPerSecond);
        public float ActiveDuration => Mathf.Max(0.01f, activeDuration);
        public float TargetDistance => Mathf.Max(0.1f, targetDistance);
        public float HitRadius => Mathf.Max(0.05f, hitRadius);
        public float ArcRange => Mathf.Max(0.1f, arcRange);
        public float ArcAngle => Mathf.Clamp(arcAngle, 1f, 180f);
        public float ArcHalfHeight => Mathf.Max(0f, arcHalfHeight);
        public float BoxCenterDistance => Mathf.Max(0f, boxCenterDistance);
        public Vector3 BoxSize => new Vector3(
            Mathf.Max(0.05f, Mathf.Abs(boxSize.x)),
            Mathf.Max(0.05f, Mathf.Abs(boxSize.y)),
            Mathf.Max(0.05f, Mathf.Abs(boxSize.z)));
        public Vector3 BoxHalfExtents => BoxSize * 0.5f;
        public AudioCue SwingAudioCue => swingAudioCue;
        public float SwingAudioDelay => Mathf.Max(0f, swingAudioDelay);
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

    private readonly struct ScheduledSwingAudio
    {
        public ScheduledSwingAudio(AudioCue cue, float playTime)
        {
            Cue = cue;
            PlayTime = playTime;
        }

        public AudioCue Cue { get; }
        public float PlayTime { get; }
    }

    [Header("Attack")]
    [SerializeField] [Min(0f)] private float attackDamage = 25f;
    [FormerlySerializedAs("hitboxWindows")]
    [SerializeField] private List<AttackConfig> attackConfigs = new List<AttackConfig>
    {
        new AttackConfig(1, 0, 30f, 0.12f, 1.2f, 1f),
        new AttackConfig(2, 0, 30f, 0.12f, 1.25f, 1f),
        new AttackConfig(3, 0, 30f, 0.14f, 1.3f, 1.05f)
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
    [SerializeField] private Transform attackAudioAnchor;
    [SerializeField] private AudioEmitter audioEmitter;

    private readonly Collider[] hitBuffer = new Collider[MaxHitColliders];
    private readonly HashSet<Component> processedTargets = new HashSet<Component>();
    private readonly List<ScheduledSwingAudio> pendingSwingAudio = new List<ScheduledSwingAudio>();

    private PlayerMovement playerMovement;
    private PlayerMovementFovFeedback movementFovFeedback;
    private PlayerHealth playerHealth;
    private PhotonView photonView;
    private int lastAttackSequence;
    private AttackConfig activeAttackConfig;
    private float activeHitboxStartTime;
    private float activeHitboxEndTime;
    private bool hasActiveHitboxWindow;
    private Camera attackFeedbackCamera;
    private Camera attackFeedbackHandsCamera;
    private AttackConfig activeCameraFeedbackConfig;
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
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        lastAttackSequence = playerMovement != null ? playerMovement.AttackAnimationSequence : 0;
        activeAttackDamage = Mathf.Max(0f, attackDamage);
        nextAttackItem = null;
        CancelActiveHitboxWindow();
        CancelPendingSwingAudio();
    }

    private void Update()
    {
        if (playerMovement == null)
            return;

        if (playerMovement.AttackAnimationSequence != lastAttackSequence)
        {
            lastAttackSequence = playerMovement.AttackAnimationSequence;
            AttackConfig attackConfig = ResolveAttackConfig(playerMovement.AttackComboStep);
            ScheduleSwingAudio(attackConfig);

            if (HasLocalAuthority())
            {
                ScheduleHitboxWindow(attackConfig);
                TriggerAttackCameraFeedback(activeAttackConfig);
            }
            else
            {
                CancelActiveHitboxWindow();
                ResetAttackCameraFeedback();
            }
        }

        UpdatePendingSwingAudio();

        if (!HasLocalAuthority())
            return;

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
        CancelPendingSwingAudio();
        ResetAttackCameraFeedback();
    }

    public void SetNextAttackItem(ItemDefinition itemDefinition)
    {
        nextAttackItem = itemDefinition;
    }

    private void ScheduleHitboxWindow(AttackConfig attackConfig)
    {
        activeAttackConfig = attackConfig;
        if (activeAttackConfig == null)
        {
            nextAttackItem = null;
            CancelActiveHitboxWindow();
            return;
        }

        activeAttackDamage = ResolveAttackDamage(nextAttackItem);
        nextAttackItem = null;

        float now = Time.time;
        activeHitboxStartTime = now + activeAttackConfig.DamageStartDelay;
        activeHitboxEndTime = activeHitboxStartTime + activeAttackConfig.ActiveDuration;
        hasActiveHitboxWindow = true;
        processedTargets.Clear();
    }

    private void UpdateActiveHitboxWindow()
    {
        if (!hasActiveHitboxWindow || activeAttackConfig == null)
            return;

        float now = Time.time;
        if (now > activeHitboxEndTime)
        {
            CancelActiveHitboxWindow();
            return;
        }

        if (now < activeHitboxStartTime)
            return;

        TryHitTargets(activeAttackConfig);
    }

    private void CancelActiveHitboxWindow()
    {
        hasActiveHitboxWindow = false;
        activeAttackConfig = null;
        activeHitboxStartTime = 0f;
        activeHitboxEndTime = 0f;
        processedTargets.Clear();
    }

    private void ScheduleSwingAudio(AttackConfig attackConfig)
    {
        if (attackConfig == null || attackConfig.SwingAudioCue == null)
            return;

        pendingSwingAudio.Add(new ScheduledSwingAudio(
            attackConfig.SwingAudioCue,
            Time.time + attackConfig.SwingAudioDelay));
    }

    private void UpdatePendingSwingAudio()
    {
        for (int i = 0; i < pendingSwingAudio.Count;)
        {
            ScheduledSwingAudio scheduledSwing = pendingSwingAudio[i];
            if (Time.time + 0.0001f < scheduledSwing.PlayTime)
            {
                i++;
                continue;
            }

            pendingSwingAudio.RemoveAt(i);
            PlaySwingAudio(scheduledSwing.Cue);
        }
    }

    private void CancelPendingSwingAudio()
    {
        pendingSwingAudio.Clear();
    }

    private void PlaySwingAudio(AudioCue cue)
    {
        if (cue == null)
            return;

        ResolveReferences();
        Transform anchor = ResolveAttackAudioAnchor();
        if (audioEmitter != null)
        {
            audioEmitter.PlayAttached(cue, anchor);
            return;
        }

        if (cue.Is3D && cue.Anchor == AudioPlaybackAnchor.FollowTransform && anchor != null)
        {
            GameAudioService.Play(cue, anchor);
            return;
        }

        if (cue.Is3D)
            GameAudioService.Play(cue, anchor != null ? anchor.position : transform.position);
        else
            GameAudioService.Play(cue);
    }

    private void TriggerAttackCameraFeedback(AttackConfig attackConfig)
    {
        if (!HasConfiguredAttackCameraFeedback(attackConfig))
        {
            ResetAttackCameraFeedback();
            return;
        }

        activeCameraFeedbackConfig = attackConfig;
        ResolveAttackCameraFeedbackTargets();
        if (attackFeedbackCamera == null)
            return;

        if (!hasAttackFeedbackCameraBaseFov)
        {
            attackFeedbackCameraBaseFov = attackFeedbackCamera.fieldOfView;
            hasAttackFeedbackCameraBaseFov = true;
        }

        if (!attackConfig.ApplyFovFeedbackToHandsCamera)
            RestoreAttackFeedbackHandsCameraFov();

        if (attackConfig.ApplyFovFeedbackToHandsCamera && attackFeedbackHandsCamera != null && !hasAttackFeedbackHandsCameraBaseFov)
        {
            attackFeedbackHandsCameraBaseFov = attackFeedbackHandsCamera.fieldOfView;
            hasAttackFeedbackHandsCameraBaseFov = true;
        }

        float now = Time.time;
        float reachTime = attackConfig.AttackFovReachTime;
        float holdTime = attackConfig.AttackFovHoldTime;
        float relaxTime = attackConfig.AttackFovRelaxTime;

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
        AttackConfig attackConfig = activeCameraFeedbackConfig;
        if (attackConfig == null)
            return;

        float safeWeight = Mathf.Clamp01(weight);

        if (TryResolveAttackFeedbackBaseFov(useHandsCamera: false, out float cameraBaseFov))
        {
            attackFeedbackCamera.fieldOfView = ResolveAttackFeedbackFov(
                cameraBaseFov,
                attackConfig.AttackFovPercentChange,
                safeWeight);
        }

        if (attackConfig.ApplyFovFeedbackToHandsCamera
            && TryResolveAttackFeedbackBaseFov(useHandsCamera: true, out float handsCameraBaseFov))
        {
            attackFeedbackHandsCamera.fieldOfView = ResolveAttackFeedbackFov(
                handsCameraBaseFov,
                attackConfig.AttackFovPercentChange,
                safeWeight);
        }

        if (attackFeedbackCamera == null)
            return;

        Vector3 cameraRotation = attackConfig.AttackCameraRotation;
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
        activeCameraFeedbackConfig = null;
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

    private bool HasConfiguredAttackCameraFeedback(AttackConfig attackConfig)
    {
        return attackConfig != null && attackConfig.HasCameraFeedback;
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

    private AttackConfig ResolveAttackConfig(int comboStep)
    {
        int safeComboStep = Mathf.Clamp(comboStep, 1, MovementConfig.AttackComboStepCount);
        if (attackConfigs != null)
        {
            for (int i = 0; i < attackConfigs.Count; i++)
            {
                AttackConfig attackConfig = attackConfigs[i];
                if (attackConfig != null && attackConfig.ComboStep == safeComboStep)
                    return attackConfig;
            }
        }

        return new AttackConfig(safeComboStep, 0, 30f, 0.12f, hitDistance, hitRadius);
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

    private void TryHitTargets(AttackConfig attackConfig)
    {
        ResolveAttackTransforms();

        Vector3 origin = ResolveAttackOrigin();
        Vector3 attackForward = ResolveAttackForward();
        int hitCount = CollectHitCandidates(attackConfig, origin, attackForward);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = hitBuffer[i];
            if (hitCollider == null)
                continue;

            if (!IsColliderInsideHitShape(attackConfig, origin, attackForward, hitCollider))
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

            Vector3 resolvedHitPoint = ResolveHitPoint(attackConfig, origin, attackForward, hitCollider);
            DamageInfo damageInfo = new DamageInfo(
                activeAttackDamage,
                gameObject,
                CombatAlignment.Player,
                resolvedHitPoint,
                attackForward,
                PlayerDamageAnimationType.None,
                playerCameraImpact: attackConfig.PlayerCameraImpactType,
                impactVfxAttackAngle: attackConfig.ImpactVfxAttackAngle,
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
                    attackConfig.PlayerCameraImpactType,
                    attackConfig.ImpactVfxAttackAngle,
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

    private int CollectHitCandidates(AttackConfig attackConfig, Vector3 origin, Vector3 attackForward)
    {
        switch (attackConfig.HitShape)
        {
            case PlayerMeleeAttackShape.Arc:
                return Physics.OverlapSphereNonAlloc(
                    origin,
                    attackConfig.ArcRange,
                    hitBuffer,
                    DestructibleDebrisCollision.ExcludeDebrisLayer(hitMask.value),
                    QueryTriggerInteraction.Ignore);

            case PlayerMeleeAttackShape.Box:
                return Physics.OverlapBoxNonAlloc(
                    ResolveBoxCenter(attackConfig, origin, attackForward),
                    attackConfig.BoxHalfExtents,
                    hitBuffer,
                    ResolveBoxRotation(attackForward),
                    DestructibleDebrisCollision.ExcludeDebrisLayer(hitMask.value),
                    QueryTriggerInteraction.Ignore);

            case PlayerMeleeAttackShape.Ball:
            default:
                return Physics.OverlapSphereNonAlloc(
                    ResolveBallCenter(attackConfig, origin, attackForward),
                    attackConfig.HitRadius,
                    hitBuffer,
                    DestructibleDebrisCollision.ExcludeDebrisLayer(hitMask.value),
                    QueryTriggerInteraction.Ignore);
        }
    }

    private bool IsColliderInsideHitShape(
        AttackConfig attackConfig,
        Vector3 origin,
        Vector3 attackForward,
        Collider hitCollider)
    {
        if (attackConfig.HitShape != PlayerMeleeAttackShape.Arc)
            return true;

        Vector3 forward = ResolvePlanarAttackForward(attackForward);
        Vector3 referencePoint = ResolveArcReferencePoint(attackConfig, origin, forward, hitCollider);
        if (attackConfig.ArcHalfHeight > 0f
            && Mathf.Abs(referencePoint.y - origin.y) > attackConfig.ArcHalfHeight)
        {
            return false;
        }

        Vector3 planarOffset = Vector3.ProjectOnPlane(referencePoint - origin, Vector3.up);
        float minForwardDot = Mathf.Cos(attackConfig.ArcAngle * 0.5f * Mathf.Deg2Rad);
        return IsInsideForwardArc(planarOffset, forward, attackConfig.ArcRange, minForwardDot);
    }

    private Vector3 ResolveHitPoint(AttackConfig attackConfig, Vector3 origin, Vector3 attackForward, Collider hitCollider)
    {
        switch (attackConfig.HitShape)
        {
            case PlayerMeleeAttackShape.Arc:
                return ResolveForwardArcHitPoint(
                    attackConfig,
                    origin,
                    ResolvePlanarAttackForward(attackForward),
                    hitCollider);

            case PlayerMeleeAttackShape.Box:
            {
                Vector3 boxCenter = ResolveBoxCenter(attackConfig, origin, attackForward);
                float hitProbeDistance = Mathf.Max(0.05f, attackConfig.BoxCenterDistance + attackConfig.BoxHalfExtents.z);
                return ResolveHitPoint(origin, boxCenter, attackForward, hitProbeDistance, hitCollider);
            }

            case PlayerMeleeAttackShape.Ball:
            default:
            {
                Vector3 ballCenter = ResolveBallCenter(attackConfig, origin, attackForward);
                float hitProbeDistance = Mathf.Max(0.05f, attackConfig.TargetDistance + attackConfig.HitRadius);
                return ResolveHitPoint(origin, ballCenter, attackForward, hitProbeDistance, hitCollider);
            }
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

    private static Vector3 ResolveBallCenter(AttackConfig attackConfig, Vector3 origin, Vector3 attackForward)
    {
        return origin + attackForward * attackConfig.TargetDistance;
    }

    private static Vector3 ResolveBoxCenter(AttackConfig attackConfig, Vector3 origin, Vector3 attackForward)
    {
        return origin + attackForward * attackConfig.BoxCenterDistance;
    }

    private Quaternion ResolveBoxRotation(Vector3 attackForward)
    {
        Vector3 forward = attackForward.sqrMagnitude > 0.0001f ? attackForward.normalized : transform.forward;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;

        Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f
            ? Vector3.ProjectOnPlane(transform.up, forward)
            : Vector3.up;
        if (up.sqrMagnitude <= 0.0001f)
            up = Vector3.ProjectOnPlane(Vector3.forward, forward);
        if (up.sqrMagnitude <= 0.0001f)
            up = Vector3.right;

        return Quaternion.LookRotation(forward.normalized, up.normalized);
    }

    private Vector3 ResolvePlanarAttackForward(Vector3 attackForward)
    {
        Vector3 forward = Vector3.ProjectOnPlane(attackForward, Vector3.up);
        if (forward.sqrMagnitude > 0.0001f)
            return forward.normalized;

        forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.0001f)
            return forward.normalized;

        return Vector3.forward;
    }

    private static Vector3 ResolveArcReferencePoint(
        AttackConfig attackConfig,
        Vector3 origin,
        Vector3 forward,
        Collider hitCollider)
    {
        Vector3 sampleCenter = origin + forward * (attackConfig.ArcRange * 0.5f);
        if (hitCollider == null)
            return sampleCenter;

        Vector3 closestPoint = hitCollider.ClosestPoint(sampleCenter);
        if (IsFiniteVector(closestPoint))
            return closestPoint;

        return hitCollider.bounds.center;
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

    private static Vector3 ResolveForwardArcHitPoint(
        AttackConfig attackConfig,
        Vector3 origin,
        Vector3 attackForward,
        Collider hitCollider)
    {
        if (hitCollider == null)
            return origin + attackForward * attackConfig.ArcRange;

        Vector3 sampleCenter = origin + attackForward * (attackConfig.ArcRange * 0.5f);
        Vector3 closestPoint = hitCollider.ClosestPoint(sampleCenter);
        return IsFiniteVector(closestPoint) ? closestPoint : sampleCenter;
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

    private void ResolveReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (movementFovFeedback == null)
            movementFovFeedback = GetComponent<PlayerMovementFovFeedback>();

        if (playerHealth == null)
            playerHealth = GetComponent<PlayerHealth>();

        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (audioEmitter == null)
            audioEmitter = GetComponent<AudioEmitter>();

        ResolveAttackTransforms();

        if (attackAudioAnchor == null)
            attackAudioAnchor = FindChildTransformByName(AttackAudioAnchorName)
                ?? attackDirection
                ?? attackOrigin
                ?? transform;
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

    private Transform ResolveAttackAudioAnchor()
    {
        if (attackAudioAnchor == null)
            ResolveReferences();

        return attackAudioAnchor != null ? attackAudioAnchor : transform;
    }

    private Transform FindChildTransformByName(string childName)
    {
        if (string.IsNullOrWhiteSpace(childName))
            return null;

        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform childTransform = childTransforms[i];
            if (childTransform != null && string.Equals(childTransform.name, childName, StringComparison.Ordinal))
                return childTransform;
        }

        return null;
    }

    private bool HasLocalAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawHitboxGizmo)
            return;

        AttackConfig attackConfig = ResolveAttackConfig(previewComboStep);
        if (attackConfig == null)
            return;

        Vector3 origin = ResolveAttackOrigin();
        Vector3 attackForward = ResolveAttackForward();

        DrawAttackShapeGizmo(attackConfig, origin, attackForward);
    }

    private void DrawAttackShapeGizmo(AttackConfig attackConfig, Vector3 origin, Vector3 attackForward)
    {
        switch (attackConfig.HitShape)
        {
            case PlayerMeleeAttackShape.Arc:
                DrawForwardArcGizmo(
                    origin,
                    ResolvePlanarAttackForward(attackForward),
                    attackConfig.ArcRange,
                    attackConfig.ArcAngle,
                    attackConfig.ArcHalfHeight);
                break;

            case PlayerMeleeAttackShape.Box:
                DrawBoxGizmo(
                    ResolveBoxCenter(attackConfig, origin, attackForward),
                    ResolveBoxRotation(attackForward),
                    attackConfig.BoxSize);
                break;

            case PlayerMeleeAttackShape.Ball:
            default:
                DrawBallGizmo(
                    ResolveBallCenter(attackConfig, origin, attackForward),
                    attackConfig.HitRadius);
                break;
        }
    }

    private void DrawBallGizmo(Vector3 center, float radius)
    {
        Gizmos.color = hitboxGizmoColor;
        Gizmos.DrawSphere(center, radius);
        Gizmos.color = new Color(hitboxGizmoColor.r, hitboxGizmoColor.g, hitboxGizmoColor.b, 1f);
        Gizmos.DrawWireSphere(center, radius);
    }

    private void DrawBoxGizmo(Vector3 center, Quaternion rotation, Vector3 size)
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
        Gizmos.color = hitboxGizmoColor;
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color = new Color(hitboxGizmoColor.r, hitboxGizmoColor.g, hitboxGizmoColor.b, 1f);
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = previousMatrix;
    }

    private void DrawForwardArcGizmo(Vector3 origin, Vector3 forward, float range, float angle, float halfHeight)
    {
        float safeRange = Mathf.Max(0.1f, range);
        float safeAngle = Mathf.Clamp(angle, 1f, 180f);
        float halfAngle = safeAngle * 0.5f;
        Vector3 leftDirection = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
        Vector3 rightDirection = Quaternion.AngleAxis(halfAngle, Vector3.up) * forward;

#if UNITY_EDITOR
        Handles.color = hitboxGizmoColor;
        Handles.DrawSolidArc(origin, Vector3.up, leftDirection, safeAngle, safeRange);
        Handles.color = new Color(hitboxGizmoColor.r, hitboxGizmoColor.g, hitboxGizmoColor.b, 1f);
        Handles.DrawWireArc(origin, Vector3.up, leftDirection, safeAngle, safeRange);
#endif

        Gizmos.color = new Color(hitboxGizmoColor.r, hitboxGizmoColor.g, hitboxGizmoColor.b, 1f);
        Gizmos.DrawLine(origin, origin + leftDirection * safeRange);
        Gizmos.DrawLine(origin, origin + rightDirection * safeRange);
        Gizmos.DrawLine(origin, origin + forward * safeRange);

        Vector3 previousPoint = origin + leftDirection * safeRange;
        for (int i = 1; i <= ArcGizmoSegments; i++)
        {
            float t = i / (float)ArcGizmoSegments;
            Vector3 direction = Quaternion.AngleAxis(Mathf.Lerp(-halfAngle, halfAngle, t), Vector3.up) * forward;
            Vector3 currentPoint = origin + direction * safeRange;
            Gizmos.DrawLine(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }

        if (halfHeight <= 0f)
            return;

        Vector3 verticalOffset = Vector3.up * halfHeight;
        Gizmos.DrawLine(origin - verticalOffset, origin + verticalOffset);
    }
}
