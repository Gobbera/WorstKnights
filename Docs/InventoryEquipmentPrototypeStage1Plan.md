# Inventory / Equipment Prototype - Stage 1 Plan

Date: 2026-05-26

## Checkpoint

- Manual project snapshot created at `Builds/ProjectSnapshots/KingsWorstKnights_pre_inventory_20260526_000755.zip`.
- Important: this snapshot only contains what was already saved on disk. If the current Unity scene had unsaved changes in the editor, those changes are not part of the backup.

## Requested Scope

Implement the first functional prototype of a hand-based inventory/equipment system with pickup support, keeping the architecture simple and expandable.

### Items in the current test

- Sword
  - Allowed hand: `RightOnly`
- Health potion
  - Allowed hand: `LeftOnly`

### Hand slot model

- Right hand
  - `Slot1RightHand`
  - `Slot2RightHand`
- Left hand
  - `Slot1LeftHand`
  - `Slot2LeftHand`

Rules:

- Each hand has 2 slots.
- Only 1 slot per hand is active at a time.
- Key `1` toggles the active right-hand slot.
- Key `2` toggles the active left-hand slot.
- Left mouse uses the item in the active right-hand slot.
- Right mouse uses the item in the active left-hand slot.
- Active slot visual = local scale `1.1`.
- Inactive slot visual = local scale `1.0`.

### Pickup flow

- Player presses `E` near / looking at a world item.
- The item carries data:
  - item name
  - UI sprite
  - hand requirement
  - optional future use type
- Pickup checks:
  - which item was targeted
  - which hand requirement the item allows
  - which active slot should receive it
  - whether that active slot is empty
- If compatible active slot is empty:
  - equip item
  - update UI sprite
  - disable/remove world item
- If occupied:
  - do not pick up
  - emit a clear `Debug.Log`

### Supported hand requirements now

- `RightOnly`
- `LeftOnly`
- `Any`
- `TwoHanded`

Notes:

- `Any` can prefer right first, then left, or remain configurable.
- `TwoHanded` does not need full behavior yet, but the architecture should leave room for future blocking of both hands.

### Logging required for testing

- item equipped
- slot occupied
- incompatible hand
- item used
- slot toggled

## Findings From Current Project State

1. The project is not inside a Git repository right now, so version saving had to be done with a manual `.zip` snapshot instead of a commit.
2. The names `Inventory`, `Slot1LeftHand`, `Slot2LeftHand`, `Slot1RightHand`, and `Slot2RightHand` do not appear in the saved scene YAML on disk.
   This strongly suggests the current scene setup may exist only in the open Unity editor and still needs to be saved.
3. The local player already uses `Mouse0` as the base attack input through `InputConfig` and `PlayerController`.
   This is the main input conflict we need to resolve when we implement item use on the right hand.
4. `Assets/Resources/Player.prefab` is the main player prefab used by runtime setup.
5. That player prefab already contains a `Sword` object under an existing `HandSocket`.
   This can visually conflict with the prototype if we want “equipped” to be represented only by the new slot system at first.
6. `CombatHealth` currently supports damage, but there is no generic heal API yet.
   For the first prototype, potion use can be a debug action or a very small targeted extension.

## Recommended Stage Split

### Stage 0 - Editor alignment

- Save the current working scene in Unity.
- Confirm the `Inventory` UI object and all 4 slot objects exist in the saved scene.
- Decide whether the current always-visible sword in `Player.prefab` should stay for now or be disabled during prototype validation.
- Confirm whether the prototype should affect only the local offline test flow first, without Photon synchronization for inventory state.

### Stage 1 - Core data and hand-slot runtime

- Add enums:
  - `HandType`
  - `HandRequirement`
  - optionally a simple `ItemUseType`
- Add an `ItemDefinition` ScriptableObject for item data.
- Add a runtime controller that owns:
  - 2 slots for right hand
  - 2 slots for left hand
  - active index per hand
  - equip/use methods
  - validation and debug logs

### Stage 2 - UI binding

- Add a small UI script that:
  - references the 4 slot transforms / images
  - assigns slot sprites
  - applies active scale `1.1`
  - restores inactive scale `1.0`

### Stage 3 - Pickup interaction

- Add a world pickup component that references `ItemDefinition`.
- Add a simple pickup detector on the local player:
  - likely camera-forward raycast with a short distance
  - triggered by `E`
- On success, pass the item definition into the hand-slot controller.

### Stage 4 - Item use prototype

- Right active slot use on left mouse.
- Left active slot use on right mouse.
- For this stage:
  - sword can log use and optionally reuse current melee attack later
  - potion can log use first, then optionally consume/heal in a tiny follow-up step

## Planned Runtime Architecture

### Core idea

Keep world-item data separate from runtime slot state and separate again from UI display.

### Proposed scripts

- `ItemDefinition`
  - ScriptableObject with item metadata and allowed-hand rules.
- `WorldPickupItem`
  - Component placed on sword/potion world objects.
  - Holds a reference to `ItemDefinition`.
- `HandEquipmentController`
  - Main runtime owner of the 4 hand slots.
  - Knows active slot per hand.
  - Performs equip validation and item use.
- `HandEquipmentUI`
  - Reads slot state and refreshes sprites/scales on the 4 UI objects.
- `PlayerPickupInteractor`
  - Lives on the local player.
  - Detects interact target with `E`.

## Editor Work Needed Before Implementation

Please do these in the Unity editor first:

1. Save the scene you are currently using for the prototype.
2. Open the scene file that will actually receive this system and confirm it is the one you want me to patch.
   Recommended guess: `Assets/Scenes/FieldTestCharacter.unity`.
3. Confirm the `Inventory` object is part of the saved scene, not just an unsaved editor state.
4. Confirm each slot object contains an `Image` component that will display the item sprite, or tell me if the `Image` is on a child object instead of on the slot root.
5. Decide whether the current sword already attached inside `Assets/Resources/Player.prefab` should:
   - remain as-is temporarily
   - be disabled for this prototype
6. Confirm if we should treat the first pass as local-only gameplay logic.
   Recommended: yes, local-only first.
7. If the world sword and potion objects are already positioned in the scene, save the scene after verifying them.

## Decisions Still Needed Before Coding

These are the only decisions that materially affect the first implementation:

1. What should happen with the current `Mouse0` melee attack?
   Recommended first pass: route `Mouse0` through the new right-hand slot system, and let the sword item call the existing melee attack behavior when equipped.
2. Should the potion actually heal now, or only log use in Stage 1?
   Recommended first pass: log use only, then add healing in a small follow-up step.
3. Should equipped items appear only in UI for now, or also be attached/removed visually from hand sockets?
   Recommended first pass: UI only.

## Implementation Goal For Next Step

After the editor confirmations above, implement only the first functional version:

- 4 hand slots
- active slot toggling
- UI sprite refresh
- UI active scale refresh
- pickup with `E`
- world item disable on successful pickup
- clear debug logs
- simple use flow on left/right mouse

Keep out for now:

- backpack
- drag and drop
- stacking
- advanced dual wield
- full two-handed logic
- deep networking sync
