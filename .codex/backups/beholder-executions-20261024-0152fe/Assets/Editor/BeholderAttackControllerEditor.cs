using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[CustomEditor(typeof(BeholderAttackController))]
[CanEditMultipleObjects]
public sealed class BeholderAttackControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (targets.Length != 1) return;
        var component = (BeholderAttackController)target;
        var animator = serializedObject.FindProperty("animator").objectReferenceValue as Animator;
        if (animator == null) animator = component.GetComponentInChildren<Animator>(true);
        RuntimeAnimatorController runtime = animator != null ? animator.runtimeAnimatorController : null;
        while (runtime is AnimatorOverrideController overrides) runtime = overrides.runtimeAnimatorController;
        var controller = runtime as AnimatorController;
        SerializedProperty attacks = serializedObject.FindProperty("attacks");
        for (int i = 0; i < attacks.arraySize; i++)
        {
            SerializedProperty attack = attacks.GetArrayElementAtIndex(i);
            if (!attack.FindPropertyRelative("enabled").boolValue) continue;
            string issue = GetConfigurationIssue(attack, controller);
            if (issue != null)
                EditorGUILayout.HelpBox($"Ataque {i} ({attack.FindPropertyRelative("name").stringValue}): {issue}", MessageType.Warning);
        }
    }

    public static string GetConfigurationIssue(SerializedProperty attack, AnimatorController controller)
    {
        var clip = attack.FindPropertyRelative("animation").objectReferenceValue as AnimationClip;
        var mask = attack.FindPropertyRelative("mask").objectReferenceValue as AvatarMask;
        if (clip == null) return "Atribua Animation para permitir o início do ataque.";
        if (mask == null) return "Atribua Mask para identificar a camada do ataque.";
        if (controller == null) return "O Animator não possui um Animator Controller válido.";
        string layerName = mask.name.Replace('.', '_');
        AnimatorControllerLayer layer = System.Array.Find(controller.layers, l => l.name == layerName);
        if (layer == null) return $"A camada '{layerName}' não existe no Animator Controller.";
        string stateName = attack.FindPropertyRelative("animatorState").stringValue;
        AnimatorState state = FindState(layer.stateMachine, stateName);
        if (state == null) return $"O estado '{layerName}.{stateName}' não existe. Confira Animator State.";
        if (state.motion != clip) return $"O Motion de '{layerName}.{stateName}' deve usar o clip '{clip.name}'.";
        if (layer.avatarMask != mask) return $"A camada '{layerName}' deve usar a máscara '{mask.name}'.";
        return null;
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (ChildAnimatorState child in machine.states)
            if (child.state.name == path) return child.state;
        foreach (ChildAnimatorStateMachine child in machine.stateMachines)
        {
            string prefix = child.stateMachine.name + ".";
            if (path.StartsWith(prefix, System.StringComparison.Ordinal))
                return FindState(child.stateMachine, path.Substring(prefix.Length));
        }
        return null;
    }
}
