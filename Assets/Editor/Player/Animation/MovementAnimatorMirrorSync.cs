#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class MovementAnimatorMirrorSync
{
    private const string ControllerPath = "Assets/Skecth/KnightAnimController.controller";
    private const string MirrorAssetPath = "Assets/Resources/MovementAnimatorMirror.asset";
    private const string DocumentationPath = "Docs/MovementAnimatorReference.md";

    private static bool isSyncing;

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.delayCall += () => TrySync(logResult: false);
    }

    [MenuItem("Tools/Animation/Sync Movement Animator Mirror")]
    public static void SyncFromMenu()
    {
        TrySync(logResult: true);
    }

    internal static void TrySync(bool logResult)
    {
        if (isSyncing)
            return;

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            if (logResult)
                Debug.LogWarning($"MovementAnimatorMirrorSync: controller not found at '{ControllerPath}'.");

            return;
        }

        isSyncing = true;

        try
        {
            EnsureParentDirectory(MirrorAssetPath);
            EnsureParentDirectory(DocumentationPath);

            MovementAnimatorMirror mirror = LoadOrCreateMirrorAsset();
            List<MovementAnimatorParameterSnapshot> parameters = BuildParameterSnapshots(controller.parameters);
            List<MovementAnimatorStateSnapshot> states = BuildStateSnapshots(controller);
            List<MovementAnimatorBinding> bindings = BuildBindings(controller.parameters);
            string generatedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

            mirror.EditorOverwrite(controller, ControllerPath, generatedUtc, bindings, parameters, states);
            EditorUtility.SetDirty(mirror);

            string markdown = BuildMarkdown(controller, generatedUtc, bindings, parameters, states);
            File.WriteAllText(DocumentationPath, markdown, new UTF8Encoding(false));

            AssetDatabase.SaveAssets();

            if (logResult)
                Debug.Log($"MovementAnimatorMirrorSync: synced '{ControllerPath}' to '{MirrorAssetPath}' and '{DocumentationPath}'.");
        }
        finally
        {
            isSyncing = false;
        }
    }

    private static MovementAnimatorMirror LoadOrCreateMirrorAsset()
    {
        MovementAnimatorMirror mirror = AssetDatabase.LoadAssetAtPath<MovementAnimatorMirror>(MirrorAssetPath);
        if (mirror != null)
            return mirror;

        mirror = ScriptableObject.CreateInstance<MovementAnimatorMirror>();
        AssetDatabase.CreateAsset(mirror, MirrorAssetPath);
        return mirror;
    }

    private static void EnsureParentDirectory(string path)
    {
        string parentDirectory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
            Directory.CreateDirectory(parentDirectory);
    }

    private static List<MovementAnimatorParameterSnapshot> BuildParameterSnapshots(AnimatorControllerParameter[] parameters)
    {
        List<MovementAnimatorParameterSnapshot> snapshots = new List<MovementAnimatorParameterSnapshot>(parameters.Length);

        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            snapshots.Add(new MovementAnimatorParameterSnapshot
            {
                name = parameter.name,
                parameterType = parameter.type,
                defaultFloat = parameter.defaultFloat,
                defaultInt = parameter.defaultInt,
                defaultBool = parameter.defaultBool
            });
        }

        return snapshots;
    }

    private static List<MovementAnimatorStateSnapshot> BuildStateSnapshots(AnimatorController controller)
    {
        List<MovementAnimatorStateSnapshot> snapshots = new List<MovementAnimatorStateSnapshot>();

        for (int i = 0; i < controller.layers.Length; i++)
        {
            AnimatorControllerLayer layer = controller.layers[i];
            CollectStatesRecursive(layer.name, string.Empty, layer.stateMachine, layer.stateMachine.defaultState, snapshots);
        }

        return snapshots;
    }

    private static void CollectStatesRecursive(
        string layerName,
        string parentPath,
        AnimatorStateMachine stateMachine,
        AnimatorState defaultState,
        List<MovementAnimatorStateSnapshot> snapshots)
    {
        ChildAnimatorState[] childStates = stateMachine.states;
        for (int i = 0; i < childStates.Length; i++)
        {
            AnimatorState state = childStates[i].state;
            string statePath = string.IsNullOrWhiteSpace(parentPath) ? state.name : $"{parentPath}/{state.name}";

            snapshots.Add(new MovementAnimatorStateSnapshot
            {
                layerName = layerName,
                statePath = statePath,
                motionName = GetMotionName(state.motion),
                isDefaultState = state == defaultState,
                isBlendTree = state.motion is BlendTree,
                transitions = BuildTransitionSnapshots(state.transitions)
            });
        }

        ChildAnimatorStateMachine[] childMachines = stateMachine.stateMachines;
        for (int i = 0; i < childMachines.Length; i++)
        {
            AnimatorStateMachine childMachine = childMachines[i].stateMachine;
            string nextParentPath = string.IsNullOrWhiteSpace(parentPath) ? childMachine.name : $"{parentPath}/{childMachine.name}";
            CollectStatesRecursive(layerName, nextParentPath, childMachine, childMachine.defaultState, snapshots);
        }
    }

    private static List<MovementAnimatorTransitionSnapshot> BuildTransitionSnapshots(AnimatorStateTransition[] transitions)
    {
        List<MovementAnimatorTransitionSnapshot> snapshots = new List<MovementAnimatorTransitionSnapshot>(transitions.Length);

        for (int i = 0; i < transitions.Length; i++)
        {
            AnimatorStateTransition transition = transitions[i];
            MovementAnimatorTransitionSnapshot snapshot = new MovementAnimatorTransitionSnapshot
            {
                destination = GetTransitionDestination(transition),
                hasExitTime = transition.hasExitTime,
                exitTime = transition.exitTime,
                duration = transition.duration,
                conditions = BuildConditionSnapshots(transition.conditions)
            };

            snapshots.Add(snapshot);
        }

        return snapshots;
    }

    private static List<MovementAnimatorConditionSnapshot> BuildConditionSnapshots(AnimatorCondition[] conditions)
    {
        List<MovementAnimatorConditionSnapshot> snapshots = new List<MovementAnimatorConditionSnapshot>(conditions.Length);

        for (int i = 0; i < conditions.Length; i++)
        {
            AnimatorCondition condition = conditions[i];
            snapshots.Add(new MovementAnimatorConditionSnapshot
            {
                parameterName = condition.parameter,
                mode = condition.mode.ToString(),
                threshold = condition.threshold
            });
        }

        return snapshots;
    }

    private static List<MovementAnimatorBinding> BuildBindings(AnimatorControllerParameter[] parameters)
    {
        List<MovementAnimatorBinding> bindings = new List<MovementAnimatorBinding>();

        AddBinding(bindings, MovementAnimatorSemantic.Horizontal, AnimatorControllerParameterType.Float, parameters, "Horizontal", "MoveX", "InputX");
        AddBinding(bindings, MovementAnimatorSemantic.Vertical, AnimatorControllerParameterType.Float, parameters, "Vertical", "MoveY", "InputY");
        AddBinding(bindings, MovementAnimatorSemantic.IsGrounded, AnimatorControllerParameterType.Bool, parameters, "IsGrounded", "Grounded");
        AddBinding(bindings, MovementAnimatorSemantic.IsCrouching, AnimatorControllerParameterType.Bool, parameters, "IsCrouching", "Crouching");
        AddBinding(bindings, MovementAnimatorSemantic.IsSprinting, AnimatorControllerParameterType.Bool, parameters, "IsSprinting", "Sprinting");
        AddBinding(bindings, MovementAnimatorSemantic.IsJumping, AnimatorControllerParameterType.Bool, parameters, "IsJumping", "Jumping");
        AddBinding(bindings, MovementAnimatorSemantic.IsFalling, AnimatorControllerParameterType.Bool, parameters, "IsFalling", "Falling");
        AddBinding(bindings, MovementAnimatorSemantic.MovementMagnitude, AnimatorControllerParameterType.Float, parameters, "MovementMagnitude", "MoveMagnitude", "Speed");
        AddBinding(bindings, MovementAnimatorSemantic.IsMoving, AnimatorControllerParameterType.Bool, parameters, "IsMoving", "Moving");
        AddBinding(bindings, MovementAnimatorSemantic.SpeedMultiplier, AnimatorControllerParameterType.Float, parameters, "SpeedMultiplier", "MoveScale", "LocomotionScale");
        AddBinding(bindings, MovementAnimatorSemantic.VerticalSpeed, AnimatorControllerParameterType.Float, parameters, "VerticalSpeed", "YVelocity", "FallSpeed");
        AddBinding(bindings, MovementAnimatorSemantic.CrouchEnterTrigger, AnimatorControllerParameterType.Trigger, parameters, "CrouchEnter", "EnterCrouch", "CrouchDown", "CrouchStart");
        AddBinding(bindings, MovementAnimatorSemantic.CrouchExitTrigger, AnimatorControllerParameterType.Trigger, parameters, "CrouchExit", "ExitCrouch", "CrouchUp", "StandUp", "CrouchStandUp");
        AddBinding(bindings, MovementAnimatorSemantic.JumpTrigger, AnimatorControllerParameterType.Trigger, parameters, "Jump");
        AddBinding(bindings, MovementAnimatorSemantic.AttackTrigger, AnimatorControllerParameterType.Trigger, parameters, "Attack", "AttackTrigger");
        AddBinding(bindings, MovementAnimatorSemantic.KickTrigger, AnimatorControllerParameterType.Trigger, parameters, "Kick", "KickTrigger");
        AddBinding(bindings, MovementAnimatorSemantic.LandTrigger, AnimatorControllerParameterType.Trigger, parameters, "Land", "Landing");
        AddBinding(bindings, MovementAnimatorSemantic.IdleTurnLeftTrigger, AnimatorControllerParameterType.Trigger, parameters, "IdleTurnLeft", "TurnLeft", "IdleStepLeft");
        AddBinding(bindings, MovementAnimatorSemantic.IdleTurnRightTrigger, AnimatorControllerParameterType.Trigger, parameters, "IdleTurnRight", "TurnRight", "IdleStepRight");
        AddBinding(bindings, MovementAnimatorSemantic.RightHandOccupied, AnimatorControllerParameterType.Bool, parameters, "IsRightHandOccupied", "RightHandOccupied");
        AddBinding(bindings, MovementAnimatorSemantic.LeftHandOccupied, AnimatorControllerParameterType.Bool, parameters, "IsLeftHandOccupied", "LeftHandOccupied");
        AddBinding(bindings, MovementAnimatorSemantic.ThumbsUpTrigger, AnimatorControllerParameterType.Trigger, parameters, "ThumbsUp", "ThumbsUpTrigger");

        return bindings;
    }

    private static void AddBinding(
        List<MovementAnimatorBinding> bindings,
        MovementAnimatorSemantic semantic,
        AnimatorControllerParameterType expectedType,
        AnimatorControllerParameter[] parameters,
        params string[] aliases)
    {
        MovementAnimatorBinding binding = new MovementAnimatorBinding
        {
            semantic = semantic,
            parameterType = expectedType,
            existsInController = false,
            parameterName = aliases.Length > 0 ? aliases[0] : string.Empty
        };

        for (int i = 0; i < aliases.Length; i++)
        {
            string alias = aliases[i];
            for (int j = 0; j < parameters.Length; j++)
            {
                AnimatorControllerParameter parameter = parameters[j];
                if (parameter.type != expectedType)
                    continue;

                if (!string.Equals(parameter.name, alias, StringComparison.OrdinalIgnoreCase))
                    continue;

                binding.parameterName = parameter.name;
                binding.existsInController = true;
                bindings.Add(binding);
                return;
            }
        }

        bindings.Add(binding);
    }

    private static string BuildMarkdown(
        AnimatorController controller,
        string generatedUtc,
        List<MovementAnimatorBinding> bindings,
        List<MovementAnimatorParameterSnapshot> parameters,
        List<MovementAnimatorStateSnapshot> states)
    {
        StringBuilder builder = new StringBuilder(4096);

        builder.AppendLine("# Movement Animator Reference");
        builder.AppendLine();
        builder.AppendLine($"- Controller: `{ControllerPath}`");
        builder.AppendLine($"- Generated: `{generatedUtc}`");
        builder.AppendLine($"- Controller name: `{controller.name}`");
        builder.AppendLine();

        builder.AppendLine("## Saved Snapshot");
        builder.AppendLine();

        bool hasLandingState = ContainsState(states, "Landing");
        bool hasFallingState = ContainsState(states, "Falling");
        bool hasLandTrigger = ContainsBinding(bindings, MovementAnimatorSemantic.LandTrigger);
        bool hasFallingBool = ContainsBinding(bindings, MovementAnimatorSemantic.IsFalling);

        builder.AppendLine($"- `Landing` state in saved controller: {(hasLandingState ? "yes" : "no")}");
        builder.AppendLine($"- `Falling` state in saved controller: {(hasFallingState ? "yes" : "no")}");
        builder.AppendLine($"- `Land` trigger in saved controller: {(hasLandTrigger ? "yes" : "no")}");
        builder.AppendLine($"- `IsFalling`/`Falling` bool in saved controller: {(hasFallingBool ? "yes" : "no")}");
        builder.AppendLine();

        builder.AppendLine("## Runtime Parameters Driven By Code");
        builder.AppendLine();
        builder.AppendLine("| Semantic | Expected Type | Bound Parameter | In Controller | Runtime Source |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");

        for (int i = 0; i < bindings.Count; i++)
        {
            MovementAnimatorBinding binding = bindings[i];
            builder.AppendLine($"| `{binding.semantic}` | `{binding.parameterType}` | `{binding.parameterName}` | `{(binding.existsInController ? "Yes" : "No")}` | {GetSemanticDescription(binding.semantic)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Controller Parameters");
        builder.AppendLine();
        builder.AppendLine("| Name | Type | Default |");
        builder.AppendLine("| --- | --- | --- |");

        for (int i = 0; i < parameters.Count; i++)
        {
            MovementAnimatorParameterSnapshot parameter = parameters[i];
            builder.AppendLine($"| `{parameter.name}` | `{parameter.parameterType}` | `{GetDefaultValue(parameter)}` |");
        }

        builder.AppendLine();
        builder.AppendLine("## States");
        builder.AppendLine();

        for (int i = 0; i < states.Count; i++)
        {
            MovementAnimatorStateSnapshot state = states[i];
            builder.AppendLine($"### `{state.statePath}`");
            builder.AppendLine();
            builder.AppendLine($"- Layer: `{state.layerName}`");
            builder.AppendLine($"- Default state: `{(state.isDefaultState ? "Yes" : "No")}`");
            builder.AppendLine($"- Motion: `{state.motionName}`");
            builder.AppendLine($"- Blend Tree: `{(state.isBlendTree ? "Yes" : "No")}`");
            builder.AppendLine("- Transitions:");

            if (state.transitions == null || state.transitions.Count == 0)
            {
                builder.AppendLine("  - None");
            }
            else
            {
                for (int j = 0; j < state.transitions.Count; j++)
                {
                    MovementAnimatorTransitionSnapshot transition = state.transitions[j];
                    builder.AppendLine($"  - To `{transition.destination}` | Exit Time: `{(transition.hasExitTime ? transition.exitTime.ToString("0.###") : "Off")}` | Duration: `{transition.duration:0.###}`");

                    if (transition.conditions == null || transition.conditions.Count == 0)
                    {
                        builder.AppendLine("    - Conditions: none");
                        continue;
                    }

                    for (int k = 0; k < transition.conditions.Count; k++)
                    {
                        MovementAnimatorConditionSnapshot condition = transition.conditions[k];
                        builder.AppendLine($"    - `{condition.parameterName}` `{condition.mode}` `{condition.threshold:0.###}`");
                    }
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Notes");
        builder.AppendLine();
        builder.AppendLine("- `Jump` is triggered by code when a new jump request is accepted, including delayed jumps.");
        builder.AppendLine("- `Attack` is triggered by code when a new attack request is accepted and replicated.");
        builder.AppendLine("- `Land` is triggered by code when `IsGrounded` changes from false to true.");
        builder.AppendLine("- `ThumbsUp` is triggered by code when the local player presses the configured emote shortcut.");
        builder.AppendLine("- `CrouchEnter` and `CrouchExit` are optional triggers used when you want dedicated agachar/levantar clips before returning to locomotion.");
        builder.AppendLine("- `IsFalling` is driven automatically when the player is airborne and the vertical velocity is below `-0.1`.");
        builder.AppendLine("- `VerticalSpeed` is driven automatically if you add that float parameter to the Animator.");
        builder.AppendLine("- `IsRightHandOccupied` and `IsLeftHandOccupied` follow the active equipped slots so hand-pose layers can blend over locomotion.");
        builder.AppendLine("- This document is regenerated from the saved controller asset. If a state is missing here, save the Animator or confirm you edited the same controller used by the player prefab.");

        return builder.ToString();
    }

    private static bool ContainsState(List<MovementAnimatorStateSnapshot> states, string stateName)
    {
        for (int i = 0; i < states.Count; i++)
        {
            if (string.Equals(Path.GetFileName(states[i].statePath), stateName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(states[i].statePath, stateName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsBinding(List<MovementAnimatorBinding> bindings, MovementAnimatorSemantic semantic)
    {
        for (int i = 0; i < bindings.Count; i++)
        {
            if (bindings[i].semantic == semantic && bindings[i].existsInController)
                return true;
        }

        return false;
    }

    private static string GetSemanticDescription(MovementAnimatorSemantic semantic)
    {
        switch (semantic)
        {
            case MovementAnimatorSemantic.Horizontal:
                return "Local X input after wall and slope filtering. Crouch keeps the full -1..1 directional range for a dedicated crouch blend tree.";
            case MovementAnimatorSemantic.Vertical:
                return "Local Y input after wall and slope filtering. Crouch keeps the full -1..1 directional range for a dedicated crouch blend tree.";
            case MovementAnimatorSemantic.IsGrounded:
                return "True while the movement probe considers the player grounded.";
            case MovementAnimatorSemantic.IsCrouching:
                return "True while `CurrentState` is `crouching`.";
            case MovementAnimatorSemantic.IsSprinting:
                return "True while `CurrentState` is `sprinting`.";
            case MovementAnimatorSemantic.IsJumping:
                return "True while a jump is queued or the movement state is airborne.";
            case MovementAnimatorSemantic.IsFalling:
                return "True while airborne and vertical velocity is descending.";
            case MovementAnimatorSemantic.MovementMagnitude:
                return "Absolute locomotion blend magnitude.";
            case MovementAnimatorSemantic.IsMoving:
                return "True when locomotion magnitude is above 0.1.";
            case MovementAnimatorSemantic.SpeedMultiplier:
                return "Optional locomotion scale. Sprint uses 2 and crouch reports 0.5 for controllers that still need this value.";
            case MovementAnimatorSemantic.VerticalSpeed:
                return "Current Rigidbody/network vertical velocity.";
            case MovementAnimatorSemantic.CrouchEnterTrigger:
                return "Triggered once when the grounded movement state enters `crouching`.";
            case MovementAnimatorSemantic.CrouchExitTrigger:
                return "Triggered once when crouch is released and the character can stand up.";
            case MovementAnimatorSemantic.JumpTrigger:
                return "Triggered once per accepted jump request.";
            case MovementAnimatorSemantic.AttackTrigger:
                return "Fallback trigger for Attack_1; combo steps are played directly by code from the replicated combo step.";
            case MovementAnimatorSemantic.KickTrigger:
                return "Triggered once per accepted kick request and used to play the masked Kick layer.";
            case MovementAnimatorSemantic.LandTrigger:
                return "Triggered once on air-to-ground transition.";
            case MovementAnimatorSemantic.IdleTurnLeftTrigger:
                return "Triggered when the grounded character is idle and rotates left far enough in place.";
            case MovementAnimatorSemantic.IdleTurnRightTrigger:
                return "Triggered when the grounded character is idle and rotates right far enough in place.";
            case MovementAnimatorSemantic.RightHandOccupied:
                return "True while the active right-hand slot contains an item, so a grip layer can close that hand over other animations.";
            case MovementAnimatorSemantic.LeftHandOccupied:
                return "True while the active left-hand slot contains an item, so a grip layer can close that hand over other animations.";
            case MovementAnimatorSemantic.ThumbsUpTrigger:
                return "Triggered once when the player requests the thumbs-up emote.";
            default:
                return "Not documented.";
        }
    }

    private static string GetDefaultValue(MovementAnimatorParameterSnapshot parameter)
    {
        switch (parameter.parameterType)
        {
            case AnimatorControllerParameterType.Float:
                return parameter.defaultFloat.ToString("0.###");
            case AnimatorControllerParameterType.Int:
                return parameter.defaultInt.ToString();
            case AnimatorControllerParameterType.Bool:
                return parameter.defaultBool ? "true" : "false";
            case AnimatorControllerParameterType.Trigger:
                return "trigger";
            default:
                return "-";
        }
    }

    private static string GetMotionName(Motion motion)
    {
        if (motion == null)
            return "None";

        string assetPath = AssetDatabase.GetAssetPath(motion);
        if (!string.IsNullOrWhiteSpace(assetPath))
            return Path.GetFileNameWithoutExtension(assetPath);

        return motion.name;
    }

    private static string GetTransitionDestination(AnimatorStateTransition transition)
    {
        if (transition.isExit)
            return "Exit";

        if (transition.destinationState != null)
            return transition.destinationState.name;

        if (transition.destinationStateMachine != null)
            return transition.destinationStateMachine.name;

        return "None";
    }
}

internal sealed class MovementAnimatorMirrorAssetPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        for (int i = 0; i < importedAssets.Length; i++)
        {
            if (!string.Equals(importedAssets[i], "Assets/Skecth/KnightAnimController.controller", StringComparison.Ordinal))
                continue;

            MovementAnimatorMirrorSync.TrySync(logResult: false);
            return;
        }

        for (int i = 0; i < movedAssets.Length; i++)
        {
            if (!string.Equals(movedAssets[i], "Assets/Skecth/KnightAnimController.controller", StringComparison.Ordinal))
                continue;

            MovementAnimatorMirrorSync.TrySync(logResult: false);
            return;
        }
    }
}
#endif
