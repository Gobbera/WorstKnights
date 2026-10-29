using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(BeholderAttackController.AudioTimeAttribute))]
public sealed class BeholderAttackAudioTimeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        string parentPath = property.propertyPath.Substring(0, property.propertyPath.LastIndexOf('.'));
        var animation = property.serializedObject.FindProperty(parentPath + ".animation")?.objectReferenceValue as AnimationClip;
        float duration = animation != null ? animation.length : 0f;
        using (new EditorGUI.DisabledScope(animation == null))
            EditorGUI.Slider(position, property, 0f, duration, label);
    }
}
