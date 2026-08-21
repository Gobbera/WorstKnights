using UnityEngine;

public partial class PlayerMovement
{
    private void InitializeLocomotionSpeedBlend()
    {
        if (config == null)
            return;

        currentLocomotionSpeed = GetStateTargetSpeed(CurrentState);
        locomotionSpeedVelocity = 0f;
        hasLocomotionSpeedSample = true;
        sprintReleaseBlendActive = false;
        LocomotionScale = CalculateLocomotionScale();
    }

    private void UpdateLocomotionSpeedBlend(float deltaTime)
    {
        if (config == null)
            return;

        float targetSpeed = GetStateTargetSpeed(CurrentState);
        if (!hasLocomotionSpeedSample)
        {
            currentLocomotionSpeed = targetSpeed;
            hasLocomotionSpeedSample = true;
        }

        bool shouldBlendSprintRelease = sprintReleaseBlendActive
            && CurrentState == MovementState.walking
            && IsGrounded
            && Input.sqrMagnitude > InputDeadzone
            && currentLocomotionSpeed > targetSpeed + 0.01f;

        if (shouldBlendSprintRelease)
        {
            float safeDeltaTime = Mathf.Max(deltaTime, 0.0001f);
            float smoothTime = Mathf.Max(0.0001f, config.sprintReleaseDecelerationTime);
            currentLocomotionSpeed = Mathf.SmoothDamp(
                currentLocomotionSpeed,
                targetSpeed,
                ref locomotionSpeedVelocity,
                smoothTime,
                Mathf.Infinity,
                safeDeltaTime);

            if (Mathf.Abs(currentLocomotionSpeed - targetSpeed) <= 0.01f)
            {
                currentLocomotionSpeed = targetSpeed;
                locomotionSpeedVelocity = 0f;
                sprintReleaseBlendActive = false;
            }
        }
        else
        {
            currentLocomotionSpeed = targetSpeed;
            locomotionSpeedVelocity = 0f;
            sprintReleaseBlendActive = false;
        }

        LocomotionScale = CalculateLocomotionScale();
    }

    private void ApplyMovement()
    {
        if (surfaceState.IsSlidingSlope && !exitingSlope)
        {
            ApplySlidingMovement();
            return;
        }

        Vector3 moveDirection = GetEffectiveMoveDirection();
        float inputMagnitude = Mathf.Clamp01(moveDirection.magnitude);

        Vector3 supportNormal = surfaceState.IsWalkableSlope && !exitingSlope ? surfaceState.GroundNormal : Vector3.up;
        Vector3 targetVelocity = Vector3.zero;

        if (!IsDirectionChangeBrakeActive()
            && moveDirection.sqrMagnitude > 0.0001f
            && inputMagnitude > 0f)
        {
            Vector3 projectedDirection = Vector3.ProjectOnPlane(moveDirection.normalized, supportNormal);
            if (projectedDirection.sqrMagnitude > 0.0001f)
            {
                float targetSpeed = GetTargetMoveSpeed(includeDirectionChange: true) * inputMagnitude * currentAcceleration;
                targetVelocity = projectedDirection.normalized * targetSpeed;
            }
        }

        Vector3 currentPlanarVelocity = GetPlanarVelocity(supportNormal);
        bool shouldApplyAirCorrection = IsGrounded || inputMagnitude > 0f;
        Vector3 velocityDelta = shouldApplyAirCorrection ? targetVelocity - currentPlanarVelocity : Vector3.zero;

        float airControl = Mathf.Max(config.airMultiplier, 0f);
        float acceleration = IsGrounded
            ? config.groundAcceleration * GetDirectionChangeAccelerationMultiplier()
            : config.airAcceleration * airControl;
        acceleration *= GetMovementVolumeAccelerationMultiplier();

        if (shouldApplyAirCorrection && acceleration > 0f && velocityDelta.sqrMagnitude > 0.0001f)
        {
            Vector3 accelerationForce = Vector3.ClampMagnitude(velocityDelta / Time.fixedDeltaTime, acceleration);
            rb.AddForce(accelerationForce, ForceMode.Acceleration);
        }

        ApplyGroundSnapAcceleration(surfaceState.GroundNormal);
    }

    private void ApplySlidingMovement()
    {
        Vector3 slideDirection = Vector3.ProjectOnPlane(Vector3.down, surfaceState.GroundNormal);
        if (slideDirection.sqrMagnitude > 0.0001f)
        {
            slideDirection.Normalize();
            rb.AddForce(slideDirection * config.slideAcceleration, ForceMode.Acceleration);
        }

        ApplyGroundSnapAcceleration(surfaceState.GroundNormal);
    }

