# Skeleton Archer and Swordsman

## Assets

- Shared FBX: `Assets/WorstKnights/Models/Enemies/New Skeleton/SkeletonTesteIA.fbx`.
- Extracted clips: `New Skeleton/Animations/SkeletonTesteIA.<Action>.anim`.
- Controllers: `SkeletonTesteIAAnimController` and `SkeletonArcherAnimController`.
- Equipment sources, reusable visual prefabs and materials: `New Skeleton/Equipment`
  and `New Skeleton/Materials`.
- Gameplay prefabs: `Assets/Prefabs/Enemies/New Skeleton/`:
  `Skeleton Base`, `Skeleton Swordsman`, `Skeleton Archer`, and `Skeleton Arrow`.

The two enemies are Prefab Variants of Skeleton Base. The base copies the existing
`Assets/Prefabs/Enemies/Enemy.prefab` configuration and substitutes the new visual
model. The original Enemy prefab, Skeleton FBX, controller and clips are retained.
The swordsman retains the original melee damage, timing, spawn/death flow,
perception, navigation, reaction and audio components.

## Import and authoring

The body uses a Generic rig, matching the existing Skeleton. Bone names and the
34-bone source rest hierarchy are preserved. Equipment uses separate FBX assets.
Body material `Bone` maps to the existing `WK_Skeleton_Bones` material. Equipment
materials use the project's existing WK shader with source Blender colors.

The gameplay root/collider uses the `Enemy` layer. Body and equipment renderers
use `Default`, matching the original Skeleton. The Player's world camera excludes
the `Enemy` layer, so assigning that layer to the visual meshes hides them in Game.
`Tools > Enemies > Skeleton > Repair Visual Layers` repairs existing generated
prefabs and their loaded scene instances without changing their placement.

Use `Tools > Enemies > Skeleton > Build New Skeleton Variants` after updating the
exports. The builder preserves generated asset GUIDs, extracts the eight rig
clips, copies the original controller state organization, rebuilds the shared
base and variants, and does not edit the active scene. Rebuilding regenerates
the generated prefab contents; author future gameplay overrides in another
variant if they must survive a rebuild.

The export process uses an isolated snapshot of the Blender working file. The
export metadata records source bind landmarks, equipment transforms, bow draw
weights and arrow poses. Import calibration accounts for FBX coordinate changes;
equipment is fitted to hand/back sockets using the imported reference pose.

The bow shot clip includes the bow blend-shape curve and a presentation arrow
rebased to `Hand.R/RightHandSocket`. Those curves follow the actor's position,
rotation and upper-body aiming. The physical arrow is an independent prefab,
with its forward axis along local +Z and its origin at the nock.

## Archer behavior

The optional `EnemyArcherCombat` component owns ranged attacks. `EnemyBrain`
uses it when present and enabled; otherwise the original melee/Beholder paths
remain in use. The Archer variant keeps `EnemyAttack` for shared references but
disables its melee damage, avoiding damage during bow preparation.

- Vision range: 20 m. Maximum shooting range: 16 m.
- Below 4 m, seek a reachable retreat point until approximately 6.5 m away.
- Retreat sampling is bounded and requires an actual usable NavMesh path.
  If no retreat route exists, the archer can still attempt a defensive shot.
- A shot locks movement through preparation and recovery. Turn toward the
  committed visible target until release; the upper spine provides limited
  vertical aiming. Hidden targets do not update the aiming point.
- Missing visibility, invalid targets or unreachable ballistic targets prevent
  release. Existing pursuit, last-seen search, spawn, death and stun flows are
  shared with the original enemy.
- Damage can interrupt the early 18% of preparation. Stun, traversal, death
  and component disable cancel preparation without a delayed release.

Timing at 24 fps: pickup frame 24, draw frames 46-52, release frame 57, recovery
ends at frame 76. The clip lasts approximately 3.167 seconds, followed by a
1.1-second recovery cooldown. Defaults are editable on `EnemyArcherCombat`.

## Projectile and damage

Default arrow speed is 22 m/s, gravity is 9.81 m/s2, and damage is 12. The lower
ballistic arc aims at the target capsule center. Swept sphere collision and a
spawn overlap check prevent thin-wall tunneling and blocked-muzzle escapes.
The shooter's colliders are excluded. A projectile consumes itself before
damage callbacks, so one arrow cannot apply repeated damage.

