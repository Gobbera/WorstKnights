# Kick Attack Implementation

## Scope

- Added on 2026-07-27.
- Manual project snapshot: `Builds/ProjectSnapshots/KingsWorstKnights_pre_kick_attack_20260727_211258.zip`.
- Snapshot excludes regenerated caches and build output: `Library`, `Temp`, `Logs`, `Builds`, `.git`, `.vs`.

## Requested Behavior

- Knight gets a new `Kick` attack.
- Default input is `F`, configurable through `InputConfig`.
- Kick has its own cooldown, configured in `MovementConfig`.
- Kick has its own active action duration, configured in `MovementConfig`, used to block starting an attack while the kick is still playing.
- Kick deals low configurable damage.
- Kick pushes valid targets slightly backward with configurable force.
- Enemies can opt in or out of being moved by Kick through an enemy-side component.
- Kick can damage/break destructible objects.
- Kick can hit other players.

## Implementation Shape

- `PlayerInputHandler` exposes `KickPressed`.
- `PlayerController` routes accepted input to `PlayerMovement.Kick()`.
- `PlayerMovement` owns cooldown, action duration, stamina cost, replicated animation sequence, and movement slow timing.
- `MovementAnimationController` plays `Kick.Kick` on the masked `Kick` Animator layer when the replicated sequence changes.
- `PlayerKickAttack` performs the hit query and applies damage/knockback to enemies, destructibles, and players.
- `EnemyKickReaction` lets each enemy configure whether it can be pushed by Kick and tune a per-enemy multiplier.
- `EnemyMotor` applies enemy knockback through dynamic Rigidbody physics on the authoritative client, so Kick upward force lifts affected enemies and gravity pulls them back down.
