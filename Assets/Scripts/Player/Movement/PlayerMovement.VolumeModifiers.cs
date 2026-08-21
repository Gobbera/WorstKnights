using System.Collections.Generic;
using UnityEngine;

public partial class PlayerMovement
{
    private readonly HashSet<MovementVolumeController> activeMovementVolumes = new HashSet<MovementVolumeController>();
    private readonly List<MovementVolumeController> movementVolumesPendingRemoval = new List<MovementVolumeController>();

    private float movementVolumeSpeedMultiplier = 1f;
    private float movementVolumeAccelerationMultiplier = 1f;
    private float movementVolumeGroundDragMultiplier = 1f;
    private Vector3 movementVolumeConveyorVelocity;
    private float movementVolumeControlLockUntil;

    public void RegisterMovementVolume(MovementVolumeController volume)
    {
        if (!HasAuthority() || volume == null || !volume.IsContinuousEffect)
            return;

        activeMovementVolumes.Add(volume);
        RefreshMovementVolumeModifiers();
    }

    public void UnregisterMovementVolume(MovementVolumeController volume)
    {
        if (volume == null)
            return;

        if (!activeMovementVolumes.Remove(volume))
            return;

        RefreshMovementVolumeModifiers();
    }

    public void ApplyMovementVolumeTrap(float duration, bool zeroPlanarVelocityOnTrap)
    {
        if (!HasAuthority() || rb == null)
            return;

        if (zeroPlanarVelocityOnTrap)
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);

        float safeDuration = Mathf.Max(0f, duration);
        if (safeDuration > 0f)
            movementVolumeControlLockUntil = Mathf.Max(movementVolumeControlLockUntil, Time.time + safeDuration);
    }

    public bool TryApplyMovementVolumeBounce(
        Vector3 launchDirection,
        float minIncomingSpeed,
        float minBounceLaunchSpeed,
        float bounceRestitution,
        float bounceSpeedBonus,
        float maxBounceLaunchSpeed,
        float lateralVelocityMultiplier)
    {
        if (!HasAuthority() || rb == null)
            return false;

        Vector3 safeLaunchDirection = launchDirection.sqrMagnitude > 0.0001f
            ? launchDirection.normalized
            : Vector3.up;

        float incomingSpeed = Mathf.Max(0f, Vector3.Dot(-safeLaunchDirection, rb.linearVelocity));
        float requiredIncomingSpeed = Mathf.Max(0f, minIncomingSpeed);
        float safeMinBounceLaunchSpeed = Mathf.Max(0f, minBounceLaunchSpeed);
        float safeBounceRestitution = Mathf.Max(0f, bounceRestitution);
        float safeBounceSpeedBonus = Mathf.Max(0f, bounceSpeedBonus);

        if (incomingSpeed + 0.0001f < requiredIncomingSpeed
            && safeMinBounceLaunchSpeed <= 0.0001f
            && safeBounceSpeedBonus <= 0.0001f)
        {
            return false;
        }

        float launchSpeed = Mathf.Max(
            safeMinBounceLaunchSpeed,
            incomingSpeed * safeBounceRestitution + safeBounceSpeedBonus);

        float safeMaxBounceLaunchSpeed = Mathf.Max(0f, maxBounceLaunchSpeed);
        if (safeMaxBounceLaunchSpeed > 0f)
            launchSpeed = Mathf.Min(launchSpeed, safeMaxBounceLaunchSpeed);

        if (launchSpeed <= 0.0001f)
            return false;

        float clampedLateralVelocityMultiplier = Mathf.Max(0f, lateralVelocityMultiplier);
        Vector3 lateralVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, safeLaunchDirection) * clampedLateralVelocityMultiplier;
        rb.linearVelocity = lateralVelocity + safeLaunchDirection * launchSpeed;

        jumpQueued = false;
        exitingSlope = true;
        surfaceAdhesionEligibleUntil = 0f;
        jumpGroundIgnoreUntil = Time.time + Mathf.Max(0.08f, config != null ? config.jumpGroundIgnoreTime : 0.12f);
        CancelInvoke(nameof(ResetJump));
        Invoke(nameof(ResetJump), Mathf.Max(0.08f, config != null ? config.jumpGroundIgnoreTime : 0.12f));
        SetMovementStateInternal(MovementState.air);
        return true;
    }

    private void RefreshMovementVolumeModifiers()
    {
        if (!HasAuthority())
            return;

        movementVolumeSpeedMultiplier = 1f;
        movementVolumeAccelerationMultiplier = 1f;
        movementVolumeGroundDragMultiplier = 1f;
        movementVolumeConveyorVelocity = Vector3.zero;

        if (activeMovementVolumes.Count == 0)
            return;

        movementVolumesPendingRemoval.Clear();
        foreach (MovementVolumeController volume in activeMovementVolumes)
        {
            if (volume == null || !volume.isActiveAndEnabled || !volume.IsContinuousEffect)
            {
                movementVolumesPendingRemoval.Add(volume);
                continue;
            }

            volume.AccumulateContinuousMovementModifiers(
                ref movementVolumeSpeedMultiplier,
                ref movementVolumeAccelerationMultiplier,
                ref movementVolumeGroundDragMultiplier,
                ref movementVolumeConveyorVelocity);
        }

        for (int i = 0; i < movementVolumesPendingRemoval.Count; i++)
            activeMovementVolumes.Remove(movementVolumesPendingRemoval[i]);

        movementVolumeSpeedMultiplier = Mathf.Clamp(movementVolumeSpeedMultiplier, 0f, 10f);
        movementVolumeAccelerationMultiplier = Mathf.Clamp(movementVolumeAccelerationMultiplier, 0f, 10f);
        movementVolumeGroundDragMultiplier = Mathf.Clamp(movementVolumeGroundDragMultiplier, 0f, 20f);
    }

    private float ApplyMovementVolumeSpeedMultiplier(float baseSpeed)
    {
        return baseSpeed * movementVolumeSpeedMultiplier;
    }

    private float GetMovementVolumeAccelerationMultiplier()
    {
        return movementVolumeAccelerationMultiplier;
    }

    private float ResolveGroundDrag()
    {
        float baseGroundDrag = config != null ? Mathf.Max(0f, config.groundDrag) : 0f;
        return baseGroundDrag * movementVolumeGroundDragMultiplier;
    }

    private void ApplyMovementVolumeConveyor()
    {
        if (!HasAuthority() || rb == null)
            return;

        if (movementVolumeConveyorVelocity.sqrMagnitude <= 0.0001f)
            return;

        Vector3 conveyorDirection = movementVolumeConveyorVelocity.normalized;
        float targetSpeed = movementVolumeConveyorVelocity.magnitude;
        float currentSpeedAlongDirection = Vector3.Dot(rb.linearVelocity, conveyorDirection);
        float missingSpeed = targetSpeed - currentSpeedAlongDirection;
        if (missingSpeed <= 0.0001f)
            return;

        rb.AddForce(conveyorDirection * missingSpeed, ForceMode.VelocityChange);
    }

    private float GetEffectiveMovementControlLockUntil()
    {
        return Mathf.Max(damageKnockbackControlLockUntil, movementVolumeControlLockUntil);
    }

    private void ClearMovementVolumeRuntimeState()
    {
        activeMovementVolumes.Clear();
        movementVolumesPendingRemoval.Clear();
        movementVolumeSpeedMultiplier = 1f;
        movementVolumeAccelerationMultiplier = 1f;
        movementVolumeGroundDragMultiplier = 1f;
        movementVolumeConveyorVelocity = Vector3.zero;
        movementVolumeControlLockUntil = 0f;
    }
}
