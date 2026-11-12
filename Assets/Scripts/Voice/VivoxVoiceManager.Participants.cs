using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using Unity.Services.Vivox;

public sealed partial class VivoxVoiceManager
{
    private readonly VoiceListeningPreferences listeningPreferences = new VoiceListeningPreferences();
    private readonly Dictionary<VivoxParticipant, VoiceListeningMix> appliedParticipantMixes =
        new Dictionary<VivoxParticipant, VoiceListeningMix>();
    private readonly Dictionary<string, int> voiceActorsByIdentity = new Dictionary<string, int>(StringComparer.Ordinal);

    public void CopyRoomVoiceParticipants(List<RoomVoiceParticipant> destination)
    {
        destination.Clear();
        if (!PhotonNetwork.InRoom) return;

        System.Collections.ObjectModel.ReadOnlyCollection<VivoxParticipant> voices = null;
        if (IsVoiceConnected && TryGetVivoxService(out IVivoxService service))
            service.ActiveChannels.TryGetValue(activeChannelName, out voices);

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player == null || player.IsLocal) continue;
            string identity = GetPlayerVoiceIdentity(player);
            bool connected = false;
            if (voices != null && !player.IsInactive)
                for (int i = 0; i < voices.Count; i++)
                    if (TryResolveRemoteParticipantPlayerId(voices[i], out string id)
                        && string.Equals(id, identity, StringComparison.Ordinal)) { connected = true; break; }

            destination.Add(new RoomVoiceParticipant(player.ActorNumber, player.NickName, connected,
                player.IsInactive, listeningPreferences.Get(player.ActorNumber, identity)));
        }
        destination.Sort((left, right) => left.ActorNumber.CompareTo(right.ActorNumber));
    }

    public bool SetParticipantVolume(int actorNumber, float volume)
    {
        Player player = FindRemoteRoomPlayer(actorNumber);
        if (player == null) return false;
        string identity = GetPlayerVoiceIdentity(player);
        var previous = listeningPreferences.Get(actorNumber, identity);
        listeningPreferences.Set(actorNumber, identity, new VoiceListeningPreference(volume, previous.Muted));
        RefreshParticipantListening();
        return true;
    }

    public bool SetParticipantMuted(int actorNumber, bool muted)
    {
        Player player = FindRemoteRoomPlayer(actorNumber);
        if (player == null) return false;
        string identity = GetPlayerVoiceIdentity(player);
        var previous = listeningPreferences.Get(actorNumber, identity);
        listeningPreferences.Set(actorNumber, identity, new VoiceListeningPreference(previous.Volume, muted));
        RefreshParticipantListening();
        return true;
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);
        if (otherPlayer != null)
        {
            voiceRangeReceiver.Remove(otherPlayer.ActorNumber);
            remoteVoiceAnchors.Remove(otherPlayer.ActorNumber);
        }
        if (otherPlayer != null && !otherPlayer.IsInactive) listeningPreferences.RemoveActor(otherPlayer.ActorNumber);
    }

    private static Player FindRemoteRoomPlayer(int actorNumber)
    {
        if (!PhotonNetwork.InRoom) return null;
        foreach (Player player in PhotonNetwork.PlayerList)
            if (player != null && !player.IsLocal && player.ActorNumber == actorNumber) return player;
        return null;
    }

    private static string GetPlayerVoiceIdentity(Player player)
    {
        return player.CustomProperties.TryGetValue(PhotonVivoxPlayerIdProperty, out object value)
            && value is string identity && !string.IsNullOrWhiteSpace(identity) ? identity : null;
    }

    private VoiceListeningPreference GetParticipantListeningPreference(string voiceIdentity)
    {
        return voiceActorsByIdentity.TryGetValue(voiceIdentity, out int actor)
            ? listeningPreferences.Get(actor, voiceIdentity)
            : listeningPreferences.GetForIdentity(voiceIdentity);
    }

    private void RefreshListeningIdentities()
    {
        voiceActorsByIdentity.Clear();
        if (!PhotonNetwork.InRoom) return;
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player == null || player.IsLocal) continue;
            string identity = GetPlayerVoiceIdentity(player);
            if (identity != null) voiceActorsByIdentity[identity] = player.ActorNumber;
        }
    }

    private void RefreshParticipantListening()
    {
        RefreshListeningIdentities();
        UpdateParticipantListeningMixes();
        UpdateUnityVoiceOutputs();
    }

    private void UpdateParticipantListeningMixes()
    {
        if (!IsVoiceConnected || !TryGetVivoxService(out IVivoxService service)
            || !service.ActiveChannels.TryGetValue(activeChannelName, out var participants)) return;

        foreach (VivoxParticipant participant in participants)
            if (TryResolveRemoteParticipantPlayerId(participant, out string id))
                ApplyListenerMixToParticipant(participant, id);
    }

    private void GetParticipantProximityMix(string playerId, out int volume, out bool muted)
    {
        bool simulated = activeVoiceMode == VivoxVoiceMode.Positional3D
            && !ShouldUseNativeVivoxPositionalAudio(activeVoiceMode);
        volume = MaximumVivoxLocalVolume;
        bool ready = !simulated || (voiceActorsByIdentity.ContainsKey(playerId)
            && participantVolumeByPlayerId.TryGetValue(playerId, out volume));
        if (!ready) volume = MinimumVivoxLocalVolume;
        muted = !ready || proximityMutedPlayerIds.Contains(playerId);
    }

    private void ApplyListenerMixToParticipant(VivoxParticipant participant, string playerId)
    {
        GetParticipantProximityMix(playerId, out int proximityVolume, out bool proximityMuted);
        var mix = new VoiceListeningMix(proximityVolume, proximityMuted, GetParticipantListeningPreference(playerId));
        try
        {
            bool known = appliedParticipantMixes.TryGetValue(participant, out VoiceListeningMix previous);
            if (!known || previous.Muted != mix.Muted)
            {
                if (mix.Muted) participant.MutePlayerLocally();
                else participant.UnmutePlayerLocally();
            }
            if (!known || previous.SdkVolume != mix.SdkVolume) participant.SetLocalVolume(mix.SdkVolume);
            appliedParticipantMixes[participant] = mix;
        }
        catch (InvalidOperationException)
        {
            appliedParticipantMixes.Remove(participant);
            ClearParticipantMixState(playerId);
        }
        catch (NullReferenceException)
        {
            appliedParticipantMixes.Remove(participant);
            ClearParticipantMixState(playerId);
        }
    }
}
