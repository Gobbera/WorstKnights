using System;
using System.IO;
using Photon.Pun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public enum SceneryObjectColliderShape
{
    Box = 0,
    Sphere = 1,
    Capsule = 2
}

public static class SceneryObjectAuthoringUtility
{
    public const string SceneryPrefabsFolder = "Assets/Prefabs/Objetcs";
    public const string ModelChildName = "Model";
    public const string CollisionChildName = "Collision";
    public const string SpawnPointName = "Spawn Objects Point";

    private const float MinColliderSize = 0.05f;

    public static bool IsPrefabAsset(GameObject prefabAsset)
    {
        if (prefabAsset == null)
            return false;

        string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
        return !string.IsNullOrWhiteSpace(prefabPath)
            && prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSceneryPrefabAsset(GameObject prefabAsset)
    {
        if (!IsPrefabAsset(prefabAsset))
            return false;

        string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
        return prefabPath.StartsWith(SceneryPrefabsFolder, StringComparison.OrdinalIgnoreCase);
    }

    public static GameObject ResolvePrefabAsset(GameObject targetObject)
    {
        if (targetObject == null)
            return null;

        if (IsPrefabAsset(targetObject))
            return targetObject;

        PrefabStage prefabStage = PrefabStageUtility.GetPrefabStage(targetObject);
        if (prefabStage != null
            && prefabStage.prefabContentsRoot != null
            && targetObject.transform.IsChildOf(prefabStage.prefabContentsRoot.transform))
        {
            GameObject stagedPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabStage.assetPath);
            if (IsPrefabAsset(stagedPrefabAsset))
                return stagedPrefabAsset;
        }

        GameObject nearestPrefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(targetObject);
        if (nearestPrefabRoot != null)
        {
            GameObject sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(nearestPrefabRoot);
            if (IsPrefabAsset(sourcePrefab))
                return sourcePrefab;
        }

        GameObject directSource = PrefabUtility.GetCorrespondingObjectFromSource(targetObject);
        return IsPrefabAsset(directSource) ? directSource : null;
    }

    public static string SanitizeAssetName(string value, string fallback)
    {
        string source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            source = source.Replace(invalidChar, '_');

        return string.IsNullOrWhiteSpace(source) ? fallback : source;
    }

    public static string CreateUniquePrefabPath(string objectName)
    {
        EnsureFolder(SceneryPrefabsFolder);
        string safeName = SanitizeAssetName(objectName, "New Scenery Object");
        return AssetDatabase.GenerateUniqueAssetPath($"{SceneryPrefabsFolder}/{safeName}.prefab");
    }

    public static GameObject CreateDraftRoot(string objectName, GameObject modelSource)
    {
        GameObject root = new GameObject(SanitizeAssetName(objectName, "New Scenery Object"));
        root.hideFlags = HideFlags.HideAndDontSave;
        ConfigureWorldObject(EnsureWorldObject(root), root.name);
        ReplaceModel(root, modelSource);
        SetHideFlagsRecursively(root, HideFlags.HideAndDontSave);
        return root;
    }

    public static void ReplaceModel(GameObject root, GameObject modelSource)
    {
        if (root == null)
            return;

        Transform modelRoot = EnsureDirectChild(root.transform, ModelChildName);
        ClearChildren(modelRoot);

        if (modelSource == null)
            return;

        GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelSource) as GameObject;
        if (modelInstance == null)
            modelInstance = UnityEngine.Object.Instantiate(modelSource);

