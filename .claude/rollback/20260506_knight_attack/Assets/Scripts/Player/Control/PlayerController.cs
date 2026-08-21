using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView), typeof(PlayerInputHandler), typeof(PlayerMovement))]
public class PlayerController : MonoBehaviourPun
{
    [SerializeField] private IPlayerInput playerInput;
    [SerializeField] private IPlayerMovement playerMovement;

    private void Awake()
    {
        if (playerInput == null)
            playerInput = GetComponent<PlayerInputHandler>();

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        if (!photonView.IsMine)
            return;

        if (!ValidateComponents())
            return;

        playerInput.UpdateInput();
        HandleMovement();
    }

    private bool ValidateComponents()
    {
        if (playerInput != null && playerMovement != null)
            return true;

        Debug.LogError("PlayerController: Missing PlayerInputHandler or PlayerMovement component!", gameObject);
        return false;
    }

    private void HandleMovement()
    {
        Vector2 movementInput = playerInput.MovementInput;

        UpdateMovementState(movementInput);
        ProcessJumpInput();
        playerMovement.Move(movementInput);
    }

    private void ProcessJumpInput()
    {
        if (playerInput.JumpPressed)
            playerMovement.Jump();
    }

    private void UpdateMovementState(Vector2 movementInput)
    {
        if (!playerMovement.IsGrounded || playerMovement.IsJumpQueued)
        {
            playerMovement.SetState(MovementState.air);
            return;
        }

        if (playerInput.CrouchHeld)
        {
            playerMovement.StartCrouch();
            return;
        }

        if (playerMovement.CurrentState == MovementState.crouching)
        {
            playerMovement.StopCrouch();
            if (playerMovement.CurrentState == MovementState.crouching)
                return;
        }

        if (movementInput.sqrMagnitude > 0.01f)
        {
            bool canSprint = playerInput.SprintPressed && IsForwardSprintInput(movementInput);
            playerMovement.SetState(canSprint ? MovementState.sprinting : MovementState.walking);
            return;
        }

        playerMovement.SetState(MovementState.idle);
    }

    private static bool IsForwardSprintInput(Vector2 movementInput)
    {
        return movementInput.y > 0.01f;
    }
}
