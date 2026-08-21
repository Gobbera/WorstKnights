# Napkin Runbook

## Curation Rules
- Re-prioritize on every read.
- Keep recurring, high-value notes only.
- Max 10 items per category.
- Each item includes date + "Do instead".

## Execution & Validation (Highest Priority)
1. **[2026-08-19] Shader Graph edits can reimport mid-write and require exact graph references**
   Do instead: for `.shadergraph`/`.shadersubgraph` rewrites, preserve a blank line between top-level JSON objects, validate multi-object JSON immediately, and verify every edge `m_Node.m_Id` points to a graph node object, not a slot object, before Unity imports it.

2. **[2026-07-08] Cross-network tests must prove Photon region and build identity first**
   Do instead: before debugging Vivox or gameplay RPCs, log and compare `CloudRegion`, `AppVersion`, room name, player count, and a build hash on every client; use the same explicit region during controlled tests.

3. **[2026-04-07] Player physics must not be driven from `Update`**
   Do instead: cache input in `Update`/controller code and apply Rigidbody forces or velocity corrections in `FixedUpdate`.

4. **[2026-05-13] Gameplay player setup now has a single prefab source**
   Do instead: when debugging spawn, camera, model sockets, or runtime composition, inspect `Assets/Resources/Player.prefab` because both the scene reference and Photon spawn use that same asset.

5. **[2026-04-15] Unity script moves must carry `.meta` files with them**
   Do instead: move `.cs` and matching `.meta` together and delete stray script copies without `.meta`, otherwise Unity imports, prefab references, and generated project files can drift apart.

6. **[2026-05-07] Cross-project scene migrations can keep YAML intact while breaking GUID-linked refs**
   Do instead: compare imported scene GUIDs against local `.meta` files, then either preserve the original `.meta` or patch the scene/prefab references before debugging runtime logic.

7. **[2026-04-07] Unity movement refactors need asset serialization checks**
   Do instead: after changing serialized movement fields, open the matching `.asset` YAML or inspector values and confirm new fields were actually written.

8. **[2026-05-11] Fresh Unity scripts can miss CLI validation until project files regenerate**
   Do instead: when `dotnet build Assembly-CSharp.csproj` ignores a new `.cs` file, either trigger a Unity project-file refresh before trusting the build or validate by folding the change into an already-included script.

9. **[2026-06-29] Parallel runtime/editor CLI builds can lock Unity temp assemblies**
   Do instead: run `dotnet build Assembly-CSharp.csproj` and `dotnet build Assembly-CSharp-Editor.csproj` sequentially, because both can contend for `Temp/obj/Assembly-CSharp/*.dll`.

10. **[2026-05-12] Unsaved scene authoring changes are invisible to file-level patching**
   Do instead: when a user adds scene objects in the editor but they are not yet in the `.unity` YAML on disk, prefer a runtime/bootstrap attachment path or ask them to save the scene before patching references directly.

## Domain Behavior Guardrails
1. **[2026-07-13] New gameplay scenes must be registered in both scene bootstraps and Build Settings**
   Do instead: when cloning or adding a gameplay map, update every hardcoded gameplay-scene check such as `RoomManager`/enemy bootstraps and add the scene to `ProjectSettings/EditorBuildSettings.asset`, otherwise direct Play and menu/room loading diverge.

2. **[2026-07-23] Menu-to-gameplay transitions must not depend on raw scene asset paths or immediate Photon readiness**
   Do instead: load gameplay scenes by validated build index/name from `RoomList`, and let `RoomManager` resume room join from callbacks like `OnJoinedLobby` when the menu changes scene before Photon finishes becoming ready.

3. **[2026-07-23] Scene-fixed interactables should degrade from PhotonView sync to room-state sync**
   Do instead: keep doors and destructibles usable offline/local by default, use RPCs when a valid `PhotonView` exists, and fall back to scene-stable room properties plus master-authority requests when the object is a fixed scene actor without manual `PhotonView` setup.