        modelInstance.name = modelSource.name;
        modelInstance.transform.SetParent(modelRoot, false);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.identity;
        modelInstance.transform.localScale = Vector3.one;
        EditorUtility.SetDirty(modelInstance);
    }

    public static GameObject ResolveCurrentModelSource(GameObject root)
    {
        Transform modelRoot = FindDirectChildByName(root != null ? root.transform : null, ModelChildName);
        if (modelRoot == null || modelRoot.childCount == 0)
            return null;

        GameObject modelInstance = modelRoot.GetChild(0).gameObject;
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(modelInstance);
        return source != null ? source : modelInstance;
    }

    public static bool EnsureCollisionFromRenderers(GameObject root, SceneryObjectColliderShape colliderShape)
    {
        if (root == null || !TryCalculateLocalRendererBounds(root.transform, out Bounds rootLocalBounds))
            return false;

        Transform collisionHost = EnsureDirectChild(root.transform, CollisionChildName);
        collisionHost.localPosition = rootLocalBounds.center;
        collisionHost.localRotation = Quaternion.identity;
        collisionHost.localScale = Vector3.one;
        collisionHost.gameObject.layer = root.layer;

        Collider collider = ApplyCollider(collisionHost.gameObject, colliderShape, rootLocalBounds);
        collider.isTrigger = false;
        collider.enabled = true;

        EditorUtility.SetDirty(collisionHost.gameObject);
        EditorUtility.SetDirty(collider);
        EditorUtility.SetDirty(root);
        return true;
    }

    public static int CountColliders(GameObject root, bool includeTriggers)
    {
        if (root == null)
            return 0;

        int count = 0;
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null && (includeTriggers || !collider.isTrigger))
                count++;
        }

        return count;
    }

    public static void ApplyDestructibleSetup(GameObject root, bool destructibleEnabled, bool spawnOnDestroyed)
    {
        if (root == null)
            return;

        if (!destructibleEnabled)
        {
            RemoveComponent<DestructibleSpawnOnDestroyed>(root);
            RemoveComponent<DestructibleReactionSignalBridge>(root);
            RemoveComponent<DestructibleObjectController>(root);
            return;
        }

        PhotonView photonView = EnsureComponent<PhotonView>(root);
        DestructibleObjectController destructible = EnsureComponent<DestructibleObjectController>(root);
        ConfigureDestructible(root, destructible, photonView);

        if (spawnOnDestroyed)
        {
            DestructibleSpawnOnDestroyed spawn = EnsureComponent<DestructibleSpawnOnDestroyed>(root);
            ConfigureSpawnOnDestroyed(root, spawn, destructible);
        }
        else
        {
            RemoveComponent<DestructibleSpawnOnDestroyed>(root);
        }

        EnsureReactionSetup(root, destructible);
    }

    public static void PrepareForPrefabSave(GameObject root, SceneryObjectColliderShape colliderShape, bool regenerateCollision)
    {
        if (root == null)
            return;

        ConfigureWorldObject(EnsureWorldObject(root), root.name);

        if (regenerateCollision)
            EnsureCollisionFromRenderers(root, colliderShape);

        DestructibleObjectController destructible = root.GetComponent<DestructibleObjectController>();
        if (destructible != null)
        {
            PhotonView photonView = root.GetComponent<PhotonView>();
            ConfigureDestructible(root, destructible, photonView);
            ClearNetworkSceneId(destructible);

            DestructibleSpawnOnDestroyed spawn = root.GetComponent<DestructibleSpawnOnDestroyed>();
            if (spawn != null)
                ConfigureSpawnOnDestroyed(root, spawn, destructible);

            EnsureReactionSetup(root, destructible);
        }

        SetHideFlagsRecursively(root, HideFlags.None);
        EditorUtility.SetDirty(root);
    }

    public static WorldObject EnsureWorldObject(GameObject root)
    {
        return root != null ? EnsureComponent<WorldObject>(root) : null;
    }

    public static Transform EnsureSpawnPoint(GameObject root)
    {
        return root != null ? EnsureDirectChild(root.transform, SpawnPointName) : null;
    }

    public static void SetHideFlagsRecursively(GameObject root, HideFlags hideFlags)
    {
        if (root == null)
            return;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null)
                transforms[i].gameObject.hideFlags = hideFlags;
        }
    }

    private static void ConfigureDestructible(GameObject root, DestructibleObjectController destructible, PhotonView photonView)
    {
        if (root == null || destructible == null)
            return;

        SerializedObject serializedDestructible = new SerializedObject(destructible);
        SerializedProperty nameProperty = serializedDestructible.FindProperty("destructibleName");
        if (nameProperty != null && IsDefaultDestructibleName(nameProperty.stringValue))
            nameProperty.stringValue = root.name;

        SerializedProperty destructionTargetProperty = serializedDestructible.FindProperty("destructionTarget");
        if (destructionTargetProperty != null && destructionTargetProperty.objectReferenceValue == null)
            destructionTargetProperty.objectReferenceValue = root;

        SerializedProperty photonViewProperty = serializedDestructible.FindProperty("photonView");
        if (photonViewProperty != null)
            photonViewProperty.objectReferenceValue = photonView;

        serializedDestructible.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(destructible);
    }

    private static void ConfigureWorldObject(WorldObject worldObject, string objectName)
    {
        if (worldObject == null)
            return;

        SerializedObject serializedWorldObject = new SerializedObject(worldObject);
        SerializedProperty objectNameProperty = serializedWorldObject.FindProperty("objectName");
        if (objectNameProperty != null)
            objectNameProperty.stringValue = string.IsNullOrWhiteSpace(objectName) ? worldObject.gameObject.name : objectName;

        serializedWorldObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(worldObject);
    }

    private static void ConfigureSpawnOnDestroyed(
        GameObject root,
        DestructibleSpawnOnDestroyed spawn,
        DestructibleObjectController destructible)
    {
        if (root == null || spawn == null)
            return;

        Transform defaultSpawnPoint = EnsureSpawnPoint(root);

        SerializedObject serializedSpawn = new SerializedObject(spawn);
        SerializedProperty destructibleProperty = serializedSpawn.FindProperty("destructible");
        if (destructibleProperty != null)
            destructibleProperty.objectReferenceValue = destructible;

        SerializedProperty spawnEntriesProperty = serializedSpawn.FindProperty("spawnEntries");
        if (spawnEntriesProperty != null)
        {
            if (spawnEntriesProperty.arraySize == 0)
                AddDefaultSpawnEntry(spawnEntriesProperty, defaultSpawnPoint);

            for (int i = 0; i < spawnEntriesProperty.arraySize; i++)
            {
                SerializedProperty entryProperty = spawnEntriesProperty.GetArrayElementAtIndex(i);
                SerializedProperty spawnPointProperty = entryProperty.FindPropertyRelative("spawnPoint");
                Transform configuredSpawnPoint = spawnPointProperty != null
                    ? spawnPointProperty.objectReferenceValue as Transform
                    : null;

                if (spawnPointProperty != null && !IsTransformInsideRoot(configuredSpawnPoint, root.transform))
                    spawnPointProperty.objectReferenceValue = defaultSpawnPoint;
            }
        }

        serializedSpawn.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(spawn);
    }

    private static void EnsureReactionSetup(GameObject root, DestructibleObjectController destructible)
    {
        if (root == null || destructible == null)
            return;

        ReactionSignalReceiver receiver = EnsureComponent<ReactionSignalReceiver>(root);
        ReactionSignalEmitter emitter = EnsureComponent<ReactionSignalEmitter>(root);
        DestructibleReactionSignalBridge bridge = EnsureComponent<DestructibleReactionSignalBridge>(root);

        ConfigureReactionEmitter(emitter, receiver);
        ConfigureDestructibleBridge(bridge, destructible, emitter, receiver);
        AddSignalEntryIfMissing(receiver, "Damaged");
        AddSignalEntryIfMissing(receiver, "Destroyed");
    }

    private static void ConfigureReactionEmitter(ReactionSignalEmitter emitter, ReactionSignalReceiver receiver)
    {
        if (emitter == null)
            return;

        SerializedObject serializedEmitter = new SerializedObject(emitter);
        SerializedProperty receiverProperty = serializedEmitter.FindProperty("signalReceiver");
        if (receiverProperty != null)
            receiverProperty.objectReferenceValue = receiver;

        serializedEmitter.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(emitter);
    }

    private static void ConfigureDestructibleBridge(
        DestructibleReactionSignalBridge bridge,
        DestructibleObjectController destructible,
        ReactionSignalEmitter emitter,
        ReactionSignalReceiver receiver)
    {
        if (bridge == null)
            return;

        SerializedObject serializedBridge = new SerializedObject(bridge);
        SetObjectReference(serializedBridge, "destructible", destructible);
        SetObjectReference(serializedBridge, "signalEmitter", emitter);
        SetObjectReference(serializedBridge, "signalReceiver", receiver);
        serializedBridge.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bridge);
    }

    private static void AddSignalEntryIfMissing(ReactionSignalReceiver receiver, string signalId)
    {
        if (receiver == null || string.IsNullOrWhiteSpace(signalId))
            return;

        SerializedObject serializedReceiver = new SerializedObject(receiver);
        SerializedProperty signalEntriesProperty = serializedReceiver.FindProperty("signalEntries");
        if (signalEntriesProperty == null)
            return;

        for (int i = 0; i < signalEntriesProperty.arraySize; i++)
        {
            SerializedProperty entryProperty = signalEntriesProperty.GetArrayElementAtIndex(i);
            string currentSignalId = entryProperty.FindPropertyRelative("signalId").stringValue;
            if (string.Equals(currentSignalId?.Trim(), signalId.Trim(), StringComparison.OrdinalIgnoreCase))
                return;
        }

        int newIndex = signalEntriesProperty.arraySize;
        signalEntriesProperty.InsertArrayElementAtIndex(newIndex);

        SerializedProperty newEntryProperty = signalEntriesProperty.GetArrayElementAtIndex(newIndex);
        newEntryProperty.FindPropertyRelative("signalId").stringValue = signalId;
        newEntryProperty.FindPropertyRelative("feedbackOrigin").objectReferenceValue = null;
        newEntryProperty.FindPropertyRelative("audioCue").objectReferenceValue = null;
        newEntryProperty.FindPropertyRelative("effectPrefab").objectReferenceValue = null;
        newEntryProperty.FindPropertyRelative("effectLifetime").floatValue = 5f;

        SerializedProperty callsProperty = newEntryProperty
            .FindPropertyRelative("onSignalReceived")
            ?.FindPropertyRelative("m_PersistentCalls.m_Calls");
        if (callsProperty != null)
            callsProperty.ClearArray();

        serializedReceiver.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(receiver);
    }

    private static void AddDefaultSpawnEntry(SerializedProperty spawnEntriesProperty, Transform defaultSpawnPoint)
    {
        int newIndex = spawnEntriesProperty.arraySize;
        spawnEntriesProperty.InsertArrayElementAtIndex(newIndex);

        SerializedProperty entryProperty = spawnEntriesProperty.GetArrayElementAtIndex(newIndex);
        entryProperty.FindPropertyRelative("prefab").objectReferenceValue = null;
        entryProperty.FindPropertyRelative("spawnPoint").objectReferenceValue = defaultSpawnPoint;
        entryProperty.FindPropertyRelative("localPositionOffset").vector3Value = Vector3.zero;
        entryProperty.FindPropertyRelative("localEulerOffset").vector3Value = Vector3.zero;
    }

    private static void ClearNetworkSceneId(DestructibleObjectController destructible)
    {
        SerializedObject serializedDestructible = new SerializedObject(destructible);
        SerializedProperty networkSceneIdProperty = serializedDestructible.FindProperty("networkSceneId");
        if (networkSceneIdProperty != null)
            networkSceneIdProperty.stringValue = string.Empty;

        serializedDestructible.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static Collider ApplyCollider(GameObject hostObject, SceneryObjectColliderShape colliderShape, Bounds rootLocalBounds)
    {
        switch (colliderShape)
        {
            case SceneryObjectColliderShape.Sphere:
            {
                SphereCollider sphereCollider = EnsureSingleCollider<SphereCollider>(hostObject);
                sphereCollider.center = Vector3.zero;
                sphereCollider.radius = Mathf.Max(
                    MinColliderSize,
                    Mathf.Max(rootLocalBounds.extents.x, rootLocalBounds.extents.y, rootLocalBounds.extents.z));
                return sphereCollider;
            }
            case SceneryObjectColliderShape.Capsule:
            {
                CapsuleCollider capsuleCollider = EnsureSingleCollider<CapsuleCollider>(hostObject);
                capsuleCollider.center = Vector3.zero;
                capsuleCollider.direction = 1;
                capsuleCollider.radius = Mathf.Max(MinColliderSize, Mathf.Max(rootLocalBounds.extents.x, rootLocalBounds.extents.z));
                capsuleCollider.height = Mathf.Max(capsuleCollider.radius * 2f, Mathf.Max(MinColliderSize, rootLocalBounds.size.y));
                return capsuleCollider;
            }
            default:
            {
                BoxCollider boxCollider = EnsureSingleCollider<BoxCollider>(hostObject);
                boxCollider.center = Vector3.zero;
                boxCollider.size = EnsureMinimumSize(rootLocalBounds.size);
                return boxCollider;
            }
        }
    }

    private static T EnsureComponent<T>(GameObject root) where T : Component
    {
        T component = root.GetComponent<T>();
        if (component == null)
            component = root.AddComponent<T>();

        EditorUtility.SetDirty(component);
        return component;
    }

    private static void RemoveComponent<T>(GameObject root) where T : Component
    {
        T component = root != null ? root.GetComponent<T>() : null;
        if (component != null)
            UnityEngine.Object.DestroyImmediate(component);
    }

    private static T EnsureSingleCollider<T>(GameObject hostObject) where T : Collider
    {
        Collider[] colliders = hostObject.GetComponents<Collider>();
        T matchingCollider = null;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider is T typedCollider && matchingCollider == null)
            {
                matchingCollider = typedCollider;
                continue;
            }

            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }

        if (matchingCollider != null)
            return matchingCollider;

        return hostObject.AddComponent<T>();
    }

    private static Transform EnsureDirectChild(Transform root, string childName)
    {
        Transform existingChild = FindDirectChildByName(root, childName);
        if (existingChild != null)
            return existingChild;

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(root, false);
        childObject.transform.localPosition = Vector3.zero;
        childObject.transform.localRotation = Quaternion.identity;
        childObject.transform.localScale = Vector3.one;
        EditorUtility.SetDirty(childObject);
        return childObject.transform;
    }

    private static Transform FindDirectChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != null && string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;
        }

        return null;
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null)
            return;

        for (int i = parent.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    private static bool TryCalculateLocalRendererBounds(Transform referenceTransform, out Bounds localBounds)
    {
        localBounds = default;
        if (referenceTransform == null)
            return false;

        Renderer[] renderers = referenceTransform.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return false;

        bool hasBounds = false;
        Bounds worldBounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rendererComponent = renderers[i];
            if (rendererComponent == null)
                continue;

            if (!hasBounds)
            {
                worldBounds = rendererComponent.bounds;
                hasBounds = true;
                continue;
            }

            worldBounds.Encapsulate(rendererComponent.bounds);
        }

        if (!hasBounds)
            return false;

        Vector3[] corners =
        {
            new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.min.z),
            new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.max.z),
            new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.min.z),
            new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.max.z),
            new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.min.z),
            new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.max.z),
            new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.min.z),
            new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.max.z)
        };

        Vector3 firstPoint = referenceTransform.InverseTransformPoint(corners[0]);
        localBounds = new Bounds(firstPoint, Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
            localBounds.Encapsulate(referenceTransform.InverseTransformPoint(corners[i]));

        return true;
    }

    private static Vector3 EnsureMinimumSize(Vector3 size)
    {
        return new Vector3(
            Mathf.Max(MinColliderSize, Mathf.Abs(size.x)),
            Mathf.Max(MinColliderSize, Mathf.Abs(size.y)),
            Mathf.Max(MinColliderSize, Mathf.Abs(size.z)));
    }

    private static bool IsDefaultDestructibleName(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value.Trim(), "Destructible", StringComparison.Ordinal);
    }

    private static bool IsTransformInsideRoot(Transform candidate, Transform root)
    {
        return candidate == null || root == null || candidate == root || candidate.IsChildOf(root);
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string currentPath = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = $"{currentPath}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
                AssetDatabase.CreateFolder(currentPath, parts[i]);

            currentPath = nextPath;
        }
    }
}
