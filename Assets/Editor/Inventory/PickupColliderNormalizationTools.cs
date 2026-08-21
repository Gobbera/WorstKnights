using UnityEditor;
using UnityEngine;

public static class PickupColliderNormalizationTools
{
    [MenuItem("Tools/Inventory/Rebuild Pickup Colliders From Visuals")]
    public static void RebuildPickupCollidersFromVisuals()
    {
        string[] searchRoots = { "Assets/Prefabs/Items", "Assets/Resources" };
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", searchRoots);
        int updatedPrefabCount = 0;
        int updatedPickupCount = 0;

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            bool prefabDirty = false;

            try
            {
                WorldPickupItem[] pickupItems = prefabRoot.GetComponentsInChildren<WorldPickupItem>(true);
                for (int pickupIndex = 0; pickupIndex < pickupItems.Length; pickupIndex++)
                {
                    WorldPickupItem pickupItem = pickupItems[pickupIndex];
                    if (pickupItem == null
                        || !InventoryItemAuthoringUtility.RebuildPickupColliderFromRenderers(pickupItem, ItemPickupColliderShape.Box))
                    {
                        continue;
                    }

                    InventoryItemAuthoringUtility.EnsureDropCollisionFromPickup(pickupItem);
                    InventoryItemAuthoringUtility.EnsureRigidbody(pickupItem.gameObject);
                    prefabDirty = true;
                    updatedPickupCount++;
                    EditorUtility.SetDirty(pickupItem);
                }

                if (!prefabDirty)
                    continue;

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                updatedPrefabCount++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[PickupColliderNormalizationTools] {updatedPickupCount} pickup(s) atualizados em {updatedPrefabCount} prefab(s).");
    }
}
