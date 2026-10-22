# Napkin Runbook

## Curation Rules
- Re-prioritize on every read.
- Keep recurring, high-value notes only.
- Max 10 items per category.
- Each item includes date + "Do instead".

## Execution & Validation (Highest Priority)
1. **[2026-08-19] Shader Graph edits can reimport mid-write and require exact graph references**
   Do instead: for `.shadergraph`/`.shadersubgraph` rewrites, preserve a blank line between top-level JSON objects, validate multi-object JSON immediately, update `CategoryData.m_ChildObjectList` for new exposed properties, and verify every edge `m_Node.m_Id` points to a graph node object, not a slot object, before Unity imports it.

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
1. **[2026-09-01] Rotating door hinges need closed-pose pivot snapshots**
   Do instead: capture the rotate pivot in the moving part's parent space while closed and evaluate the hinge pose from that snapshot each frame, especially when `Pivot` is a child of the moving part.

2. **[2026-07-13] New gameplay scenes must be registered in both scene bootstraps and Build Settings**
   Do instead: when cloning or adding a gameplay map, update every hardcoded gameplay-scene check such as `RoomManager`/enemy bootstraps and add the scene to `ProjectSettings/EditorBuildSettings.asset`, otherwise direct Play and menu/room loading diverge.

3. **[2026-07-23] Menu-to-gameplay transitions need Photon room handoff plus a gameplay camera fallback**
   Do instead: create or join the Photon room from `RoomList` before loading gameplay, publish scene path/index in `CustomRoomPropertiesForLobby`, keep `AutomaticallySyncScene` off in Menu until the master handoff, mark the pending gameplay scene before hiding the lobby and calling `LoadScene`, keep scene-baked players alive until `RoomManager` has spawned the runtime Photon player, and let `RoomManager` create a temporary gameplay camera until the local network player camera is active so the Game view does not stay on the last Menu frame.

4. **[2026-07-23] Scene-fixed interactables should degrade from PhotonView sync to room-state sync**
   Do instead: keep doors and destructibles usable offline/local by default, use RPCs when a valid `PhotonView` exists, and fall back to scene-stable room properties plus master-authority requests when the object is a fixed scene actor without manual `PhotonView` setup.

5. **[2026-07-14] Stairs and walls should not ship with raw MeshCollider gameplay collision**
   Do instead: for movement-test maps, use `Tools/Level/Collision/Generate Stair Ramp Helpers` for straight ProBuilder stairs, `Generate Mesh Stair Ramp Helpers` for imported stair meshes, and helper box colliders for snaggy walls, then disable any original MeshCollider so the player capsule reads simplified collision.

6. **[2026-08-25] Walkable Rigidbody slopes need tangent-gravity compensation**
   Do instead: keep low-friction player capsules from sliding on slopes below `maxSlopeAngle` by canceling only `Physics.gravity` projected onto the walkable ground plane; leave steeper `IsSlidingSlope` surfaces uncompensated.

7. **[2026-07-15] Slope crest stability depends on surface-normal snap plus a short post-slope adhesion window**
   Do instead: keep `PlayerMovement` ground snap relative to the contacted surface normal instead of world `Y`, and preserve a brief downward adhesion probe after walkable slopes or step assists so sprinting off the last stair/ramp segment stays grounded without suppressing real jumps.

8. **[2026-05-08] Gameplay room bootstrap lives in code, not as a fixed scene object**
   Do instead: when connection or spawn fails in `FieldTestCharacter`, inspect `RoomManager.BootstrapGameplayRoomManager()` and scene-name/path checks before hunting for a missing manager in the hierarchy.

9. **[2026-05-26] World pickup items should be authored on prefab assets, not inferred by runtime name scans**
   Do instead: use `Tools/Inventory/Item Authoring Tool`, keep `WorldPickupItem`, item select/use cues, root solid collision, and the reaction bridge stack on the prefab root, keep `PickupTrigger` only for interaction/outline, and bind the correct `ItemDefinition` so scene instances inherit stable pickup behavior.

