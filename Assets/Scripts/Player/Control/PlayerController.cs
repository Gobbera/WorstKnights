using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView), typeof(PlayerInputHandler), typeof(PlayerMovement))]
public class PlayerController : MonoBehaviourPun
{
    [Header("Crouch Kick")]
    [SerializeField] [Min(0f)] private float crouchKickStandDelay = 0.12f;
    [SerializeField] private IPlayerInput playerInput;
    [SerializeField] private IPlayerMovement playerMovement;
    [SerializeField] private HandEquipmentController handEquipmentController;
    [SerializeField] private PlayerEmoteWheelController emoteWheelController;
    [SerializeField] private PlayerPickupInteractor playerPickupInteractor;
    private bool kickQueuedAfterCrouchStand;
    private float queuedKickTime;

    private void Awake()
    {
        if (playerInput == null)
            playerInput = GetComponent<PlayerInputHandler>();

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (handEquipmentController == null)
            handEquipmentController = GetComponent<HandEquipmentController>();

        if (emoteWheelController == null)
            emoteWheelController = GetComponent<PlayerEmoteWheelController>();

        if (playerPickupInteractor == null)
            playerPickupInteractor = GetComponent<PlayerPickupInteractor>();
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
        if (handEquipmentController == null)
            handEquipmentController = GetComponent<HandEquipmentController>();

        if (emoteWheelController == null)
            emoteWheelController = GetComponent<PlayerEmoteWheelController>();

        if (playerPickupInteractor == null)
            playerPickupInteractor = GetComponent<PlayerPickupInteractor>();

        if (playerPickupInteractor != null && playerPickupInteractor.IsInteractionUiOpen)
        {
            ClearQueuedCrouchKick();
            playerMovement.SetSprintHeld(false);
            UpdateMovementState(Vector2.zero);
            playerMovement.Move(Vector2.zero);
            return;
        }

        Vector2 movementInput = playerInput.MovementInput;
        playerMovement.SetSprintHeld(playerInput.SprintPressed);

        UpdateMovementState(movementInput);
        if (emoteWheelController == null || !emoteWheelController.IsWheelOpen)
        {
            ProcessQueuedCrouchKick();
            ProcessKickInput();
            ProcessAttackInput();
            ProcessJumpInput();
        }

        playerMovement.Move(movementInput);
    }

    private void ProcessAttackInput()
    {
        if (handEquipmentController == null)
            handEquipmentController = GetComponent<HandEquipmentController>();

        if (handEquipmentController != null && handEquipmentController.SuppressesDefaultPrimaryAttack)
            return;

        if (playerInput.AttackPressed)
            playerMovement.Attack();
    }

    private void ProcessKickInput()
    {
        if (playerInput.KickPressed && !kickQueuedAfterCrouchStand)
            TryStartKickFromCurrentState();
    }

    private void TryStartKickFromCurrentState()
    {
        if (!playerMovement.CanStartKick)
            return;

        if (playerMovement.CurrentState != MovementState.crouching)
        {
            ClearQueuedCrouchKick();
            playerMovement.TryKick();
            return;
        }

        playerMovement.StopCrouch();
        if (playerMovement.CurrentState == MovementState.crouching)
            return;

        kickQueuedAfterCrouchStand = true;
        queuedKickTime = Time.time + Mathf.Max(0f, crouchKickStandDelay);
    }

    private void ProcessQueuedCrouchKick()
    {
        if (!kickQueuedAfterCrouchStand)
            return;

        if (Time.time < queuedKickTime)
            return;

        ClearQueuedCrouchKick();
        playerMovement.TryKick();
    }

    private void ClearQueuedCrouchKick()
    {
        kickQueuedAfterCrouchStand = false;
        queuedKickTime = 0f;
    }

    private void ProcessJumpInput()
    {
        if (playerInput.JumpPressed)
            playerMovement.Jump();
    }

    private void UpdateMovementState(Vector2 movementInput)
    {
        playerMovement.RefreshGrounding();

        if (playerMovement.IsJumpQueued)
        {
            playerMovement.SetState(MovementState.jumping);
            return;
        }

        if (!playerMovement.IsGrounded)
        {
            if (playerMovement.CurrentState != MovementState.jumping)
                playerMovement.SetState(MovementState.air);

            return;
        }

        if (playerMovement.IsMovementControlLocked)
        {
            playerMovement.SetState(
                playerMovement.CurrentState == MovementState.crouching
                    ? MovementState.crouching
                    : MovementState.idle);
            return;
        }

        bool suppressCrouchForKick = playerMovement.IsKickActionActive || kickQueuedAfterCrouchStand;
        if (playerInput.CrouchHeld && !suppressCrouchForKick)
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
            bool canSprint = playerInput.SprintPressed
                && IsForwardSprintInput(movementInput)
                && playerMovement.CanSprint;
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
