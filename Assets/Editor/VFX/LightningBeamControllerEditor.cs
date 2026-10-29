using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LightningBeamController))]
public sealed class LightningBeamControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var beam = (LightningBeamController)target;
        EditorGUILayout.HelpBox("Duration = 0 mantém o raio até parar. Origin e Aim Target podem se mover; o primeiro collider limita o raio.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Iniciar raio")) beam.StartBeam();
            if (GUILayout.Button("Parar raio")) beam.StopBeam();
        }
        if (beam.IsPlaying && !Application.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
    }

    public override bool RequiresConstantRepaint() { return target != null && ((LightningBeamController)target).IsPlaying; }
}
