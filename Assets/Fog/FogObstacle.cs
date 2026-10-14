using UnityEngine;

[DisallowMultipleComponent]
public class FogObstacle : MonoBehaviour
{
    [SerializeField, Min(0f)] private float radius = 1.5f;
    [SerializeField] private bool deriveRadiusFromCollider = true;
    [SerializeField, Min(0f)] private float colliderPadding = 0.15f;

    public float Radius => ResolveRadius();

    public float ResolveRadius()
    {
        if (!deriveRadiusFromCollider)
            return radius;

        Collider obstacleCollider = GetComponent<Collider>();
        if (obstacleCollider == null)
            return radius;

        Bounds bounds = obstacleCollider.bounds;
        float colliderRadius = Mathf.Max(bounds.extents.x, bounds.extents.z) + colliderPadding;
        return Mathf.Max(radius, colliderRadius);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.55f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, ResolveRadius());
    }
}
