using UnityEngine;

public partial class MovementAnimationController
{
    private void UpdateCrouchTransitionAnimationState(bool isGrounded, bool isJumpQueued, bool isCrouching)
    {
        if (!hasInitializedAnimatorState)
            return;

        if (IsAnimatorStateActive(ReactionDamageStatePath, 0))
            return;

        if (!wasCrouching && isCrouching)
            PlayCrouchEnterAnimation();
        else if (wasCrouching
            && !isCrouching
            && isGrounded
            && !isJumpQueued
            && playerMovement.CurrentState != MovementState.air
            && playerMovement.CurrentState != MovementState.jumping)
        {
            PlayCrouchExitAnimation();
        }

        wasCrouching = isCrouching;
    }

    private void UpdateAirborneAnimationState(bool isGrounded, bool isJumpQueued)
    {
        if (isGrounded)
        {
            if (hasAirbornePhase)
                ResetAirbornePhaseState();

            pendingJumpAirborneStart = false;
            wasGrounded = true;
            return;
        }

        pendingJumpAirborneStart |= isJumpQueued;

        if (!hasAirbornePhase || wasGrounded)
        {
            BeginAirbornePhase(isJumpQueued);
        }
        else
        {
            airborneTime += Time.deltaTime;
        }

        wasGrounded = false;
    }

    private void UpdateIdleTurnAnimationState(bool isGrounded, bool isJumpQueued)
    {
        float currentYaw = transform.eulerAngles.y;
        if (!hasIdleTurnYawSample)
        {
            lastIdleTurnYaw = currentYaw;
            hasIdleTurnYawSample = true;
            return;
        }

        float deltaYaw = Mathf.DeltaAngle(lastIdleTurnYaw, currentYaw);
        lastIdleTurnYaw = currentYaw;
        idleTurnCooldownTimer = Mathf.Max(0f, idleTurnCooldownTimer - Time.deltaTime);

        if (!CanTriggerIdleTurn(isGrounded, isJumpQueued))
        {
            idleTurnAccumulatedYaw = 0f;
            return;
        }

        if (IsAnimatorStateActive(ReactionDamageStatePath, 0))
        {
            idleTurnAccumulatedYaw = 0f;
            return;
        }

        if (Mathf.Abs(deltaYaw) <= 0.01f)
            return;

        if (idleTurnAccumulatedYaw != 0f && Mathf.Sign(idleTurnAccumulatedYaw) != Mathf.Sign(deltaYaw))
            idleTurnAccumulatedYaw = 0f;

        idleTurnAccumulatedYaw += deltaYaw;

        if (idleTurnCooldownTimer > 0f || Mathf.Abs(idleTurnAccumulatedYaw) < Mathf.Max(1f, idleTurnTriggerAngle))
            return;

        if (idleTurnAccumulatedYaw > 0f)
            PlayIdleTurnRightAnimation();
        else
            PlayIdleTurnLeftAnimation();

        idleTurnAccumulatedYaw = 0f;
        idleTurnCooldownTimer = Mathf.Max(0.05f, idleTurnCooldown);
    }

    private void BeginAirbornePhase(bool isJumpQueued)
    {
        hasAirbornePhase = true;
        airborneTime = 0f;
        airborneJumpStarted = pendingJumpAirborneStart || isJumpQueued;
        pendingJumpAirborneStart = false;
    }

    private bool ComputeIsFalling(bool isGrounded, bool isJumpQueued, float verticalSpeed)
    {
        if (suppressAirborneAnimations || isGrounded || isJumpQueued || !hasAirbornePhase)
            return false;

        if (!airborneJumpStarted && !allowFallingWithoutJump)
            return false;

        if (airborneTime < GetFallingStartDelay())
            return false;

        return verticalSpeed < -0.1f;
    }

    private void ResetAirbornePhaseState()
    {
        hasAirbornePhase = false;
        airborneJumpStarted = false;
        airborneTime = 0f;
    }

    private float GetFallingStartDelay()
    {
        return Mathf.Max(0f, playerMovement != null && playerMovement.Config != null ? playerMovement.Config.fallingStartDelay : 0.08f);
    }

    private bool CanTriggerIdleTurn(bool isGrounded, bool isJumpQueued)
    {
        if (!enableIdleTurnInPlace || playerMovement == null)
            return false;

        if (!isGrounded || isJumpQueued)
            return false;

        if (playerMovement.CurrentState != MovementState.idle)
            return false;

        return playerMovement.Input.sqrMagnitude <= idleTurnMovementThreshold * idleTurnMovementThreshold;
    }

    private void UpdateLocomotionAnimationState(bool isGrounded, bool isJumpQueued, float movementScale)
    {
        bool suppressGroundLocomotion = !isGrounded
            || isJumpQueued
            || playerMovement.CurrentState == MovementState.air
            || playerMovement.CurrentState == MovementState.jumping;
        bool useFullDirectionalInput = playerMovement.CurrentState == MovementState.crouching;
        float directionalScale = useFullDirectionalInput ? 1f : movementScale;
        Vector2 locomotionInput = playerMovement.AnimationInput;
        Vector2 animationInput = suppressGroundLocomotion ? Vector2.zero : locomotionInput * directionalScale;
        float locomotionSmoothTime = ResolveLocomotionSmoothTime(animationInput);
        horizontal = SmoothAnimationParameter(horizontal, animationInput.x, ref horizontalVelocity, locomotionSmoothTime);
        vertical = SmoothAnimationParameter(vertical, animationInput.y, ref verticalVelocity, locomotionSmoothTime);
        movementMagnitude = SmoothAnimationParameter(movementMagnitude, animationInput.magnitude, ref movementMagnitudeVelocity, locomotionSmoothTime);
    }

