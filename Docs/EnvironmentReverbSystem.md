# Environment Reverb System

Use Assets/Prefabs/Audio/Reverb/PF_BoxReverbZone.prefab for room and corridor reverb. Size its trigger BoxCollider, select an AudioReverbPreset and choose the transition time. No manager needs to be placed manually.

## Authoring

- **Preset**: Room, Stoneroom, Hallway, StoneCorridor, Cave or another Unity preset.
- **Wet Scale**: overall effect strength; 1 is full strength and 0.5 is subtler.
- **Priority**: the higher value wins when zones overlap.
- **Transition Time**: fade duration when this zone becomes active.
- **Player Detection Mask**: layers included in local-player detection.
- **Draw Gizmos**: shows the authored box in Scene view.

The component enforces Is Trigger on its BoxCollider.

| Environment | Preset | Wet Scale | Transition Time |
| --- | --- | --- | --- |
| Stone room | Stoneroom | 0.7 | 0.8 s |
| Long corridor | StoneCorridor | 0.85 | 0.6 s |
| Large cave | Cave | 0.9 | 1.2 s |
| Dry area | Off | 0 | as required |

A thin zone in a doorway can use Hallway, a higher priority such as 100 and a 0.2-0.5-second transition to soften the change between rooms.

## Runtime behavior

When the local player enters a zone, BoxReverbZone creates a scene-local BoxReverbZoneManager. The manager selects the current preset and blends its settings onto a technical AudioReverbZone following the active AudioListener. The authored BoxCollider determines room membership; the technical zone supplies Unity's reverb DSP.

The profile-based EnvironmentReverbManager remains available for scenes that use EnvironmentReverbProfile and its associated volume/portal components.

## Sound effects

Each AudioCue controls its participation through **Affected By Environment Reverb** and **Environment Reverb Send**. Enable these for world footsteps, doors, impacts, weapons, enemy voices and world loops. UI, music and intentionally dry ambience can bypass environment reverb.

## Player voice chat

The current simulated proximity voice now routes remote participants through owned Vivox Participant Audio Taps and Unity AudioSources. The sources enable reverb-zone sends, so the listener's existing room preset, wet scale and transition affect received speech as well as world sounds.

Configure **Unity Voice Output**, **Environment Reverb Send** and optional **Voice Mixer Group** on Assets/Resources/Voice/VivoxVoiceSettings.asset. Unity's 2D source output avoids a second distance rolloff; the voice manager applies the existing proximity gain and mute to the tap source. The local microphone is not tapped or reverberated before transmission.

See [VivoxEnvironmentReverb.md](VivoxEnvironmentReverb.md) for scope, fallback behavior and validation limits. Native Vivox positional output remains on its existing direct route.
