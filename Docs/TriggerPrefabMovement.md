# Trigger prefab movement

Move travel distance, linear speed, activation angle and angular speed from `InteractionTriggerSystem` into each `InteractionTriggerMechanism` prefab. The system continues to own activation policy, occupancy filters, timing and target connections. All new and updated authoring text is English.

Implementation steps:

1. Snapshot saved files and the live scene, including any unsaved movement settings.
2. Make mechanism prefabs own movement and expose type-specific movement controls in their Inspector. Update pressure detection to use the prefab's travel distance.
3. Migrate existing values into prefab assets; preserve instance overrides when multiple systems use different settings for the same prefab.
4. Translate trigger-system UI, prompts, diagnostics and documentation into English.
5. Run the relevant Editor tests and native Play Mode verification, then remove temporary validation assets.

Checkpoint: `.codex/backups/trigger-prefab-movement-20261105/`.

## Result

Movement now belongs exclusively to `InteractionTriggerMechanism` on the categorized prefab. Its Inspector shows lever angle/axis/angular speed or button travel/direction/linear speed, plus the shared audio controls. The system Inspector contains only activation, occupancy, target and output settings. Built-in labels, prompts, help text, tooltips, exceptions and the trigger-system documentation are English.

The existing values were captured from the live scene and migrated to the renamed `Lever_01`, `Pressure_Button_01` and `Button_01` assets without changing their GUIDs. The scene's built-in prompt is now **[{key}] Activate**. The old controller movement fields were removed from scene serialization.

## Validation

All **57 Editor tests** passed: 23 mechanism-system tests, eleven statue-puzzle tests and 23 flamethrower tests. Added integration checks verify that prefab angular speed/angle drive lever motion and that a custom 0.18 m pressure travel updates both linear movement and the occupancy band, keeping the occupant detected throughout the travel.

Native Play Mode checks passed for lever rotation, puzzle failure triggering three heads, success without fire, cooldown wait/return, physics-crate and player filtering, plate/button travel, single-use behavior, prepared audio reuse and pose/audio restoration when disabled. The original Map3 scene was restored cleanly after the temporary validation scene closed. Two-client Photon testing was not performed.

Migration evidence: `.codex/diagnostics/trigger-prefab-movement-migration-20261105.txt`. Test/Play evidence: `.codex/diagnostics/trigger-prefab-movement-validation-20261105.txt`.
