using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FirstPersonAnimationController : MonoBehaviour
{
    private const int AnimatorLayerIndex = 0;
    private const string BaseLayerName = "FPS Base";
    private const string RightArmLayerName = "FPS Right Arm";
    private const string LeftArmLayerName = "FPS Left Arm";
    private const string PreferredLayerName = "FPS";
    private const string LegacyLayerName = "KN_FPS";
    private const string ImportedModelName = "KnightFPS";
    private const string ArmatureRootName = "Armature";
    private const string AttackTriggerParameter = "Attack";
    private const string AttackComboStepParameter = "AttackComboStep";
    private const string PickUpItemRightTriggerParameter = "PickUpItemRight";
    private const string PickUpItemLeftTriggerParameter = "PickUpItemLeft";
    private const string RightDrawTriggerParameter = "RightDraw";
    private const string LeftDrawTriggerParameter = "LeftDraw";
    private const string ThumbsUpTriggerParameter = "ThumbsUp";
    private const string PointTriggerParameter = "Point";
    private const string IsGroundedParameter = "IsGrounded";
    private const string IsSprintingParameter = "IsSprinting";
    private const string IsMovingParameter = "IsMoving";
    private const string MovementMagnitudeParameter = "MovementMagnitude";
    private const string IsRightHandOccupiedParameter = "IsRightHandOccupied";
    private const string IsLeftHandOccupiedParameter = "IsLeftHandOccupied";
    private const string IsLeftTorchEquippedParameter = "IsLeftTorchEquipped";
    private const float PendingActionGraceTime = 0.25f;
    private const float ActionExitNormalizedTime = 1f;
    private const float MovingParameterThreshold = 0.1f;

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PhotonView photonView;

    [Header("Base FPS States")]
    [SerializeField] private string idleState = "FPS Base.Idle";
    [SerializeField] private string runForwardState = "FPS Base.Run Forward";

    [Header("Right Arm FPS States")]
    [SerializeField] private string rightArmEmptyState = "FPS Right Arm.Empty";
    [SerializeField] private string rightArmRunForwardState = "FPS Right Arm.Run Forward";
    [SerializeField] private string rightHandGripState = "FPS Right Arm.Right Hand Grip";
    [SerializeField] private string rightHandGripWalkState = "FPS Right Arm.Right Hand Grip Walk";
    [SerializeField] private string attackStep1State = "FPS Right Arm.Attack_01";
    [SerializeField] private string attackStep2State = "FPS Right Arm.Attack_02";
    [SerializeField] private string attackStep3State = "FPS Right Arm.Attack_03";
    [SerializeField] private string rightDrawState = "FPS Right Arm.Right Draw";
    [SerializeField] private string thumbsUpState = "FPS Right Arm.Emote_Thumbs_Up";
    [SerializeField] private string pointState = "FPS Right Arm.Emote_Point";
    [SerializeField] private string pickupRightState = "FPS Right Arm.Pick_Up_Item_Right";

    [Header("Left Arm FPS States")]
    [SerializeField] private string leftArmEmptyState = "FPS Left Arm.Empty";
    [SerializeField] private string leftArmRunForwardState = "FPS Left Arm.Run Forward";
    [SerializeField] private string leftHandGripState = "FPS Left Arm.Left Hand Grip";
    [SerializeField] private string leftHandGripWalkState = "FPS Left Arm.Left Hand Grip Walk";
    [SerializeField] private string torchGripState = "FPS Left Arm.Torch Grip";
    [SerializeField] private string torchGripWalkState = "FPS Left Arm.Torch Grip Walk";
    [SerializeField] private string leftDrawState = "FPS Left Arm.Left Draw";
    [SerializeField] private string pickupLeftState = "FPS Left Arm.Pick_Up_Item_Left";

    private readonly HashSet<int> warnedMissingStateHashes = new HashSet<int>();
    private readonly Dictionary<int, AnimatorControllerParameterType> animatorParameterTypes = new Dictionary<int, AnimatorControllerParameterType>();
    private int lastAttackAnimationSequence;
    private int lastPickupAnimationSequence;
    private int lastDrawAnimationSequence;
    private int lastEmoteAnimationSequence;
    private int activeActionStateHash;
    private int requestedStateHash;
    private int baseLayerIndex = -1;
    private int rightArmLayerIndex = -1;
    private int leftArmLayerIndex = -1;
    private float activeActionRequestTime;
    private string requestedStatePath;
    private RuntimeAnimatorController cachedParameterController;
    private bool hasInitialized;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        hasInitialized = false;
        activeActionStateHash = 0;
        requestedStateHash = 0;
        requestedStatePath = string.Empty;
        activeActionRequestTime = 0f;
    }

    private void Update()
    {
        ResolveReferences();

        if (animator == null || playerMovement == null || !IsLocalOwner())
            return;

        if (!hasInitialized)
            InitializeRuntimeState();

        UpdateAnimatorContextParameters();
        UpdateLayerWeights();
        TryPlayTriggeredAction();
    }

    private void ResolveReferences()
    {
        Animator bestAnimator = ResolveBestAnimator();
        if (bestAnimator != null && animator != bestAnimator)
        {
            animator = bestAnimator;
            warnedMissingStateHashes.Clear();
            activeActionStateHash = 0;
            requestedStateHash = 0;
            requestedStatePath = string.Empty;
            ClearAnimatorParameterCache();
            ClearAnimatorLayerCache();
            hasInitialized = false;
        }

        if (animator != null)
        {
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

        if (photonView == null)
            photonView = GetComponentInParent<PhotonView>();

        CacheAnimatorLayerIndices();
    }

    private Animator ResolveBestAnimator()
    {
        Animator bestAnimator = animator != null ? animator : GetComponent<Animator>();
        int bestScore = ScoreAnimator(bestAnimator);
        Animator[] childAnimators = GetComponentsInChildren<Animator>(true);

        for (int i = 0; i < childAnimators.Length; i++)
        {
            Animator candidate = childAnimators[i];
            int candidateScore = ScoreAnimator(candidate);
            if (candidateScore <= bestScore)
                continue;

            bestAnimator = candidate;
            bestScore = candidateScore;
        }

        return bestAnimator;
    }

    private int ScoreAnimator(Animator candidate)
    {
        if (candidate == null)
            return int.MinValue;

        int score = 0;

        if (candidate.runtimeAnimatorController != null)
            score += 10;

        if (candidate.transform != transform)
            score += 4;

        if (string.Equals(candidate.gameObject.name, ImportedModelName, System.StringComparison.Ordinal))
            score += 40;

        if (HasDirectChild(candidate.transform, ArmatureRootName))
            score += 100;

        if (CanResolveAnyConfiguredState(candidate))
            score += 20;

        return score;
    }

    private bool CanResolveAnyConfiguredState(Animator candidate)
    {
        return TryResolveStatePath(candidate, idleState, out _, out _)
            || TryResolveStatePath(candidate, runForwardState, out _, out _)
            || TryResolveStatePath(candidate, attackStep1State, out _, out _);
    }

    private static bool HasDirectChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return false;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != null && string.Equals(child.name, childName, System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void InitializeRuntimeState()
    {
        lastAttackAnimationSequence = playerMovement.AttackAnimationSequence;
        lastPickupAnimationSequence = playerMovement.PickupAnimationSequence;
        lastDrawAnimationSequence = playerMovement.DrawAnimationSequence;
        lastEmoteAnimationSequence = playerMovement.EmoteAnimationSequence;
        hasInitialized = true;

        UpdateAnimatorContextParameters();
        ResetActionParameters();
        CacheAnimatorLayerIndices();
        UpdateLayerWeights();
        PlayInitialLayerStates();
    }

    private bool IsLocalOwner()
    {
        return photonView == null || photonView.IsMine;
    }

    private void PlayInitialLayerStates()
    {
        PlayStateOnLayer(ResolveSustainedState(), baseLayerIndex, false);
        PlayStateOnLayer(ResolveRightArmSustainedState(), rightArmLayerIndex, false);
        PlayStateOnLayer(ResolveLeftArmSustainedState(), leftArmLayerIndex, false);
    }

    private void UpdateLayerWeights()
    {
        if (animator == null)
            return;

        CacheAnimatorLayerIndices();
        SetLayerWeightIfNeeded(baseLayerIndex, 1f);
        bool useRunForward = ShouldUseRunForwardState();
        SetLayerWeightIfNeeded(rightArmLayerIndex, useRunForward && !playerMovement.IsRightHandOccupied ? 0f : 1f);
        SetLayerWeightIfNeeded(leftArmLayerIndex, useRunForward && !playerMovement.IsLeftHandOccupied ? 0f : 1f);
    }

    private void CacheAnimatorLayerIndices()
    {
        if (animator == null)
            return;

        if (baseLayerIndex < 0 || baseLayerIndex >= animator.layerCount)
            baseLayerIndex = ResolveLayerIndex(BaseLayerName, 0);

        if (rightArmLayerIndex < 0 || rightArmLayerIndex >= animator.layerCount)
            rightArmLayerIndex = ResolveLayerIndex(RightArmLayerName, -1);

        if (leftArmLayerIndex < 0 || leftArmLayerIndex >= animator.layerCount)
            leftArmLayerIndex = ResolveLayerIndex(LeftArmLayerName, -1);
    }

    private int ResolveLayerIndex(string layerName, int fallbackIndex)
    {
        if (animator == null)
            return fallbackIndex;

        int layerIndex = animator.GetLayerIndex(layerName);
        if (layerIndex >= 0)
            return layerIndex;

        return fallbackIndex >= 0 && fallbackIndex < animator.layerCount ? fallbackIndex : -1;
    }

    private void ClearAnimatorLayerCache()
    {
        baseLayerIndex = -1;
        rightArmLayerIndex = -1;
        leftArmLayerIndex = -1;
    }

    private void SetLayerWeightIfNeeded(int layerIndex, float weight)
    {
        if (animator == null || layerIndex < 0 || layerIndex >= animator.layerCount)
            return;

        if (Mathf.Approximately(animator.GetLayerWeight(layerIndex), weight))
            return;

        animator.SetLayerWeight(layerIndex, weight);
    }

    private bool TryPlayTriggeredAction()
    {
        if (playerMovement.AttackAnimationSequence != lastAttackAnimationSequence)
        {
            lastAttackAnimationSequence = playerMovement.AttackAnimationSequence;
            return TryRequestAttack(playerMovement.AttackComboStep, true);
        }

        if (playerMovement.PickupAnimationSequence != lastPickupAnimationSequence)
        {
            lastPickupAnimationSequence = playerMovement.PickupAnimationSequence;
            string triggerParameter = playerMovement.PickupAnimationHand == HandType.Left
                ? PickUpItemLeftTriggerParameter
                : PickUpItemRightTriggerParameter;
            return TrySetTriggerParameterIfExists(triggerParameter, true);
        }

        if (playerMovement.DrawAnimationSequence != lastDrawAnimationSequence)
        {
            lastDrawAnimationSequence = playerMovement.DrawAnimationSequence;
            string triggerParameter = playerMovement.DrawAnimationHand == HandType.Left
                ? LeftDrawTriggerParameter
                : RightDrawTriggerParameter;
            return TrySetTriggerParameterIfExists(triggerParameter, true);
        }

        if (playerMovement.EmoteAnimationSequence != lastEmoteAnimationSequence)
        {
            lastEmoteAnimationSequence = playerMovement.EmoteAnimationSequence;
            switch (playerMovement.CurrentEmoteType)
            {
                case PlayerEmoteType.ThumbsUp:
                    return TrySetTriggerParameterIfExists(ThumbsUpTriggerParameter, true);
                case PlayerEmoteType.Point:
                    return TrySetTriggerParameterIfExists(PointTriggerParameter, true);
                default:
                    return false;
            }
        }

        return false;
    }

    private string ResolveAttackState(int comboStep)
    {
        switch (comboStep)
        {
            case 2:
                return attackStep2State;
            case 3:
                return attackStep3State;
            default:
                return attackStep1State;
        }
    }

    private string ResolvePickupState(HandType hand)
    {
        return hand == HandType.Left ? pickupLeftState : pickupRightState;
    }

    private string ResolveDrawState(HandType hand)
    {
        return hand == HandType.Left ? leftDrawState : rightDrawState;
    }

    private string ResolveEmoteState(PlayerEmoteType type)
    {
        switch (type)
        {
            case PlayerEmoteType.ThumbsUp:
                return thumbsUpState;
            case PlayerEmoteType.Point:
                return pointState;
            default:
                return string.Empty;
        }
    }

    private void PlaySustainedState()
    {
        PlaySustainedState(ResolveSustainedState());
    }

    private void PlaySustainedState(string statePath)
    {
        PlayState(statePath, false, out _);
    }

    private string ResolveSustainedState()
    {
        return ShouldUseRunForwardState() ? runForwardState : idleState;
    }

    private string ResolveRightArmSustainedState()
    {
        if (ShouldUseRunForwardState() && playerMovement.IsRightHandOccupied)
            return rightArmRunForwardState;

        bool useHeldItemWalkState = ShouldUseHeldItemWalkState();

        if (playerMovement.IsRightHandOccupied)
            return useHeldItemWalkState ? rightHandGripWalkState : rightHandGripState;

        return rightArmEmptyState;
    }

    private string ResolveLeftArmSustainedState()
    {
        if (ShouldUseRunForwardState() && playerMovement.IsLeftHandOccupied)
            return leftArmRunForwardState;

        bool useHeldItemWalkState = ShouldUseHeldItemWalkState();

        if (playerMovement.IsLeftHandTorchEquipped)
            return useHeldItemWalkState ? torchGripWalkState : torchGripState;

        if (playerMovement.IsLeftHandOccupied)
            return useHeldItemWalkState ? leftHandGripWalkState : leftHandGripState;

        return leftArmEmptyState;
    }

    private bool ShouldUseRunForwardState()
    {
        if (playerMovement == null)
            return false;

        if (playerMovement.CurrentState != MovementState.sprinting)
            return false;

        return playerMovement.IsGrounded && !playerMovement.IsJumpQueued;
    }

    private bool ShouldUseHeldItemWalkState()
    {
        if (playerMovement == null)
            return false;

        if (playerMovement.CurrentState != MovementState.walking)
            return false;

        return playerMovement.IsGrounded && !playerMovement.IsJumpQueued;
    }

    private void UpdateAnimatorContextParameters()
    {
        if (animator == null || playerMovement == null)
            return;

        float movementMagnitude = ResolveMovementMagnitude();
        bool isMoving = movementMagnitude > MovingParameterThreshold
            || (playerMovement.HasLocomotionIntent
                && playerMovement.CurrentState == MovementState.walking
                && playerMovement.IsGrounded
                && !playerMovement.IsJumpQueued);

        SetBoolParameterIfExists(IsGroundedParameter, playerMovement.IsGrounded);
        SetBoolParameterIfExists(IsSprintingParameter, playerMovement.CurrentState == MovementState.sprinting);
        SetBoolParameterIfExists(IsMovingParameter, isMoving);
        SetBoolParameterIfExists(IsRightHandOccupiedParameter, playerMovement.IsRightHandOccupied);
        SetBoolParameterIfExists(IsLeftHandOccupiedParameter, playerMovement.IsLeftHandOccupied);
        SetBoolParameterIfExists(IsLeftTorchEquippedParameter, playerMovement.IsLeftHandTorchEquipped);
        SetFloatParameterIfExists(MovementMagnitudeParameter, movementMagnitude);
    }

    private float ResolveMovementMagnitude()
    {
        if (playerMovement == null)
            return 0f;

        if (!playerMovement.IsGrounded
            || playerMovement.IsJumpQueued
            || playerMovement.CurrentState == MovementState.air
            || playerMovement.CurrentState == MovementState.jumping)
        {
            return 0f;
        }

        return playerMovement.AnimationInput.magnitude * Mathf.Max(0f, playerMovement.LocomotionScale);
    }

    private bool PlayActionState(string statePath)
    {
        if (string.IsNullOrWhiteSpace(statePath))
            return false;

        if (!PlayState(statePath, true, out int playedStateHash))
        {
            activeActionStateHash = 0;
            PlaySustainedState();
            return false;
        }

        activeActionStateHash = playedStateHash;
        activeActionRequestTime = Time.time;
        return true;
    }

    private bool PlayState(string statePath, bool forceRestart, out int resolvedStateHash)
    {
        resolvedStateHash = 0;

        if (animator == null || string.IsNullOrWhiteSpace(statePath))
            return false;

        if (!TryResolveStatePath(animator, statePath, out string resolvedStatePath, out resolvedStateHash))
        {
            WarnMissingStateOnce(statePath, Animator.StringToHash(statePath));
            return false;
        }

        if (!forceRestart && string.Equals(requestedStatePath, resolvedStatePath, System.StringComparison.Ordinal) && IsStateActive(resolvedStateHash))
            return true;

        if (TryRequestAnimatorControllerTransition(resolvedStatePath, resolvedStateHash, forceRestart))
            return true;

        animator.CrossFadeInFixedTime(resolvedStateHash, 0f, AnimatorLayerIndex, 0f);
        requestedStatePath = resolvedStatePath;
        requestedStateHash = resolvedStateHash;
        return true;
    }

    private bool PlayStateOnLayer(string statePath, int layerIndex, bool forceRestart)
    {
        if (animator == null || string.IsNullOrWhiteSpace(statePath))
            return false;

        if (layerIndex < 0 || layerIndex >= animator.layerCount)
            return false;

        if (!TryResolveStatePath(animator, statePath, layerIndex, out string resolvedStatePath, out int resolvedStateHash))
        {
            WarnMissingStateOnce(statePath, Animator.StringToHash(statePath));
            return false;
        }

        if (!forceRestart && IsStateActiveOnLayer(resolvedStateHash, layerIndex))
            return true;

        animator.CrossFadeInFixedTime(resolvedStateHash, 0f, layerIndex, 0f);
        requestedStatePath = resolvedStatePath;
        requestedStateHash = resolvedStateHash;
        return true;
    }

    private bool TryRequestAnimatorControllerTransition(string resolvedStatePath, int resolvedStateHash, bool forceRestart)
    {
        string shortStateName = ExtractShortStateName(resolvedStatePath);
        if (!TryRequestAnimatorControllerState(shortStateName, forceRestart))
            return false;

        requestedStatePath = resolvedStatePath;
        requestedStateHash = resolvedStateHash;
        return true;
    }

    private bool TryRequestAnimatorControllerState(string shortStateName, bool forceRestart)
    {
        switch (shortStateName)
        {
            case "Attack_01":
                return TryRequestAttack(1, forceRestart);
            case "Attack_02":
                return TryRequestAttack(2, forceRestart);
            case "Attack_03":
                return TryRequestAttack(3, forceRestart);
            case "Pick_Up_Item_Right":
                return TrySetTriggerParameterIfExists(PickUpItemRightTriggerParameter, forceRestart);
            case "Pick_Up_Item_Left":
                return TrySetTriggerParameterIfExists(PickUpItemLeftTriggerParameter, forceRestart);
            case "Right Draw":
                return TrySetTriggerParameterIfExists(RightDrawTriggerParameter, forceRestart);
            case "Left Draw":
                return TrySetTriggerParameterIfExists(LeftDrawTriggerParameter, forceRestart);
            case "Emote_Thumbs_Up":
                return TrySetTriggerParameterIfExists(ThumbsUpTriggerParameter, forceRestart);
            case "Emote_Point":
                return TrySetTriggerParameterIfExists(PointTriggerParameter, forceRestart);
            default:
                return CanDriveSustainedStateFromContext(shortStateName);
        }
    }

    private bool TryRequestAttack(int comboStep, bool forceRestart)
    {
        if (!SetIntParameterIfExists(AttackComboStepParameter, Mathf.Clamp(comboStep, 1, MovementConfig.AttackComboStepCount)))
            return false;

        return TrySetTriggerParameterIfExists(AttackTriggerParameter, forceRestart);
    }

    private bool CanDriveSustainedStateFromContext(string shortStateName)
    {
        switch (shortStateName)
        {
            case "Idle":
                return HasBoolParameter(IsRightHandOccupiedParameter)
                    && HasBoolParameter(IsLeftHandOccupiedParameter)
                    && HasBoolParameter(IsSprintingParameter);
            case "Run Forward":
                return HasBoolParameter(IsSprintingParameter)
                    && HasBoolParameter(IsGroundedParameter);
            case "Right Hand Grip":
                return HasBoolParameter(IsRightHandOccupiedParameter)
                    && HasBoolParameter(IsMovingParameter)
                    && HasBoolParameter(IsSprintingParameter);
            case "Right Hand Grip Walk":
                return HasBoolParameter(IsRightHandOccupiedParameter)
                    && HasBoolParameter(IsMovingParameter)
                    && HasBoolParameter(IsSprintingParameter)
                    && HasBoolParameter(IsGroundedParameter);
            case "Left Hand Grip":
                return HasBoolParameter(IsRightHandOccupiedParameter)
                    && HasBoolParameter(IsLeftHandOccupiedParameter)
                    && HasBoolParameter(IsLeftTorchEquippedParameter)
                    && HasBoolParameter(IsMovingParameter)
                    && HasBoolParameter(IsSprintingParameter);
            case "Left Hand Grip Walk":
                return HasBoolParameter(IsRightHandOccupiedParameter)
                    && HasBoolParameter(IsLeftHandOccupiedParameter)
                    && HasBoolParameter(IsLeftTorchEquippedParameter)
                    && HasBoolParameter(IsMovingParameter)
                    && HasBoolParameter(IsSprintingParameter)
                    && HasBoolParameter(IsGroundedParameter);
            case "Torch Grip":
                return HasBoolParameter(IsRightHandOccupiedParameter)
                    && HasBoolParameter(IsLeftHandOccupiedParameter)
                    && HasBoolParameter(IsLeftTorchEquippedParameter)
                    && HasBoolParameter(IsMovingParameter)
                    && HasBoolParameter(IsSprintingParameter);
            case "Torch Grip Walk":
                return HasBoolParameter(IsRightHandOccupiedParameter)
                    && HasBoolParameter(IsLeftHandOccupiedParameter)
                    && HasBoolParameter(IsLeftTorchEquippedParameter)
                    && HasBoolParameter(IsMovingParameter)
                    && HasBoolParameter(IsSprintingParameter)
                    && HasBoolParameter(IsGroundedParameter);
            default:
                return false;
        }
    }

    private void ResetActionParameters()
    {
        ResetTriggerParameterIfExists(AttackTriggerParameter);
        ResetTriggerParameterIfExists(PickUpItemRightTriggerParameter);
        ResetTriggerParameterIfExists(PickUpItemLeftTriggerParameter);
        ResetTriggerParameterIfExists(RightDrawTriggerParameter);
        ResetTriggerParameterIfExists(LeftDrawTriggerParameter);
        ResetTriggerParameterIfExists(ThumbsUpTriggerParameter);
        ResetTriggerParameterIfExists(PointTriggerParameter);
        SetIntParameterIfExists(AttackComboStepParameter, 1);
    }

    private bool HasBoolParameter(string parameterName)
    {
        return HasParameter(parameterName, AnimatorControllerParameterType.Bool);
    }

    private bool SetIntParameterIfExists(string parameterName, int value)
    {
        if (animator == null || !HasParameter(parameterName, AnimatorControllerParameterType.Int))
            return false;

        animator.SetInteger(parameterName, value);
        return true;
    }

    private bool TrySetTriggerParameterIfExists(string parameterName, bool resetFirst)
    {
        if (animator == null || !HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
            return false;

        if (resetFirst)
            animator.ResetTrigger(parameterName);

        animator.SetTrigger(parameterName);
        return true;
    }

    private void ResetTriggerParameterIfExists(string parameterName)
    {
        if (animator == null || !HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
            return;

        animator.ResetTrigger(parameterName);
    }

    private void SetBoolParameterIfExists(string parameterName, bool value)
    {
        if (animator == null || !HasParameter(parameterName, AnimatorControllerParameterType.Bool))
            return;

        animator.SetBool(parameterName, value);
    }

    private void SetFloatParameterIfExists(string parameterName, float value)
    {
        if (animator == null || !HasParameter(parameterName, AnimatorControllerParameterType.Float))
            return;

        animator.SetFloat(parameterName, value);
    }

    private bool HasParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (animator == null || string.IsNullOrWhiteSpace(parameterName))
            return false;

        CacheAnimatorParameters();
        int parameterHash = Animator.StringToHash(parameterName);
        return animatorParameterTypes.TryGetValue(parameterHash, out AnimatorControllerParameterType foundType)
            && foundType == parameterType;
    }

    private void CacheAnimatorParameters()
    {
        RuntimeAnimatorController runtimeController = animator.runtimeAnimatorController;
        if (cachedParameterController == runtimeController)
            return;

        cachedParameterController = runtimeController;
        animatorParameterTypes.Clear();

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter != null)
                animatorParameterTypes[parameter.nameHash] = parameter.type;
        }
    }

    private void ClearAnimatorParameterCache()
    {
        cachedParameterController = null;
        animatorParameterTypes.Clear();
    }

    private bool TryResolveStatePath(Animator targetAnimator, string configuredStatePath, out string resolvedStatePath, out int stateHash)
    {
        return TryResolveStatePath(targetAnimator, configuredStatePath, AnimatorLayerIndex, out resolvedStatePath, out stateHash);
    }

    private bool TryResolveStatePath(Animator targetAnimator, string configuredStatePath, int layerIndex, out string resolvedStatePath, out int stateHash)
    {
        resolvedStatePath = string.Empty;
        stateHash = 0;

        if (targetAnimator == null || string.IsNullOrWhiteSpace(configuredStatePath))
            return false;

        if (layerIndex < 0 || layerIndex >= targetAnimator.layerCount)
            return false;

        foreach (string candidatePath in EnumerateStatePathCandidates(configuredStatePath))
        {
            int candidateHash = Animator.StringToHash(candidatePath);
            if (!targetAnimator.HasState(layerIndex, candidateHash))
                continue;

            resolvedStatePath = candidatePath;
            stateHash = candidateHash;
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateStatePathCandidates(string configuredStatePath)
    {
        if (string.IsNullOrWhiteSpace(configuredStatePath))
            yield break;

        HashSet<string> visitedPaths = new HashSet<string>();
        string trimmedPath = configuredStatePath.Trim();
        string shortStateName = ExtractShortStateName(trimmedPath);

        foreach (string candidatePath in new[]
        {
            trimmedPath,
            $"{BaseLayerName}.{shortStateName}",
            $"{RightArmLayerName}.{shortStateName}",
            $"{LeftArmLayerName}.{shortStateName}",
            $"{PreferredLayerName}.{shortStateName}",
            $"{LegacyLayerName}.{shortStateName}",
            shortStateName
        })
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || !visitedPaths.Add(candidatePath))
                continue;

            yield return candidatePath;
        }
    }

    private static string ExtractShortStateName(string statePath)
    {
        if (string.IsNullOrWhiteSpace(statePath))
            return string.Empty;

        int separatorIndex = statePath.LastIndexOf('.');
        if (separatorIndex < 0 || separatorIndex >= statePath.Length - 1)
            return statePath;

        return statePath.Substring(separatorIndex + 1);
    }

    private bool IsActionStateActive()
    {
        if (activeActionStateHash == 0)
            return false;

        if (IsStateActive(activeActionStateHash))
            return true;

        return requestedStateHash == activeActionStateHash
            && Time.time - activeActionRequestTime <= PendingActionGraceTime;
    }

    private bool IsStateActive(int stateHash)
    {
        return IsStateActiveOnLayer(stateHash, AnimatorLayerIndex);
    }

    private bool IsStateActiveOnLayer(int stateHash, int layerIndex)
    {
        if (animator == null || stateHash == 0 || layerIndex < 0 || layerIndex >= animator.layerCount)
            return false;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(layerIndex);
        if (currentState.fullPathHash == stateHash)
            return true;

        return animator.IsInTransition(layerIndex)
            && animator.GetNextAnimatorStateInfo(layerIndex).fullPathHash == stateHash;
    }

    private bool HasActiveActionFinished()
    {
        if (animator == null || activeActionStateHash == 0 || AnimatorLayerIndex >= animator.layerCount)
            return true;

        if (animator.IsInTransition(AnimatorLayerIndex))
            return false;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(AnimatorLayerIndex);
        if (currentState.fullPathHash != activeActionStateHash)
        {
            return requestedStateHash != activeActionStateHash
                || Time.time - activeActionRequestTime > PendingActionGraceTime;
        }

        return currentState.normalizedTime >= ActionExitNormalizedTime;
    }

    private void WarnMissingStateOnce(string statePath, int stateHash)
    {
        if (!warnedMissingStateHashes.Add(stateHash))
            return;

        Debug.LogWarning($"FirstPersonAnimationController: state '{statePath}' nao foi encontrado no Animator FPS.", gameObject);
    }
}