4. **[2026-07-14] ProBuilder stairs and walls should not ship with raw MeshCollider gameplay collision**
   Do instead: for movement-test maps, generate helper ramp colliders for straight ProBuilder stairs and helper box colliders for snaggy walls, then disable the original MeshCollider so the player capsule reads simplified collision.

5. **[2026-07-15] Slope crest stability depends on surface-normal snap plus a short post-slope adhesion window**
   Do instead: keep `PlayerMovement` ground snap relative to the contacted surface normal instead of world `Y`, and preserve a brief downward adhesion probe after walkable slopes or step assists so sprinting off the last stair/ramp segment stays grounded without suppressing real jumps.

6. **[2026-05-08] Gameplay room bootstrap lives in code, not as a fixed scene object**
   Do instead: when connection or spawn fails in `FieldTestCharacter`, inspect `RoomManager.BootstrapGameplayRoomManager()` and scene-name/path checks before hunting for a missing manager in the hierarchy.

7. **[2026-05-26] World pickup items should be authored on prefab assets, not inferred by runtime name scans**
   Do instead: attach `WorldPickupItem` on the prefab root, bind the correct `ItemDefinition`, and save an explicit trigger collider on the asset so scene instances inherit stable pickup behavior.

8. **[2026-05-21] Menu lobby refresh must handle stale Photon connections**
   Do instead: when re-entering the menu, if the client is still connected but outside a room, join the lobby directly or disconnect first; never wait on `!PhotonNetwork.IsConnected` unless this scene actually triggered a disconnect.

9. **[2026-04-07] Player probes and state decisions must stay aligned with the real capsule**
   Do instead: derive casts from the `CapsuleCollider`, refresh grounding before controller state decisions that can set `MovementState.air`, and keep the player capsule on a low-friction physics material.

10. **[2026-04-13] Free-look yaw splits must keep movement orientation on camera yaw**
   Do instead: rotate the `Orientation` child with the look/head yaw and let the body root catch up separately so idle free-look does not break camera-relative locomotion.

## Movement & Actions
1. **[2026-07-29] External conveyor pushes must survive player speed control**
   Do instead: accumulate conveyor velocity in `PlayerMovement.VolumeModifiers` and apply it in `FixedUpdate` after `SpeedControl`, so the player motor does not clamp or cancel the belt push.

2. **[2026-08-12] Crouch kick must stand before firing**
   Do instead: when kick starts from `MovementState.crouching`, validate kick availability, stand/queue the kick briefly, suppress crouch while queued or active, then let held crouch re-enter after `kickActionDuration`.

## UI & Emotes
1. **[2026-07-30] Emote wheel slots are authored from the player emote controller**
   Do instead: edit the five `emoteWheelSlots` on `Assets/Resources/Player.prefab`'s `PlayerEmoteWheelController` for emote type, label, and sprite, and use `Assets/Prefabs/UI/EmoteWheel.prefab` only when a scene needs an explicit saved Canvas object.

## Audio & Feedback
1. **[2026-08-17] Melee VFX context must survive each reaction bridge**
   Do instead: when adding per-attack VFX parameters, pass `ReactionSignalContext` through both `ImpactReactionSignalBridge` and destructible damage flows; for impact feedback, keep `ImpactReactionSignalBridge` on the same GameObject as `PhotonView` so its lightweight RPC can replay the cosmetic signal on other clients.

2. **[2026-08-20] Ground-aware VFX Graph collision should be bound from runtime raycasts**
   Do instead: for blood/splatter VFX that use `Collision Shape`, expose ground center/normal/size parameters in the VFX Graph and drive them with `VisualEffectGroundCollisionBinder` instead of hardcoding a world-space box at `y=0`.

3. **[2026-07-13] Vivox native positional can be replaced by simulated proximity**
   Do instead: keep `Use Native Vivox Positional Audio` off when native `JoinPositionalChannelAsync` causes `5100/1001`; join a 2D `-sim3d` Vivox channel, map `VivoxParticipant.PlayerId` through Photon `kwkVivoxPlayerId`, and drive per-participant local volume/mute from Photon player distance.

