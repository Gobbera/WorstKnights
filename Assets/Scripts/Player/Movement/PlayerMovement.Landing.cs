using UnityEngine;

public partial class PlayerMovement
{
    private void InitializeLandingAnimationState()
    {
        wasGroundedForLandingAnimation = IsGrounded;
        hasAirborneLandingPhase = !IsGrounded;
        landingAnimationArmed = false;
        landingAnimationTriggered = false;
        airborneLandingTime = 0f;
        airborneLandingStartHeight = transform.position.y;
        highestAirborneLandingHeight = airborneLandingStartHeight;
        groundedLandingConfirmTime = 0f;
        mostNegativeLandingVerticalSpeed = VerticalVelocity;
    }

    private void UpdateLandingAnimationState(float deltaTime)
    {
        if (!HasAuthority() || config == null)
            return;

        float verticalSpeed = VerticalVelocity;
        float confirmedGroundTime = Mathf.Max(0f, config.groundedConfirmTimeForLand);

        if (IsGrounded)
        {
            if (hasAirborneLandingPhase)
            {
                groundedLandingConfirmTime += deltaTime;
                bool hasConfirmedLanding = groundedLandingConfirmTime >= confirmedGroundTime;

                if (!landingAnimationTriggered
                    && landingAnimationArmed
                    && hasConfirmedLanding)
                {
                    landingAnimationSequence++;
                    landingAnimationTriggered = true;
                }

                if (hasConfirmedLanding)
                {
                    ApplyFallImpact();
                    ResetLandingAnimationState();
                }
            }
            else
            {
                groundedLandingConfirmTime = 0f;
            }

            wasGroundedForLandingAnimation = true;
            return;
        }

        groundedLandingConfirmTime = 0f;

        if (!hasAirborneLandingPhase || wasGroundedForLandingAnimation)
        {
            BeginAirborneLandingPhase(verticalSpeed);
        }
        else
        {
            airborneLandingTime += deltaTime;
            highestAirborneLandingHeight = Mathf.Max(highestAirborneLandingHeight, transform.position.y);
            mostNegativeLandingVerticalSpeed = Mathf.Min(mostNegativeLandingVerticalSpeed, verticalSpeed);
        }

        landingAnimationArmed |= ShouldArmLandingAnimation();
        wasGroundedForLandingAnimation = false;
    }

    private void ApplyFallImpact()
    {
        if (config == null || !config.enableFallImpact)
            return;

        float fallDistance = GetFallDistance();
        float downwardSpeed = Mathf.Abs(mostNegativeLandingVerticalSpeed);
        float harmfulFallDistance = GetHarmfulFallDistance();
        if (harmfulFallDistance <= 0f)
        {
            NotifyPlayerFallImpact(0f, fallDistance, downwardSpeed);
            return;
        }

        float damage = harmfulFallDistance * Mathf.Max(0f, config.fallDamagePerMeter);
        float maxDamage = Mathf.Max(0f, config.maxFallDamage);
        if (maxDamage > 0f)
            damage = Mathf.Min(damage, maxDamage);

        NotifyPlayerFallImpact(damage, fallDistance, downwardSpeed);

        ApplyFallMovementSlow();
    }

    private void NotifyPlayerFallImpact(float damage, float fallDistance, float downwardSpeed)
    {
        if (playerHealth == null)
            playerHealth = GetComponent<PlayerHealth>();

        playerHealth?.ReceiveFallDamage(damage, fallDistance, downwardSpeed);
    }

    private float GetHarmfulFallDistance()
    {
        return Mathf.Max(0f, GetFallDistance() - Mathf.Max(0f, config.minFallHeightForDamage));
    }

    private float GetFallDistance()
    {
        return Mathf.Max(0f, highestAirborneLandingHeight - transform.position.y);
    }

    private void BeginAirborneLandingPhase(float verticalSpeed)
    {
        hasAirborneLandingPhase = true;
        airborneLandingTime = 0f;
        airborneLandingStartHeight = transform.position.y;
        highestAirborneLandingHeight = airborneLandingStartHeight;
        groundedLandingConfirmTime = 0f;
        landingAnimationArmed = false;
        landingAnimationTriggered = false;
        mostNegativeLandingVerticalSpeed = verticalSpeed;
    }

    private bool ShouldArmLandingAnimation()
    {
        float airborneRise = highestAirborneLandingHeight - airborneLandingStartHeight;

        if (airborneLandingTime >= Mathf.Max(0f, config.minAirTimeForLand))
            return true;

        if (airborneRise >= Mathf.Max(0f, config.minAirHeightForLand))
            return true;

        if (mostNegativeLandingVerticalSpeed <= -Mathf.Max(0f, config.minDownwardSpeedForLand))
            return true;

        return false;
    }

    private void ResetLandingAnimationState()
    {
        hasAirborneLandingPhase = false;
        landingAnimationArmed = false;
        landingAnimationTriggered = false;
        airborneLandingTime = 0f;
        airborneLandingStartHeight = transform.position.y;
        highestAirborneLandingHeight = airborneLandingStartHeight;
        groundedLandingConfirmTime = 0f;
        mostNegativeLandingVerticalSpeed = VerticalVelocity;
    }

    private bool IsJumpGroundSuppressed()
    {
        return Time.time < jumpGroundIgnoreUntil;
    }

    private void SuppressGrounding(ref SurfaceProbeResult result)
    {
        result.HasGround = false;
        result.IsWalkableSlope = false;
        result.IsSlidingSlope = false;
        result.HasStep = false;
        result.GroundAngle = 0f;
    }
}
