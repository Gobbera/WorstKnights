using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DestructibleSpawnOnDestroyed))]
public class DestructibleSpawnOnDestroyedEditor : Editor
{
    private SerializedProperty destructibleProperty;
    private SerializedProperty spawnEntriesProperty;

    private void OnEnable()
    {
        destructibleProperty = serializedObject.FindProperty("destructible");
        spawnEntriesProperty = serializedObject.FindProperty("spawnEntries");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSetupSection();
        EditorGUILayout.Space();
        DrawSpawnEntriesSection();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSetupSection()
    {
        EditorGUILayout.LabelField("1. Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(destructibleProperty, new GUIContent("Destructible"));
        EditorGUILayout.HelpBox(
            "Este helper escuta o evento de destruicao do Destructible e instancia os prefabs configurados abaixo diretamente na cena.",
            MessageType.None);
    }

    private void DrawSpawnEntriesSection()
    {
        EditorGUILayout.LabelField("2. Spawn Entries", EditorStyles.boldLabel);

        if (spawnEntriesProperty.arraySize == 0)
        {
            EditorGUILayout.HelpBox(
                "Adicione pelo menos uma entrada para definir qual prefab deve nascer quando o objeto for destruido.",
                MessageType.Warning);
        }

        int removeIndex = -1;
        for (int i = 0; i < spawnEntriesProperty.arraySize; i++)
        {
            SerializedProperty entryProperty = spawnEntriesProperty.GetArrayElementAtIndex(i);
            SerializedProperty prefabProperty = entryProperty.FindPropertyRelative("prefab");
            SerializedProperty spawnPointProperty = entryProperty.FindPropertyRelative("spawnPoint");
            SerializedProperty localPositionOffsetProperty = entryProperty.FindPropertyRelative("localPositionOffset");
            SerializedProperty localEulerOffsetProperty = entryProperty.FindPropertyRelative("localEulerOffset");
            SerializedProperty lifetimeProperty = entryProperty.FindPropertyRelative("lifetime");
            SerializedProperty fadeOutDurationProperty = entryProperty.FindPropertyRelative("fadeOutDuration");
            SerializedProperty ignorePlayerCollisionProperty = entryProperty.FindPropertyRelative("ignorePlayerCollision");
            SerializedProperty ignoreEnemyCollisionProperty = entryProperty.FindPropertyRelative("ignoreEnemyCollision");
            SerializedProperty useDebrisCollisionLayerProperty = entryProperty.FindPropertyRelative("useDebrisCollisionLayer");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Entry {i + 1}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                removeIndex = i;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(prefabProperty, new GUIContent("Prefab"));
            EditorGUILayout.PropertyField(spawnPointProperty, new GUIContent("Spawn Point"));
            EditorGUILayout.PropertyField(localPositionOffsetProperty, new GUIContent("Local Position Offset"));
            EditorGUILayout.PropertyField(localEulerOffsetProperty, new GUIContent("Local Euler Offset"));
            EditorGUILayout.PropertyField(lifetimeProperty, new GUIContent("Lifetime"));
            EditorGUILayout.PropertyField(fadeOutDurationProperty, new GUIContent("Fade Out Duration"));
            EditorGUILayout.PropertyField(ignorePlayerCollisionProperty, new GUIContent("Ignore Player Collision"));
            EditorGUILayout.PropertyField(ignoreEnemyCollisionProperty, new GUIContent("Ignore Enemy Collision"));
            EditorGUILayout.PropertyField(useDebrisCollisionLayerProperty, new GUIContent("Use Debris Collision Layer"));

            if (prefabProperty.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "Arraste aqui um prefab do Project para ele ser instanciado quando o destrutivel quebrar.",
                    MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
            spawnEntriesProperty.DeleteArrayElementAtIndex(removeIndex);

        if (GUILayout.Button("Add Spawn Entry"))
            AddSpawnEntry();

        if (GUILayout.Button("Validate Fade Support"))
            DestructibleFadeSupportValidator.ValidateSpawnerFromInspector((DestructibleSpawnOnDestroyed)target);
    }

    private void AddSpawnEntry()
    {
        int newIndex = spawnEntriesProperty.arraySize;
        spawnEntriesProperty.InsertArrayElementAtIndex(newIndex);

        SerializedProperty entryProperty = spawnEntriesProperty.GetArrayElementAtIndex(newIndex);
        entryProperty.FindPropertyRelative("prefab").objectReferenceValue = null;
        entryProperty.FindPropertyRelative("spawnPoint").objectReferenceValue = null;
        entryProperty.FindPropertyRelative("localPositionOffset").vector3Value = Vector3.zero;
        entryProperty.FindPropertyRelative("localEulerOffset").vector3Value = Vector3.zero;
        entryProperty.FindPropertyRelative("lifetime").floatValue = 5f;
        entryProperty.FindPropertyRelative("fadeOutDuration").floatValue = 0.75f;
        entryProperty.FindPropertyRelative("ignorePlayerCollision").boolValue = true;
        entryProperty.FindPropertyRelative("ignoreEnemyCollision").boolValue = false;
        entryProperty.FindPropertyRelative("useDebrisCollisionLayer").boolValue = false;
    }
}

internal static class DestructibleFadeSupportValidator
{
    private const string FadeAlphaProperty = "_FadeAlpha";
    private const string CutoffProperty = "_Cutoff";
    private const string ValidateSelectionMenuPath = "Tools/Destruction/Validate Selected Fade Support";
    private const string ValidateProjectMenuPath = "Tools/Destruction/Validate Project Fade Support";
    private const string ResetProjectFadeAlphaMenuPath = "Tools/Destruction/Reset Project Fade Alpha To Visible";
    private static readonly string[] LegacyColorProperties = { "_Base_Color", "_BaseColor", "_Color" };

    [MenuItem(ValidateSelectionMenuPath)]
    private static void ValidateSelection()
    {
        Object[] selectedObjects = Selection.objects;
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogWarning("[DestructibleFadeSupport] Select a destructible object, prefab, or DestructibleSpawnOnDestroyed component first.");
            return;
        }

        ValidationTotals totals = new ValidationTotals();
        for (int i = 0; i < selectedObjects.Length; i++)
            ValidateSelectionObject(selectedObjects[i], totals);

        LogTotals(totals, "Selection scan");
    }

    [MenuItem(ValidateSelectionMenuPath, true)]
    private static bool ValidateSelectionMenu()
    {
        return Selection.objects != null && Selection.objects.Length > 0;
    }

    [MenuItem(ValidateProjectMenuPath)]
    private static void ValidateProjectPrefabs()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        ValidationTotals totals = new ValidationTotals();

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            DestructibleSpawnOnDestroyed[] spawners = prefab.GetComponentsInChildren<DestructibleSpawnOnDestroyed>(true);
            for (int spawnerIndex = 0; spawnerIndex < spawners.Length; spawnerIndex++)
                ValidateSpawner(spawners[spawnerIndex], totals, path, false);
        }

        LogTotals(totals, "Project prefab scan");
    }

    [MenuItem(ResetProjectFadeAlphaMenuPath)]
    private static void ResetProjectFadeAlphaToVisible()
    {
        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        int supportedCount = 0;
        int changedCount = 0;

        for (int i = 0; i < materialGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || !material.HasProperty(FadeAlphaProperty))
                continue;

            supportedCount++;
            bool changed = false;

            if (!Mathf.Approximately(material.GetFloat(FadeAlphaProperty), 1f))
            {
                material.SetFloat(FadeAlphaProperty, 1f);
                changed = true;
            }

            if (material.HasProperty(CutoffProperty) && !Mathf.Approximately(material.GetFloat(CutoffProperty), 0f))
            {
                material.SetFloat(CutoffProperty, 0f);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(material);
                changedCount++;
            }
        }

        if (changedCount > 0)
            AssetDatabase.SaveAssets();

        Debug.Log(
            $"[DestructibleFadeSupport] Reset {changedCount}/{supportedCount} material(s) to {FadeAlphaProperty}=1 and {CutoffProperty}=0.");
    }