4. **[2026-07-13] Standalone Vivox profiles are intentionally ephemeral by default**
   Do instead: when testing builds, expect profiles like `kwk-player-xxxxxxxx`; only pass `--ugs-profile=<name>` for deliberate persistent identity, and never reuse the same manual profile in two simultaneous clients.

5. **[2026-07-08] Vivox channel drops must suspend 3D publishing and reconnect with backoff**
   Do instead: treat `ChannelJoined` as the safe point for `Set3DPosition`, publish around `0.3s`, and on `ChannelLeft` or connection recovery stop 3D updates immediately and retry with backoff so server disconnect `5100` does not cascade into stale-session `1001` loops.

6. **[2026-07-08] Same-machine standalone Vivox clients need distinct Authentication profiles**
   Do instead: build once and launch each standalone instance with a unique `--ugs-profile=<name>` argument; otherwise both instances reuse the same cached anonymous identity while Editor + build appear healthy because their defaults already differ.

7. **[2026-07-07] Vivox positional voice must publish the owner-local camera pose at a limited rate**
   Do instead: join a positional channel with shared `Channel3DProperties`, resolve the owning `PlayerSetup`/`FP_Camera`, and call `Set3DPosition` about 2-4 times per second rather than every frame.

8. **[2026-07-07] Vivox package installation does not populate service credentials**
   Do instead: complete Vivox Dashboard onboarding, open `Project Settings > Services > Vivox` to pull Server/Domain/Issuer, validate before Play/build, and rebuild players made before credentials were saved.

9. **[2026-07-07] Photon rooms remain authoritative when integrating Vivox**
   Do instead: mirror the active Photon room into a deterministic Vivox channel and use Unity Authentication only for Vivox identity; do not introduce Unity Lobby or Multiplayer Services as a second room authority.

10. **[2026-07-02] Cross-client audio should reuse replicated gameplay state before adding dedicated Photon events**
   Do instead: drive jump, land, attack, pickup, emote, and similar world SFX from existing replicated sequence counters or shared item/state application paths first, and only add explicit audio RPC/event traffic for sounds that have no stable gameplay signal to hook into.

## Inventory & Equipment
1. **[2026-08-17] Sword trail no longer uses the legacy attack-trail stack**
   Do instead: keep `Assets/Prefabs/Items/Sword.prefab` free of `BladeTrailSocket`, embedded `VFX_Sword_Trail`, and `WeaponAttackTrail`; let the newer sword trail system own playback.

2. **[2026-08-11] Item grips are perspective-specific per hand**
   Do instead: keep one `WorldPickupItem` as the gameplay/network source, author TP grips into `GripPoints_TP_Right`/`GripPoints_TP_Left`, author FPS grips into `GripPoints_FPS_Right`/`GripPoints_FPS_Left`, expose both perspectives in the inspector/tooling, keep legacy `GripPoint*` names as migration fallback only, and let runtime FPS fall back to TP only when no FPS grip has been saved yet.

3. **[2026-08-11] World pickup item network physics is intentionally disabled for retest**
   Do instead: keep item prefab roots with a local dynamic Rigidbody (`useGravity=true`, `isKinematic=false`, `detectCollisions=true`) plus an enabled solid `DropCollision`, do not reintroduce MasterClient pickup-physics snapshots or room-synced rest poses without a new design, and let equip/FPS clone paths suppress physics only while held or owner-visual.

4. **[2026-08-04] FPS item runtime sockets must match authoring sockets**
   Do instead: equip FPS item visuals into `RightHandSocket`/`LeftHandSocket` children under `FPS_Model`, not directly into raw `Hand.R`/`Hand.L` bones, so Scene authoring and Play mode use the same anchor.

5. **[2026-06-25] Hand sockets under rig bones must keep non-zero local scale**
   Do instead: when moving `RightHandSocket` or `LeftHandSocket` onto rig bones, verify the socket `Transform.localScale` stays near `(1,1,1)`; zero-scale sockets make equipped visuals instantiate invisibly.