10. **[2026-05-21] Menu lobby refresh must handle stale Photon connections**
   Do instead: when re-entering the menu, if the client is still connected but outside a room, join the lobby directly or disconnect first; never wait on `!PhotonNetwork.IsConnected` unless this scene actually triggered a disconnect.

## Enemy Navigation & Procedural Animation
1. **[2026-10-21] Beholder investigation prioritizes sensed points, then room transitions**
   Do instead: keep `EnemyBrain.Use Last Seen Search Flow` enabled; let authority record each player's latest seen/heard position and confirmed `EnemySearchArea` transition. Room membership alone must not trigger detection. Chase visits sensed positions without restarting Alert, resolving off-NavMesh evidence to a reachable endpoint. Search sweeps the original room before following a physically linked A/B `EnemyTraversalVolume`; door passage must not create evidence. Area membership is the union of attached BoxColliders plus explicit child bounds; child bounds require explicit Points of Interest. Do not use the hidden player's live position as a destination; after Search speech plus delay, enter Reflection.

2. **[2026-10-20] Player movement awareness is an authority-side radius, not an AudioCue**
   Do instead: tune crouch/walk/sprint radii on `Assets/Resources/Player.prefab`'s `PlayerMovementNoiseSource`, measure short-range hearing from collider edges, let authoritative `EnemyHearing` sample replicated movement via `EnemyTargetRegistry`, and keep hearing-induced Alert/inspection separate from vision/contact-authorized Chase; never treat `NoiseSystem` as automatically fed by audible AudioCues.

3. **[2026-10-01] First-detection enemy alert belongs to the base state flow**
   Do instead: route first target awareness through `EnemyAIState.TryChaseVisibleTarget` and `EnemyAlertState`, let the Beholder trigger Alert only once until Reflection completes (rediscovery while disarmed goes straight to Chase), use `EnemyBrain.Alert Before Chase On First Target Detection`/`Rotate During Alert`, and keep per-prefab alert trigger/audio cue setup in `EnemyAnimationController`; `AudioEmitter` is optional because alert audio can fall back to `GameAudioService`.

4. **[2026-09-25] Large-enemy tight passages should be authored traversal volumes**
   Do instead: keep the Beholder's real collider, bake NavMesh for its radius, and author `EnemyTraversalVolume` side points linked to the correct door. Link each door side to a room-owned `EnemySearchArea`. Preserve the requested far-side destination even when NavMesh samples a near-side point; lost-sight Chase must approach the door and verify the body crossed before accepting arrival. Pause/resume navigation during traversal, choose the route from the actor's entry side, and judge canceled crossings by the actor's physical side. The Beholder has both a capsule and a root box: during traversal, narrow the capsule and temporarily disable the box or the box still catches door frames; restore both on exit/death. Ignore only the open door leaf's collision during the route, restore it afterward, and use route-progress timeout plus a point-specific warning for physical stalls. Keep door reorientation conditional on obstruction ahead and retain the contact sensor and interaction lock.

5. **[2026-09-25] Combat masks must follow gameplay layers**
   Do instead: whenever moving enemies/players to dedicated layers, update `PlayerMeleeAttack.hitMask` to include `Enemy`/`Player` and `EnemyAttack.hitMask` to include `Player`; otherwise overlaps stop finding combat colliders before damage code runs.

6. **[2026-09-25] Enemy separation belongs to layers plus NavMesh avoidance**
   Do instead: put enemy roots/colliders on the `Enemy` layer, disable `Enemy x Enemy` in the Physics matrix, and keep authored `NavMeshAgent` avoidance/radius values aligned so enemies avoid each other without Rigidbody pushing.

7. **[2026-09-25] Floating enemies need one vertical movement driver**
   Do instead: use either gravity-driven collision or the authority-only `EnemyGroundAnchor`, not both at once. For a gravity trial, disable the entire anchor component: it applies a zero-friction capsule material even when `enableGroundAnchor` is false. Keep hover on the visual child if gravity proves reliable; evaluate ramp behavior before settling on a permanent mode.

