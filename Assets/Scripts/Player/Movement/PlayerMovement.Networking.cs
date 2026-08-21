using Photon.Pun;
using UnityEngine;

public partial class PlayerMovement
{
    private bool HasAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private void ApplyAuthorityState()
    {
        if (rb == null)
            return;

        bool isMine = HasAuthority();
        rb.isKinematic = !isMine;
        rb.useGravity = isMine;

        if (!isMine)
            rb.linearVelocity = Vector3.zero;
    }

    private void ApplyRemoteState()
    {
        if (!hasNetworkState)
            return;

        SetMovementStateInternal(networkState);
        Input = networkInput;
        AnimationInput = networkAnimationInput;
        IsGrounded = networkGrounded;
        jumpQueued = networkJumpQueued;
        attackAnimationSequence = networkAttackAnimationSequence;
        attackComboStep = networkAttackComboStep;
        kickAnimationSequence = networkKickAnimationSequence;
        jumpAnimationSequence = networkJumpAnimationSequence;
        landingAnimationSequence = networkLandingAnimationSequence;
        pickupAnimationSequence = networkPickupAnimationSequence;
        pickupAnimationHand = networkPickupAnimationHand;
        drawAnimationSequence = networkDrawAnimationSequence;
        drawAnimationHand = networkDrawAnimationHand;
        damageAnimationSequence = networkDamageAnimationSequence;
        currentDamageAnimationType = networkDamageAnimationType;
        emoteAnimationSequence = networkEmoteAnimationSequence;
        currentEmoteType = networkEmoteType;
        rightHandOccupied = networkRightHandOccupied;
        leftHandOccupied = networkLeftHandOccupied;
        leftHandTorchEquipped = networkLeftHandTorchEquipped;

        OnSlope = false;
        IsSlidingOnSlope = false;
        IsTouchingWall = false;
        rb.linearDamping = 0f;
    }

    private void InterpolateRemoteTransform(float deltaTime)
    {
        if (!hasNetworkState || rb == null)
            return;

        float distanceToTarget = Vector3.Distance(rb.position, networkPosition);
        if (distanceToTarget > remoteTeleportDistance)
        {
            rb.position = networkPosition;
            rb.rotation = networkRotation;
            return;
        }

        Vector3 predictedPosition = networkPosition + networkVelocity * (deltaTime * 0.5f);
        float positionT = 1f - Mathf.Exp(-remotePositionLerpSpeed * deltaTime);
        float rotationT = 1f - Mathf.Exp(-remoteRotationLerpSpeed * deltaTime);

        rb.MovePosition(Vector3.Lerp(rb.position, predictedPosition, positionT));
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, networkRotation, rotationT));
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(rb != null ? rb.linearVelocity : Vector3.zero);
            stream.SendNext((int)CurrentState);
            stream.SendNext(Input);
            stream.SendNext(AnimationInput);
            stream.SendNext(IsGrounded);
            stream.SendNext(jumpQueued);
            stream.SendNext(attackAnimationSequence);
            stream.SendNext(attackComboStep);
            stream.SendNext(IsAttackMovementSlowActive());
            stream.SendNext(kickAnimationSequence);
            stream.SendNext(IsKickMovementSlowActive());
            stream.SendNext(IsFallMovementSlowActive());
            stream.SendNext(jumpAnimationSequence);
            stream.SendNext(landingAnimationSequence);
            stream.SendNext(pickupAnimationSequence);
            stream.SendNext((int)pickupAnimationHand);
            stream.SendNext(drawAnimationSequence);
            stream.SendNext((int)drawAnimationHand);
            stream.SendNext(damageAnimationSequence);
            stream.SendNext((int)currentDamageAnimationType);
            stream.SendNext(emoteAnimationSequence);
            stream.SendNext((int)currentEmoteType);
            stream.SendNext(rightHandOccupied);
            stream.SendNext(leftHandOccupied);
            stream.SendNext(leftHandTorchEquipped);
            stream.SendNext(lookYawOffset);
            stream.SendNext(lookPitch);
            return;
        }

        networkPosition = (Vector3)stream.ReceiveNext();
        networkRotation = (Quaternion)stream.ReceiveNext();
        networkVelocity = (Vector3)stream.ReceiveNext();
        networkState = (MovementState)(int)stream.ReceiveNext();
        networkInput = (Vector2)stream.ReceiveNext();
        networkAnimationInput = (Vector2)stream.ReceiveNext();
        networkGrounded = (bool)stream.ReceiveNext();
        networkJumpQueued = (bool)stream.ReceiveNext();
        networkAttackAnimationSequence = (int)stream.ReceiveNext();
        networkAttackComboStep = (int)stream.ReceiveNext();
        networkAttackMovementSlowActive = (bool)stream.ReceiveNext();
        networkKickAnimationSequence = (int)stream.ReceiveNext();
        networkKickMovementSlowActive = (bool)stream.ReceiveNext();
        networkFallMovementSlowActive = (bool)stream.ReceiveNext();
        networkJumpAnimationSequence = (int)stream.ReceiveNext();
        networkLandingAnimationSequence = (int)stream.ReceiveNext();
        networkPickupAnimationSequence = (int)stream.ReceiveNext();
        networkPickupAnimationHand = (HandType)(int)stream.ReceiveNext();
        networkDrawAnimationSequence = (int)stream.ReceiveNext();
        networkDrawAnimationHand = (HandType)(int)stream.ReceiveNext();
        networkDamageAnimationSequence = (int)stream.ReceiveNext();
        networkDamageAnimationType = (PlayerDamageAnimationType)(int)stream.ReceiveNext();
        networkEmoteAnimationSequence = (int)stream.ReceiveNext();
        networkEmoteType = (PlayerEmoteType)(int)stream.ReceiveNext();
        networkRightHandOccupied = (bool)stream.ReceiveNext();
        networkLeftHandOccupied = (bool)stream.ReceiveNext();
        networkLeftHandTorchEquipped = (bool)stream.ReceiveNext();
        networkLookYawOffset = (float)stream.ReceiveNext();
        networkLookPitch = (float)stream.ReceiveNext();

        if (!hasNetworkState)
        {
            hasNetworkState = true;
            transform.SetPositionAndRotation(networkPosition, networkRotation);
        }

        ApplyRemoteState();
    }
}
