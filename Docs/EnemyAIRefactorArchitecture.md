# Enemy AI Refactor Architecture

## Existing Contracts To Preserve
- `EnemyHealth` remains the authority for health, death, damage replication, kill awards, and cleanup.
- `EnemyAttack` remains responsible for attack execution, animation timing, hit windows, player damage, and shield-block interaction.
- `EnemyMotor` remains responsible for applying actual local movement and damage knockback.
- `EnemyNetworkSync` remains responsible for replicating transform, state, speed, health, attack sequence, and damage sequence.
- `EnemyAnimationController` remains responsible for translating code state into Animator parameters and direct spawn/death/damage playback.

## New Architecture
- `EnemyBrain` becomes the behavior orchestrator instead of owning all target search and movement logic.
- `EnemyStateMachine` owns state transitions and delegates behavior to small state classes.
- `EnemyPerception` aggregates `EnemyVision` and `EnemyHearing`.
- `EnemyVision` handles cone checks, target filtering, and line-of-sight raycasts.
- `EnemyHearing` listens to `NoiseSystem` events and stores investigation positions.
- `EnemyNavigation` wraps `NavMeshAgent` and exposes destination/stop/reached/path methods to AI states.
- `EnemyMotor` still applies motion, while `NavMeshAgent` provides path steering when available.

## Behavioral States
- `Idle`: waits and observes.
- `Patrol`: moves through configured patrol points.
- `Alert`: investigates a suspicious position.
- `Chase`: follows a confirmed target using last visible target position.
- `Attack`: allows `EnemyAttack` to execute when in range.
- `Search`: searches around the last known target position without tracking the real hidden target.
- `Return`: returns to patrol/home after search ends.

## Combat Memory
- Confirmed visual contact, successful attack starts, and incoming damage register short combat awareness.
- While combat awareness is active, losing line of sight uses `Combat Lost Sight Grace Duration` instead of only the regular lost-sight grace.
- `Combat Memory Awareness Radius` can keep the enemy engaged with a known combat target without requiring the target to remain inside the vision cone.
- `Close Awareness` is separate: it wakes the enemy when a player gets very close, even if that player was never seen before.
- `Search` automatically uses the combat search duration/radius/point count/speed when the search came from recent combat.
- This prevents an enemy from dropping back to patrol immediately when the player briefly leaves the cone during melee, while still avoiding permanent magical tracking.

## Multiplayer
- Only the authoritative enemy runs AI decisions.
- Remote enemies receive replicated state through `EnemyNetworkSync`.
- Player candidates are tracked through a lightweight registry fed by `PlayerHealth` enable/disable.

## Basic Enemy Setup
- Required gameplay components: `EnemySetup`, `EnemyBrain`, `EnemyNavigation`, `EnemyPerception`, `EnemyVision`, `EnemyHearing`, `EnemyMotor`, `EnemyAttack`, `EnemyHealth`, `EnemyAnimationController`, `EnemyNetworkSync`, collider, and Rigidbody.
- `NavMeshAgent` is recommended on the prefab. If it is missing, `EnemyNavigation` can create one at runtime when `Create Agent At Runtime` is enabled.
- `EnemyBrain` owns behavior configuration: spawn mode, default state, patrol points, movement speeds, attack distance handoff, search timings, return behavior, and debug gizmos.
- `EnemyAttack` still owns the actual hit timing, damage, attack animation flow, and shield-block interaction.

## Layers And Perception
- `EnemyVision.Target Layers` should include player hitbox/collider layers.
- `EnemyVision.Obstacle Layers` should include walls, closed doors, solid scenario geometry, and any object that blocks line of sight.
- If the project has no dedicated Player layer, broad masks can work, but a Player layer is cleaner and safer.
- Hearing is event-based. Emit sounds with `NoiseSystem.EmitNoise(position, radius, intensity, noiseType, source)`.
- The enemy investigates the noise position; it does not automatically know who emitted the sound.

## NavMesh
- Bake a NavMesh or use `NavMeshSurface` before enemies need to move.
- Keep enemy spawn points on or near the NavMesh.
- Match the `NavMeshAgent` radius/height with the enemy collider.
- Runtime dungeon generation can rebuild NavMesh later without changing the AI state code, because AI only talks to `EnemyNavigation`.

## Patrol
- Assign patrol transforms in `EnemyBrain.Patrol Points`.
- Use `EnemyPatrolPoint` on a point when that point needs a custom wait time.
- `EnemyPatrolMode.Sequential` is implemented now; `Random` is available as a selectable mode for reusable prefab setup.

## Quick Tests
- Vision: select an enemy and confirm the yellow cone follows its forward direction. Put the player behind a wall included in `Obstacle Layers`; the enemy should lose line of sight and search the last known position.
- Hearing: emit a noise from code with `NoiseSystem.EmitNoise`. An enemy with hearing enabled and within the audible radius should enter alert/search behavior around the emitted position.
- Attack: keep using the existing `EnemyAttack` settings. The state machine only decides when attack conditions are valid.
