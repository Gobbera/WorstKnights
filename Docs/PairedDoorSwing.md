# Paired Door Swing Direction

Checkpoint: `.codex/backups/paired-door-swing-20261109/`.

The current reimported Door_Arch_B has identity rotations and positive unit scales on both leaf roots/hinges. Both persisted opening angles were +90 degrees. Sampling the panel centers confirmed that a shared +1 direction moved the left panel +0.78233 m along world Z and the right panel -0.53233 m, splitting the opening across opposite sides of the doorway.

Pairing must share the physical opening side rather than blindly copying a hinge-local sign. Resolve the initiating panel's chosen side from its closed normal and the panel-center travel. Evaluate both directions on the peer and use the one that reaches the same side. Each root remains an equivalent controller and retains its own angle magnitude, hinge, curve, duration and reactions.

Open Away From Player also uses panel-center geometry rather than only the moving Transform origin, because an origin located on the hinge does not translate when the panel rotates. Cache the reference in closed local space so child hinges, scale and mirrored imports remain supported. Room-state publication keeps the existing encoding but stores each peer's resolved direction under its own key. Closing retains each peer's current direction.

## Validation

All 164 focused Editor cases passed. Four new cases cover equal positive angles on opposite hinge axes, either peer initiating the physical side, transform origins on the hinge and application of each peer's own network direction without flipping the pair.

Native Play used a copy of the current live arch and PlayerPickupInteractor's real aim/use path. From the positive side of the doorway, the full-open panel-center Z displacements were -0.782336 m and -0.532336 m. Opening from the opposite side produced positive displacements for both panels, including +0.3536384 m and +0.2406311 m during travel. Both hinges remained fixed, both panels reached their configured angles, closing returned both exactly, and reversal remained synchronized. Per-root prompts, cooldown and reactions also passed.

The live scene's authored door timing is 3.5 seconds; the test preserves its curve, including the small initial anticipation. Same-side checks run after that anticipation, and the reverse interaction is timed against the current duration rather than the older 1.5-second setup. Colliders match the current mesh bounds, so no collider or geometry edits were needed. The original dirty scene remains loaded and is restored after validation. No model, transform, material or serialized scene changes are required for the fix. Temporary validation assets and the helper are removed. Evidence: `.codex/diagnostics/paired-door-swing-20261109.txt`. Two-client Photon validation was not performed.
