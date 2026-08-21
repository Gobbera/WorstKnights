# Crouch Animator Plan

## Goal

Add crouched locomotion in all directions without duplicating the airborne flow that already exists for `Jump`, `Falling`, and `Landing`.

## Recommended Layout

Keep the base layer organized in two groups:

1. `Grounded` flow on the left side of the graph.
2. `Airborne` flow on the right side of the graph.

Suggested visual positioning:

- `Idle` at the upper-left.
- `Movement` directly below `Idle`.
- `Crouch Locomotion` directly below `Movement`.
- `Jump` to the right of the grounded group.
- `Falling` to the right of `Jump`.
- `Landing` above or slightly below `Jump`, but still on the airborne side.

This keeps all grounded transitions readable and makes crouch behave like another grounded locomotion mode instead of a separate animation system.

## Parameters

Use these Animator parameters:

- `Horizontal` float
- `Vertical` float
- `MovementMagnitude` float
- `IsMoving` bool
- `IsGrounded` bool
- `IsFalling` bool
- `Jump` trigger
- `Land` trigger
- `IsCrouching` bool
- `CrouchEnter` trigger
- `CrouchExit` trigger

`MovementAnimationController` already drives `IsCrouching` automatically.
If `CrouchEnter` and `CrouchExit` exist, the code also triggers them once for the agachar and levantar clips.
While crouched, `Horizontal` and `Vertical` stay in the full `-1..1` range so a dedicated crouch blend tree can use the same positions as walk locomotion.

## Crouch State Choice

Use one 2D blend tree called `Crouch Locomotion`.

Use `2D Freeform Directional` with:

- `(0, 0)` = `CrouchIdle`
- `(0, 1)` = `CrouchForward`
- `(0, -1)` = `CrouchBackward`
- `(1, 0)` = `CrouchRight`
- `(-1, 0)` = `CrouchLeft`
- `(1, 1)` = `CrouchForwardRight`
- `(-1, 1)` = `CrouchForwardLeft`
- `(1, -1)` = `CrouchBackwardRight`
- `(-1, -1)` = `CrouchBackwardLeft`

If you do not have diagonal clips yet, start with the four cardinals plus `CrouchIdle`. Unity will still blend between them.

## Transitions

Recommended transitions:

- `Idle` -> `Crouch Locomotion` when `IsCrouching` is true.
- `Movement` -> `Crouch Locomotion` when `IsCrouching` is true.
- `Crouch Locomotion` -> `Idle` when `IsCrouching` is false and `MovementMagnitude <= 0.1`.
- `Crouch Locomotion` -> `Movement` when `IsCrouching` is false and `MovementMagnitude > 0.1`.

Keep exit time off for these transitions so crouch feels responsive.

## Airborne Integration

Route crouch into the same airborne states instead of creating crouch-specific jump/fall states unless the game really needs them.

Recommended:

- `Crouch Locomotion` -> `Falling` when `IsGrounded` is false and `IsFalling` is true.
- `Crouch Locomotion` -> `Jump` only if crouch jump is allowed in design.
- `Landing` returns to `Crouch Locomotion` if `IsCrouching` is still true, otherwise return to `Idle`.

If crouch jump is not allowed, keep crouch grounded only and let the controller exit crouch before jump.

## Practical Build Order

1. Add `IsCrouching` to the Animator controller.
2. Add `CrouchEnter` and `CrouchExit` if you want dedicated agachar/levantar states.
3. Create `Crouch Locomotion` blend tree.
4. Wire the grounded transitions in and out of crouch.
5. Add `Crouch Locomotion` -> `Falling`.
6. Test idle crouch, moving crouch, ledge fall while crouched, and uncrouch while moving.
