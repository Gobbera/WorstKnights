# Interaction Trigger System

`InteractionTriggerSystem` owns activation policy and the connection to a gameplay system. `InteractionTriggerMechanism` owns movement and audio on each prefab. Gameplay targets keep their own rules: the statue puzzle checks plates and triggers flamethrowers only on failure; doors keep their locks, motion and replication.

## Authoring workflow

1. Create **GameObject > Mechanisms > Interaction Trigger System**. The object includes the controller and a Lever instance.
2. Select **Type**: Lever, Pressure Button or Button.
3. Select a **Prefab** from the filtered category and click **Apply Prefab**. This replaces only the controller's mechanism child and supports Undo. Target connections, outputs and unrelated children are preserved.
4. Assign **Linked System**: `StatuePuzzleController`, `DoorInteractionTriggerTarget`, `SignalInteractionTriggerTarget`, `InteractionTriggerStateTarget`, `InteractionTriggerRouter` or another `IInteractionTriggerTarget`. Leave it empty to use only the UnityEvent outputs.
5. Configure activation mode, timing and occupancy filters on the system.
6. Click **Edit Prefab (Movement and Audio)** to configure the mechanism. These settings belong to the prefab and are shared by its instances. A scene instance can override them for a specific placement.

The current categorized prefabs are:

| Type | Prefab |
| --- | --- |
| Lever | `Assets/Prefabs/Mechanisms/Triggers/Levers/Lever_01.prefab` |
| Pressure Button | `Assets/Prefabs/Mechanisms/Triggers/Pressure Buttons/Pressure_Button_01.prefab` |
| Button | `Assets/Prefabs/Mechanisms/Triggers/Buttons/Button_01.prefab` |

To add a variant, save it in the matching category and add `InteractionTriggerMechanism` with the corresponding **Mechanism Type**, a dedicated AudioSource and **Moving Part**. Lever/Button also need `InteractionTriggerInteractable` and interaction colliders. Pressure Button needs a solid **Pressure Surface** and a kinematic Rigidbody on the moving part. The prefab picker only lists prepared prefabs of the matching type.

**Tools > Mechanisms > Prepare Interaction Trigger Prefabs** prepares the initial assets. Normal prefab selection does not reset their movement or audio settings.

## Movement and audio: mechanism prefab

| Mechanism | Movement controls |
| --- | --- |
| Lever | Moving Part, Rotation Axis, Activation Angle, Angular Speed. |
| Pressure Button | Moving Part, Press Distance, Movement Speed, Pressure Surface. Movement is down in world space. |
| Button | Moving Part, Press Distance, Movement Speed, Local Press Direction. |

Distances are world-space meters, linear speed is meters per second, angles are degrees and angular speed is degrees per second. The default travel is 0.04 m; the default lever angle is -50 degrees at 180 degrees per second. Movement preserves the authored resting pose and lever pivot. Pressure detection reads the mechanism's actual **Press Distance**, including custom travel values.

`InteractionTriggerInteractable` also owns per-prefab **Interaction Distance**, optional **Lock Player Controls / Control Lock Duration**, a **Player Animation Trigger**, and local start/finish events. `Lever_Interaction_01` is a prepared variant with 1.5 m reach and a one-second lock. It is available in the Lever category without replacing existing scene controls. The player's global Interaction Distance caps the per-object value. See [TimedInteraction.md](TimedInteraction.md).

**Range Mode > Box Collider** replaces numeric reach with player-body containment in a linked **Interaction Box**. Create a separate trigger box and edit its local center/size directly in the interactable Inspector. Eligible box interactions can be aimed beyond numeric reach, while pickups/ordinary objects keep their existing reach. Missing/disabled boxes deny use; entering grants eligibility for the normal interaction key. See [InteractionBox.md](InteractionBox.md).

The system has no movement fields and sends only activation state to the mechanism. Changing a prefab's motion therefore applies when it is used by any system, without controller-specific overrides taking precedence.

