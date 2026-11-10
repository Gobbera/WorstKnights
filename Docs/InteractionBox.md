# Interaction Box Range

## Scope and checkpoint

Checkpoint: `.codex/backups/interaction-box-20261106/` contains affected source, prefab and documentation before implementation.

1. Add Distance / Box Collider range modes to Interaction Trigger Interactable, preserving Distance as the serialized default.
2. In box mode, require the player's body/root position inside an enabled linked box. Support local center/size, rotation and scale; the box grants eligibility without activating the mechanism automatically.
3. Let eligible box interactions extend aim search beyond the numeric player reach, while preserving pickup/ordinary interactable reach and line of sight.
4. Provide Inspector creation/linking and inline box editing. Prepare a separate trigger box on the existing Lever_Interaction_01 variant without changing its selected Distance mode.
5. Validate focus, prompt, use, box geometry, disabled/missing references, ordinary reach regression and timed-lock behavior in Play Mode.

## Inspector workflow

On **Interaction Trigger Interactable**, choose **Range Mode**:

| Mode | Eligibility |
| --- | --- |
| Distance | Camera-to-mechanism physical collider distance, capped by the player's global Interaction Distance. |
| Box Collider | Player root/body position inside the enabled assigned box. This replaces numeric reach. |

For **Box Collider**, assign **Interaction Box** or click **Create Interaction Box** in Scene/Prefab Mode. A new child named **Interaction Area** is created as a separate trigger on Ignore Raycast; physical mechanism colliders remain solid. Use **Box Center (Local)** and **Box Size (Local)** directly in the same Inspector, or **Select Interaction Box** to edit the native BoxCollider. The selected interactable displays its box gizmo. Local size/center follow the collider transform's rotation and scale.

The prepared `Lever_Interaction_01` variant now includes a linked box. Its selected mode remains Distance, with the existing 1.5 m reach and one-second optional control lock. Switch Range Mode to Box Collider to use the volume. Other Lever/Button prefabs can use the same fields and creation workflow. External BoxColliders can also be linked explicitly.

## Player behavior

The player's root/body point must be inside the box, rather than the camera. The camera can be outside a short box while the body is inside. Missing, disabled, inactive or zero-sized boxes reject interaction. Entering the area grants permission to aim at the mechanism and press the normal interaction key; it does not activate it automatically. The box itself is not an aim target.

Eligible box providers extend the player's aim search far enough to reach their mechanism, so a larger box is not silently limited by the numeric distance setting. Box interactions require line of sight; ordinary interactables and pickups retain their previous numeric reach even while the search is extended. A registry avoids scanning the scene every frame. Trigger volumes are excluded from numeric mechanism-distance geometry.

Focus, prompt, direct interaction and the existing Master request validation share the same box predicate. Exiting or disabling the box prevents new uses and hides its prompt. Accepted interactions keep the existing timed player lock, animation trigger and start/finish behavior. Changing area membership does not cancel an already accepted timed animation window.

## Validation

**126 Editor tests pass**, including 11 area-specific tests: body versus camera containment, outside rejection, local-center/rotation/nonuniform-scale geometry, missing/disabled/inactive/zero-sized or zero-scaled boxes, focus beyond numeric reach, aim-proxy rejection, wall obstruction, unchanged ordinary/pickup reach, numeric-range isolation and idempotent box creation preserving solid collision.

Native Play Mode used the actual Player and prepared Lever variant. It confirmed outside rejection, inside-box focus/prompt/use on a mechanism 5 m away, timed control lock and Animator trigger behavior, automatic recovery, and prompt/use rejection after leaving or disabling the box. The original scene and its prior dirty state are restored; temporary preview scenes, animation probe assets and helper scripts are removed afterward.

Runtime and Editor projects compile without errors or warnings. Evidence: `.codex/diagnostics/interaction-box-20261106.txt`. Two-client Photon validation remains outstanding.
