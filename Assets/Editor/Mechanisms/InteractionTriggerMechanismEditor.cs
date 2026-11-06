using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(InteractionTriggerMechanism))]
public sealed class InteractionTriggerMechanismEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        Field("mechanismType", "Mechanism Type");
        Field("movingPart", "Moving Part");
        var type = (InteractionTriggerType)serializedObject.FindProperty("mechanismType").enumValueIndex;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Movement", EditorStyles.boldLabel);
        if (type == InteractionTriggerType.Lever)
        {
            Field("rotationAxis", "Rotation Axis");
            Field("leverAngle", "Activation Angle");
            Field("rotationSpeed", "Angular Speed");
        }
        else
        {
            Field("pressDistance", "Press Distance");
            Field("movementSpeed", "Movement Speed");
            if (type == InteractionTriggerType.Button) Field("localPressDirection", "Local Press Direction");
            else Field("pressureSurface", "Pressure Surface");
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);
        Field("activationAudioCue", "Activation Audio Cue");
        Field("deactivationAudioCue", "Deactivation Audio Cue");
        Field("audioSource", "Audio Source");
        EditorGUILayout.HelpBox("Movement and audio belong to this mechanism prefab. Edit the prefab to share settings across its instances, or override an instance for a specific placement.", MessageType.Info);
        serializedObject.ApplyModifiedProperties();
    }
    private void Field(string name, string label)
    {
        var property = serializedObject.FindProperty(name);
        EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), true);
    }
}