    private bool ApplyStepAssist()
    {
        if (!surfaceState.HasStep || !surfaceState.HasGround || surfaceState.IsSlidingSlope || desiredMoveDirection.sqrMagnitude <= 0.0001f)
            return false;

        float liftAmount = Mathf.Min(surfaceState.StepHeight, config.stepLiftSpeed * Time.fixedDeltaTime);
        if (liftAmount <= 0f)
            return false;

        rb.MovePosition(rb.position + Vector3.up * liftAmount);
        return true;
    }

    private float GetCurrentSpeed()
    {
        return hasLocomotionSpeedSample ? currentLocomotionSpeed : GetStateTargetSpeed(CurrentState);
    }

    private float GetStateTargetSpeed(MovementState state)
    {
        float baseSpeed = ApplyWalkBackSpeedMultiplier(GetStateBaseSpeed(state), state);
        return ApplyMovementVolumeSpeedMultiplier(ApplyTemporaryMovementSpeedMultipliers(baseSpeed));
    }

    private float GetStateBaseSpeed(MovementState state)
    {
        switch (state)
        {
            case MovementState.crouching:
                return config.crouchSpeed;
            case MovementState.sprinting:
                return config.sprintSpeed;
            default:
                return config.walkSpeed;
        }
    }

    private float ApplyAttackMovementSpeedMultiplier(float baseSpeed)
    {
        if (config == null || !IsAttackMovementSlowActive())
            return baseSpeed;

        return baseSpeed * Mathf.Clamp01(config.attackMovementSpeedMultiplier);
    }

    private float ApplyWalkBackSpeedMultiplier(float baseSpeed, MovementState state)
    {
        if (config == null || state != MovementState.walking || !HasBackwardWalkInput())
            return baseSpeed;

        return baseSpeed * Mathf.Clamp01(config.walkBackSpeedMultiplier);
    }

    private bool HasBackwardWalkInput()
    {
        return HasAuthority()
            ? requestedInput.y < -InputDeadzone
            : networkAnimationInput.y < -InputDeadzone;
    }

    private float ApplyKickMovementSpeedMultiplier(float baseSpeed)
    {
        if (config == null || !IsKickMovementSlowActive())
            return baseSpeed;

        return baseSpeed * Mathf.Clamp01(config.kickMovementSpeedMultiplier);
    }

    private float ApplyTemporaryMovementSpeedMultipliers(float baseSpeed)
    {
        float modifiedSpeed = ApplyAttackMovementSpeedMultiplier(baseSpeed);
        modifiedSpeed = ApplyKickMovementSpeedMultiplier(modifiedSpeed);
        return ApplyFallMovementSpeedMultiplier(modifiedSpeed);
    }

    private float ApplyFallMovementSpeedMultiplier(float baseSpeed)
    {
        if (config == null || !IsFallMovementSlowActive())
            return baseSpeed;

        return baseSpeed * Mathf.Clamp01(config.fallMovementSpeedMultiplier);
    }

    private bool IsAttackMovementSlowActive()
    {
        return HasAuthority()
            ? Time.time < attackMovementSlowUntil
            : networkAttackMovementSlowActive;
    }

    private bool IsKickMovementSlowActive()
    {
        return HasAuthority()
            ? Time.time < kickMovementSlowUntil
            : networkKickMovementSlowActive;
    }

    private bool IsFallMovementSlowActive()
    {
        return HasAuthority()
            ? Time.time < fallMovementSlowUntil
            : networkFallMovementSlowActive;
    }

    private void ApplyFallMovementSlow()
    {
        if (!HasAuthority() || config == null)
            return;

        float slowDuration = Mathf.Max(0f, config.fallMovementSlowDuration);
        float speedMultiplier = Mathf.Clamp01(config.fallMovementSpeedMultiplier);
        if (slowDuration <= 0f || speedMultiplier >= 0.9999f)
            return;

        fallMovementSlowUntil = Mathf.Max(fallMovementSlowUntil, Time.time + slowDuration);
    }

    private float CalculateLocomotionScale()
    {
        if (config == null)
            return 1f;

        float walkSpeed = Mathf.Max(0.01f, config.walkSpeed);
        if (config.sprintSpeed > walkSpeed && currentLocomotionSpeed > walkSpeed)
            return Mathf.Lerp(1f, 2f, Mathf.InverseLerp(walkSpeed, config.sprintSpeed, currentLocomotionSpeed));

        return Mathf.Clamp(currentLocomotionSpeed / walkSpeed, 0f, 1f);
    }

