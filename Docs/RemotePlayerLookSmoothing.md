# Remote Player Look Smoothing

## Scope and checkpoint

Checkpoint: `.codex/backups/remote-player-look-20261109/` contains the affected saved sources, Player prefab and look-system documentation before this change.

The user confirmed that matching standalone builds synchronize the statue room. This change addresses visible stepping in remote player head direction and torso pitch, rather than changing room mechanisms.

## Implementation stages

1. Smooth received look angles continuously on remote players, with a separate configurable response time. Keep local camera/input behavior and Photon serialization unchanged.
2. Feed head look and attack aim from the same filtered angles. Preserve smooth acquisition/release and complementary pitch when an attack ends. Initialize the filter from the first received state and reset it for teleports.
3. Save the response time on the shared Player prefab and add deterministic Editor regression coverage for packet gaps, turns, pose transitions, attack handoff and local input.
4. Compile and run the focused tests in the Unity Editor without replacing or saving the user's open scenes. Report the remaining validation needed in two standalone clients.

## Authoring

`PlayerMovement > Network Sync > Remote Look Smooth Time` controls the remote-only angle filter. The initial value is 0.08 seconds. A larger value softens motion further and increases visual delay. Zero disables the network angle filter while retaining the authored pose acquisition/release behavior.

## Validation

- Runtime and Editor CLI builds pass with zero warnings and zero errors.
- 29 cases pass on the Unity Editor main thread: 13 remote-look cases (including 30/60/120 FPS cases) and 16 existing melee-aim regressions. The new cases cover continuous head/torso motion between 10 Hz packets, held-pose stability, reversals, the angular seam, walking-to-idle reacquisition, attack release, initialization/teleport reset, zero smoothing and unchanged local camera response.
- The shared Player prefab is checked for the serialized 0.08-second response time.
- All 36 `SendNext` entries and all 36 `ReceiveNext` entries retain their previous fields and ordering. Network send rate and body movement interpolation are unchanged.
- Results: `.codex/diagnostics/remote-player-look-20261109.txt`. Tests simulate received states and Animator layer changes; they do not establish behavior across a real Photon connection.
- The temporary Editor validation helper was archived in the checkpoint and removed. The original Menu scene was restored clean without saving over its source asset.

Rebuild both standalone clients to validate the preferred visual response with actual latency. Check idle/crouched look, full body turns, walking-to-idle transitions and attacks while looking up/down. The response time is configurable; network interpolation cannot remove network latency or repair an unrelated whole-frame stall.
