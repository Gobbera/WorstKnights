using UnityEngine;

public partial class MovementAnimationController
{
    private void UpdateCrouchTransitionAnimationState(bool isGrounded, bool isJumpQueued, bool isCrouching)
    {
        if (!hasInitializedAnimatorState)
            return;

        if (!wasCrouching && isCrouching)
            PlayCrouchEnterAnimation();
        else if (wasCrouching && !isCrouching && isGrounded && !isJumpQueued && playerMovement.CurrentState != MovementState.air)
            PlayCrouchExitAnimation();

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
        bool suppressGroundLocomotion = !isGrounded || isJumpQueued || playerMovement.CurrentState == MovementState.air;
        bool useFullDirectionalInput = playerMovement.CurrentState == MovementState.crouching;
        float directionalScale = useFullDirectionalInput ? 1f : movementScale;
        Vector2 locomotionInput = playerMovement.AnimationInput;
        Vector2 animationInput = suppressGroundLocomotion ? Vector2.zero : locomotionInput * directionalScale;
        horizontal = SmoothAnimationParameter(horizontal, animationInput.x, ref horizontalVelocity);
        vertical = SmoothAnimationParameter(vertical, animationInput.y, ref verticalVelocity);
        movementMagnitude = SmoothAnimationParameter(movementMagnitude, animationInput.magnitude, ref movementMagnitudeVelocity);
    }

    private float SmoothAnimationParameter(float current, float target, ref float velocity)
    {
        if (animationSmoothTime <= 0f)
            return target;

        float next = Mathf.SmoothDamp(current, target, ref velocity, animationSmoothTime);
        if (Mathf.Abs(next - target) <= AnimationParameterSnapEpsilon)
        {
            velocity = 0f;
            return target;
        }

        return next;
    }
}
