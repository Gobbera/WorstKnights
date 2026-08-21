using System.IO;
using UnityEditor;
using UnityEngine;

public class SceneryObjectAuthoringToolWindow : EditorWindow
{
    private enum ToolMode
    {
        NewObject,
        EditObject
    }

    private ToolMode mode;
    private Vector2 scrollPosition;

    private string newObjectName = "New Scenery Object";
    private GameObject newModelSource;
    private bool newIsDestructible = true;
    private bool newSpawnOnDestroyed;
    private SceneryObjectColliderShape newColliderShape = SceneryObjectColliderShape.Box;
    private GameObject newDraftRoot;
    private GameObject newDraftModelSource;

    private GameObject editPrefab;
    private string editPrefabPath;
    private string editObjectName = "Scenery Object";
    private GameObject editReplacementModelSource;
    private bool editIsDestructible;
    private bool editSpawnOnDestroyed;
    private SceneryObjectColliderShape editColliderShape = SceneryObjectColliderShape.Box;
    private GameObject editDraftRoot;

    private UnityEditor.Editor receiverEditor;
    private Component receiverEditorTarget;

    [MenuItem("Tools/World/Object Authoring Tool")]
    public static void OpenWindow()
    {
        SceneryObjectAuthoringToolWindow window = GetWindow<SceneryObjectAuthoringToolWindow>("Object Authoring");
        window.EnterNewObjectMode();
        window.Show();
    }

    public static void OpenForTarget(GameObject targetObject)
    {
        SceneryObjectAuthoringToolWindow window = GetWindow<SceneryObjectAuthoringToolWindow>("Object Authoring");
        window.EnterEditObjectMode(targetObject);
        window.Show();
    }

    private void OnDisable()
    {
        DestroyCachedEditors();
        DestroyNewDraft();
        UnloadEditDraft();
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.Space();

        if (mode == ToolMode.NewObject)
            DrawNewObjectMode();
        else
            DrawEditObjectMode();

        EditorGUILayout.EndScrollView();
    }

    private void DrawNewObjectMode()
    {
        EnsureNewDraft();

        EditorGUILayout.LabelField("Novo Objeto de Cenario", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"Cria um prefab de cenario em {SceneryObjectAuthoringUtility.SceneryPrefabsFolder}, com filho Model, Collision automatico e setup destrutivel opcional.",
            MessageType.None);

        EditorGUI.BeginChangeCheck();
        newObjectName = EditorGUILayout.TextField("Object Name", newObjectName);
        GameObject nextModelSource = (GameObject)EditorGUILayout.ObjectField("Modelo", newModelSource, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            if (newDraftRoot != null)
                newDraftRoot.name = SceneryObjectAuthoringUtility.SanitizeAssetName(newObjectName, "New Scenery Object");

            if (nextModelSource != newModelSource)
            {
                newModelSource = nextModelSource;
                RebuildNewDraftModel();
            }
        }

        DrawCollisionSection(newDraftRoot, ref newColliderShape);
        DrawDestructionSection(
            newDraftRoot,
            ref newIsDestructible,
            ref newSpawnOnDestroyed);

        EditorGUILayout.Space();
        bool canCreate = !string.IsNullOrWhiteSpace(newObjectName) && newModelSource != null;
        using (new EditorGUI.DisabledScope(!canCreate))
        {
            if (GUILayout.Button("Criar Objeto de Cenario"))
                CreateNewObjectPrefab();
        }

        if (!canCreate)
            EditorGUILayout.HelpBox("Informe um nome e arraste um modelo FBX/prefab para criar o objeto.", MessageType.Warning);
    }

