# Attack Combo Implementation

## Scope

- Added on 2026-07-27.
- Manual project snapshot: `Builds/ProjectSnapshots/KingsWorstKnights_pre_attack_combo_20260727_222639.zip`.
- Snapshot excludes regenerated caches and build output: `Library`, `Temp`, `Logs`, `Builds`, `.git`, `.vs`.

## Requested Behavior

- Mouse primary attack starts a three-hit combo.
- First accepted click plays `Attack_1`.
- A follow-up click during the configured combo window queues `Attack_2`.
- Another follow-up click during the next configured combo window queues `Attack_3`.
- Each queued hit waits for the previous attack's execution time before playing.
- The combo resets if the player does not continue inside the configured window.
- Combo windows open shortly before each attack animation ends and close shortly after it ends.
- Window timing must be configurable per combo step for healthy gameplay tuning.

## Implementation Shape

- `PlayerMovement` owns combo state, accepted attack sequence, replicated combo step, queue timing, stamina, and movement slow.
- `MovementConfig` exposes attack combo timing fields for the three animation steps.
- `MovementAnimationController` plays `Attack_1`, `Attack_2`, or `Attack_3` on the existing `Upper Body Attack` layer from the replicated combo step.
- `PlayerMeleeAttack` continues to apply the same hit behavior once per accepted attack sequence.
- Photon sync now sends the combo step immediately after `AttackAnimationSequence`; all multiplayer clients should use the updated build together.
- `Upper Body Attack` fades its layer weight back to locomotion after the attack state returns to `Empty`, keeping the end of the combo smooth.
