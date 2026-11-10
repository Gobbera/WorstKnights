# Statues Room multiplayer audit

Date: 2026-11-09. Scope: diagnosis only; no gameplay code, scene or prefab changes.

## Evidence and limits

- Inspected saved `Assets/Scenes/Map3.unity`, referenced prefabs, interaction, puzzle, inventory, door, platform and trap scripts, and existing test coverage.
- The user confirmed that the failing test uses Unity Editor plus a standalone build.
- Follow-up: the user repeated the test with both clients running builds and reported that all examined room interactions appeared synchronized and functional. Treat the earlier identity concern as an Editor/build hypothesis, not a reproduced failure between matching builds. No mechanism fixes were implemented.
- Inspected the recent standalone at `C:/Compartilhada/KingsWorstKnights_Data/`. Its main assembly contains the current puzzle-trigger and paired-door APIs. The older project-local `Builds/` directory is not evidence that the tested build is outdated.
- Parsed the recent standalone's compiled `level1` object/transform records without modifying it. The expected room, two elevator controls, platform, door leaves and three traps are present. Serialized root-object record order differs from the saved Editor root list. Record order alone does not establish the value returned by `GetSiblingIndex()` at runtime.
- No connected Unity document-control session was available. The visible Unity window was Menu. This audit did not switch scenes, enter Play Mode, join a Photon room, or reproduce the interactions with two connected clients.
- The latest standalone Player.log records a lobby connection, not the failing room interaction session. It cannot identify which gameplay request failed.

## Authored interaction chain

```text
Plate pickup / removal / placement -> four statue slots
Elevator_01 lever -> InteractionTriggerRouter, route 0 -> puzzle check
  incorrect / incomplete -> three flamethrower TriggerTrap outputs
  correct -> SelectRouteAndActivate(1)
          -> main lever switches to B
          -> FollowState signal -> TwoPosition platform descends
Subsequent Elevator_01 uses -> A/B platform travel
Elevator_02 lever -> SetB on Elevator_01 -> call platform down
```

- The main lever is under `Statues_Room/Stair_Circle (1)`.
- The puzzle controller, lower elevator control and door controllers also include objects outside the `Statues_Room` Transform; they remain part of the connected room gameplay.
- Four slots are assigned to the puzzle. All three traps are assigned both as failure outputs and cooldown sources.
- Main router route 0 is Momentary puzzle checking; route 1 is AlternateAB platform control.
- The platform is TwoPosition, uses its signal source, and retains a 15.8 m path at 2.5 m/s with player carrying enabled.
- The old `Statue Puzzle Button` object is inactive and is not the active main lever path.
- Local-only switches are off on the configured two trigger systems, router, platform, signal source, traps and door prefab/controllers. The Player inventory also has local-only disabled.

## Findings

### 1. Runtime hierarchy identity is the leading test-specific hypothesis

`SceneNetworkStateIdUtility` at `DoorController.cs:1427` constructs IDs from scene name plus every ancestor's name and `GetSiblingIndex()`, including the root. It does not use a persisted scene-instance identity.

- Doors compute `runtimeNetworkSceneId` in Awake and prefer it over their serialized value.
- Trigger systems, routers, traps, puzzle controller and slots also build hierarchy IDs at runtime.
- World pickups have a separate implementation of the same name/sibling-index scheme when no serialized ID is assigned.
- Platform and signal source retain their serialized ID when nonempty. In Map3 their shared token is the legacy `Map3:Stair_Circle (1)[355]`, although the platform is now nested under the room. A legacy token shared by both clients is not itself proof of failure.
- The saved Editor root index of Statues_Room is 212. An example affected ID prefix is therefore `Map3:Statues_Room[212]/...`. Root ordering and hierarchy changes are being used as part of the network contract.

If Editor and build compute different IDs, they send/listen to different room-property keys. Trigger requests are discarded by `key != networkKey`, and puzzle RPC lookups fail silently. This provides a common mechanism for multiple apparently unrelated failures, especially in the user's Editor/build test arrangement. The actual IDs must still be logged and compared on both clients to establish this as the observed root cause.

Recommended correction: persist a unique ID per placed scene instance, validate uniqueness and missing identities in authoring/build checks, and use the same identity consistently for requests, room state, puzzle slots and relevant pickups. Prefab asset IDs must not be reused by every placed instance. Runtime-spawned objects need authoritative assigned IDs.

### 2. Doors have authority and transport-selection gaps

`DoorController.cs:506` applies room-property fallback changes immediately on the requesting client, then publishes them. It does not route these requests through the Master or use an expected-state comparison. Concurrent requests can compete using stale local open/locked state.

`ShouldUsePhotonViewSync` at line 1138 accepts a nonnull PhotonView without checking `ViewID > 0`. It is checked before the property fallback, so an unregistered view selects an invalid RPC path. `DoorSignalSource` has the same condition. The inspected Map3 doors do not have an assigned PhotonView; this is a confirmed code defect but is not established as the cause of the present scene failure.

