# Skeleton Variants Implementation

## Requested outcome

Export the live `SkeletonTesteIA` Blender working copy into
`Assets/WorstKnights/Models/Enemies/New Skeleton`, following the existing
Skeleton model, extracted AnimationClip and Animator Controller organization.
Create `Skeleton Swordsman` and `Skeleton Archer` prefabs using the existing
Skeleton enemy setup. Preserve the original enemy, model assets and unrelated
working tree edits.

Gameplay prefabs live in `Assets/Prefabs/Enemies/New Skeleton`, following the
existing separation between enemy model assets and gameplay prefabs.

## Checkpoint and scene preservation

- Saved-on-disk enemy scripts, editor tools, prefabs, original Skeleton assets,
  project version and layers are copied into a timestamped `.codex/backups`
  directory with a SHA-256 manifest before implementation.
- A Blender MCP snapshot captures the live authoring data. Export processing
  runs in an isolated background Blender process using that snapshot.
- The active Unity scene and its unsaved changes must be preserved. Asset
  authoring and verification use prefab assets or isolated additive scenes.
- All new authoring text is English. User-facing progress is Portuguese.

## Asset stages

1. Inspect the existing enemy prefab, materials, animation naming, import
   settings, lifecycle, navigation, player damage, shields and Photon authority.
2. Export the shared body and eight rig animations as `SkeletonTesteIA.fbx`.
   Export Sword, Bow, Quiver and Arrow into `Equipment`. Preserve bow blend
   shapes. Record calibration transforms and accessory animation samples.
3. Extract clips into `Animations`, configure the Generic rig consistently with
   the existing Skeleton, and create controllers following its state names.
4. Build a shared Skeleton prefab from the existing Enemy configuration with
   the new visual model and materials. Create Swordsman and Archer variants.
   Rigid equipment follows hand/back sockets. The shot presentation arrow is
   animated in model-local space; its flight is not a world-space model clip.
5. Reuse spawn, death, perception, investigation, motor, navigation, health and
   networking contracts. Add an optional archer combat component and narrowly
   scoped shared AI integration, leaving melee and Beholder paths unchanged.

## Archer combat behavior

- Approach a visible target until within shooting range, stop and face it,
  prepare the shot, release once, then recover before another attack.
- Prefer a configurable standoff distance. Retreat to reachable NavMesh
  positions when a visible player gets too close; use bounded sampling and
  hysteresis, and do not freeze if a retreat position cannot be found.
- Use the existing last-seen/search behavior after losing sight. Do not track
  hidden targets through walls or fire through blocking geometry.
- Preserve the authored shot timing: pickup frame 24, draw frames 46-52,
  release frame 57, recovery through frame 76, at 24 fps.
- Animate bow draw and the presentation arrow alongside the body clip. Create
  an independent projectile at release; hide the presentation arrow afterward.
- Only the enemy authority creates damaging projectiles. Use swept collision,
  owner exclusion, one terminal hit, shield interaction, player damage routing,
  gravity/lifetime, and visual impact cleanup. Replicate attack timing and
  projectile presentation without applying duplicate damage on remote peers.
- Cancel preparation on death, disable, stun or interruption. A cancelled
  preparation must never release a delayed arrow.

## Validation stages

- Verify source Blender Actions, rest hierarchy and transforms remain intact.
- Verify imported animation paths, materials, bind pose, unit scale, forward
  direction, socket alignment, bow deformation and arrow/body synchronization.
- Compile runtime and editor assemblies sequentially and refresh the live
  Editor before relying on newly added scripts.
- Test release timing across frame steps, cancellation, cooldown, targeting,
  wall collision, shield blocks, self exclusion, single-hit damage and authority.
- Exercise both prefabs in isolated Unity scenes, including approach, retreat,
  firing, interruption and death, while preserving the user's scene state.
- Document actual validation results and any remaining manual review items.

## Delivered state

- FBX, equipment, calibration metadata, eight extracted clips and two controllers
  are generated under the requested New Skeleton model directory.
- Shared base, Swordsman, Archer and projectile prefabs are under
  `Assets/Prefabs/Enemies/New Skeleton`.
- Swordsman preserves the original melee setup. Archer uses the optional ranged
  component, authored shot timing, local equipment presentation, swept projectile
  collision, shield/player damage routing and Photon presentation replication.
- All 17 focused cases and 26 existing perception/combat checks passed.
- Four exploratory legacy traversal checks failed and remain documented in
  `Docs/SkeletonArcher.md`; traversal/navigation implementations were retained.
- Source preservation audit: 143 of 149 checkpointed files are byte-identical.
  The six changed existing files contain the scoped optional archer integration.
  Original model, clips, controller, prefab and project layers are unchanged.
- The original Map3 scene is restored after isolated testing. Review combat
  tuning and real multi-client Photon behavior before production deployment.
