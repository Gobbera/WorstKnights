using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("World/Reactions/Trigger Volume Reaction Signal Bridge")]
public class TriggerVolumeReactionSignalBridge : MonoBehaviour
{
    private sealed class TriggerParticipantState
    {
        public int activeColliderCount;
        public float nextStayEmitTime;
    }

    [Header("Target")]
    [HideInInspector] [SerializeField] private ReactionSignalEmitter signalEmitter;
    [SerializeField] private ReactionSignalTargetMode targetMode = ReactionSignalTargetMode.SelfReceiver;
    [Header("Signals")]
    [SerializeField] private string enteredSignalId = "Entered";
    [SerializeField] private string stayedSignalId;
    [SerializeField] private string exitedSignalId = "Exited";
    [SerializeField] [Min(0f)] private float stayEmitInterval = 0.25f;
    [SerializeField] private LayerMask detectionMask = Physics.DefaultRaycastLayers;

    private readonly Dictionary<Object, TriggerParticipantState> participantStates = new Dictionary<Object, TriggerParticipantState>();

    public ReactionSignalTargetMode TargetMode => targetMode;
    public string EnteredSignalId => enteredSignalId;
    public string StayedSignalId => stayedSignalId;
    public string ExitedSignalId => exitedSignalId;

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
        stayEmitInterval = Mathf.Max(0f, stayEmitInterval);
        ResolveReferences();
    }

    protected virtual void OnDisable()
    {
        participantStates.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsEligible(other))
            return;

        Object participantKey = ResolveParticipantKey(other);
        if (participantKey == null)
            return;

        TriggerParticipantState participantState = GetOrCreateParticipantState(participantKey);
        participantState.activeColliderCount++;
        if (participantState.activeColliderCount == 1)
            EmitSignal(enteredSignalId, other);

        participantState.nextStayEmitTime = Time.time + Mathf.Max(0f, stayEmitInterval);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsEligible(other) || string.IsNullOrWhiteSpace(stayedSignalId))
            return;

        Object participantKey = ResolveParticipantKey(other);
        if (participantKey == null)
            return;

        TriggerParticipantState participantState = GetOrCreateParticipantState(participantKey);
        if (stayEmitInterval > 0f && Time.time + 0.0001f < participantState.nextStayEmitTime)
            return;

        EmitSignal(stayedSignalId, other);
        participantState.nextStayEmitTime = Time.time + Mathf.Max(0f, stayEmitInterval);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsEligible(other))
            return;

        Object participantKey = ResolveParticipantKey(other);
        if (participantKey == null)
            return;

        if (!participantStates.TryGetValue(participantKey, out TriggerParticipantState participantState))
        {
            EmitSignal(exitedSignalId, other);
            return;
        }

        participantState.activeColliderCount = Mathf.Max(0, participantState.activeColliderCount - 1);
        if (participantState.activeColliderCount > 0)
            return;

        participantStates.Remove(participantKey);
        EmitSignal(exitedSignalId, other);
    }

    private bool EmitSignal(string signalId, Collider other)
    {
        if (string.IsNullOrWhiteSpace(signalId))
            return false;

        ResolveReferences();

        Vector3 worldPosition = ResolveTriggerPosition(other);
        Vector3 worldDirection = ResolveTriggerDirection(other);

        if (targetMode == ReactionSignalTargetMode.OtherReceiver)
            return ReactionSignalEmitter.TryEmit(other, signalId, worldPosition, worldDirection);

        if (signalEmitter != null && signalEmitter.TryEmit(signalId, worldPosition, worldDirection))
            return true;

        return ReactionSignalEmitter.TryEmit(this, signalId, worldPosition, worldDirection);
    }

    private bool IsEligible(Collider other)
    {
        return other != null && ((detectionMask.value & (1 << other.gameObject.layer)) != 0);
    }

    private void ResolveReferences()
    {
        if (signalEmitter == null)
            signalEmitter = GetComponent<ReactionSignalEmitter>();
    }

    private TriggerParticipantState GetOrCreateParticipantState(Object participantKey)
    {
        if (!participantStates.TryGetValue(participantKey, out TriggerParticipantState participantState))
        {
            participantState = new TriggerParticipantState();
            participantStates.Add(participantKey, participantState);
        }

        return participantState;
    }

    private static Object ResolveParticipantKey(Collider other)
    {
        if (other == null)
            return null;

        if (other.attachedRigidbody != null)
            return other.attachedRigidbody;

        Transform root = other.transform.root;
        return root != null ? root : other.transform;
    }

    private Vector3 ResolveTriggerPosition(Collider other)
    {
        if (other != null)
            return other.ClosestPoint(transform.position);

        return transform.position;
    }

    private Vector3 ResolveTriggerDirection(Collider other)
    {
        if (other == null)
            return transform.forward;

        Vector3 direction = other.transform.position - transform.position;
        if (direction.sqrMagnitude <= 0.0001f)
            return transform.forward;

        return direction.normalized;
    }
}
