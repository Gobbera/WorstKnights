using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FlamethrowerTrapController))]
public sealed class FlamethrowerTrapControllerEditor : Editor
{
    private void OnEnable() => EditorApplication.update += RefreshPreview;
    private void OnDisable() => EditorApplication.update -= RefreshPreview;
    private void RefreshPreview()
    {
        if (Application.isPlaying || target == null) return;
        var trap = (FlamethrowerTrapController)target;
        if (trap.Phase == FlamethrowerTrapController.TrapPhase.Idle && trap.ActiveSurfacePatchCount == 0) return;
        trap.RefreshTrap();
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
        Repaint();
    }
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var trap = (FlamethrowerTrapController)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Estado", trap.Phase.ToString());
        if (!trap.HasCompatibleEffect())
            EditorGUILayout.HelpBox("Prepare o prefab pelo menu Tools > Hazards > Prepare Dragon Flamethrower Trap.", MessageType.Warning);
        EditorGUILayout.HelpBox("A previa fora do Play Mode mostra o fogo sem causar dano ou tocar audio. Azul = alcance; laranja = contato com o cenario.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Disparar")) trap.TriggerTrap();
            if (GUILayout.Button("Parar")) trap.StopTrap();
        }
    }
    [DrawGizmo(GizmoType.Selected)]
    static void DrawJet(FlamethrowerTrapController trap, GizmoType gizmoType)
    {
        var data = new SerializedObject(trap);
        Transform pivot = (Transform)data.FindProperty("flamethrowerPivot").objectReferenceValue;
        if (pivot == null) return;
        Vector3 origin = trap.IsEmitting ? trap.JetOrigin : pivot.TransformPoint(data.FindProperty("localPositionOffset").vector3Value);
        Quaternion rotation = trap.IsEmitting ? trap.JetRotation : pivot.rotation * Quaternion.Euler(data.FindProperty("localEulerAngles").vector3Value);
        float length = trap.IsEmitting ? trap.ResolvedLength : trap.Range;
        float start = data.FindProperty("startRadius").floatValue;
        float end = Mathf.Lerp(start, data.FindProperty("endRadius").floatValue, length / trap.Range);
        Gizmos.color = trap.IsEmitting && length < trap.Range - 0.01f ? new Color(1f, 0.4f, 0f, 0.8f) : Color.cyan;
        Vector3 endCenter = origin + rotation * Vector3.forward * length;
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f, b = (i + 1) * Mathf.PI * 2f / 24f;
            Vector3 radialA = rotation * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Vector3 radialB = rotation * new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f);
            Gizmos.DrawLine(origin + radialA * start, origin + radialB * start);
            Gizmos.DrawLine(endCenter + radialA * end, endCenter + radialB * end);
            if (i % 6 == 0) Gizmos.DrawLine(origin + radialA * start, endCenter + radialA * end);
        }
        if (trap.IsEmitting)
            Gizmos.DrawWireSphere(origin + trap.JetDirection * trap.DamageLength, 0.1f);
        if (trap.IsEmitting && trap.HasSurfaceContact && data.FindProperty("surfaceSpreadEnabled").boolValue)
        {
            Color previous = Handles.color;
            Handles.color = new Color(1f, 0.35f, 0.05f, 0.8f);
            Handles.DrawWireDisc(trap.SurfaceContact.point + trap.SurfaceContact.normal * 0.06f,
                trap.SurfaceContact.normal, data.FindProperty("surfaceSpreadRadius").floatValue);
            Handles.color = previous;
        }
    }
}
