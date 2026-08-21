using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("World/Reactions/Collision Reaction Signal Bridge")]
public class CollisionReactionSignalBridge : MonoBehaviour
{
    [Header("Target")]
    [HideInInspector] [SerializeField] private ReactionSignalEmitter signalEmitter;
    [SerializeField] private ReactionSignalTargetMode targetMode = ReactionSignalTargetMode.SelfReceiver;
    [Header("Signals")]
    [SerializeField] private string collisionEnterSignalId = "Impact";
    [SerializeField] private string collisionStaySignalId;
    [SerializeField] private string collisionExitSignalId;
    [SerializeField] [Min(0f)] private float minimumRelativeSpeed = 0.25f;
    [SerializeField] [Min(0f)] private float stayEmitInterval = 0.25f;
    [SerializeField] private LayerMask collisionMask = Physics.DefaultRaycastLayers;

    private readonly Dictionary<Collider, float> nextStayEmitTimes = new Dictionary<Collider, float>();

    public ReactionSignalTargetMode TargetMode => targetMode;
    public string CollisionEnterSignalId => collisionEnterSignalId;
    public string CollisionStaySignalId => collisionStaySignalId;
    public string CollisionExitSignalId => collisionExitSignalId;

    protected virtual void Reset()
    {
        ResolveReferences();
    }

    protected virtual void Awake()
    {
        ResolveReferences();
    }

    protected virtual void OnValidate()
    {
        minimumRelativeSpeed = Mathf.Max(0f, minimumRelativeSpeed);
        stayEmitInterval = Mathf.Max(0f, stayEmitInterval);
        ResolveReferences();
    }

    protected virtual void OnDisable()
    {
        nextStayEmitTimes.Clear();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsEligible(collision))
            return;

        EmitSignal(collisionEnterSignalId, collision, requireMinimumSpeed: true);
        Collider otherCollider = collision.collider;
        if (otherCollider != null)
            nextStayEmitTimes[otherCollider] = Time.time + Mathf.Max(0f, stayEmitInterval);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!IsEligible(collision) || string.IsNullOrWhiteSpace(collisionStaySignalId))
            return;

        Collider otherCollider = collision.collider;
        if (otherCollider == null)
            return;

        float nextEmitTime = 0f;
        nextStayEmitTimes.TryGetValue(otherCollider, out nextEmitTime);
        if (stayEmitInterval > 0f && Time.time + 0.0001f < nextEmitTime)
            return;

        EmitSignal(collisionStaySignalId, collision, requireMinimumSpeed: true);
        nextStayEmitTimes[otherCollider] = Time.time + Mathf.Max(0f, stayEmitInterval);
    }

    private void OnCollisionExit(Collision collision)
    {
        Collider otherCollider = collision != null ? collision.collider : null;
        if (otherCollider != null)
            nextStayEmitTimes.Remove(otherCollider);

        if (!IsEligible(collision))
            return;

        EmitSignal(collisionExitSignalId, collision, requireMinimumSpeed: false);
    }

    private bool EmitSignal(string signalId, Collision collision, bool requireMinimumSpeed)
    {
        if (string.IsNullOrWhiteSpace(signalId) || collision == null)
            return false;

        if (requireMinimumSpeed && collision.relativeVelocity.magnitude + 0.0001f < minimumRelativeSpeed)
            return false;

        ResolveReferences();

        Vector3 worldPosition = ResolveCollisionPoint(collision);
        Vector3 worldDirection = ResolveCollisionDirection(collision);

        if (targetMode == ReactionSignalTargetMode.OtherReceiver)
            return ReactionSignalEmitter.TryEmit(collision.collider, signalId, worldPosition, worldDirection);

        if (signalEmitter != null && signalEmitter.TryEmit(signalId, worldPosition, worldDirection))
            return true;

        return ReactionSignalEmitter.TryEmit(this, signalId, worldPosition, worldDirection);
    }

    private bool IsEligible(Collision collision)
    {
        return collision != null
            && collision.collider != null
            && ((collisionMask.value & (1 << collision.collider.gameObject.layer)) != 0);
    }

    private void ResolveReferences()
    {
        if (signalEmitter == null)
            signalEmitter = GetComponent<ReactionSignalEmitter>();
    }

    private Vector3 ResolveCollisionPoint(Collision collision)
    {
        if (collision.contactCount > 0)
            return collision.GetContact(0).point;

        if (collision.collider != null)
            return collision.collider.ClosestPoint(transform.position);

        return transform.position;
    }

    private Vector3 ResolveCollisionDirection(Collision collision)
    {
        Vector3 direction = -collision.relativeVelocity;
        if (direction.sqrMagnitude > 0.0001f)
            return direction.normalized;

        if (collision.collider == null)
            return transform.forward;

        direction = collision.collider.transform.position - transform.position;
        if (direction.sqrMagnitude <= 0.0001f)
            return transform.forward;

        return direction.normalized;
    }
}
