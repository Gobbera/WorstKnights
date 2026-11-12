using System;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyArrowProjectile : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField, Min(0f)] private float gravity = 9.81f;
    [SerializeField, Min(0.001f)] private float collisionRadius = 0.035f;
    [SerializeField, Min(0f)] private float arrowLength = 0.88f;
    [SerializeField, Min(0.1f)] private float maximumFlightTime = 6f;
    [SerializeField] private LayerMask collisionMask = Physics.DefaultRaycastLayers;

    [Header("Impact")]
    [SerializeField, Min(0f)] private float playerPenetrationDepth = 0.6f;
    [Tooltip("Total time after impact, including the fade-out.")]
    [SerializeField, Min(0f)] private float impactLifetime = 6f;
    [SerializeField, Min(0f)] private float impactFadeOutDuration = 1f;

    [Header("Arrow Audio")]
    [Tooltip("Loops while the arrow is flying and follows its position.")]
    [SerializeField] private AudioCue flightAudioCue;
    [Tooltip("Plays once at impact, including hits on shields and world geometry.")]
    [SerializeField] private AudioCue impactAudioCue;

    private readonly RaycastHit[] hits = new RaycastHit[64];
    private readonly Collider[] overlaps = new Collider[64];
    private GameObject owner;
    private EnemySetup ownerSetup;
    private PhotonView ownerView;
    private Vector3 velocity;
    private float damage;
    private float age;
    private bool launched;
    private bool terminal;
    private bool damageEnabled;
    private bool wasRoomView;
    private int launchingActor;
    private int ownerViewId;
    private bool initialCollisionChecked;
    private Vector3 impactLocalPosition;
    private Quaternion impactLocalRotation;
    private AudioSource flightAudioSource;

    public float Gravity => gravity;
    public float ArrowLength => arrowLength;
    public Vector3 Velocity => velocity;
    public bool HasImpacted => terminal;
    public Transform ImpactTarget { get; private set; }
    public event Action<EnemyArrowProjectile, Vector3, Vector3> Impacted;
    public event Action<EnemyArrowProjectile> Disposed;

    public void Launch(GameObject instigator, Vector3 launchVelocity, float amount, bool canDealDamage,
        float transportAge = 0f)
    {
        owner = instigator;
        ownerSetup = owner != null ? owner.GetComponent<EnemySetup>() : null;
        ownerView = owner != null ? owner.GetComponent<PhotonView>() : null;
        ownerViewId = ownerView != null ? ownerView.ViewID : 0;
        wasRoomView = ownerView != null && ownerView.IsRoomView;
        launchingActor = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
        velocity = launchVelocity;
        damage = Mathf.Max(0f, amount);
        damageEnabled = canDealDamage;
        age = Mathf.Clamp(transportAge, 0f, maximumFlightTime);
        if (age > 0f)
        {
            transform.position += velocity * age + Vector3.down * (0.5f * gravity * age * age);
            velocity += Vector3.down * (gravity * age);
        }
        launched = true;
        terminal = false;
        ImpactTarget = null;
        OrientToVelocity();
        initialCollisionChecked = false;
        StartFlightAudio();
    }

    private void OnEnable()
    {
        if (launched && !terminal) StartFlightAudio();
    }

    private void OnDisable() => StopFlightAudio();

    private void StartFlightAudio()
    {
        StopFlightAudio();
        if (flightAudioCue == null) return;
        AudioClip clip = flightAudioCue.GetRandomClip();
        if (clip == null) return;

        if (flightAudioSource == null)
        {
            flightAudioSource = gameObject.AddComponent<AudioSource>();
            flightAudioSource.playOnAwake = false;
            flightAudioSource.dopplerLevel = 0f;
        }

        flightAudioCue.ConfigureAudioSource(flightAudioSource, clip, forceLoop: true);
        if (clip.length > 0f)
            flightAudioSource.time = Mathf.Repeat(age * Mathf.Abs(flightAudioSource.pitch), clip.length);
        flightAudioSource.Play();
    }

    private void StopFlightAudio()
    {
        if (flightAudioSource != null) flightAudioSource.Stop();
    }

    private void FixedUpdate()
    {
        if (!launched || terminal) return;
        if (!initialCollisionChecked)
        {
            initialCollisionChecked = true;
            if (HasDamageAuthority()) CheckInitialOverlap();
            if (terminal) return;
        }
        SimulateFlight(Time.fixedDeltaTime);
    }

    private void OnDestroy() => Disposed?.Invoke(this);

    private void LateUpdate()
    {
        if (ImpactTarget == null) return;
        transform.SetPositionAndRotation(ImpactTarget.TransformPoint(impactLocalPosition),
            ImpactTarget.rotation * impactLocalRotation);
    }

    private void SimulateFlight(float deltaTime)
    {
        float step = Mathf.Min(Mathf.Max(0f, deltaTime), Mathf.Max(0f, maximumFlightTime - age));
        if (step <= 0f) { Destroy(gameObject); return; }
        Vector3 previousTip = transform.position + transform.forward * arrowLength;
        Vector3 nextPosition = transform.position + velocity * step + Vector3.down * (0.5f * gravity * step * step);
        Vector3 nextVelocity = velocity + Vector3.down * (gravity * step);
        Vector3 nextForward = nextVelocity.sqrMagnitude > 0.0001f ? nextVelocity.normalized : transform.forward;
        Vector3 nextTip = nextPosition + nextForward * arrowLength;
        Vector3 travel = nextTip - previousTip;
        if (HasDamageAuthority() && travel.sqrMagnitude > 0.000001f
            && TryGetFirstHit(previousTip, travel, out RaycastHit hit))
        {
            ResolveHit(hit.collider, hit.point, hit.normal, previousTip);
            return;
        }
        transform.position = nextPosition;
        velocity = nextVelocity;
        age += step;
        OrientToVelocity();
        if (age >= maximumFlightTime) Destroy(gameObject);
    }

    private int EffectiveMask => DestructibleDebrisCollision.ExcludeDebrisLayer(collisionMask.value);

    private bool TryGetFirstHit(Vector3 origin, Vector3 travel, out RaycastHit nearest)
    {
        nearest = default;
        float distance = travel.magnitude;
        int count = Physics.SphereCastNonAlloc(origin, collisionRadius, travel / distance, hits,
            distance, EffectiveMask, QueryTriggerInteraction.Collide);
        RaycastHit[] results = hits;
        if (count == hits.Length)
        {
            results = Physics.SphereCastAll(origin, collisionRadius, travel / distance, distance,
                EffectiveMask, QueryTriggerInteraction.Collide);
            count = results.Length;
        }
        float closest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = results[i];
            if (!CanHit(candidate.collider) || candidate.distance >= closest) continue;
            closest = candidate.distance;
            nearest = candidate;
            found = true;
        }
        return found;
    }

    private void CheckInitialOverlap()
    {
        Vector3 origin = transform.position;
        Vector3 tip = origin + transform.forward * arrowLength;
        int count = Physics.OverlapCapsuleNonAlloc(origin, tip, collisionRadius, overlaps,
            EffectiveMask, QueryTriggerInteraction.Collide);
        Collider[] results = overlaps;
        if (count == overlaps.Length)
        {
            results = Physics.OverlapCapsule(origin, tip, collisionRadius, EffectiveMask, QueryTriggerInteraction.Collide);
            count = results.Length;
        }
        Collider closest = null;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider candidate = results[i];
            if (!CanHit(candidate)) continue;
            float value = (candidate.ClosestPoint(origin) - origin).sqrMagnitude;
            if (value >= distance) continue;
            closest = candidate;
            distance = value;
        }
        if (closest != null)
            ResolveHit(closest, closest.ClosestPoint(tip), -transform.forward, origin);
    }

    private bool CanHit(Collider collider)
    {
        if (collider == null || !collider.enabled || collider.transform.IsChildOf(transform)) return false;
        if (owner != null && collider.transform.IsChildOf(owner.transform)) return false;
        ShieldGuardHitbox shield = collider.GetComponentInParent<ShieldGuardHitbox>();
        if (shield != null && shield.ContainsBlockCollider(collider)) return shield.IsBlocking;
        return !collider.isTrigger;
    }

    private void ResolveHit(Collider collider, Vector3 point, Vector3 normal, Vector3 traceOrigin)
    {
        if (terminal || !HasDamageAuthority()) return;
        // Consume the projectile before damage callbacks can interrupt its owner.
        terminal = true;
        Vector3 direction = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : transform.forward;
        PlayerHealth target = collider.GetComponentInParent<PlayerHealth>();
        Transform attachment = null;
        if (target != null && target.IsAlive && target.CanBeTargetedByEnemy)
        {
            if (ShieldGuardHitbox.TryBlockEnemyHit(target, owner, traceOrigin, point, direction,
                collisionRadius, out Vector3 shieldPoint))
            {
                point = shieldPoint;
                target.ReceiveEnemyShieldBlock(owner, point, direction);
            }
            else
            {
                attachment = target.transform;
                target.ReceiveEnemyDamage(damage, owner, point, direction,
                    PlayerDamageAnimationType.ReactionDamage, PlayerCameraImpactType.DefaultHit);
            }
        }
        float penetration = attachment != null ? Mathf.Clamp(playerPenetrationDepth, 0f, arrowLength) : 0.04f;
        ApplyImpactPose(point - transform.forward * Mathf.Max(0f, arrowLength - penetration),
            transform.rotation, attachment);
        Impacted?.Invoke(this, point, normal);
    }

    public void ApplyReplicatedImpact(Vector3 point, Vector3 normal)
    {
        ApplyReplicatedImpactPose(point - transform.forward * Mathf.Max(0f, arrowLength - 0.04f),
            transform.rotation, null);
    }

    public void ApplyReplicatedImpactPose(Vector3 position, Quaternion rotation, Transform attachment)
    {
        if (terminal) return;
        terminal = true;
        ApplyImpactPose(position, rotation, attachment);
    }

    private void ApplyImpactPose(Vector3 position, Quaternion rotation, Transform attachment)
    {
        StopFlightAudio();
        transform.SetPositionAndRotation(position, rotation);
        if (impactAudioCue != null)
        {
            AudioSource impactSource = GameAudioService.Play(impactAudioCue,
                position + transform.forward * arrowLength);
            if (impactSource != null) impactSource.loop = false;
        }
        ImpactTarget = attachment;
        if (ImpactTarget != null)
        {
            impactLocalPosition = ImpactTarget.InverseTransformPoint(position);
            impactLocalRotation = Quaternion.Inverse(ImpactTarget.rotation) * rotation;
        }

        if (impactLifetime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        SpawnedDestructibleFadeOut fadeOut = GetComponent<SpawnedDestructibleFadeOut>();
        if (fadeOut == null) fadeOut = gameObject.AddComponent<SpawnedDestructibleFadeOut>();
        fadeOut.Initialize(gameObject, impactLifetime, impactFadeOutDuration);
    }

    private void OrientToVelocity()
    {
        if (velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(velocity.normalized,
                Mathf.Abs(Vector3.Dot(velocity.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
    }

    private bool HasDamageAuthority()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode) return damageEnabled;
        if (ownerViewId == 0) return false;
        if (ownerSetup != null) return ownerSetup.HasAuthority;
        if (wasRoomView) return PhotonNetwork.IsMasterClient;
        PhotonView view = PhotonView.Find(ownerViewId);
        return view != null ? view.IsMine : PhotonNetwork.LocalPlayer.ActorNumber == launchingActor && damageEnabled;
    }
}