    private void DrawEditObjectMode()
    {
        EditorGUILayout.LabelField("Editar Objeto de Cenario", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Edicao aberta pelo WorldObject de um prefab asset ou instancia de prefab. As alteracoes so sao gravadas quando voce clicar em Salvar Edicoes.",
            MessageType.None);

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Prefab", editPrefab, typeof(GameObject), false);

        if (editPrefab == null)
        {
            EditorGUILayout.HelpBox("Nenhum prefab carregado. Abra esta edicao pelo botao do componente WorldObject no objeto.", MessageType.Warning);
            return;
        }

        if (!SceneryObjectAuthoringUtility.IsPrefabAsset(editPrefab))
        {
            EditorGUILayout.HelpBox("O alvo precisa ser um prefab asset ou uma instancia de prefab.", MessageType.Warning);
            return;
        }

        if (!SceneryObjectAuthoringUtility.IsSceneryPrefabAsset(editPrefab))
        {
            EditorGUILayout.HelpBox(
                $"Este prefab esta fora de {SceneryObjectAuthoringUtility.SceneryPrefabsFolder}. A ferramenta ainda pode editar, mas novos objetos de cenario sao criados nessa pasta.",
                MessageType.Info);
        }

        EnsureEditDraftLoaded();
        if (editDraftRoot == null)
            return;

        EditorGUI.BeginChangeCheck();
        editObjectName = EditorGUILayout.TextField("Object Name", editObjectName);
        if (EditorGUI.EndChangeCheck())
            editDraftRoot.name = SceneryObjectAuthoringUtility.SanitizeAssetName(editObjectName, editPrefab.name);

        DrawModelReplacementSection();
        DrawCollisionSection(editDraftRoot, ref editColliderShape);
        DrawDestructionSection(
            editDraftRoot,
            ref editIsDestructible,
            ref editSpawnOnDestroyed);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Confirmacao", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Salvar Edicoes"))
            SaveEditDraft();

        if (GUILayout.Button("Recarregar Prefab"))
            ReloadEditDraft();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawModelReplacementSection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Modelo", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            GameObject currentModelSource = SceneryObjectAuthoringUtility.ResolveCurrentModelSource(editDraftRoot);
            EditorGUILayout.ObjectField("Modelo Atual", currentModelSource, typeof(GameObject), false);
        }

        editReplacementModelSource = (GameObject)EditorGUILayout.ObjectField(
            "Substituir Modelo",
            editReplacementModelSource,
            typeof(GameObject),
            false);

        using (new EditorGUI.DisabledScope(editReplacementModelSource == null))
        {
            if (GUILayout.Button("Aplicar Modelo"))
            {
                SceneryObjectAuthoringUtility.ReplaceModel(editDraftRoot, editReplacementModelSource);
                SceneryObjectAuthoringUtility.EnsureCollisionFromRenderers(editDraftRoot, editColliderShape);
                SceneryObjectAuthoringUtility.SetHideFlagsRecursively(editDraftRoot, HideFlags.HideAndDontSave);
                editReplacementModelSource = null;
                Repaint();
            }
        }
    }

    private void DrawCollisionSection(
        GameObject draftRoot,
        ref SceneryObjectColliderShape colliderShape)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Collision", EditorStyles.boldLabel);
        colliderShape = (SceneryObjectColliderShape)EditorGUILayout.EnumPopup("Collider Shape", colliderShape);

        if (GUILayout.Button("Gerar Collider Automatico"))
        {
            if (!SceneryObjectAuthoringUtility.EnsureCollisionFromRenderers(draftRoot, colliderShape))
                Debug.LogWarning("[SceneryObjectAuthoringTool] Nao foi possivel calcular bounds do visual para gerar o collider.", draftRoot);
        }

    }

    private void DrawDestructionSection(
        GameObject draftRoot,
        ref bool isDestructible,
        ref bool spawnOnDestroyed)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Destruction", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        bool nextIsDestructible = EditorGUILayout.Toggle("Destrutivel", isDestructible);
        if (EditorGUI.EndChangeCheck())
        {
            isDestructible = nextIsDestructible;
            SceneryObjectAuthoringUtility.ApplyDestructibleSetup(draftRoot, isDestructible, spawnOnDestroyed);
            DestroyCachedEditors();
        }

        if (!isDestructible)
        {
            EditorGUILayout.HelpBox("Sem DestructibleObjectController, este objeto sera apenas cenario com collider.", MessageType.None);
            return;
        }

        SceneryObjectAuthoringUtility.ApplyDestructibleSetup(draftRoot, isDestructible, spawnOnDestroyed);

        DestructibleObjectController destructible = draftRoot.GetComponent<DestructibleObjectController>();
        DrawDestructibleAuthoringFields(destructible);

        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        bool nextSpawnOnDestroyed = EditorGUILayout.Toggle("Spawn objects when destroyed", spawnOnDestroyed);
        if (EditorGUI.EndChangeCheck())
        {
            spawnOnDestroyed = nextSpawnOnDestroyed;
            SceneryObjectAuthoringUtility.ApplyDestructibleSetup(draftRoot, isDestructible, spawnOnDestroyed);
        }

        if (spawnOnDestroyed)
        {
            DestructibleSpawnOnDestroyed spawn = draftRoot.GetComponent<DestructibleSpawnOnDestroyed>();
            DrawSpawnEntriesAuthoringFields(spawn);
        }

        ReactionSignalReceiver receiver = draftRoot.GetComponent<ReactionSignalReceiver>();
        if (receiver != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Reactions", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "O setup segue o Barrel: Receiver, Emitter e Bridge sao criados para os sinais Damaged e Destroyed.",
                MessageType.None);
            DrawComponentInspector(receiver, ref receiverEditor, ref receiverEditorTarget);
        }
    }

    private void DrawDestructibleAuthoringFields(DestructibleObjectController destructible)
    {
        if (destructible == null)
            return;

        SerializedObject serializedDestructible = new SerializedObject(destructible);
        serializedDestructible.Update();

        SerializedProperty maxHealthProperty = serializedDestructible.FindProperty("maxHealth");
        if (maxHealthProperty != null)
        {
            EditorGUI.BeginChangeCheck();
            float nextMaxHealth = Mathf.Max(1f, EditorGUILayout.FloatField("Max Health", Mathf.Max(1f, maxHealthProperty.floatValue)));
            if (EditorGUI.EndChangeCheck())
            {
                maxHealthProperty.floatValue = nextMaxHealth;
                serializedDestructible.ApplyModifiedProperties();
                EditorUtility.SetDirty(destructible);
                return;
            }
        }

        serializedDestructible.ApplyModifiedPropertiesWithoutUndo();
    }

    private void DrawSpawnEntriesAuthoringFields(DestructibleSpawnOnDestroyed spawn)
    {
        if (spawn == null)
            return;

        SerializedObject serializedSpawn = new SerializedObject(spawn);
        serializedSpawn.Update();

        SerializedProperty spawnEntriesProperty = serializedSpawn.FindProperty("spawnEntries");
        if (spawnEntriesProperty == null)
        {
            serializedSpawn.ApplyModifiedPropertiesWithoutUndo();
            return;
        }

        EditorGUILayout.LabelField("Spawn Entries", EditorStyles.boldLabel);

        if (spawnEntriesProperty.arraySize == 0)
            EditorGUILayout.HelpBox("Adicione pelo menos uma entrada para definir qual prefab deve nascer quando o objeto for destruido.", MessageType.Warning);

        int removeIndex = -1;
        for (int i = 0; i < spawnEntriesProperty.arraySize; i++)
        {
            SerializedProperty entryProperty = spawnEntriesProperty.GetArrayElementAtIndex(i);
            SerializedProperty prefabProperty = entryProperty.FindPropertyRelative("prefab");
            SerializedProperty spawnPointProperty = entryProperty.FindPropertyRelative("spawnPoint");
            SerializedProperty localPositionOffsetProperty = entryProperty.FindPropertyRelative("localPositionOffset");
            SerializedProperty localEulerOffsetProperty = entryProperty.FindPropertyRelative("localEulerOffset");

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

            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
            spawnEntriesProperty.DeleteArrayElementAtIndex(removeIndex);

        if (GUILayout.Button("Add Spawn Entry"))
        {
            int newIndex = spawnEntriesProperty.arraySize;
            spawnEntriesProperty.InsertArrayElementAtIndex(newIndex);

            SerializedProperty entryProperty = spawnEntriesProperty.GetArrayElementAtIndex(newIndex);
            entryProperty.FindPropertyRelative("prefab").objectReferenceValue = null;
            entryProperty.FindPropertyRelative("spawnPoint").objectReferenceValue = null;
            entryProperty.FindPropertyRelative("localPositionOffset").vector3Value = Vector3.zero;
            entryProperty.FindPropertyRelative("localEulerOffset").vector3Value = Vector3.zero;
        }

        serializedSpawn.ApplyModifiedPropertiesWithoutUndo();
    }

    private void EnsureNewDraft()
    {
        if (newDraftRoot == null)
        {
            newDraftRoot = SceneryObjectAuthoringUtility.CreateDraftRoot(newObjectName, newModelSource);
            newDraftModelSource = newModelSource;
            SceneryObjectAuthoringUtility.ApplyDestructibleSetup(newDraftRoot, newIsDestructible, newSpawnOnDestroyed);
            return;
        }

        if (newDraftModelSource != newModelSource)
            RebuildNewDraftModel();
    }

    private void RebuildNewDraftModel()
    {
        if (newDraftRoot == null)
            return;

        SceneryObjectAuthoringUtility.ReplaceModel(newDraftRoot, newModelSource);
        SceneryObjectAuthoringUtility.SetHideFlagsRecursively(newDraftRoot, HideFlags.HideAndDontSave);
        newDraftModelSource = newModelSource;

        if (newModelSource != null)
            SceneryObjectAuthoringUtility.EnsureCollisionFromRenderers(newDraftRoot, newColliderShape);
    }

    private void CreateNewObjectPrefab()
    {
        if (newDraftRoot == null)
            return;

        string safeName = SceneryObjectAuthoringUtility.SanitizeAssetName(newObjectName, "New Scenery Object");
        newDraftRoot.name = safeName;
        SceneryObjectAuthoringUtility.ApplyDestructibleSetup(newDraftRoot, newIsDestructible, newSpawnOnDestroyed);
        SceneryObjectAuthoringUtility.PrepareForPrefabSave(newDraftRoot, newColliderShape, regenerateCollision: true);

        string prefabPath = SceneryObjectAuthoringUtility.CreateUniquePrefabPath(safeName);
        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(newDraftRoot, prefabPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        DestroyCachedEditors();
        DestroyNewDraft();

        Selection.activeObject = prefabAsset;
        EditorGUIUtility.PingObject(prefabAsset);

        newObjectName = "New Scenery Object";
        newModelSource = null;
        newDraftModelSource = null;
    }

    private void SetEditTarget(GameObject targetObject)
    {
        GameObject prefabAsset = SceneryObjectAuthoringUtility.ResolvePrefabAsset(targetObject);
        if (prefabAsset == null && SceneryObjectAuthoringUtility.IsPrefabAsset(targetObject))
            prefabAsset = targetObject;

        if (prefabAsset == editPrefab)
            return;

        editPrefab = prefabAsset;
        ReloadEditDraft();
    }

    private void EnsureEditDraftLoaded()
    {
        if (editDraftRoot != null)
            return;

        if (editPrefab == null)
            return;

        editPrefabPath = AssetDatabase.GetAssetPath(editPrefab);
        if (string.IsNullOrWhiteSpace(editPrefabPath))
            return;

        editDraftRoot = PrefabUtility.LoadPrefabContents(editPrefabPath);
        SceneryObjectAuthoringUtility.SetHideFlagsRecursively(editDraftRoot, HideFlags.HideAndDontSave);
        editObjectName = editDraftRoot.name;
        editIsDestructible = editDraftRoot.GetComponent<DestructibleObjectController>() != null;
        editSpawnOnDestroyed = editDraftRoot.GetComponent<DestructibleSpawnOnDestroyed>() != null;
        editReplacementModelSource = null;
        DestroyCachedEditors();
    }

    private void ReloadEditDraft()
    {
        DestroyCachedEditors();
        UnloadEditDraft();
        EnsureEditDraftLoaded();
    }

    private void SaveEditDraft()
    {
        if (editDraftRoot == null || string.IsNullOrWhiteSpace(editPrefabPath))
            return;

        string safeName = SceneryObjectAuthoringUtility.SanitizeAssetName(editObjectName, editPrefab.name);
        editDraftRoot.name = safeName;
        SceneryObjectAuthoringUtility.ApplyDestructibleSetup(editDraftRoot, editIsDestructible, editSpawnOnDestroyed);
        SceneryObjectAuthoringUtility.PrepareForPrefabSave(editDraftRoot, editColliderShape, regenerateCollision: false);

        PrefabUtility.SaveAsPrefabAsset(editDraftRoot, editPrefabPath);
        AssetDatabase.SaveAssets();

        string originalName = Path.GetFileNameWithoutExtension(editPrefabPath);
        if (!string.Equals(originalName, safeName, System.StringComparison.Ordinal))
        {
            string renameError = AssetDatabase.RenameAsset(editPrefabPath, safeName);
            if (!string.IsNullOrEmpty(renameError))
                Debug.LogWarning($"[SceneryObjectAuthoringTool] Nao foi possivel renomear o prefab: {renameError}", editDraftRoot);
            else
                editPrefabPath = $"{Path.GetDirectoryName(editPrefabPath)?.Replace('\\', '/')}/{safeName}.prefab";
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(editPrefabPath);
        DestroyCachedEditors();
        UnloadEditDraft();

        editPrefab = savedPrefab;
        Selection.activeObject = savedPrefab;
        EditorGUIUtility.PingObject(savedPrefab);
        EnsureEditDraftLoaded();
    }

    private void UnloadEditDraft()
    {
        if (editDraftRoot == null)
            return;

        PrefabUtility.UnloadPrefabContents(editDraftRoot);
        editDraftRoot = null;
    }

    private void DestroyNewDraft()
    {
        if (newDraftRoot == null)
            return;

        DestroyImmediate(newDraftRoot);
        newDraftRoot = null;
    }

    private void DrawComponentInspector(
        Component component,
        ref UnityEditor.Editor cachedEditor,
        ref Component cachedTarget)
    {
        if (component == null)
            return;

        if (cachedEditor == null || cachedTarget != component)
        {
            DestroyCachedEditor(ref cachedEditor, ref cachedTarget);
            cachedEditor = UnityEditor.Editor.CreateEditor(component);
            cachedTarget = component;
        }

        cachedEditor?.OnInspectorGUI();
    }

    private void DestroyCachedEditors()
    {
        DestroyCachedEditor(ref receiverEditor, ref receiverEditorTarget);
    }

    private void EnterNewObjectMode()
    {
        mode = ToolMode.NewObject;
        scrollPosition = Vector2.zero;
        DestroyCachedEditors();
        UnloadEditDraft();
    }

    private void EnterEditObjectMode(GameObject targetObject)
    {
        mode = ToolMode.EditObject;
        scrollPosition = Vector2.zero;
        DestroyCachedEditors();
        DestroyNewDraft();
        SetEditTarget(targetObject);
    }

    private void DestroyCachedEditor(ref UnityEditor.Editor cachedEditor, ref Component cachedTarget)
    {
        if (cachedEditor != null)
            DestroyImmediate(cachedEditor);

        cachedEditor = null;
        cachedTarget = null;
    }
}
