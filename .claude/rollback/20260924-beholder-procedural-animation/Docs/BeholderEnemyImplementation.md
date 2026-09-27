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
- Beholder-specific combat actions should be added later through a dedicated component that observes/extends `EnemyBrain` state and target information.
