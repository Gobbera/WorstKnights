# Voice Intensity Calibration

Stage 4 of [Voice Controls Implementation Plan](VoiceControlsImplementationPlan.md). Local relative intensity and calibration now feed [Dynamic Voice Proximity](VoiceDynamicProximity.md), which also adds optional Hear myself playback in the test dialog.

## Using the microphone test

1. Restart Play or rebuild so the next Vivox login requests periodic participant updates. Join a room and wait for voice to connect.
2. Open Settings > Microphone Test > Open test. An offline dialog stays unavailable and does not initialize voice.
3. If quiet speech keeps reading as loud, select Automatic input level to switch it Off before recalibrating. This changes Vivox input processing and can change the voice volume sent to others. The explicit choice is saved and reapplied after future logins; the SDK default is preserved until the user changes it. Unsupported platforms show Unavailable.
4. Select Calibrate and stay silent. If using push-to-talk, hold the configured key throughout each measurement, including silence.
5. Select Measure next step for quiet, normal and comfortably louder speech. Use the same microphone distance and speak steadily for about three seconds of accepted samples per step. The measured room/quiet/normal/loud input levels appear above the meter.
6. A successful profile is saved automatically. Cancel, Close, Escape, connection loss and failed calibration keep the previous valid profile. Reset profile removes only the current microphone/context profile.
7. Optionally enable Hear myself for local microphone playback while testing. Use headphones; hold PTT if configured. Closing the test turns playback Off.

Before calibration and throughout a collecting attempt, the meter displays smoothed input energy as Input level. It does not label an unknown microphone's input as Quiet or Loud. Once a valid profile is available, the meter explicitly displays Calibrated intensity with Silent, Quiet, Normal or Loud. The reference row is labeled Measured input during an attempt and Saved input for the active profile. These percentages use different scales: input energy is the SDK's bounded observation, while calibrated intensity is relative to the measured speech anchors. Saved input remains visible after calibration; it is not the live normalized meter. Failed/canceled attempts return the reference row to the active profile's saved anchors; a failed attempt also states when the previous calibration remains active. Input closed means mute/PTT or another transmission gate prevents measurement. The dialog uses the existing microphone mode: speech during calibration can be heard by the room. Local playback is Off by default and available through Hear myself, using the existing Vivox microphone rather than a second capture device.

## Measurement and calibration

`VivoxVoiceManager` requests `ParticipantPropertyUpdateFrequency.TenPerSecond` in login options. This SDK setting applies to participant property updates across that login, including remote participants; it does not change encoded voice quality. Existing sessions must log in again to adopt the setting.

`LocalVoiceActivity` observes only the active channel's self participant, with the existing connection, mute, focus and transmission gates. `VoiceIntensityMonitor` polls its energy at most once per 0.1 seconds. A slow frame never duplicates the same observation to catch up. The SDK publishes changed properties: absence of an energy event alone cannot prove stale input.

Each calibration step accepts 30 samples. Silence requires available input without detected speech. Guided speech steps accept energy above the room's 90th-percentile level plus a margin based on measured noise variation, even when the SDK detector misses soft speech. Closed input does not contribute samples. Each collecting step fails after 15 seconds without enough valid samples. Silence uses the 90th percentile and speech uses medians. Normal/quiet and loud/normal gaps must each exceed 7% of the observed noise-to-loud span; quiet must also clear the noise floor. High-offset but clearly separated readings are accepted. Compressed, out-of-order or nearly identical readings are rejected. Isolated peaks cannot define a speech level. An unsuccessful attempt does not replace a usable profile.

Normalization interpolates directly between observed SDK energy anchors: the noise gate, quiet (25%), normal (60%) and loud (100%). The SDK energy contract does not identify the value as linear PCM amplitude, so no extra logarithmic amplitude conversion is assumed. Two consecutive speech observations above the noise threshold open the calibrated meter gate. Opening/closing thresholds use 20%/10% of the measured noise-to-quiet gap. Unscaled exponential smoothing uses 0.08 seconds for attack and 0.22 seconds for release on both raw and calibrated meters; blocked input immediately clears both. Provisional anchors remain an internal fallback; the UI does not classify them as calibrated speech.

`VoiceIntensityProfileStore` saves versioned, validated anchors in local PlayerPrefs. A hashed context key includes the effective microphone name, Vivox input volume and available automatic-gain/noise-suppression flags. The manager checks context once per second while connected and reloads the matching profile when it changes. Profiles are independent of ephemeral Vivox player IDs and listener volume preferences.

## Limits and live review

Energy is a relative SDK observation, not physical sound pressure. Microphone gain, distance, operating-system effects, hardware processing and Vivox processing all affect it. The dialog reports automatic input-level status and provides an explicit opt-in On/Off control for Vivox automatic gain. It does not modify noise suppression, echo cancellation, SDK input volume, received volume or range. Microphone-processing reads/writes require initialized SDK and confirmed login; offline settings cannot initialize voice. Automatic gain control or compression may collapse quiet/normal/loud differences; calibration rejects those overlapping anchors instead of claiming usable results. OS/hardware changes invisible to the SDK require manual recalibration. A high Input level alone does not establish audio clipping or physical loudness.

