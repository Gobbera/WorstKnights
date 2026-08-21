# Movement Animator Reference

- Controller: `Assets/Skecth/KnightAnimController.controller`
- Generated: `2026-08-05 20:52:49 UTC`
- Controller name: `KnightAnimController`

## Saved Snapshot

- `Landing` state in saved controller: yes
- `Falling` state in saved controller: yes
- `Land` trigger in saved controller: yes
- `IsFalling`/`Falling` bool in saved controller: yes

## Runtime Parameters Driven By Code

| Semantic | Expected Type | Bound Parameter | In Controller | Runtime Source |
| --- | --- | --- | --- | --- |
| `Horizontal` | `Float` | `Horizontal` | `Yes` | Local X input after wall and slope filtering. Crouch keeps the full -1..1 directional range for a dedicated crouch blend tree. |
| `Vertical` | `Float` | `Vertical` | `Yes` | Local Y input after wall and slope filtering. Crouch keeps the full -1..1 directional range for a dedicated crouch blend tree. |
| `IsGrounded` | `Bool` | `IsGrounded` | `Yes` | True while the movement probe considers the player grounded. |
| `IsCrouching` | `Bool` | `IsCrouching` | `Yes` | True while `CurrentState` is `crouching`. |
| `IsSprinting` | `Bool` | `IsSprinting` | `No` | True while `CurrentState` is `sprinting`. |
| `IsJumping` | `Bool` | `IsJumping` | `No` | True while a jump is queued or the movement state is airborne. |
| `IsFalling` | `Bool` | `IsFalling` | `Yes` | True while airborne and vertical velocity is descending. |
| `MovementMagnitude` | `Float` | `MovementMagnitude` | `Yes` | Absolute locomotion blend magnitude. |
| `IsMoving` | `Bool` | `IsMoving` | `Yes` | True when locomotion magnitude is above 0.1. |
| `SpeedMultiplier` | `Float` | `SpeedMultiplier` | `Yes` | Optional locomotion scale. Sprint uses 2 and crouch reports 0.5 for controllers that still need this value. |
| `VerticalSpeed` | `Float` | `VerticalSpeed` | `No` | Current Rigidbody/network vertical velocity. |
| `CrouchEnterTrigger` | `Trigger` | `CrouchEnter` | `Yes` | Triggered once when the grounded movement state enters `crouching`. |
| `CrouchExitTrigger` | `Trigger` | `CrouchExit` | `Yes` | Triggered once when crouch is released and the character can stand up. |
| `JumpTrigger` | `Trigger` | `Jump` | `Yes` | Triggered once per accepted jump request. |
| `AttackTrigger` | `Trigger` | `Attack` | `Yes` | Fallback trigger for Attack_1; combo steps are played directly by code from the replicated combo step. |
| `KickTrigger` | `Trigger` | `Kick` | `Yes` | Triggered once per accepted kick request and used to play the masked Kick layer. |
| `LandTrigger` | `Trigger` | `Land` | `Yes` | Triggered once on air-to-ground transition. |
| `IdleTurnLeftTrigger` | `Trigger` | `IdleTurnLeft` | `Yes` | Triggered when the grounded character is idle and rotates left far enough in place. |
| `IdleTurnRightTrigger` | `Trigger` | `IdleTurnRight` | `Yes` | Triggered when the grounded character is idle and rotates right far enough in place. |
| `RightHandOccupied` | `Bool` | `IsRightHandOccupied` | `Yes` | True while the active right-hand slot contains an item, so a grip layer can close that hand over other animations. |
| `LeftHandOccupied` | `Bool` | `IsLeftHandOccupied` | `Yes` | True while the active left-hand slot contains an item, so a grip layer can close that hand over other animations. |
| `ThumbsUpTrigger` | `Trigger` | `ThumbsUp` | `Yes` | Triggered once when the player requests the thumbs-up emote. |

## Controller Parameters

| Name | Type | Default |
| --- | --- | --- |
| `Blend` | `Float` | `0` |
| `Horizontal` | `Float` | `0` |
| `Vertical` | `Float` | `0` |
| `SpeedMultiplier` | `Float` | `0` |
| `Jump` | `Trigger` | `trigger` |
| `Attack` | `Trigger` | `trigger` |
| `Kick` | `Trigger` | `trigger` |
| `MovementMagnitude` | `Float` | `0` |
| `IsMoving` | `Bool` | `false` |
| `IsGrounded` | `Bool` | `false` |
| `Land` | `Trigger` | `trigger` |
| `IsFalling` | `Bool` | `false` |
| `IdleTurnLeft` | `Trigger` | `trigger` |
| `IdleTurnRight` | `Trigger` | `trigger` |
| `IsCrouching` | `Bool` | `false` |
| `CrouchEnter` | `Trigger` | `trigger` |
| `CrouchExit` | `Trigger` | `trigger` |
| `IsRightHandOccupied` | `Bool` | `false` |
| `IsLeftHandOccupied` | `Bool` | `false` |
| `IsLeftTorchEquipped` | `Bool` | `false` |
| `ThumbsUp` | `Trigger` | `trigger` |