6. **[2026-07-06] Multiplayer pickups need scene-instance ids, not prefab ids**
   Do instead: keep item prefab `networkSceneId` fields blank, let each scene instance resolve a deterministic id, rebuild `WorldPickupItem` lookup from live inactive-inclusive objects on a registry miss, and apply owner-local drops immediately while sending RPCs only to other clients.

7. **[2026-07-01] Hand grip authoring should happen in Scene Mode and keep explicit perspective refs**
   Do instead: place a prefab instance of the item in the scene, select it or open `Tools/Inventory/Item Grip Authoring Tool` from its Inspector, assign a scene `Target` that contains sockets, choose a compatible FPS/TP `Preview Socket`, adjust in Scene view, then save to the matching `GripPoints_TP_*` or `GripPoints_FPS_*` transform required by the item `HandRequirement`.

8. **[2026-05-27] Single-key actions across both hands need an explicit hand-resolution rule**
   Do instead: resolve shared actions like `G` drop from the last interacted hand first, then fall back to whichever active hand actually has an item instead of hardcoding right-hand priority.

9. **[2026-05-27] Dropped pickups must preserve their world source per slot**
   Do instead: when equipping a `WorldPickupItem`, store the original pickup reference alongside the slot data, then make `DropToWorld` reactivate inactive stored pickups before repositioning them instead of relying only on `ItemDefinition` or the equipped visual clone.

10. **[2026-08-05] Physical item drops must restore pickup transform before physics**
   Do instead: cache the pickup's pre-equip world scale/parent, keep all item colliders disabled while equipped, suppress drop Rigidbody/collision before reparenting, restore scale on `DropToWorld`, then enable `DropCollision` and launch physics.

## Animation Coupling
1. **[2026-08-05] First-person arms sync is owner-local and separate from TP**
   Do instead: drive `Assets/Models/Knight_FPS/Animation/KnightFPS.controller` from `PlayerMovement` action sequences on the imported `KnightFPS` Animator under `FPS_Model`; keep `FPS Base` as the locomotion authority with `Idle`/`Run Forward`, mirror `Run Forward` inside `FPS Right Arm` and `FPS Left Arm` when literal grip-to-run transitions are needed, put right-hand grip/attack/draw/pickup/emotes on `FPS Right Arm` with `Right Arm Mask`, put left-hand/torch/draw/pickup on `FPS Left Arm` with `Left Arm Mask`, configure transitions with explicit TP-style triggers/bools such as `Attack`, `AttackComboStep`, `PickUpItemRight`, `IsRightHandOccupied`, `IsLeftHandOccupied`, `IsLeftTorchEquipped`, and `IsMoving`, avoid reintroducing `FPSState`, force that Animator to `AlwaysAnimate`, keep TP controller transitions untouched, and hide FPS renderers through owner-only perspective rules.

2. **[2026-05-20] Enemy attack clips only fire when the controller trigger matches the replicated attack sequence**
   Do instead: when adding an enemy attack state, keep a trigger parameter named `Attack` on the saved controller asset and gate the transition into the attack clip from that trigger, not from unconditional exit-time links.

3. **[2026-07-01] Override action layers can latch the last pose when they return to `Empty`**
   Do instead: when an emote, grip, or other override layer exits to an `Empty` state with no motion, either zero that layer's runtime weight or crossfade into an explicit reset pose so lower layers can retake control immediately.

4. **[2026-05-22] Enemy hit reactions must bypass locomotion fallback**
   Do instead: when adding enemy damage states, drive them from a replicated damage sequence and suppress Idle/Walk fallback while the hit state is active, otherwise the locomotion crossfade can cancel the reaction immediately.

5. **[2026-05-06] Preserved FBX GUID swaps must not leave the old file beside the replacement**
   Do instead: once the replacement model is staged with the old `.meta`, delete the original FBX and its `.meta` immediately so Animator clip references re-resolve against the final asset instead of a duplicate-guid stale path.

6. **[2026-06-25] Hand pose masks must resolve from the saved player sockets**
   Do instead: when retargeting grip or emote hand layers, derive the right/left hand bone paths from `Assets/Resources/Player.prefab` socket parents and rerun `Tools/Animation/Sync Player Hand Animator` so the AvatarMask paths still match the live `Model/EditedKnight` hierarchy.

