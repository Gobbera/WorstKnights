using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct VoiceListeningPreference
{
    public float Volume { get; }
    public bool Muted { get; }
    public static VoiceListeningPreference Default => new VoiceListeningPreference(1f, false);

    public VoiceListeningPreference(float volume, bool muted)
    {
        Volume = float.IsNaN(volume) ? 1f : Mathf.Clamp01(volume);
        Muted = muted;
    }
}

/// <summary>Private to one listener; known voice identities survive room/voice reconnects.</summary>
public sealed class VoiceListeningPreferences
{
    private readonly Dictionary<int, VoiceListeningPreference> actorOverrides = new Dictionary<int, VoiceListeningPreference>();
    private readonly Dictionary<string, VoiceListeningPreference> identityOverrides =
        new Dictionary<string, VoiceListeningPreference>(StringComparer.Ordinal);

    public VoiceListeningPreference Get(int actorNumber, string voiceIdentity)
    {
        if (actorOverrides.TryGetValue(actorNumber, out VoiceListeningPreference preference))
        {
            if (!string.IsNullOrWhiteSpace(voiceIdentity)) identityOverrides[voiceIdentity] = preference;
            return preference;
        }
        return GetForIdentity(voiceIdentity);
    }

    public VoiceListeningPreference GetForIdentity(string voiceIdentity)
    {
        return !string.IsNullOrWhiteSpace(voiceIdentity)
            && identityOverrides.TryGetValue(voiceIdentity, out VoiceListeningPreference preference)
            ? preference : VoiceListeningPreference.Default;
    }

    public void Set(int actorNumber, string voiceIdentity, VoiceListeningPreference preference)
    {
        actorOverrides[actorNumber] = preference;
        if (!string.IsNullOrWhiteSpace(voiceIdentity)) identityOverrides[voiceIdentity] = preference;
    }

    public void RemoveActor(int actorNumber) { actorOverrides.Remove(actorNumber); }
    public void ResetRoomActors() { actorOverrides.Clear(); }
}

public readonly struct VoiceListeningMix
{
    public float UnityVolume { get; }
    public int SdkVolume { get; }
    public bool Muted { get; }

    public VoiceListeningMix(int proximityDb, bool proximityMuted, VoiceListeningPreference preference)
    {
        int baseDb = Mathf.Clamp(proximityDb, -50, 0);
        UnityVolume = Mathf.Pow(10f, baseDb / 20f) * preference.Volume;
        SdkVolume = preference.Volume <= 0f ? -50
            : Mathf.Clamp(Mathf.RoundToInt(baseDb + 20f * Mathf.Log10(preference.Volume)), -50, 0);
        Muted = proximityMuted || preference.Muted || preference.Volume <= 0f;
    }
}

public readonly struct RoomVoiceParticipant
{
    public int ActorNumber { get; }
    public string DisplayName { get; }
    public bool VoiceConnected { get; }
    public bool Reconnecting { get; }
    public VoiceListeningPreference Listening { get; }

    public RoomVoiceParticipant(int actorNumber, string displayName, bool voiceConnected,
        bool reconnecting, VoiceListeningPreference listening)
    {
        ActorNumber = actorNumber;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Player {actorNumber}" : displayName;
        VoiceConnected = voiceConnected;
        Reconnecting = reconnecting;
        Listening = listening;
    }
}
