using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LightningStrikeController))]
public sealed class LightningStrikeControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Origin = ponto A. Aim Target e opcional; sem alvo, dispara pelo eixo azul (Z). "
            + "O raio termina no primeiro collider do Hit Mask. Owner Root ignora quem dispara.", MessageType.Info);
        if (GUILayout.Button("Disparar (Raycast)"))
            ((LightningStrikeController)target).Fire();
    }
}
