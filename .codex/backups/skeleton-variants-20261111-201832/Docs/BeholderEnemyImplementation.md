# Beholder Enemy Implementation

## Scope

- Add the Beholder to the standard enemy behavior stack: patrol, alert, chase, search, return, spawn/death/damage lifecycle, perception, navigation, networking, and animation state reporting.
- Keep the Beholder free of the common melee attack setup for now. Future eye/magic mechanics should plug into a Beholder-specific action layer instead of forcing `EnemyAttack` into a fake melee role.
- Preserve the existing skeleton enemy architecture where possible: `EnemyBrain` remains the behavior orchestrator, `EnemyNavigation` moves, `EnemyPerception` senses, `EnemyHealth` owns death, and `EnemyAnimationController` drives common state presentation.

## Implementation Notes

- Prefer prefab setup and small extension components over duplicating enemy AI states.
- The Beholder controller currently provides locomotion presentation but no authored attack animation contract.
- Configure `EnemyAttack.Attack Enabled` as disabled for the Beholder until its dedicated eye/magic actions exist.
- Configure `EnemySetup.Use Gravity When Simulated` as disabled so the scene-authored hover height is preserved while the normal enemy navigation steers horizontally.
- The prefab uses generated home patrol until a hand-authored scene route exists. If a map needs exact pathing, assign scene transforms to `EnemyBrain.Patrol Points`; explicit patrol points override generated home patrol.
- The first visual detection should use the standard `EnemyBrain` alert-before-chase flow. Tune `Alert Duration` and `Rotate During Alert` on the brain; add a future eye/body/tentacle reaction through `EnemyAnimationController.Alert Feedback`.
- Beholder-specific combat actions should be added later through a dedicated component that observes/extends `EnemyBrain` state and target information.

## Procedural Animation

- `BeholderProceduralAnimationController` runs in `LateUpdate`, after the Animator has evaluated the base idle/locomotion pose.
- The first procedural layer is eye tracking. During `Chase` the controller resolves the current `EnemyBrain.CurrentTarget` and rotates every resolved eye-control bone toward the player.
- If the replicated/remote brain has only state and no target reference, the controller can fall back to the nearest valid `PlayerHealth` inside `Nearest Target Fallback Radius`.
- Eye bones are auto-resolved from names containing `Center Eye` or `Eye.Tentacule`, excluding mesh and `_end` transforms. The `_end` child is used to infer each bone's local look axis.
- The controller blends into tracking with `Acquire Smooth Time` and releases with `Release Smooth Time`, so it can layer over normal idle without requiring a baked `.anim` clip.
