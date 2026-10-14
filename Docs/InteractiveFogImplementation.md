# Interactive Fog Implementation

## Scope
- Repair the imported `Assets/Fog` tutorial setup so it runs in this URP project without manual null-reference wiring.
- Keep the effect as a local interactive fog volume: moving density texture, player wake, obstacle clearing, and scene-depth clipping.
- Leave the project-owned `WKFakeVolumeController` system untouched; that system remains the preferred decorative light/fog volume library.

## Implementation Notes
- `FogSimulation` owns the compute-driven density field used by `FogVolume.shader`.
- The component resolves the local runtime player because gameplay players are spawned from `Assets/Resources/Player.prefab`.
- `FogObstacle` remains the explicit authoring component for important blockers, while `FogSimulation` can also collect suitable scene colliders automatically.
- `GameObject > VFX > Interactive Fog Volume` creates a configured cube volume with the correct material, compute shader, and renderer settings.

## Use
1. Add or select an `Interactive Fog Volume`.
2. Size the effect with the GameObject `Transform > Scale`.
3. Enable `Follow Player Horizontally` for a player-centered fog bank, or disable it for fixed room fog.
4. Add `FogObstacle` to props that should carve extra clear space, especially pillars, walls, and large scene objects.
