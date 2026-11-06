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
- `PlayerMeleeAttack` owns `Attack Configs` per combo step, applying hit behavior, drawing the selected hit shape preview, and scheduling per-attack swing audio once per accepted attack sequence.
- Photon sync now sends the combo step immediately after `AttackAnimationSequence`; all multiplayer clients should use the updated build together.
- `Upper Body Attack` fades its layer weight back to locomotion after the attack state returns to `Empty`, keeping the end of the combo smooth.

## Attack Configs

Each combo step can choose a `Hit Shape`:

- `Ball`: current sphere behavior, using `Center Distance` and `Radius`.
- `Arc`: aimed lateral arc, using `Range`, `Angle`, and `Half Height`. With `Use Vertical Camera Aim` enabled, the arc plane follows camera pitch. Angle and half height are evaluated in that same frame; `Half Height = 0` disables the additional plane-thickness limit. Disabling vertical aim keeps the arc horizontal.
- `Box`: oriented box in the attack direction, using `Center Distance` and `Size`.

The custom inspector drawer only shows the parameters for the selected shape, plus the shared timing, audio, impact VFX, and camera feedback fields.

## Arc camera aiming

Hit filtering, contact-point sampling and the hitbox gizmo share the attack rotation. Looking down tilts the arc toward the ground; looking up tilts it toward upper targets without changing its lateral opening. The query remains limited by the configured range and collider contacts; the drawn fan indicates its center plane, with Half Height controlling optional thickness around that plane.

All three authored Player combo steps now use 1.8 m arc range. The first two previously used 1.68 m, less than the standing camera's approximately 1.758 m ground clearance, which prevented direct ground contact even with downward aim. Their timing, damage and lateral angles are preserved. The kick keeps its independent horizontal targeting behavior.

The authored arcs use **Half Height = 0.18 m**, bounding contact to a 0.36 m band around the aimed plane. Setting Half Height to zero removes this restriction and can accept a nearby floor even during a horizontal attack. Keep a positive half height when contact should follow the visible aimed arc. Ground impact reactions are still allowed when looking down; see [PlayerMeleeFloorContact.md](PlayerMeleeFloorContact.md).

See [PlayerMeleeArcAim.md](PlayerMeleeArcAim.md) for the change scope and validation evidence.
