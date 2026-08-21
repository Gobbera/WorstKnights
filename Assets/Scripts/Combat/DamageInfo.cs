using UnityEngine;

public enum PlayerCameraImpactType
{
    None = 0,
    DefaultHit = 1,
    HeavyHit = 2
}

public readonly struct DamageInfo
{
    public DamageInfo(
        float amount,
        GameObject instigator,
        CombatAlignment sourceAlignment,
        Vector3 hitPoint,
        Vector3 hitDirection,
        PlayerDamageAnimationType playerDamageAnimation = PlayerDamageAnimationType.None,
        PlayerCameraImpactType playerCameraImpact = PlayerCameraImpactType.None,
        float impactVfxAttackAngle = 0f,
        bool hasImpactVfxAttackAngle = false)
    {
        Amount = Mathf.Max(0f, amount);
        Instigator = instigator;
        SourceAlignment = sourceAlignment;
        HitPoint = hitPoint;
        HitDirection = hitDirection;
        PlayerDamageAnimation = playerDamageAnimation;
        PlayerCameraImpact = playerCameraImpact;
        ImpactVfxAttackAngle = Mathf.Abs(impactVfxAttackAngle);
        HasImpactVfxAttackAngle = hasImpactVfxAttackAngle;
    }

    public float Amount { get; }
    public GameObject Instigator { get; }
    public CombatAlignment SourceAlignment { get; }
    public Vector3 HitPoint { get; }
    public Vector3 HitDirection { get; }
    public PlayerDamageAnimationType PlayerDamageAnimation { get; }
    public PlayerCameraImpactType PlayerCameraImpact { get; }
    public float ImpactVfxAttackAngle { get; }
    public bool HasImpactVfxAttackAngle { get; }
}
