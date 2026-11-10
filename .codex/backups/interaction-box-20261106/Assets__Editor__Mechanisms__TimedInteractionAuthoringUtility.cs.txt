using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TimedInteractionAuthoringUtility
{
    public const string VariantPath = "Assets/Prefabs/Mechanisms/Triggers/Levers/Lever_Interaction_01.prefab";
    [MenuItem("Tools/Mechanisms/Create Timed Interaction Lever Variant")]
    public static void Prepare()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(InteractionTriggerAuthoringUtility.LeverPath);
            var preview = EditorSceneManager.NewPreviewScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
            try
            {
                instance.name = "Lever_Interaction_01";
                var data = new SerializedObject(instance.GetComponent<InteractionTriggerInteractable>());
                data.FindProperty("interactionDistance").floatValue = 1.5f;
                data.FindProperty("lockPlayerControls").boolValue = true;
                data.FindProperty("controlLockDuration").floatValue = 1f;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
            }
            finally { Object.DestroyImmediate(instance); EditorSceneManager.ClosePreviewScene(preview); }
        }
        const string playerPath = "Assets/Resources/Player.prefab";
        var player = PrefabUtility.LoadPrefabContents(playerPath);
        try
        {
            if (player.GetComponent<PlayerInteractionControl>() != null) return;
            player.AddComponent<PlayerInteractionControl>();
            PrefabUtility.SaveAsPrefabAsset(player, playerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
    }
}
