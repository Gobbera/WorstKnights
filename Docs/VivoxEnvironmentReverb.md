# Vivox Environment Reverb

## Scope and checkpoint

Checkpoint: `.codex/backups/vivox-environment-reverb-20261109/` contains the affected saved sources, voice settings and documentation before implementation.

Route the existing simulated proximity voice through Unity so the same listener-driven BoxReverbZone/EnvironmentReverbManager transition affects received speech. Preserve microphone capture, channel identity, the existing proximity distance/fade policy and the update interval.

## Implementation stages

1. Create one owned Vivox Participant Audio Tap per remote participant in the active supported channel. Silence that participant in the original channel mix to avoid duplicate dry playback. Do not tap the local microphone.
2. Configure each Unity AudioSource for 2D playback with reverb-zone participation. Keep the current proximity gain/mute on the Unity source because participant taps expose unmixed audio. Avoid a second distance rolloff.
3. Reconcile existing participants after joins/recovery, clean up on participant/channel departure, and retain direct Vivox playback if routing is disabled or a tap cannot register. Keep the optional native Vivox positional path unchanged.
4. Enable routing in the shared voice settings, document authoring, compile and test source configuration, gain/mute, lifecycle and environment DSP integration. Do not replace or save over the user's open scene.

## Routing policy

- Supported Unity route: the existing default `-sim3d` proximity mode and non-positional 2D mode on platforms with Audio Tap support.
- The optional native positional Vivox path retains its direct output. Its raw participant tap would require a separate spatialization policy, outside this change to the currently working simulated proximity mode.
- `Unity Voice Output` enables the route. `Environment Reverb Send` controls participation (initial value 1). `Voice Mixer Group` is optional; null preserves Unity's default output instead of imposing a new mixer policy.
- Reverb follows the listener's current zone and existing transitions. It does not modify transmitted microphone audio or set an environment for other clients.

## Validation

- Runtime and Editor CLI compilation pass with zero warnings and zero errors.
- 16 native Editor cases pass for source reverb participation, preserved 2D routing, gain conversion, mute, adjustable sends, supported-mode selection and the authored voice settings.
- A native Play Mode comparison uses the production source configuration, an actual BoxReverbZoneManager and a synthetic impulse at 48 kHz. After warming the DSP path, tail energy is effectively zero with reverb bypassed and nonzero with the zone enabled. The saved diagnostics include the recorded dry/wet values. The earlier offline AudioRenderer captures returned no usable samples and were not counted as proof.
- The synthetic test does not initialize a voice session or capture microphone audio. It verifies the Unity DSP path, not the live Vivox transport.
- Original proximity calculation, mute thresholds, distance settings and SDK volume/mute calls are unchanged. Participant-source gain mirrors the existing integer adjustment on Unity's amplitude scale; it is not a second distance rolloff. Global Vivox output mute and participant mute are honored on the Unity sources.
- The original Map3 scene remains loaded and clean after validation. Temporary validation objects/scripts are archived in the checkpoint and removed from Assets.
- Results: `.codex/diagnostics/vivox-environment-reverb-20261109.txt`.

Rebuild both clients and validate actual speech while entering/leaving a reverb zone, walking across the audible boundary, muting output and reconnecting. Live voice quality, exact perceived attenuation and reconnection still require that voice session.

## SDK references

- [Audio Taps in C#](https://docs.unity.com/en-us/vivox-unity/developer-guide/audio-taps/audio-taps-in-c-sharp)
- [Tap parameters and original-mix suppression](https://docs.unity.com/en-us/vivox-unity/developer-guide/audio-taps/tap-component-parameters)
- [Unity reverb-zone sends, including 2D sources](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/AudioSource-reverbZoneMix.html)
