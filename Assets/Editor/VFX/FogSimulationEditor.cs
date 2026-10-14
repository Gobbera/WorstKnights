using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(FogSimulation))]
public sealed class FogSimulationEditor : Editor
{
    private const string ComputePath = "Assets/Fog/FogSim.compute";
    private const string MaterialPath = "Assets/Fog/FogVolume.mat";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Configure References"))
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is FogSimulation simulation)
                    ConfigureSimulation(simulation);
            }
        }

        if (GUILayout.Button("Refresh Obstacle Sources"))
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is FogSimulation simulation)
                    simulation.RefreshObstacleSources();
            }
        }
    }

    [MenuItem("GameObject/VFX/Interactive Fog Volume", false, 11)]
    private static void CreateInteractiveFogVolume(MenuCommand command)
    {
        GameObject fogVolume = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(fogVolume, "Create Interactive Fog Volume");
        fogVolume.name = "Interactive Fog Volume";
        GameObjectUtility.SetParentAndAlign(fogVolume, command.context as GameObject);
        fogVolume.transform.localScale = new Vector3(30f, 2.5f, 30f);

        Collider collider = fogVolume.GetComponent<Collider>();
        if (collider != null)
            Undo.DestroyObjectImmediate(collider);

        MeshRenderer renderer = fogVolume.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        FogSimulation simulation = Undo.AddComponent<FogSimulation>(fogVolume);
        ConfigureSimulation(simulation);

        Selection.activeGameObject = fogVolume;
    }

    private static void ConfigureSimulation(FogSimulation simulation)
    {
        if (simulation == null)
            return;

        Undo.RecordObject(simulation, "Configure Fog Simulation");
        SerializedObject serializedSimulation = new SerializedObject(simulation);

        Renderer renderer = simulation.GetComponent<Renderer>();
        Material material = renderer != null
            ? renderer.sharedMaterial
            : AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (renderer != null)
        {
            Undo.RecordObject(renderer, "Configure Fog Renderer");
            if (material == null)
                material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            EditorUtility.SetDirty(renderer);
        }

        serializedSimulation.FindProperty("compute").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
        serializedSimulation.FindProperty("fogMaterial").objectReferenceValue = material;
        serializedSimulation.FindProperty("fogRenderer").objectReferenceValue = renderer;
        serializedSimulation.ApplyModifiedProperties();

        EditorUtility.SetDirty(simulation);
    }
}
