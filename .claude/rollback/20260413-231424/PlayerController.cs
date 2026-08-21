using Photon.Pun;
using UnityEngine;

public class PlayerController : MonoBehaviourPun
{
    [SerializeField] private IPlayerInput playerInput;
    [SerializeField] private IPlayerMovement playerMovement;

    private void Awake()
    {
        // Find components - try SerializeField first, then GetComponent
        if (playerInput == null)
            playerInput = GetComponent<PlayerInputHandler>();
        
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        if (!photonView.IsMine) return; // Only process input for local player

        // Null checks - components must be present on local player
        if (playerInput == null || playerMovement == null)
        {
            Debug.LogError("PlayerController: Missing PlayerInputHandler or PlayerMovement component!", gameObject);
            return;
        }

        playerInput.UpdateInput();
        HandleMovement();
    }

    private void HandleMovement()
    {
        Vector2 movementInput = playerInput.MovementInput;
        bool jumpPressed = playerInput.JumpPressed;

        if (playerInput.CrouchPressed)
        {
            playerMovement.StartCrouch();
        }
        else if (playerInput.CrouchReleased)
        {
            playerMovement.StopCrouch();
        }

        if (jumpPressed)
            playerMovement.Jump();

        playerMovement.Move(movementInput);

        if (playerMovement.CurrentState != MovementState.crouching)
        {
            if (!playerMovement.IsGrounded || playerMovement.IsJumpQueued)
            {
                playerMovement.SetState(MovementState.air);
            }
            else if (movementInput.sqrMagnitude > 0.01f)
            {
                if (playerInput.SprintPressed)
                    playerMovement.SetState(MovementState.sprinting);
                else
                    playerMovement.SetState(MovementState.walking);
            }
            else
            {
                playerMovement.SetState(MovementState.idle);
            }
        }
    }
}
