using UnityEngine;

public partial class PlayerMovement
{
    private void UpdateDirectionChangeInertia(float deltaTime)
    {
        AdvanceDirectionChangeInertia(deltaTime);
        UpdateDirectionChangeInputMemory(deltaTime);

        if (!CanUseDirectionChangeInertia())
        {
            ResetDirectionChangeInertia();
            ClearDirectionChangeHistory();
            return;
        }

        if (rawInput.sqrMagnitude <= InputDeadzone || desiredMoveDirection.sqrMagnitude <= InputDeadzone)
            return;

        TryBeginDirectionChangeInertiaFromCurrentInput();

        CacheDirectionChangeHistory();
    }

    private void TryBeginDirectionChangeInertiaFromCurrentInput()
    {
        if (!CanUseDirectionChangeInertia()
            || IsDirectionChangeInertiaActive()
            || rawInput.sqrMagnitude <= InputDeadzone
            || desiredMoveDirection.sqrMagnitude <= InputDeadzone)
        {
            return;
        }

        if (TryGetDirectionChangeProfile(out DirectionChangeInertiaProfile profile)
            && ShouldTriggerDirectionChange(profile))
        {
            BeginDirectionChangeInertia(profile);
        }
    }

    private void AdvanceDirectionChangeInertia(float deltaTime)
    {
        if (!IsDirectionChangeInertiaActive())
        {
            directionChangeBrakeActive = false;
            directionChangeSpeedMultiplier = 1f;
            return;
        }

        if (directionChangeBrakeActive && !ShouldContinueDirectionChangeBrake())
        {
            directionChangeBrakeActive = false;
            directionChangeHoldTimer = 0f;
        }

        if (directionChangeHoldTimer > 0f)
        {
            directionChangeHoldTimer = Mathf.Max(0f, directionChangeHoldTimer - deltaTime);
            directionChangeSpeedMultiplier = activeDirectionChangeProfile.speedMultiplier;

            if (directionChangeHoldTimer > 0f)
                return;

            directionChangeBrakeActive = false;
        }

        if (directionChangeRecoveryTimer > 0f)
        {
            directionChangeRecoveryTimer = Mathf.Max(0f, directionChangeRecoveryTimer - deltaTime);

            float recoveryDuration = Mathf.Max(activeDirectionChangeProfile.recoveryDuration, 0.0001f);
            float recoveryProgress = 1f - (directionChangeRecoveryTimer / recoveryDuration);
            directionChangeSpeedMultiplier = Mathf.Lerp(activeDirectionChangeProfile.speedMultiplier, 1f, recoveryProgress);

            if (directionChangeRecoveryTimer > 0f)
                return;
        }

        ResetDirectionChangeInertia();
    }

    private bool CanUseDirectionChangeInertia()
    {
        return HasAuthority()
            && config != null
            && IsGrounded
            && !jumpQueued
            && !surfaceState.IsSlidingSlope
            && !exitingSlope
            && (CurrentState == MovementState.walking || CurrentState == MovementState.sprinting);
    }

    private bool TryGetDirectionChangeProfile(out DirectionChangeInertiaProfile profile)
    {
        switch (CurrentState)
        {
            case MovementState.walking:
                profile = new DirectionChangeInertiaProfile(
                    config.walkCameraTurnReversalAngle,
                    config.walkReversalHoldTime,
                    config.walkReversalRecoveryTime,
                    config.walkReversalSpeedMultiplier,
                    config.walkReversalAccelerationMultiplier);
                return true;
            case MovementState.sprinting:
                profile = new DirectionChangeInertiaProfile(
                    config.sprintReversalAngle,
                    config.sprintReversalHoldTime,
                    config.sprintReversalRecoveryTime,
                    config.sprintReversalSpeedMultiplier,
                    config.sprintReversalAccelerationMultiplier);
                return true;
            default:
                profile = default;
                return false;
        }
    }

    private bool ShouldTriggerDirectionChange(DirectionChangeInertiaProfile profile)
    {
        if (!hasDirectionChangeHistory
            || rememberedDirectionalInput.sqrMagnitude <= InputDeadzone)
        {
            return false;
        }

        Vector3 currentDesiredDirection = desiredMoveDirection.normalized;
        Vector3 supportNormal = surfaceState.IsWalkableSlope ? surfaceState.GroundNormal : Vector3.up;
        Vector3 planarVelocity = GetPlanarVelocity(supportNormal);
        float planarSpeed = planarVelocity.magnitude;
        if (planarSpeed < Mathf.Max(0f, config.directionChangeMinPlanarSpeed))
            return false;

        float momentumDot = Vector3.Dot(planarVelocity / planarSpeed, currentDesiredDirection);
        if (momentumDot > profile.reversalDotThreshold)
            return false;

        Vector2 currentInputDirection = rawInput.normalized;
        Vector2 previousInputDirection = rememberedDirectionalInput.normalized;
        float inputDot = Vector2.Dot(previousInputDirection, currentInputDirection);

        bool cameraDrivenReversal = inputDot >= config.cameraTurnReversalInputAlignmentDot;

        switch (CurrentState)
        {
            case MovementState.walking:
                return cameraDrivenReversal || inputDot <= config.walkInputReversalDot;
            case MovementState.sprinting:
                return cameraDrivenReversal || inputDot <= config.sprintInputReversalDot;
            default:
                return false;
        }
    }

