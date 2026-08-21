using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WorldObject))]
public class WorldObjectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        WorldObject worldObject = (WorldObject)target;
        GameObject prefabAsset = SceneryObjectAuthoringUtility.ResolvePrefabAsset(worldObject.gameObject);
        if (GUILayout.Button("Abrir Object Authoring Tool"))
            SceneryObjectAuthoringToolWindow.OpenForTarget(worldObject.gameObject);

        if (prefabAsset == null)
            EditorGUILayout.HelpBox("Este objeto precisa ser um prefab asset ou uma instancia de prefab para abrir a edicao.", MessageType.Warning);
    }
}
