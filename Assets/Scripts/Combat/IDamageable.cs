public interface IDamageable
{
    bool IsAlive { get; }
    CombatAlignment Alignment { get; }
    void ApplyDamage(DamageInfo damageInfo);
}
