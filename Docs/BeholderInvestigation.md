# Beholder room investigation

The Beholder's last-seen flow keeps a latest sensed position for each player.
Sight, movement hearing, contact, and player-sourced noise update evidence on the
authoritative instance only. A room transition is a delayed clue, not a live
tracking destination: after losing the target, Chase visits the primary player's
last sensed point, then the other recent players' points. Search sweeps the
original room before following a confirmed transition to another room.

`EnemySearchArea` trigger boxes define room membership and their points define
the sweep. Multiple BoxColliders on the area object form one room; optional
`Additional Area Bounds` can reference colliders on child objects. An
`EnemyTraversalVolume` with both side search areas linked provides a physical
route between rooms; the volume still owns door traversal, but door contact or
player passage never creates a perception clue. The Beholder also resolves its
own room from these bounds and keeps its current room through small doorway
gaps or overlapping colliders. Being in the same room never detects a player:
vision, hearing, contact, or player-sourced noise must first create evidence.
Unlinked or locked routes are skipped rather than teleporting or searching a
room through a wall. Enemies without the last-seen flow retain legacy behavior.

An off-NavMesh sensed point is projected to a reachable point along the agent's
path. Chase approaches that safe endpoint, then Search begins; the Beholder
does not walk directly off the NavMesh to reach the original coordinate. Room
transitions are confirmed from the player's room-bound colliders for 0.2 seconds,
even when the player is no longer visible, but only after a real perception
event has started an evidence trace. The memory expires after 45 seconds.

The authority owns evidence and investigation decisions. Remote instances
receive state, target, position, dialogue, and a compact evidence snapshot
through `EnemyNetworkSync`. The snapshot lets a new Master resume the current
investigation if authority changes; it does not broadcast every sensing tick.

## Emotional perception

The Beholder keeps its authored vision distance (12 m), view angle (100 degrees),
and hearing multiplier (1.4 for `NoiseSystem`) as baselines. Mood profiles add
runtime-only perception modifiers; vision check frequency and player-authored
movement noise radii do not change. The approved tuning is:

| Mood | Vision distance | View angle | Hearing factor | Maximum hearing bonus |
| --- | ---: | ---: | ---: | ---: |
| Neutral | 12 m | 100 degrees | 1.0 | 0 m |
| Irritated | 14 m | 107 degrees | 1.1 | 1 m |
| Angry | 16 m | 113 degrees | 1.2 | 2 m |
| Infuriated | 18 m | 120 degrees | 1.3 | 3 m |

For either player movement or a `NoiseSystem` event, the effective hearing
radius is `base + min(mood bonus cap, base * (mood factor - 1))`. Player movement
uses its crouch/walk/sprint radius as `base`; other noise uses event radius,
intensity, and the existing 1.4 hearing multiplier. A zero-radius idle player
remains silent, and low-radius sounds stay proportionally quiet. Only the
authority performs perception; replicated mood applies the same local profile
on remote instances and after authority handoff.

## Map setup

1. Put one `EnemySearchArea` on each room's `Point of Interest` object. Give it
   one or more enabled BoxColliders covering the room and author the points to
   visit. All BoxColliders on that object are included automatically; use
   `Additional Area Bounds` for colliders on children. They can be triggers;
   the Beholder queries their union directly. Keep search points on the NavMesh
   and inside at least one of these bounds. If a bound is on a child, populate
   `Points of Interest` explicitly so that child is not treated as a search point.
2. On each connecting `EnemyTraversalVolume`, assign the physical room on
   `Side A Search Area` and `Side B Search Area`. An unlinked or locked door is
   not a valid search transition. The door/traversal system still handles the
   crossing itself.
3. On the Beholder, leave `Use Last Seen Search Flow` enabled. `Room Change
   Confirmation Time` defaults to 0.2 seconds to reject boundary flicker, and
   `Player Evidence Lifetime` defaults to 45 seconds. Search duration, speed,
   and point-wait settings control each room sweep; Map3's Beholder currently
   uses 18 seconds before the mood multiplier. An unreachable last-seen position
   falls back to the nearest reachable NavMesh endpoint, then to the room's
   points. The Console warns when none of those points are reachable.

Two connected builds must use the same code because the Photon snapshot format
changed. The Unity Test Runner should cover `EnemyPerceptionTests` and the
existing `EnemyTraversalTests`; then verify A to B, a two-player chase, and a
Master handoff in Play Mode with two clients.
