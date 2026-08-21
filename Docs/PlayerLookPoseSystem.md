# Player Look Pose System

## Scope
- Add a first-person head-look system that keeps the camera aim independent from the body while idle.
- Limit the effect to safe movement states so sprinting and airborne clips stay untouched.
- Replicate the look pose so remote players see head and spine follow.

## Current Runtime Flow
- `MouseLook` now owns only the input angles: `viewYaw` and `viewPitch`.
- `PlayerMovement.LateUpdate()` is the runtime driver for body yaw, `Orientation`, camera local yaw, and bone pose.
- `HeadBobController` runs later so bob is applied on top of the already-correct camera aim.
- With both TP and FPS models in `Assets/Resources/Player.prefab`, `PlayerMovement` must resolve look-pose targets explicitly:
  - local look input comes from the `MouseLook` on the GameObject named `FP_Camera`
  - procedural bones come from the TP `Model` animator, not from `FPS_Model`
  - `FPS_Model` is never used as the source for remote head/spine look pose

## State Rules
- Active by default in `idle`.
- Active in `crouching`.
- Disabled in `walking` unless `allowHeadLookWhileWalking` is enabled.
- Disabled in `sprinting`.
- Disabled in `air`.
- Disabled while the `Upper Body Attack` or `Thumbs Up` layers are in a non-`Empty` state when their safety toggles are enabled.
- During `Upper Body Attack`, an optional Attack Aim Pose can still add a limited procedural pitch over `Spine`, `Chest`, and a small amount of `Head` so attacks follow the camera's vertical aim without re-enabling the full head-look pose.

## Horizontal Rule
- Camera yaw always follows `MouseLook.ViewYaw`.
- Body yaw is allowed to lag only until `horizontalHeadLookLimit`.
- Once that limit is exceeded, the root rotates by the overflow so the visual head stays within range.
- `Orientation` gets the remaining yaw offset so movement stays camera-relative.

## Vertical Rule
- Pitch is consumed in order:
  - head first
  - chest second
  - spine last
- Each bone has its own limit in the inspector.
- The default axis assumption is:
  - positive look pitch = look up
  - rig pitch pose is inverted on local X, so `invertPosePitch` defaults to enabled

## Attack Aim Pose
- `disableHeadLookDuringAttackLayer` should stay enabled for the normal head-look pose.
- `enableAttackAimPose` applies a separate procedural pitch while `Upper Body Attack` is active.
- Attack aim consumes pitch in order:
  - spine first
  - chest second
  - head last
- Keep head contribution low so the attack animation remains readable while the torso and arms inherit the vertical aim.
- When the attack layer returns to `Empty`, the attack aim pitch blends out using `attackAimReleaseSmoothTime` while the normal head-look pose receives the complementary pitch. This keeps the combined spine/chest pose near the camera-limited target instead of letting the attack pose hold and then correcting late.

## Inspector Tuning
- Component: `PlayerMovement`
- Most important fields:
  - `horizontalHeadLookLimit`
  - `headPitchLimit`
  - `chestPitchLimit`
  - `spinePitchLimit`
  - `spinePitchAxis`
  - `chestPitchAxis`
  - `headPitchAxis`
  - `headYawAxis`
  - `allowHeadLookWhileWalking`
  - `disableHeadLookDuringAttackLayer`
  - `enableAttackAimPose`
  - `attackAimPoseWeight`
  - `attackAimPitchLimit`
  - `attackAimSpinePitchLimit`
  - `attackAimChestPitchLimit`
  - `attackAimHeadPitchLimit`
  - `attackAimAcquireSmoothTime`
  - `attackAimReleaseSmoothTime`
  - `disableHeadLookDuringThumbsUpLayer`
  - `invertPosePitch`
  - `invertPoseYaw`
- Bone path fallbacks are exposed so the setup can be retargeted if the model hierarchy changes.
- If `Spine` or `Chest` bends sideways on up/down look, change `spinePitchAxis` or `chestPitchAxis` before changing limits. The default torso axis now assumes local `Z` is the forward/back bend axis.

## Notes
- `TP_Camera` must not carry the `FP_Camera` marker. Marker-based camera lookups should identify only the real first-person camera.
- This project snapshot could not be tied to a git commit because the workspace currently has no `.git` metadata.
- The feature was kept script-driven and package-free on purpose so the full flow stays visible in vanilla Unity code.