For rebuilt-client review, test real microphone energy separation in matching clients, including background fan/keyboard noise, held/released PTT, normal speech and comfortably louder speech. Confirm that a saved profile reloads after restarting, switching microphone reloads the appropriate profile, and failed/canceled attempts preserve earlier calibration. If levels overlap, switch Automatic input level Off and repeat the test. If readings remain near 100% at all speech levels, review microphone/OS gain and external processing.

The original stage 4 added no Photon intensity messages. Stage 5 now publishes calibrated intensity for simulated 3D range limiting; listener volume/mute remains independent and received Unity reverb keeps its existing route. See [Dynamic Voice Proximity](VoiceDynamicProximity.md) for current range settings, the local playback option and the two-client test procedure.

## Validation

- Final runtime/Editor CLI compilation passed with zero warnings and zero errors after removing the temporary validation helper. The helper is archived in `.codex/backups/voice-intensity-calibration-20261110/`.
- 22 new native Editor cases passed: calibration, normalization across microphone scales, noise/spike rejection, 30/60/120 fps smoothing, input gating, timeouts, failed/canceled attempts, persistence, frame hitches and dialog lifecycle.
- All 86 applicable existing voice/input/HUD/reverb regressions passed. Two keyboard frame-edge cases are skipped in Edit Mode and retain their previous native Play validation.
- Settings and dialog previews were rendered at 1280 x 720 using synthetic metadata and inspected. Unity Services remained uninitialized; no microphone capture, voice login or network connection was started. Map3 remained loaded and clean.
- Diagnostics: `.codex/diagnostics/voice-intensity-calibration-20261110.txt`. Preview files share that directory with the `voice-intensity-settings-preview` and `voice-intensity-dialog-preview` prefixes.

The implementation follows the installed Vivox SDK's `IParticipantProperties.AudioEnergy` contract and `LoginOptions.ParticipantUpdateFrequency` API; see Unity's [Vivox v16 upgrade guide](https://docs.unity.com/en-us/vivox-unity/v16-upgrade-guide) for the participant-update setting. Unity documents the automatic-gain switch in [IVivoxGlobalAudioSettings](https://docs.unity.cn/Packages/com.unity.services.vivox%4016.11/api/Unity.Services.Vivox.IVivoxGlobalAudioSettings.html).

## Sensitive-input follow-up validation

The user reported completed calibration classifying very quiet speech as Loud with Automatic input level On. The follow-up adds raw-meter presentation, range-relative anchor validation, guided soft-speech collection, measured step levels and the saved automatic-input-level switch. An arbitrary meter scale and automatic processing are separate possible causes; no real microphone measurement was taken by the validation tooling.

- All 33 intensity/calibration cases passed, including 11 additional high-offset, soft-speech, noise-variation, compressed-level, raw-meter, mute/reset, offline UI and isolated AGC-switch cases. All 86 applicable existing regressions also passed (119 total). The two previously validated Play-only keyboard cases remain skipped in Edit Mode.
- SDK automatic gain was represented by an in-memory implementation for switch tests; noise suppression, echo cancellation and clipping protection retained their values. No native microphone processing was changed by tests.
- The new panel was rendered and inspected using synthetic data. Diagnostics: `.codex/diagnostics/voice-intensity-sensitive-input-20261110.txt`; previews use the `voice-sensitive-input-` prefix in that directory. Unity Services stayed uninitialized and Map3 stayed loaded and clean.
- Live microphone confirmation is still required: switch Automatic input level Off, calibrate again and compare quiet/normal/loud readings before proceeding with dynamic range.
- The temporary validation helper is archived in the sensitive-input checkpoint and removed from Assets. The final calibrated preview uses a fresh UI instance because repeated immediate Canvas captures within one Editor frame can leave incomplete cached geometry.
- Final runtime/Editor CLI compilation after helper removal passed with zero warnings and zero errors.

## Saved-reference display clarification

Read-only inspection of the standalone game's profile found room 0%, quiet 48.203939%, normal 54.039603% and loud 63.921022%, with automatic input level Off. These match the user's reported 48%/54%/64%. They are calibration input anchors. At those same input values, the active profile maps to calibrated quiet 25%, normal 60% and loud 100%, after smoothing settles. The input-reference row previously reused speech labels without an explicit scale, including after calibration, making it easy to confuse that row with the live intensity meter.

The UI now distinguishes Measured input, Saved input and Calibrated intensity. The saved-reference row reads the active profile, so a discarded failed/canceled attempt cannot display its anchors as though they were active. Measurement, normalization, profile storage and microphone processing are unchanged by this display correction. The numeric anchors alone do not establish microphone accuracy or repeatability; assessing overlapping speech distributions still requires signal observations.

All 119 applicable existing native Editor cases passed. A native probe copied these saved numeric anchors into an in-memory monitor and confirmed the expected quiet/normal/loud mapping after smoothing, checked saved references after failed/canceled attempts, and rendered the clarified UI without label overflow. No user profile was changed; Unity Services remained uninitialized and Map3 remained loaded and clean. Diagnostics: `.codex/diagnostics/voice-intensity-display-clarity-20261110.txt`; preview: `.codex/diagnostics/voice-intensity-display-clarity-preview-20261110.png`.