7. **[2026-06-24] Player model swaps are split between prefab visuals and Animator assets**
   Do instead: when replacing the knight, inspect `Assets/Resources/Player.prefab` for the FBX child under `Model`, then separately retarget `Animator.m_Avatar` plus every `Assets/Skecth/KnightAnimController.controller` motion reference from `Assets/Models/Knight.fbx` to the new FBX clips.

8. **[2026-04-29] NewKnight reimports must preserve FBX guid, generic avatar, clip loop table, and controller bindings**
   Do instead: replace the FBX in place without deleting its `.meta`, keep Rig=`Generic` with `Create From This Model`, verify clip names plus loop flags, and validate `Assets/Skecth/KnightAnimController.controller` because reimports do not repair external motion refs on their own.

9. **[2026-05-04] Sprint blend trees need intent axes, not normalized velocity axes**
   Do instead: keep physics driven by clamped/effective movement, but feed `AnimationInput` from intended axes scaled only by movement availability so `Vertical * LocomotionScale` can reach the authored sprint points like `(0, 2)`.

10. **[2026-05-05] Square 2D blend trees cannot hit diagonal corners from normalized keyboard input**
   Do instead: preserve raw axis intent like `(-1, 1)` or `(1, 1)` for animation parameters and clamp only the physical move vector, otherwise sprint diagonals top out near `(+/-1.41, 1.41)` and never reach `(+/-2, 2)` clips.

## Editor Automation
1. **[2026-08-20] Destructible fade support is validated from the spawn inspector**
   Do instead: use `Validate Fade Support` on `DestructibleSpawnOnDestroyed` or `Tools/Destruction/Validate Project Fade Support` to catch spawn prefabs whose fade entries lack `_FadeAlpha` or a fallback color property.

2. **[2026-08-17] Destructible fragments should use spawn-entry cleanup controls**
   Do instead: configure fragment prefabs through `DestructibleSpawnOnDestroyed` entries with `Lifetime > 0`, optional `Fade Out Duration`, `Ignore Player Collision`, `Ignore Enemy Collision`, and `Use Debris Collision Layer`; actors need refreshed collider-pair ignores, while player probes and combat masks need `DestructibleDebris` excluded because casts do not respect only `Physics.IgnoreCollision`.

3. **[2026-08-12] World object prefabs should use the Object Authoring Tool**
   Do instead: create new destructible scene props through `Tools/World/Object Authoring Tool`, edit existing prefabs/instances from the root `WorldObject` Inspector button, keep prefab assets under `Assets/Prefabs/Objetcs`, generate the explicit `Collision` child from renderers, and let the tool wire `WorldObject`, `DestructibleObjectController`, optional `DestructibleSpawnOnDestroyed`, `PhotonView`, and Damaged/Destroyed reactions.

4. **[2026-06-26] Hand mask sync should stay menu-driven during manual animation authoring**
   Do instead: keep `PlayerHandAnimatorSync` manual via `Tools/Animation/Sync Player Hand Animator` while learning or hand-tuning `AvatarMask` assets, otherwise entering Play can reapply generated hand-mask paths and hide what changed.

5. **[2026-06-28] Character pose checks can be authored in Scene Mode by sampling clips**
   Do instead: attach `SceneAnimationPosePreview` to the character/model root, load clips from the Animator or source FBX, keep the preview active while selecting props, and restore it from the Inspector or `Tools/Animation/Restaurar Scene Pose Preview`.

6. **[2026-06-30] Scene pose preview is a component override, not a global menu tool**
   Do instead: when the animation preview seems missing, open `FieldTestCharacter`, select the player/model instance that carries `SceneAnimationPosePreview`, and use its custom Inspector; if that Inspector block is absent, check for editor compile errors before assuming the tool was removed.

## Enemy & Combat Networking
1. **[2026-08-17] Enemy spawn/death lifecycle is code-driven and replicated**
   Do instead: keep `EnemyState.PreSpawn`/`Spawning`/`Dead` as brain states, let `EnemyAnimationController` crossfade `Pre-Spawn`/`Spawn`/`Death` directly, cancel active `EnemyAttack` on death, and schedule cleanup from death clip duration plus fade-start delay plus fade via `EnemyHealth` RPC.

