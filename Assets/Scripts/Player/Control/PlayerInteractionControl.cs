using Photon.Pun;
using UnityEngine;

public interface IPlayerInteractionLockOwner
{
    void OnInteractionLockEnded(PlayerInteractionControl control);
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class PlayerInteractionControl : MonoBehaviour
{
    private MonoBehaviour owner;
    private float lockedUntil;
    private bool stopPlanarVelocity;
    private PhotonView view;
    private PlayerHealth health;
    private Rigidbody body;

    public bool HasLocalAuthority => view == null || view.IsMine;
    public bool IsLocked => isActiveAndEnabled && HasLocalAuthority && owner != null && owner.isActiveAndEnabled
        && (health == null || health.IsAlive) && Time.time < lockedUntil;
    public static bool IsLockedFor(Component component) => component != null
        && component.GetComponentInParent<PlayerInteractionControl>() is { } control && control.IsLocked;

    private void Awake() => ResolveReferences();
    private void ResolveReferences()
    {
        view = GetComponent<PhotonView>(); health = GetComponent<PlayerHealth>(); body = GetComponent<Rigidbody>();
    }

    public bool TryLock(MonoBehaviour source, float duration)
    {
        ResolveReferences();
        if (!isActiveAndEnabled || !HasLocalAuthority || source == null || !source.isActiveAndEnabled || IsLocked
            || (health != null && !health.IsAlive) || duration <= 0f || float.IsNaN(duration) || float.IsInfinity(duration)) return false;
        EndLock();
        owner = source;
        lockedUntil = Time.time + duration;
        stopPlanarVelocity = true;
        GetComponent<PlayerMovement>()?.PrepareForInteractionLock();
        return true;
    }

    public void Release(MonoBehaviour source) { if (owner == source) EndLock(); }
    private void Update() { if (owner != null && !IsLocked) EndLock(); }
    private void FixedUpdate()
    {
        if (!IsLocked || !stopPlanarVelocity) return;
        if (body != null && !body.isKinematic) body.linearVelocity = Vector3.up * body.linearVelocity.y;
        stopPlanarVelocity = false;
    }
    private void OnDisable() => EndLock();
    private void EndLock()
    {
        var previous = owner;
        owner = null; lockedUntil = 0f; stopPlanarVelocity = false;
        if (previous != null && previous is IPlayerInteractionLockOwner listener) listener.OnInteractionLockEnded(this);
    }

    public static void PlayAnimationTrigger(GameObject player, string triggerName)
    {
        if (player == null || string.IsNullOrWhiteSpace(triggerName)) return;
        int hash = Animator.StringToHash(triggerName);
        foreach (var animator in player.GetComponentsInChildren<Animator>(true))
        {
            if (!animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) continue;
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Trigger)
                { animator.SetTrigger(hash); break; }
        }
    }
}
