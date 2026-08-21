using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class InventoryItemAuthoringToolWindow : EditorWindow
{
    private enum ToolMode
    {
        NewItem,
        EditItem
    }

    private ToolMode mode;
    private Vector2 scrollPosition;

    private string newItemName = "New Item";
    private GameObject newModelSource;
    private Sprite newUiSprite;
    private HandRequirement newHandRequirement = HandRequirement.Any;
    private HandType newPreferredHand = HandType.Right;
    private ItemUseType newUseType = ItemUseType.None;
    private float newHealAmount;
    private float newBaseDamage = 25f;
    private bool newConsumeOnUse;
    private bool newCanBeSold;
    private int newSellPrice;
    private ItemPickupColliderShape newColliderShape = ItemPickupColliderShape.Box;
    private bool newAddRigidbody = true;
    private Color newOutlineColor = new Color(1f, 0.82f, 0.2f, 1f);
    private float newOutlineWidth = 5f;

    private GameObject editPrefab;
    private ItemDefinition editFallbackItemDefinition;
    private ItemPickupColliderShape editColliderShape = ItemPickupColliderShape.Box;
    private Color editOutlineColor = new Color(1f, 0.82f, 0.2f, 1f);
    private float editOutlineWidth = 5f;
    private GameObject editDraftPrefab;
    private bool editDraftLoaded;
    private bool editHasPendingChanges;
    private ItemDefinition editDraftItemDefinition;
    private string editDraftNetworkSceneId = string.Empty;
    private Transform editDraftRightGripPoint;
    private Transform editDraftLeftGripPoint;
    private string editDraftItemName = "Item";
    private Sprite editDraftUiSprite;
    private HandRequirement editDraftHandRequirement = HandRequirement.Any;
    private HandType editDraftPreferredHand = HandType.Right;
    private ItemUseType editDraftUseType = ItemUseType.None;
    private float editDraftHealAmount;
    private float editDraftBaseDamage = 25f;
    private bool editDraftConsumeOnUse;
    private bool editDraftCanBeSold;
    private int editDraftSellPrice;

    [MenuItem("Tools/Inventory/Item Authoring Tool")]
    public static void OpenWindow()
    {
        InventoryItemAuthoringToolWindow window = GetWindow<InventoryItemAuthoringToolWindow>("Item Authoring");
        window.mode = ToolMode.NewItem;
        window.Show();
    }

    public static void OpenForTarget(WorldPickupItem pickupItem)
    {
        InventoryItemAuthoringToolWindow window = GetWindow<InventoryItemAuthoringToolWindow>("Item Authoring");
        window.mode = ToolMode.EditItem;
        window.SetTarget(pickupItem);
        window.Show();
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.Space();

        if (mode == ToolMode.NewItem)
            DrawNewItemMode();
        else
            DrawEditItemMode();

        EditorGUILayout.EndScrollView();
    }

    private void DrawNewItemMode()
    {
        EditorGUILayout.LabelField("Novo Item", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"Cria um ItemDefinition em {InventoryItemAuthoringUtility.ItemDefinitionsFolder} e um prefab completo em {InventoryItemAuthoringUtility.ItemPrefabsFolder}.",
            MessageType.None);

        newItemName = EditorGUILayout.TextField("Item Name", newItemName);
        newModelSource = (GameObject)EditorGUILayout.ObjectField("Modelo", newModelSource, typeof(GameObject), false);

        EditorGUILayout.Space();
        DrawItemDefinitionFields(
            ref newUiSprite,
            ref newHandRequirement,
            ref newPreferredHand,
            ref newUseType,
            ref newHealAmount,
            ref newBaseDamage,
            ref newConsumeOnUse,
            ref newCanBeSold,
            ref newSellPrice);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pickup", EditorStyles.boldLabel);
        newColliderShape = (ItemPickupColliderShape)EditorGUILayout.EnumPopup("Collider Shape", newColliderShape);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Physics", EditorStyles.boldLabel);
        DrawStatusRow("Rigidbody", newAddRigidbody);
        if (GUILayout.Button("Rigidbody"))
            newAddRigidbody = true;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Outline", EditorStyles.boldLabel);
        newOutlineColor = EditorGUILayout.ColorField("Color", newOutlineColor);
        newOutlineWidth = EditorGUILayout.Slider("Width", newOutlineWidth, 0f, 10f);

        bool canCreate = !string.IsNullOrWhiteSpace(newItemName) && newModelSource != null;
        using (new EditorGUI.DisabledScope(!canCreate))
        {
            if (GUILayout.Button("Criar Item"))
                CreateNewItem();
        }

        if (!canCreate)
            EditorGUILayout.HelpBox("Informe um nome e selecione um modelo para criar o item.", MessageType.Warning);
    }

    private void DrawEditItemMode()
    {
        EditorGUILayout.LabelField("Editar", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Edicao aberta a partir do Inspector de um prefab asset, instancia de prefab na Scene ou item existente.",
            MessageType.None);

        if (editPrefab == null)
        {
            EditorGUILayout.HelpBox("Abra esta edicao pelo botao Abrir Item Authoring Tool no Inspector de um prefab asset, instancia de prefab na Scene ou WorldPickupItem existente.", MessageType.Warning);
            return;
        }

        if (!InventoryItemAuthoringUtility.IsItemPrefabAsset(editPrefab))
        {
            EditorGUILayout.HelpBox($"O prefab precisa estar em {InventoryItemAuthoringUtility.ItemPrefabsFolder}.", MessageType.Warning);
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(editPrefab);
        WorldPickupItem pickupAsset = editPrefab.GetComponent<WorldPickupItem>();
        DrawPrefabStatus(editPrefab, pickupAsset);

        if (pickupAsset == null)
        {
            editFallbackItemDefinition = (ItemDefinition)EditorGUILayout.ObjectField("Item Definition", editFallbackItemDefinition, typeof(ItemDefinition), false);
            editOutlineColor = EditorGUILayout.ColorField("Outline Color", editOutlineColor);
            editOutlineWidth = EditorGUILayout.Slider("Outline Width", editOutlineWidth, 0f, 10f);

            if (GUILayout.Button("Adicionar Setup De Item Ao Prefab"))
            {
                InventoryItemAuthoringUtility.ApplyBasicPrefabSetup(editPrefab, editFallbackItemDefinition, editOutlineColor, editOutlineWidth);
                editPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                ResetEditDraft();
            }

            return;
        }

        EnsureEditDraftLoaded(pickupAsset);
        DrawPickupAssetFields();
        DrawItemDefinitionEditor();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Prefab Setup", EditorStyles.boldLabel);
        editOutlineColor = EditorGUILayout.ColorField("Outline Color", editOutlineColor);
        editOutlineWidth = EditorGUILayout.Slider("Outline Width", editOutlineWidth, 0f, 10f);
        if (GUILayout.Button("Gerar Grip Points TP/FPS"))
        {
            if (InventoryItemAuthoringUtility.EnsureGripPointsOnPrefab(editPrefab))
            {
                editPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                ResetEditDraft();
                return;
            }
        }

        if (GUILayout.Button("Gerar Outline"))
        {
            MutatePrefab(editPrefab, root =>
            {
                InventoryItemAuthoringUtility.EnsureOutline(root, editOutlineColor, editOutlineWidth);
                EditorUtility.SetDirty(root);
            });

            editPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            ResetEditDraft();
        }

        if (GUILayout.Button("Rigidbody"))
        {
            MutatePrefab(editPrefab, root =>
            {
                InventoryItemAuthoringUtility.EnsureRigidbody(root);
            });

            editPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            ResetEditDraft();
        }

        if (GUILayout.Button("Collision"))
        {
            MutatePrefab(editPrefab, root =>
            {
                WorldPickupItem pickupItem = root.GetComponent<WorldPickupItem>();
                if (pickupItem == null)
                    pickupItem = root.AddComponent<WorldPickupItem>();

                InventoryItemAuthoringUtility.EnsureDropCollisionFromPickup(pickupItem);
                EditorUtility.SetDirty(root);
                EditorUtility.SetDirty(pickupItem);
            });

            editPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            ResetEditDraft();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pickup Collider", EditorStyles.boldLabel);
        editColliderShape = (ItemPickupColliderShape)EditorGUILayout.EnumPopup("Collider Shape", editColliderShape);
        if (GUILayout.Button("Gerar Pickup"))
        {
            MutatePrefab(editPrefab, root =>
            {
                WorldPickupItem pickupItem = root.GetComponent<WorldPickupItem>();
                if (pickupItem == null)
                    pickupItem = root.AddComponent<WorldPickupItem>();

                if (!InventoryItemAuthoringUtility.RebuildPickupColliderFromRenderers(pickupItem, editColliderShape))
                    Debug.LogWarning("[InventoryItemAuthoringTool] Nao foi possivel calcular bounds do visual para recriar o PickupTrigger.", root);

                InventoryItemAuthoringUtility.EnsureDropCollisionFromPickup(pickupItem);
            });

            editPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            ResetEditDraft();
        }

        DrawEditConfirmationControls(pickupAsset);
    }

    private void DrawPickupAssetFields()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("World Pickup Runtime Data", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        ItemDefinition nextItemDefinition = (ItemDefinition)EditorGUILayout.ObjectField(
            "Item Definition",
            editDraftItemDefinition,
            typeof(ItemDefinition),
            false);
        editDraftNetworkSceneId = EditorGUILayout.TextField("Network Scene Id", editDraftNetworkSceneId);
        if (InventoryItemAuthoringUtility.ShouldUseGripPoint(editDraftHandRequirement, HandType.Right))
        {
            editDraftRightGripPoint = (Transform)EditorGUILayout.ObjectField(
                "Right Hand Grip Point",
                editDraftRightGripPoint,
                typeof(Transform),
                true);
        }
        else
        {
            editDraftRightGripPoint = null;
        }

        if (InventoryItemAuthoringUtility.ShouldUseGripPoint(editDraftHandRequirement, HandType.Left))
        {
            editDraftLeftGripPoint = (Transform)EditorGUILayout.ObjectField(
                "Left Hand Grip Point",
                editDraftLeftGripPoint,
                typeof(Transform),
                true);
        }
        else
        {
            editDraftLeftGripPoint = null;
        }

        if (!EditorGUI.EndChangeCheck())
            return;

        if (nextItemDefinition != editDraftItemDefinition)
        {
            editDraftItemDefinition = nextItemDefinition;
            LoadItemDefinitionDraft(editDraftItemDefinition);
        }

        editHasPendingChanges = true;
    }

    private void DrawItemDefinitionEditor()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Item Definition", EditorStyles.boldLabel);

        if (editDraftItemDefinition == null)
        {
            EditorGUILayout.HelpBox("Este prefab ainda nao aponta para um ItemDefinition.", MessageType.Warning);
            return;
        }

        EditorGUI.BeginChangeCheck();
        editDraftItemName = EditorGUILayout.TextField("Item Name", editDraftItemName);
        editDraftUiSprite = (Sprite)EditorGUILayout.ObjectField("UI Sprite", editDraftUiSprite, typeof(Sprite), false);
        editDraftHandRequirement = (HandRequirement)EditorGUILayout.EnumPopup("Hand Requirement", editDraftHandRequirement);
        if (editDraftHandRequirement == HandRequirement.Any)
            editDraftPreferredHand = (HandType)EditorGUILayout.EnumPopup("Preferred Hand", editDraftPreferredHand);
        else
            editDraftPreferredHand = InventoryItemAuthoringUtility.ResolveStoredPreferredHand(editDraftHandRequirement, editDraftPreferredHand);
        editDraftUseType = (ItemUseType)EditorGUILayout.EnumPopup("Use Type", editDraftUseType);

        if (IsConsumableUseType(editDraftUseType))
        {
            editDraftHealAmount = Mathf.Max(0f, EditorGUILayout.FloatField("Heal Amount", editDraftHealAmount));
            editDraftConsumeOnUse = EditorGUILayout.Toggle("Consume On Use", editDraftConsumeOnUse);
        }

        if (IsWeaponUseType(editDraftUseType))
            editDraftBaseDamage = Mathf.Max(0f, EditorGUILayout.FloatField("Base Damage", editDraftBaseDamage));

        editDraftCanBeSold = EditorGUILayout.Toggle("Can Be Sold", editDraftCanBeSold);
        if (editDraftCanBeSold)
            editDraftSellPrice = Mathf.Max(0, EditorGUILayout.IntField("Sell Price", editDraftSellPrice));

        if (EditorGUI.EndChangeCheck())
            editHasPendingChanges = true;
    }

    private void DrawEditConfirmationControls(WorldPickupItem pickupAsset)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Confirmação", EditorStyles.boldLabel);
        bool canConfirm = editHasPendingChanges && pickupAsset != null;
        if (editHasPendingChanges)
            EditorGUILayout.HelpBox("Existem alterações pendentes. Clique em Confirmar Edições para gravar no prefab e no ItemDefinition.", MessageType.Warning);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!canConfirm))
        {
            if (GUILayout.Button("Confirmar Edições"))
                ApplyEditDraft(pickupAsset);
        }

        using (new EditorGUI.DisabledScope(!canConfirm))
        {
            if (GUILayout.Button("Reverter"))
                LoadEditDraft(pickupAsset);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void EnsureEditDraftLoaded(WorldPickupItem pickupAsset)
    {
        if (pickupAsset == null)
            return;

        if (editDraftLoaded && editDraftPrefab == editPrefab)
            return;

        LoadEditDraft(pickupAsset);
    }

    private void ResetEditDraft()
    {
        editDraftPrefab = null;
        editDraftLoaded = false;
        editHasPendingChanges = false;
    }

    private void LoadEditDraft(WorldPickupItem pickupAsset)
    {
        editDraftPrefab = editPrefab;
        editDraftLoaded = true;
        editHasPendingChanges = false;

        if (pickupAsset == null)
        {
            editDraftItemDefinition = null;
            editDraftNetworkSceneId = string.Empty;
            editDraftRightGripPoint = null;
            editDraftLeftGripPoint = null;
            LoadItemDefinitionDraft(null);
            return;
        }

        SerializedObject serializedPickup = new SerializedObject(pickupAsset);
        editDraftItemDefinition = serializedPickup.FindProperty("itemDefinition")?.objectReferenceValue as ItemDefinition;
        editDraftNetworkSceneId = serializedPickup.FindProperty("networkSceneId")?.stringValue ?? string.Empty;
        editDraftRightGripPoint = serializedPickup
            .FindProperty("rightHandPose")
            ?.FindPropertyRelative("gripPoint")
            ?.objectReferenceValue as Transform;
        editDraftLeftGripPoint = serializedPickup
            .FindProperty("leftHandPose")
            ?.FindPropertyRelative("gripPoint")
            ?.objectReferenceValue as Transform;

        LoadItemDefinitionDraft(editDraftItemDefinition);
    }

    private void LoadItemDefinitionDraft(ItemDefinition itemDefinition)
    {
        editDraftItemDefinition = itemDefinition;
        if (itemDefinition == null)
        {
            editDraftItemName = "Item";
            editDraftUiSprite = null;
            editDraftHandRequirement = HandRequirement.Any;
            editDraftPreferredHand = HandType.Right;
            editDraftUseType = ItemUseType.None;
            editDraftHealAmount = 0f;
            editDraftBaseDamage = 25f;
            editDraftConsumeOnUse = false;
            editDraftCanBeSold = false;
            editDraftSellPrice = 0;
            return;
        }

        SerializedObject serializedItem = new SerializedObject(itemDefinition);
        editDraftItemName = serializedItem.FindProperty("itemName")?.stringValue ?? itemDefinition.ItemName;
        editDraftUiSprite = serializedItem.FindProperty("uiSprite")?.objectReferenceValue as Sprite;
        editDraftHandRequirement = (HandRequirement)(serializedItem.FindProperty("handRequirement")?.enumValueIndex ?? (int)itemDefinition.HandRequirement);
        editDraftPreferredHand = (HandType)(serializedItem.FindProperty("preferredHand")?.enumValueIndex ?? (int)itemDefinition.PreferredHand);
        editDraftPreferredHand = InventoryItemAuthoringUtility.ResolveStoredPreferredHand(editDraftHandRequirement, editDraftPreferredHand);
        editDraftUseType = (ItemUseType)(serializedItem.FindProperty("useType")?.enumValueIndex ?? (int)itemDefinition.UseType);
        editDraftHealAmount = Mathf.Max(0f, serializedItem.FindProperty("healAmount")?.floatValue ?? itemDefinition.HealAmount);
        editDraftBaseDamage = Mathf.Max(0f, serializedItem.FindProperty("baseDamage")?.floatValue ?? itemDefinition.BaseDamage);
        editDraftConsumeOnUse = serializedItem.FindProperty("consumeOnUse")?.boolValue ?? itemDefinition.ConsumeOnUse;
        editDraftCanBeSold = serializedItem.FindProperty("canBeSold")?.boolValue ?? itemDefinition.CanBeSold;
        editDraftSellPrice = Mathf.Max(0, serializedItem.FindProperty("sellPrice")?.intValue ?? itemDefinition.SellPrice);
    }

    private void ApplyEditDraft(WorldPickupItem pickupAsset)
    {
        if (pickupAsset == null || editPrefab == null)
            return;

        string prefabPath = AssetDatabase.GetAssetPath(editPrefab);
        if (string.IsNullOrWhiteSpace(prefabPath))
            return;

        string safeItemName = InventoryItemAuthoringUtility.SanitizeAssetName(
            editDraftItemName,
            editPrefab.name);
        bool shouldUseRightGrip = InventoryItemAuthoringUtility.ShouldUseGripPoint(editDraftHandRequirement, HandType.Right);
        bool shouldUseLeftGrip = InventoryItemAuthoringUtility.ShouldUseGripPoint(editDraftHandRequirement, HandType.Left);
        HandType storedPreferredHand = InventoryItemAuthoringUtility.ResolveStoredPreferredHand(editDraftHandRequirement, editDraftPreferredHand);
        string rightGripPath = shouldUseRightGrip ? GetRelativeTransformPath(editDraftRightGripPoint, editPrefab.transform) : string.Empty;
        string leftGripPath = shouldUseLeftGrip ? GetRelativeTransformPath(editDraftLeftGripPoint, editPrefab.transform) : string.Empty;

        if (editDraftItemDefinition != null)
        {
            Undo.RecordObject(editDraftItemDefinition, "Confirm Item Definition Edits");
            InventoryItemAuthoringUtility.ApplyItemDefinitionData(
                editDraftItemDefinition,
                new ItemDefinitionAuthoringData
                {
                    ItemName = editDraftItemName,
                    UiSprite = editDraftUiSprite,
                    HandRequirement = editDraftHandRequirement,
                    PreferredHand = storedPreferredHand,
                    UseType = editDraftUseType,
                    HealAmount = editDraftHealAmount,
                    BaseDamage = editDraftBaseDamage,
                    ConsumeOnUse = editDraftConsumeOnUse,
                    CanBeSold = editDraftCanBeSold,
                    SellPrice = editDraftSellPrice
                });
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            root.name = safeItemName;

            WorldPickupItem prefabPickup = root.GetComponent<WorldPickupItem>();
            if (prefabPickup == null)
                prefabPickup = root.AddComponent<WorldPickupItem>();

            SerializedObject serializedPickup = new SerializedObject(prefabPickup);
            SerializedProperty itemDefinitionProperty = serializedPickup.FindProperty("itemDefinition");
            if (itemDefinitionProperty != null)
                itemDefinitionProperty.objectReferenceValue = editDraftItemDefinition;

            SerializedProperty networkSceneIdProperty = serializedPickup.FindProperty("networkSceneId");
            if (networkSceneIdProperty != null)
                networkSceneIdProperty.stringValue = editDraftNetworkSceneId ?? string.Empty;

            SetGripPointProperty(
                serializedPickup,
                "rightHandPose",
                shouldUseRightGrip
                    ? ResolvePrefabContentsTransform(root.transform, rightGripPath, editDraftRightGripPoint)
                    : null);
            SetGripPointProperty(
                serializedPickup,
                "leftHandPose",
                shouldUseLeftGrip
                    ? ResolvePrefabContentsTransform(root.transform, leftGripPath, editDraftLeftGripPoint)
                    : null);
            serializedPickup.ApplyModifiedPropertiesWithoutUndo();

            InventoryItemAuthoringUtility.EnsureGripPoints(prefabPickup);

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(prefabPickup);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        RenameItemDefinitionAssetIfNeeded(safeItemName);
        string finalPrefabPath = MoveAssetToMatchingName(
            prefabPath,
            InventoryItemAuthoringUtility.ItemPrefabsFolder,
            safeItemName,
            ".prefab");
        AssetDatabase.Refresh();

        editPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(finalPrefabPath);
        if (editPrefab != null)
            Selection.activeObject = editPrefab;

        editHasPendingChanges = false;
        editDraftLoaded = false;
        Debug.Log($"[InventoryItemAuthoringTool] Edicoes confirmadas para '{safeItemName}'.", editPrefab);
    }

    private static void SetGripPointProperty(SerializedObject serializedPickup, string posePropertyName, Transform gripPoint)
    {
        SerializedProperty gripProperty = serializedPickup
            .FindProperty(posePropertyName)
            ?.FindPropertyRelative("gripPoint");
        if (gripProperty != null)
            gripProperty.objectReferenceValue = gripPoint;
    }

    private void RenameItemDefinitionAssetIfNeeded(string safeItemName)
    {
        if (editDraftItemDefinition == null)
            return;

        string itemDefinitionPath = AssetDatabase.GetAssetPath(editDraftItemDefinition);
        MoveAssetToMatchingName(
            itemDefinitionPath,
            InventoryItemAuthoringUtility.ItemDefinitionsFolder,
            safeItemName,
            ".asset");
    }

    private static string MoveAssetToMatchingName(
        string currentPath,
        string expectedFolder,
        string safeItemName,
        string extension)
    {
        if (string.IsNullOrWhiteSpace(currentPath)
            || string.IsNullOrWhiteSpace(expectedFolder)
            || string.IsNullOrWhiteSpace(safeItemName)
            || string.IsNullOrWhiteSpace(extension))
        {
            return currentPath;
        }

        string expectedPrefix = expectedFolder.EndsWith("/", StringComparison.Ordinal)
            ? expectedFolder
            : $"{expectedFolder}/";
        if (!currentPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            return currentPath;

        string targetPath = $"{expectedFolder}/{safeItemName}{extension}";
        if (string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase))
            return currentPath;

        UnityEngine.Object existingAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath);
        if (existingAsset != null)
            targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);

        string moveError = AssetDatabase.MoveAsset(currentPath, targetPath);
        if (string.IsNullOrEmpty(moveError))
            return targetPath;

        Debug.LogWarning($"[InventoryItemAuthoringTool] Nao foi possivel renomear asset '{currentPath}' para '{targetPath}': {moveError}");
        return currentPath;
    }

    private static string GetRelativeTransformPath(Transform target, Transform root)
    {
        if (target == null || root == null)
            return string.Empty;

        if (!IsChildOrSelf(target, root))
        {
            Transform sourceTransform = PrefabUtility.GetCorrespondingObjectFromSource(target) as Transform;
            if (sourceTransform != null && IsChildOrSelf(sourceTransform, root))
                target = sourceTransform;
        }

        if (target == root)
            return string.Empty;

        string path = target.name;
        Transform current = target.parent;
        while (current != null && current != root)
        {
            path = $"{current.name}/{path}";
            current = current.parent;
        }

        return current == root ? path : string.Empty;
    }

    private static bool IsChildOrSelf(Transform target, Transform root)
    {
        if (target == null || root == null)
            return false;

        Transform current = target;
        while (current != null)
        {
            if (current == root)
                return true;

            current = current.parent;
        }

        return false;
    }

    private static Transform ResolvePrefabContentsTransform(Transform root, string relativePath, Transform fallbackSource)
    {
        if (root == null)
            return null;

        Transform resolvedTransform = string.IsNullOrWhiteSpace(relativePath)
            ? null
            : root.Find(relativePath);
        if (resolvedTransform != null)
            return resolvedTransform;

        if (fallbackSource == null)
            return null;

        return FindDirectChildByName(root, fallbackSource.name);
    }

    private static void DrawItemDefinitionFields(
        ref Sprite uiSprite,
        ref HandRequirement handRequirement,
        ref HandType preferredHand,
        ref ItemUseType useType,
        ref float healAmount,
        ref float baseDamage,
        ref bool consumeOnUse,
        ref bool canBeSold,
        ref int sellPrice)
    {
        EditorGUILayout.LabelField("Item Data", EditorStyles.boldLabel);
        uiSprite = (Sprite)EditorGUILayout.ObjectField("UI Sprite", uiSprite, typeof(Sprite), false);
        handRequirement = (HandRequirement)EditorGUILayout.EnumPopup("Hand Requirement", handRequirement);
        if (handRequirement == HandRequirement.Any)
            preferredHand = (HandType)EditorGUILayout.EnumPopup("Preferred Hand", preferredHand);
        else
            preferredHand = InventoryItemAuthoringUtility.ResolveStoredPreferredHand(handRequirement, preferredHand);
        useType = (ItemUseType)EditorGUILayout.EnumPopup("Use Type", useType);

        if (IsConsumableUseType(useType))
        {
            healAmount = Mathf.Max(0f, EditorGUILayout.FloatField("Heal Amount", healAmount));
            consumeOnUse = EditorGUILayout.Toggle("Consume On Use", consumeOnUse);
        }

        if (IsWeaponUseType(useType))
            baseDamage = Mathf.Max(0f, EditorGUILayout.FloatField("Base Damage", baseDamage));

        canBeSold = EditorGUILayout.Toggle("Can Be Sold", canBeSold);
        if (canBeSold)
            sellPrice = Mathf.Max(0, EditorGUILayout.IntField("Sell Price", sellPrice));
    }

    private void DrawPrefabStatus(GameObject prefabAsset, WorldPickupItem pickupAsset)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

        string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
        EditorGUILayout.HelpBox(prefabPath, MessageType.None);

        bool hasPickup = pickupAsset != null;
        bool hasOutline = prefabAsset.GetComponent<Outline>() != null;
        bool hasRigidbody = HasWorldRigidbody(prefabAsset);
        bool hasPickupTrigger = FindDirectChildByName(prefabAsset.transform, InventoryItemAuthoringUtility.PickupTriggerName) != null;
        bool hasRightGrip = FindDirectChildByName(prefabAsset.transform, InventoryItemAuthoringUtility.RightGripName) != null;
        bool hasLeftGrip = FindDirectChildByName(prefabAsset.transform, InventoryItemAuthoringUtility.LeftGripName) != null;
        bool hasFirstPersonRightGrip = FindDirectChildByName(prefabAsset.transform, InventoryItemAuthoringUtility.FirstPersonRightGripName) != null;
        bool hasFirstPersonLeftGrip = FindDirectChildByName(prefabAsset.transform, InventoryItemAuthoringUtility.FirstPersonLeftGripName) != null;
        bool hasDropCollision = HasActiveDropCollision(prefabAsset.transform);
        HandRequirement handRequirement = pickupAsset != null && pickupAsset.ItemDefinition != null
            ? pickupAsset.ItemDefinition.HandRequirement
            : HandRequirement.Any;

        DrawStatusRow("WorldPickupItem", hasPickup);
        DrawStatusRow("Outline", hasOutline);
        DrawStatusRow("Rigidbody", hasRigidbody);
        DrawStatusRow("PickupTrigger", hasPickupTrigger);
        DrawGripPointStatus(InventoryItemAuthoringUtility.RightGripName, handRequirement, HandType.Right, hasRightGrip);
        DrawGripPointStatus(InventoryItemAuthoringUtility.FirstPersonRightGripName, handRequirement, HandType.Right, hasFirstPersonRightGrip);
        DrawGripPointStatus(InventoryItemAuthoringUtility.LeftGripName, handRequirement, HandType.Left, hasLeftGrip);
        DrawGripPointStatus(InventoryItemAuthoringUtility.FirstPersonLeftGripName, handRequirement, HandType.Left, hasFirstPersonLeftGrip);
        DrawStatusRow("DropCollision", hasDropCollision);
    }

    private static bool HasWorldRigidbody(GameObject prefabAsset)
    {
        Rigidbody itemRigidbody = prefabAsset != null ? prefabAsset.GetComponent<Rigidbody>() : null;
        return itemRigidbody != null
            && itemRigidbody.useGravity
            && !itemRigidbody.isKinematic
            && itemRigidbody.detectCollisions;
    }

    private static bool HasActiveDropCollision(Transform root)
    {
        Transform dropCollision = FindDirectChildByName(root, InventoryItemAuthoringUtility.DropCollisionName);
        Collider collider = dropCollision != null ? dropCollision.GetComponent<Collider>() : null;
        return collider != null && collider.enabled && !collider.isTrigger;
    }

    private static void DrawGripPointStatus(string label, HandRequirement handRequirement, HandType hand, bool exists)
    {
        if (!InventoryItemAuthoringUtility.ShouldUseGripPoint(handRequirement, hand))
            return;

        DrawStatusRow(label, exists);
    }

    private static void DrawStatusRow(string label, bool isOk)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label);

        GUIStyle statusStyle = new GUIStyle(EditorStyles.label);
        if (isOk)
        {
            statusStyle.fontStyle = FontStyle.Bold;
            statusStyle.normal.textColor = new Color(0.25f, 0.75f, 0.35f);
        }
        else
        {
            statusStyle.normal.textColor = new Color(0.9f, 0.25f, 0.25f);
        }

        EditorGUILayout.LabelField(isOk ? "OK" : "Faltando", statusStyle, GUILayout.Width(90f));
        EditorGUILayout.EndHorizontal();
    }

    private void CreateNewItem()
    {
        ItemDefinitionAuthoringData data = new ItemDefinitionAuthoringData
        {
            ItemName = newItemName,
            UiSprite = newUiSprite,
            HandRequirement = newHandRequirement,
            PreferredHand = InventoryItemAuthoringUtility.ResolveStoredPreferredHand(newHandRequirement, newPreferredHand),
            UseType = newUseType,
            HealAmount = newHealAmount,
            BaseDamage = newBaseDamage,
            ConsumeOnUse = newConsumeOnUse,
            CanBeSold = newCanBeSold,
            SellPrice = newSellPrice
        };

        ItemDefinition itemDefinition = InventoryItemAuthoringUtility.CreateItemDefinition(data, out string itemDefinitionPath);
        GameObject prefabAsset = InventoryItemAuthoringUtility.CreateItemPrefab(
            itemDefinition,
            newModelSource,
            newColliderShape,
            newOutlineColor,
            newOutlineWidth,
            newAddRigidbody,
            out string prefabPath);

        Debug.Log($"[InventoryItemAuthoringTool] Item criado: {itemDefinitionPath} / {prefabPath}", prefabAsset);
        editPrefab = prefabAsset;
        ResetEditDraft();
        mode = ToolMode.EditItem;
        Selection.activeObject = prefabAsset;
        EditorGUIUtility.PingObject(prefabAsset);
    }

    private void SetTarget(WorldPickupItem pickupItem)
    {
        if (pickupItem == null)
            return;

        GameObject prefabAsset = ResolvePrefabAsset(pickupItem.gameObject);
        if (prefabAsset != null)
        {
            editPrefab = prefabAsset;
            ResetEditDraft();
        }
    }

    private static GameObject ResolvePrefabAsset(GameObject candidate)
    {
        if (candidate == null)
            return null;

        if (InventoryItemAuthoringUtility.IsItemPrefabAsset(candidate))
            return candidate;

        GameObject sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
        if (sourcePrefab != null && InventoryItemAuthoringUtility.IsItemPrefabAsset(sourcePrefab))
            return sourcePrefab;

        PrefabStage prefabStage = PrefabStageUtility.GetPrefabStage(candidate);
        if (prefabStage != null && !string.IsNullOrWhiteSpace(prefabStage.assetPath))
        {
            GameObject prefabStageAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabStage.assetPath);
            if (InventoryItemAuthoringUtility.IsItemPrefabAsset(prefabStageAsset))
                return prefabStageAsset;
        }

        string path = AssetDatabase.GetAssetPath(candidate);
        if (!string.IsNullOrWhiteSpace(path))
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (InventoryItemAuthoringUtility.IsItemPrefabAsset(prefabAsset))
                return prefabAsset;
        }

        return null;
    }

    private static void MutatePrefab(GameObject prefabAsset, Action<GameObject> mutateAction)
    {
        if (prefabAsset == null || mutateAction == null)
            return;

        string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
        if (string.IsNullOrWhiteSpace(prefabPath))
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            mutateAction(root);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
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

    private static bool IsConsumableUseType(ItemUseType useType)
    {
        return useType == ItemUseType.Consumable;
    }

    private static bool IsWeaponUseType(ItemUseType useType)
    {
        return useType == ItemUseType.Weapon
            || useType == ItemUseType.MeleeWeapon;
    }

}