2. **[2026-08-20] PvP hits must bypass same-alignment filtering without allowing self-hit**
   Do instead: when a player attack targets `PlayerHealth`, send a player-specific `DamageInfo` with `CombatAlignment.Neutral`, but reject the attacker's own `PlayerHealth`/`PhotonView` before applying damage.

3. **[2026-08-13] Enemy attack commitment should be separate from hit reach**
   Do instead: tune enemy AI with a larger serialized attack-start radius and keep damage tied to weapon/contact hit windows, so backing out of exact melee range cannot force endless chase loops.

4. **[2026-08-05] Player melee damage is timed by combo hitbox windows**
   Do instead: tune `PlayerMeleeAttack.hitboxWindows` on `Assets/Resources/Player.prefab` for each combo step's damage start frame, active duration, target distance, and radius instead of changing one global instant-hit distance.

5. **[2026-05-21] Enemy death cleanup must be scheduled on every client, not only on the authority object**
   Do instead: when an enemy can die under Photon, schedule destruction through an `RPC` to all clients and also destroy on replicated death application, otherwise remote clients can keep a dead corpse in scene.

6. **[2026-07-27] Enemy Rigidbody physics is authority-only**
   Do instead: keep authoritative living enemies dynamic with gravity and keep remote/non-living enemies kinematic with replicated transforms, otherwise kick upward force or remote sync can be canceled.

7. **[2026-07-06] Remote enemy simulation handoff must precede replicated state application**
   Do instead: disable remote AI idempotently before applying replicated `EnemyState`, planar speed, attack sequence, and damage sequence; calling `SetSimulationEnabled(false)` afterward resets the remote brain to Idle and stops its animation every frame.

8. **[2026-05-21] Damage knockback must be integrated with movement controllers, not only Rigidbody force**
   Do instead: route hit reactions through `PlayerMovement` and `EnemyMotor`, because their movement loops and speed clamps can cancel a raw impulse on the next tick.

9. **[2026-05-22] Combat movement penalties need explicit replicated state**
   Do instead: when an attack or hit temporarily changes locomotion speed, sync a compact boolean/sequence for that modifier so remote locomotion scaling and animation match the authority player instead of inferring only from base movement state.

10. **[2026-06-17] Player health replication must cover every local health mutation path**
   Do instead: route fall damage, PvP hits, and future owner-local damage through a helper that broadcasts `PlayerHealth` state after `ApplyDamage`, instead of relying only on death or enemy-only RPC paths.

## Camera & Ownership
1. **[2026-08-20] FPS hands/items default to a true URP overlay camera**
   Do instead: keep `FP_Camera` as Base without `FirstPersonView`, keep `Hands Camera` as Overlay rendering only `FirstPersonView` with `clearDepth=true`, and let `WallRenderFeature` skip itself in this mode to avoid duplicate first-person draws.

2. **[2026-08-07] Wall-only first-person bypass is now a fallback mode**
   Do instead: use `PlayerSetup.firstPersonOverlayDepthMode=WallLayerStencilBypass` only when hands should preserve scene depth except against objects on the `Wall` layer; otherwise keep `AlwaysOnTop`.

3. **[2026-08-20] Player low-light assist is owner-local runtime lighting**
   Do instead: keep `PlayerVisibilityAssistLight` on `Assets/Resources/Player.prefab`, let it create its own weak point light on the local `FP_Camera`, and suppress it from `HandEquipmentController.HasActiveEquippedLightSource()` rather than serializing/syncing a world light.

4. **[2026-08-07] Layer temporary attack FOV over live movement FOV**
   Do instead: let locomotion FOV update first and have attack feedback resolve the current locomotion FOV each frame as its base, otherwise sprint-to-attack can snap back to a stale sprint FOV when the attack relaxes.

