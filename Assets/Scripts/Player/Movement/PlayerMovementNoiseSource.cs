using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public sealed class PlayerMovementNoiseSource : MonoBehaviour
{
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField, Min(0f)] private float crouchRadius = 0.5f;
    [SerializeField, Min(0f)] private float walkRadius = 7f;
    [SerializeField, Min(0f)] private float sprintRadius = 10f;
    [SerializeField, Min(0f)] private float minimumPlanarSpeed = 0.15f;

    public float CurrentRadius
    {
        get
        {
            if (playerMovement == null)
                playerMovement = GetComponent<PlayerMovement>();

            return playerMovement != null
                ? ResolveRadius(playerMovement.CurrentState, playerMovement.PlanarSpeed, playerMovement.IsGrounded)
                : 0f;
        }
    }

    public NoiseType CurrentNoiseType => playerMovement != null
        && playerMovement.CurrentState == MovementState.sprinting
            ? NoiseType.Sprint
            : NoiseType.Footstep;

    public float ResolveRadius(MovementState state, float planarSpeed, bool isGrounded)
    {
        if (!isGrounded || planarSpeed < Mathf.Max(0f, minimumPlanarSpeed))
            return 0f;

        return state switch
        {
            MovementState.crouching => Mathf.Max(0f, crouchRadius),
            MovementState.walking => Mathf.Max(0f, walkRadius),
            MovementState.sprinting => Mathf.Max(0f, sprintRadius),
            _ => 0f
        };
    }
}
