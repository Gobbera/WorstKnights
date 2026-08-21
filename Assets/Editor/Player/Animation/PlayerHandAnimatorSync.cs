#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerHandAnimatorSync
{
    private const string ControllerPath = "Assets/Skecth/KnightAnimController.controller";
    private const string PlayerPrefabPath = "Assets/Resources/Player.prefab";
    private const string ModelAssetPath = "Assets/Models/NewKnight/EditedKnight.fbx";
    private const string RightHandMaskPath = "Assets/Skecth/RightHandGrip.mask";
    private const string LeftHandMaskPath = "Assets/Skecth/LeftHandGrip.mask";
    private const string EmoteMaskPath = "Assets/Skecth/EmoteTumbsUp.mask";

    private const string RightHandSocketName = "RightHandSocket";
    private const string LeftHandSocketName = "LeftHandSocket";
    private const string RightGripClipName = "Right Hand Grip";
    private const string LeftGripClipName = "Left Hand Grip";
    private const string ThumbsUpClipName = "Emote_Tumb_Up";

    private const string RightGripLayerName = "Right Hand Grip";
    private const string LeftGripLayerName = "Left Hand Grip";
    private const string ThumbsUpLayerName = "Thumbs Up";

    private const string EmptyStateName = "Empty";
    private const string GripStateName = "Grip";
    private const string ThumbsUpStateName = "ThumbsUp";

    [MenuItem("Tools/Animation/Sync Player Hand Animator")]
    public static void SyncFromMenu()
    {
        TrySync(logResult: true);
    }

    public static void SyncFromCommandLine()
    {
        TrySync(logResult: true);
    }

    internal static void TrySync(bool logResult)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelAssetPath);

        if (controller == null || playerPrefab == null || modelPrefab == null)
        {
            if (logResult)
                Debug.LogWarning("[PlayerHandAnimatorSync] Controller, prefab do player, ou modelo base nao foi encontrado.");

            return;
        }

        if (!TryFindClip(ModelAssetPath, RightGripClipName, out AnimationClip rightGripClip)
            || !TryFindClip(ModelAssetPath, LeftGripClipName, out AnimationClip leftGripClip)
            || !TryFindClip(ModelAssetPath, ThumbsUpClipName, out AnimationClip thumbsUpClip))
        {
            if (logResult)
                Debug.LogWarning("[PlayerHandAnimatorSync] Um ou mais clips de grip/emote nao foram encontrados no EditedKnight.fbx.");

            return;
        }

        if (!TryResolveHandBonePaths(playerPrefab, out string rightHandPath, out string leftHandPath))
        {
            if (logResult)
                Debug.LogWarning("[PlayerHandAnimatorSync] Nao foi possivel resolver os ossos de mao a partir do Player.prefab.");

            return;
        }

        bool changed = false;

        AvatarMask rightHandMask = LoadOrCreateMask(RightHandMaskPath, ref changed);
        AvatarMask leftHandMask = LoadOrCreateMask(LeftHandMaskPath, ref changed);
        AvatarMask emoteMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(EmoteMaskPath);

        SyncHandMask(rightHandMask, modelPrefab, rightHandPath, ref changed);
        SyncHandMask(leftHandMask, modelPrefab, leftHandPath, ref changed);

        EnsureParameter(controller, "IsRightHandOccupied", AnimatorControllerParameterType.Bool, ref changed);
        EnsureParameter(controller, "IsLeftHandOccupied", AnimatorControllerParameterType.Bool, ref changed);
        EnsureParameter(controller, "ThumbsUp", AnimatorControllerParameterType.Trigger, ref changed);

        AnimatorControllerLayer rightGripLayer = EnsureLayer(controller, RightGripLayerName, rightHandMask, ref changed);
        AnimatorControllerLayer leftGripLayer = EnsureLayer(controller, LeftGripLayerName, leftHandMask, ref changed);
        AnimatorControllerLayer thumbsUpLayer = EnsureLayer(controller, ThumbsUpLayerName, emoteMask, ref changed);

        ConfigureGripLayer(rightGripLayer.stateMachine, rightGripClip, "IsRightHandOccupied", ref changed);
        ConfigureGripLayer(leftGripLayer.stateMachine, leftGripClip, "IsLeftHandOccupied", ref changed);
        ConfigureThumbsUpLayer(thumbsUpLayer.stateMachine, thumbsUpClip, ref changed);

        if (!changed)
        {
            if (logResult)
                Debug.Log("[PlayerHandAnimatorSync] Animator ja estava sincronizado.");

            return;
        }

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(rightHandMask);
        EditorUtility.SetDirty(leftHandMask);
        AssetDatabase.SaveAssets();
        MovementAnimatorMirrorSync.TrySync(logResult: false);

        if (logResult)
            Debug.Log("[PlayerHandAnimatorSync] Controller, masks e documentacao de animator foram sincronizados.");
    }

    private static bool TryResolveHandBonePaths(GameObject playerPrefab, out string rightHandPath, out string leftHandPath)
    {
        rightHandPath = string.Empty;
        leftHandPath = string.Empty;

        Transform playerRoot = playerPrefab != null ? playerPrefab.transform : null;
        Transform modelRoot = FindTransformByName(playerRoot, "Model");
        Transform rightSocket = FindTransformByName(playerRoot, RightHandSocketName);
        Transform leftSocket = FindTransformByName(playerRoot, LeftHandSocketName);

        if (modelRoot == null || rightSocket == null || leftSocket == null || rightSocket.parent == null || leftSocket.parent == null)
            return false;

        rightHandPath = AnimationUtility.CalculateTransformPath(rightSocket.parent, modelRoot);
        leftHandPath = AnimationUtility.CalculateTransformPath(leftSocket.parent, modelRoot);
        return !string.IsNullOrWhiteSpace(rightHandPath) && !string.IsNullOrWhiteSpace(leftHandPath);
    }

    private static bool TryFindClip(string assetPath, string clipName, out AnimationClip clip)
    {
        clip = null;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (!(assets[i] is AnimationClip animationClip))
                continue;

            if (!string.Equals(animationClip.name, clipName, StringComparison.Ordinal))
                continue;

            clip = animationClip;
            return true;
        }

        return false;
    }

    private static AvatarMask LoadOrCreateMask(string assetPath, ref bool changed)
    {
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(assetPath);
        if (mask != null)
            return mask;

        EnsureParentDirectory(assetPath);
        mask = new AvatarMask();
        AssetDatabase.CreateAsset(mask, assetPath);
        changed = true;
        return mask;
    }

    private static void SyncHandMask(AvatarMask targetMask, GameObject modelPrefab, string handBonePath, ref bool changed)
    {
        AvatarMask generatedMask = BuildHandMask(modelPrefab, handBonePath);
        if (!AvatarMaskMatches(targetMask, generatedMask))
        {
            EditorUtility.CopySerialized(generatedMask, targetMask);
            changed = true;
        }

        UnityEngine.Object.DestroyImmediate(generatedMask);
    }

    private static AvatarMask BuildHandMask(GameObject modelPrefab, string handBonePath)
    {
        AvatarMask mask = new AvatarMask();
        SetAllHumanoidBodyParts(mask, false);

        GameObject tempRoot = new GameObject("Model");
        try
        {
            GameObject instance = UnityEngine.Object.Instantiate(modelPrefab, tempRoot.transform, false);
            instance.name = modelPrefab.name;

            Transform handBone = tempRoot.transform.Find(handBonePath);
            if (handBone == null)
                throw new InvalidOperationException($"Nao foi possivel encontrar o osso de mao em '{handBonePath}'.");

            mask.AddTransformPath(handBone, true);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(tempRoot);
        }

        return mask;
    }

    private static void SetAllHumanoidBodyParts(AvatarMask mask, bool active)
    {
        foreach (AvatarMaskBodyPart bodyPart in Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (bodyPart == AvatarMaskBodyPart.LastBodyPart)
                continue;

            mask.SetHumanoidBodyPartActive(bodyPart, active);
        }
    }

    private static bool AvatarMaskMatches(AvatarMask currentMask, AvatarMask desiredMask)
    {
        if (currentMask == null || desiredMask == null)
            return currentMask == desiredMask;

        foreach (AvatarMaskBodyPart bodyPart in Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (bodyPart == AvatarMaskBodyPart.LastBodyPart)
                continue;

            if (currentMask.GetHumanoidBodyPartActive(bodyPart) != desiredMask.GetHumanoidBodyPartActive(bodyPart))
                return false;
        }

        if (currentMask.transformCount != desiredMask.transformCount)
            return false;

        for (int i = 0; i < currentMask.transformCount; i++)
        {
            if (!string.Equals(currentMask.GetTransformPath(i), desiredMask.GetTransformPath(i), StringComparison.Ordinal))
                return false;

            if (currentMask.GetTransformActive(i) != desiredMask.GetTransformActive(i))
                return false;
        }

        return true;
    }

    private static void EnsureParameter(
        AnimatorController controller,
        string parameterName,
        AnimatorControllerParameterType parameterType,
        ref bool changed)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (!string.Equals(parameters[i].name, parameterName, StringComparison.Ordinal))
                continue;

            if (parameters[i].type != parameterType)
                Debug.LogWarning($"[PlayerHandAnimatorSync] O parametro '{parameterName}' ja existe com tipo diferente no controller.");

            return;
        }

        controller.AddParameter(parameterName, parameterType);
        changed = true;
    }

    private static AnimatorControllerLayer EnsureLayer(
        AnimatorController controller,
        string layerName,
        AvatarMask avatarMask,
        ref bool changed)
    {
        int layerIndex = FindLayerIndex(controller, layerName);
        if (layerIndex < 0)
        {
            controller.AddLayer(layerName);
            layerIndex = FindLayerIndex(controller, layerName);
            changed = true;
        }

        AnimatorControllerLayer[] layers = controller.layers;
        AnimatorControllerLayer layer = layers[layerIndex];
        bool layerChanged = false;

        if (layer.defaultWeight != 1f)
        {
            layer.defaultWeight = 1f;
            layerChanged = true;
        }

        if (layer.blendingMode != AnimatorLayerBlendingMode.Override)
        {
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layerChanged = true;
        }

        if (layer.avatarMask != avatarMask)
        {
            layer.avatarMask = avatarMask;
            layerChanged = true;
        }

        if (layer.stateMachine == null)
        {
            layer.stateMachine = new AnimatorStateMachine();
            layer.stateMachine.name = layerName;
            AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
            layerChanged = true;
        }

        if (layerChanged)
        {
            layers[layerIndex] = layer;
            controller.layers = layers;
            changed = true;
        }

        return controller.layers[layerIndex];
    }

    private static void ConfigureGripLayer(
        AnimatorStateMachine stateMachine,
        AnimationClip gripClip,
        string parameterName,
        ref bool changed)
    {
        AnimatorState emptyState = EnsureState(stateMachine, EmptyStateName, new Vector3(200f, 120f, 0f), ref changed);
        AnimatorState gripState = EnsureState(stateMachine, GripStateName, new Vector3(520f, 120f, 0f), ref changed);

        ConfigureState(emptyState, null, ref changed);
        ConfigureState(gripState, gripClip, ref changed);

        if (stateMachine.defaultState != emptyState)
        {
            stateMachine.defaultState = emptyState;
            changed = true;
        }

        AnimatorStateTransition enterTransition = EnsureTransition(emptyState, gripState, ref changed);
        ConfigureTransition(
            enterTransition,
            hasExitTime: false,
            exitTime: 0f,
            duration: 0.05f,
            new ConditionSpec(AnimatorConditionMode.If, 0f, parameterName),
            ref changed);

        AnimatorStateTransition exitTransition = EnsureTransition(gripState, emptyState, ref changed);
        ConfigureTransition(
            exitTransition,
            hasExitTime: false,
            exitTime: 0f,
            duration: 0.05f,
            new ConditionSpec(AnimatorConditionMode.IfNot, 0f, parameterName),
            ref changed);
    }

    private static void ConfigureThumbsUpLayer(
        AnimatorStateMachine stateMachine,
        AnimationClip thumbsUpClip,
        ref bool changed)
    {
        AnimatorState emptyState = EnsureState(stateMachine, EmptyStateName, new Vector3(200f, 320f, 0f), ref changed);
        AnimatorState thumbsUpState = EnsureState(stateMachine, ThumbsUpStateName, new Vector3(520f, 320f, 0f), ref changed);

        ConfigureState(emptyState, null, ref changed);
        ConfigureState(thumbsUpState, thumbsUpClip, ref changed);

        if (stateMachine.defaultState != emptyState)
        {
            stateMachine.defaultState = emptyState;
            changed = true;
        }

        AnimatorStateTransition enterTransition = EnsureTransition(emptyState, thumbsUpState, ref changed);
        ConfigureTransition(
            enterTransition,
            hasExitTime: false,
            exitTime: 0f,
            duration: 0.05f,
            new ConditionSpec(AnimatorConditionMode.If, 0f, "ThumbsUp"),
            ref changed);

        AnimatorStateTransition exitTransition = EnsureTransition(thumbsUpState, emptyState, ref changed);
        ConfigureTransition(
            exitTransition,
            hasExitTime: true,
            exitTime: 1f,
            duration: 0.05f,
            Array.Empty<ConditionSpec>(),
            ref changed);
    }

    private static AnimatorState EnsureState(
        AnimatorStateMachine stateMachine,
        string stateName,
        Vector3 position,
        ref bool changed)
    {
        ChildAnimatorState[] childStates = stateMachine.states;
        for (int i = 0; i < childStates.Length; i++)
        {
            if (childStates[i].state != null && string.Equals(childStates[i].state.name, stateName, StringComparison.Ordinal))
                return childStates[i].state;
        }

        changed = true;
        return stateMachine.AddState(stateName, position);
    }

    private static void ConfigureState(AnimatorState state, Motion motion, ref bool changed)
    {
        if (state.motion != motion)
        {
            state.motion = motion;
            changed = true;
        }

        if (state.writeDefaultValues)
        {
            state.writeDefaultValues = false;
            changed = true;
        }
    }

    private static AnimatorStateTransition EnsureTransition(AnimatorState source, AnimatorState destination, ref bool changed)
    {
        AnimatorStateTransition[] transitions = source.transitions;
        AnimatorStateTransition matchingTransition = null;

        for (int i = transitions.Length - 1; i >= 0; i--)
        {
            AnimatorStateTransition transition = transitions[i];
            if (transition.destinationState != destination || transition.isExit)
                continue;

            if (matchingTransition == null)
            {
                matchingTransition = transition;
                continue;
            }

            source.RemoveTransition(transition);
            changed = true;
        }

        if (matchingTransition != null)
            return matchingTransition;

        changed = true;
        return source.AddTransition(destination);
    }

    private static void ConfigureTransition(
        AnimatorStateTransition transition,
        bool hasExitTime,
        float exitTime,
        float duration,
        ConditionSpec condition,
        ref bool changed)
    {
        ConfigureTransition(transition, hasExitTime, exitTime, duration, new[] { condition }, ref changed);
    }

    private static void ConfigureTransition(
        AnimatorStateTransition transition,
        bool hasExitTime,
        float exitTime,
        float duration,
        ConditionSpec[] conditions,
        ref bool changed)
    {
        if (transition.hasExitTime != hasExitTime)
        {
            transition.hasExitTime = hasExitTime;
            changed = true;
        }

        if (!Mathf.Approximately(transition.exitTime, exitTime))
        {
            transition.exitTime = exitTime;
            changed = true;
        }

        if (!transition.hasFixedDuration)
        {
            transition.hasFixedDuration = true;
            changed = true;
        }

        if (!Mathf.Approximately(transition.duration, duration))
        {
            transition.duration = duration;
            changed = true;
        }

        if (transition.canTransitionToSelf)
        {
            transition.canTransitionToSelf = false;
            changed = true;
        }

        if (!ConditionsMatch(transition.conditions, conditions))
        {
            AnimatorCondition[] existingConditions = transition.conditions;
            for (int i = existingConditions.Length - 1; i >= 0; i--)
                transition.RemoveCondition(existingConditions[i]);

            for (int i = 0; i < conditions.Length; i++)
                transition.AddCondition(conditions[i].Mode, conditions[i].Threshold, conditions[i].Parameter);

            changed = true;
        }
    }

    private static bool ConditionsMatch(AnimatorCondition[] currentConditions, ConditionSpec[] expectedConditions)
    {
        if (currentConditions == null)
            return expectedConditions == null || expectedConditions.Length == 0;

        if (expectedConditions == null)
            return currentConditions.Length == 0;

        if (currentConditions.Length != expectedConditions.Length)
            return false;

        for (int i = 0; i < currentConditions.Length; i++)
        {
            if (currentConditions[i].mode != expectedConditions[i].Mode)
                return false;

            if (!string.Equals(currentConditions[i].parameter, expectedConditions[i].Parameter, StringComparison.Ordinal))
                return false;

            if (!Mathf.Approximately(currentConditions[i].threshold, expectedConditions[i].Threshold))
                return false;
        }

        return true;
    }

    private static int FindLayerIndex(AnimatorController controller, string layerName)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (string.Equals(layers[i].name, layerName, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static Transform FindTransformByName(Transform root, string name)
    {
        if (root == null)
            return null;

        if (string.Equals(root.name, name, StringComparison.Ordinal))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform match = FindTransformByName(root.GetChild(i), name);
            if (match != null)
                return match;
        }

        return null;
    }

    private static void EnsureParentDirectory(string assetPath)
    {
        string parentDirectory = Path.GetDirectoryName(assetPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
            Directory.CreateDirectory(parentDirectory);
    }

    private readonly struct ConditionSpec
    {
        public ConditionSpec(AnimatorConditionMode mode, float threshold, string parameter)
        {
            Mode = mode;
            Threshold = threshold;
            Parameter = parameter;
        }

        public AnimatorConditionMode Mode { get; }
        public float Threshold { get; }
        public string Parameter { get; }
    }
}
#endif
