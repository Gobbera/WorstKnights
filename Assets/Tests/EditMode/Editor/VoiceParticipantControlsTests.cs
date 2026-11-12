using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class VoiceParticipantControlsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private Room originalRoom, room;
    private ClientState originalState;
    private bool originalOffline;
    private VivoxVoiceManager previousManager, manager;
    private GameObject managerRoot, menu, sourceRoot;
    private RoomVoiceSettingsPanel panel;
    private Player remote;

    [SetUp]
    public void SetUp()
    {
        originalRoom = PhotonNetwork.NetworkingClient.CurrentRoom;
        originalState = PhotonNetwork.NetworkingClient.State;
        originalOffline = PhotonNetwork.OfflineMode;
        typeof(PhotonNetwork).GetField("offlineMode", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        room = new Room("Local participant control fixture", new RoomOptions(), true);
        PhotonNetwork.NetworkingClient.CurrentRoom = room;
        typeof(LoadBalancingClient).GetField("state", Private).SetValue(PhotonNetwork.NetworkingClient, ClientState.Joined);
        room.StorePlayer(Player("You", 1, true));
        remote = Player("Alex", 2, false);
        room.StorePlayer(remote);
        room.StorePlayer(Player("Alex", 3, false));
        managerRoot = new GameObject("Inactive participant controls manager fixture");
        managerRoot.SetActive(false);
        manager = managerRoot.AddComponent<VivoxVoiceManager>();
        previousManager = VivoxVoiceManager.Instance;
        typeof(VivoxVoiceManager).GetProperty("Instance").SetValue(null, manager);
    }

    [TearDown]
    public void TearDown()
    {
        typeof(VivoxVoiceManager).GetProperty("Instance").SetValue(null, previousManager);
        if (menu != null) Object.DestroyImmediate(menu);
        if (sourceRoot != null) Object.DestroyImmediate(sourceRoot);
        if (managerRoot != null) Object.DestroyImmediate(managerRoot);
        PhotonNetwork.NetworkingClient.CurrentRoom = originalRoom;
        typeof(LoadBalancingClient).GetField("state", Private).SetValue(PhotonNetwork.NetworkingClient, originalState);
        typeof(PhotonNetwork).GetField("offlineMode", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, originalOffline);
        managerRoot = menu = sourceRoot = null;
    }

    [Test]
    public void PendingActorChoicesAttachToThePublishedVoiceIdentityAndSurviveReconnect()
    {
        var preferences = new VoiceListeningPreferences();
        preferences.Set(2, null, new VoiceListeningPreference(0.35f, true));
        Assert.That(preferences.Get(2, "voice-a").Muted, Is.True);
        preferences.ResetRoomActors();
        var restored = preferences.Get(8, "voice-a");
        Assert.That(restored.Volume, Is.EqualTo(0.35f));
        Assert.That(restored.Muted, Is.True);
    }

    [Test]
    public void AnUneditedWaitingRowCannotOverwriteAKnownIdentityPreference()
    {
        var preferences = new VoiceListeningPreferences();
        preferences.Set(2, "voice-a", new VoiceListeningPreference(0.2f, true));
        preferences.ResetRoomActors();
        Assert.That(preferences.Get(4, null).Muted, Is.False);
        Assert.That(preferences.Get(4, "voice-a").Muted, Is.True);
        Assert.That(preferences.Get(4, "voice-a").Volume, Is.EqualTo(0.2f));
    }

    [Test]
    public void DifferentListenersAndReusedActorsDoNotShareChoices()
    {
        var first = new VoiceListeningPreferences();
        var second = new VoiceListeningPreferences();
        first.Set(2, "voice-a", new VoiceListeningPreference(0.1f, true));
        Assert.That(second.Get(2, "voice-a").Volume, Is.EqualTo(1f));
        Assert.That(second.Get(2, "voice-a").Muted, Is.False);
        first.RemoveActor(2);
        Assert.That(first.Get(2, "voice-b").Muted, Is.False);
        Assert.That(first.Get(2, "voice-b").Volume, Is.EqualTo(1f));
    }

    [TestCase(-6, 0.5f, 0.2505936f, -12)]
    [TestCase(-20, 0.25f, 0.025f, -32)]
    [TestCase(0, 1f, 1f, 0)]
    public void ListenerGainIsAppliedAfterDistanceGainOnce(int distanceDb, float userVolume, float amplitude, int sdkDb)
    {
        var mix = new VoiceListeningMix(distanceDb, false, new VoiceListeningPreference(userVolume, false));
        Assert.That(mix.UnityVolume, Is.EqualTo(amplitude).Within(0.00001f));
        Assert.That(mix.SdkVolume, Is.EqualTo(sdkDb));
        Assert.That(mix.Muted, Is.False);
    }

    [Test]
    public void ManualMuteCannotBeUndoneByDistanceResetAndUnmuteCannotBypassRange()
    {
        var muted = new VoiceListeningPreference(0.4f, true);
        Assert.That(new VoiceListeningMix(-50, true, muted).Muted, Is.True);
        Assert.That(new VoiceListeningMix(0, false, muted).Muted, Is.True);
        var unmuted = new VoiceListeningPreference(0.4f, false);
        Assert.That(new VoiceListeningMix(-50, true, unmuted).Muted, Is.True);
        Assert.That(new VoiceListeningMix(0, false, unmuted).Muted, Is.False);
    }

    [Test]
    public void ZeroVolumeIsSilentAndRestoringTheSliderPreservesManualMute()
    {
        var zero = new VoiceListeningMix(0, false, new VoiceListeningPreference(0f, false));
        Assert.That(zero.Muted, Is.True);
        Assert.That(zero.UnityVolume, Is.Zero);
        Assert.That(new VoiceListeningMix(0, false, new VoiceListeningPreference(1f, true)).Muted, Is.True);
    }

    [TestCase(-1f, 0f)]
    [TestCase(2f, 1f)]
    [TestCase(float.NaN, 1f)]
    public void InvalidVolumesCannotAmplifyOrCorruptPlayback(float requested, float expected)
    {
        Assert.That(new VoiceListeningPreference(requested, false).Volume, Is.EqualTo(expected));
    }

    [Test]
    public void RoomRosterUsesActorIdentityIncludesWaitingMembersAndExcludesSelf()
    {
        var entries = Snapshot();
        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries[0].ActorNumber, Is.EqualTo(2));
        Assert.That(entries[1].ActorNumber, Is.EqualTo(3));
        Assert.That(entries[0].DisplayName, Is.EqualTo(entries[1].DisplayName));
        Assert.That(entries[0].VoiceConnected, Is.False);
        Assert.That(manager.SetParticipantVolume(1, 0f), Is.False);
        Assert.That(manager.SetParticipantMuted(999, true), Is.False);
    }

    [Test]
    public void RoomControlsFollowDelayedIdentityAndRetainValuesAfterVoiceStateReset()
    {
        Assert.That(manager.SetParticipantVolume(2, 0.35f), Is.True);
        manager.SetParticipantMuted(2, true);
        remote.CustomProperties["kwkVivoxPlayerId"] = "voice-a";
        Snapshot();
        Invoke(manager, "ClearProximityMixState");
        Assert.That(Snapshot()[0].Listening.Volume, Is.EqualTo(0.35f));
        Assert.That(Snapshot()[0].Listening.Muted, Is.True);
        manager.SetParticipantMuted(2, false);
        Assert.That(Snapshot()[0].Listening.Volume, Is.EqualTo(0.35f));
    }

    [Test]
    public void RejoiningWithANewActorRestoresKnownIdentityWithoutLeakingToAnotherPlayer()
    {
        remote.CustomProperties["kwkVivoxPlayerId"] = "voice-a";
        manager.SetParticipantVolume(2, 0.45f); manager.SetParticipantMuted(2, true);
        manager.OnLeftRoom();
        room.Players.Remove(2);
        var newPlayer = Player("Another player", 2, false);
        newPlayer.CustomProperties["kwkVivoxPlayerId"] = "voice-b";
        room.StorePlayer(newPlayer);
        var rejoined = Player("Alex", 5, false);
        rejoined.CustomProperties["kwkVivoxPlayerId"] = "voice-a";
        room.StorePlayer(rejoined);
        var entries = Snapshot();
        Assert.That(entries[0].Listening.Muted, Is.False);
        Assert.That(entries[2].Listening.Muted, Is.True);
        Assert.That(entries[2].Listening.Volume, Is.EqualTo(0.45f));
    }

    [Test]
    public void ListenerControlsDoNotModifyCachedProximityOrAuthoredRange()
    {
        remote.CustomProperties["kwkVivoxPlayerId"] = "voice-a";
        var distanceVolumes = Field<Dictionary<string, int>>(manager, "participantVolumeByPlayerId");
        distanceVolumes["voice-a"] = -20;
        manager.SetParticipantVolume(2, 0.2f); manager.SetParticipantMuted(2, true);
        Assert.That(distanceVolumes["voice-a"], Is.EqualTo(-20));
        var settings = Resources.Load<VivoxVoiceSettings>("Voice/VivoxVoiceSettings");
        Assert.That(settings.AudibleDistance, Is.EqualTo(32));
        Assert.That(settings.ConversationalDistance, Is.EqualTo(1));
    }

    [Test]
    public void UnityTapGainAndMutePreserveTheConfiguredReverbSend()
    {
        sourceRoot = new GameObject("Listening mix source");
        var source = sourceRoot.AddComponent<AudioSource>();
        source.reverbZoneMix = 0.7f; source.bypassReverbZones = false;
        InvokeStatic("ApplyUnityVoiceSourceListenerMix", source, -20, false, new VoiceListeningPreference(0.4f, false));
        Assert.That(source.volume, Is.EqualTo(0.04f).Within(0.00001f));
        Assert.That(source.mute, Is.False);
        InvokeStatic("ApplyUnityVoiceSourceListenerMix", source, -20, false, new VoiceListeningPreference(0.4f, true));
        Assert.That(source.mute, Is.True);
        Assert.That(source.reverbZoneMix, Is.EqualTo(0.7f));
        Assert.That(source.bypassReverbZones, Is.False);
    }

    [Test]
    public void MissingRoomMappingCannotReuseAStaleAudibleDistanceGain()
    {
        typeof(VivoxVoiceManager).GetField("activeVoiceMode", Private).SetValue(manager, VivoxVoiceMode.Positional3D);
        Field<Dictionary<string, int>>(manager, "participantVolumeByPlayerId")["voice-a"] = -10;
        object[] state = { "voice-a", 0, false };
        MethodInfo mix = typeof(VivoxVoiceManager).GetMethod("GetParticipantProximityMix", Private);
        mix.Invoke(manager, state);
        Assert.That((bool)state[2], Is.True);
        remote.CustomProperties["kwkVivoxPlayerId"] = "voice-a";
        Invoke(manager, "RefreshListeningIdentities");
        mix.Invoke(manager, state);
        Assert.That((bool)state[2], Is.False);
        Assert.That((int)state[1], Is.EqualTo(-10));
        room.Players.Remove(2);
        Invoke(manager, "RefreshListeningIdentities");
        mix.Invoke(manager, state);
        Assert.That((bool)state[2], Is.True);
    }

    [Test]
    public void UiCallbacksAffectOnlyTheirActorAndRefreshDoesNotReplaceDraggedRows()
    {
        CreatePanel();
        var rows = Field<Dictionary<int, VoiceParticipantRow>>(panel, "rows");
        var first = rows[2];
        Field<Slider>(first, "volumeSlider").value = 0.32f;
        Field<Button>(first, "muteButton").onClick.Invoke();
        var entries = Snapshot();
        Assert.That(entries[0].Listening.Volume, Is.EqualTo(0.32f).Within(0.001f));
        Assert.That(entries[0].Listening.Muted, Is.True);
        Assert.That(entries[1].Listening.Muted, Is.False);
        Assert.That(entries[1].Listening.Volume, Is.EqualTo(1f));
        panel.RefreshForRoom();
        Assert.That(rows[2], Is.SameAs(first));
        Assert.That(Field<TMP_Text>(first, "muteLabel").text, Is.EqualTo("Unmute"));
        Assert.That(Field<TMP_Text>(first, "playerName").richText, Is.False);
        Assert.That(RoomVoiceSettingsPanel.EnsureForMenu(menu.GetComponent<SettingsMenu>()), Is.SameAs(panel));
    }

    [Test]
    public void LeavingPlayersDisappearAndAFullRoomCanScroll()
    {
        for (int actor = 4; actor <= 16; actor++) room.StorePlayer(Player($"Player {actor}", actor, false));
        CreatePanel();
        var rows = Field<Dictionary<int, VoiceParticipantRow>>(panel, "rows");
        Assert.That(rows.Count, Is.EqualTo(15));
        var scroll = Field<ScrollRect>(panel, "scroll");
        Assert.That(scroll.vertical, Is.True);
        Assert.That(scroll.verticalScrollbar.gameObject.activeSelf, Is.True);
        room.Players.Remove(2); manager.OnPlayerLeftRoom(remote); panel.RefreshForRoom();
        Assert.That(rows.ContainsKey(2), Is.False);
        Assert.That(rows.Count, Is.EqualTo(14));
    }

    [Test]
    public void SliderThumbFitsItsRowAndVoiceModeCaptionDoesNotWrap()
    {
        CreatePanel();
        VoiceSettingsPanel.EnsureForMenu(menu.GetComponent<SettingsMenu>());
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel.GetComponent<RectTransform>());
        var row = Field<Dictionary<int, VoiceParticipantRow>>(panel, "rows")[2];
        var slider = Field<Slider>(row, "volumeSlider");
        Assert.That(slider.handleRect.rect.height, Is.LessThanOrEqualTo(row.GetComponent<RectTransform>().rect.height));
        Assert.That(slider.fillRect.parent, Is.Not.SameAs(slider.transform));
        var dropdown = Field<TMP_Dropdown>(menu.GetComponent<VoiceSettingsPanel>(), "modeDropdown");
        Assert.That(dropdown.captionText.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));
    }

    [Test]
    public void RoomListHidesOutsideARoomAndShowsAnEmptyStateForASoloPlayer()
    {
        room.Players.Remove(2); room.Players.Remove(3);
        CreatePanel();
        Assert.That(Field<GameObject>(panel, "emptyMessage").activeSelf, Is.True);
        PhotonNetwork.NetworkingClient.CurrentRoom = null;
        typeof(LoadBalancingClient).GetField("state", Private).SetValue(PhotonNetwork.NetworkingClient, originalState);
        panel.RefreshForRoom();
        Assert.That(panel.GetComponent<CanvasGroup>().alpha, Is.Zero);
        Assert.That(panel.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
    }

    private void CreatePanel()
    {
        menu = Object.Instantiate(Resources.Load<GameObject>("UI/InGameSettingsMenu"));
        panel = RoomVoiceSettingsPanel.EnsureForMenu(menu.GetComponent<SettingsMenu>());
        Assert.That(panel, Is.Not.Null);
        panel.RefreshForRoom();
    }

    private List<RoomVoiceParticipant> Snapshot()
    {
        var list = new List<RoomVoiceParticipant>(); manager.CopyRoomVoiceParticipants(list); return list;
    }

    private static Player Player(string name, int actor, bool local)
        => (Player)Activator.CreateInstance(typeof(Player), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { name, actor, local }, null);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void InvokeStatic(string name, params object[] args)
        => typeof(VivoxVoiceManager).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
}