    public static void ValidateSpawnerFromInspector(DestructibleSpawnOnDestroyed spawner)
    {
        ValidationTotals totals = new ValidationTotals();
        ValidateSpawner(spawner, totals, ResolveContextPath(spawner), true);
        LogTotals(totals, "Inspector scan");
    }

    private static void ValidateSelectionObject(Object selectedObject, ValidationTotals totals)
    {
        if (selectedObject == null)
            return;

        DestructibleSpawnOnDestroyed directSpawner = selectedObject as DestructibleSpawnOnDestroyed;
        if (directSpawner != null)
        {
            ValidateSpawner(directSpawner, totals, ResolveContextPath(directSpawner), true);
            return;
        }

        GameObject selectedGameObject = selectedObject as GameObject;
        if (selectedGameObject == null)
            return;

        DestructibleSpawnOnDestroyed[] spawners = selectedGameObject.GetComponentsInChildren<DestructibleSpawnOnDestroyed>(true);
        for (int i = 0; i < spawners.Length; i++)
            ValidateSpawner(spawners[i], totals, ResolveContextPath(spawners[i]), true);
    }

    private static void ValidateSpawner(
        DestructibleSpawnOnDestroyed spawner,
        ValidationTotals totals,
        string sourcePath,
        bool logSupportedEntries)
    {
        if (spawner == null)
            return;

        SerializedObject serializedSpawner = new SerializedObject(spawner);
        SerializedProperty spawnEntries = serializedSpawner.FindProperty("spawnEntries");
        if (spawnEntries == null || !spawnEntries.isArray)
            return;

        for (int i = 0; i < spawnEntries.arraySize; i++)
        {
            SerializedProperty entry = spawnEntries.GetArrayElementAtIndex(i);
            SerializedProperty prefabProperty = entry.FindPropertyRelative("prefab");
            SerializedProperty fadeOutDurationProperty = entry.FindPropertyRelative("fadeOutDuration");
            float fadeOutDuration = fadeOutDurationProperty != null ? fadeOutDurationProperty.floatValue : 0f;
            if (fadeOutDuration <= 0f)
                continue;

            totals.FadeEntries++;
            GameObject prefab = prefabProperty != null ? prefabProperty.objectReferenceValue as GameObject : null;
            string entryLabel = $"{sourcePath} entry {i + 1}";
            if (prefab == null)
            {
                totals.Issues++;
                Debug.LogWarning($"[DestructibleFadeSupport] {entryLabel}: fade is enabled but prefab is missing.", spawner);
                continue;
            }

            ValidatePrefab(entryLabel, prefab, totals, spawner, logSupportedEntries);
        }
    }

