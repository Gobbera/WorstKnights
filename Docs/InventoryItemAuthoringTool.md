# Inventory Item Authoring Tool

Date: 2026-08-10

## Requested Scope

Move item-authoring responsibilities out of `WorldPickupItem` and into a dedicated Editor window.

## Goals

- Keep `WorldPickupItem` focused on runtime interaction between player and item.
- Remove authoring-heavy serialized parameters from `WorldPickupItem` such as collider auto-configuration, collider shape/cache values, equipped scale multiplier, preserve-world-scale toggle, and drop mass.
- Create a separate window with two main flows:
  - `Novo Item`: create a new `ItemDefinition`, assemble a pickup prefab from a selected model, configure pickup collider, create default grip point transforms, outline, and save the prefab under `Assets/Prefabs/Items`.
  - `Editar`: search only existing prefabs from `Assets/Prefabs/Items` and edit item data, pickup collider, grip point references, and related setup.
- Keep scene/socket-based grip positioning in its own window.

## Editor Tool Shape

- Item configuration menu entry: `Tools/Inventory/Item Authoring Tool`.
- Grip positioning menu entry: `Tools/Inventory/Item Grip Authoring Tool`.
- New item outputs:
  - `Assets/Resources/Items/<ItemName>.asset`
  - `Assets/Prefabs/Items/<ItemName>.prefab`
- Prefab structure:
  - Root: `WorldPickupItem` + `Outline`
  - `Model`: selected model/prefab instance
  - `PickupTrigger`: trigger collider used by player interaction
  - `GripPoint_Right` / `GripPoint_Left`: hand grip reference transforms
  - `GripPoint_FPS_Right` / `GripPoint_FPS_Left`: first-person hand grip reference transforms
  - `DropCollision`: optional generated solid collider for physical drops

## Runtime Boundary

`WorldPickupItem` may still manage runtime state transitions such as equip, unequip/drop, collision suppression, renderer visibility, first-person presentation clones, and network scene registration. It should not own Editor-only item creation or collider authoring settings.

## Current Implementation

- `WorldPickupItem` now exposes only the runtime item reference, network scene id, pickup trigger reference, and right/left grip references.
- Collider shape, collider fitting, outline setup, prefab creation, and `DropCollision` live in `Tools/Inventory/Item Authoring Tool`.
- Scene preview, target socket selection, and grip pose saving live in `Tools/Inventory/Item Grip Authoring Tool`.
- Grip authoring saves TP sockets into `GripPoint_Right` / `GripPoint_Left` and FPS sockets into `GripPoint_FPS_Right` / `GripPoint_FPS_Left`, so the two perspectives do not overwrite each other.
- `ItemDefinition` fields are shown by use type: consumables show healing/consume settings, while `Weapon` and `MeleeWeapon` show `Base Damage`.
- Sellable item fields are independent from use type: enable `Can Be Sold` to reveal `Sell Price`.
- In `Editar`, item data, runtime pickup references, and checked prefab setup actions are staged in the window and saved only when `Confirmar Edições` is clicked at the bottom of the window; `Reverter` reloads the current prefab data.
- Confirming edits writes through the prefab contents and renames the prefab root/file plus the local `ItemDefinition` asset to match `Item Name` when applicable.
- Player melee hitboxes use `ItemDefinition.BaseDamage` when the attack was started through a `Weapon` or `MeleeWeapon` item.
- The old `WorldPickupItem` inspector now points authors to the new tool instead of showing collider/scale/drop authoring fields.

## Usage

### Create A New Item

1. Open `Tools/Inventory/Item Authoring Tool`.
2. Click `Novo Item`.
3. Fill `Item Name`, select the model, configure item data, choose the pickup collider shape, and set outline color/width.
4. Click `Criar Item`.

The tool creates the `ItemDefinition`, builds the prefab root with `WorldPickupItem` and `Outline`, adds the selected model under `Model`, creates `PickupTrigger`, creates TP/FPS grip point transforms, and creates a disabled `DropCollision` collider for physical drops.

### Edit An Existing Item

1. Open `Tools/Inventory/Item Authoring Tool`.
2. Click `Editar`.
3. Use the `Item Prefab` search field to pick a prefab from `Assets/Prefabs/Items`, or select one in Project and click `Usar Selecionado`.
4. Edit the `ItemDefinition` and runtime pickup references in the window.
5. Mark `Gerar/Atualizar Outline`, `Gerar/Atualizar Grip Points`, or `Gerar/Atualizar Drop Collision` when the prefab should receive one of those setup pieces.
6. At the bottom of the window, click `Confirmar Edições` to save those edits, or `Reverter` to discard pending changes.
7. Use `Recriar PickupTrigger Pelo Visual` to rebuild the trigger collider from renderer bounds.

### Author A Grip

1. Drag an item prefab instance into a scene.
2. Select that item instance in the Hierarchy, or open `Tools/Inventory/Item Grip Authoring Tool` from the item Inspector.
3. In `Target`, assign the scene object that contains the sockets.
4. Use `Preview Socket` to choose one socket found inside `Target`. The tool uses `PlayerItemSocketMarker` when present, and falls back to transform names containing `Socket` when no marker exists.
5. Click `Posicionar Para Ajuste`, adjust the item in the Scene view, then click `Salvar Grip`.

The grip is saved back to the prefab source.
