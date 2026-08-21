using UnityEditor;
using UnityEngine;

public static class PrototypePickupPrefabConfigurator
{
    private const string SwordPrefabPath = "Assets/Prefabs/Items/Sword.prefab";
    private const string PotionPrefabPath = "Assets/Prefabs/Items/Heath Potion.prefab";
    private const string TorchPrefabPath = "Assets/Prefabs/Items/Torch.prefab";
    private const string SwordItemPath = "Assets/Resources/Items/PrototypeSwordItem.asset";
    private const string PotionItemPath = "Assets/Resources/Items/PrototypeHealthPotionItem.asset";
    private const string TorchItemPath = "Assets/Resources/Items/PrototypeTorch.asset";

    [MenuItem("Tools/Inventory/Configure Prototype Pickup Prefabs")]
    public static void ConfigurePrototypePickupPrefabsMenu()
    {
        ConfigurePrototypePickupPrefabs();
    }

    public static void ConfigurePrototypePickupPrefabs()
    {
        ConfigurePrefab(SwordPrefabPath, SwordItemPath, ItemPickupColliderShape.Box);
        ConfigurePrefab(PotionPrefabPath, PotionItemPath, ItemPickupColliderShape.Capsule);
        ConfigurePrefab(TorchPrefabPath, TorchItemPath, ItemPickupColliderShape.Box);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PrototypePickupPrefabConfigurator] Prototype item prefabs foram atualizados com o novo Item Authoring setup.");
    }

    private static void ConfigurePrefab(string prefabPath, string itemDefinitionPath, ItemPickupColliderShape colliderShape)
    {
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        ItemDefinition itemDefinition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemDefinitionPath);
        if (prefabAsset == null || itemDefinition == null)
        {
            Debug.LogWarning($"[PrototypePickupPrefabConfigurator] Prefab ou ItemDefinition nao encontrado: {prefabPath} / {itemDefinitionPath}");
            return;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            WorldPickupItem pickupItem = prefabRoot.GetComponent<WorldPickupItem>();
            if (pickupItem == null)
                pickupItem = prefabRoot.AddComponent<WorldPickupItem>();

            pickupItem.SetItemDefinition(itemDefinition);
            InventoryItemAuthoringUtility.EnsureRigidbody(prefabRoot);
            InventoryItemAuthoringUtility.EnsureGripPoints(pickupItem);
            InventoryItemAuthoringUtility.RebuildPickupColliderFromRenderers(pickupItem, colliderShape);
            InventoryItemAuthoringUtility.EnsureDropCollisionFromPickup(pickupItem);

            Outline outline = prefabRoot.GetComponent<Outline>();
            if (outline == null)
                outline = prefabRoot.AddComponent<Outline>();

            outline.OutlineMode = Outline.Mode.OutlineVisible;
            outline.OutlineColor = new Color(1f, 0.82f, 0.2f, 1f);
            outline.OutlineWidth = colliderShape == ItemPickupColliderShape.Box && prefabRoot.name == "Sword" ? 6f : 5f;
            outline.enabled = false;

            EditorUtility.SetDirty(prefabRoot);
            EditorUtility.SetDirty(pickupItem);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