8. **[2026-09-25] Large enemies need authored NavMesh radius parity**
   Do instead: keep a real `NavMeshAgent` component saved on enemy prefabs, tune its radius/height/baseOffset in Prefab or Scene Mode, leave `EnemyNavigation.Configure Agent From Capsule` off unless intentionally regenerating from the collider, and rebake with the larger radius or a dedicated agent before changing motor code.

9. **[2026-10-22] Beholder Hip and eye tracking are animation overlays**
   Do instead: apply bounded Hip yaw/pitch before eye tracking in `BeholderProceduralAnimationController.LateUpdate`, use the replicated AI target on remote clients, and restore the animated Hip pose as tracking releases; when eyes are not tracking, do not write eye-bone rotations.

10. **[2026-10-01] Beholder eye color drives linked eye VFX**
   Do instead: tune eye material profiles, linked `WKFakeVolumeController` volumes, and eye `Spot Light` references from `BeholderEyeGlowController`; let that controller use the current animated emission color/intensity as the shared source.

## Movement & Actions
1. **[2026-04-07] Player probes and state decisions must stay aligned with the real capsule**
   Do instead: derive casts from the `CapsuleCollider`, refresh grounding before controller state decisions that can set `MovementState.air`, and keep the player capsule on a low-friction physics material.

2. **[2026-07-29] External conveyor pushes must survive player speed control**
   Do instead: accumulate conveyor velocity in `PlayerMovement.VolumeModifiers` and apply it in `FixedUpdate` after `SpeedControl`, so the player motor does not clamp or cancel the belt push.

3. **[2026-09-01] Player ragdoll ground collision depends on usable bone colliders**
   Do instead: keep bone Rigidbodies kinematic/gravityless and ragdoll colliders disabled in `Awake`; when `PlayerRagdollController` disables the root Player colliders, make simulated bone colliders solid and large enough in world scale, especially legs/feet, and on recovery force Animator `Rebind()`/`Update(0)` plus `MovementAnimationController.ResetAfterRagdoll()`.

4. **[2026-09-02] Impact ragdoll recovery is owner-timed after grounded contact**
   Do instead: route impact requests through `PlayerHealth.RequestImpactRagdoll`, let the owning player broadcast activation/recovery, and start the recovery timer only while `PlayerRagdollController.HasRagdollGroundContact()` is true.

5. **[2026-09-02] Collision impact checks should use `Collision.relativeVelocity`**
   Do instead: decide wall/object impact from the collision event's relative velocity and contact normals, because root Rigidbody velocity can already be reduced by the physics solver when `OnCollisionEnter` runs.

6. **[2026-08-31] Knight feet are skinned to leg/end bones, not named foot bones**
   Do instead: debug foot deformation from `Legs`/`Lower Body` renderers and `Leg_Lower.*(_end)` bones; do not simulate `_end`/IK/Target bones as independent ragdoll bodies, use foot colliders under the lower-leg Rigidbody instead, then tune collider world size plus joint projection/preprocessing.

7. **[2026-08-12] Crouch kick must stand before firing**
   Do instead: when kick starts from `MovementState.crouching`, validate kick availability, stand/queue the kick briefly, suppress crouch while queued or active, then let held crouch re-enter after `kickActionDuration`.

## UI & Emotes
1. **[2026-10-14] In-game settings UI is gameplay-root bootstrapped**
   Do instead: keep the reusable Settings overlay at `Assets/Resources/UI/InGameSettingsMenu.prefab`, let `GameplaySceneRoot` instantiate it through `InGameSettingsMenuController`, and guard local gameplay input with `InGameSettingsMenuController.IsAnyMenuOpen` instead of pausing multiplayer time.

2. **[2026-07-30] Emote wheel slots are authored from the player emote controller**
   Do instead: edit the five `emoteWheelSlots` on `Assets/Resources/Player.prefab`'s `PlayerEmoteWheelController` for emote type, label, and sprite, and use `Assets/Prefabs/UI/EmoteWheel.prefab` only when a scene needs an explicit saved Canvas object.