    private static void ValidatePrefab(
        string entryLabel,
        GameObject prefab,
        ValidationTotals totals,
        Object context,
        bool logSupportedEntries)
    {
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            totals.Issues++;
            Debug.LogWarning($"[DestructibleFadeSupport] {entryLabel}: {prefab.name} has fade enabled but no renderers.", context);
            return;
        }

        int materialCount = 0;
        int fadeAlphaCount = 0;
        int legacyCount = 0;
        int unsupportedCount = 0;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                materialCount++;
                Material material = materials[materialIndex];
                if (material == null)
                {
                    unsupportedCount++;
                    continue;
                }

                if (material.HasProperty(FadeAlphaProperty))
                {
                    fadeAlphaCount++;
                    continue;
                }

                if (HasLegacyColorFadeProperty(material))
                {
                    legacyCount++;
                    continue;
                }

                unsupportedCount++;
            }
        }

        if (materialCount == 0)
        {
            totals.Issues++;
            Debug.LogWarning($"[DestructibleFadeSupport] {entryLabel}: {prefab.name} has renderers but no materials.", context);
            return;
        }

        if (unsupportedCount > 0)
        {
            totals.Issues++;
            Debug.LogWarning(
                $"[DestructibleFadeSupport] {entryLabel}: {prefab.name} has {unsupportedCount}/{materialCount} material slot(s) with no {FadeAlphaProperty} or legacy color fade property.",
                context);
            return;
        }

        if (fadeAlphaCount == 0)
        {
            totals.LegacyOnlyEntries++;
            Debug.LogWarning(
                $"[DestructibleFadeSupport] {entryLabel}: {prefab.name} can fade only through the legacy transparent fallback. Prefer a shader/material with {FadeAlphaProperty}.",
                context);
            return;
        }

        totals.SupportedEntries++;
        if (legacyCount > 0)
        {
            Debug.Log(
                $"[DestructibleFadeSupport] {entryLabel}: {prefab.name} supports {FadeAlphaProperty}, but {legacyCount}/{materialCount} material slot(s) still use legacy fallback.",
                context);
            return;
        }

        if (logSupportedEntries)
            Debug.Log($"[DestructibleFadeSupport] {entryLabel}: {prefab.name} fully supports {FadeAlphaProperty}.", context);
    }

    private static bool HasLegacyColorFadeProperty(Material material)
    {
        for (int i = 0; i < LegacyColorProperties.Length; i++)
        {
            if (material.HasProperty(LegacyColorProperties[i]))
                return true;
        }

        return false;
    }

    private static string ResolveContextPath(Component component)
    {
        if (component == null)
            return "<null>";

        string assetPath = AssetDatabase.GetAssetPath(component);
        if (!string.IsNullOrEmpty(assetPath))
            return assetPath;

        return GetHierarchyPath(component.transform);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
            return "<null>";

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static void LogTotals(ValidationTotals totals, string label)
    {
        Debug.Log(
            $"[DestructibleFadeSupport] {label}: {totals.FadeEntries} fade entry(s), " +
            $"{totals.SupportedEntries} strong, {totals.LegacyOnlyEntries} legacy-only, {totals.Issues} issue(s).");
    }

    private struct ValidationTotals
    {
        public int FadeEntries;
        public int SupportedEntries;
        public int LegacyOnlyEntries;
        public int Issues;
    }
}
