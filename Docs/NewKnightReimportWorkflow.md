# NewKnight Reimport Workflow

This document is the source of truth for future reimports of `Assets/Blender/NewKnight.fbx`.
The goal is to replace or update the FBX without losing clip setup, avatar setup, or Animator wiring.

## Canonical Assets

- FBX: `Assets/Blender/NewKnight.fbx`
- FBX meta: `Assets/Blender/NewKnight.fbx.meta`
- Animator Controller: `Assets/Skecth/KnightAnimController.controller`
- Player prefab: `Assets/Resources/Player.prefab`
- Runtime animation code: `Assets/Scripts/Player/Animation/MovementAnimationController.cs`
- Generated controller snapshot: `Docs/MovementAnimatorReference.md`

## Non-Negotiable Rules

1. Replace `Assets/Blender/NewKnight.fbx` in place. Do not rename it and do not delete its `.meta`.
2. Keep the clip names exactly as listed below. The `Knight|...` names are the import standard.
3. Rig must stay `Generic` with `Avatar Definition = Create From This Model`.
4. After reimport, the `Animator` on `Assets/Resources/Player.prefab` must point to the embedded avatar inside `NewKnight.fbx` (`fileID: 9000000`, guid `05ebaeff50a4a0d4b85edfc4e6af66a5`).
5. Reimporting the FBX does not fix controller references automatically. The controller must still be validated after every animation update.
6. Do not export straight from Blender into the live `Assets/` path if Unity auto-refresh is active. Finish the export outside the project first, then replace the FBX only after the file is fully written.

## Canonical Import Settings

These are the settings that matter for this project and must be restored if Unity resets them:

- `Import Animation`: On
- `Animation Type`: `Generic`
- `Avatar Definition`: `Create From This Model`
- `Global Scale`: `1`
- `Preserve Hierarchy`: Off
- `Optimize Bones`: On

Every current clip in `NewKnight.fbx.meta` also uses the same transform preservation pattern:

- `Keep Original Position Y`: On
- `Keep Original Position XZ`: Off
- `Keep Original Orientation`: Off

## Canonical Clip Table

| Clip name | Loop | Current usage |
| --- | --- | --- |
| `Knight|1Pose` | No | Reference pose, not used by the current controller |
| `Knight|Air` | Yes | `Falling` state |
| `Knight|Crouch` | No | `Crouch` state |
| `Knight|Crouch Stand Up` | No | `Stand Up` state |
| `Knight|Crouched Idle` | Yes | `Crouch Movement` blend tree center `(0, 0)` |
| `Knight|Crouched Walk Back` | Yes | `Crouch Movement` backward and current backward diagonal fallback |
| `Knight|Crouched Walk Forward` | Yes | `Crouch Movement` forward and current forward diagonal fallback |
| `Knight|Crouched Walk Left` | Yes | `Crouch Movement` left |
| `Knight|Crouched Walk Right` | Yes | `Crouch Movement` right |
| `Knight|Idle` | Yes | `Idle` state |
| `Knight|Jump` | No | `Jump` state |
| `Knight|Landing` | No | `Landing` state |
| `Knight|Run` | Yes | `Movement` blend tree forward run `(0, 2)` |
| `Knight|StepLeftSide` | No | `StepLeftSide` state |
| `Knight|StepRightSide` | No | `StepRightSide` state |
| `Knight|Walk` | Yes | `Movement` blend tree forward and current forward diagonal fallback |
| `Knight|Walk Back` | Yes | `Movement` blend tree backward and current backward diagonal fallback |
| `Knight|Walk Left` | Yes | `Movement` blend tree left |
| `Knight|Walk Right` | Yes | `Movement` blend tree right |

## Animator Contract That Cannot Drift

The runtime code expects this controller contract to stay stable.

### States directly played by code

- `Jump`
- `Landing`
- `Crouch`
- `Stand Up`

If any of those state names change, update `MovementAnimationController.cs` too.

### Parameters and triggers currently used by code

- `Horizontal`
- `Vertical`
- `MovementMagnitude`
- `IsMoving`
- `IsGrounded`
- `IsFalling`
- `IsCrouching`
- `SpeedMultiplier`
- `Jump`
- `Land`
- `CrouchEnter`
- `CrouchExit`
- `IdleTurnLeft`
- `IdleTurnRight`

### Parameters supported by code but currently absent from the controller

- `IsSprinting`
- `IsJumping`
- `VerticalSpeed`

## Current Controller Notes

- `Jump` state speed is currently `0.23`.
- `Crouch Movement` uses a 2D blend tree driven by full `Horizontal` / `Vertical` input in the `-1..1` range while crouched.
- `Docs/MovementAnimatorReference.md` is generated from the saved controller asset and should be treated as the latest controller snapshot.

### Current detail in the main Movement blend tree

The `Movement` blend tree is currently sourced from `NewKnight.fbx`.