5. **[2026-08-04] Player look pose must target FP camera input and TP model bones explicitly**
   Do instead: resolve `MouseLook` from the GameObject named `FP_Camera`, resolve procedural bones from the TP `Model` animator, and never let `FPS_Model` or inactive `TP_Camera` satisfy look-pose/camera marker searches.

6. **[2026-08-05] Attack aim and head look need complementary pitch handoff**
   Do instead: while `Upper Body Attack` pitch releases, feed normal head-look the remaining camera-limited pitch so spine/chest do not hold the attack pose and then correct late.

7. **[2026-07-30] First-person body separation must hide renderers, not rig roots**
   Do instead: use the simple `PlayerPerspectiveVisibility` Elements list on `Assets/Resources/Player.prefab` for all owner-only/remote-only mesh visibility; keep `PlayerSetup` out of mesh hiding and keep animated roots like `TP_Model`/`Knight` active so bones, sockets, and owner-only children such as `Separated_UpperBody` still work.

8. **[2026-07-03] First-person camera height is finalized by `HeadBobController`**
   Do instead: when crouch changes the capsule or model height but the view stays fixed, adjust the camera's base local position in `HeadBobController` rather than only touching `PlayerMovement` or collider scaling.

9. **[2026-07-07] First-person hit shake must layer into `HeadBobController`**
   Do instead: apply damage camera impact through `HeadBobController` after bob, landing, and crouch offsets are composed, instead of driving a competing camera transform from combat scripts.

10. **[2026-04-21] First-person camera effects must stay owner-local and resolve the active camera by marker**
   Do instead: apply sway, FOV, recoil, and similar offsets only on the owning client, and target the enabled `FP_Camera` child selected by setup code instead of assuming a single camera child.

## Rendering & Materials
1. **[2026-08-21] Runtime liquid state must be renderer-local**
   Do instead: drive per-instance liquid values such as `_WobbleX`, `_WobbleZ`, and `_FillAmount` through `MaterialPropertyBlock` on the target `Renderer`; never write them to `sharedMaterial` because every potion using the same material will inherit the motion/fill.

2. **[2026-08-21] Liquid fill sliders must use renderer-relative bounds**
   Do instead: in `SG_Liquid_Effect`, remap `_Fill` through `Object.World Bounds Min/Max - Object.Position` rather than fixed `Y` ranges; keep wobble scripts writing both `_WobbleX` and legacy `_WoobleX` while the graph carries the typo.

3. **[2026-08-20] WK toon fade should stay opaque/dithered**
   Do instead: drive WK toon object disappearance through `_FadeAlpha` plus Shader Graph dither/alpha clip, keep saved WK materials serialized with `_FadeAlpha: 1` and `_Cutoff: 0`, and keep `AlphaClipThreshold` at `0` so full fade alpha is fully opaque.

4. **[2026-08-20] `Invalid AABB` can be a Prefab Stage post-play renderer/cache issue**
   Do instead: run `Tools/Diagnostics/Scan Serialized Invalid Values`, keep `Tools/Diagnostics/Monitor Invalid Scene Values` enabled before reproducing with `Player.prefab` open, inspect any `PrefabStage(...)` path from the post-play/log-triggered scan, remove serialized `m_AABB` NaN overrides, and rebuild/remove corrupted `Cloth` components when `SkinnedMeshRenderer.localBounds` wakes as NaN.

5. **[2026-08-20] Auto texture tiling should stay renderer-local**
   Do instead: keep `_Texture_Tiling` changes in `MaterialPropertyBlock` on a specific renderer; editing the saved `.mat` vector changes every mesh using that shared material.

## User Directives
1. **[2026-05-26] Large gameplay systems should start from a saved checkpoint plus a staged implementation doc**
   Do instead: before sizable Unity feature work, create a project snapshot from saved-on-disk files, record the requested scope in `Docs/`, and align editor-side setup steps before writing runtime code.

2. **[2026-07-22] Reaction setup and bridge changes must update the central workflow doc**
   Do instead: whenever a reaction setup, emitter, or bridge is added or changed, update `Docs/ReactionEmitterWorkflow.md` in the same pass with the new flow, parameters, and tool behavior.
