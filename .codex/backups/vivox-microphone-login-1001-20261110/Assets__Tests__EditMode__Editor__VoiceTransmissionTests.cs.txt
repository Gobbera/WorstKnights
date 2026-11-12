using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Unity.Services.Core;
using Object = UnityEngine.Object;

public sealed class VoiceTransmissionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private bool hadMode, hadBinding;
    private int savedMode, savedBinding;
    private GameObject menuObject;
    private VoiceSettingsPanel panel;
    private Keyboard keyboard, previousKeyboard;

    [SetUp]
    public void SetUp()
    {
        hadMode = PlayerPrefs.HasKey(VoiceTransmissionPreferences.ModePreferenceKey);
        hadBinding = PlayerPrefs.HasKey(VoiceTransmissionPreferences.BindingPreferenceKey);
        savedMode = PlayerPrefs.GetInt(VoiceTransmissionPreferences.ModePreferenceKey);
        savedBinding = PlayerPrefs.GetInt(VoiceTransmissionPreferences.BindingPreferenceKey);
        PlayerPrefs.DeleteKey(VoiceTransmissionPreferences.ModePreferenceKey);
        PlayerPrefs.DeleteKey(VoiceTransmissionPreferences.BindingPreferenceKey);
        ReloadPreferences();
    }

    [TearDown]
    public void TearDown()
    {
        if (panel != null) Invoke(panel, "OnDisable");
        if (menuObject != null) Object.DestroyImmediate(menuObject);
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        Restore(VoiceTransmissionPreferences.ModePreferenceKey, hadMode, savedMode);
        Restore(VoiceTransmissionPreferences.BindingPreferenceKey, hadBinding, savedBinding);
        PlayerPrefs.Save();
        ReloadPreferences();
        panel = null; menuObject = null; keyboard = previousKeyboard = null;
    }

    [Test]
    public void OpenMicrophoneNeedsAReadyFocusedChannelAndPausesDuringBindingCapture()
    {
        var gate = new VoiceTransmissionGate();
        Assert.That(gate.Evaluate(VoiceTransmissionMode.OpenMicrophone, false, true, false, false), Is.False);
        Assert.That(gate.Evaluate(VoiceTransmissionMode.OpenMicrophone, true, false, false, false), Is.False);
        Assert.That(gate.Evaluate(VoiceTransmissionMode.OpenMicrophone, true, true, false, true), Is.False);
        Assert.That(gate.Evaluate(VoiceTransmissionMode.OpenMicrophone, true, true, false, false), Is.True);
    }

    [Test]
    public void PushToTalkRequiresANewPressAndClosesOnRelease()
    {
        var gate = new VoiceTransmissionGate();
        Assert.That(Ptt(gate, true), Is.False);
        Assert.That(Ptt(gate, false), Is.False);
        Assert.That(Ptt(gate, true), Is.True);
        Assert.That(Ptt(gate, true), Is.True);
        Assert.That(Ptt(gate, false), Is.False);
        Assert.That(Ptt(gate, true), Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ReconnectFocusAndBindingInterruptionsCannotReopenAHeldKey(int interruption)
    {
        var gate = new VoiceTransmissionGate();
        Ptt(gate, false);
        Assert.That(Ptt(gate, true), Is.True);
        Assert.That(gate.Evaluate(VoiceTransmissionMode.PushToTalk, interruption != 0,
            interruption != 1, true, interruption == 2), Is.False);
        Assert.That(Ptt(gate, true), Is.False);
        Assert.That(Ptt(gate, false), Is.False);
        Assert.That(Ptt(gate, true), Is.True);
    }

    [Test]
    public void ChangingModeOrResettingTheBindingRequiresRelease()
    {
        var gate = new VoiceTransmissionGate();
        Assert.That(gate.Evaluate(VoiceTransmissionMode.OpenMicrophone, true, true, true, false), Is.True);
        Assert.That(Ptt(gate, true), Is.False);
        Ptt(gate, false);
        Assert.That(Ptt(gate, true), Is.True);
        gate.Reset();
        Assert.That(Ptt(gate, true), Is.False);
        Ptt(gate, false);
        Assert.That(Ptt(gate, true), Is.True);
    }

    [Test]
    public void SavedChoicesSurvivePreferenceReload()
    {
        Assert.That(VoiceTransmissionPreferences.Mode, Is.EqualTo(VoiceTransmissionMode.OpenMicrophone));
        Assert.That(VoiceTransmissionPreferences.PushToTalkKey, Is.EqualTo(Key.V));
        VoiceTransmissionPreferences.SetMode(VoiceTransmissionMode.PushToTalk);
        VoiceTransmissionPreferences.SetPushToTalkKey(Key.B);
        ReloadPreferences();
        Assert.That(VoiceTransmissionPreferences.Mode, Is.EqualTo(VoiceTransmissionMode.PushToTalk));
        Assert.That(VoiceTransmissionPreferences.PushToTalkKey, Is.EqualTo(Key.B));
    }

    [Test]
    public void InvalidSavedChoicesFallBackToUsableDefaults()
    {
        PlayerPrefs.SetInt(VoiceTransmissionPreferences.ModePreferenceKey, 99);
        PlayerPrefs.SetInt(VoiceTransmissionPreferences.BindingPreferenceKey, -100);
        ReloadPreferences();
        Assert.That(VoiceTransmissionPreferences.Mode, Is.EqualTo(VoiceTransmissionMode.OpenMicrophone));
        Assert.That(VoiceTransmissionPreferences.PushToTalkKey, Is.EqualTo(Key.V));
    }

    [TestCase(Key.None)]
    [TestCase(Key.Escape)]
    [TestCase((Key)111)]
    [TestCase((Key)9999)]
    public void ReservedAndInvalidBindingsCannotBeSaved(Key key)
    {
        Assert.That(VoiceTransmissionPreferences.IsValidBinding(key), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => VoiceTransmissionPreferences.SetPushToTalkKey(key));
        Assert.That(VoiceTransmissionPreferences.PushToTalkKey, Is.EqualTo(Key.V));
    }

    [Test]
    public void SharedMenuHasWiredVoiceControlsAndCanBeConfiguredWithoutAVoiceSession()
    {
        CreateMenu();
        TMP_Dropdown dropdown = Field<TMP_Dropdown>(panel, "modeDropdown");
        Assert.That(dropdown.options[0].text, Is.EqualTo("Open Microphone"));
        Assert.That(dropdown.options[1].text, Is.EqualTo("Push to Talk"));
        Assert.That(Field<Button>(panel, "bindingButton").interactable, Is.False);
        dropdown.value = 1;
        Assert.That(VoiceTransmissionPreferences.Mode, Is.EqualTo(VoiceTransmissionMode.PushToTalk));
        Assert.That(Field<Button>(panel, "bindingButton").interactable, Is.True);
        Assert.That(VoiceSettingsPanel.EnsureForMenu(menuObject.GetComponent<SettingsMenu>()), Is.SameAs(panel));
        Assert.That(menuObject.GetComponentsInChildren<VoiceSettingsPanel>(true).Length, Is.EqualTo(1));
    }

    [Test]
    public void RebindingWaitsForTheInitiatingKeyReleaseThenSavesANewKeyboardKey()
    {
        if (!Application.isPlaying) Assert.Ignore("Keyboard frame-edge integration requires the native Play Mode probe.");
        CreateMenu(); CreateKeyboard();
        VoiceTransmissionPreferences.SetMode(VoiceTransmissionMode.PushToTalk);
        Keys(Key.Enter);
        panel.BeginRebinding();
        Invoke(panel, "Update");
        Assert.That(VoiceSettingsPanel.IsAnyRebinding, Is.True);
        KeysAndUpdatePanel();
        Assert.That(keyboard.anyKey.isPressed, Is.False, "The initiating key must be released in the input fixture.");
        Assert.That(Field<bool>(panel, "captureArmed"), Is.True, "Capture should arm after the initiating release.");
        KeysAndUpdatePanel(Key.B);
        Assert.That(Keyboard.current, Is.SameAs(keyboard));
        Assert.That(VoiceSettingsPanel.IsAnyRebinding, Is.False);
        Assert.That(VoiceTransmissionPreferences.PushToTalkKey, Is.EqualTo(Key.B));
        Assert.That(Field<TMP_Text>(panel, "bindingLabel").text, Is.EqualTo("B"));
    }

    [Test]
    public void EscapeCancelsCaptureAndDoesNotAlsoCloseTheInGameMenu()
    {
        if (!Application.isPlaying) Assert.Ignore("Keyboard frame-edge integration requires the native Play Mode probe.");
        CreateMenu(); CreateKeyboard();
        VoiceTransmissionPreferences.SetMode(VoiceTransmissionMode.PushToTalk);
        panel.BeginRebinding(); KeysAndUpdatePanel();
        KeysAndUpdatePanel(Key.Escape);
        Assert.That(VoiceSettingsPanel.IsAnyRebinding, Is.False);
        Assert.That(VoiceSettingsPanel.BlocksMenuToggleThisFrame, Is.True);
        var controller = menuObject.GetComponent<InGameSettingsMenuController>();
        Invoke(controller, "Update");
        Assert.That(controller.IsOpen, Is.True);
        Assert.That(VoiceTransmissionPreferences.PushToTalkKey, Is.EqualTo(Key.V));
    }

    [Test]
    public void ClosingTheMenuOrLosingFocusCancelsCapture()
    {
        CreateMenu();
        VoiceTransmissionPreferences.SetMode(VoiceTransmissionMode.PushToTalk);
        panel.BeginRebinding();
        menuObject.GetComponent<InGameSettingsMenuController>().CloseMenu();
        Assert.That(VoiceSettingsPanel.IsAnyRebinding, Is.False);
        panel.BeginRebinding();
        Invoke(panel, "OnApplicationFocus", false);
        Assert.That(VoiceSettingsPanel.IsAnyRebinding, Is.False);
    }

    [Test]
    public void TheSavedBindingReadsHeldAndReleasedKeyboardInput()
    {
        CreateKeyboard();
        VoiceTransmissionPreferences.SetPushToTalkKey(Key.B);
        Keys(Key.V);
        Assert.That(VoiceTransmissionPreferences.IsPushToTalkHeld(), Is.False);
        Keys(Key.B);
        Assert.That(VoiceTransmissionPreferences.IsPushToTalkHeld(), Is.True);
        Keys();
        Assert.That(VoiceTransmissionPreferences.IsPushToTalkHeld(), Is.False);
    }

    [Test]
    public void OfflineSuspensionDoesNotInitializeServicesOrCaptureAMicrophone()
    {
        var before = UnityServices.State;
        var root = new GameObject("Inactive offline voice manager fixture");
        root.SetActive(false);
        try
        {
            var manager = root.AddComponent<VivoxVoiceManager>();
            manager.SuspendVoiceTransmission();
            Assert.That(manager.IsMicrophoneTransmissionAllowed, Is.False);
            Assert.That(UnityServices.State, Is.EqualTo(before));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void OlderMenusCanCreateVoiceControlsWithoutDuplicatingRows()
    {
        menuObject = Object.Instantiate(Resources.Load<GameObject>("UI/InGameSettingsMenu"));
        var settings = menuObject.GetComponent<SettingsMenu>();
        var existing = menuObject.GetComponent<VoiceSettingsPanel>();
        if (existing != null) Object.DestroyImmediate(existing);
        Transform rows = settings.resolutionDropdown.transform.parent.parent;
        Object.DestroyImmediate(rows.Find("TXT_Voice Mode").gameObject);
        Object.DestroyImmediate(rows.Find("TXT_Push-to-Talk Key").gameObject);
        panel = VoiceSettingsPanel.EnsureForMenu(settings);
        Assert.That(Field<TMP_Dropdown>(panel, "modeDropdown").options.Count, Is.EqualTo(2));
        Assert.That(Field<Button>(panel, "bindingButton"), Is.Not.Null);
        VoiceSettingsPanel.EnsureForMenu(settings);
        int voiceRows = 0;
        foreach (Transform row in rows)
            if (row.name == "TXT_Voice Mode" || row.name == "TXT_Push-to-Talk Key") voiceRows++;
        Assert.That(voiceRows, Is.EqualTo(2));
    }

    private void CreateMenu()
    {
        menuObject = Object.Instantiate(Resources.Load<GameObject>("UI/InGameSettingsMenu"));
        menuObject.GetComponent<SettingsMenu>().enabled = false;
        menuObject.GetComponent<InGameSettingsMenuController>().enabled = false;
        panel = VoiceSettingsPanel.EnsureForMenu(menuObject.GetComponent<SettingsMenu>());
        if (!Application.isPlaying) Invoke(panel, "OnEnable");
        // Edit Mode does not run the normal runtime lifecycle; avoid changing Editor cursor state.
        typeof(InGameSettingsMenuController).GetField("isOpen", PrivateInstance)
            .SetValue(menuObject.GetComponent<InGameSettingsMenuController>(), true);
    }

    private void CreateKeyboard()
    {
        previousKeyboard = Keyboard.current;
        keyboard = InputSystem.AddDevice<Keyboard>();
        foreach (var key in keyboard.allKeys) _ = key.wasPressedThisFrame;
    }

    private void Keys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }

    private void KeysAndUpdatePanel(params Key[] keys)
    {
        // Frame-edge queries must be read inside the same input update in this Edit Mode fixture.
        Exception failure = null;
        Action tick = () =>
        {
            try
            {
                if (keys.Length > 0) Assert.That(keyboard[keys[0]].wasPressedThisFrame, Is.True);
                Invoke(panel, "Update");
            }
            catch (Exception error) { failure = error; }
        };
        InputSystem.onAfterUpdate += tick;
        try { Keys(keys); }
        finally { InputSystem.onAfterUpdate -= tick; }
        if (failure != null) throw failure;
    }

    private static bool Ptt(VoiceTransmissionGate gate, bool held)
        => gate.Evaluate(VoiceTransmissionMode.PushToTalk, true, true, held, false);

    private static T Field<T>(object target, string name)
        => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);

    private static void Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args);

    private static void ReloadPreferences()
        => typeof(VoiceTransmissionPreferences).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);

    private static void Restore(string key, bool existed, int value)
    {
        if (existed) PlayerPrefs.SetInt(key, value);
        else PlayerPrefs.DeleteKey(key);
    }
}
