using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum InteractionRangeMode { Distance, BoxCollider }

[DisallowMultipleComponent]
[RequireComponent(typeof(InteractionTriggerMechanism))]
[AddComponentMenu("World/Mechanisms/Interaction Trigger Interactable")]
public sealed class InteractionTriggerInteractable : MonoBehaviour, IPlayerInteractable, IPlayerInteractionPromptProvider,
    IPlayerInteractionRangeProvider, IPlayerInteractionAreaProvider, IPlayerInteractionLockOwner
{
    [Header("Interaction Range")]
    [SerializeField] private InteractionRangeMode rangeMode;
    [Tooltip("Maximum camera-to-collider distance in meters, capped by the player's Interaction Distance.")]
    [SerializeField, Min(0.1f)] private float interactionDistance = 3f;
    [Tooltip("Box Collider mode uses the player's root/body position inside this enabled volume. Use a separate trigger collider.")]
    [SerializeField] private BoxCollider interactionBox;
    [Header("Player Interaction")]
    [SerializeField] private bool lockPlayerControls;
    [SerializeField, Min(0f)] private float controlLockDuration = 1f;
    [Tooltip("Optional Trigger parameter on the player's active Animators. Leave empty until the animation states are authored.")]
    [SerializeField] private string playerAnimationTrigger;
    [Header("Local Player Events")]
    [SerializeField] private UnityEvent<GameObject> onInteractionStarted = new UnityEvent<GameObject>();
    [SerializeField] private UnityEvent<GameObject> onInteractionFinished = new UnityEvent<GameObject>();
    private readonly HashSet<PlayerInteractionControl> lockedPlayers = new HashSet<PlayerInteractionControl>();
    private Collider[] interactionColliders;
    private int lastAcceptedSequence = -1;
    public int InteractionPriority => 200;
    private InteractionTriggerSystem System => GetComponentInParent<InteractionTriggerSystem>();
    public UnityEvent<GameObject> OnInteractionStarted => onInteractionStarted;
    public UnityEvent<GameObject> OnInteractionFinished => onInteractionFinished;
    public InteractionRangeMode RangeMode => rangeMode;
    public BoxCollider InteractionBox => interactionBox;
    public bool UsesInteractionArea => rangeMode == InteractionRangeMode.BoxCollider;
    public Collider InteractionAreaCollider => interactionBox;

    private void OnEnable() => RefreshAreaRegistration();
    private void OnValidate()
    {
        interactionDistance = Mathf.Max(0.1f, interactionDistance);
        controlLockDuration = Mathf.Max(0f, controlLockDuration);
        if (Application.IsPlaying(gameObject)) RefreshAreaRegistration();
    }
    private void RefreshAreaRegistration()
    {
        if (isActiveAndEnabled && rangeMode == InteractionRangeMode.BoxCollider) PlayerInteractionAreaRegistry.Register(this);
        else PlayerInteractionAreaRegistry.Unregister(this);
    }

    public bool IsInInteractionRange(PlayerPickupInteractor interactor)
    {
        if (interactor == null) return false;
        if (rangeMode == InteractionRangeMode.BoxCollider)
        {
            if (interactionBox == null || !interactionBox.enabled || !interactionBox.gameObject.activeInHierarchy) return false;
            Vector3 scale = interactionBox.transform.lossyScale;
            if (scale.x == 0f || scale.y == 0f || scale.z == 0f) return false;
            Vector3 point = interactionBox.transform.InverseTransformPoint(interactor.transform.position) - interactionBox.center;
            Vector3 half = interactionBox.size * 0.5f;
            return half.x > 0f && half.y > 0f && half.z > 0f
                && Mathf.Abs(point.x) <= half.x + 0.0001f && Mathf.Abs(point.y) <= half.y + 0.0001f && Mathf.Abs(point.z) <= half.z + 0.0001f;
        }
        float range = Mathf.Min(interactionDistance, interactor.InteractionDistance);
        if (interactionColliders == null) interactionColliders = GetComponentsInChildren<Collider>(true);
        Vector3 origin = interactor.InteractionOrigin;
        bool hasCollider = false;
        foreach (var collider in interactionColliders)
        {
            if (collider == null || collider == interactionBox || collider.isTrigger || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            hasCollider = true;
            if ((collider.ClosestPoint(origin) - origin).sqrMagnitude <= range * range) return true;
        }
        return !hasCollider && (transform.position - origin).sqrMagnitude <= range * range;
    }

    public float GetAreaInteractionReach(PlayerPickupInteractor interactor)
    {
        if (rangeMode != InteractionRangeMode.BoxCollider || !IsInInteractionRange(interactor)) return 0f;
        if (interactionColliders == null) interactionColliders = GetComponentsInChildren<Collider>(true);
        Vector3 origin = interactor.InteractionOrigin;
        float reach = Vector3.Distance(origin, transform.position);
        foreach (var collider in interactionColliders)
            if (collider != null && collider != interactionBox && !collider.isTrigger && collider.enabled && collider.gameObject.activeInHierarchy)
                reach = Mathf.Max(reach, Vector3.Distance(origin, collider.bounds.center) + collider.bounds.extents.magnitude);
        return reach + 0.25f;
    }
    public bool TryInteract(PlayerPickupInteractor interactor)
    {
        var system = System;
        return isActiveAndEnabled && interactor != null && CanBeginPlayerInteraction(interactor)
            && IsInInteractionRange(interactor) && system != null && system.TryUse(interactor.gameObject);
    }
    public bool TryGetInteractionPrompt(PlayerInteractionPromptContext context, out string promptTemplate)
    {
        var system = System;
        promptTemplate = system != null ? system.InteractionPrompt : string.Empty;
        return isActiveAndEnabled && system != null && system.CanInteract && IsInInteractionRange(context.Interactor)
            && CanBeginPlayerInteraction(context.Interactor);
    }

    private bool CanBeginPlayerInteraction(PlayerPickupInteractor interactor)
    {
        if (interactor == null || PlayerInteractionControl.IsLockedFor(interactor)) return false;
        var movement = interactor.GetComponent<PlayerMovement>();
        return !lockPlayerControls || movement == null || movement.CanBeginInventoryItemAction();
    }

    internal void NotifyAccepted(PlayerPickupInteractor interactor, int sequence)
    {
        if (!isActiveAndEnabled || interactor == null || sequence <= lastAcceptedSequence) return;
        lastAcceptedSequence = sequence;
        PlayerInteractionControl.PlayAnimationTrigger(interactor.gameObject, playerAnimationTrigger);
        var control = interactor.GetComponent<PlayerInteractionControl>();
        if (control == null || !control.HasLocalAuthority) return;
        bool locked = lockPlayerControls && control.TryLock(this, controlLockDuration);
        if (locked) lockedPlayers.Add(control);
        onInteractionStarted.Invoke(interactor.gameObject);
        if (!locked) onInteractionFinished.Invoke(interactor.gameObject);
    }

    internal void ResetAcceptanceSequence() => lastAcceptedSequence = -1;

    public void OnInteractionLockEnded(PlayerInteractionControl control)
    {
        if (lockedPlayers.Remove(control)) onInteractionFinished.Invoke(control.gameObject);
    }
    private void OnDisable()
    {
        PlayerInteractionAreaRegistry.Unregister(this);
        var players = new List<PlayerInteractionControl>(lockedPlayers);
        foreach (var control in players) if (control != null) control.Release(this);
        lockedPlayers.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        if (rangeMode != InteractionRangeMode.BoxCollider || interactionBox == null) return;
        var matrix = Gizmos.matrix; var color = Gizmos.color;
        Gizmos.matrix = interactionBox.transform.localToWorldMatrix;
        Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.12f); Gizmos.DrawCube(interactionBox.center, interactionBox.size);
        Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.9f); Gizmos.DrawWireCube(interactionBox.center, interactionBox.size);
        Gizmos.matrix = matrix; Gizmos.color = color;
    }
}
