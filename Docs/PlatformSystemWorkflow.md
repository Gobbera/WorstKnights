# Platform System Workflow

`PlatformController` owns a path with sequential axis/diagonal movement points, player carrying, optional breaking/respawn and existing room-state replication.

## Motion

1. Add `PlatformController` to the platform and assign **Moving Part** to the mesh, pivot or body that moves.
2. Choose **Motion Mode**: Static stays still; PingPong traverses the path in both directions repeatedly while active; OneWay traverses it once; TwoPosition moves to B when active and returns to A when inactive, stopping at each endpoint.
3. Use **Add Movement Point** to add a segment. Choose Axis (X/Y/Z plus Positive/Negative) or Diagonal, then set **Distance** in meters and **Speed** in meters per second. Diagonal vectors are normalized.
4. Each segment begins at the previous segment's destination. **Carry Players** enables the existing passenger movement behavior.

## Activation

| Activation Mode | Behavior |
| --- | --- |
| Always Active | Movement is continuously enabled. |
| Player On Top | The platform's top volume detects a player and enables movement. |
| Signal Source | **Activation Signals** references `DoorSignalSource` components. **Signal Requirement** selects Any or All. |

A OneWay route completes once its first valid activation starts it; releasing a signal does not reverse or restart it. A PingPong route follows its activation requirement and pauses/resumes its existing traversal.

Existing `DoorSignalLever` and `DoorSignalTriggerZone` can drive these signals. Modular Lever/Button/Pressure Button mechanisms can use `SignalInteractionTriggerTarget` as their **Linked System**. Set its **Signal Source** and choose Follow State, Toggle, Activate or Deactivate. Use Activate to latch a OneWay route; use Follow State for occupancy/held-switch behavior with PingPong. The adapter adds no path or motion logic.

## Unlocking a mechanism after an event

Assign an `InteractionTriggerRouter` to the modular trigger's **Linked System**. Configure a route to the original gameplay target and another route to the platform's signal adapter. Connect the completion event to **SelectRouteAndActivate(int)** to immediately activate the platform route in B. Set that route's **Override Activation Mode** to **Alternate AB**, and use a **Follow State** signal adapter with **TwoPosition** platform motion. Plain **SelectRoute(int)** remains available when only the next accepted interaction should reach the new target.

In Map3, **Statue Puzzle > On Puzzle Solved** immediately activates route 1 on the Lever's router and starts the descent. Subsequent uses alternate A/B, returning to the original endpoint or moving to the final endpoint. The latest authored path is 15.8 m downward at 2.5 m/s with Carry Players enabled. **Tools > Mechanisms > Connect Statue Puzzle To Platform** provides the single-puzzle/single-platform setup shortcut. See [PlatformTwoPosition.md](PlatformTwoPosition.md).

## Movement audio

Assign **Start Audio Cue**, **Moving Audio Cue** and **Stop Audio Cue** on PlatformController. Start plays once on departure, Moving loops during travel, and Stop plays once on arrival at A or B. Start and Moving begin together. Continuous reversal keeps the current loop; pausing, breaking and disabling do not play an arrival. Empty stages stay silent.

The **Audio Sources** foldout exposes three dedicated sources. **Prepare Audio Sources** creates missing sources before Play, preserves the existing Moving source and repairs shared stage references; the routing setup uses the same preparation. Older objects prepare missing sources in Awake. All sources follow Moving Part. The existing serialized movement cue/source remain compatible. Audio derives from synchronized movement on each client without a separate audio RPC; restoration resumes the loop without replaying past one-shots. See [PlatformAudioStages.md](PlatformAudioStages.md).

## Breakable platforms

Enable **Breakable**, configure **Break Delay**, **Top Trigger Height** and **Player Detection Mask**. A zero delay breaks immediately when a player occupies the top volume. Enable **Respawns** and set **Respawn Delay** to restore the original transform, visuals/colliders and restart traversal from the first point. Breaking works with Static platforms too.

The selected gizmo displays the complete path and the top detection volume. Platform motion, passenger carrying and break/respawn rules remain owned by `PlatformController`; routing only selects which mechanism receives interaction commands.