Recommended correction: consistent scene identity, authoritative validation for door commands, state revision/conflict handling, and RPC selection only with a valid registered view. Preserve paired leaves and replicate resolved opening directions.

### 3. Trigger requests can fail silently

`InteractionTriggerSystem.Networking.cs:25` rejects a request for a mismatched key, stale sequence, actor ownership, or interaction range. The sender receives no explicit application-level rejection/resynchronization response. Successful `RaiseEvent` only reports submission, not accepted gameplay execution.

Replica state application restores the lever and accepted-player feedback; it deliberately does not replay the linked gameplay target. That target runs on authority and must publish its own replicated state. Replaying targets on all clients would duplicate puzzle/trap/platform actions.

Recommended correction: stable identities, diagnostic request IDs and rejection reasons, accepted-state responses, and a bounded resynchronization path for stale state. Preserve Master-only gameplay dispatch.

### 4. Puzzle world state depends on player RPC history

`HandEquipmentController.StatuePuzzle.cs` sends plate placements and puzzle checks through `AllBufferedViaServer` on player PhotonViews. `StatuePuzzleController.ApplyCheckSolution` recalculates success from each client's local slots. There is no independent room snapshot of slot occupancy or solved state.

Consequences include silent loss when slot/item/puzzle IDs do not resolve, inconsistent conclusions when local slot state differs, and weak late-join recovery after the relevant player's cached events disappear. Existing connected clients do not automatically lose their current state when a player leaves; the concern is recovery for later clients and room continuity. The pending check also has no timeout if the expected callback never applies.

Recommended correction: Master validates placements/checks, publishes authoritative slot/item assignments plus solved state and revision, and triggers failure/success outputs once per accepted attempt. Keep inventory ownership and world plate placement coordinated. Late join should restore current state without replaying old failure effects.

### 5. Flamethrower synchronization exists

`FlamethrowerTrapController.Networking.cs` publishes sequence, activation time and active state through room properties; clients derive phases using Photon time. Player damage is local-owner-only, enemy damage is Master-only.

The room's three failure connections are present and local-only is off. If trap IDs differ, remote clients never apply the cycle and therefore do not emit fire or run local-player fire damage. There is no evidence that the intended correction requires networking particles individually.

Recommended correction: stable trap identities and verification of phase, VFX, light, audio, cooldown and damage from the same replicated cycle. Keep damage ownership rules.

### 6. Platform synchronization exists; check upstream activation first

`PlatformController.cs:1070` publishes elapsed progress, Photon reference time, movement state, break/respawn state and travel direction. Remote clients reconstruct movement from the snapshot. Its stored room key can match across Editor/build independently of the lever's runtime-derived key.

No missing platform replication mechanism was established. A failed lever request or failed authoritative puzzle check can prevent its signal from changing at all. If the signal changes and a platform snapshot is published, compare receipt/decoding and motion on the other client separately. Player carrying and lever anchoring during travel need independent validation.

## Proposed correction order

1. Capture both clients' region, AppVersion, room, Master actor, scene/build identity and resolved mechanism IDs; record request receipt, acceptance/rejection and property application. Compare RPC lists when using different builds.
2. Replace hierarchy-generated identities with validated persistent scene-instance identities for the whole connected chain.
3. Standardize authority for requests and correct valid-view/fallback selection.
4. Give the puzzle room-owned current state and ordered, authoritative mutations.
5. Verify traps and platform through their existing timed state paths; correct any remaining presentation, carrying or late-join defects identified by those checks.

## Required connected validation

- Editor as Master + standalone client, then standalone as Master + Editor client.
- Two clients from the same fresh build as a control case.
- Both door leaves: open/close from either side and simultaneous requests.
- Plate pickup, remove, replace and competing placements from both clients.
- Incomplete/incorrect attempt: all three traps, audio/light, local-player damage, cooldown and lever release.
- Correct attempt: one completion, plate locks, route switch and immediate platform descent.
- Main lever repeated A/B trips, lower-floor call, reversals, passengers and lever anchoring during movement.
- Late join before/during/after puzzle completion and while the platform/traps are active.
- Departing placing/submitting player, Master migration, and return to the scene.

Existing tests cover local gameplay and some state decoding/restoration, but several explicitly enable local-only mode. Those checks do not establish transport, cross-client identity equality or room persistence. No test suites were rerun for this read-only audit.

## Primary references

- [Photon synchronization and state](https://doc.photonengine.com/pun/current/gameplay/synchronization-and-state): room properties, concurrency and CAS.
- [Photon RPCs and RaiseEvent](https://doc.photonengine.com/pun/current/gameplay/rpcsandraiseevent): buffered events and RPC-list compatibility between builds.
- [Photon host migration](https://doc.photonengine.com/pun/current/gameplay/hostmigration): application responsibility for retained room state.
