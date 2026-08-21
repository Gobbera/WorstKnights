using UnityEditor;
using UnityEngine;

public static class WallLayerTools
{
    private const string WallLayerName = "Wall";
    private const string AssignMenuPath = "Tools/Rendering/Assign Wall Layer To Selection";

    [MenuItem(AssignMenuPath, priority = 1200)]
    private static void AssignSelectedObjectsToWallBypassLayer()
    {
        int layer = LayerMask.NameToLayer(WallLayerName);
        if (layer < 0)
        {
            Debug.LogWarning($"Layer '{WallLayerName}' nao existe em Project Settings > Tags and Layers.");
            return;
        }

        GameObject[] selectedObjects = Selection.gameObjects;
        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selectedObject = selectedObjects[i];
            if (selectedObject == null)
                continue;

            Undo.RegisterFullObjectHierarchyUndo(selectedObject, "Assign Wall Layer");
            SetLayerRecursively(selectedObject.transform, layer);
            EditorUtility.SetDirty(selectedObject);
        }
    }

    [MenuItem(AssignMenuPath, validate = true)]
    private static bool ValidateAssignSelectedObjectsToWallBypassLayer()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        EditorUtility.SetDirty(root.gameObject);
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }
}
