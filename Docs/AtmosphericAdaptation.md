# Atmospheric Adaptation

The system is parked and is not attached to Map3. To revisit it later, add `AtmosphericAdaptationController` to a scene GameObject. In Play Mode it finds the locally owned player and creates a temporary URP Volume on `FP_Camera`. It does not edit the camera's saved Volume Profile, lights, materials, shaders, or network state. The runtime Volume has priority 20, between the base camera Volume (0) and damage/health feedback (50).

## Inspector tuning

- `Dark Profile` and `Light Profile` tune a color-filter swatch plus saturation, contrast, and exposure offsets. The dark filter is slightly cool; the light filter is slightly warm. The controller no longer animates White Balance, reducing the risk of unwanted color casts.
- `Global Intensity` blends the atmosphere with the camera baseline. It starts at 0.9; 0 disables the effect.
- `Enable Exposure` starts on. Its small negative dark and positive light offsets make the transition visible without changing scene lights; turn it off to compare color and contrast alone.
- `Dark To Light Seconds` and `Light To Dark Seconds` control automatic transition speed.
- `Sample Interval`, `Max Light Distance`, `Intensity For Full Light`, and `Occlusion Mask` control sensing. Point and spot lights contribute according to intensity, range, distance, and line of sight. Raise `Intensity For Full Light` if the scene reaches the Light Profile too easily; lower it if nearby torches seem too weak. Directional lights and the player's visibility-assist light are ignored.
- `Detected Light Influence` shows the last sample from 0 (dark) to 1 (light). `Current Blend` shows the applied transition state. `Current Volume Weight` and `Runtime Status` show whether the effect is actually bound and contributing.
- `Preview Mode` can force either look or use `Manual Blend` for immediate comparison in Play Mode. Return to `Automatic` for gameplay.

The camera's saved Color Adjustments and White Balance components may remain disabled. This is intentional: when the controller is attached to a scene, it creates a separate, active `AtmosphericAdaptationVolume` under the locally owned `FP_Camera` in Play Mode. Only Color Adjustments is overridden by this effect. Select that runtime child to inspect the active overrides. If `Runtime Status` stays at `Waiting for local player`, check that the scene contains the controller and that a local player has spawned. If it reports a camera setting error, enable Post Processing or include the camera's layer in its Volume Layer Mask as indicated.

No setup is needed on the player prefab. To use the effect in another scene, add the same controller to one scene GameObject. It will bind to that client's local player when the player spawns. The first-person camera's Volume Layer Mask must include its own layer, as it does in the current player prefab.

Vignette is intentionally untouched because the existing damage, low-health, and low-stamina feedback already owns it.
