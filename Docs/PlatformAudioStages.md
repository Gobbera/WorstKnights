# Platform Audio Stages

## Scope and checkpoint

Checkpoint: `.codex/backups/platform-audio-stages-20261108/`.

1. Preserve the existing movement cue/source as Moving and add independent Start and Stop cues/sources.
2. Play Start once on departure, keep Moving looping during travel, and play Stop once on arrival. Continuous reversal must not replay departure/arrival. Pause, break and disable must silence motion without faking an arrival.
3. Prepare sources and clip data before activation. Retain existing AudioCue spatial/mixer/reverb settings and derive playback from synchronized movement without adding audio RPCs.
4. Update the Inspector and source preparation, then validate repeated trips, empty cues, source reuse and state restoration.

## Authoring and playback

Assign **Start Audio Cue**, **Moving Audio Cue** and **Stop Audio Cue** in the Movement Audio section. Each field is optional. The existing `movementAudioCue` and `movementAudioSource` serialized fields remain unchanged and appear as Moving in the Inspector.

Start is a one-shot at departure. Moving begins at the same time and loops until travel ends. Arrival at either TwoPosition endpoint, or the final OneWay endpoint, cuts the loop and remaining Start sound and plays Stop once. A new departure cuts any remaining Stop sound. Continuous reversal and uninterrupted PingPong turns keep the loop. A PingPong pause silences motion and plays Start again on resume, without an arrival sound. Break and disable silence all stages; re-enabling an already moving platform resumes the loop without replaying Start.

The **Audio Sources** foldout allows explicit source assignments. **Prepare Audio Sources** authors missing Start/Moving/Stop children and repairs shared references without replacing the existing Moving source. The puzzle routing setup reuses this preparation. Older controllers prepare missing sources in Awake, including clip data requests; activation creates no source. Independent sources preserve each cue's pitch, volume, mixer, spatial and reverb settings. Start/Stop are forced non-looping, even when the AudioCue is marked Loop. Moving is forced looping. All three follow Moving Part.

Playback uses the existing replicated movement state. Initial room-state restoration starts only the current loop; it does not replay historical Start/Stop. Stale snapshots remain rejected. No snapshot-format change or additional audio RPC is required.

## Validation

All 136 Editor regression cases passed, including five new platform cases for idempotent source preparation, shared-source repair, both arrival endpoints, pause/break behavior and OneWay versus continuous PingPong travel.

Native Play Mode verified independent Start/Moving/Stop playback, forced loop flags, no repeated one-shots during steady motion/rest, continuous reversal, arrival at A and B, new departure interrupting the old Stop tail, disable/re-enable, break/respawn, independently empty cue fields, initial moving-snapshot restoration and an unchanged source count of three. Silent generated clips were used only in the temporary preview. The original scene was restored and temporary validation assets were removed.

Map3 now has prepared Start and Stop audio children in addition to the original Moving source. Its path (15.8 m downward at 2.5 m/s), activation references and original movement cue/source were preserved. Cues remain unassigned for the intended assets. Compared with the live checkpoint, the scene has six added blocks (two GameObjects, two Transforms, two AudioSources), two updated blocks (controller references and the prefab's added-child list), and no removed objects.

Runtime and Editor builds succeeded. The first full dependency build reported existing Photon obsolete-API/unassigned-field warnings. Evidence: `.codex/diagnostics/platform-audio-stages-20261108.txt`. Two-client Photon validation was not performed.