Mechanism animation now advances from its own local pose/press distance rather than child Rigidbody interpolation feedback. When mounted on a moving carrier, it follows the parent's full transport while continuing its authored rotation/press speed. This prevents Lever_Arm from waiting until an elevator stops. See [LeverCarrierMotion.md](LeverCarrierMotion.md).

**Activation Audio Cue**, **Deactivation Audio Cue** and **Audio Source** also belong to the mechanism prefab. The source exists before interaction and clips are prepared during initialization. Playback is a one-shot, even when the cue has Loop enabled. Empty cues keep the corresponding transition silent. Disabling the mechanism/controller restores the resting pose and stops audio; re-enabling restores its remembered activation state.

## Activation: system controller

| Mode | Behavior |
| --- | --- |
| Momentary | Activates once, then returns after **Momentary Hold Time** and, when enabled, after the linked system is ready. Rejects repeated interaction while active. |
| Toggle | Alternates active/inactive on each accepted use. Supports **Start Active**. |
| Alternate AB | Alternates B/A, with separate **On State B** and **On State A** outputs. Supports **Start in B**. Both positions count as actions for the puzzle. |
| Once | Accepts one use and remains active for the scene session. |

**Interaction Cooldown** prevents rapid uses. **Wait for Linked System** also holds the mechanism while the target reports busy. Targets may independently reject activation according to their own rules. Initial state does not fire outputs or submit a puzzle check.

Pressure Button uses occupancy instead of click interaction and returns only after all accepted occupants leave, the interaction cooldown expires and the linked target becomes ready, when configured. Remaining on the plate does not repeat its command.

## Pressure filters: system controller

| Occupant Filter | Accepted occupants |
| --- | --- |
| Players | Living `PlayerHealth` actors, deduplicated per player. |
| Objects | Dynamic Rigidbodies without `PlayerHealth` or `EnemyHealth`, such as physics crates. |
| Enemies | Living `EnemyHealth` actors, deduplicated per enemy. |
| Anything | Living players, living enemies and dynamic physics objects; excludes static floors and walls. |
| Specific Object | Colliders on the selected scene object and its descendants; supports explicitly chosen static/kinematic objects. |

**Occupancy Mask** restricts every filter. **Detection Height** and **Contact Tolerance** define the contact band relative to the original plate surface. The band includes the mechanism's configured travel distance so lowering the plate does not lose its occupants. Triggers, the mechanism itself and colliders outside the contact band are ignored. Multiple colliders on the same actor/body do not multiply activation.

## Targets and outputs

`IInteractionTriggerTarget` exposes `CanTrigger`, `IsTriggerBusy` and `ReceiveTrigger(InteractionTriggerContext)`. Context includes the source, activating object/player, active state and whether the state counts as an action. Commands and UnityEvents run on authority; targets own their gameplay and replication.

`StatuePuzzleController` can be assigned directly to **Linked System**. It checks the four plates, invokes **On Puzzle Failed** or **On Puzzle Solved**, and reports busy while a check is pending or any **Activation Cooldown Sources** is still cycling. Flamethrower references stay on the puzzle, never on a Lever/Button/Pressure Button prefab. In Toggle mode, only activation checks the puzzle; Alternate AB checks on either position. Solving prevents further attempts.

`DoorInteractionTriggerTarget` references a `DoorController` and offers Follow State, Toggle, Open, Close, Unlock and Lock. Follow State maps active/inactive to open/close. Other actions use an action transition; use Momentary or Alternate AB to repeat Toggle on every use. **Unlock Before Opening** sends a single unlock/open command to preserve network ordering.

`InteractionTriggerRouter` selects a named destination by zero-based route index. `SelectRoute(int)` changes the next action's target while preserving the current press through release. `SelectRouteAndActivate(int)` explicitly releases the old destination and immediately activates the new route in B. Each route can override activation mode, such as Momentary before completion and Alternate AB afterward. `SignalInteractionTriggerTarget` adapts Follow State, Toggle, Activate and Deactivate to the existing `DoorSignalSource`. Combine Follow State with a TwoPosition platform for a repeatable A/B elevator. See [PlatformTwoPosition.md](PlatformTwoPosition.md).

