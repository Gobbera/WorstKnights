# Paired Double Doors

## Scope and checkpoint

Checkpoint: `.codex/backups/paired-doors-20261109/`.

1. Keep a full DoorController and reaction stack on each door root.
2. Link the root controllers reciprocally so either door can initiate the action.
3. Preserve the authored geometry, child hinges, prompt pivots, motion profiles, colliders and audio references.
4. Validate either root initiating open/close/reversal, shared cooldown, reciprocal state/network updates and single-door compatibility.

## Authoring

Add a DoorController to each door root. Each controller uses its own root as **Moving Part**, its authored **Rotate Pivot** and **Prompt Pivot**, and its own motion and reaction settings. Set **Paired Door** on both controllers to reference each other. Pairing only activates for two distinct controllers in the same scene with reciprocal references. Empty, self or one-sided links do not create an implicit primary/follower relationship.

Either controller can initiate an explicit open/close/lock state. Accepted state is applied locally to each peer without calling the other peer's public toggle, avoiding recursion and double toggles. Both peers share the interaction cooldown and enemy interaction blocking. An unlocked peer delegates lock interaction to its locked counterpart when needed. Use matching startup, lock, duration and curve settings for synchronized double doors.

Paired opening shares a physical side of the doorway. The initiating controller's panel-center travel defines that side, and the peer selects the hinge-local sign that reaches it. Local direction signs may differ even when the configured angle values match. Open Away From Player samples the panel geometry so a Transform origin on the hinge still resolves the correct side. Closing retains each peer's own resolved direction; room state stores those individual directions. See [PairedDoorSwing.md](PairedDoorSwing.md).

Whichever active peer updates first advances both motion updates once in that frame; the other peer skips that frame. This prevents update order from introducing a frame of delay without assigning a fixed driving controller. Each controller still evaluates its own captured hinge and authored motion profile.

Room-state publication writes both scene-door keys in one property update. RPC or room-state application also updates the local peer without republishing or recursive RPCs. Existing door-state encoding is retained. Each root has its own stable scene identity. Both peers expose the combined moving colliders for enemy obstruction and traversal collision exceptions; their static frame is excluded.

Each root has its own ReactionSignalReceiver, ReactionSignalEmitter and DoorReactionSignalBridge. A pair action emits one transition on each root. Cues, feedback origins and prompt pivots remain editable per door. No controller or reaction stack is needed on the common static arch.

## Map3

Door_L_Arch_B and Door_R_Arch_B each own one controller. Their Paired Door fields reference each other. Moving Part points to the controller's own root; Rotate Pivot and Prompt Pivot reference that root's original child transforms. The latest mesh reimport applied the former mirror transforms: both roots now have positive unit scales and matching hinge orientations. Both authored Y angles remain +90 degrees; the runtime resolves each leaf's local sign to open both toward the same physical side. Open Away From Player works from either side.

The latest authored motion settings are preserved (currently a 3.5-second duration and 1-second cooldown), together with the curve, colliders, leaf NavMesh obstacles and AudioCue references. Each navigation helper references its own root's controller. Reaction origins point to their own leaf pivots. The common arch contains static geometry; each door root contains its controller and reaction stack.

## Validation

### Model reimport audit

The placed arch remains a model prefab instance; the controllers, pivots, prompts and reaction components are scene authoring on that instance. After a model reimport, inspect the live hierarchy and the Moving Part, Paired Door, hinge, prompt, navigation and reaction references before rebuilding authoring data. Wait for import/reload to finish and preserve the latest mesh, materials and authored transforms.

The subsequent reimport audit found both door roots, their four child pivots/prompts, reciprocal pairing and all navigation/reaction references intact, with no missing scripts or broken serialized references. The common arch has only its Transform. No additional scene repair or model rollback was needed. Evidence: `.codex/diagnostics/door-arch-reimport-20261109.txt`.

### Behavior validation

All 164 focused Editor cases passed: 26 paired-root cases, 136 existing mechanism/platform/puzzle/interaction cases and the two existing route/probe tests. Pair cases cover reciprocal identity, either peer initiating, physical swing-side conversion, origins on the hinge, one-sided/self-link safety, per-root prompts, cooldown, lock delegation, permanent signal opening, imported mirror axes, shared state restoration, independent slide settings, initial open state, destruction, collider ownership and enemy obstruction/collision exceptions.

Native Play Mode used an additive copy of the configured arch and PlayerPickupInteractor's real focus/use path. Aiming at each panel resolved that panel's own DoorController. One interaction opened both to 90 degrees; interaction on the other panel closed both. Opening from the opposite side and reversing during travel kept both panels synchronized, with fixed authored hinge positions. Both roots retained their own prompt anchors and reaction stack. Two opening/closing cycles emitted one Opened and Closed event per peer per cycle, without recursion or duplicated callbacks. The static arch had no controller/receiver/emitter/bridge.

The FBX was reimported while authoring was in progress. The migration waited for the authored hierarchy to be restored and retained the latest local prompt adjustments. Temporary previews kept the original scene loaded and restored it afterward; their assets and helper were removed. Runtime/Editor compilation succeeded. Evidence: `.codex/diagnostics/paired-doors-20261109.txt`. The broader enemy-suite failures reported in the preceding investigation were not retested; this pass targets door and interaction behavior. Two-client Photon validation was not performed.