## States

### `Movement`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `KnightAnimController`
- Blend Tree: `Yes`
- Transitions:
  - To `Idle` | Exit Time: `Off` | Duration: `0,15`
    - `IsMoving` `IfNot` `0,1`
  - To `Jump` | Exit Time: `Off` | Duration: `0,2`
    - `Jump` `If` `0`
  - To `Falling` | Exit Time: `Off` | Duration: `0,18`
    - `IsGrounded` `IfNot` `0`
    - `IsFalling` `If` `0`
  - To `Crouch` | Exit Time: `0,75` | Duration: `0,25`
    - `CrouchEnter` `If` `0`

### `Idle`

- Layer: `Base Layer`
- Default state: `Yes`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Movement` | Exit Time: `Off` | Duration: `0,2`
    - `MovementMagnitude` `Greater` `0,1`
  - To `Jump` | Exit Time: `Off` | Duration: `0`
    - `Jump` `If` `0`
  - To `Falling` | Exit Time: `Off` | Duration: `0,18`
    - `IsGrounded` `IfNot` `0`
    - `IsFalling` `If` `0`
  - To `StepLeftSide` | Exit Time: `Off` | Duration: `0,15`
    - `IdleTurnLeft` `If` `0`
    - `IsGrounded` `If` `0`
    - `IsMoving` `IfNot` `0`
  - To `StepRightSide` | Exit Time: `Off` | Duration: `0,15`
    - `IdleTurnRight` `If` `0`
    - `IsGrounded` `If` `0`
    - `IsMoving` `IfNot` `0`
  - To `Crouch` | Exit Time: `Off` | Duration: `0,15`
    - `CrouchEnter` `If` `0`

### `Jump`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Falling` | Exit Time: `Off` | Duration: `0,25`
    - `IsGrounded` `IfNot` `0`
    - `IsFalling` `If` `0`
  - To `Landing` | Exit Time: `Off` | Duration: `0,05`
    - `Land` `If` `0`

### `Landing`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Idle` | Exit Time: `Off` | Duration: `0,2`
    - `IsGrounded` `If` `0`

### `Falling`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Landing` | Exit Time: `Off` | Duration: `0,25`
    - `Land` `If` `0`

### `StepLeftSide`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Idle` | Exit Time: `0,75` | Duration: `0,15`
    - Conditions: none
  - To `Movement` | Exit Time: `Off` | Duration: `0,15`
    - `IsMoving` `If` `0`
    - `IsGrounded` `If` `0`

### `StepRightSide`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Idle` | Exit Time: `0,75` | Duration: `0,15`
    - Conditions: none
  - To `Movement` | Exit Time: `Off` | Duration: `0,15`
    - `IsMoving` `If` `0`
    - `IsGrounded` `If` `0`

### `Crouch`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Crouch Movement` | Exit Time: `0,75` | Duration: `0,1`
    - Conditions: none

### `Crouch Movement`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `KnightAnimController`
- Blend Tree: `Yes`
- Transitions:
  - To `Stand Up` | Exit Time: `0,75` | Duration: `0,25`
    - `CrouchExit` `If` `0`
  - To `Falling` | Exit Time: `Off` | Duration: `0,18`
    - `IsGrounded` `IfNot` `0`
    - `IsFalling` `If` `0`

### `Stand Up`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Idle` | Exit Time: `0,95` | Duration: `0,12`
    - `MovementMagnitude` `Less` `0,1`
  - To `Movement` | Exit Time: `Off` | Duration: `0,15`
    - `MovementMagnitude` `Greater` `0,1`

### `Reaction Damage`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Idle` | Exit Time: `0,95` | Duration: `0,15`
    - Conditions: none

### `Empty`

- Layer: `Right Hand Grip`
- Default state: `Yes`
- Motion: `None`
- Blend Tree: `No`
- Transitions:
  - To `Grip` | Exit Time: `Off` | Duration: `0,05`
    - `IsRightHandOccupied` `If` `0`

### `Grip`