    private float GetTargetMoveSpeed(bool includeDirectionChange)
    {
        float baseSpeed = GetCurrentSpeed();
        bool preserveMomentum = jumpQueued || !IsGrounded;
        if (includeDirectionChange && !preserveMomentum)
            baseSpeed *= GetDirectionChangeSpeedMultiplier();

        return preserveMomentum ? Mathf.Max(baseSpeed, airborneMomentumSpeed) : baseSpeed;
    }

    private void SpeedControl()
    {
        float maxSpeed = surfaceState.IsSlidingSlope ? config.maxSlideSpeed : GetTargetMoveSpeed(includeDirectionChange: false);
        if (maxSpeed <= 0f)
            return;

        bool useSurfacePlane = surfaceState.IsWalkableSlope || surfaceState.IsSlidingSlope;
        Vector3 planeNormal = useSurfacePlane ? surfaceState.GroundNormal : Vector3.up;
        Vector3 planarVelocity = GetPlanarVelocity(planeNormal);

        if (planarVelocity.magnitude <= maxSpeed)
            return;

        Vector3 limitedPlanarVelocity = planarVelocity.normalized * maxSpeed;
        if (useSurfacePlane)
        {
            Vector3 normalVelocity = Vector3.Project(rb.linearVelocity, planeNormal);
            rb.linearVelocity = limitedPlanarVelocity + normalVelocity;
        }
        else
        {
            rb.linearVelocity = new Vector3(limitedPlanarVelocity.x, rb.linearVelocity.y, limitedPlanarVelocity.z);
        }
    }

    private Vector3 GetPlanarVelocity(Vector3 planeNormal)
    {
        if (planeNormal == Vector3.up)
            return new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        return Vector3.ProjectOnPlane(rb.linearVelocity, planeNormal);
    }

    private Vector3 GetEffectiveMoveDirection()
    {
        if (desiredMoveDirection.sqrMagnitude <= InputDeadzone)
            return Vector3.zero;

        if (surfaceState.IsSlidingSlope && !exitingSlope)
            return Vector3.zero;

        Vector3 effectiveMoveDirection = Vector3.ProjectOnPlane(desiredMoveDirection, Vector3.up);
        if (surfaceState.HasWallBlock)
            effectiveMoveDirection = Vector3.ProjectOnPlane(effectiveMoveDirection, surfaceState.WallNormal);

        return Vector3.ProjectOnPlane(effectiveMoveDirection, Vector3.up);
    }

    private void UpdateEffectiveInput()
    {
        Input = CalculateEffectiveInput(GetDirectionChangeSpeedMultiplier());
        AnimationInput = CalculateAnimationInput();
    }

    private Vector2 CalculateEffectiveInput(float magnitudeScale)
    {
        if (orientation == null || rawInput.sqrMagnitude <= InputDeadzone)
            return Vector2.zero;

        Vector3 effectiveMoveDirection = GetEffectiveMoveDirection();
        if (effectiveMoveDirection.sqrMagnitude <= InputDeadzone)
            return Vector2.zero;

        Vector3 localDirection = orientation.InverseTransformDirection(effectiveMoveDirection);
        return Vector2.ClampMagnitude(new Vector2(localDirection.x, localDirection.z), 1f) * Mathf.Clamp01(magnitudeScale);
    }

    private Vector2 CalculateAnimationInput()
    {
        if (orientation == null || config == null)
            return Vector2.zero;

        if (!IsDirectionChangeBrakeActive())
        {
            Vector2 intentAxes = CalculateAnimationIntentAxes();
            if (intentAxes.sqrMagnitude > InputDeadzone)
            {
                float availabilityScale = CalculateAnimationAvailabilityScale(intentAxes);
                return availabilityScale > InputDeadzone
                    ? ClampAnimationAxes(intentAxes * availabilityScale)
                    : Vector2.zero;
            }
        }

        return CalculateVelocityAnimationInput();
    }

    private Vector2 CalculateAnimationIntentAxes()
    {
        if (requestedInput.sqrMagnitude <= InputDeadzone)
            return Vector2.zero;

        if (surfaceState.IsSlidingSlope && !exitingSlope)
            return Vector2.zero;

        return ClampAnimationAxes(requestedInput);
    }

    private float CalculateAnimationAvailabilityScale(Vector2 intentAxes)
    {
        Vector2 physicalIntent = Vector2.ClampMagnitude(intentAxes, 1f);
        float physicalIntentMagnitude = physicalIntent.magnitude;
        if (physicalIntentMagnitude <= InputDeadzone)
            return 0f;

        return Mathf.Clamp01(Input.magnitude / physicalIntentMagnitude);
    }

