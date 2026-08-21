using UnityEngine;

public sealed class SpawnedDestructibleActorCollisionIgnore : MonoBehaviour
{
    private GameObject targetRoot;
    private float nextRefreshTime;
    private float stopTime = float.PositiveInfinity;
    private bool ignorePlayers;
    private bool ignoreEnemies;

    public void Initialize(GameObject root, float lifetime, bool ignorePlayers, bool ignoreEnemies)
    {
        targetRoot = root != null ? root : gameObject;
        this.ignorePlayers = ignorePlayers;
        this.ignoreEnemies = ignoreEnemies;
        stopTime = lifetime > 0f
            ? Time.time + lifetime + DestructibleSpawnOnDestroyed.PlayerCollisionRefreshPadding
            : float.PositiveInfinity;

        Refresh();
    }

    private void OnEnable()
    {
        if (targetRoot == null)
            targetRoot = gameObject;

        nextRefreshTime = 0f;
    }

    private void FixedUpdate()
    {
        if (Time.time > stopTime)
        {
            enabled = false;
            return;
        }

        if (Time.time < nextRefreshTime)
            return;

        Refresh();
    }

    private void Refresh()
    {
        DestructibleSpawnOnDestroyed.IgnoreCollisionWithActors(
            targetRoot != null ? targetRoot : gameObject,
            ignorePlayers,
            ignoreEnemies);
        nextRefreshTime = Time.time + DestructibleSpawnOnDestroyed.PlayerCollisionRefreshInterval;
    }
}
