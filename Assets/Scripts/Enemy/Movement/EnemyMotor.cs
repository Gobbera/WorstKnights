using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemySetup))]
public class EnemyMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] [Min(0f)] private float moveSpeed = 3.25f;
    [SerializeField] [Min(0f)] private float stopDistance = 1.1f;
    [SerializeField] [Min(0f)] private float turnSpeed = 10f;
    [SerializeField] private EnemySetup enemySetup;

    private Vector3 scriptedKnockbackVelocity;
    private float damageKnockbackRemainingTime;
    private bool isAlive = true;

    public float MoveSpeed => moveSpeed;
    public float StopDistance => stopDistance;
    public float TurnSpeed => turnSpeed;
    public float CurrentPlanarSpeed { get; private set; }

    private void Awake()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();
    }

    private void FixedUpdate()
    {
        ApplyDamageKnockbackMotion(Time.fixedDeltaTime);
    }

    public void SetAliveState(bool alive)
    {
        isAlive = alive;
        if (!alive)
        {
            CurrentPlanarSpeed = 0f;
            scriptedKnockbackVelocity = Vector3.zero;
            damageKnockbackRemainingTime = 0f;
            Rigidbody rb = ResolveRigidbody();
            if (rb != null && !rb.isKinematic)
                rb.linearVelocity = Vector3.zero;
        }
    }

    public void Stop()
    {
        if (HasActiveDamageKnockback())
        {
            CurrentPlanarSpeed = GetDamageKnockbackPlanarSpeed();
            return;
        }

        StopPlanarMotion();
        CurrentPlanarSpeed = 0f;
    }

    public void MoveTowards(Vector3 planarOffset, float deltaTime)
    {
        if (!isAlive)
        {
            CurrentPlanarSpeed = 0f;
            return;
        }

        if (HasActiveDamageKnockback())
        {
            CurrentPlanarSpeed = GetDamageKnockbackPlanarSpeed();
            return;
        }

        float stoppingDistance = Mathf.Max(0f, stopDistance);
        if (planarOffset.sqrMagnitude <= stoppingDistance * stoppingDistance)
        {
            StopPlanarMotion();
            CurrentPlanarSpeed = 0f;
            return;
        }

        Vector3 moveDirection = planarOffset.normalized;
        float safeMoveSpeed = Mathf.Max(0f, moveSpeed);
        CurrentPlanarSpeed = safeMoveSpeed;

        Rigidbody rb = ResolveRigidbody();
        if (rb != null && !rb.isKinematic)
        {
            Vector3 desiredPlanarVelocity = moveDirection * safeMoveSpeed;
            rb.linearVelocity = new Vector3(desiredPlanarVelocity.x, rb.linearVelocity.y, desiredPlanarVelocity.z);
        }
        else if (rb != null)
        {
            Vector3 nextPosition = rb.position + moveDirection * safeMoveSpeed * deltaTime;
            rb.MovePosition(nextPosition);
        }
        else
        {
            Vector3 nextPosition = transform.position + moveDirection * safeMoveSpeed * deltaTime;
            transform.position = nextPosition;
        }
    }

    public void RotateTowards(Vector3 planarOffset, float deltaTime)
    {
        RotateTowards(planarOffset, deltaTime, 1f);
    }

    public void RotateTowards(Vector3 planarOffset, float deltaTime, float turnSpeedMultiplier)
    {
        if (!isAlive || planarOffset.sqrMagnitude <= 0.0001f || HasActiveDamageKnockback())
            return;

        Quaternion targetRotation = Quaternion.LookRotation(planarOffset.normalized, Vector3.up);
        float safeTurnSpeed = Mathf.Max(0f, turnSpeed) * Mathf.Max(0f, turnSpeedMultiplier);
        float rotationT = 1f - Mathf.Exp(-safeTurnSpeed * deltaTime);
        Quaternion nextRotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationT);

        Rigidbody rb = enemySetup != null ? enemySetup.Rigidbody : null;
        if (rb != null)
            rb.MoveRotation(nextRotation);
        else
            transform.rotation = nextRotation;
    }

    public void ApplyReplicatedPlanarSpeed(float speed)
    {
        CurrentPlanarSpeed = Mathf.Max(0f, speed);
    }

    public void ApplyDamageKnockback(Vector3 velocityChange, float duration)
    {
        if (velocityChange.sqrMagnitude <= 0.0001f)
            return;

        float safeDuration = Mathf.Max(0.05f, duration);
        damageKnockbackRemainingTime = Mathf.Max(damageKnockbackRemainingTime, safeDuration);

        Rigidbody rb = ResolveRigidbody();
        if (rb != null && !rb.isKinematic)
        {
            rb.AddForce(velocityChange, ForceMode.VelocityChange);
            CurrentPlanarSpeed = Mathf.Max(CurrentPlanarSpeed, GetRigidbodyPlanarSpeed(rb));
            return;
        }

        scriptedKnockbackVelocity += velocityChange;
        CurrentPlanarSpeed = Mathf.Max(CurrentPlanarSpeed, GetScriptedKnockbackPlanarSpeed());
    }

    private void ApplyDamageKnockbackMotion(float deltaTime)
    {
        if (!HasActiveDamageKnockback())
            return;

        Rigidbody rb = ResolveRigidbody();
        if (rb != null && !rb.isKinematic)
        {
            CurrentPlanarSpeed = GetRigidbodyPlanarSpeed(rb);
            damageKnockbackRemainingTime = Mathf.Max(0f, damageKnockbackRemainingTime - deltaTime);
            return;
        }

        if (scriptedKnockbackVelocity.sqrMagnitude <= 0.0001f)
        {
            scriptedKnockbackVelocity = Vector3.zero;
            damageKnockbackRemainingTime = 0f;
            CurrentPlanarSpeed = 0f;
            return;
        }

        Vector3 delta = scriptedKnockbackVelocity * deltaTime;
        if (rb != null)
            rb.MovePosition(rb.position + delta);
        else
            transform.position += delta;

        CurrentPlanarSpeed = GetScriptedKnockbackPlanarSpeed();

        float safeRemainingTime = Mathf.Max(deltaTime, damageKnockbackRemainingTime);
        scriptedKnockbackVelocity = Vector3.MoveTowards(
            scriptedKnockbackVelocity,
            Vector3.zero,
            scriptedKnockbackVelocity.magnitude * (deltaTime / safeRemainingTime));
        damageKnockbackRemainingTime = Mathf.Max(0f, damageKnockbackRemainingTime - deltaTime);

        if (damageKnockbackRemainingTime <= 0f)
        {
            scriptedKnockbackVelocity = Vector3.zero;
            CurrentPlanarSpeed = 0f;
        }
    }

    private bool HasActiveDamageKnockback()
    {
        return damageKnockbackRemainingTime > 0f;
    }

    private float GetDamageKnockbackPlanarSpeed()
    {
        Rigidbody rb = ResolveRigidbody();
        if (rb != null && !rb.isKinematic)
            return GetRigidbodyPlanarSpeed(rb);

        return GetScriptedKnockbackPlanarSpeed();
    }

    private void StopPlanarMotion()
    {
        Rigidbody rb = ResolveRigidbody();
        if (rb == null || rb.isKinematic)
            return;

        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
    }

    private Rigidbody ResolveRigidbody()
    {
        return enemySetup != null ? enemySetup.Rigidbody : GetComponent<Rigidbody>();
    }

    private static float GetRigidbodyPlanarSpeed(Rigidbody rb)
    {
        if (rb == null)
            return 0f;

        return Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up).magnitude;
    }

    private float GetScriptedKnockbackPlanarSpeed()
    {
        return Vector3.ProjectOnPlane(scriptedKnockbackVelocity, Vector3.up).magnitude;
    }
}