## Audio & Feedback
1. **[2026-10-20] Networked enemy dialogue should use AudioCue configuration with authoritative choices**
   Do instead: keep a dedicated voice source for one-at-a-time speech, configure it through `AudioCue.ConfigureAudioSource`, and replicate cue/clip indices plus chosen volume, pitch, and start time through the enemy state stream.

2. **[2026-09-28] Reverb authoring should stay native-like**
   Do instead: use a single collider-shaped `BoxReverbZone` prefab for normal room/corridor authoring, choose an `AudioReverbPreset` plus transition time on the component, let the hidden runtime manager drive the listener-following technical zone, and control source participation through `AudioCue.AffectedByEnvironmentReverb`/`EnvironmentReverbSend`.

3. **[2026-09-14] Footsteps are layered by surface plus character**
   Do instead: keep cadence/events in player or enemy controllers, put shared probe/playback in `FootstepPlaybackController`, resolve terrain through `FootstepSurfaceResolver`, and add per-character boot/armor/bone layers through `FootstepCharacterAudioProfile`.

4. **[2026-09-28] Ambience is scene-local and profile driven**
   Do instead: use `AmbientAudioController` with a default `AmbientAudioProfile`, switch/mix through local-player-only `AmbientZone` triggers, and use `AmbientRandomEmitter` for localized one-shots instead of networking ambience.

5. **[2026-08-17] Melee VFX context must survive each reaction bridge**
   Do instead: when adding per-attack VFX parameters, pass `ReactionSignalContext` through both `ImpactReactionSignalBridge` and destructible damage flows; for impact feedback, keep `ImpactReactionSignalBridge` on the same GameObject as `PhotonView` so its lightweight RPC can replay the cosmetic signal on other clients.

6. **[2026-09-10] Torch flame loops belong to flame state, not reactions**
   Do instead: configure continuous fire audio through `TorchFlameController.litLoopAudioCue`; keep `Lit` reaction audio for one-shot ignition like `Torch_Woosh`, and let `SetLit`/`SetPresentationVisible` start or stop the loop for wall and hand torches.

7. **[2026-07-02] Cross-client audio should reuse replicated gameplay state before adding dedicated Photon events**
   Do instead: drive jump, land, attack, pickup, emote, torch-lit loops, and similar world SFX from existing replicated sequence counters or shared item/state application paths first, and only add explicit audio RPC/event traffic for sounds that have no stable gameplay signal to hook into.

8. **[2026-07-13] Vivox native positional can be replaced by simulated proximity**
   Do instead: keep `Use Native Vivox Positional Audio` off when native `JoinPositionalChannelAsync` causes `5100/1001`; join a 2D `-sim3d` Vivox channel, map `VivoxParticipant.PlayerId` through Photon `kwkVivoxPlayerId`, and drive per-participant local volume/mute from Photon player distance.

9. **[2026-07-13] Standalone Vivox profiles are intentionally ephemeral by default**
   Do instead: when testing builds, expect profiles like `kwk-player-xxxxxxxx`; only pass `--ugs-profile=<name>` for deliberate persistent identity, and never reuse the same manual profile in two simultaneous clients.

10. **[2026-07-08] Vivox channel drops must suspend 3D publishing and reconnect with backoff**
   Do instead: treat `ChannelJoined` as the safe point for `Set3DPosition`, publish around `0.3s`, and on `ChannelLeft` or connection recovery stop 3D updates immediately and retry with backoff so server disconnect `5100` does not cascade into stale-session `1001` loops.

## Inventory & Equipment
1. **[2026-08-17] Sword trail no longer uses the legacy attack-trail stack**
   Do instead: keep `Assets/Prefabs/Items/Sword.prefab` free of `BladeTrailSocket`, embedded `VFX_Sword_Trail`, and `WeaponAttackTrail`; let the newer sword trail system own playback.

2. **[2026-08-11] Item grips are perspective-specific per hand**
   Do instead: keep one `WorldPickupItem` as the gameplay/network source, author TP grips into `GripPoints_TP_Right`/`GripPoints_TP_Left`, author FPS grips into `GripPoints_FPS_Right`/`GripPoints_FPS_Left`, expose both perspectives in the inspector/tooling, keep legacy `GripPoint*` names as migration fallback only, and let runtime FPS fall back to TP only when no FPS grip has been saved yet.

