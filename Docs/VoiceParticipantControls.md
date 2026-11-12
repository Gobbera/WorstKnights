# Per-Participant Voice Controls

Stage 3 of [Voice Controls Implementation Plan](VoiceControlsImplementationPlan.md).

## Player behavior

Open settings while in a room to see `Players in Room`. The local player is excluded. Each remote member has a display name, voice connection status, volume slider (0–100%, default 100%) and `Mute` / `Unmute` button. The list scrolls for larger rooms, including the project's 16-player limit.

The controls affect only the current listener. Muting preserves the slider value; moving the slider while muted chooses the volume to restore later. Zero volume is silent independently of the mute button. Increasing the slider cannot extend the speaker's audible range.

Room membership comes from Photon, so members are listed before Vivox finishes connecting. `Voice unavailable` indicates there is no matching connected voice participant yet; temporarily inactive room members show `Reconnecting...`. Muting shows `Muted for you`. Names are displayed as plain text and never used to identify controls.

## Identity and lifetime

`VoiceListeningPreferences` belongs to the persistent voice manager. Pending edits are stored by room actor number, then associated with the existing `kwkVivoxPlayerId` property when it arrives. Known voice identities retain choices for the running game session across voice reconnections and room rejoin with a new actor number. Room transitions reset actor associations to prevent reused actor numbers from inheriting an unrelated player's settings. Missing-identity rows that have not been edited cannot overwrite a previously known identity preference.

No listener preference is published to Photon, sent to another player or written to PlayerPrefs. Choices are not persisted across application restarts; standalone Vivox identities are intentionally ephemeral in the current project configuration.

## Mixing and lifecycle

The original distance gain and distance mute remain separate from listener preferences. `VoiceListeningMix` combines them:

- Unity amplitude = existing distance amplitude × listener volume.
- SDK participant gain = existing distance dB + listener-volume dB, bounded to the SDK's existing -50..0 adjustment policy.
- Effective local mute = distance mute OR manual mute OR zero listener volume.

The SDK path supports direct/native positional playback and fallback output. Unity Participant Audio Taps expose unmixed audio, so their AudioSources receive the combined amplitude once. Reverb source configuration and output-device/moderator mute remain honored. No distance settings or microphone/sender intensity properties are modified.

Applied SDK requests are cached per `VivoxParticipant` instance, separate from the distance cache. Distance resets apply the combined listener state and cannot undo a manual mute. A reconnect creates a new participant instance and reapplies the existing identity preference. Unmapped simulated-proximity participants remain muted, including after a formerly valid mapping disappears. The microphone login guard introduced for error 1001 is preserved.

## UI setup

`SettingsMenu` instantiates one `Resources/UI/RoomVoiceSettingsPanel.prefab` below the existing voice settings. The shared menu prefab and gameplay scenes need no new references. Edit the room panel resource to author the header, scroll view and inactive `VoiceParticipantRow` template.

The panel is visible only inside a Photon room and refreshes on menu opening and every 0.25 unscaled seconds while open. Rows are reused by actor number, preserving slider drag and scroll state when values change. Joining/leaving changes only the affected rows. The room section shows an empty message for a solo player and stops accepting pointer input outside a room.

## Validation

All 21 new native Editor cases and 65 applicable existing voice/input/HUD/lifecycle regressions pass (86 total, zero failures). The two keyboard frame-edge integration cases are skipped in Edit Mode and retain the earlier native Play validation. Focused cases cover identity rebinding/rejoin, independent listeners, actor reuse, duplicate names, manual mute across distance changes, unchanged authored range, source/reverb participation, actual UI callbacks, row reuse, full-room scrolling and slider geometry. Tests use an isolated in-memory Photon room and restore the original client state and manager singleton without connecting a network session or capturing a microphone.

The synthetic room preview is `.codex/diagnostics/voice-participant-controls-preview-20261110.png`; test results are `.codex/diagnostics/voice-participant-controls-20261110.txt`. Unity Services remained uninitialized and the original Map3 scene remained loaded and clean. Actual received speech and reconnection behavior should also be checked in matching rebuilt clients.

Final runtime/Editor CLI compilation passed with zero warnings and zero errors after removing temporary validation tooling.
