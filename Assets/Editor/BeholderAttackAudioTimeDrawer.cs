using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(BeholderAttackController.AudioTimeAttribute))]
public sealed class BeholderAttackAudioTimeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        int dot = property.propertyPath.LastIndexOf('.');
        string prefix = dot < 0 ? string.Empty : property.propertyPath.Substring(0, dot + 1);
        var animation = property.serializedObject.FindProperty(prefix + "animation")?.objectReferenceValue as AnimationClip;
        float duration = animation != null ? animation.length : 0f;
        using (new EditorGUI.DisabledScope(animation == null))
            EditorGUI.Slider(position, property, 0f, duration, label);
    }
}
