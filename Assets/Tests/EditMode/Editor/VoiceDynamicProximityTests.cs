using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using ExitGames.Client.Photon;
using NUnit.Framework;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class VoiceDynamicProximityTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> objects = new List<Object>();
    private Room previousRoom, room;
    private ClientState previousState;
    private bool previousOffline;
    private VivoxVoiceManager manager, previousManager;
    private VivoxVoiceSettings settings;
    private Player remote;

    [SetUp]
    public void SetUp()
    {
        previousRoom = PhotonNetwork.NetworkingClient.CurrentRoom;
        previousState = PhotonNetwork.NetworkingClient.State; previousOffline = PhotonNetwork.OfflineMode;
        typeof(PhotonNetwork).GetField("offlineMode", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        room = new Room("Voice range fixture", new RoomOptions(), true);
        PhotonNetwork.NetworkingClient.CurrentRoom = room;
        typeof(LoadBalancingClient).GetField("state", Private).SetValue(PhotonNetwork.NetworkingClient, ClientState.Joined);
        room.StorePlayer(Player(1, true)); remote = Player(2, false); room.StorePlayer(remote);
        remote.CustomProperties["kwkVivoxPlayerId"] = "range-speaker";
        settings = Object.Instantiate(Resources.Load<VivoxVoiceSettings>("Voice/VivoxVoiceSettings")); objects.Add(settings);
        var root = Add(new GameObject("Inactive voice range fixture")); root.SetActive(false);
        manager = root.AddComponent<VivoxVoiceManager>();
        Set(manager, "settings", settings); Set(manager, "activeVoiceMode", VivoxVoiceMode.Positional3D);
        previousManager = VivoxVoiceManager.Instance;
        typeof(VivoxVoiceManager).GetProperty("Instance").SetValue(null, manager);
        Call(manager, "RefreshListeningIdentities");
    }

    [TearDown]
    public void TearDown()
    {
        typeof(VivoxVoiceManager).GetProperty("Instance").SetValue(null, previousManager);
        for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
        PhotonNetwork.NetworkingClient.CurrentRoom = previousRoom;
        typeof(LoadBalancingClient).GetField("state", Private).SetValue(PhotonNetwork.NetworkingClient, previousState);
        typeof(PhotonNetwork).GetField("offlineMode", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, previousOffline);
    }

    [Test]
    public void QuietAndNormalShareThirtyMetresWhileLoudCanReachFifty()
    {
        float quiet = VoiceDynamicRange.Distance(settings, 0.25f), normal = VoiceDynamicRange.Distance(settings, 0.6f);
        Assert.That(quiet, Is.EqualTo(30f)); Assert.That(normal, Is.EqualTo(quiet));
        Assert.That(VoiceDynamicRange.Distance(settings, 1f), Is.EqualTo(50f));
        Assert.That(VoiceDynamicRange.Distance(settings, 0f), Is.EqualTo(30f));
    }

    [TestCase(float.NaN, 30f)] [TestCase(float.PositiveInfinity, 30f)]
    [TestCase(-1f, 30f)] [TestCase(2f, 50f)]
    public void InvalidIntensityCannotCorruptOrExtendTheRange(float value, float expected)
        => Assert.That(VoiceDynamicRange.Distance(settings, value), Is.EqualTo(expected));

    [Test]
    public void NormalRangeCannotExceedMaximumOrShrinkBelowConversation()
    {
        Set(settings, "normalVoiceDistance", 100f);
        Assert.That(VoiceDynamicRange.Distance(settings, 0f), Is.EqualTo(50f));
        Set(settings, "normalVoiceDistance", 0f);
        Assert.That(VoiceDynamicRange.Distance(settings, 0f), Is.GreaterThan(settings.ConversationalDistance));
    }

    [TestCase(0.8f, 30f)] [TestCase(0.82f, 30f)]
    [TestCase(0.91f, 40f)] [TestCase(1f, 50f)]
    public void OnlyTheLoudBandExpandsRangeWithASmoothOnset(float intensity, float expected)
        => Assert.That(VoiceDynamicRange.Distance(settings, intensity), Is.EqualTo(expected).Within(0.0001f));

    [Test]
    public void NormalAndLoudFadeReachTheirAdvertisedCutoffs()
    {
        float normal = VoiceDynamicRange.Distance(settings, 0.6f), loud = VoiceDynamicRange.Distance(settings, 1f);
        Assert.That(VoiceDynamicRange.Fade(settings, 25f, normal), Is.GreaterThan(0f));
        Assert.That(VoiceDynamicRange.Fade(settings, 29f, normal), Is.GreaterThan(0f));
        Assert.That(VoiceDynamicRange.Fade(settings, 30f, normal), Is.Zero);
        Assert.That(VoiceDynamicRange.Fade(settings, 45f, loud), Is.GreaterThan(0f));
        Assert.That(VoiceDynamicRange.Fade(settings, 49f, loud), Is.GreaterThan(0f));
        Assert.That(VoiceDynamicRange.Fade(settings, 50f, loud), Is.Zero);
    }

    [Test]
    public void DistanceTableRemainsMonotonicAndStopsAtTheDynamicBoundary()
    {
        float radius = VoiceDynamicRange.Distance(settings, 0.6f);
        Assert.That(VoiceDynamicRange.Fade(settings, 0.5f, radius), Is.EqualTo(1f));
        Assert.That(VoiceDynamicRange.Fade(settings, radius, radius), Is.Zero);
        Assert.That(VoiceDynamicRange.Fade(settings, radius + 1f, radius), Is.Zero);
        float previous = 1f;
        for (float distance = 1f; distance < radius; distance += 0.1f)
        { float value = VoiceDynamicRange.Fade(settings, distance, radius); Assert.That(value, Is.LessThanOrEqualTo(previous + 0.00001f)); previous = value; }
    }

    [TestCase(1f, 0, 0)]
    [TestCase(2f, -1, 0)]
    [TestCase(3f, -3, -1)]
    [TestCase(5f, -4, -1)]
    [TestCase(7f, -5, -2)]
    [TestCase(8f, -8, -3)]
    [TestCase(10f, -10, -4)]
    [TestCase(12f, -13, -4)]
    [TestCase(13f, -15, -5)]
    [TestCase(14f, -16, -5)]
    [TestCase(15f, -20, -6)]
    [TestCase(20f, -25, -9)]
    [TestCase(25f, -50, -10)]
    public void RequestedNormalAndLoudTableIsAppliedExactly(float distance,
        int expectedNormalDecibels, int expectedLoudDecibels)
    {
        float normal = VoiceDynamicRange.Fade(settings, distance, VoiceDynamicRange.NormalDistance(settings));
        float loud = VoiceDynamicRange.Fade(settings, distance, settings.AudibleDistance);
        Assert.That(VoiceDynamicRange.AmplitudeToDecibels(normal), Is.EqualTo(expectedNormalDecibels));
        Assert.That(VoiceDynamicRange.AmplitudeToDecibels(loud), Is.EqualTo(expectedLoudDecibels));
    }

    [TestCase(30f, -13)]
    [TestCase(35f, -15)]
    [TestCase(40f, -20)]
    [TestCase(45f, -25)]
    public void RequestedLoudOnlyTableIsAppliedExactly(float distance, int expectedDecibels)
    {
        float loud = VoiceDynamicRange.Fade(settings, distance, settings.AudibleDistance);
        Assert.That(VoiceDynamicRange.AmplitudeToDecibels(loud), Is.EqualTo(expectedDecibels));
    }

    [Test]
    public void MinusFiftyDecibelsRemainsAudibleUntilTheNormalCutoff()
    {
        float normalDistance = VoiceDynamicRange.NormalDistance(settings);
        float minusFifty = VoiceDynamicRange.Fade(settings, 25f, normalDistance);
        Assert.That(VoiceDynamicRange.AmplitudeToDecibels(minusFifty), Is.EqualTo(-50));
        Assert.That((bool)Static("ShouldMuteSimulatedProximity", minusFifty, 25f, normalDistance), Is.False);
        Assert.That((bool)Static("ShouldMuteSimulatedProximity", 0f, normalDistance, normalDistance), Is.True);
    }

    [TestCase(1f, 0)]
    [TestCase(0.5f, -6)]
    [TestCase(0.25f, -12)]
    [TestCase(0.1f, -20)]
    [TestCase(0.01f, -40)]
    [TestCase(0f, -50)]
    [TestCase(float.NaN, -50)]
    public void FadeAmplitudeUsesPerceptualDecibels(float amplitude, int expectedDecibels)
        => Assert.That(VoiceDynamicRange.AmplitudeToDecibels(amplitude), Is.EqualTo(expectedDecibels));

    [Test]
    public void PacketRoundTripAndTimestampWrapRetainTheSenderSignal()
    {
        int timestamp = int.MaxValue - 50, now = unchecked(timestamp + 100);
        var encoded = new VoiceRangePacket(true, 154, 65535, timestamp).Encode();
        Assert.That(encoded.Length, Is.EqualTo(9));
        Assert.That(VoiceRangePacket.TryDecode(encoded, now, out var decoded), Is.True);
        Assert.That(decoded.Sequence, Is.EqualTo(65535));
        Assert.That(decoded.Intensity, Is.EqualTo(154f / 255f));
        Assert.That(decoded.Timestamp, Is.EqualTo(timestamp));
    }

    [Test]
    public void MalformedExpiredAndFarFuturePacketsAreRejected()
    {
        foreach (object value in new object[] { null, "audio", new object[] { 1f }, new byte[3],
            new byte[] { 2, 1, 100, 0, 0, 0, 0, 0, 0 }, new byte[] { 1, 2, 100, 0, 0, 0, 0, 0, 0 } })
            Assert.That(VoiceRangePacket.TryDecode(value, 1000, out _), Is.False);
        Assert.That(VoiceRangePacket.TryDecode(new VoiceRangePacket(true, 255, 1, 1000).Encode(), 3001, out _), Is.False);
        Assert.That(VoiceRangePacket.TryDecode(new VoiceRangePacket(true, 255, 1, 3000).Encode(), 1000, out _), Is.False);
    }

    [Test]
    public void PublisherFiltersNoiseCapsUpdatesAndRefreshesUnchangedState()
    {
        var publisher = new VoiceRangePublisher();
        Assert.That(publisher.TryCreate(0f, 0, true, 0.6f, out var first), Is.True);
        Assert.That(publisher.TryCreate(0.1f, 100, true, 1f, out _), Is.False);
        Assert.That(publisher.TryCreate(0.2f, 200, true, 0.8f, out var second), Is.True);
        Assert.That(second.Sequence, Is.GreaterThan(first.Sequence));
        Assert.That(publisher.TryCreate(0.4f, 400, true, 0.801f, out _), Is.False);
        Assert.That(publisher.TryCreate(0.71f, 710, true, 0.8f, out _), Is.True);
        Assert.That(publisher.TryCreate(0.72f, 720, true, 0f, out var stopped, force: true), Is.True);
        Assert.That(stopped.Level, Is.Zero); Assert.That(publisher.HasAudibleSignal, Is.False);
        publisher.Reset();
        publisher.TryCreate(0.73f, 730, false, 1f, out var reconnected);
        Assert.That(reconnected.Sequence, Is.GreaterThan(stopped.Sequence));
        Assert.That(reconnected.Calibrated, Is.False); Assert.That(reconnected.Level, Is.Zero);
    }

    [Test]
    public void ReceiverRejectsDuplicatesAndReorderingAndAcceptsSequenceWrap()
    {
        var receiver = new VoiceRangeReceiver();
        Assert.That(receiver.Receive(2, new VoiceRangePacket(true, 50, 65535, 1000), 0f), Is.True);
        Assert.That(receiver.Receive(2, new VoiceRangePacket(true, 255, 65535, 1001), 0.1f), Is.False);
        Assert.That(receiver.Receive(2, new VoiceRangePacket(true, 255, 65534, 999), 0.1f), Is.False);
        Assert.That(receiver.Receive(2, new VoiceRangePacket(true, 150, 0, 1002), 0.1f), Is.True);
        Assert.That(receiver.Receive(2, new VoiceRangePacket(true, 255, 1, 1000), 0.2f), Is.False);
    }

    [TestCase(30)] [TestCase(60)] [TestCase(120)]
    public void LostMetadataDecaysTowardMinimumWithoutReenablingFixedRange(int fps)
    {
        var receiver = new VoiceRangeReceiver(); receiver.Receive(2, new VoiceRangePacket(true, 255, 1, 0), 0f);
        for (int i = 1; i <= fps * 5; i++) receiver.Update(i / (float)fps, 1f / fps);
        Assert.That(receiver.TryGetIntensity(2, out float intensity), Is.True);
        Assert.That(intensity, Is.LessThan(0.001f));
        Assert.That(VoiceDynamicRange.Distance(settings, intensity), Is.EqualTo(30f).Within(0.01f));
        receiver.Receive(3, new VoiceRangePacket(false, 255, 1, 0), 0f);
        Assert.That(receiver.TryGetIntensity(3, out _), Is.False);
    }

    [Test]
    public void DelayedPacketsDoNotReceiveAnotherFullFreshnessWindow()
    {
        var receiver = new VoiceRangeReceiver();
        receiver.Receive(2, new VoiceRangePacket(true, 255, 1, 0), 1.9f, 1.9f);
        receiver.Update(2.1f, 0.2f);
        Assert.That(receiver.TryGetIntensity(2, out float intensity), Is.True);
        Assert.That(intensity, Is.LessThan(0.8f));
    }

    [Test]
    public void RoomEventsOnlyUpdateKnownRemoteActiveActors()
    {
        Send(2, 0.25f); var receiver = Field<VoiceRangeReceiver>(manager, "voiceRangeReceiver");
        Assert.That(receiver.Count, Is.EqualTo(1));
        Send(1, 1f); Send(99, 1f);
        typeof(Player).GetProperty("IsInactive").SetValue(remote, true); Send(2, 1f, 2);
        Assert.That(receiver.Count, Is.EqualTo(1));
        Assert.That(receiver.TryGetIntensity(2, out float intensity), Is.True);
        Assert.That(intensity, Is.EqualTo(VoiceRangePacket.Quantize(0.25f) / 255f));
        manager.OnEvent(Event(2, "invalid"));
    }

    [Test]
    public void ListenersCannotChangeSenderRangeAndLeavingClearsTheActor()
    {
        Send(2, 0.25f);
        float range = (float)Call(manager, "ResolveRemoteAudibleDistance", "range-speaker");
        Assert.That(range, Is.EqualTo(30f));
        manager.SetParticipantVolume(2, 0.1f); manager.SetParticipantMuted(2, true);
        Assert.That((float)Call(manager, "ResolveRemoteAudibleDistance", "range-speaker"), Is.EqualTo(range));
        manager.OnPlayerLeftRoom(remote);
        Assert.That(Field<VoiceRangeReceiver>(manager, "voiceRangeReceiver").Count, Is.Zero);
        Assert.That((float)Call(manager, "ResolveRemoteAudibleDistance", "range-speaker"), Is.EqualTo(30f));
    }

    [Test]
    public void UncalibratedLegacyAndUnsupportedModesKeepTheFixedRange()
    {
        Assert.That((float)Call(manager, "ResolveRemoteAudibleDistance", "range-speaker"), Is.EqualTo(30f));
        Send(2, 1f, calibrated: false);
        Assert.That((float)Call(manager, "ResolveRemoteAudibleDistance", "range-speaker"), Is.EqualTo(30f));
        Send(2, 0.25f, 2); Set(settings, "dynamicProximity", false);
        Assert.That((float)Call(manager, "ResolveRemoteAudibleDistance", "range-speaker"), Is.EqualTo(50f));
        Set(settings, "dynamicProximity", true); Set(settings, "useNativeVivoxPositionalAudio", true);
        Assert.That(manager.SupportsDynamicVoiceRange, Is.False);
        Set(settings, "useNativeVivoxPositionalAudio", false); Set(manager, "activeVoiceMode", VivoxVoiceMode.NonPositional2D);
        Assert.That((float)Call(manager, "ResolveRemoteAudibleDistance", "range-speaker"), Is.EqualTo(50f));
        Call(manager, "ResetDynamicVoiceRoomState");
        Assert.That(Field<VoiceRangeReceiver>(manager, "voiceRangeReceiver").Count, Is.Zero);
    }

    [Test]
    public void RemoteInactiveCameraCanAnchorAnActivePlayerAndDestroyedPlayersAreNotReused()
    {
        var root = Add(new GameObject("Remote player anchor fixture")); root.SetActive(false);
        var view = root.AddComponent<PhotonView>(); typeof(PhotonView).GetProperty("OwnerActorNr").SetValue(view, 2);
        root.AddComponent<PlayerSetup>();
        var camera = new GameObject("Inactive remote camera"); camera.transform.SetParent(root.transform);
        camera.AddComponent<FP_Camera>(); camera.SetActive(false); root.SetActive(true);
        object[] args = { 2, null };
        Assert.That(Call(manager, "TryGetCachedRemoteVoiceAnchor", args), Is.True);
        Assert.That(args[1], Is.SameAs(camera.transform));
        root.SetActive(false); args[1] = null;
        Assert.That(Call(manager, "TryGetCachedRemoteVoiceAnchor", args), Is.False);
    }

    [TestCase(true, true, true, true, true)]
    [TestCase(false, true, true, true, false)] [TestCase(true, false, true, true, false)]
    [TestCase(true, true, false, true, false)] [TestCase(true, true, true, false, false)]
    public void MicrophonePlaybackRequiresAnOpenTestRequestedTapAndInput(bool open, bool requested, bool running, bool input, bool expected)
        => Assert.That(Static("ShouldPlayMicrophoneMonitor", open, requested, running, input), Is.EqualTo(expected));

    [Test]
    public void LocalMonitorIsDryTwoDimensionalAndUsesItsOwnModerateGain()
    {
        var source = Add(new GameObject("Microphone monitor source fixture")).AddComponent<AudioSource>();
        Static("ConfigureMicrophoneMonitorSource", source, settings);
        Assert.That(source.spatialBlend, Is.Zero); Assert.That(source.volume, Is.EqualTo(0.35f));
        Assert.That(source.mute, Is.True); Assert.That(source.bypassReverbZones, Is.True);
        Assert.That(source.bypassListenerEffects, Is.True); Assert.That(source.reverbZoneMix, Is.Zero);
        Assert.That(source.playOnAwake, Is.False); Assert.That(source.ignoreListenerPause, Is.True);
    }

    [Test]
    public void OfflineMonitoringCannotInitializeVoiceAndClosingTheTestClearsItsRequest()
    {
        var state = UnityServices.State; manager.SetMicrophoneTestOpen(true);
        Assert.That(manager.CanMonitorMicrophone, Is.False);
        Assert.That(manager.SetMicrophoneMonitoring(true), Is.False);
        var source = Add(new GameObject("Suppressed monitor cleanup fixture")).AddComponent<AudioSource>();
        Set(manager, "microphoneMonitorSource", source);
        typeof(VivoxVoiceManager).GetProperty("MicrophoneMonitorRequested").SetValue(manager, true);
        manager.SetMicrophoneTestOpen(false);
        Assert.That(manager.MicrophoneMonitorRequested, Is.False); Assert.That(source.mute, Is.True);
        Assert.That(UnityServices.State, Is.EqualTo(state));
    }

    [Test]
    public void TestPanelHasMonitoringControlsAndCloseClearsTheSession()
    {
        var state = UnityServices.State;
        var menu = Add(Object.Instantiate(Resources.Load<GameObject>("UI/InGameSettingsMenu")));
        var panel = VoiceIntensitySettingsPanel.EnsureForMenu(menu.GetComponent<SettingsMenu>()); panel.Open();
        TMP_Dropdown microphoneDropdown = Field<TMP_Dropdown>(panel, "microphoneDropdown");
        Assert.That(microphoneDropdown, Is.Not.Null);
        Assert.That(microphoneDropdown.options[0].text, Is.EqualTo("Voice unavailable"));
        Assert.That(microphoneDropdown.interactable, Is.False);
        Assert.That(Field<Button>(panel, "monitorButton"), Is.Not.Null);
        Assert.That(Field<Button>(panel, "monitorButton").interactable, Is.False);
        Assert.That(Field<TMP_Text>(panel, "monitorLabel").text, Is.EqualTo("Hear myself: Off"));
        Assert.That(Field<TMP_Text>(panel, "rangeLabel").text, Does.Contain("Join a room"));
        Assert.That(Field<bool>(manager, "microphoneTestOpen"), Is.True);
        Field<Button>(panel, "monitorButton").onClick.Invoke();
        panel.Close(); Assert.That(Field<bool>(manager, "microphoneTestOpen"), Is.False);
        Assert.That(manager.MicrophoneMonitorRequested, Is.False);
        Assert.That(UnityServices.State, Is.EqualTo(state));
    }

    [Test]
    public async Task OfflineMicrophoneSelectionDoesNotInitializeVivox()
    {
        ServicesInitializationState state = UnityServices.State;
        Assert.That(await manager.SetInputDeviceAsync("offline-device"), Is.False);
        Assert.That(manager.InputDevices, Is.Empty);
        Assert.That(UnityServices.State, Is.EqualTo(state));
    }

    [Test]
    public void SettingsTabsSeparateControlsVideoAudioAndVoipWithoutDuplicatingControls()
    {
        GameObject menuObject = Add(Object.Instantiate(Resources.Load<GameObject>("UI/InGameSettingsMenu")));
        SettingsMenu menu = menuObject.GetComponent<SettingsMenu>();
        VoiceSettingsPanel.EnsureForMenu(menu);
        VoiceIntensitySettingsPanel.EnsureForMenu(menu);
        RoomVoiceSettingsPanel.EnsureForMenu(menu);
        SettingsTabsController tabs = SettingsTabsController.EnsureForMenu(menu);

        Assert.That(tabs.TabCount, Is.EqualTo(4));
        Assert.That(menu.mouseSensitivitySlider.transform.parent.gameObject.activeSelf, Is.True);
        Assert.That(menu.resolutionDropdown.transform.parent.gameObject.activeSelf, Is.False);
        Assert.That(menu.volumeSlider.transform.parent.gameObject.activeSelf, Is.False);

        tabs.SelectTab(SettingsTab.Video);
        Assert.That(menu.resolutionDropdown.transform.parent.gameObject.activeSelf, Is.True);
        Assert.That(menu.fullscreenToggle.transform.parent.gameObject.activeSelf, Is.True);
        Assert.That(menu.mouseSensitivitySlider.transform.parent.gameObject.activeSelf, Is.False);

        tabs.SelectTab(SettingsTab.Audio);
        Assert.That(menu.volumeSlider.transform.parent.gameObject.activeSelf, Is.True);
        Assert.That(menu.resolutionDropdown.transform.parent.gameObject.activeSelf, Is.False);

        tabs.SelectTab(SettingsTab.Voip);
        Transform rows = menu.resolutionDropdown.transform.parent.parent;
        Assert.That(rows.Find("TXT_Voice Mode").gameObject.activeSelf, Is.True);
        Assert.That(rows.Find("TXT_Push-to-Talk Key").gameObject.activeSelf, Is.True);
        Assert.That(rows.Find("TXT_Microphone").gameObject.activeSelf, Is.True);
        Assert.That(rows.Find("TXT_Microphone Test").gameObject.activeSelf, Is.True);
        Assert.That(menu.volumeSlider.transform.parent.gameObject.activeSelf, Is.False);
    }

    private void Send(int actor, float intensity, ushort sequence = 1, bool calibrated = true)
        => manager.OnEvent(Event(actor,
            new VoiceRangePacket(calibrated, VoiceRangePacket.Quantize(intensity), sequence, PhotonNetwork.ServerTimestamp).Encode()));
    private static EventData Event(int actor, object data)
    {
        var value = new EventData { Code = VivoxVoiceManager.VoiceRangeEventCode };
        var parameters = typeof(EventData).GetField("Parameters");
        if (parameters.GetValue(value) == null) parameters.SetValue(value, Activator.CreateInstance(parameters.FieldType));
        object map = parameters.GetValue(value);
        var item = map.GetType().GetProperty("Item");
        item.SetValue(map, actor, new object[] { (byte)254 });
        item.SetValue(map, data, new object[] { (byte)245 });
        return value;
    }
    private T Add<T>(T value) where T : Object { objects.Add(value); return value; }
    private static Player Player(int actor, bool local) => (Player)Activator.CreateInstance(typeof(Player),
        BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "Voice fixture", actor, local }, null);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static object Static(string name, params object[] args) => typeof(VivoxVoiceManager)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
}
