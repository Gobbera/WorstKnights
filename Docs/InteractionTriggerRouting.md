# Interaction Trigger Routing

## Scope and checkpoint

Allow a mechanism to change gameplay targets after a puzzle or another event. Keep activation policy on `InteractionTriggerSystem`, movement/audio on its prefab, and gameplay/motion rules on the destination systems.

Checkpoint: `.codex/backups/interaction-trigger-routing-20261106/` contains the saved scene and affected source/documentation before implementation. Preserve a live scene copy before configuring the open scene.

## Implementation stages

1. Add a reusable target router with named routes. `SelectRoute(int)` preserves an ongoing press until release; `SelectRouteAndActivate(int)` explicitly hands off and starts the new target immediately.
2. Add a signal target adapter for existing `DoorSignalSource` consumers, including platforms and doors. Reuse their current motion and network replication.
3. Configure Map3: route 0 checks Statue Puzzle; `On Puzzle Solved` selects and activates route 1 immediately. Use TwoPosition motion with an Alternate AB control route, preserving the authored path.
4. Validate route transitions, busy targets, pressure occupancy, signal policies and state restoration, then exercise the configured scene flow in Play Mode.

## Authoring

1. Add `InteractionTriggerRouter` to the trigger controller's object and assign it to **Interaction Trigger System > Linked System**. Use one router per trigger.
2. Add entries to **Routes**, each with a descriptive **Name** and a **Target** implementing `IInteractionTriggerTarget`. Route indices start at zero. Empty targets, disabled targets and nested routers block interaction.
3. Set **Initial Route** to the starting destination.
4. Connect any gameplay UnityEvent to **InteractionTriggerRouter.SelectRoute(int)** for a deferred destination change, or **SelectRouteAndActivate(int)** to immediately transfer control and activate B. Supply the destination index. The router has no puzzle/platform dependency; another puzzle, door, quest or mechanism event can make the same selection.
5. Routes can enable **Override Activation Mode**, such as Momentary while checking a puzzle and Alternate AB afterward. Without an override the trigger's own mode is used. Cooldown, occupancy filters and mechanism prefab configuration stay on their existing components.

Plain `SelectRoute` does not invoke the new destination. Momentary, Toggle and Pressure Button retain the original destination and activation policy through release, including its busy state. Alternate AB treats both positions as actions. `SelectRouteAndActivate` explicitly releases the old destination and starts the new target in B immediately; it does not duplicate the original use outputs. Repeating the handoff to an already selected route is ignored. Restoring replicated routing never replays gameplay.

## Signal adapter

`SignalInteractionTriggerTarget` connects the modular trigger contract to an existing `DoorSignalSource`. Assign its **Signal Source** and choose:

| Action | Behavior |
| --- | --- |
| Follow State | Activate on press/B; deactivate on release/A. Useful for held platforms or doors. |
| Toggle | Toggle once per action; a Momentary release does not toggle again. |
| Activate | Latch the signal on. Useful for an authored OneWay platform. |
| Deactivate | Latch the signal off. |

The adapter reports no busy animation of its own; the signal consumer retains its motion, lock and replication rules. OneWay completes once started; PingPong runs while its activation requirement is satisfied. TwoPosition selects the final endpoint for active/B and the original endpoint for inactive/A, stopping at each end and allowing repeated trips. See [PlatformTwoPosition.md](PlatformTwoPosition.md).

## Map3 connection

**Interaction Trigger System > Linked System** points to its router:

| Index | Name | Target |
| --- | --- | --- |
| 0 | Check Statues | Statue Puzzle Controller |
| 1 | Control Platform | Signal Interaction Trigger Target on `Stair_Circle (1)` |

**Statue Puzzle > On Puzzle Solved** calls `SelectRouteAndActivate(1)`. Failure keeps route 0 in Momentary mode and the existing three flamethrower outputs/cooldowns. Success immediately activates the elevator and changes the lever to Alternate AB. It stays in B; the next interaction selects A and returns the platform to its original endpoint.

The platform uses **TwoPosition**, preserving the latest authored path of 15.8 meters downward at 2.5 m/s, with **Carry Players** enabled. Its **Signal Source** activation references the authored `DoorSignalSource` on the same object. The adapter uses **Follow State**. **Movement Audio Cue** is configured on PlatformController and loops only during travel through a prepared source.

**Tools > Mechanisms > Connect Statue Puzzle To Platform** provides a scene setup shortcut when there is one puzzle, one platform and its matching trigger. It supports Undo, preserves other routes/outputs/signal references, and avoids duplicate components or listeners. It replaces any legacy `On Puzzle Solved` enable/disable call targeting that same platform: signal activation now controls movement, and the platform must remain enabled. Map3 had a call disabling the platform on solve; this conflict is removed. For several puzzles/platforms, configure their explicit references in the Inspector. The older Lever/Pressure setup menus preserve an existing router.

## Networking

The Master Client selects routes and dispatches gameplay commands. Selected and held route indices are stored together with a sequence in room properties. Late join and master handoff restore the chosen destination and any original destination waiting for release without replaying gameplay. Trigger presentation, signals and platform motion use their existing replication. Keep **Local Only** consistent with the trigger when prototyping.

## Validation

The current **91 Editor tests** pass, including 20 routing/signal tests and 13 platform tests. Native Play Mode confirms immediate activation on solve, repeated A/B travel, mid-route reversal and movement audio lifecycle on the configured Map3 objects. Runtime and Editor projects compile without errors or warnings. See [PlatformTwoPosition.md](PlatformTwoPosition.md) for current evidence. Earlier deferred-selection validation remains archived in `interaction-trigger-routing-20261106.txt`. Two-client Photon validation is still required.
