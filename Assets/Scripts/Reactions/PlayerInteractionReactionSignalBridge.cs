using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Reactions/Player Interaction Reaction Signal Bridge")]
public sealed class PlayerInteractionReactionSignalBridge : MonoBehaviour, IPlayerInteractable
{
    [Header("Interaction")]
    [SerializeField] private int interactionPriority = 500;
    [SerializeField] private bool forwardToTargetInteractable = true;
    [SerializeField] private MonoBehaviour targetInteractable;
    [SerializeField] private bool requireSuccessfulTargetInteraction = true;
    [SerializeField] private bool emitWithoutTargetInteractable = true;
    [Header("Target")]
    [HideInInspector] [SerializeField] private ReactionSignalEmitter signalEmitter;
    [HideInInspector] [SerializeField] private ReactionSignalReceiver signalReceiver;
    [Header("Signals")]
    [SerializeField] private string interactedSignalId = "Interacted";
    [SerializeField] private Transform feedbackOrigin;
    [SerializeField] private bool directionFromInteractor = true;

    public int InteractionPriority => interactionPriority;
    public string InteractedSignalId => interactedSignalId;

    private void Reset()
    {
        ResolveReferences(resolveTargetInteractable: true);
    }

    private void Awake()
    {
        ResolveReferences(resolveTargetInteractable: true);
    }

    private void OnValidate()
    {
        ResolveReferences(resolveTargetInteractable: true);
    }

    public bool TryInteract(PlayerPickupInteractor interactor)
    {
        ResolveReferences(resolveTargetInteractable: true);

        bool usedTargetInteractable = false;
        bool targetHandledInteraction = false;
        if (forwardToTargetInteractable)
        {
            if (TryResolveTargetInteractable(out IPlayerInteractable target))
            {
                usedTargetInteractable = true;
                targetHandledInteraction = target.TryInteract(interactor);
                if (requireSuccessfulTargetInteraction && !targetHandledInteraction)
                    return false;
            }
            else if (!emitWithoutTargetInteractable)
            {
                return false;
            }
        }

        bool emitted = EmitSignal(interactor);
        return emitted || (usedTargetInteractable && targetHandledInteraction);
    }

    private bool EmitSignal(PlayerPickupInteractor interactor)
    {
        if (string.IsNullOrWhiteSpace(interactedSignalId))
            return false;

        ResolveReferences(resolveTargetInteractable: false);

        Vector3 worldPosition = ResolveWorldPosition();
        Vector3 worldDirection = ResolveWorldDirection(interactor, worldPosition);

        if (signalEmitter != null && signalEmitter.TryEmit(interactedSignalId, worldPosition, worldDirection))
            return true;

        if (signalReceiver != null)
        {
            signalReceiver.ReceiveSignal(interactedSignalId, worldPosition, worldDirection);
            return true;
        }

        return ReactionSignalEmitter.TryEmit(this, interactedSignalId, worldPosition, worldDirection);
    }

    private bool TryResolveTargetInteractable(out IPlayerInteractable target)
    {
        target = null;

        if (!forwardToTargetInteractable)
            return false;

        if (IsValidTargetInteractable(targetInteractable))
        {
            target = (IPlayerInteractable)targetInteractable;
            return true;
        }

        MonoBehaviour resolvedTarget = FindBestTargetInteractable();
        if (resolvedTarget == null)
            return false;

        targetInteractable = resolvedTarget;
        target = (IPlayerInteractable)resolvedTarget;
        return true;
    }

    private void ResolveReferences(bool resolveTargetInteractable)
    {
        if (signalEmitter == null)
            signalEmitter = GetComponent<ReactionSignalEmitter>();

        if (signalReceiver == null)
            ReactionSignalEmitter.TryResolveReceiver(this, out signalReceiver);

        if (resolveTargetInteractable
            && forwardToTargetInteractable
            && !IsValidTargetInteractable(targetInteractable))
        {
            targetInteractable = FindBestTargetInteractable();
        }
    }

    private MonoBehaviour FindBestTargetInteractable()
    {
        MonoBehaviour bestTarget = null;
        int bestPriority = int.MinValue;

        SelectBestTarget(GetComponents<MonoBehaviour>(), ref bestTarget, ref bestPriority);
        SelectBestTarget(GetComponentsInParent<MonoBehaviour>(true), ref bestTarget, ref bestPriority);
        SelectBestTarget(GetComponentsInChildren<MonoBehaviour>(true), ref bestTarget, ref bestPriority);

        return bestTarget;
    }

    private void SelectBestTarget(MonoBehaviour[] candidates, ref MonoBehaviour bestTarget, ref int bestPriority)
    {
        if (candidates == null)
            return;

        for (int i = 0; i < candidates.Length; i++)
        {
            MonoBehaviour candidate = candidates[i];
            if (!IsValidTargetInteractable(candidate))
                continue;

            IPlayerInteractable interactable = (IPlayerInteractable)candidate;
            if (interactable.InteractionPriority <= bestPriority)
                continue;

            bestTarget = candidate;
            bestPriority = interactable.InteractionPriority;
        }
    }

    private bool IsValidTargetInteractable(MonoBehaviour candidate)
    {
        return candidate != null
            && !ReferenceEquals(candidate, this)
            && candidate is IPlayerInteractable
            && candidate is not PlayerInteractionReactionSignalBridge;
    }

    private Vector3 ResolveWorldPosition()
    {
        return feedbackOrigin != null ? feedbackOrigin.position : transform.position;
    }

    private Vector3 ResolveWorldDirection(PlayerPickupInteractor interactor, Vector3 worldPosition)
    {
        if (directionFromInteractor && interactor != null)
        {
            Vector3 direction = worldPosition - interactor.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
                return direction.normalized;
        }

        return transform.forward;
    }
}
