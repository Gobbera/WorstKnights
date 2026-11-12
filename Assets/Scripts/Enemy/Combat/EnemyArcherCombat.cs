using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(EnemySetup), typeof(EnemyHealth))]
public sealed class EnemyArcherCombat : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyBrain brain;
    [SerializeField] private EnemyHealth health;
    [SerializeField] private EnemyVision vision;
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip attackClip;
    [SerializeField] private Transform presentationArrow;
    [SerializeField] private Transform aimSpine;
    [SerializeField] private EnemyArrowProjectile projectilePrefab;

    [Header("Attack Audio")]
    [Tooltip("Plays when the attack animation starts. Stops on release or interruption.")]
    [SerializeField] private AudioCue attackStartAudioCue;
    [Tooltip("Plays when the arrow is actually released.")]
    [SerializeField] private AudioCue shotAudioCue;
    [Tooltip("Optional anchor for attack audio. Shot audio otherwise starts at the arrow launch position.")]
    [SerializeField] private Transform shotAudioAnchor;
    [SerializeField] private AudioEmitter audioEmitter;

    [Header("Shooting Range")]
    [SerializeField, Min(0.1f)] private float maximumRange = 16f;
    [SerializeField, Min(0f)] private float minimumPreferredDistance = 4f;
    [SerializeField, Min(0f)] private float retreatEndDistance = 6.5f;
    [SerializeField, Min(0.1f)] private float maximumRetreatStep = 4f;
    [SerializeField, Range(0f, 90f)] private float startFacingAngle = 12f;
    [SerializeField, Min(0.05f)] private float retreatRepathInterval = 0.4f;

    [Header("Shot Timing")]
    [SerializeField] private string attackStatePath = "Base Layer.Attack_01";
    [SerializeField, Min(0.1f)] private float fallbackDuration = 76f / 24f;
    [SerializeField, Range(0f, 1f)] private float releaseNormalizedTime = 57f / 76f;
    [SerializeField, Range(0f, 1f)] private float damageInterruptWindowEnd = 0.18f;
    [SerializeField, Min(0f)] private float recoveryCooldown = 1.1f;

    [Header("Projectile")]
    [SerializeField, Min(0.1f)] private float projectileSpeed = 22f;
    [SerializeField, Min(0f)] private float projectileDamage = 12f;
    [SerializeField, Range(0f, 80f)] private float maximumAimPitch = 35f;

    private readonly Dictionary<int, EnemyArrowProjectile> projectiles = new Dictionary<int, EnemyArrowProjectile>();
    private PlayerHealth committedTarget;
    private PhotonView photonView;
    private Vector3 visibleAimPoint;
    private Vector3 retreatDestination;
    private float attackStartedAt;
    private float nextAttackAt;
    private float nextRetreatPathAt;
    private bool attacking;
    private bool released;
    private bool retreating;
    private bool hasRetreatDestination;
    private bool alive = true;
    private int shotSequence;
    private double startedAtServer;
    private int latestLaunchSequence;
    private AudioSource attackStartAudioSource;

    public bool IsAttacking => attacking && alive && isActiveAndEnabled;
    public bool CanStartAttack => alive && isActiveAndEnabled && projectilePrefab != null
        && animator != null && attackClip != null && presentationArrow != null && (health == null || health.IsAlive);
    public bool CanTrackTarget => IsAttacking && NormalizedTime < releaseNormalizedTime;
    public bool CanDamageInterrupt => !IsAttacking || NormalizedTime <= damageInterruptWindowEnd;
    public int ShotSequence => shotSequence;
    public double ShotStartedAtServer => startedAtServer;
    public Vector3 VisibleAimPoint => visibleAimPoint;
    public float MaximumRange => maximumRange;
    public float Duration => attackClip != null ? Mathf.Max(0.1f, attackClip.length) : fallbackDuration;
    public float NormalizedTime => attacking ? Mathf.Max(0f, (Time.time - attackStartedAt) / Duration) : 1f;
    private bool HasAuthority => enemySetup == null || enemySetup.HasAuthority;

    private void Awake() => ResolveReferences();

    private void OnEnable()
    {
        ResolveReferences();
        if (health != null) health.DamageApplied += OnDamaged;
        ShowArrow(false);
    }

    private void OnDisable()
    {
        if (health != null) health.DamageApplied -= OnDamaged;
        CancelAttack();
    }

    private void ResolveReferences()
    {
        if (enemySetup == null) enemySetup = GetComponent<EnemySetup>();
        if (brain == null) brain = GetComponent<EnemyBrain>();
        if (health == null) health = GetComponent<EnemyHealth>();
        if (vision == null) vision = GetComponent<EnemyVision>();
        if (animator == null) animator = enemySetup != null ? enemySetup.Animator : GetComponentInChildren<Animator>();
        if (photonView == null) photonView = GetComponent<PhotonView>();
        if (audioEmitter == null) audioEmitter = GetComponent<AudioEmitter>();
    }

    public void SetAliveState(bool value)
    {
        alive = value;
        if (!value) CancelAttack();
    }

    public bool CanAttackTarget(PlayerHealth target)
    {
        if (!CanStartAttack || target == null || !target.CanBeTargetedByEnemy) return false;
        Vector3 offset = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
        return offset.sqrMagnitude <= maximumRange * maximumRange;
    }

    public bool TryGetCommittedFacingPoint(out Vector3 point)
    {
        point = Vector3.zero;
        return CanTrackTarget && committedTarget != null && vision != null
            && vision.CanSeeTarget(committedTarget, out point);
    }

    public bool TryStartAttack(PlayerHealth target)
    {
        if (!HasAuthority || IsAttacking || Time.time < nextAttackAt || !CanAttackTarget(target)
            || vision == null || !vision.CanSeeTarget(target, out _)) return false;
        Vector3 flat = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
        if (flat.sqrMagnitude > 0.0001f
            && Vector3.Dot(transform.forward, flat.normalized) < Mathf.Cos(startFacingAngle * Mathf.Deg2Rad)) return false;
        if (!animator.HasState(0, Animator.StringToHash(attackStatePath))) return false;
        committedTarget = target;
        visibleAimPoint = AimPoint(target);
        if (!TryGetVelocity(visibleAimPoint, out _)) return false;
        attacking = true;
        released = false;
        attackStartedAt = Time.time;
        startedAtServer = PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
        shotSequence++;
        retreating = false;
        hasRetreatDestination = false;
        animator.CrossFadeInFixedTime(Animator.StringToHash(attackStatePath), 0.08f, 0, 0f);
        ShowArrow(true);
        PlayAttackStartAudio(0f);
        return true;
    }

    public bool TryMaintainDistance(PlayerHealth target)
    {
        if (!HasAuthority || IsAttacking || target == null || brain == null) return false;
        float distance = Vector3.ProjectOnPlane(transform.position - target.transform.position, Vector3.up).magnitude;
        if (!EnemyArcherBallistics.ShouldRetreat(distance, retreating, minimumPreferredDistance, retreatEndDistance))
        {
            if (retreating) brain.StopNavigation();
            retreating = false;
            hasRetreatDestination = false;
            return false;
        }
        if (!hasRetreatDestination || Time.time >= nextRetreatPathAt)
        {
            hasRetreatDestination = TryFindRetreatPosition(target.transform.position, distance, out retreatDestination);
            nextRetreatPathAt = Time.time + retreatRepathInterval;
        }
        if (!hasRetreatDestination)
        {
            retreating = false;
            return false; // A cornered archer can still defend itself with a shot.
        }
        retreating = true;
        brain.SetNavigationDestination(retreatDestination, brain.ChaseSpeed, 0.2f, false);
        return true;
    }

    private bool TryFindRetreatPosition(Vector3 target, float distance, out Vector3 result)
    {
        result = transform.position;
        EnemyNavigation navigation = enemySetup != null ? enemySetup.EnemyNavigation : GetComponent<EnemyNavigation>();
        if (navigation == null || !navigation.HasUsableAgent) return false;
        Vector3 away = Vector3.ProjectOnPlane(transform.position - target, Vector3.up).normalized;
        if (away.sqrMagnitude < 0.001f) away = -transform.forward;
        float step = Mathf.Clamp(retreatEndDistance - distance + 0.5f, 1f, maximumRetreatStep);
        float[] angles = { 0f, 35f, -35f, 70f, -70f };
        foreach (float angle in angles)
        {
            Vector3 candidate = transform.position + Quaternion.AngleAxis(angle, Vector3.up) * away * step;
            if (!brain.TrySampleNavigationPosition(candidate, 1f, out Vector3 sampled)
                || Vector3.ProjectOnPlane(sampled - target, Vector3.up).magnitude < distance + 0.5f
                || (sampled - transform.position).sqrMagnitude > (maximumRetreatStep + 1f) * (maximumRetreatStep + 1f)
                || !brain.NavigationCanReach(sampled)) continue;
            result = sampled;
            return true;
        }
        return false;
    }

    private void Update()
    {
        if (!attacking) return;
        if (!alive || health != null && !health.IsAlive || brain != null && (brain.IsStunned || brain.IsTraversing))
        {
            CancelAttack();
            return;
        }
        if (HasAuthority && CanTrackTarget && committedTarget != null && vision != null
            && vision.CanSeeTarget(committedTarget, out _)) visibleAimPoint = AimPoint(committedTarget);
    }

    private void LateUpdate()
    {
        if (!attacking) return;
        float normalized = NormalizedTime;
        ApplyAimPitch(normalized);
        if (!released && normalized >= releaseNormalizedTime)
        {
            released = true;
            StopAttackStartAudio();
            ShowArrow(false);
            if (HasAuthority) TryReleaseArrow();
        }
        if (normalized >= 1f)
        {
            attacking = false;
            committedTarget = null;
            nextAttackAt = Time.time + recoveryCooldown;
            ShowArrow(false);
        }
    }

    private void ApplyAimPitch(float normalized)
    {
        if (aimSpine == null || !TryGetVelocity(visibleAimPoint, out Vector3 launch)) return;
        float flat = Vector3.ProjectOnPlane(launch, Vector3.up).magnitude;
        float pitch = Mathf.Clamp(Mathf.Atan2(launch.y, flat) * Mathf.Rad2Deg, -maximumAimPitch, maximumAimPitch);
        float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(34f / 76f, 46f / 76f, normalized));
        if (normalized > releaseNormalizedTime)
            weight *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(releaseNormalizedTime, 1f, normalized));
        aimSpine.rotation = Quaternion.AngleAxis(-pitch * weight, transform.right) * aimSpine.rotation;
    }

    private bool TryGetVelocity(Vector3 point, out Vector3 launch)
    {
        Vector3 origin = presentationArrow != null ? presentationArrow.position : transform.position + Vector3.up * 1.5f;
        if (!EnemyArcherBallistics.TryGetLaunchVelocity(origin, point, projectileSpeed,
            projectilePrefab != null ? projectilePrefab.Gravity : 9.81f, out launch)) return false;
        float pitch = Mathf.Atan2(launch.y, Vector3.ProjectOnPlane(launch, Vector3.up).magnitude) * Mathf.Rad2Deg;
        return Mathf.Abs(pitch) <= maximumAimPitch;
    }

    private void TryReleaseArrow()
    {
        if (!CanStartAttack || committedTarget == null || !committedTarget.CanBeTargetedByEnemy
            || vision == null || !vision.CanSeeTarget(committedTarget, out _)
            || !CanAttackTarget(committedTarget) || !TryGetVelocity(AimPoint(committedTarget), out Vector3 launch)) return;
        Vector3 origin = presentationArrow.position;
        double sentAt = PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
        CreateProjectile(shotSequence, origin, launch, 0f, true);
        if (CanSendRpc())
            photonView.RPC(nameof(RpcLaunchArrow), RpcTarget.Others, shotSequence, origin, launch, sentAt);
    }

    private void CreateProjectile(int sequence, Vector3 position, Vector3 launch, float age, bool authority)
    {
        if (projectilePrefab == null || sequence <= latestLaunchSequence) return;
        latestLaunchSequence = sequence;
        EnemyArrowProjectile arrow = Instantiate(projectilePrefab, position, Quaternion.LookRotation(launch.normalized));
        SceneManager.MoveGameObjectToScene(arrow.gameObject, gameObject.scene);
        arrow.name = "Skeleton Arrow " + sequence;
        projectiles[sequence] = arrow;
        arrow.Impacted += (projectile, point, normal) => OnArrowImpact(sequence, projectile);
        arrow.Disposed += projectile => projectiles.Remove(sequence);
        arrow.Launch(gameObject, launch, projectileDamage, authority, age);
        if (sequence >= shotSequence) StopAttackStartAudio();
        PlayShotAudio(position);
    }

    private void PlayAttackStartAudio(float elapsed)
    {
        if (attackStartAudioCue == null) return;
        AudioClip clip = attackStartAudioCue.GetRandomClip();
        if (clip == null) return;

        if (attackStartAudioSource == null)
        {
            GameObject sourceObject = new GameObject("Attack Start Audio");
            sourceObject.transform.SetParent(transform, false);
            attackStartAudioSource = sourceObject.AddComponent<AudioSource>();
            attackStartAudioSource.playOnAwake = false;
            attackStartAudioSource.dopplerLevel = 0f;
        }

        attackStartAudioSource.Stop();
        Transform anchor = shotAudioAnchor != null ? shotAudioAnchor : transform;
        attackStartAudioSource.transform.SetParent(anchor, false);
        attackStartAudioCue.ConfigureAudioSource(attackStartAudioSource, clip);
        attackStartAudioSource.loop = false;
        float offset = Mathf.Max(0f, elapsed) * Mathf.Abs(attackStartAudioSource.pitch);
        if (offset >= clip.length) return;
        attackStartAudioSource.time = offset;
        attackStartAudioSource.Play();
    }

    private void StopAttackStartAudio()
    {
        if (attackStartAudioSource != null) attackStartAudioSource.Stop();
    }

    private void PlayShotAudio(Vector3 launchPosition)
    {
        if (shotAudioCue == null) return;
        if (shotAudioAnchor != null)
        {
            if (audioEmitter != null) audioEmitter.PlayAttached(shotAudioCue, shotAudioAnchor);
            else GameAudioService.Play(shotAudioCue, shotAudioAnchor);
            return;
        }

        if (audioEmitter != null) audioEmitter.PlayAtPosition(shotAudioCue, launchPosition);
        else GameAudioService.Play(shotAudioCue, launchPosition);
    }

    private void OnArrowImpact(int sequence, EnemyArrowProjectile arrow)
    {
        if (CanSendRpc())
        {
            Transform target = arrow.ImpactTarget;
            PhotonView targetView = target != null ? target.GetComponent<PhotonView>() : null;
            int targetViewId = targetView != null ? targetView.ViewID : 0;
            Vector3 localPosition = target != null ? target.InverseTransformPoint(arrow.transform.position) : Vector3.zero;
            Quaternion localRotation = target != null
                ? Quaternion.Inverse(target.rotation) * arrow.transform.rotation : Quaternion.identity;
            photonView.RPC(nameof(RpcArrowImpact), RpcTarget.Others, sequence,
                arrow.transform.position, arrow.transform.rotation, targetViewId, localPosition, localRotation);
        }
        projectiles.Remove(sequence);
    }

    [PunRPC]
    private void RpcLaunchArrow(int sequence, Vector3 position, Vector3 launch, double sentAt, PhotonMessageInfo info)
    {
        if (!IsAuthorityMessage(info) || HasAuthority || !EnemyArcherBallistics.IsFinite(position)
            || !EnemyArcherBallistics.IsFinite(launch) || launch.sqrMagnitude < 0.001f) return;
        CreateProjectile(sequence, position, launch, Mathf.Clamp((float)(PhotonNetwork.Time - sentAt), 0f, 1f), false);
        if (sequence == shotSequence) { released = true; ShowArrow(false); }
    }

    [PunRPC]
    private void RpcArrowImpact(int sequence, Vector3 position, Quaternion rotation, int targetViewId,
        Vector3 localPosition, Quaternion localRotation, PhotonMessageInfo info)
    {
        if (!IsAuthorityMessage(info) || HasAuthority) return;
        if (projectiles.TryGetValue(sequence, out EnemyArrowProjectile arrow) && arrow != null)
        {
            PhotonView targetView = targetViewId != 0 ? PhotonView.Find(targetViewId) : null;
            Transform target = targetView != null ? targetView.transform : null;
            if (target != null)
            {
                position = target.TransformPoint(localPosition);
                rotation = target.rotation * localRotation;
            }
            arrow.ApplyReplicatedImpactPose(position, rotation, target);
        }
        projectiles.Remove(sequence);
    }

    public void ApplyReplicatedShot(int sequence, double serverStart, bool active, Vector3 aim)
    {
        if (HasAuthority || sequence < shotSequence) return;
        visibleAimPoint = aim;
        if (!active) { if (sequence >= shotSequence) CancelAttack(); shotSequence = sequence; return; }
        if (sequence == shotSequence && attacking) return;
        bool newShot = sequence > shotSequence;
        shotSequence = sequence;
        startedAtServer = serverStart;
        float elapsed = Mathf.Max(0f, (float)(PhotonNetwork.Time - serverStart));
        if (elapsed >= Duration) return;
        attacking = true;
        attackStartedAt = Time.time - elapsed;
        released = elapsed >= Duration * releaseNormalizedTime;
        if (animator != null)
            animator.CrossFadeInFixedTime(Animator.StringToHash(attackStatePath), 0.06f, 0, elapsed);
        ShowArrow(!released);
        if (newShot && !released && sequence > latestLaunchSequence) PlayAttackStartAudio(elapsed);
    }

    public void CancelAttack()
    {
        StopAttackStartAudio();
        if (attacking) nextAttackAt = Mathf.Max(nextAttackAt, Time.time + recoveryCooldown);
        attacking = false;
        released = true;
        committedTarget = null;
        retreating = false;
        hasRetreatDestination = false;
        ShowArrow(false);
    }

    private void OnDamaged(DamageInfo damage)
    {
        if (CanDamageInterrupt) CancelAttack();
    }

    private void ShowArrow(bool value)
    {
        if (presentationArrow != null && presentationArrow.gameObject.activeSelf != value)
            presentationArrow.gameObject.SetActive(value);
    }

    private bool CanSendRpc() => photonView != null && photonView.ViewID != 0
        && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;

    private bool IsAuthorityMessage(PhotonMessageInfo info) => photonView != null
        && (photonView.IsRoomView ? info.Sender == PhotonNetwork.MasterClient : info.Sender == photonView.Owner);

    private static Vector3 AimPoint(PlayerHealth target)
    {
        CapsuleCollider capsule = target.GetComponent<CapsuleCollider>();
        return capsule != null ? capsule.bounds.center : target.transform.position + Vector3.up;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, maximumRange);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, minimumPreferredDistance);
    }
}
