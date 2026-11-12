# Voice Activity HUD

Stage 1 of [Voice Controls Implementation Plan](VoiceControlsImplementationPlan.md).

## Runtime setup

`GameplaySceneRoot` creates one scene-local instance of `Assets/Resources/UI/VoiceActivityHUD.prefab` during gameplay bootstrap and local-player setup. No scene or Player prefab editing is required. Existing scene-authored instances are reused. The HUD is not created in non-gameplay scenes.

The default icon sits at the bottom left, 48 reference pixels from each edge on a 1920 x 1080 reference canvas. Its size is 36 x 40. The canvas scales with the screen and renders at sorting order 50, below the settings overlay (500). It has no GraphicRaycaster, its CanvasGroup does not block raycasts, and the graphic is not a pointer target.

## States and timing

| Local state | Presentation |
| --- | --- |
| Idle | White microphone at 30% opacity |
| Speaking | Green microphone at full opacity |
| Muted or channel transmission disabled | Red crossed-out microphone |
| Voice disabled, disconnected, recovering or local participant missing | Gray crossed-out microphone |

The `VoiceActivityIndicator` component on the prefab exposes colors, idle/unavailable opacity, transition time (0.08 seconds) and speech release hold (0.12 seconds). A short idle gap retains the speaking presentation; mute/disconnect bypass that hold. Fades use unscaled time and exponential interpolation for consistent behavior at different frame rates. There is no periodic blinking animation.

The microphone artwork is a native Unity UI vector mesh (`MicrophoneActivityGraphic`), with no external sprite or font dependency. Edit the child RectTransform to reposition or resize it.

## Activity source

`VivoxVoiceManager.LocalVoiceActivity` exposes a read-only `VoiceActivitySnapshot`. Each update resolves only the `IsSelf` participant from the active owned channel, then combines `SpeechDetected` with connection, input mute and actual channel transmission. Remote speech, listener volume, distance attenuation and received reverb cannot activate this local indicator.

The HUD introduces no extra microphone capture, microphone playback, Photon messages or voice-processing changes. Existing SDK voice-activity detection controls the observed speaking state, including its existing release delay; the UI's short release hold applies after that state ends. The HUD does not retune the detector.

`AudioEnergy` is the latest bounded SDK observation, not calibrated loudness. [Voice Intensity Calibration](VoiceIntensityCalibration.md) now requests ten-per-second participant updates and exposes a separate calibrated, smoothed `LocalVoiceIntensity` monitor. The HUD continues to use speech/transmission state. Dynamic voice range remains a later stage.

[Voice Transmission Settings](VoiceTransmissionSettings.md) now supplies the effective microphone gate for open microphone/push-to-talk. Blocked input also gates this HUD, independently of a stale SDK speaking flag.

## Validation

Focused activity/HUD cases cover state precedence over stale speech, invalid energy, speech gaps, fade timing at 30/60/120 fps, paused gameplay time, re-enable reset, pointer transparency and scene bootstrap reuse. Existing Unity voice-output regressions cover unchanged source reverb, gain and mute behavior.

The rendered preview in `.codex/diagnostics/voice-activity-hud-preview-20261110.png` uses synthetic activity states. Its captions are validation annotations, not part of the shipped HUD. No microphone or voice connection is used for that preview. Actual microphone detection still needs a voice-session check after rebuilding.
