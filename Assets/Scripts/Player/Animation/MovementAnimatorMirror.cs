using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MovementAnimatorMirror", menuName = "Animation/Movement Animator Mirror")]
public sealed class MovementAnimatorMirror : ScriptableObject
{
    [SerializeField] private RuntimeAnimatorController controller;
    [SerializeField] private string controllerAssetPath;
    [SerializeField] private string lastGeneratedUtc;
    [SerializeField] private List<MovementAnimatorBinding> bindings = new List<MovementAnimatorBinding>();
    [SerializeField] private List<MovementAnimatorParameterSnapshot> parameters = new List<MovementAnimatorParameterSnapshot>();
    [SerializeField] private List<MovementAnimatorStateSnapshot> states = new List<MovementAnimatorStateSnapshot>();

    public RuntimeAnimatorController Controller => controller;
    public string ControllerAssetPath => controllerAssetPath;
    public string LastGeneratedUtc => lastGeneratedUtc;
    public IReadOnlyList<MovementAnimatorBinding> Bindings => bindings;
    public IReadOnlyList<MovementAnimatorParameterSnapshot> Parameters => parameters;
    public IReadOnlyList<MovementAnimatorStateSnapshot> States => states;

    public bool TryGetBindingName(MovementAnimatorSemantic semantic, out string parameterName)
    {
        for (int i = 0; i < bindings.Count; i++)
        {
            MovementAnimatorBinding binding = bindings[i];
            if (binding.semantic != semantic || !binding.existsInController || string.IsNullOrWhiteSpace(binding.parameterName))
                continue;

            parameterName = binding.parameterName;
            return true;
        }

        parameterName = string.Empty;
        return false;
    }

#if UNITY_EDITOR
    public void EditorOverwrite(
        RuntimeAnimatorController sourceController,
        string sourcePath,
        string generatedUtc,
        List<MovementAnimatorBinding> newBindings,
        List<MovementAnimatorParameterSnapshot> newParameters,
        List<MovementAnimatorStateSnapshot> newStates)
    {
        controller = sourceController;
        controllerAssetPath = sourcePath;
        lastGeneratedUtc = generatedUtc;
        bindings = newBindings ?? new List<MovementAnimatorBinding>();
        parameters = newParameters ?? new List<MovementAnimatorParameterSnapshot>();
        states = newStates ?? new List<MovementAnimatorStateSnapshot>();
    }
#endif
}

[System.Serializable]
public class MovementAnimatorBinding
{
    public MovementAnimatorSemantic semantic;
    public string parameterName;
    public AnimatorControllerParameterType parameterType;
    public bool existsInController;
}

[System.Serializable]
public class MovementAnimatorParameterSnapshot
{
    public string name;
    public AnimatorControllerParameterType parameterType;
    public float defaultFloat;
    public int defaultInt;
    public bool defaultBool;
}

[System.Serializable]
public class MovementAnimatorStateSnapshot
{
    public string layerName;
    public string statePath;
    public string motionName;
    public bool isDefaultState;
    public bool isBlendTree;
    public List<MovementAnimatorTransitionSnapshot> transitions = new List<MovementAnimatorTransitionSnapshot>();
}

[System.Serializable]
public class MovementAnimatorTransitionSnapshot
{
    public string destination;
    public bool hasExitTime;
    public float exitTime;
    public float duration;
    public List<MovementAnimatorConditionSnapshot> conditions = new List<MovementAnimatorConditionSnapshot>();
}

[System.Serializable]
public class MovementAnimatorConditionSnapshot
{
    public string parameterName;
    public string mode;
    public float threshold;
}