3. **[2026-08-11] World pickup item network physics is intentionally disabled for retest**
   Do instead: keep item prefab roots with a local dynamic Rigidbody (`useGravity=true`, `isKinematic=false`, `detectCollisions=true`) plus an enabled solid root collider, do not reintroduce MasterClient pickup-physics snapshots or room-synced rest poses without a new design, and let equip/FPS clone paths suppress physics only while held or owner-visual.

4. **[2026-08-04] FPS item runtime sockets must match authoring sockets**
   Do instead: equip FPS item visuals into `RightHandSocket`/`LeftHandSocket` children under `FPS_Model`, not directly into raw `Hand.R`/`Hand.L` bones, so Scene authoring and Play mode use the same anchor.

5. **[2026-06-25] Hand sockets under rig bones must keep non-zero local scale**
   Do instead: when moving `RightHandSocket` or `LeftHandSocket` onto rig bones, verify the socket `Transform.localScale` stays near `(1,1,1)`; zero-scale sockets make equipped visuals instantiate invisibly.

6. **[2026-07-06] Multiplayer pickups need scene-instance ids, not prefab ids**
   Do instead: keep item prefab `networkSceneId` fields blank, let each scene instance resolve a deterministic id, rebuild `WorldPickupItem` lookup from live inactive-inclusive objects on a registry miss, and apply owner-local drops immediately while sending RPCs only to other clients.

7. **[2026-07-01] Hand grip authoring should happen in Scene Mode and keep explicit perspective refs**
   Do instead: place a prefab instance of the item in the scene, select it or open `Tools/Inventory/Item Grip Authoring Tool` from its Inspector, assign a scene `Target` that contains sockets, choose a compatible FPS/TP `Preview Socket`, adjust in Scene view, then save to the matching `GripPoints_TP_*` or `GripPoints_FPS_*` transform required by the item `HandRequirement`.

8. **[2026-10-19] Loot pivots must follow the movable compartment**
   Do instead: put `LootSpawner` on `Pivot_Asset`, leave `Spawn Point` empty to use that pivot, parent it under `DoorController.movingPart` for sliding drawers, and gate it with the matching door; use the standalone pivot prefab for floor or table loot.

9. **[2026-05-27] Dropped pickups must preserve their world source per slot**
   Do instead: when equipping a `WorldPickupItem`, store the original pickup reference alongside the slot data, then make `DropToWorld` reactivate inactive stored pickups before repositioning them instead of relying only on `ItemDefinition` or the equipped visual clone.

10. **[2026-08-05] Physical item drops must restore pickup transform before physics**
   Do instead: cache the pickup's pre-equip world scale/parent, keep all item colliders disabled while equipped, suppress drop Rigidbody/collision before reparenting, restore scale on `DropToWorld`, then enable the root solid collider and launch physics.

## Animation Coupling
1. **[2026-08-05] First-person arms sync is owner-local and separate from TP**
   Do instead: drive `Assets/Models/Knight_FPS/Animation/KnightFPS.controller` from `PlayerMovement` action sequences on the imported `KnightFPS` Animator under `FPS_Model`; keep `FPS Base` as the locomotion authority with `Idle`/`Run Forward`, mirror `Run Forward` inside `FPS Right Arm` and `FPS Left Arm` when literal grip-to-run transitions are needed, put right-hand grip/attack/draw/pickup/emotes on `FPS Right Arm` with `Right Arm Mask`, put left-hand/torch/draw/pickup on `FPS Left Arm` with `Left Arm Mask`, configure transitions with explicit TP-style triggers/bools such as `Attack`, `AttackComboStep`, `PickUpItemRight`, `IsRightHandOccupied`, `IsLeftHandOccupied`, `IsLeftTorchEquipped`, and `IsMoving`, avoid reintroducing `FPSState`, force that Animator to `AlwaysAnimate`, keep TP controller transitions untouched, and hide FPS renderers through owner-only perspective rules.