- Layer: `Right Hand Grip`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `Off` | Duration: `0,05`
    - `IsRightHandOccupied` `IfNot` `0`

### `Empty`

- Layer: `Left Hand Grip`
- Default state: `Yes`
- Motion: `None`
- Blend Tree: `No`
- Transitions:
  - To `Grip` | Exit Time: `Off` | Duration: `0,25`
    - `IsLeftHandOccupied` `If` `0`
    - `IsLeftTorchEquipped` `IfNot` `0`

### `Grip`

- Layer: `Left Hand Grip`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `Off` | Duration: `0,25`
    - `IsLeftHandOccupied` `IfNot` `0`

### `Empty`

- Layer: `Thumbs Up`
- Default state: `Yes`
- Motion: `None`
- Blend Tree: `No`
- Transitions:
  - To `ThumbsUp` | Exit Time: `Off` | Duration: `0,05`
    - `ThumbsUp` `If` `0`

### `ThumbsUp`

- Layer: `Thumbs Up`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `1` | Duration: `0,92`
    - Conditions: none

### `Point`

- Layer: `Thumbs Up`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `1` | Duration: `0,05`
    - Conditions: none

### `Empty`

- Layer: `Upper Body Attack`
- Default state: `Yes`
- Motion: `None`
- Blend Tree: `No`
- Transitions:
  - To `Attack_1` | Exit Time: `Off` | Duration: `0,05`
    - `Attack` `If` `0`

### `Attack_1`

- Layer: `Upper Body Attack`
- Default state: `No`
- Motion: `Attack_Sw_01`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `0,95` | Duration: `0,15`
    - Conditions: none

### `Attack_2`

- Layer: `Upper Body Attack`
- Default state: `No`
- Motion: `Attack_Sw_02`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `0,95` | Duration: `0,15`
    - Conditions: none

### `Attack_3`

- Layer: `Upper Body Attack`
- Default state: `No`
- Motion: `Attack_Sw_03`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `0,95` | Duration: `0,15`
    - Conditions: none

### `Pick Up Item Right`

- Layer: `Upper Body Attack`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `1` | Duration: `0,05`
    - Conditions: none

### `Pick Up Item Left`

- Layer: `Upper Body Attack`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `1` | Duration: `0,05`
    - Conditions: none

### `Right Drawn`

- Layer: `Upper Body Attack`
- Default state: `No`
- Motion: `Right Drawn`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `0,95` | Duration: `0,12`
    - Conditions: none

### `Left Drawn`

- Layer: `Upper Body Attack`
- Default state: `No`
- Motion: `Left Drawn`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `0,95` | Duration: `0,12`
    - Conditions: none

### `Empty`

- Layer: `Torch Grip`
- Default state: `Yes`
- Motion: `None`
- Blend Tree: `No`
- Transitions:
  - To `Torch Grip` | Exit Time: `0,75` | Duration: `0,25`
    - `IsLeftHandOccupied` `If` `0`
    - `IsLeftTorchEquipped` `If` `0`

### `Torch Grip`

- Layer: `Torch Grip`
- Default state: `No`
- Motion: `Knight`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `Off` | Duration: `0,1`
    - `IsLeftTorchEquipped` `IfNot` `0`

### `Empty`

- Layer: `Kick`
- Default state: `Yes`
- Motion: `None`
- Blend Tree: `No`
- Transitions:
  - To `Kick` | Exit Time: `Off` | Duration: `0,05`
    - `Kick` `If` `0`

### `Kick`

- Layer: `Kick`
- Default state: `No`
- Motion: `Kick`
- Blend Tree: `No`
- Transitions:
  - To `Empty` | Exit Time: `0,9` | Duration: `0,08`
    - Conditions: none

## Notes

- `Jump` is triggered by code when a new jump request is accepted, including delayed jumps.
- `Attack` is triggered by code when a new attack request is accepted and replicated.
- `Land` is triggered by code when `IsGrounded` changes from false to true.
- `ThumbsUp` is triggered by code when the local player presses the configured emote shortcut.
- `CrouchEnter` and `CrouchExit` are optional triggers used when you want dedicated agachar/levantar clips before returning to locomotion.
- `IsFalling` is driven automatically when the player is airborne and the vertical velocity is below `-0.1`.
- `VerticalSpeed` is driven automatically if you add that float parameter to the Animator.
- `IsRightHandOccupied` and `IsLeftHandOccupied` follow the active equipped slots so hand-pose layers can blend over locomotion.
- This document is regenerated from the saved controller asset. If a state is missing here, save the Animator or confirm you edited the same controller used by the player prefab.
