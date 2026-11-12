using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class VivoxVoiceOutputTests
{
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private readonly List<Object> objects = new List<Object>();
    private VivoxVoiceSettings settings;
    private AudioSource source;

    [SetUp]
    public void SetUp()
    {
        settings = ScriptableObject.CreateInstance<VivoxVoiceSettings>(); objects.Add(settings);
        var root = new GameObject("Voice output regression source"); objects.Add(root);
        source = root.AddComponent<AudioSource>();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void UnityVoiceParticipatesInEnvironmentReverbWithoutADuplicateDistanceRolloff()
    {
        source.bypassEffects = source.bypassListenerEffects = source.bypassReverbZones = true;
        source.spatialBlend = 1f; source.dopplerLevel = 2f; source.panStereo = 1f; source.pitch = 1.3f;
        Configure();
        Assert.That(source.spatialBlend, Is.Zero);
        Assert.That(source.dopplerLevel, Is.Zero);
        Assert.That(source.panStereo, Is.Zero);
        Assert.That(source.pitch, Is.EqualTo(1f));
        Assert.That(source.bypassEffects || source.bypassListenerEffects || source.bypassReverbZones, Is.False);
        Assert.That(source.reverbZoneMix, Is.EqualTo(1f));
        Assert.That(source.loop, Is.True);
        Assert.That(source.playOnAwake, Is.False);
        Assert.That(source.outputAudioMixerGroup, Is.Null);
    }

    [Test]
    public void ReverbSendCanBeAdjustedOrDisabledWithoutMutingSpeech()
    {
        Set("environmentReverbSend", 0.35f); Configure(); Mix(0, false);
        Assert.That(source.reverbZoneMix, Is.EqualTo(0.35f).Within(0.001f));
        Assert.That(source.bypassReverbZones, Is.False);
        Set("environmentReverbSend", 0f); Configure();
        Assert.That(source.reverbZoneMix, Is.Zero);
        Assert.That(source.bypassReverbZones, Is.True);
        Assert.That(source.mute, Is.False);
        Assert.That(source.volume, Is.EqualTo(1f));
    }

    [Test]
    public void ReconfiguringReverbDoesNotOverrideTheCurrentProximityGainOrMute()
    {
        Mix(-20, true); Set("environmentReverbSend", 0.6f); Configure();
        Assert.That(source.volume, Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(source.mute, Is.True);
        Mix(-6, false); Configure();
        Assert.That(source.volume, Is.EqualTo(0.501187f).Within(0.0001f));
        Assert.That(source.mute, Is.False);
    }

    [TestCase(0, 1f)]
    [TestCase(-6, 0.501187f)]
    [TestCase(-20, 0.1f)]
    [TestCase(-50, 0.00316228f)]
    [TestCase(-80, 0.00316228f)]
    [TestCase(10, 1f)]
    public void ExistingParticipantVolumeIsConvertedToTheUnityAmplitude(int adjustment, float amplitude)
    {
        Mix(adjustment, false);
        Assert.That(source.volume, Is.EqualTo(amplitude).Within(0.00001f));
        Assert.That(source.mute, Is.False);
    }

    [Test]
    public void DistanceAndParticipantMutesSilenceBothDrySpeechAndItsReverbSend()
    {
        Configure(); Mix(-10, true);
        Assert.That(source.mute, Is.True);
        Mix(-10, false);
        Assert.That(source.mute, Is.False);
        Assert.That(source.volume, Is.EqualTo(0.316228f).Within(0.00001f));
    }

    [TestCase(VivoxVoiceMode.Disabled, false)]
    [TestCase(VivoxVoiceMode.NonPositional2D, true)]
    [TestCase(VivoxVoiceMode.Positional3D, true)]
    public void UnityRoutingIsLimitedToTheSupportedActiveVoiceModes(VivoxVoiceMode mode, bool expected)
    {
        Assert.That(Route(mode), Is.EqualTo(expected));
        Set("unityVoiceOutput", false);
        Assert.That(Route(mode), Is.False);
    }

    [Test]
    public void OptionalNativePositionalModeKeepsItsExistingDirectOutput()
    {
        Set("useNativeVivoxPositionalAudio", true);
        Assert.That(Route(VivoxVoiceMode.Positional3D), Is.False);
        Assert.That(Route(VivoxVoiceMode.NonPositional2D), Is.True);
        Assert.That((bool)Invoke("ShouldUseUnityVoiceOutput", null, VivoxVoiceMode.Positional3D), Is.False);
    }

    [Test]
    public void SharedSettingsEnableReverbAndKeepTheAuthoredProximityPolicy()
    {
        var authored = AssetDatabase.LoadAssetAtPath<VivoxVoiceSettings>("Assets/Resources/Voice/VivoxVoiceSettings.asset");
        Assert.That(authored.UnityVoiceOutput, Is.True);
        Assert.That(authored.EnvironmentReverbSend, Is.EqualTo(1f));
        Assert.That(authored.UseNativeVivoxPositionalAudio, Is.False);
        Assert.That(authored.AudibleDistance, Is.EqualTo(25));
        Assert.That(authored.ConversationalDistance, Is.EqualTo(1));
        Assert.That(authored.AudioFadeIntensity, Is.EqualTo(1.4f).Within(0.001f));
        Assert.That(authored.PositionUpdateInterval, Is.EqualTo(0.3f).Within(0.001f));
    }

    [Test]
    public void ReverbSendIsClampedToTheUnityRange()
    {
        Set("environmentReverbSend", 8f); Configure();
        Assert.That(source.reverbZoneMix, Is.EqualTo(1.1f).Within(0.001f));
        Set("environmentReverbSend", -1f); Configure();
        Assert.That(source.reverbZoneMix, Is.Zero);
    }

    private void Set(string field, object value) => typeof(VivoxVoiceSettings).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(settings, value);
    private void Configure() => Invoke("ConfigureUnityVoiceSource", source, settings);
    private void Mix(int volume, bool muted) => Invoke("ApplyUnityVoiceSourceMix", source, volume, muted);
    private bool Route(VivoxVoiceMode mode) => (bool)Invoke("ShouldUseUnityVoiceOutput", settings, mode);
    private static object Invoke(string method, params object[] args) => typeof(VivoxVoiceManager).GetMethod(method, StaticPrivate).Invoke(null, args);
}
