# Movement Animator Reference

- Controller: `Assets/Skecth/SketchKnightAnimController.controller`
- Generated: `2026-05-07 00:19:47 UTC`
- Controller name: `SketchKnightAnimController`

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
| `LandTrigger` | `Trigger` | `Land` | `Yes` | Triggered once on air-to-ground transition. |
| `IdleTurnLeftTrigger` | `Trigger` | `IdleTurnLeft` | `Yes` | Triggered when the grounded character is idle and rotates left far enough in place. |
| `IdleTurnRightTrigger` | `Trigger` | `IdleTurnRight` | `Yes` | Triggered when the grounded character is idle and rotates right far enough in place. |

## Controller Parameters

| Name | Type | Default |
| --- | --- | --- |
| `Blend` | `Float` | `0` |
| `Horizontal` | `Float` | `0` |
| `Vertical` | `Float` | `0` |
| `SpeedMultiplier` | `Float` | `0` |
| `Jump` | `Trigger` | `trigger` |
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

## States

### `Movement`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `SketchKnightAnimController`
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
- Motion: `NewKnight`
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
- Motion: `NewKnight`
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
- Motion: `NewKnight`
- Blend Tree: `No`
- Transitions:
  - To `Idle` | Exit Time: `Off` | Duration: `0,2`
    - `IsGrounded` `If` `0`

### `Falling`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `NewKnight`
- Blend Tree: `No`
- Transitions:
  - To `Landing` | Exit Time: `Off` | Duration: `0,25`
    - `Land` `If` `0`

### `StepLeftSide`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `NewKnight`
- Blend Tree: `No`
- Transitions:
  - To `Idle` | Exit Time: `0,75` | Duration: `0,25`
    - Conditions: none
  - To `Movement` | Exit Time: `Off` | Duration: `0,15`
    - `IsMoving` `If` `0`
    - `IsGrounded` `If` `0`

### `StepRightSide`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `NewKnight`
- Blend Tree: `No`
- Transitions:
  - To `Idle` | Exit Time: `0,75` | Duration: `0,25`
    - Conditions: none
  - To `Movement` | Exit Time: `Off` | Duration: `0,15`
    - `IsMoving` `If` `0`
    - `IsGrounded` `If` `0`

### `Crouch`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `NewKnight`
- Blend Tree: `No`
- Transitions:
  - To `Crouch Movement` | Exit Time: `0,75` | Duration: `0,1`
    - Conditions: none

### `Crouch Movement`

- Layer: `Base Layer`
- Default state: `No`
- Motion: `SketchKnightAnimController`
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
- Motion: `NewKnight`
- Blend Tree: `No`
- Transitions:
  - To `Idle` | Exit Time: `0,95` | Duration: `0,12`
    - `MovementMagnitude` `Less` `0,1`
  - To `Movement` | Exit Time: `Off` | Duration: `0,15`
    - `MovementMagnitude` `Greater` `0,1`

## Notes

- `Jump` is triggered by code when a new jump request is accepted, including delayed jumps.
- `Land` is triggered by code when `IsGrounded` changes from false to true.
- `CrouchEnter` and `CrouchExit` are optional triggers used when you want dedicated agachar/levantar clips before returning to locomotion.
- `IsFalling` is driven automatically when the player is airborne and the vertical velocity is below `-0.1`.
- `VerticalSpeed` is driven automatically if you add that float parameter to the Animator.
- This document is regenerated from the saved controller asset. If a state is missing here, save the Animator or confirm you edited the same controller used by the player prefab.
