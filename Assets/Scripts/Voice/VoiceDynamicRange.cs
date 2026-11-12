using Unity.Services.Vivox;
using UnityEngine;

public static class VoiceDynamicRange
{
    public static float NormalDistance(VivoxVoiceSettings settings)
        => Mathf.Min(settings.AudibleDistance, Mathf.Max(settings.NormalVoiceDistance, settings.ConversationalDistance + 0.1f));

    public static float Distance(VivoxVoiceSettings settings, float intensity)
    {
        float safe = float.IsNaN(intensity) || float.IsInfinity(intensity) ? 0f : Mathf.Clamp01(intensity);
        float loud = Mathf.InverseLerp(VoiceIntensityMonitor.LoudThreshold, 1f, safe);
        return Mathf.Lerp(NormalDistance(settings), settings.AudibleDistance, Mathf.SmoothStep(0f, 1f, loud));
    }

    public static float Fade(VivoxVoiceSettings settings, float distance, float audibleDistance)
    {
        float conversation = Mathf.Min(settings.ConversationalDistance, audibleDistance);
        if (distance <= conversation) return 1f;
        if (distance >= audibleDistance) return 0f;
        float normalDistance = NormalDistance(settings);
        float loudBlend = Mathf.InverseLerp(normalDistance, settings.AudibleDistance, audibleDistance);
        float decibels = settings.EvaluateSimulatedAttenuationDecibels(distance, loudBlend);
        return Mathf.Pow(10f, Mathf.Clamp(decibels, -50f, 0f) / 20f);
    }

    public static int AmplitudeToDecibels(float amplitude, int minimumDecibels = -50)
    {
        int floor = Mathf.Min(minimumDecibels, 0);
        if (float.IsNaN(amplitude) || float.IsInfinity(amplitude) || amplitude <= 0f)
            return floor;

        float safeAmplitude = Mathf.Clamp01(amplitude);
        float decibels = 20f * Mathf.Log10(safeAmplitude);
        return Mathf.Clamp(Mathf.RoundToInt(decibels), floor, 0);
    }
}
