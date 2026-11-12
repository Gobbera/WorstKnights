using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class VoiceIntensityProfileStore
{
    [Serializable]
    private sealed class SavedProfile
    {
        public int version = 1;
        public float noise, quiet, normal, loud;
    }

    public static string KeyFor(string microphone, int inputGain, bool? automaticGain, bool? noiseSuppression)
    {
        string context = $"{microphone}\n{inputGain}\n{automaticGain}\n{noiseSuppression}";
        using (var hash = SHA256.Create())
        {
            byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(context));
            var suffix = new StringBuilder(32);
            for (int i = 0; i < 16; i++) suffix.Append(bytes[i].ToString("x2"));
            return "voice.intensity.v1." + suffix;
        }
    }

    public static VoiceIntensityProfile Load(string key)
    {
        if (string.IsNullOrEmpty(key) || !PlayerPrefs.HasKey(key)) return VoiceIntensityProfile.Default;
        try
        {
            var saved = JsonUtility.FromJson<SavedProfile>(PlayerPrefs.GetString(key));
            if (saved != null && saved.version == 1 && VoiceIntensityProfile.TryCreate(saved.noise, saved.quiet,
                saved.normal, saved.loud, out VoiceIntensityProfile profile)) return profile;
        }
        catch (ArgumentException) { }
        return VoiceIntensityProfile.Default;
    }

    public static bool Has(string key) => !string.IsNullOrEmpty(key) && PlayerPrefs.HasKey(key);

    public static void Save(string key, VoiceIntensityProfile profile)
    {
        if (string.IsNullOrEmpty(key) || !profile.IsCalibrated) return;
        PlayerPrefs.SetString(key, JsonUtility.ToJson(new SavedProfile
            { noise = profile.Noise, quiet = profile.Quiet, normal = profile.Normal, loud = profile.Loud }));
        PlayerPrefs.Save();
    }

    public static void Clear(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        PlayerPrefs.DeleteKey(key); PlayerPrefs.Save();
    }
}