- High-speed forward nodes use `Knight|Run Forward`, `Knight|Run Forward Left`, and a mirrored right-side equivalent.
- Right strafe / sprint-right slots currently mirror the left-side clips instead of relying on separate right-side sprint clips.

That means:

- reimporting `NewKnight.fbx` still does not guarantee the desired mirror layout survives unchanged
- after a reimport, verify the right-side `Movement` nodes still use the intended mirrored setup where applicable
- do not assume Unity will preserve every blend-tree child motion exactly as before unless you re-check the controller asset

## Reimport Flow

1. Export the updated Blender file to a temporary path outside the Unity project.
2. Wait for Blender to finish writing the file completely.
3. Replace `Assets/Blender/NewKnight.fbx` with that finished file.
4. Keep `Assets/Blender/NewKnight.fbx.meta` untouched so the FBX guid stays the same.
5. In Unity, select `NewKnight.fbx` and confirm:
   - `Animation Type = Generic`
   - `Avatar Definition = Create From This Model`
   - `Import Animation = On`
6. Open the Animation tab and verify every clip listed in the canonical clip table still exists with the exact same name.
7. Reapply the correct `Loop Time` flags from the canonical clip table if Unity reset them.
8. For all clips, confirm:
   - `Keep Original Position Y = On`
   - `Keep Original Position XZ = Off`
   - `Keep Original Orientation = Off`
9. Click `Apply`.
10. Open `Assets/Resources/Player.prefab` and verify the `Animator` uses:
   - controller `Assets/Skecth/KnightAnimController.controller`
   - avatar `fileID: 9000000`, guid `05ebaeff50a4a0d4b85edfc4e6af66a5`
11. Open `Assets/Skecth/KnightAnimController.controller` and verify at minimum:
   - `Idle`
   - `Movement`
   - `Jump`
   - `Falling`
   - `Landing`
   - `Crouch`
   - `Crouch Movement`
   - `Stand Up`
   - `StepLeftSide`
   - `StepRightSide`
12. If any motion field is missing after reimport, rebind it before saving the controller.
13. Test in `Assets/Scenes/FieldTestCharacter.unity`:
   - idle
   - walk forward/back/left/right
   - crouch enter
   - crouch locomotion
   - stand up
   - jump
   - falling
   - landing
   - idle turn left/right

## If Unity reports `File is corrupted`

When Unity says the processed byte count does not match the file size, treat it as an incomplete write first.

Do this:

1. Stop exporting directly into `Assets/Blender/NewKnight.fbx`.
2. Export the FBX to a temporary folder outside the project.
3. Wait until the export fully finishes.
4. Copy the finished file over `Assets/Blender/NewKnight.fbx`.
5. In Unity, right-click `NewKnight.fbx` and choose `Reimport`.

If the error still happens after that, the exported FBX itself is bad and must be exported again from Blender.

## If some animations do not export from Blender

When clips are missing from the FBX, the problem is usually in Blender before Unity ever sees the file.

### Most common causes

- The animation exists as an `Action`, but it is not pushed to the NLA and is not included by the FBX exporter mode you used.
- The `Action` has no fake user and gets lost when Blender changes the active action.
- The `Action` is on the wrong armature or object.
- The strip is muted, disabled, or its frame range is wrong.
- The exporter is using only the active action instead of exporting all actions.
- Constraints or IK were not baked, so the exported animation appears empty or incomplete.
- Some actions were renamed, merged, or replaced and no longer match the expected `Knight|...` naming standard.

### Blender pre-export checklist

1. Open the `Action Editor` and verify every animation you want really exists as its own `Action`.
2. Confirm each action uses the canonical naming pattern, for example:
   - `Knight|Idle`
   - `Knight|Jump`
   - `Knight|Crouch`
3. For every action you want to keep, enable the shield icon (`Fake User`) if needed.
4. Push each exportable action into the `NLA` if your export flow depends on NLA strips.
5. Make sure the strips are not muted and have valid start/end frames.
6. Confirm the animated object is the same armature used by the mesh.
7. If the animation depends on IK or constraints, bake it before export if the FBX result is inconsistent.
8. Check the FBX export settings and be consistent:
   - if using `All Actions`, make sure all desired actions exist and are clean
   - if using `NLA Strips`, make sure all desired strips are present in the NLA
9. Export to a temporary folder first, then inspect the resulting FBX in Unity.

### Fast diagnosis rule

- Missing one clip only:
  usually the action name, fake user, active action, or NLA strip setup is wrong.
- Missing several clips:
  usually the FBX export mode changed, such as `Active Action` only versus `All Actions` or `NLA Strips`.
- Clip exists in Unity but is broken:
  usually bake/constraint/armature mismatch instead of export omission.

## Quick Prompt For Future Codex

If a future session needs this workflow, use a prompt like:

`Reimportei Assets/Blender/NewKnight.fbx. Siga Docs/NewKnightReimportWorkflow.md, reaplique clips/loops se necessario, valide o avatar do Player.prefab e confira o KnightAnimController.`
