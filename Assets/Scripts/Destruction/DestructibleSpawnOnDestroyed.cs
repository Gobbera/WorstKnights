using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Destruction/Destructible Spawn On Destroyed")]
public class DestructibleSpawnOnDestroyed : MonoBehaviour
{
    [Serializable]
    public sealed class SpawnEntry
    {
        public GameObject prefab;
        public Transform spawnPoint;
        public Vector3 localPositionOffset = Vector3.zero;
        public Vector3 localEulerOffset = Vector3.zero;
        [Min(0f)] public float lifetime = 5f;
        [Min(0f)] public float fadeOutDuration;
        public bool ignorePlayerCollision = true;
        public bool ignoreEnemyCollision;
        public bool useDebrisCollisionLayer;
    }

    [SerializeField] private DestructibleObjectController destructible;
    [SerializeField] private List<SpawnEntry> spawnEntries = new List<SpawnEntry>();

    internal const float PlayerCollisionRefreshInterval = 0.25f;
    internal const float PlayerCollisionRefreshPadding = 0.25f;

    private const string PlayerTag = "Player";

    private bool spawned;
    private bool subscribed;

    private void Reset()
    {
        if (destructible == null)
            destructible = GetComponent<DestructibleObjectController>();
    }

    private void Awake()
    {
        if (destructible == null)
            destructible = GetComponent<DestructibleObjectController>();
    }

