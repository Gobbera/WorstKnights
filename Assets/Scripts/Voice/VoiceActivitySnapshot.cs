using System;
using UnityEngine;

public enum VoiceActivityState
{
    Disconnected,
    Disabled,
    Muted,
    Idle,
    Speaking
}

/// <summary>A local input observation, independent of received audio and listener volume.</summary>
public readonly struct VoiceActivitySnapshot
{
    public VoiceActivityState State { get; }
    // Latest SDK energy. VoiceIntensityMonitor calibrates and smooths this sender observation.
    public float AudioEnergy { get; }
    public bool IsSpeaking => State == VoiceActivityState.Speaking;

    private VoiceActivitySnapshot(VoiceActivityState state, float audioEnergy)
    {
        State = state;
        AudioEnergy = audioEnergy;
    }

    public static VoiceActivitySnapshot FromInput(bool voiceEnabled, bool connected,
        bool hasLocalParticipant, bool inputMuted, bool transmitting, bool speechDetected, double energy)
    {
        if (!voiceEnabled)
            return new VoiceActivitySnapshot(VoiceActivityState.Disabled, 0f);
        if (!connected || !hasLocalParticipant)
            return default;
        if (inputMuted || !transmitting)
            return new VoiceActivitySnapshot(VoiceActivityState.Muted, 0f);

        float safeEnergy = double.IsNaN(energy) || double.IsInfinity(energy)
            ? 0f : (float)Math.Max(0d, Math.Min(1d, energy));
        return new VoiceActivitySnapshot(speechDetected ? VoiceActivityState.Speaking : VoiceActivityState.Idle,
            Mathf.Clamp01(safeEnergy));
    }
}
