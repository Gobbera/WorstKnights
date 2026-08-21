# Player Movement Architecture

This folder contains the multiplayer-ready player locomotion stack currently used by the project.

## Active Runtime Flow

1. `Input/PlayerInputHandler.cs`
   Reads input and resolves `InputConfig`.

2. `Control/PlayerController.cs`
   Converts input intent into movement commands and state transitions.

3. `Movement/PlayerMovement*.cs`
   Handles physics, slopes, steps, jumps, direction-change inertia, landing sequencing and Photon serialization.

4. `Camera/MouseLook.cs`
   Applies owner-local yaw to the player root and pitch to the active camera.

5. `Camera/HeadBob/HeadBobController.cs`
   Applies owner-local first-person head bob based on movement speed and state.

6. `Animation/MovementAnimationController*.cs`
   Drives Animator parameters and triggers from movement state plus replicated jump/landing/emote sequences and active-hand grip booleans.

7. `Setup/PlayerSetup.cs`
   Enables local-only components such as input and cameras, hides the local third-person model and syncs nicknames.

## Folder Layout

- `Animation/`
  Runtime animator bridge plus the mirror asset bindings used to stay aligned with the Animator Controller.
- `Camera/`
  Local look/camera ownership logic plus first-person camera motion such as head bob.
- `Control/`
  High-level player orchestration.
- `Contracts/`
  Shared player input and movement interfaces.
- `Input/`
  Input config and input polling.
- `Markers/`
  Lightweight marker components used by setup/discovery code.
- `Movement/`
  The movement motor and its partial files grouped by responsibility.
- `Setup/`
  Ownership-dependent player bootstrap.
- `State/`
  Shared enums and state definitions.

## Photon Notes

- `PlayerMovement` is the observed component on the `PhotonView`.
- Local authority is checked inside both `PlayerController` and `PlayerMovement`.
- Remote players receive replicated position, rotation, velocity, state, filtered input, hand-occupation booleans, and animation sequence counters from `PlayerMovement.OnPhotonSerializeView`.

## Current Prefab Expectations

On the root player object:

- `PhotonView`
- `Rigidbody`
- `CapsuleCollider`
- `PlayerController`
- `PlayerMovement`
- `PlayerInputHandler`
- `PlayerSetup`

On child objects:

- An `Orientation` child transform referenced by `PlayerMovement`
- A camera with `MouseLook` for the local owner
- A third-person model child with `Animator` and `MovementAnimationController`
- Optional marker components such as `FP_Camera` and `ThirdPersonModel`

## Configuration Assets

- `InputConfig` is loaded automatically from `Assets/Resources/InputConfig.asset` if not assigned in the inspector.
- `MovementConfig` is referenced directly by the player prefab and currently lives in `PlayerAssets/MovementConfig.asset`.
- `HeadBobProfile_Default` is loaded from `Assets/Resources/HeadBobProfile_Default.asset` and drives the first-person camera head bob.
- `Assets/Resources/Player.prefab` is the single source of truth for both Photon instantiation and the scene player reference in `FieldTestCharacter`.

## Related Docs

- `Docs/HeadBobSystem.md`
  Technical overview and tuning notes for the current first-person head bob implementation.
