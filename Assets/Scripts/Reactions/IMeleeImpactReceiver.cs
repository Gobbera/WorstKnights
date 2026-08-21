using UnityEngine;

public interface IMeleeImpactReceiver
{
    void ReceiveMeleeImpact(DamageInfo damageInfo, Collider hitCollider);
}