Player hits use `PlayerHealth.ReceiveEnemyDamage`. Active shield hitboxes use
the existing `ShieldGuardHitbox` and shield-block feedback. Solid geometry and
other characters stop an arrow. Friendly enemy damage is not enabled. Arrows
penetrate an unblocked player hit by 0.6 m and follow the player's position and
rotation. Replicated impacts carry the player's PhotonView ID and the embedded
local pose. Impacted arrows last 6 seconds, fading during the final second through
the same `SpawnedDestructibleFadeOut` component used by skeleton death. Penetration,
total impact lifetime and fade duration are editable on `EnemyArrowProjectile`.
No ammunition limit is imposed.

## Attack audio

The Swordsman uses `EnemyAnimationController` > `Melee Attack Audio`. Assign an
`Attack Audio Cue` in the Inspector. `Attack Audio Delay` defaults to 0.5 seconds
on the Swordsman, with `Attack Audio Anchor` attached to the sword. Delayed playback
is canceled when the attack is interrupted, the enemy dies or the component is
disabled. Remote clients use the existing replicated melee attack sequence.

The Archer has four independent AudioCue slots, initially empty.

- `EnemyArcherCombat` > `Attack Audio` > `Attack Start Audio Cue` starts with the
  attack animation and stops on release or interruption. Remote animation snapshots
  resume the clip at the elapsed attack time.
- `EnemyArcherCombat` > `Attack Audio` > `Shot Audio Cue` plays when a projectile
  is actually created, including creation from the replicated launch RPC. An
  optional `Shot Audio Anchor` follows the bow; when empty, release audio starts
  at the launch position.
- `Skeleton Arrow` > `EnemyArrowProjectile` > `Arrow Audio` > `Flight Audio Cue`
  loops for the duration of flight and follows the arrow. It stops on impact,
  disable or destruction.
- `Skeleton Arrow` > `EnemyArrowProjectile` > `Arrow Audio` > `Impact Audio Cue`
  plays once at impact, including shield blocks and world geometry. It can finish
  after the projectile is destroyed.

Volume, pitch, distance and environment reverb are configured through the assigned
AudioCue assets. Flight playback forces looping. Start and impact playback use
one-shot sources. The arrow launch and impact messages also drive projectile
audio on remote clients.

## Photon

`EnemyNetworkSync` snapshots include the archer's sequence, server start time,
active state and last visible aim point. Remote animation starts at the elapsed
shot time instead of replaying windup from zero. Reliable RPCs replicate each
projectile launch and impact. Visual replicas do not apply player damage.

The existing enemy PhotonView owns the RPCs. Arrow prefabs do not require a
PhotonView or a Photon Resources entry: each client creates its own visual
projectile from the launch message. Damage authority follows the owner enemy,
including room-view master changes. Network peers must use the updated build
because the shared enemy snapshot now contains the optional archer block.

## Scene use

Place either gameplay prefab in a scene with a baked NavMesh, or assign it to
the appropriate existing spawner. No enemies in Map3 are replaced automatically.
The existing scene remains available for reviewing the original enemy.

The `SceneAnimationPosePreview` component is configured like the original
Skeleton. The Archer's editor preview includes the presentation arrow; it scales
away immediately after release. At runtime the combat component controls its
visibility and creates the physical projectile.

## Validation

Build and runtime results are recorded under `.codex/diagnostics/skeleton-*`.
The authoring previews include both variants and the archer pickup/draw poses.
The exported/imported bind landmarks match within approximately 0.006 mm.

An exploratory run of legacy traversal tests passed 13 of 17 checks. Four
existing checks need a separate review: `DoorProbe_IgnoresTheLeafBehindTheEnemyButFindsItAhead`,
`ChaseWithoutSight_ApproachesLastKnownPositionBeforeSearching`,
`LostTarget_RemainsInChaseUntilTheLastKnownPositionIsReached`, and
`Search_DoesNotStartRoomSweepBeforeReachingTheObservedPosition`. Their details
remain in `skeleton-shared-enemy-contracts.txt`. `EnemyTraversalController`,
`EnemyTraversalVolume` and `EnemyNavigation` are unchanged by this feature;
these findings were not repaired as part of archer development.

The focused suite passed all 17 cases, including the moving/rotated actor,
accessory timing, thin-wall sweep, release delay, single-hit player damage,
shield blocking, early cancellation, NavMesh retreat, retreat during cooldown,
and non-damaging visual replicas. The existing perception/combat fixture also
passed all 26 checks in runtime context. Runtime compilation completed with
zero warnings and zero errors.

Manual review remains appropriate for combat tuning, tight passages, targets on
different floor heights, and a real multi-client Photon session. Offline authority
and visual-only projectile behavior are covered by the focused validation.
