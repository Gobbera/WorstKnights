using System;
using System.Collections.Generic;
using UnityEngine;

public enum VoiceIntensityBand { Silent, Quiet, Normal, Loud }
public enum VoiceCalibrationStage { Inactive, Silence, Quiet, Normal, Loud, Complete, Failed }

public readonly struct VoiceIntensityProfile
{
    public const float EnergyFloor = 0.000000001f;
    public float Noise { get; }
    public float Quiet { get; }
    public float Normal { get; }
    public float Loud { get; }
    public bool IsCalibrated { get; }
    public float OpenThreshold => Noise + Mathf.Max((Quiet - Noise) * 0.2f, EnergyFloor);
    public float CloseThreshold => Noise + Mathf.Max((Quiet - Noise) * 0.1f, EnergyFloor);
    public static VoiceIntensityProfile Default => new VoiceIntensityProfile(0.005f, 0.025f, 0.1f, 0.4f, false);

    private VoiceIntensityProfile(float noise, float quiet, float normal, float loud, bool calibrated)
    {
        Noise = noise; Quiet = quiet; Normal = normal; Loud = loud; IsCalibrated = calibrated;
    }

    public static bool TryCreate(float noise, float quiet, float normal, float loud, out VoiceIntensityProfile profile)
    {
        profile = Default;
        if (!Finite(noise) || !Finite(quiet) || !Finite(normal) || !Finite(loud)
            || noise < 0f || loud > 1f) return false;
        float speechGap = Mathf.Max((loud - noise) * 0.07f, EnergyFloor);
        if (quiet - noise <= Mathf.Max(noise * 0.01f, EnergyFloor)
            || normal - quiet <= speechGap || loud - normal <= speechGap) return false;
        profile = new VoiceIntensityProfile(noise, quiet, normal, loud, true);
        return true;
    }

    public float Normalize(float energy)
    {
        if (Loud <= Normal || Normal <= Quiet || Quiet <= Noise) return 0f;
        float floor = CloseThreshold;
        if (!Finite(energy) || energy <= floor) return 0f;
        if (energy >= Loud) return 1f;
        if (energy < Quiet) return 0.25f * Mathf.InverseLerp(floor, Quiet, energy);
        if (energy < Normal) return Mathf.Lerp(0.25f, 0.6f, Mathf.InverseLerp(Quiet, Normal, energy));
        return Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(Normal, Loud, energy));
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

public sealed class VoiceIntensityCalibrator
{
    public const int SamplesPerStep = 30;
    private readonly List<float>[] samples = { new List<float>(32), new List<float>(32), new List<float>(32), new List<float>(32) };
    private readonly float?[] measuredLevels = new float?[4];
    private float elapsed;
    private bool hasResult;
    private VoiceIntensityProfile result;
    private float speechThreshold;

    public VoiceCalibrationStage Stage { get; private set; }
    public bool IsSampling { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public float Progress => Stage == VoiceCalibrationStage.Complete ? 1f :
        Stage >= VoiceCalibrationStage.Silence && Stage <= VoiceCalibrationStage.Loud
        ? samples[(int)Stage - 1].Count / (float)SamplesPerStep : 0f;

    public float? MeasuredLevel(VoiceCalibrationStage stage)
    {
        int index = (int)stage - 1;
        return index >= 0 && index < measuredLevels.Length ? measuredLevels[index] : null;
    }

    public void Begin()
    {
        foreach (var values in samples) values.Clear();
        Array.Clear(measuredLevels, 0, measuredLevels.Length);
        Stage = VoiceCalibrationStage.Silence; IsSampling = true; elapsed = 0f;
        hasResult = false; Message = string.Empty;
    }

    public void Continue()
    {
        if (Stage < VoiceCalibrationStage.Quiet || Stage > VoiceCalibrationStage.Loud || IsSampling) return;
        IsSampling = true; elapsed = 0f;
    }

    public void Cancel(string message = "Calibration canceled.")
    {
        Stage = VoiceCalibrationStage.Inactive; IsSampling = false; hasResult = false; Message = message;
    }

    public void Collect(float energy, bool inputAvailable, bool speech, float deltaTime)
    {
        if (!IsSampling) return;
        elapsed += Mathf.Max(0f, deltaTime);
        bool valid = inputAvailable && !float.IsNaN(energy) && !float.IsInfinity(energy) && energy >= 0f && energy <= 1f;
        if (Stage == VoiceCalibrationStage.Silence) valid &= !speech;
        // The guided step supplies intent; soft speech can fall below the SDK's speech detector.
        else valid &= energy > speechThreshold;
        var values = samples[(int)Stage - 1];
        if (valid) values.Add(energy);
        if (values.Count >= SamplesPerStep)
        {
            IsSampling = false;
            measuredLevels[(int)Stage - 1] = Percentile(values, Stage == VoiceCalibrationStage.Silence ? 0.9f : 0.5f);
            if (Stage == VoiceCalibrationStage.Silence)
            {
                float noise = measuredLevels[0].Value;
                float variation = noise - Percentile(samples[0], 0.1f);
                speechThreshold = noise + Mathf.Max(variation * 2f,
                    Mathf.Max(noise * 0.01f, VoiceIntensityProfile.EnergyFloor));
            }
            if (Stage != VoiceCalibrationStage.Loud) { Stage++; return; }
            if (VoiceIntensityProfile.TryCreate(measuredLevels[0].Value, measuredLevels[1].Value,
                measuredLevels[2].Value, measuredLevels[3].Value, out result))
            {
                Stage = VoiceCalibrationStage.Complete; hasResult = true; Message = "Calibration saved.";
            }
            else
            {
                Stage = VoiceCalibrationStage.Failed;
                Message = "Speech levels are too similar or out of order. Try automatic input level Off and recalibrate; check microphone gain if input stays near 100%.";
            }
        }
        else if (elapsed >= 15f)
        {
            IsSampling = false; Stage = VoiceCalibrationStage.Failed;
            Message = "Not enough clear samples. Check the microphone, hold PTT if needed, and retry.";
        }
    }

    public bool TryTakeResult(out VoiceIntensityProfile profile)
    {
        profile = result;
        if (!hasResult) return false;
        hasResult = false; return true;
    }

    private static float Percentile(List<float> values, float percentile)
    {
        if (values.Count == 0) return 0f;
        var copy = values.ToArray(); Array.Sort(copy);
        return copy[Mathf.Clamp(Mathf.RoundToInt((copy.Length - 1) * percentile), 0, copy.Length - 1)];
    }
}

/// <summary>Uses sender metadata only; no listener volume, audio playback or distance policy.</summary>
public sealed class VoiceIntensityMonitor
{
    public const float LoudThreshold = 0.82f;
    public const float SampleInterval = 0.1f;
    public VoiceIntensityProfile Profile { get; private set; } = VoiceIntensityProfile.Default;
    public VoiceIntensityCalibrator Calibration { get; } = new VoiceIntensityCalibrator();
    public bool InputAvailable { get; private set; }
    public float RawEnergy { get; private set; }
    public float InputLevel { get; private set; }
    public float Intensity { get; private set; }
    public int CompletedRevision { get; private set; }
    public VoiceIntensityBand Band => Intensity < 0.015f ? VoiceIntensityBand.Silent
        : Intensity < 0.42f ? VoiceIntensityBand.Quiet : Intensity < LoudThreshold ? VoiceIntensityBand.Normal : VoiceIntensityBand.Loud;
    private float sampleElapsed;
    private int consecutiveSpeechSamples;
    private bool gateOpen;

    public void SetProfile(VoiceIntensityProfile profile)
    {
        Profile = profile.Loud > profile.Normal && profile.Normal > profile.Quiet && profile.Quiet > profile.Noise
            ? profile : VoiceIntensityProfile.Default;
        Calibration.Cancel(string.Empty); ResetEnvelope();
    }

    public void ResetEnvelope()
    {
        RawEnergy = InputLevel = Intensity = sampleElapsed = 0f; consecutiveSpeechSamples = 0;
        gateOpen = false; InputAvailable = false;
    }

    public void Update(VoiceActivitySnapshot activity, float deltaTime)
    {
        float dt = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ? 0f : Mathf.Max(0f, deltaTime);
        InputAvailable = activity.State == VoiceActivityState.Idle || activity.State == VoiceActivityState.Speaking;
        RawEnergy = InputAvailable ? activity.AudioEnergy : 0f;
        sampleElapsed += dt;
        if (sampleElapsed + 0.000001f >= SampleInterval)
        {
            float sampledTime = sampleElapsed; sampleElapsed = 0f;
            Calibration.Collect(RawEnergy, InputAvailable, activity.IsSpeaking, sampledTime);
            if (Calibration.TryTakeResult(out VoiceIntensityProfile profile))
            { Profile = profile; CompletedRevision++; gateOpen = false; consecutiveSpeechSamples = 0; }
            bool evidence = InputAvailable && activity.IsSpeaking
                && RawEnergy > (gateOpen ? Profile.CloseThreshold : Profile.OpenThreshold);
            consecutiveSpeechSamples = evidence ? consecutiveSpeechSamples + 1 : 0;
            gateOpen = evidence && (gateOpen || consecutiveSpeechSamples >= 2);
        }
        if (!InputAvailable)
        {
            InputLevel = Intensity = 0f; gateOpen = false; consecutiveSpeechSamples = 0; return;
        }
        InputLevel = Smooth(InputLevel, RawEnergy, dt);
        float target = gateOpen && activity.IsSpeaking ? Profile.Normalize(RawEnergy) : 0f;
        Intensity = Smooth(Intensity, target, dt);
    }

    private static float Smooth(float current, float target, float dt)
        => Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / (target > current ? 0.08f : 0.22f)));
}