2. **[2026-05-20] Enemy attack clips only fire when the controller trigger matches the replicated attack sequence**
   Do instead: when adding an enemy attack state, keep a trigger parameter named `Attack` on the saved controller asset and gate the transition into the attack clip from that trigger, not from unconditional exit-time links.

3. **[2026-07-01] Override action layers can latch the last pose when they return to `Empty`**
   Do instead: when an emote, grip, or other override layer exits to an `Empty` state with no motion, either zero that layer's runtime weight or crossfade into an explicit reset pose so lower layers can retake control immediately.

4. **[2026-05-22] Enemy hit reactions must bypass locomotion fallback**
   Do instead: when adding enemy damage states, drive them from a replicated damage sequence and suppress Idle/Walk fallback while the hit state is active, otherwise the locomotion crossfade can cancel the reaction immediately.

5. **[2026-10-22] FBX reimports must preserve GUID and single Animator ownership**
   Do instead: replace an FBX in place with its `.meta` and remove any old duplicate-GUID copy. When changing Avatar Definition, check whether Unity adds an imported Animator alongside prefab-added Animators; leave only the gameplay-referenced Animator active, clear conflicting scene overrides, and verify the clip preview.

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

4. **[2026-09-24] Non-melee enemies still need the attack contract**
   Do instead: keep `EnemyAttack` on special enemies for brain/network/animation references, but set `Attack Enabled` off until their dedicated ability component owns damage.

5. **[2026-08-05] Player melee attack tuning lives per combo step**
   Do instead: tune `PlayerMeleeAttack.attackConfigs` on `Assets/Resources/Player.prefab`; each combo step chooses `Ball`, `Arc`, or `Box` hit shape and owns timing, swing audio/delay, impact VFX angle, and camera feedback instead of changing one global attack value.

6. **[2026-05-21] Enemy death cleanup must be scheduled on every client, not only on the authority object**
   Do instead: when an enemy can die under Photon, schedule destruction through an `RPC` to all clients and also destroy on replicated death application, otherwise remote clients can keep a dead corpse in scene.

7. **[2026-07-27] Enemy Rigidbody physics is authority-only**
   Do instead: keep authoritative living enemies dynamic with gravity and keep remote/non-living enemies kinematic with replicated transforms, otherwise kick upward force or remote sync can be canceled.

8. **[2026-07-06] Remote enemy simulation handoff must precede replicated state application**
   Do instead: disable remote AI idempotently before applying replicated `EnemyState`, planar speed, attack sequence, and damage sequence; calling `SetSimulationEnabled(false)` afterward resets the remote brain to Idle and stops its animation every frame.

9. **[2026-05-21] Damage knockback must be integrated with movement controllers, not only Rigidbody force**
   Do instead: route hit reactions through `PlayerMovement` and `EnemyMotor`, because their movement loops and speed clamps can cancel a raw impulse on the next tick.

10. **[2026-05-22] Combat movement penalties need explicit replicated state**
   Do instead: when an attack or hit temporarily changes locomotion speed, sync a compact boolean/sequence for that modifier so remote locomotion scaling and animation match the authority player instead of inferring only from base movement state.

## Camera & Ownership
1. **[2026-09-25] New gameplay render layers must be in player camera masks**
   Do instead: when adding layers like `Door` or moving the character body to `Player`, keep `Assets/Resources/Player.prefab` `FP_Camera.cullingMask` rendering that world layer; the hands camera should stay limited to `FirstPersonView`.

2. **[2026-08-31] Local ragdoll view must leave first-person hierarchy**
   Do instead: while owner ragdoll is active, hide FPS-only renderers, switch to a detached orbital `TP_Camera` that follows the ragdoll body, disable camera `MouseLook` during physics, then restore `FP_Camera`/`Hands Camera` and `PlayerPerspectiveVisibility` on recovery.

3. **[2026-08-31] Close skinned character parts can vanish from renderer bounds**
   Do instead: for characters near the camera, attach `SkinnedMeshCullingStabilizer` or set `SkinnedMeshRenderer.updateWhenOffscreen`, disable dynamic occlusion, and enlarge local bounds; only use FP overlay-layer fixes for owner-only first-person meshes.