Optional outputs are **On Used**, **On Activated**, **On Deactivated**, **On State A** and **On State B**. They can call other scripts, Animator methods or object activation. The primary target receives the command before these outputs. Do not also wire `CheckSolution()` to an output when the puzzle is already assigned as the target.

`InteractionTriggerStateTarget` controls another persistent-state trigger with Set B, Set A or Toggle. It changes the controlled mechanism's pose, gameplay output and replicated state together. In Map3, Elevator_02 momentarily calls Set B on Elevator_01 to retrieve the platform at the lower floor, without reversing it on release or bypassing the unsolved puzzle stage. See [ElevatorCallControl.md](ElevatorCallControl.md).

## Networking

The Master Client decides transitions. Remote interactions send reliable requests with a stable scene ID, player ownership and expected sequence; wrong-owner, distant and stale requests are rejected. Pressure is detected on the Master. State and timestamps are stored in room properties. Each client moves its prefab mechanism and plays its audio from that state. Late join restores pose and single-use state without replaying old gameplay or audio.

Target systems keep their own replication. The puzzle sends checks through the Master's local inventory using the existing server-ordered buffered RPC flow, preserving order with plate placement and attributing the actual activating player. Physics objects/enemies may activate without player attribution. This requires the Master's local inventory to be ready. Loose-object physics keeps the project's existing configuration; this system does not add crate replication.

## Map3 and migration

The existing Lever is grouped under **Interaction Trigger System**, linked to an **Interaction Trigger Router**. Route 0 checks **Statue Puzzle** in Momentary mode. **On Puzzle Solved** selects and immediately activates route 1 in Alternate AB mode. The platform starts descending on solve; following interactions select A/B and return to the original endpoint or descend again. The puzzle owns the three cooldown references and its failure outputs remain intact. The platform uses TwoPosition and retains its latest authored movement points/speed. Legacy pressure/click inputs are inactive. Switching Type/Prefab on the same system changes its activator.

**Tools > Mechanisms > Connect Statue Puzzle To Lever** reconnects the scene. **Tools > Hazards > Setup Statue Puzzle Pressure Button** is a compatibility menu that configures the same modular system as Pressure Button.

**Tools > Mechanisms > Connect Statue Puzzle To Platform** configures the puzzle-to-platform routes when the active scene has one puzzle and one platform. Existing routes, outputs and platform motion settings are preserved. Lever/Pressure setup preserves an existing router.

The movement migration captures saved files and the live scene before changing serialization, then transfers existing values to the corresponding prefabs. Conflicting values for multiple instances of one prefab are preserved as mechanism instance overrides. Your renamed `*_01` prefab assets and GUIDs are retained. The built-in prompt is **[{key}] Activate**. Inspector labels, help text, tooltips and diagnostics for this workflow are English.

Movement checkpoint: `.codex/backups/trigger-prefab-movement-20261105/`. Original modular-system checkpoint: `.codex/backups/interaction-trigger-system-20261105/`.

## Validation

The prefab-movement refactor passed **57 Editor tests** and native Play Mode checks covering motion, pressure occupancy, puzzle success/failure, cooldowns, audio reuse and restoration. Custom prefab travel/angle/speed are covered by integration tests. See [TriggerPrefabMovement.md](TriggerPrefabMovement.md) for migration and evidence paths. A two-client Photon session has not been validated.

The subsequent event-driven routing integration passed **73 Editor tests** and native Play Mode checks of the configured Map3 puzzle-to-platform flow, including the real elevator reaching its authored destination. See [InteractionTriggerRouting.md](InteractionTriggerRouting.md).

The current immediate-handoff/TwoPosition implementation passes **91 Editor tests** and native Play Mode checks of automatic activation on solve, repeated A/B trips, continuous reversal and movement audio. See [PlatformTwoPosition.md](PlatformTwoPosition.md).
