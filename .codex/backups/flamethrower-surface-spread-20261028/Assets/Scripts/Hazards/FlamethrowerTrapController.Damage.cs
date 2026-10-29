using System;
using Photon.Pun;
using UnityEngine;

public sealed partial class FlamethrowerTrapController
{
    private bool IsObstruction(Collider collider)
    {
        if (collider == null || collider.isTrigger || collider.transform.IsChildOf(transform)) return false;
        return collider.GetComponentInParent<CombatHealth>() == null;
    }

    private int CastObstructions(Vector3 origin, Vector3 direction, float distance)
    {
        int count;
        while ((count = Physics.RaycastNonAlloc(origin, direction, obstructionHits, distance,
            obstructionMask, QueryTriggerInteraction.Ignore)) == obstructionHits.Length)
            Array.Resize(ref obstructionHits, obstructionHits.Length * 2);
        return count;
    }

    private float ResolveObstructedLength(Vector3 origin, Vector3 direction, float distance)
    {
        int count = CastObstructions(origin, direction, distance);
        float nearest = distance;
        for (int i = 0; i < count; i++)
            if (IsObstruction(obstructionHits[i].collider))
                nearest = Mathf.Min(nearest, Mathf.Max(0f, obstructionHits[i].distance - 0.01f));
        return nearest;
    }

    private bool HasLineOfSight(Vector3 point)
    {
        Vector3 delta = point - JetOrigin;
        if (delta.sqrMagnitude < 0.000001f) return true;
        int count = CastObstructions(JetOrigin, delta.normalized, delta.magnitude);
        for (int i = 0; i < count; i++)
            if (IsObstruction(obstructionHits[i].collider)) return false;
        return true;
    }

    private bool TryGetContact(Collider collider, out Vector3 point)
    {
        point = default;
        float centerDistance = Mathf.Clamp(Vector3.Dot(collider.bounds.center - JetOrigin, JetDirection), 0f, DamageLength);
        // End samples also catch a large actor whose edge, rather than its center, crosses the cone.
        for (int sample = 0; sample < 3; sample++)
        {
            float distance = sample == 0 ? centerDistance : sample == 1 ? 0f : DamageLength;
            Vector3 closest = collider.ClosestPoint(JetOrigin + JetDirection * distance);
            Vector3 offset = closest - JetOrigin;
            float axial = Vector3.Dot(offset, JetDirection);
            if (axial < -0.001f || axial > DamageLength + 0.001f) continue;
            float radius = Mathf.Lerp(startRadius, endRadius, Mathf.Clamp01(axial / range));
            if ((offset - JetDirection * axial).sqrMagnitude > radius * radius) continue;
            if (!HasLineOfSight(closest)) continue;
            point = closest;
            return true;
        }
        return false;
    }

    private void ApplyDamageTick(double now)
    {
        if (!IsEmitting || DamageLength <= 0.001f || damagePerSecond <= 0f) return;
        int count;
        while ((count = Physics.OverlapCapsuleNonAlloc(JetOrigin, JetOrigin + JetDirection * DamageLength,
            endRadius, overlaps, targetMask, QueryTriggerInteraction.Ignore)) == overlaps.Length)
            Array.Resize(ref overlaps, overlaps.Length * 2);
        damagedThisTick.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlaps[i];
            if (collider == null || collider.transform.IsChildOf(transform)) continue;
            CombatHealth health = collider.GetComponentInParent<CombatHealth>();
            if (health == null || !health.IsAlive || damagedThisTick.Contains(health)) continue;
            if (health is PlayerHealth player)
            {
                if (!damagePlayers || (Online && !player.IsLocallyOwned)) continue;
            }
            else if (health is EnemyHealth)
            {
                if (!damageEnemies || (Online && !PhotonNetwork.IsMasterClient)) continue;
            }
            else continue;
            if (nextDamageTime.TryGetValue(health, out double nextTime) && now + 0.00001d < nextTime) continue;
            if (!TryGetContact(collider, out Vector3 contact)) continue;
            damagedThisTick.Add(health);
            nextDamageTime[health] = now + damageTickInterval;
            float damage = damagePerSecond * damageTickInterval;
            if (health is PlayerHealth targetPlayer)
                targetPlayer.ReceiveEnvironmentalDamage(damage, gameObject, contact, JetDirection,
                    ignoreDamageImmunity: true, suppressKnockback: true,
                    playerDamageAnimation, playerCameraImpact);
            else if (health is EnemyHealth targetEnemy)
                targetEnemy.ReceiveEnvironmentalDamage(damage, gameObject, contact, JetDirection);
        }
    }
}
