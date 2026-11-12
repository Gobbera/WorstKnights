using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public sealed partial class VivoxVoiceManager : IOnEventCallback
{
    public const byte VoiceRangeEventCode = 74;
    private static readonly RaiseEventOptions VoiceRangeEventOptions = new RaiseEventOptions
    { Receivers = ReceiverGroup.Others, CachingOption = EventCaching.DoNotCache };
    private readonly VoiceRangePublisher voiceRangePublisher = new VoiceRangePublisher();
    private readonly VoiceRangeReceiver voiceRangeReceiver = new VoiceRangeReceiver();
    private readonly struct RemoteVoiceAnchor
    {
        public readonly Transform Root, Anchor;
        public RemoteVoiceAnchor(Transform root, Transform anchor) { Root = root; Anchor = anchor; }
        public bool IsValid => Root != null && Root.gameObject.activeInHierarchy && Anchor != null;
    }
    private readonly Dictionary<int, RemoteVoiceAnchor> remoteVoiceAnchors = new Dictionary<int, RemoteVoiceAnchor>();
    private float nextRemoteVoiceAnchorRefresh;

    public bool SupportsDynamicVoiceRange => ShouldUseDynamicVoiceRange(settings, CurrentMode);
    public bool UsesDynamicVoiceRange => SupportsDynamicVoiceRange && intensityMonitor.Profile.IsCalibrated;
    public float LocalVoiceRangeLimit => settings == null ? 0f : UsesDynamicVoiceRange
        ? VoiceDynamicRange.Distance(settings, intensityMonitor.Intensity)
        : SupportsDynamicVoiceRange ? VoiceDynamicRange.NormalDistance(settings) : settings.AudibleDistance;

    private static bool ShouldUseDynamicVoiceRange(VivoxVoiceSettings configuration, VivoxVoiceMode mode)
        => configuration != null && configuration.DynamicProximity && mode == VivoxVoiceMode.Positional3D
            && !configuration.UseNativeVivoxPositionalAudio;

    private void UpdateVoiceRangeNetwork(float deltaTime)
    {
        voiceRangeReceiver.Update(Time.unscaledTime, deltaTime);
        if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || applicationIsQuitting
            || !ShouldUseDynamicVoiceRange(settings, CurrentMode)) return;
        float intensity = IsVoiceConnected && IsMicrophoneTransmissionAllowed && intensityMonitor.InputAvailable
            ? intensityMonitor.Intensity : 0f;
        if (voiceRangePublisher.TryCreate(Time.unscaledTime, PhotonNetwork.ServerTimestamp,
            intensityMonitor.Profile.IsCalibrated, intensity, out var packet))
            PhotonNetwork.RaiseEvent(VoiceRangeEventCode, packet.Encode(), VoiceRangeEventOptions,
                new SendOptions { Reliability = false });
    }

    private void PublishSuppressedVoiceRange()
    {
        if (!voiceRangePublisher.HasAudibleSignal || !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || applicationIsQuitting) return;
        if (voiceRangePublisher.TryCreate(Time.unscaledTime, PhotonNetwork.ServerTimestamp, true, 0f,
            out var packet, force: true))
            PhotonNetwork.RaiseEvent(VoiceRangeEventCode, packet.Encode(), VoiceRangeEventOptions,
                new SendOptions { Reliability = false });
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent == null || photonEvent.Code != VoiceRangeEventCode || !PhotonNetwork.InRoom
            || photonEvent.Sender == PhotonNetwork.LocalPlayer.ActorNumber) return;
        Player sender = PhotonNetwork.CurrentRoom.GetPlayer(photonEvent.Sender);
        if (sender == null || sender.IsLocal || sender.IsInactive) return;
        int serverNow = PhotonNetwork.ServerTimestamp;
        if (!VoiceRangePacket.TryDecode(photonEvent.CustomData, serverNow, out var packet)) return;
        float age = Mathf.Max(0, unchecked(serverNow - packet.Timestamp)) * 0.001f;
        voiceRangeReceiver.Receive(sender.ActorNumber, packet, Time.unscaledTime, age);
    }

    private float ResolveRemoteAudibleDistance(string identity)
    {
        if (ShouldUseDynamicVoiceRange(settings, activeVoiceMode))
            return voiceActorsByIdentity.TryGetValue(identity, out int actor)
                && voiceRangeReceiver.TryGetIntensity(actor, out float intensity)
                ? VoiceDynamicRange.Distance(settings, intensity) : VoiceDynamicRange.NormalDistance(settings);
        return settings.AudibleDistance;
    }

    private void ResetDynamicVoiceRoomState()
    {
        voiceRangePublisher.Reset(); voiceRangeReceiver.Clear(); ClearRemoteVoiceAnchorCache();
    }

    private void ClearRemoteVoiceAnchorCache()
    { remoteVoiceAnchors.Clear(); nextRemoteVoiceAnchorRefresh = 0f; }

    private bool TryGetCachedRemoteVoiceAnchor(int actor, out Transform anchor)
    {
        anchor = null;
        if (remoteVoiceAnchors.TryGetValue(actor, out var cached) && cached.IsValid)
        { anchor = cached.Anchor; return true; }
        if (Time.unscaledTime >= nextRemoteVoiceAnchorRefresh)
        {
            nextRemoteVoiceAnchorRefresh = Time.unscaledTime + 0.5f;
            remoteVoiceAnchors.Clear();
            foreach (var player in FindObjectsByType<PlayerSetup>(FindObjectsInactive.Exclude))
            {
                var view = player.GetComponent<PhotonView>();
                if (view == null || view.OwnerActorNr <= 0) continue;
                var camera = player.GetComponentInChildren<FP_Camera>(true);
                remoteVoiceAnchors[view.OwnerActorNr] = new RemoteVoiceAnchor(player.transform,
                    camera != null ? camera.transform : player.transform);
            }
        }
        if (!remoteVoiceAnchors.TryGetValue(actor, out cached) || !cached.IsValid) return false;
        anchor = cached.Anchor; return true;
    }
}
