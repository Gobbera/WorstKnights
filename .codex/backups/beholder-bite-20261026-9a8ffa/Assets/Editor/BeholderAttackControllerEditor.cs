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
                EditorGUILayout.HelpBox($"Ataque {i}: {issue}", MessageType.Warning);
            var settings = attack.FindPropertyRelative("settings").objectReferenceValue as BeholderAttackDefinition;
            if (issue == null && settings != null)
            {
                foreach (string boneName in new[] { settings.originName, settings.tentacleRootName })
                    if (!System.Array.Exists(component.GetComponentsInChildren<Transform>(true), bone => bone.name == boneName))
                        EditorGUILayout.HelpBox($"Ataque {i}: não foi encontrado o osso/olho '{boneName}' nesta instância.", MessageType.Warning);
            }
        }
    }

    public static string GetConfigurationIssue(SerializedProperty attack, AnimatorController controller)
    {
        return GetConfigurationIssue(attack.FindPropertyRelative("settings").objectReferenceValue as BeholderAttackDefinition, controller);
    }

    public static string GetConfigurationIssue(BeholderAttackDefinition settings, AnimatorController controller)
    {
        if (settings == null) return "Atribua um asset em Settings para definir a execução do ataque.";
        var clip = settings.animation;
        var mask = settings.mask;
        if (clip == null) return "Atribua Animation para permitir o início do ataque.";
        if (mask == null) return "Atribua Mask para identificar a camada do ataque.";
        if (controller == null) return "O Animator não possui um Animator Controller válido.";
        string layerName = mask.name.Replace('.', '_');
        AnimatorControllerLayer layer = System.Array.Find(controller.layers, l => l.name == layerName);
        if (layer == null) return $"A camada '{layerName}' não existe no Animator Controller.";
        string stateName = settings.animatorState;
        AnimatorState state = FindState(layer.stateMachine, stateName);
        if (state == null) return $"O estado '{layerName}.{stateName}' não existe. Confira Animator State.";
        if (state.motion != clip) return $"O Motion de '{layerName}.{stateName}' deve usar o clip '{clip.name}'.";
        if (layer.avatarMask != mask) return $"A camada '{layerName}' deve usar a máscara '{mask.name}'.";
        if (!settings.HasPresentation) return "Atribua o prefab de VFX no asset do ataque.";
        if (settings.HoldAnimation && !System.Array.Exists(controller.parameters,
            p => p.name == settings.SpeedParameter && p.type == AnimatorControllerParameterType.Float))
            return $"Falta o parâmetro Float '{settings.SpeedParameter}' para sustentar a pose deste tentáculo.";
        if (settings.HoldAnimation && (!state.speedParameterActive || state.speedParameter != settings.SpeedParameter))
            return $"A velocidade do estado '{layerName}.{stateName}' deve usar '{settings.SpeedParameter}'.";
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
