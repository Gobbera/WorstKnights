using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class VoiceActivityIndicatorTests
{
    private readonly List<Object> objects = new List<Object>();
    private VoiceActivityIndicator indicator;
    private MicrophoneActivityGraphic icon;

    [SetUp]
    public void SetUp()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/VoiceActivityHUD");
        Assert.That(prefab, Is.Not.Null, "The gameplay bootstrap needs an authored HUD prefab.");
        var instance = Object.Instantiate(prefab);
        objects.Add(instance);
        indicator = instance.GetComponent<VoiceActivityIndicator>();
        icon = instance.GetComponentInChildren<MicrophoneActivityGraphic>();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [TestCase(false, true, true, false, true, true, VoiceActivityState.Disabled)]
    [TestCase(true, false, true, false, true, true, VoiceActivityState.Disconnected)]
    [TestCase(true, true, false, false, true, true, VoiceActivityState.Disconnected)]
    [TestCase(true, true, true, true, true, true, VoiceActivityState.Muted)]
    [TestCase(true, true, true, false, false, true, VoiceActivityState.Muted)]
    [TestCase(true, true, true, false, true, false, VoiceActivityState.Idle)]
    [TestCase(true, true, true, false, true, true, VoiceActivityState.Speaking)]
    public void ActivityRequiresLocalSpeechAndAnAvailableUnmutedTransmission(bool enabled, bool connected,
        bool local, bool muted, bool transmitting, bool speech, VoiceActivityState expected)
    {
        var activity = VoiceActivitySnapshot.FromInput(enabled, connected, local, muted, transmitting, speech, 0.8d);
        Assert.That(activity.State, Is.EqualTo(expected));
        Assert.That(activity.IsSpeaking, Is.EqualTo(expected == VoiceActivityState.Speaking));
        if (expected != VoiceActivityState.Idle && expected != VoiceActivityState.Speaking)
            Assert.That(activity.AudioEnergy, Is.Zero, "Unavailable input must not retain stale energy.");
    }

    [TestCase(double.NaN, 0f)]
    [TestCase(double.PositiveInfinity, 0f)]
    [TestCase(-1d, 0f)]
    [TestCase(2d, 1f)]
    public void InvalidEnergyCannotLeakIntoTheActivityView(double energy, float expected)
    {
        Assert.That(VoiceActivitySnapshot.FromInput(true, true, true, false, true, false, energy).AudioEnergy,
            Is.EqualTo(expected));
    }

    [Test]
    public void SpeechFadesInAndSilenceReturnsToIdleWithoutAClockDrivenBlink()
    {
        Present(Idle(), 1f);
        float idleAlpha = icon.color.a;
        Present(Speaking(), 0.016f);
        Assert.That(icon.color.a, Is.GreaterThan(idleAlpha).And.LessThan(1f));
        Assert.That(icon.CrossedOut, Is.False);
        for (int i = 0; i < 60; i++) Present(Speaking(), 1f / 60f);
        Assert.That(icon.color.a, Is.EqualTo(1f).Within(0.001f));
        for (int i = 0; i < 90; i++) Present(Idle(), 1f / 60f);
        Assert.That(icon.color.a, Is.EqualTo(idleAlpha).Within(0.001f));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(120)]
    public void FadeProgressIsConsistentAcrossFrameRates(int framesPerSecond)
    {
        Set("speechReleaseHold", 0f);
        Present(Idle(), 2f);
        float initial = icon.color.a;
        for (int i = 0; i < framesPerSecond / 2; i++) Present(Speaking(), 1f / framesPerSecond);
        Assert.That(icon.color.a, Is.EqualTo(1f - (1f - initial) * Mathf.Exp(-0.5f / 0.08f)).Within(0.0001f));
    }

    [Test]
    public void ShortSpeechGapsHoldTheIconButMuteAndDisconnectCancelTheHold()
    {
        Set("transitionTime", 0f);
        Present(Speaking(), 0.1f);
        Present(Idle(), 0.05f);
        Assert.That(icon.color.a, Is.EqualTo(1f));
        Present(Idle(), 0.1f);
        Assert.That(icon.color.a, Is.EqualTo(0.3f).Within(0.001f));
        Present(Speaking(), 0.1f);
        Present(VoiceActivitySnapshot.FromInput(true, true, true, true, true, true, 0.9d), 0f);
        Assert.That(icon.CrossedOut, Is.True);
        Present(Idle(), 0f);
        Assert.That(icon.color.a, Is.EqualTo(0.3f).Within(0.001f));
        Present(Speaking(), 0.1f);
        Present(default, 0f);
        Assert.That(icon.CrossedOut, Is.True);
        Present(Idle(), 0f);
        Assert.That(icon.color.a, Is.EqualTo(0.3f).Within(0.001f));
    }

    [Test]
    public void IconTransitionsContinueWithPausedGameplayTime()
    {
        float previousTimeScale = Time.timeScale;
        try
        {
            Present(Idle(), 1f);
            float initial = icon.color.a;
            Time.timeScale = 0f;
            Present(Speaking(), 0.1f);
            Assert.That(icon.color.a, Is.GreaterThan(initial));
        }
        finally { Time.timeScale = previousTimeScale; }
    }

    [Test]
    public void DisablingAndReenablingTheHudCannotRestoreAnOldSpeakingHold()
    {
        Set("transitionTime", 0f);
        Present(Speaking(), 0.1f);
        indicator.gameObject.SetActive(false);
        indicator.gameObject.SetActive(true);
        // Non-ExecuteAlways behaviours do not receive runtime lifecycle callbacks in Edit Mode.
        typeof(VoiceActivityIndicator).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(indicator, null);
        Present(Idle(), 0f);
        Assert.That(icon.color.a, Is.EqualTo(0.3f).Within(0.001f));
    }

    [Test]
    public void HudIsNonInteractiveAndRendersBelowTheSettingsOverlay()
    {
        Canvas canvas = indicator.GetComponent<Canvas>();
        Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        Assert.That(canvas.sortingOrder, Is.LessThan(500));
        Assert.That(indicator.GetComponent<GraphicRaycaster>(), Is.Null);
        Assert.That(icon.raycastTarget, Is.False);
        Assert.That(indicator.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
    }

    [Test]
    public void GameplayBootstrapReusesTheSceneHud()
    {
        var root = new GameObject("Inactive gameplay root fixture");
        objects.Add(root);
        root.SetActive(false);
        root.AddComponent<GameplaySceneRoot>();
        Assert.That(VoiceActivityIndicator.EnsureForGameplayScene(), Is.SameAs(indicator));
        Assert.That(VoiceActivityIndicator.EnsureForGameplayScene(), Is.SameAs(indicator));
    }

    [Test]
    public void MissingGameplayHudIsCreatedFromTheSharedPrefab()
    {
        Object.DestroyImmediate(indicator.gameObject);
        var root = new GameObject("Inactive gameplay root fixture");
        objects.Add(root);
        root.SetActive(false);
        root.AddComponent<GameplaySceneRoot>();
        VoiceActivityIndicator created = VoiceActivityIndicator.EnsureForGameplayScene();
        Assert.That(created, Is.Not.Null);
        objects.Add(created.gameObject);
        Assert.That(created.gameObject.scene, Is.EqualTo(root.scene));
        Assert.That(created.GetComponentInChildren<MicrophoneActivityGraphic>(), Is.Not.Null);
        Assert.That(VoiceActivityIndicator.EnsureForGameplayScene(), Is.SameAs(created));
    }

    [Test]
    public void NonGameplaySceneDoesNotAcquireAVoiceHud()
    {
        Assert.That(GameplaySceneRoot.TryGetActiveSceneRoot(), Is.Null);
        Assert.That(VoiceActivityIndicator.EnsureForGameplayScene(), Is.Null);
    }

    private void Present(VoiceActivitySnapshot activity, float deltaTime)
    {
        typeof(VoiceActivityIndicator).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(indicator, new object[] { activity, deltaTime });
    }

    private void Set(string name, object value)
    {
        typeof(VoiceActivityIndicator).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(indicator, value);
    }

    private static VoiceActivitySnapshot Idle() => VoiceActivitySnapshot.FromInput(true, true, true, false, true, false, 0.01d);
    private static VoiceActivitySnapshot Speaking() => VoiceActivitySnapshot.FromInput(true, true, true, false, true, true, 0.5d);
}
