using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum VoiceTransmissionMode
{
    OpenMicrophone,
    PushToTalk
}

public static class VoiceTransmissionPreferences
{
    public const string ModePreferenceKey = "voice.transmissionMode";
    public const string BindingPreferenceKey = "voice.pushToTalkKey";
    public const Key DefaultPushToTalkKey = Key.V;

    private static bool loaded;
    private static VoiceTransmissionMode mode;
    private static Key pushToTalkKey;

    public static event Action Changed;
    public static VoiceTransmissionMode Mode { get { EnsureLoaded(); return mode; } }
    public static Key PushToTalkKey { get { EnsureLoaded(); return pushToTalkKey; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loaded = false;
        Changed = null;
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        int savedMode = PlayerPrefs.GetInt(ModePreferenceKey, 0);
        mode = Enum.IsDefined(typeof(VoiceTransmissionMode), savedMode)
            ? (VoiceTransmissionMode)savedMode : VoiceTransmissionMode.OpenMicrophone;
        Key savedKey = (Key)PlayerPrefs.GetInt(BindingPreferenceKey, (int)DefaultPushToTalkKey);
        pushToTalkKey = IsValidBinding(savedKey) ? savedKey : DefaultPushToTalkKey;
        loaded = true;
    }

    public static bool IsValidBinding(Key key)
    {
        return key != Key.None && key != Key.Escape && Enum.IsDefined(typeof(Key), key)
            && Enum.GetName(typeof(Key), key) != "IMESelected";
    }

    public static void SetMode(VoiceTransmissionMode value)
    {
        if (!Enum.IsDefined(typeof(VoiceTransmissionMode), value))
            throw new ArgumentOutOfRangeException(nameof(value));
        EnsureLoaded();
        if (mode == value) return;
        mode = value;
        PlayerPrefs.SetInt(ModePreferenceKey, (int)value);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static void SetPushToTalkKey(Key value)
    {
        if (!IsValidBinding(value)) throw new ArgumentOutOfRangeException(nameof(value));
        EnsureLoaded();
        if (pushToTalkKey == value) return;
        pushToTalkKey = value;
        PlayerPrefs.SetInt(BindingPreferenceKey, (int)value);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static string BindingDisplayName
    {
        get
        {
            Key key = PushToTalkKey;
            string displayName = Keyboard.current != null ? Keyboard.current[key].displayName : null;
            return string.IsNullOrEmpty(displayName) ? key.ToString() : displayName;
        }
    }

    public static bool IsPushToTalkHeld()
    {
        return Keyboard.current != null && Keyboard.current[PushToTalkKey].isPressed;
    }
}
