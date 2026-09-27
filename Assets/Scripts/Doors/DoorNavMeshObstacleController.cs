using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[AddComponentMenu("World/Doors/Door NavMesh Obstacle Controller")]
public sealed class DoorNavMeshObstacleController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DoorController doorController;
    [SerializeField] private NavMeshObstacle obstacle;
    [SerializeField] private Transform obstacleHost;

    [Header("Obstacle Shape")]
    [SerializeField] private bool autoCreateObstacle = true;
    [SerializeField] private bool fitObstacleFromColliders = true;
    [SerializeField] private Vector3 obstaclePadding = new Vector3(0.35f, 0.2f, 0.35f);
    [SerializeField] private Vector3 fallbackObstacleSize = new Vector3(1.1f, 2.4f, 0.25f);
    [SerializeField] private Vector3 fallbackObstacleCenter = new Vector3(0f, 1.2f, 0f);

    [Header("Carving")]
    [SerializeField] private bool carveWhenOpen = true;
    [SerializeField] private bool carveWhenClosed;
    [SerializeField] private bool carveWhileMoving;

    [Header("Enemy Repath")]
    [SerializeField] private bool replanNearbyEnemiesOnStateChange = true;
    [SerializeField] [Min(0f)] private float replanRadius = 10f;
    [SerializeField] [Min(0f)] private float replanDelay = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private bool obstacleActive;
    private bool pendingEnemyReplan;
    private float enemyReplanTime = float.PositiveInfinity;

    private void Reset()
    {
        ResolveReferences();
        ConfigureObstacleShape();
        ApplyObstacleState(forceNotify: false);
    }

    private void OnValidate()
    {
        obstaclePadding.x = Mathf.Max(0f, obstaclePadding.x);
        obstaclePadding.y = Mathf.Max(0f, obstaclePadding.y);
        obstaclePadding.z = Mathf.Max(0f, obstaclePadding.z);
        fallbackObstacleSize.x = Mathf.Max(0.01f, fallbackObstacleSize.x);
        fallbackObstacleSize.y = Mathf.Max(0.01f, fallbackObstacleSize.y);
        fallbackObstacleSize.z = Mathf.Max(0.01f, fallbackObstacleSize.z);
        replanRadius = Mathf.Max(0f, replanRadius);
        replanDelay = Mathf.Max(0f, replanDelay);

        ResolveReferences();
        ConfigureObstacleShape();
        ApplyObstacleState(forceNotify: false);
    }

    private void Awake()
    {
        ResolveReferences();
        ConfigureObstacleShape();
        ApplyObstacleState(forceNotify: false);
    }

    private void OnEnable()
    {
        ResolveReferences();
        ConfigureObstacleShape();
        ApplyObstacleState(forceNotify: false);
    }

    private void OnDisable()
    {
        SetObstacleActive(false);
        pendingEnemyReplan = false;
    }

    private void LateUpdate()
    {
        ResolveReferences();
        ConfigureObstacleShape();
        ApplyObstacleState(forceNotify: true);
        FlushPendingEnemyReplan();
    }

    private void ResolveReferences()
    {
        if (doorController == null)
            doorController = GetComponent<DoorController>();

        if (obstacleHost == null)
            obstacleHost = doorController != null && doorController.MovingPart != null
                ? doorController.MovingPart
                : transform;

        if (obstacle == null && obstacleHost != null)
            obstacle = obstacleHost.GetComponent<NavMeshObstacle>();

        if (obstacle == null && autoCreateObstacle && obstacleHost != null && Application.isPlaying)
            obstacle = obstacleHost.gameObject.AddComponent<NavMeshObstacle>();
    }

    private void ConfigureObstacleShape()
    {
        if (obstacle == null)
            return;

        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;

        if (fitObstacleFromColliders && TryCalculateColliderLocalBounds(out Bounds localBounds))
        {
            obstacle.center = localBounds.center;
            obstacle.size = Vector3.Max(localBounds.size + obstaclePadding, Vector3.one * 0.01f);
            return;
        }

        obstacle.center = fallbackObstacleCenter;
        obstacle.size = fallbackObstacleSize;
    }

    private bool TryCalculateColliderLocalBounds(out Bounds localBounds)
    {
        localBounds = default;
        if (obstacleHost == null)
            return false;

        Collider[] colliders = obstacleHost.GetComponentsInChildren<Collider>(true);
        bool hasBounds = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider candidate = colliders[i];
            if (candidate == null || !candidate.enabled || candidate.isTrigger)
                continue;

            EncapsulateWorldBounds(candidate.bounds, ref localBounds, ref hasBounds);
        }

        return hasBounds;
    }

    private void EncapsulateWorldBounds(Bounds worldBounds, ref Bounds localBounds, ref bool hasBounds)
    {
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        EncapsulateWorldPoint(new Vector3(min.x, min.y, min.z), ref localBounds, ref hasBounds);
        EncapsulateWorldPoint(new Vector3(min.x, min.y, max.z), ref localBounds, ref hasBounds);
        EncapsulateWorldPoint(new Vector3(min.x, max.y, min.z), ref localBounds, ref hasBounds);
        EncapsulateWorldPoint(new Vector3(min.x, max.y, max.z), ref localBounds, ref hasBounds);
        EncapsulateWorldPoint(new Vector3(max.x, min.y, min.z), ref localBounds, ref hasBounds);
        EncapsulateWorldPoint(new Vector3(max.x, min.y, max.z), ref localBounds, ref hasBounds);
        EncapsulateWorldPoint(new Vector3(max.x, max.y, min.z), ref localBounds, ref hasBounds);
        EncapsulateWorldPoint(new Vector3(max.x, max.y, max.z), ref localBounds, ref hasBounds);
    }

    private void EncapsulateWorldPoint(Vector3 worldPoint, ref Bounds localBounds, ref bool hasBounds)
    {
        Vector3 localPoint = obstacleHost.InverseTransformPoint(worldPoint);
        if (!hasBounds)
        {
            localBounds = new Bounds(localPoint, Vector3.zero);
            hasBounds = true;
            return;
        }

        localBounds.Encapsulate(localPoint);
    }

    private void ApplyObstacleState(bool forceNotify)
    {
        bool shouldBeActive = ShouldObstacleBeActive();
        if (obstacleActive == shouldBeActive && obstacle != null && obstacle.enabled == shouldBeActive)
            return;

        SetObstacleActive(shouldBeActive);
        if (forceNotify)
            ScheduleEnemyReplan();
    }

    private bool ShouldObstacleBeActive()
    {
        if (doorController == null || obstacle == null)
            return false;

        if (doorController.IsMoving && !carveWhileMoving)
            return false;

        return doorController.IsOpen ? carveWhenOpen : carveWhenClosed;
    }

    private void SetObstacleActive(bool active)
    {
        obstacleActive = active;
        if (obstacle == null)
            return;

        obstacle.carving = active;
        obstacle.enabled = active;
    }

    private void ScheduleEnemyReplan()
    {
        if (!replanNearbyEnemiesOnStateChange || replanRadius <= 0f || !Application.isPlaying)
            return;

        pendingEnemyReplan = true;
        enemyReplanTime = Time.time + replanDelay;
    }

    private void FlushPendingEnemyReplan()
    {
        if (!pendingEnemyReplan || Time.time < enemyReplanTime)
            return;

        pendingEnemyReplan = false;
        ForceNearbyEnemiesToRepath();
    }

    private void ForceNearbyEnemiesToRepath()
    {
        EnemyNavigation[] navigations = Resources.FindObjectsOfTypeAll<EnemyNavigation>();
        float radiusSqr = replanRadius * replanRadius;
        Vector3 origin = transform.position;
        var currentScene = gameObject.scene;

        for (int i = 0; i < navigations.Length; i++)
        {
            EnemyNavigation navigation = navigations[i];
            if (navigation == null || !navigation.isActiveAndEnabled || navigation.gameObject.scene != currentScene)
                continue;

            Vector3 offset = Vector3.ProjectOnPlane(navigation.transform.position - origin, Vector3.up);
            if (offset.sqrMagnitude > radiusSqr)
                continue;

            navigation.ForceRepath();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        ResolveReferences();
        Vector3 center = obstacle != null ? obstacle.center : fallbackObstacleCenter;
        Vector3 size = obstacle != null ? obstacle.size : fallbackObstacleSize;
        Transform gizmoTransform = obstacle != null ? obstacle.transform : obstacleHost;
        if (gizmoTransform == null)
            gizmoTransform = transform;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = gizmoTransform.localToWorldMatrix;
        Gizmos.color = obstacleActive
            ? new Color(1f, 0.35f, 0.1f, 0.45f)
            : new Color(0.25f, 0.8f, 1f, 0.25f);
        Gizmos.DrawWireCube(center, size);
        Gizmos.matrix = previousMatrix;
    }
}
