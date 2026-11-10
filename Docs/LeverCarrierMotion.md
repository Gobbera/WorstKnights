# Mechanism Motion on Moving Carriers

## Scope and checkpoint

Checkpoint: `.codex/backups/lever-carrier-motion-20261107/` contains the saved scene, mechanism source and affected documentation before investigation. Preserve the live scene before native validation.

1. Reproduce the lever-arm delay using the authored mechanism and a real moving PlatformController, comparing stationary and moving parents.
2. Keep mechanism animation progress relative to its authored resting pose. Parent transport must not consume button travel speed or reset rotation progress through interpolated child Rigidbody feedback.
3. Validate lever rotation during platform travel, pose anchoring, reversal and existing button/pressure motion, while retaining authored speed, axis, audio and colliders.

## Diagnosis

The authored lever arm has a kinematic Rigidbody with Interpolate enabled. Map3's platform moves its ancestor Transform without a root Rigidbody. The old mechanism animation advanced from the child body's world position/rotation on each step. Parent transport invalidated that interpolation sample, repeatedly feeding the resting rotation back into the next animation step.

A native Play Mode comparison reproduced the reported behavior. At approximately 0.6 s, the stationary lever had rotated 89.86 degrees while the identical lever on the moving PlatformController remained at 0 degrees. After the platform finished its 15.8 m route, the moving lever finally reached 89.86 degrees. The scene's current authored angle is -90 degrees at 180 degrees/s.

## Change

`InteractionTriggerMechanism` now stores presented local rotation and world-space press distance independently of the Rigidbody's interpolated feedback. Its authored angle, speed, axis and button travel advance from that logical pose. The parent matrix transports the pose in full, without consuming the button's linear travel speed.

When a kinematic child body's parent matrix changes, the step reconciles both Transform and Rigidbody poses, preventing parent transport from canceling the animation. Stationary parents keep the existing MovePosition/MoveRotation presentation path. Authored Rigidbody interpolation, collision, prefab settings and audio are retained. Immediate state restoration and disable reset the logical pose too.

## Validation

Native reproduction and regression results: `.codex/diagnostics/lever-carrier-motion-20261107.txt`. The original scene remains loaded during additive validation and its prior dirty state is restored. No scene or prefab migration is required.

After correction, the moving lever reaches 90 degrees at approximately 0.6 s while the platform is still descending, with effectively zero anchor error. Both levers return to A during travel and accept B again. A button on a second moving carrier completes its authored 0.04 m press and returns during travel. The moving carriers are also rotated and nonuniformly scaled before the second activation; lever anchoring and world-space button travel remain correct.

All 131 Editor regression cases passed. Five added cases cover stale interpolation feedback, carrier translation/rotation/scale, independent button travel, immediate/reset pose synchronization and reversal at the authored speed. The timed-interaction fixture explicitly selects its input policy so designer changes to the variant's range mode do not change unrelated tests. The original prefab values, kinematic bodies, interpolation settings, audio and Map3 data are preserved. Temporary validation assets are removed after testing. Two-client Photon validation is still outstanding.
