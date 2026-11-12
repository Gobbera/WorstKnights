using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class EnemyNavMeshAgentAuthoringUtility
{
    private const string EnemyPrefabRoot = "Assets/Prefabs/Enemies";
    private const string MenuPath = "Tools/Enemy/Navigation/Ensure Authored NavMesh Agents";

    [MenuItem(MenuPath)]
    public static void EnsureAuthoredNavMeshAgents()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { EnemyPrefabRoot });
        int processedCount = 0;
        int changedCount = 0;

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            if (string.IsNullOrWhiteSpace(prefabPath))
                continue;

            processedCount++;
            if (TryEnsurePrefabAgent(prefabPath))
                changedCount++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Enemy NavMeshAgent authoring checked {processedCount} prefabs and updated {changedCount}.");
    }

    private static bool TryEnsurePrefabAgent(string prefabPath)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        bool changed = false;

        try
        {
            EnemyNavigation[] navigations = prefabRoot.GetComponentsInChildren<EnemyNavigation>(true);
            for (int i = 0; i < navigations.Length; i++)
            {
                if (TryEnsureNavigationAgent(navigations[i]))
                    changed = true;
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        return changed;
    }

    private static bool TryEnsureNavigationAgent(EnemyNavigation navigation)
    {
        if (navigation == null)
            return false;

        GameObject owner = navigation.gameObject;
        NavMeshAgent agent = owner.GetComponent<NavMeshAgent>();
        bool createdAgent = false;
        if (agent == null)
        {
            agent = owner.AddComponent<NavMeshAgent>();
            createdAgent = true;
            ConfigureNewAgent(agent, owner);
        }

        SerializedObject serializedNavigation = new SerializedObject(navigation);
        bool changed = createdAgent;
        changed |= SetObjectReference(serializedNavigation, "navMeshAgent", agent);
        changed |= SetBool(serializedNavigation, "createAgentAtRuntime", false);
        changed |= SetBool(serializedNavigation, "configureAgentFromCapsule", false);
        changed |= SetBool(serializedNavigation, "forceManualAgentTransformSync", true);
        changed |= SetBool(serializedNavigation, "applyRuntimeAvoidanceSettings", false);

        if (changed)
            serializedNavigation.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    private static void ConfigureNewAgent(NavMeshAgent agent, GameObject owner)
    {
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.updateUpAxis = true;
        agent.autoRepath = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agent.avoidancePriority = 50;

        CapsuleCollider capsule = owner.GetComponent<CapsuleCollider>();
        if (capsule == null)
            return;

        Bounds capsuleBounds = capsule.bounds;
        agent.radius = Mathf.Max(0.01f, Mathf.Max(capsuleBounds.extents.x, capsuleBounds.extents.z));
        agent.height = Mathf.Max(0.1f, capsuleBounds.size.y);
        agent.baseOffset = Mathf.Max(0f, capsuleBounds.min.y - owner.transform.position.y);
    }

    private static bool SetObjectReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue == value)
            return false;

        property.objectReferenceValue = value;
        return true;
    }

    private static bool SetBool(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.boolValue == value)
            return false;

        property.boolValue = value;
        return true;
    }
}
