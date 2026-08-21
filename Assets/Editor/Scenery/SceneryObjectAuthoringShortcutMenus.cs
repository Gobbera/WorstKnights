using UnityEditor;
using UnityEngine;

public static class SceneryObjectAuthoringShortcutMenus
{
    private const string AssetMenuPath = "Assets/Open Object Authoring Tool";
    private const string GameObjectMenuPath = "GameObject/World/Open Object Authoring Tool";
    private const string WorldObjectContextMenuPath = "CONTEXT/WorldObject/Open Object Authoring Tool";

    [MenuItem(AssetMenuPath, false, 2000)]
    private static void OpenFromProjectPrefab()
    {
        GameObject prefabAsset = ResolveSelectedPrefabAsset();
        if (prefabAsset != null)
            SceneryObjectAuthoringToolWindow.OpenForTarget(prefabAsset);
    }

    [MenuItem(AssetMenuPath, true)]
    private static bool ValidateOpenFromProjectPrefab()
    {
        return ResolveSelectedPrefabAsset() != null;
    }

    [MenuItem(GameObjectMenuPath, false, 49)]
    private static void OpenFromHierarchySelection(MenuCommand command)
    {
        GameObject targetObject = command.context as GameObject;
        if (targetObject == null)
            targetObject = Selection.activeGameObject;

        SceneryObjectAuthoringToolWindow.OpenForTarget(targetObject);
    }

    [MenuItem(GameObjectMenuPath, true)]
    private static bool ValidateOpenFromHierarchySelection()
    {
        return ResolveSelectedPrefabAsset() != null;
    }

    [MenuItem(WorldObjectContextMenuPath)]
    private static void OpenFromWorldObjectContext(MenuCommand command)
    {
        WorldObject worldObject = command.context as WorldObject;
        if (worldObject != null)
            SceneryObjectAuthoringToolWindow.OpenForTarget(worldObject.gameObject);
    }

    private static GameObject ResolveSelectedPrefabAsset()
    {
        if (Selection.activeGameObject != null)
        {
            GameObject prefabAsset = SceneryObjectAuthoringUtility.ResolvePrefabAsset(Selection.activeGameObject);
            if (prefabAsset != null)
                return prefabAsset;
        }

        GameObject selectedObject = Selection.activeObject as GameObject;
        return SceneryObjectAuthoringUtility.ResolvePrefabAsset(selectedObject);
    }
}
