# WK Fake Volume Workflow

The WK fake volume system is the project-owned fog/light volume library. The runtime prefabs do not depend on the original asset package.

## Asset Library
- Runtime prefabs live in `Assets/Prefabs/VFX/FakeVolume`.
- Project-owned shaders, materials, textures, and mesh copies live in `Assets/VFX/FakeVolume`.
- `WKFakeVolumeController` and shared settings types live in `Assets/Scripts/VFX`.
- Parameter reference: `Docs/WKFakeVolumeControllerParameters.md`.

## Authoring Flow
1. Use `GameObject/VFX/WK Fake Volume`, or create an empty GameObject and add `WKFakeVolumeController`.
2. The controller creates a generated visual root automatically when no WK volume renderer is already present.
3. Choose `Shape` on the controller. Generated volumes rebuild their mesh/material/light setup when the shape changes.
4. Tune the local controller settings directly.
5. Duplicate a `WK_` prefab from `Assets/Prefabs/VFX/FakeVolume` only when a reusable authored variant is useful.

## Editor Tools
- `GameObject/VFX/WK Fake Volume` creates a new tuned-ready volume GameObject.
- The normal Inspector has no routine action buttons. Generated visuals update automatically when `Shape` or `Create Generated Light` changes.
- `Tools/VFX/Fake Volume/Add Controller To Selection` adds and binds a controller to selected scene objects.
- `Tools/VFX/Fake Volume/Auto Bind Selected Controllers` refreshes selected controller references.
- `Tools/VFX/Fake Volume/Rebind WK Prefab Library` refreshes the prefab library without reading from the original package.

## What The Controller Owns
- Volume material properties through `MaterialPropertyBlock`: color, opacity, power, edge, fade distance, noise, extra noise, and wind.
- Dimensions through Unity `Transform` scale. The controller does not write `localScale`.
- Optional generated Unity `Light` values, including separate light color when `Use Volume Color` is off. Generated light creation is off by default; enable `Create Generated Light` when the volume should emit real light.
- Optional flicker, hue cycling, swinging, rotating, and drifting.
- Shapes: `PointSphere`, `SpotCone`, `Cylinder`, `Cross`, and `Box`.
- Smoke-like movement comes from `Noise A Speed` and `Noise B Speed`. `Use Wind` adds shader flow to the fog pattern on generated shapes; cone-style legacy noise scales are normalized internally while wind is enabled so the motion remains visible.

## Guardrails
- Prefer this controller over third-party demo scripts on production prefabs.
- Keep renderer changes instance-local through the controller. Do not tune shared materials for per-object variation.
- Generated visuals live under a child named `WK Fake Volume Visual`; automatic rebuilds only replace that generated child.
- If a hand-authored prefab needs true multi-shape switching, create child roots for each shape and add them to `Shape Bindings`; otherwise use `Existing` and let the prefab's current mesh define the shape.