    private float ResolveLocomotionSmoothTime(Vector2 targetInput)
    {
        float smoothTime = animationSmoothTime;
        Vector2 currentInput = new Vector2(horizontal, vertical);

        if (currentInput.sqrMagnitude > AnimationParameterSnapEpsilon
            && targetInput.sqrMagnitude > AnimationParameterSnapEpsilon)
        {
            float directionDot = Vector2.Dot(currentInput.normalized, targetInput.normalized);
            if (directionDot <= reversalAnimationDotThreshold)
                smoothTime = Mathf.Max(smoothTime, reversalAnimationSmoothTime);
        }

        return smoothTime;
    }

    private float SmoothAnimationParameter(float current, float target, ref float velocity, float smoothTime)
    {
        if (smoothTime <= 0f)
            return target;

        float next = Mathf.SmoothDamp(current, target, ref velocity, smoothTime);
        if (Mathf.Abs(next - target) <= AnimationParameterSnapEpsilon)
        {
            velocity = 0f;
            return target;
        }

        return next;
    }

    private void UpdateEmoteAnimationState(bool isGrounded, bool isJumpQueued)
    {
        CacheAnimatorLayerIndices();
        if (animator == null || emoteLayerIndex < 0)
            return;

        bool isEmoteLayerActive = IsNonEmptyLayerStateActive(emoteLayerIndex);
        if (isEmoteLayerActive && ShouldInterruptEmote(isGrounded, isJumpQueued))
        {
            CancelActiveEmote();
            isEmoteLayerActive = false;
        }

        SetLayerWeightIfNeeded(emoteLayerIndex, isEmoteLayerActive ? 1f : 0f);
    }

    private void UpdateKickAnimationState()
    {
        CacheAnimatorLayerIndices();
        if (animator == null || kickLayerIndex < 0)
            return;

        bool isKickLayerActive = IsNonEmptyLayerStateActive(kickLayerIndex);
        SetLayerWeightIfNeeded(kickLayerIndex, isKickLayerActive ? 1f : 0f);
    }

    private void UpdateUpperBodyAttackAnimationState()
    {
        CacheAnimatorLayerIndices();
        if (animator == null || upperBodyAttackLayerIndex < 0)
            return;

        bool isUpperBodyAttackLayerActive = IsNonEmptyLayerStateActive(upperBodyAttackLayerIndex);
        if (isUpperBodyAttackLayerActive)
        {
            SetLayerWeightIfNeeded(upperBodyAttackLayerIndex, 1f);
            return;
        }

        FadeLayerWeightTowards(upperBodyAttackLayerIndex, 0f, upperBodyAttackLayerFadeOutTime);
    }

    private void FadeLayerWeightTowards(int layerIndex, float targetWeight, float duration)
    {
        if (animator == null || layerIndex < 0 || layerIndex >= animator.layerCount)
            return;

        float currentWeight = animator.GetLayerWeight(layerIndex);
        float safeTargetWeight = Mathf.Clamp01(targetWeight);
        float safeDuration = Mathf.Max(0f, duration);

        if (safeDuration <= 0.0001f)
        {
            SetLayerWeightIfNeeded(layerIndex, safeTargetWeight);
            return;
        }

        float nextWeight = Mathf.MoveTowards(currentWeight, safeTargetWeight, Time.deltaTime / safeDuration);
        if (Mathf.Abs(nextWeight - safeTargetWeight) <= AnimationParameterSnapEpsilon)
            nextWeight = safeTargetWeight;

        SetLayerWeightIfNeeded(layerIndex, nextWeight);
    }

    private bool ShouldInterruptEmote(bool isGrounded, bool isJumpQueued)
    {
        if (playerMovement == null)
            return false;

        if (!isGrounded || isJumpQueued)
            return true;

        if (playerMovement.CurrentState == MovementState.air
            || playerMovement.CurrentState == MovementState.jumping
            || playerMovement.CurrentState == MovementState.sprinting)
        {
            return true;
        }

        if (IsAnimatorStateActive(JumpStatePath, 0)
            || IsAnimatorStateActive(FallingStatePath, 0)
            || IsAnimatorStateActive(LandingStatePath, 0)
            || IsAnimatorStateActive(StandUpStatePath, 0)
            || IsAnimatorStateActive(ReactionDamageStatePath, 0))
        {
            return true;
        }

        return IsNonEmptyLayerStateActive(upperBodyAttackLayerIndex);
    }

    private void CancelActiveEmote()
    {
        ResetTriggerIfExists(MovementAnimatorSemantic.ThumbsUpTrigger, "ThumbsUp");
        TryPlayStateOnLayer(emoteLayerIndex, EmoteLayerEmptyStatePath, 0.05f);
        SetLayerWeightIfNeeded(emoteLayerIndex, 0f);
    }

    private void ResetKickAnimationLayer()
    {
        ResetTriggerIfExists(MovementAnimatorSemantic.KickTrigger, "Kick");
        TryPlayStateOnLayer(kickLayerIndex, KickLayerEmptyStatePath, 0f);
        SetLayerWeightIfNeeded(kickLayerIndex, 0f);
    }

    private void ResetUpperBodyAttackAnimationLayer()
    {
        ResetTriggerIfExists(MovementAnimatorSemantic.AttackTrigger, "Attack");
        TryPlayStateOnLayer(upperBodyAttackLayerIndex, UpperBodyAttackLayerEmptyStatePath, 0f);
        SetLayerWeightIfNeeded(upperBodyAttackLayerIndex, 0f);
    }
}