    private void OnValidate()
    {
        if (destructible == null)
            destructible = GetComponent<DestructibleObjectController>();

        if (spawnEntries == null)
            spawnEntries = new List<SpawnEntry>();

        for (int i = 0; i < spawnEntries.Count; i++)
        {
            SpawnEntry spawnEntry = spawnEntries[i];
            if (spawnEntry != null)
            {
                spawnEntry.lifetime = Mathf.Max(0f, spawnEntry.lifetime);
                spawnEntry.fadeOutDuration = Mathf.Max(0f, spawnEntry.fadeOutDuration);
            }
        }
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    [ContextMenu("Spawn Configured Objects")]
    public void SpawnConfiguredObjects()
    {
        if (spawned)
            return;

        for (int i = 0; i < spawnEntries.Count; i++)
        {
            SpawnEntry spawnEntry = spawnEntries[i];
            if (spawnEntry == null || spawnEntry.prefab == null)
                continue;

            Transform spawnAnchor = spawnEntry.spawnPoint != null ? spawnEntry.spawnPoint : transform;
            Vector3 worldPosition = spawnAnchor.TransformPoint(spawnEntry.localPositionOffset);
            Quaternion worldRotation = spawnAnchor.rotation * Quaternion.Euler(spawnEntry.localEulerOffset);
            GameObject spawnedObject = Instantiate(spawnEntry.prefab, worldPosition, worldRotation);
            ConfigureSpawnedObject(spawnedObject, spawnEntry);
        }

        spawned = true;
    }

    private static void ConfigureSpawnedObject(GameObject spawnedObject, SpawnEntry spawnEntry)
    {
        if (spawnedObject == null || spawnEntry == null)
            return;

        if (spawnEntry.useDebrisCollisionLayer)
            DestructibleDebrisCollision.TryApplyDebrisLayer(spawnedObject);

        if (spawnEntry.ignorePlayerCollision || spawnEntry.ignoreEnemyCollision)
            ConfigureActorCollisionIgnore(
                spawnedObject,
                spawnEntry.lifetime,
                spawnEntry.ignorePlayerCollision,
                spawnEntry.ignoreEnemyCollision);

        ConfigureLifetime(spawnedObject, spawnEntry.lifetime, spawnEntry.fadeOutDuration);
    }

    private static void ConfigureLifetime(GameObject spawnedObject, float lifetime, float fadeOutDuration)
    {
        if (spawnedObject == null || lifetime <= 0f)
            return;

        if (fadeOutDuration <= 0f)
        {
            Destroy(spawnedObject, lifetime);
            return;
        }

        SpawnedDestructibleFadeOut fadeOut = spawnedObject.GetComponent<SpawnedDestructibleFadeOut>();
        if (fadeOut == null)
            fadeOut = spawnedObject.AddComponent<SpawnedDestructibleFadeOut>();

        fadeOut.Initialize(spawnedObject, lifetime, fadeOutDuration);
    }

    private static void ConfigureActorCollisionIgnore(
        GameObject spawnedObject,
        float lifetime,
        bool ignorePlayers,
        bool ignoreEnemies)
    {
        IgnoreCollisionWithActors(spawnedObject, ignorePlayers, ignoreEnemies);

        HashSet<GameObject> helperTargets = new HashSet<GameObject>();
        CollectCollisionIgnoreHelperTargets(spawnedObject, helperTargets);

        foreach (GameObject helperTarget in helperTargets)
        {
            AddCollisionIgnoreHelper(helperTarget, lifetime, ignorePlayers, ignoreEnemies);
        }
    }

    private static void CollectCollisionIgnoreHelperTargets(GameObject spawnedObject, HashSet<GameObject> helperTargets)
    {
        if (spawnedObject == null || helperTargets == null)
            return;

        helperTargets.Add(spawnedObject);

        Rigidbody[] rigidbodies = spawnedObject.GetComponentsInChildren<Rigidbody>(true);
        if (rigidbodies != null)
        {
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                Rigidbody rigidbody = rigidbodies[i];
                if (rigidbody != null)
                    helperTargets.Add(rigidbody.gameObject);
            }
        }

        Collider[] colliders = spawnedObject.GetComponentsInChildren<Collider>(true);
        if (colliders == null)
            return;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            GameObject helperTarget = collider.attachedRigidbody != null
                ? collider.attachedRigidbody.gameObject
                : collider.gameObject;
            if (helperTarget != null)
                helperTargets.Add(helperTarget);
        }
    }

    private static void AddCollisionIgnoreHelper(
        GameObject helperTarget,
        float lifetime,
        bool ignorePlayers,
        bool ignoreEnemies)
    {
        if (helperTarget == null)
            return;

        SpawnedDestructibleActorCollisionIgnore collisionIgnore =
            helperTarget.GetComponent<SpawnedDestructibleActorCollisionIgnore>();
        if (collisionIgnore == null)
            collisionIgnore = helperTarget.AddComponent<SpawnedDestructibleActorCollisionIgnore>();

        collisionIgnore.Initialize(helperTarget, lifetime, ignorePlayers, ignoreEnemies);
    }

    internal static void IgnoreCollisionWithActors(GameObject spawnedObject, bool ignorePlayers, bool ignoreEnemies)
    {
        Collider[] spawnedColliders = spawnedObject.GetComponentsInChildren<Collider>(true);
        if (spawnedColliders == null || spawnedColliders.Length == 0)
            return;

        HashSet<Collider> actorColliders = new HashSet<Collider>();
        CollectActorColliders(actorColliders, ignorePlayers, ignoreEnemies);
        if (actorColliders.Count == 0)
            return;

        IgnoreCollisionPairs(spawnedColliders, actorColliders);
    }

    private static void CollectActorColliders(
        HashSet<Collider> actorColliders,
        bool includePlayers,
        bool includeEnemies)
    {
        if (actorColliders == null)
            return;

        if (includePlayers)
        {
            CollectCollidersFromComponents<PlayerHealth>(actorColliders);
            CollectCollidersFromComponents<PlayerMovement>(actorColliders);
            CollectCollidersFromComponents<PlayerSetup>(actorColliders);
            CollectTaggedColliders(PlayerTag, actorColliders);
        }

        if (!includeEnemies)
            return;

        CollectCollidersFromComponents<EnemyHealth>(actorColliders);
        CollectCollidersFromComponents<EnemyMotor>(actorColliders);
        CollectCollidersFromComponents<EnemySetup>(actorColliders);
    }

    private static void CollectCollidersFromComponents<T>(HashSet<Collider> actorColliders) where T : Component
    {
        T[] playerComponents = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude);
        if (playerComponents == null)
            return;

        for (int i = 0; i < playerComponents.Length; i++)
        {
            T playerComponent = playerComponents[i];
            if (playerComponent != null)
                CollectColliders(playerComponent.gameObject, actorColliders);
        }
    }

    private static void CollectTaggedColliders(string tag, HashSet<Collider> actorColliders)
    {
        GameObject[] taggedObjects = FindTaggedObjects(tag);
        if (taggedObjects == null)
            return;

        for (int i = 0; i < taggedObjects.Length; i++)
        {
            GameObject taggedObject = taggedObjects[i];
            if (taggedObject != null)
                CollectColliders(taggedObject, actorColliders);
        }
    }

    private static void CollectColliders(GameObject root, HashSet<Collider> colliders)
    {
        if (root == null || colliders == null)
            return;

        Collider[] rootColliders = root.GetComponentsInChildren<Collider>(true);
        if (rootColliders == null)
            return;

        for (int i = 0; i < rootColliders.Length; i++)
        {
            Collider collider = rootColliders[i];
            if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                colliders.Add(collider);
        }
    }

    private static GameObject[] FindTaggedObjects(string tag)
    {
        try
        {
            return GameObject.FindGameObjectsWithTag(tag);
        }
        catch (UnityException)
        {
            return Array.Empty<GameObject>();
        }
    }

    private static void IgnoreCollisionPairs(Collider[] sourceColliders, HashSet<Collider> targetColliders)
    {
        if (sourceColliders == null || targetColliders == null)
            return;

        for (int sourceIndex = 0; sourceIndex < sourceColliders.Length; sourceIndex++)
        {
            Collider sourceCollider = sourceColliders[sourceIndex];
            if (sourceCollider == null || !sourceCollider.enabled || !sourceCollider.gameObject.activeInHierarchy)
                continue;

            foreach (Collider targetCollider in targetColliders)
            {
                if (targetCollider != null && sourceCollider != targetCollider)
                    Physics.IgnoreCollision(sourceCollider, targetCollider, true);
            }
        }
    }

    private void HandleDestructibleDestroyed(DestructibleObjectController _, DamageInfo __)
    {
        SpawnConfiguredObjects();
    }

    private void Subscribe()
    {
        if (subscribed || destructible == null)
            return;

        destructible.Destroyed += HandleDestructibleDestroyed;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || destructible == null)
            return;

        destructible.Destroyed -= HandleDestructibleDestroyed;
        subscribed = false;
    }
}
