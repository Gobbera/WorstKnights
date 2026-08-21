using System.Collections.Generic;
using UnityEngine;

public partial class MovementAnimationController
{
    private void CacheAnimatorParameters()
    {
        parameterNames.Clear();

        if (animator == null)
            return;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
            parameterNames.Add(parameter.name);
    }

    private bool HasParameter(string parameterName)
    {
        return parameterNames.Contains(parameterName);
    }

    private string ResolveParameterName(MovementAnimatorSemantic semantic, string fallbackParameterName)
    {
        if (animatorMirror != null && animatorMirror.TryGetBindingName(semantic, out string mirroredName))
            return mirroredName;

        return fallbackParameterName;
    }

    private void SetFloatIfExists(MovementAnimatorSemantic semantic, string fallbackParameterName, float value)
    {
        string parameterName = ResolveParameterName(semantic, fallbackParameterName);
        if (!HasParameter(parameterName))
            return;

        animator.SetFloat(parameterName, value, animationSmoothTime, Time.deltaTime);
    }

    private void SetFloatDirectIfExists(MovementAnimatorSemantic semantic, string fallbackParameterName, float value)
    {
        string parameterName = ResolveParameterName(semantic, fallbackParameterName);
        if (!HasParameter(parameterName))
            return;

        animator.SetFloat(parameterName, value);
    }

    private void SetBoolIfExists(MovementAnimatorSemantic semantic, string fallbackParameterName, bool value)
    {
        string parameterName = ResolveParameterName(semantic, fallbackParameterName);
        if (!HasParameter(parameterName))
            return;

        animator.SetBool(parameterName, value);
    }

    private void SetTriggerIfExists(MovementAnimatorSemantic semantic, string fallbackParameterName)
    {
        if (animator == null)
            return;

        string parameterName = ResolveParameterName(semantic, fallbackParameterName);
        if (!HasParameter(parameterName))
            return;

        animator.SetTrigger(parameterName);
    }

    private void ResetTriggerIfExists(MovementAnimatorSemantic semantic, string fallbackParameterName)
    {
        if (animator == null)
            return;

        string parameterName = ResolveParameterName(semantic, fallbackParameterName);
        if (!HasParameter(parameterName))
            return;

        animator.ResetTrigger(parameterName);
    }

    private void PlayIdleTurnAnimation(string stateName, MovementAnimatorSemantic semantic)
    {
        if (TryPlayState(stateName, 0.08f))
            return;

        ResetTriggerIfExists(MovementAnimatorSemantic.IdleTurnLeftTrigger, "IdleTurnLeft");
        ResetTriggerIfExists(MovementAnimatorSemantic.IdleTurnRightTrigger, "IdleTurnRight");
        SetTriggerIfExists(semantic, stateName);
    }

    private bool TryPlayState(string stateName, float transitionDuration)
    {
        if (animator == null)
            return false;

        HashSet<string> visitedPaths = new HashSet<string>();

        foreach (string candidatePath in EnumerateStatePathCandidates(stateName))
        {
            if (!visitedPaths.Add(candidatePath))
                continue;

            int stateHash = Animator.StringToHash(candidatePath);
            for (int layerIndex = 0; layerIndex < animator.layerCount; layerIndex++)
            {
                if (!animator.HasState(layerIndex, stateHash))
                    continue;

                animator.CrossFadeInFixedTime(stateHash, transitionDuration, layerIndex, 0f);
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> EnumerateStatePathCandidates(string stateName)
    {
        if (TryResolveStateFullPath(stateName, out string mirroredPath))
            yield return mirroredPath;

        yield return $"Base Layer.{stateName}";
        yield return stateName;
    }

    private bool TryResolveStateFullPath(string stateName, out string fullStatePath)
    {
        fullStatePath = string.Empty;

        if (animatorMirror == null || animatorMirror.States == null)
            return false;

        for (int i = 0; i < animatorMirror.States.Count; i++)
        {
            MovementAnimatorStateSnapshot state = animatorMirror.States[i];
            if (!string.Equals(state.statePath, stateName, System.StringComparison.Ordinal))
                continue;

            fullStatePath = string.IsNullOrWhiteSpace(state.layerName)
                ? state.statePath
                : $"{state.layerName}.{state.statePath}";
            return !string.IsNullOrWhiteSpace(fullStatePath);
        }

        return false;
    }

    private float GetMovementParameterScale()
    {
        return playerMovement != null ? playerMovement.LocomotionScale : 1f;
    }
}