    private Vector2 CalculateVelocityAnimationInput()
    {
        Vector3 supportNormal = surfaceState.IsWalkableSlope && !exitingSlope ? surfaceState.GroundNormal : Vector3.up;
        Vector3 planarVelocity = GetPlanarVelocity(supportNormal);
        Vector3 localVelocity = orientation.InverseTransformDirection(Vector3.ProjectOnPlane(planarVelocity, Vector3.up));
        float animationReferenceSpeed = Mathf.Max(0.01f, GetTargetMoveSpeed(includeDirectionChange: false));
        Vector2 velocityAxes = new Vector2(localVelocity.x, localVelocity.z) / animationReferenceSpeed;

        float velocityDeadzone = Mathf.Max(0f, config.animationVelocityInputDeadzone);
        if (velocityAxes.sqrMagnitude > velocityDeadzone * velocityDeadzone)
            return Vector2.ClampMagnitude(velocityAxes, 1f);

        if (Input.sqrMagnitude <= InputDeadzone || IsDirectionChangeBrakeActive())
            return Vector2.zero;

        return Vector2.ClampMagnitude(Input, 1f);
    }

    private static Vector2 ClampAnimationAxes(Vector2 axes)
    {
        return new Vector2(
            Mathf.Clamp(axes.x, -1f, 1f),
            Mathf.Clamp(axes.y, -1f, 1f));
    }

    private void ApplyJumpForce()
    {
        if (rb == null || config == null || !jumpQueued)
            return;

        RefreshSurfaceState();
        jumpQueued = false;
        exitingSlope = true;
        surfaceAdhesionEligibleUntil = 0f;
        jumpGroundIgnoreUntil = Time.time + Mathf.Max(0f, config.jumpGroundIgnoreTime);

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        airborneMomentumSpeed = horizontalVelocity.magnitude;
        rb.linearVelocity = horizontalVelocity;

        SetMovementStateInternal(MovementState.air);
        rb.AddForce(Vector3.up * config.jumpForce, ForceMode.Impulse);

        float slopeExitDuration = Mathf.Max(config.jumpCooldown, config.jumpGroundIgnoreTime);
        Invoke(nameof(ResetJump), slopeExitDuration);
    }

    private void ResetJump()
    {
        jumpQueued = false;
        exitingSlope = false;
    }

    private void ApplyGroundSnapAcceleration(Vector3 groundNormal)
    {
        if (rb == null || config == null || !surfaceState.HasGround || IsJumpGroundSuppressed() || exitingSlope)
            return;

        float separatingSpeed = Vector3.Dot(rb.linearVelocity, groundNormal);
        if (separatingSpeed > GroundSnapMaxSeparationSpeed)
            return;

        rb.AddForce(-groundNormal * config.groundSnapAcceleration, ForceMode.Acceleration);
    }

    private bool ApplyRecentGroundAdhesion()
    {
        if (rb == null || config == null || capsule == null)
            return false;

        if (surfaceState.HasGround || Time.time > surfaceAdhesionEligibleUntil)
            return false;

        if (jumpQueued || IsJumpGroundSuppressed() || exitingSlope)
            return false;

        Vector3 planarVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (planarVelocity.sqrMagnitude <= 0.01f)
            return false;

        CapsuleShape capsuleShape = CapsuleShape.Create(transform, capsule, config.playerHeight);
        float probeRadius = capsuleShape.Radius * Mathf.Clamp(config.groundProbeRadiusScale, 0.5f, 1f);
        Vector3 probeOrigin = capsuleShape.BottomHemisphereCenter + Vector3.up * SurfaceProbeVerticalOffset;
        float probeDistance = Mathf.Max(
            config.groundProbeDistance + SurfaceAdhesionExtraProbeDistance,
            config.groundProbeDistance + planarVelocity.magnitude * Time.fixedDeltaTime);

        if (!TrySphereCastIgnoringSelf(probeOrigin, probeRadius, Vector3.down, probeDistance, GetCollisionMask(), out RaycastHit adhesionHit))
            return false;

        float groundAngle = Vector3.Angle(Vector3.up, adhesionHit.normal);
        if (groundAngle > config.maxSlopeAngle)
            return false;

        float snapDownDistance = Mathf.Clamp(adhesionHit.distance - SurfaceProbeVerticalOffset, 0f, SurfaceAdhesionMaxSnapDistance);
        if (snapDownDistance > 0.0001f)
            rb.MovePosition(rb.position + Vector3.down * snapDownDistance);

        rb.linearVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, adhesionHit.normal);
        return true;
    }
}
