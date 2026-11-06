# Elevator Call Control

## Scope and checkpoint

Checkpoint: `.codex/backups/elevator-call-20261106/`. Preserve the live scene before editing its connections.

1. Add a reusable target that requests A/B/Toggle on an existing persistent-state trigger, updating its gameplay output, presentation and replicated state together.
2. Convert `Interaction Trigger System_Elevator_02` into a Momentary call to the main elevator trigger's B state. Remove its duplicated puzzle router and keep its placement/prefab feedback.
3. Preserve puzzle gating: state requests are available only once the controlled trigger uses Toggle or Alternate AB. Release/repeated calls must not send the elevator upward.
4. Validate call-at-top, reversal while ascending, main-lever synchronization, repeated use and the configured scene in Play Mode.

## Authoring

Add `InteractionTriggerStateTarget` to a control and assign its **Controlled Trigger**. Assign this adapter to the call control's **Linked System**. Choose **Command**: Set B, Set A or Toggle. The destination must use a persistent Toggle/Alternate AB policy. Momentary, Once and Pressure Button destinations do not accept these remote state commands.

The adapter invokes the controlled trigger's existing state transition. Its visual position, gameplay target, outputs and replicated state change together. It does not write a platform signal behind the trigger's back. A state already selected is ignored, so repeated calls cannot toggle a mechanism in the opposite direction. Only action transitions issue commands; the call control's release has no effect on the requested destination.

This target can be reused for other doors, platforms and remote switch controls. Gameplay systems retain their existing movement, audio and replication.

## Map3

`Interaction Trigger System_Elevator_02` is a Momentary control with **Interaction Trigger State Target > Controlled Trigger** pointing to `Interaction Trigger System_Elevator_01`, and **Command > Set B**. B is the platform's lower endpoint. The copied puzzle router is removed from Elevator_02; its prefab, pose, movement and audio are preserved. The prompt is **[{key}] Call Elevator**.

Before the puzzle is solved, Elevator_01 uses Momentary puzzle checking, so the lower call remains unavailable. After solving, Elevator_01 uses Alternate AB and accepts the call whenever A is selected and its short interaction cooldown is ready. Calling while the elevator is ascending reverses its existing route toward the lower endpoint without jumping. The call lever returns to rest after its own hold time, while Elevator_01 remains synchronized in B. The next normal use of Elevator_01 selects A, returning upward.

**Tools > Mechanisms > Connect Lower Elevator Call Lever** configures the two named scene controls with Undo. Other scenes can assign explicit references in the Inspector. The target adds no dedicated network events: the call lever's existing authority validation dispatches the command, and the controlled trigger/signal/platform retain their existing replication.

## Validation

All **96 Editor tests** pass, including five new call-control tests covering puzzle gating, Set B, release, repeated calls, main-lever synchronization, reusable Set A/Toggle and idempotent scene setup.

Native Play Mode used isolated copies of the configured Map3 controls and platform. It verified the original failed/solved puzzle flow, a lower-floor call from the upper endpoint, reversal during ascent without a position jump, arrival/stopping at the lower endpoint, harmless release/repeated calls, and the main lever's next use selecting A. Existing movement audio stopped on arrival. The original scene was restored and temporary test assets/helper scripts were removed.

The saved scene comparison contains one new state-target component, removal of the copied lower puzzle router, and two changed objects on Elevator_02. Placement and prefab references are preserved. Runtime and Editor projects compile without errors or warnings. Evidence: `.codex/diagnostics/elevator-call-20261106.txt` and `elevator-call-scene-diff-20261106.txt`. Two-client Photon validation remains outstanding.
