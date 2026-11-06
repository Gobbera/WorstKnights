# Player melee arc camera aim

## Scope

The player's melee arc must keep its lateral opening while following vertical camera aim. Hit validation, impact sampling and the selected hitbox preview must use the same attack-oriented frame. `Use Vertical Camera Aim` continues to allow an explicitly horizontal mode.

The standing prefab camera is approximately 1.758 m above the capsule's ground plane. The first two authored arc ranges of 1.68 m cannot reach that plane even when aimed straight down. Align those two ranges to the existing third hit's 1.8 m range so every combo step can reach nearby ground.

## Implementation steps

1. Preserve combat source, the Player prefab and the open scene before validation.
2. Evaluate lateral angle and optional half height in the attack's local frame; use the same frame for impact sampling and gizmos.
3. Preserve combo timing, damage, attack shapes and single-hit-per-target behavior. Keep `Half Height = 0` as no additional plane-thickness limit; sphere broad-phase range still applies.
4. Validate downward/upward targets, lateral opening, local half height, range, vertical extremes, disabled vertical aiming and Ball/Box behavior. Verify actual prefab combo hits against a ground target in native Play Mode.

Checkpoint: `.codex/backups/player-melee-arc-aim-20261105/`.

## Behavior

Arc angle and optional half height are evaluated in the same attack-local frame used by Box orientation and the arc gizmo. The camera's vertical direction is preserved when `Use Vertical Camera Aim` is enabled. Collision contact sampling uses that oriented frame as well, so hit feedback comes from the aimed target.

The selected gizmo's filled fan, boundary lines, center direction and optional height indicator all rotate with the attack. The fan represents the arc's center plane; `Half Height = 0` continues to disable its additional thickness restriction, rather than making the arc an infinitely thin surface. The existing spherical candidate query still limits reach.

Only the first two prefab arc ranges changed, from 1.68 m to 1.8 m; the third was already 1.8 m. Combo timing, stamina, damage and lateral opening remain unchanged. Kick targeting remains separate.

## Editor validation

All **12 focused tests passed**: downward/upward damage targets, lateral opening at multiple pitch/yaw values, aim-relative height, zero-height semantics, horizontal-only mode, near/exact vertical directions, targets behind/out of range, impact point/direction, per-target deduplication, Ball/Box regression and all authored combo steps reaching standing ground. Runtime and Editor builds passed without errors or warnings.

Evidence: `.codex/diagnostics/player-melee-arc-aim-20261105.txt`.

## Native Play Mode validation

An additive temporary scene loaded the actual `Assets/Resources/Player.prefab` with controlled camera angles and real physics queries. The measured standing camera clearance was approximately 1.758 m; all three authored ranges were 1.8 m. Every combo step hit a damage-receiving ground target when looking down. Looking up hit the upper target with all three steps and did not hit the ground again. Impact direction followed camera pitch. Turning vertical aiming off restored horizontal targeting.

The preview isolated input/animation drivers and invoked the actual hit-window/hit-query methods; it did not synthesize physical input or run a multiplayer session. The original Map3 scene stayed loaded and was restored cleanly, and temporary validation assets were removed afterward.

## Follow-up: horizontal floor reactions

The original authored `Half Height = 0` allowed contacts anywhere above/below the arc plane within the candidate query, including the nearby floor when facing forward. The three Player combo steps now use **0.18 m half height** (0.36 m total), preserving downward ground hits while rejecting floors below a horizontal swing. This supersedes the initial unlimited-height prefab configuration; explicit zero remains available as an advanced option. See [PlayerMeleeFloorContact.md](PlayerMeleeFloorContact.md) for the reproduced reaction and regression coverage.
