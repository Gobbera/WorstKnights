using UnityEngine;

public static class EnemyArcherBallistics
{
    public static bool TryGetLaunchVelocity(Vector3 origin, Vector3 target, float speed, float gravity,
        out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (!IsFinite(origin) || !IsFinite(target) || !float.IsFinite(speed) || speed <= 0f
            || !float.IsFinite(gravity) || gravity < 0f)
            return false;
        Vector3 displacement = target - origin;
        if (displacement.sqrMagnitude < 0.000001f) return false;
        if (gravity < 0.0001f)
        {
            velocity = displacement.normalized * speed;
            return true;
        }
        Vector3 horizontal = Vector3.ProjectOnPlane(displacement, Vector3.up);
        float distance = horizontal.magnitude;
        float speedSquared = speed * speed;
        if (distance < 0.0001f)
        {
            if (displacement.y > speedSquared / (2f * gravity)) return false;
            velocity = Vector3.up * (displacement.y > 0f ? speed : -speed);
            return true;
        }
        double discriminant = (double)speedSquared * speedSquared
            - gravity * (gravity * (double)distance * distance + 2d * displacement.y * speedSquared);
        if (discriminant < 0d) return false;
        // The lower arc gives the player a readable, direct shot.
        double tangent = (speedSquared - System.Math.Sqrt(discriminant)) / (gravity * distance);
        float cosine = (float)(1d / System.Math.Sqrt(1d + tangent * tangent));
        velocity = horizontal / distance * (speed * cosine)
            + Vector3.up * (float)(speed * cosine * tangent);
        return IsFinite(velocity);
    }

    public static bool ShouldRetreat(float distance, bool alreadyRetreating,
        float minimumPreferredDistance, float retreatEndDistance)
    {
        return float.IsFinite(distance) && distance <
            (alreadyRetreating ? Mathf.Max(minimumPreferredDistance, retreatEndDistance) : minimumPreferredDistance);
    }

    public static bool IsFinite(Vector3 value) => float.IsFinite(value.x)
        && float.IsFinite(value.y) && float.IsFinite(value.z);
}
