using UnityEngine;

[System.Serializable]
public struct DamageKnockbackSettings
{
    public bool enabled;
    [Min(0f)] public float horizontalStrength;
    [Min(0f)] public float damageStrengthMultiplier;
    [Min(0f)] public float upwardStrength;
    [Min(0f)] public float controlLockDuration;
}

public abstract class CombatHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] [Min(1f)] private float maxHealth = 100f;
    [SerializeField] [Min(0f)] private float damageImmunityDuration = 0.1f;
    [Header("Impact")]
    [SerializeField] private DamageKnockbackSettings damageKnockback = new DamageKnockbackSettings
    {
        enabled = true,
        horizontalStrength = 1.75f,
        damageStrengthMultiplier = 0.03f,
        upwardStrength = 0f,
        controlLockDuration = 0.08f
    };

    private float currentHealth;
    private float invulnerableUntil;

    public abstract CombatAlignment Alignment { get; }
    public bool IsAlive { get; private set; } = true;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    protected virtual void Awake()
    {
        RestoreFullHealth();
    }

    public void ApplyDamage(DamageInfo damageInfo)
    {
        ApplyDamageInternal(damageInfo, ignoreDamageImmunity: false);
    }

    protected void ApplyDamageIgnoringImmunity(DamageInfo damageInfo)
    {
        ApplyDamageInternal(damageInfo, ignoreDamageImmunity: true);
    }

    private void ApplyDamageInternal(DamageInfo damageInfo, bool ignoreDamageImmunity)
    {
        if (!CanReceiveDamage(damageInfo, ignoreDamageImmunity))
            return;

        currentHealth = Mathf.Max(0f, currentHealth - damageInfo.Amount);
        invulnerableUntil = Time.time + Mathf.Max(0f, damageImmunityDuration);
        OnDamaged(damageInfo);

        if (currentHealth > 0f)
            return;

        IsAlive = false;
        OnDied(damageInfo);
    }

    protected virtual bool CanReceiveDamage(DamageInfo damageInfo, bool ignoreDamageImmunity)
    {
        if (!IsAlive || damageInfo.Amount <= 0f)
            return false;

        if (!ignoreDamageImmunity && Time.time < invulnerableUntil)
            return false;

        return damageInfo.SourceAlignment == CombatAlignment.Neutral
            || damageInfo.SourceAlignment != Alignment;
    }

    protected void RestoreFullHealth()
    {
        currentHealth = Mathf.Max(1f, maxHealth);
        IsAlive = true;
        invulnerableUntil = 0f;
    }

    protected float RecoverHealth(float amount)
    {
        if (!IsAlive)
            return 0f;

        float clampedAmount = Mathf.Max(0f, amount);
        if (clampedAmount <= 0f)
            return 0f;

        float previousHealth = currentHealth;
        currentHealth = Mathf.Min(Mathf.Max(1f, maxHealth), currentHealth + clampedAmount);
        return currentHealth - previousHealth;
    }

    protected void SetInvulnerableFor(float duration)
    {
        invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + Mathf.Max(0f, duration));
    }

    public void ApplyReplicatedState(float replicatedHealth, bool replicatedIsAlive)
    {
        currentHealth = Mathf.Clamp(replicatedHealth, 0f, Mathf.Max(1f, maxHealth));
        if (replicatedIsAlive && currentHealth <= 0f)
            currentHealth = 1f;
        else if (!replicatedIsAlive)
            currentHealth = 0f;

        IsAlive = replicatedIsAlive;
        invulnerableUntil = 0f;
        OnReplicatedStateApplied();
    }

    protected virtual void OnDamaged(DamageInfo damageInfo)
    {
    }

    protected virtual void OnDied(DamageInfo damageInfo)
    {
    }

    protected virtual void OnReplicatedStateApplied()
    {
    }

    protected bool TryBuildDamageKnockback(DamageInfo damageInfo, out Vector3 velocityChange, out float controlLockDuration)
    {
        velocityChange = Vector3.zero;
        controlLockDuration = 0f;

        if (!damageKnockback.enabled)
            return false;

        float horizontalStrength = Mathf.Max(0f, damageKnockback.horizontalStrength + damageInfo.Amount * damageKnockback.damageStrengthMultiplier);
        float upwardStrength = Mathf.Max(0f, damageKnockback.upwardStrength);
        if (horizontalStrength <= 0.0001f && upwardStrength <= 0.0001f)
            return false;

        Vector3 planarDirection = ResolveDamageKnockbackDirection(damageInfo);
        velocityChange = planarDirection * horizontalStrength + Vector3.up * upwardStrength;
        controlLockDuration = Mathf.Max(0f, damageKnockback.controlLockDuration);
        return velocityChange.sqrMagnitude > 0.0001f;
    }

    private Vector3 ResolveDamageKnockbackDirection(DamageInfo damageInfo)
    {
        Vector3 hitDirection = Vector3.ProjectOnPlane(damageInfo.HitDirection, Vector3.up);
        if (hitDirection.sqrMagnitude > 0.0001f)
            return hitDirection.normalized;

        if (damageInfo.Instigator != null)
        {
            Vector3 awayFromInstigator = Vector3.ProjectOnPlane(transform.position - damageInfo.Instigator.transform.position, Vector3.up);
            if (awayFromInstigator.sqrMagnitude > 0.0001f)
                return awayFromInstigator.normalized;
        }

        Vector3 awayFromHitPoint = Vector3.ProjectOnPlane(transform.position - damageInfo.HitPoint, Vector3.up);
        if (awayFromHitPoint.sqrMagnitude > 0.0001f)
            return awayFromHitPoint.normalized;

        Vector3 fallbackDirection = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
        return fallbackDirection.sqrMagnitude > 0.0001f
            ? fallbackDirection.normalized
            : Vector3.back;
    }
}
