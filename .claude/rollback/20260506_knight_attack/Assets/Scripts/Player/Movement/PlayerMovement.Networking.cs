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
        jumpAnimationSequence = networkJumpAnimationSequence;
        landingAnimationSequence = networkLandingAnimationSequence;

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
            stream.SendNext(jumpAnimationSequence);
            stream.SendNext(landingAnimationSequence);
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
        networkJumpAnimationSequence = (int)stream.ReceiveNext();
        networkLandingAnimationSequence = (int)stream.ReceiveNext();

        if (!hasNetworkState)
        {
            hasNetworkState = true;
            transform.SetPositionAndRotation(networkPosition, networkRotation);
        }

        ApplyRemoteState();
    }
}