    private void BeginDirectionChangeInertia(DirectionChangeInertiaProfile profile)
    {
        activeDirectionChangeProfile = profile;
        directionChangeHoldTimer = Mathf.Max(0f, profile.holdDuration);
        directionChangeRecoveryTimer = Mathf.Max(0f, profile.recoveryDuration);
        directionChangeSpeedMultiplier = Mathf.Clamp01(profile.speedMultiplier);
        directionChangeBrakeActive = directionChangeHoldTimer > 0f;
    }

    private void ResetDirectionChangeInertia()
    {
        directionChangeBrakeActive = false;
        directionChangeHoldTimer = 0f;
        directionChangeRecoveryTimer = 0f;
        directionChangeSpeedMultiplier = 1f;
        activeDirectionChangeProfile = default;
    }

    private bool IsDirectionChangeInertiaActive()
    {
        return directionChangeHoldTimer > 0f || directionChangeRecoveryTimer > 0f;
    }

    private void CacheDirectionChangeHistory()
    {
        rememberedDirectionalInput = rawInput;
        directionChangeInputMemoryTimer = GetDirectionChangeInputMemoryDuration();
        hasDirectionChangeHistory = true;
    }

    private void ClearDirectionChangeHistory()
    {
        rememberedDirectionalInput = Vector2.zero;
        directionChangeInputMemoryTimer = 0f;
        hasDirectionChangeHistory = false;
    }

    private void UpdateDirectionChangeInputMemory(float deltaTime)
    {
        if (!hasDirectionChangeHistory)
            return;

        if (rawInput.sqrMagnitude > InputDeadzone)
        {
            directionChangeInputMemoryTimer = GetDirectionChangeInputMemoryDuration();
            return;
        }

        directionChangeInputMemoryTimer = Mathf.Max(0f, directionChangeInputMemoryTimer - deltaTime);
        if (directionChangeInputMemoryTimer <= 0f)
            ClearDirectionChangeHistory();
    }

    private float GetDirectionChangeInputMemoryDuration()
    {
        return Mathf.Max(0f, config != null ? config.directionChangeInputMemoryDuration : 0.12f);
    }

    private float GetDirectionChangeSpeedMultiplier()
    {
        return IsDirectionChangeInertiaActive() ? directionChangeSpeedMultiplier : 1f;
    }

    private float GetDirectionChangeAccelerationMultiplier()
    {
        return IsDirectionChangeInertiaActive()
            ? activeDirectionChangeProfile.accelerationMultiplier
            : 1f;
    }

    private bool IsDirectionChangeBrakeActive()
    {
        return directionChangeBrakeActive && IsDirectionChangeInertiaActive();
    }

    private bool ShouldContinueDirectionChangeBrake()
    {
        if (!directionChangeBrakeActive
            || config == null
            || desiredMoveDirection.sqrMagnitude <= InputDeadzone)
        {
            return false;
        }

        Vector3 supportNormal = surfaceState.IsWalkableSlope ? surfaceState.GroundNormal : Vector3.up;
        Vector3 planarVelocity = GetPlanarVelocity(supportNormal);
        float planarSpeed = planarVelocity.magnitude;
        if (planarSpeed <= Mathf.Max(0f, config.directionChangeBrakeExitPlanarSpeed))
            return false;

        Vector3 desiredDirection = Vector3.ProjectOnPlane(desiredMoveDirection, supportNormal);
        if (desiredDirection.sqrMagnitude <= InputDeadzone)
            return false;

        float momentumDot = Vector3.Dot(planarVelocity / planarSpeed, desiredDirection.normalized);
        return momentumDot <= activeDirectionChangeProfile.reversalDotThreshold;
    }

    private readonly struct DirectionChangeInertiaProfile
    {
        public readonly float reversalDotThreshold;
        public readonly float holdDuration;
        public readonly float recoveryDuration;
        public readonly float speedMultiplier;
        public readonly float accelerationMultiplier;

        public DirectionChangeInertiaProfile(
            float reversalAngle,
            float holdDuration,
            float recoveryDuration,
            float speedMultiplier,
            float accelerationMultiplier)
        {
            reversalDotThreshold = Mathf.Cos(Mathf.Clamp(reversalAngle, 0f, 180f) * Mathf.Deg2Rad);
            this.holdDuration = Mathf.Max(0f, holdDuration);
            this.recoveryDuration = Mathf.Max(0f, recoveryDuration);
            this.speedMultiplier = Mathf.Clamp01(speedMultiplier);
            this.accelerationMultiplier = Mathf.Max(0.01f, accelerationMultiplier);
        }
    }
}