4. **[2026-08-20] FPS hands/items default to a true URP overlay camera**
   Do instead: keep `FP_Camera` as Base without `FirstPersonView`, keep `Hands Camera` as Overlay rendering only `FirstPersonView` with `clearDepth=true`, and let `WallRenderFeature` skip itself in this mode to avoid duplicate first-person draws.

5. **[2026-08-07] Wall-only first-person bypass is now a fallback mode**
   Do instead: use `PlayerSetup.firstPersonOverlayDepthMode=WallLayerStencilBypass` only when hands should preserve scene depth except against objects on the `Wall` layer; otherwise keep `AlwaysOnTop`.

6. **[2026-08-20] Player low-light assist should stay owner-local and compact**
   Do instead: keep `PlayerVisibilityAssistLight` owner-local, tune its short-range `Point`/`Spot` mode on `FP_Camera`, use WK toon shader fill (`_WK_ToonFillIntensity`/`_WK_ToonFillColor`) only when a global darkness floor is desired, and suppress the assist light from `HandEquipmentController.HasActiveEquippedLightSource()`.

7. **[2026-08-07] Layer temporary attack FOV over live movement FOV**
   Do instead: let locomotion FOV update first and have attack feedback resolve the current locomotion FOV each frame as its base, otherwise sprint-to-attack can snap back to a stale sprint FOV when the attack relaxes.

8. **[2026-08-04] Player look pose must target FP camera input and TP model bones explicitly**
   Do instead: resolve `MouseLook` from the GameObject named `FP_Camera`, resolve procedural bones from the TP `Model` animator, and never let `FPS_Model` or inactive `TP_Camera` satisfy look-pose/camera marker searches.

9. **[2026-08-05] Attack aim and head look need complementary pitch handoff**
   Do instead: while `Upper Body Attack` pitch releases, feed normal head-look the remaining camera-limited pitch so spine/chest do not hold the attack pose and then correct late.

10. **[2026-09-01] Perspective visibility must account for the global `FirstPersonView` overlay**
   Do instead: use the `PlayerPerspectiveVisibility` Elements list on `Assets/Resources/Player.prefab`, resolve final renderer visibility by target specificity, and force-hide owner-only roots/layer renderers such as `Separated_UpperBody` on remote player instances so the local Hands Camera cannot draw them.

## Post Processing Feedback
1. **[2026-10-14] Runtime URP Volumes can override inactive saved camera components**
   Do instead: keep the base `FP_Camera Profile.asset` untouched, verify the runtime Volume is on a layer in `FP_Camera`'s Volume Layer Mask and post-processing is enabled, then inspect the runtime Volume weight and active overrides in Play Mode; calibrate light thresholds against actual torch intensities (about 0.4 here).

2. **[2026-10-13] Player camera post effects should go through one local feedback controller**
   Do instead: raise local gameplay signals such as `PlayerHealth.Damaged`, then let `PlayerPostProcessingFeedbackController` on `Assets/Resources/Player.prefab` `FP_Camera` drive the child `PostProcessingFeedbackVolume` weight against the base Volume baseline; keep feedback owner-local so remote player volumes stay at zero.

3. **[2026-10-13] URP Volume inspector errors can come from broken profile subassets**
   Do instead: when `SerializedObjectNotCreatableException` appears from URP `VolumeComponentEditor`/effect editors, scan active `VolumeProfile` assets for dangling `m_Script: {fileID: 0}` subassets or null components and remove only the broken, unreferenced entries.

## Rendering & Materials
1. **[2026-09-28] WK toon emission must feed the custom final color path**
   Do instead: for `SG_WK_Base_Toon Shader`, multiply HDR `_EmissionColor` by `_EmissionIntensity` and add it before `SurfaceDescription.BaseColor`; do not rely on a separate `SurfaceDescription.Emission` block because the custom Unlit/toon path may ignore it.

2. **[2026-09-30] Fake volume fog should be authored through the WK library**
   Do instead: use `WKFakeVolumeController` settings and generated visuals under `Assets/VFX/FakeVolume`; for new authoring, add the controller to an empty object or use `GameObject/VFX/WK Fake Volume`, size volumes with `Transform` scale, then let renderer-local `MaterialPropertyBlock` values drive tuning.

3. **[2026-10-02] Interactive tutorial fog is a local compute-driven volume**
   Do instead: create/configure it through `GameObject/VFX/Interactive Fog Volume`, keep `FogSimulation` references wired to `Assets/Fog/FogSim.compute` and `Assets/Fog/FogVolume.mat`, size it with `Transform` scale, and let it resolve the local spawned player at runtime.

4. **[2026-09-08] WK toon shadows come from custom Unlit lighting**
   Do instead: when WK toon shadows fail, verify `SG_WK_Base_Toon Shader` still has `m_CastShadows`, `m_ReceiveShadows`, and `m_KeepLightingVariants` enabled; for point/spot shadows, make `Assets/Shaders/Shaders/WK/Includes/Lighting.hlsl` call `GetAdditionalLight(i, positionWS, shadowMask)` so URP fills `light.shadowAttenuation`.

5. **[2026-09-10] Torch shadow flicker usually starts at shadowed wall lights**
   Do instead: debug torch flicker by disabling shadows on `Wall_Torch` first; keep hand torch shadows off, avoid animating range/position on any shadow-casting torch, and tune Additional Light shadow tier/atlas before changing toon shader code.

6. **[2026-08-21] Runtime liquid state must be renderer-local**
   Do instead: drive per-instance liquid values such as `_WobbleX`, `_WobbleZ`, and `_FillAmount` through `MaterialPropertyBlock` on the target `Renderer`; never write them to `sharedMaterial` because every potion using the same material will inherit the motion/fill.

7. **[2026-08-21] Liquid fill sliders must use renderer-relative bounds**
   Do instead: in `SG_Liquid_Effect`, remap `_Fill` through `Object.World Bounds Min/Max - Object.Position` rather than fixed `Y` ranges; keep wobble scripts writing both `_WobbleX` and legacy `_WoobleX` while the graph carries the typo.

8. **[2026-08-20] WK toon fade should stay opaque/dithered**
   Do instead: drive WK toon object disappearance through `_FadeAlpha` plus Shader Graph dither/alpha clip, keep saved WK materials serialized with `_FadeAlpha: 1` and `_Cutoff: 0`, and keep `AlphaClipThreshold` at `0` so full fade alpha is fully opaque.

9. **[2026-08-20] `Invalid AABB` can be a Prefab Stage post-play renderer/cache issue**
   Do instead: run `Tools/Diagnostics/Scan Serialized Invalid Values`, keep `Tools/Diagnostics/Monitor Invalid Scene Values` enabled before reproducing with `Player.prefab` open, inspect any `PrefabStage(...)` path from the post-play/log-triggered scan, remove serialized `m_AABB` NaN overrides, and rebuild/remove corrupted `Cloth` components when `SkinnedMeshRenderer.localBounds` wakes as NaN.

10. **[2026-08-20] Auto texture tiling should stay renderer-local**
   Do instead: keep `_Texture_Tiling`, `_Texture_Offset`, and `_Texture_Rotation` changes in `MaterialPropertyBlock` on a specific renderer, and keep both WK toon Shader Graphs wired through the same Tiling And Offset/Rotate UV path; editing the saved `.mat` vector changes every mesh using that shared material.

## User Directives
1. **[2026-05-26] Large gameplay systems should start from a saved checkpoint plus a staged implementation doc**
   Do instead: before sizable Unity feature work, create a project snapshot from saved-on-disk files, record the requested scope in `Docs/`, and align editor-side setup steps before writing runtime code.

2. **[2026-07-22] Reaction setup and bridge changes must update the central workflow doc**
   Do instead: whenever a reaction setup, emitter, or bridge is added or changed, update `Docs/ReactionEmitterWorkflow.md` in the same pass with the new flow, parameters, and tool behavior.
