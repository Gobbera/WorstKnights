using System;
using UnityEngine;

[Serializable]
public struct EnvironmentReverbSettings
{
    [Range(-10000, 0)] public int room;
    [Range(-10000, 0)] public int roomHF;
    [Range(-10000, 0)] public int roomLF;
    [Min(0.1f)] public float decayTime;
    [Range(0.1f, 2f)] public float decayHFRatio;
    [Range(-10000, 1000)] public int reflections;
    [Range(0f, 0.3f)] public float reflectionsDelay;
    [Range(-10000, 2000)] public int reverb;
    [Range(0f, 0.1f)] public float reverbDelay;
    [Range(1000f, 20000f)] public float hfReference;
    [Range(20f, 1000f)] public float lfReference;
    [Range(0f, 100f)] public float diffusion;
    [Range(0f, 100f)] public float density;

    public static EnvironmentReverbSettings Dry => new EnvironmentReverbSettings
    {
        room = -10000,
        roomHF = -10000,
        roomLF = -10000,
        decayTime = 0.1f,
        decayHFRatio = 0.1f,
        reflections = -10000,
        reflectionsDelay = 0f,
        reverb = -10000,
        reverbDelay = 0f,
        hfReference = 5000f,
        lfReference = 250f,
        diffusion = 0f,
        density = 0f
    };

    public static EnvironmentReverbSettings StoneRoom => new EnvironmentReverbSettings
    {
        room = -950,
        roomHF = -650,
        roomLF = 0,
        decayTime = 1.6f,
        decayHFRatio = 0.65f,
        reflections = -1200,
        reflectionsDelay = 0.02f,
        reverb = -250,
        reverbDelay = 0.035f,
        hfReference = 5000f,
        lfReference = 250f,
        diffusion = 85f,
        density = 95f
    };

    public static EnvironmentReverbSettings FromPreset(AudioReverbPreset preset)
    {
        string presetName = preset.ToString();
        switch (presetName)
        {
            case "Off":
                return Dry;
            case "PaddedCell":
                return Create(-1000, -6000, 0, 0.17f, 0.1f, -1204, 0.001f, 207, 0.002f, 100f, 100f);
            case "Room":
                return Create(-1000, -454, 0, 0.4f, 0.83f, -1646, 0.002f, 53, 0.003f, 100f, 100f);
            case "Bathroom":
                return Create(-1000, -1200, 0, 1.49f, 0.54f, -370, 0.007f, 1030, 0.011f, 100f, 60f);
            case "Livingroom":
            case "LivingRoom":
                return Create(-1000, -6000, 0, 0.5f, 0.1f, -1376, 0.003f, -1104, 0.004f, 100f, 100f);
            case "Stoneroom":
            case "StoneRoom":
                return StoneRoom;
            case "Auditorium":
                return Create(-1000, -476, 0, 4.32f, 0.59f, -789, 0.02f, -289, 0.03f, 100f, 100f);
            case "Concerthall":
            case "ConcertHall":
                return Create(-1000, -500, 0, 3.92f, 0.7f, -1230, 0.02f, -2, 0.029f, 100f, 100f);
            case "Cave":
                return Create(-1000, 0, 0, 2.91f, 1.3f, -602, 0.015f, -302, 0.022f, 100f, 100f);
            case "Arena":
                return Create(-1000, -698, 0, 7.24f, 0.33f, -1166, 0.02f, 16, 0.03f, 100f, 100f);
            case "Hangar":
                return Create(-1000, -1000, 0, 10.05f, 0.23f, -602, 0.02f, 198, 0.03f, 100f, 100f);
            case "CarpetedHallway":
                return Create(-1000, -4000, 0, 0.3f, 0.1f, -1831, 0.002f, -1630, 0.03f, 100f, 100f);
            case "Hallway":
                return Create(-1000, -300, 0, 1.49f, 0.59f, -1219, 0.007f, 441, 0.011f, 100f, 100f);
            case "StoneCorridor":
                return Create(-1000, -237, 0, 2.7f, 0.79f, -1214, 0.013f, 395, 0.02f, 100f, 100f);
            case "Alley":
                return Create(-1000, -270, 0, 1.49f, 0.86f, -1204, 0.007f, -4, 0.011f, 100f, 100f);
            case "Forest":
                return Create(-1000, -3300, 0, 1.49f, 0.54f, -2560, 0.162f, -613, 0.088f, 79f, 100f);
            case "City":
                return Create(-1000, -800, 0, 1.49f, 0.67f, -2273, 0.007f, -2217, 0.011f, 50f, 100f);
            case "Mountains":
                return Create(-1000, -2500, 0, 1.49f, 0.21f, -2780, 0.3f, -2014, 0.1f, 27f, 100f);
            case "Quarry":
                return Create(-1000, -1000, 0, 1.49f, 0.83f, -10000, 0.061f, 500, 0.025f, 100f, 100f);
            case "Plain":
                return Create(-1000, -2000, 0, 1.49f, 0.5f, -2466, 0.179f, -2514, 0.1f, 21f, 100f);
            case "ParkingLot":
                return Create(-1000, 0, 0, 1.65f, 1.5f, -1363, 0.008f, -1153, 0.012f, 100f, 100f);
            case "SewerPipe":
                return Create(-1000, -1000, 0, 2.81f, 0.14f, 429, 0.014f, 648, 0.021f, 80f, 60f);
            case "Underwater":
                return Create(-1000, -4000, 0, 1.49f, 0.1f, -449, 0.007f, 1700, 0.011f, 100f, 100f);
            default:
                return Create(-1000, -100, 0, 1.49f, 0.83f, -2602, 0.007f, -200, 0.011f, 100f, 100f);
        }
    }

    private static EnvironmentReverbSettings Create(
        int room,
        int roomHF,
        int roomLF,
        float decayTime,
        float decayHFRatio,
        int reflections,
        float reflectionsDelay,
        int reverb,
        float reverbDelay,
        float diffusion,
        float density)
    {
        EnvironmentReverbSettings settings = new EnvironmentReverbSettings
        {
            room = room,
            roomHF = roomHF,
            roomLF = roomLF,
            decayTime = decayTime,
            decayHFRatio = decayHFRatio,
            reflections = reflections,
            reflectionsDelay = reflectionsDelay,
            reverb = reverb,
            reverbDelay = reverbDelay,
            hfReference = 5000f,
            lfReference = 250f,
            diffusion = diffusion,
            density = density
        };
        settings.Validate();
        return settings;
    }

    public static EnvironmentReverbSettings Lerp(EnvironmentReverbSettings from, EnvironmentReverbSettings to, float t)
    {
        t = Mathf.Clamp01(t);
        return new EnvironmentReverbSettings
        {
            room = Mathf.RoundToInt(Mathf.Lerp(from.room, to.room, t)),
            roomHF = Mathf.RoundToInt(Mathf.Lerp(from.roomHF, to.roomHF, t)),
            roomLF = Mathf.RoundToInt(Mathf.Lerp(from.roomLF, to.roomLF, t)),
            decayTime = Mathf.Lerp(from.decayTime, to.decayTime, t),
            decayHFRatio = Mathf.Lerp(from.decayHFRatio, to.decayHFRatio, t),
            reflections = Mathf.RoundToInt(Mathf.Lerp(from.reflections, to.reflections, t)),
            reflectionsDelay = Mathf.Lerp(from.reflectionsDelay, to.reflectionsDelay, t),
            reverb = Mathf.RoundToInt(Mathf.Lerp(from.reverb, to.reverb, t)),
            reverbDelay = Mathf.Lerp(from.reverbDelay, to.reverbDelay, t),
            hfReference = Mathf.Lerp(from.hfReference, to.hfReference, t),
            lfReference = Mathf.Lerp(from.lfReference, to.lfReference, t),
            diffusion = Mathf.Lerp(from.diffusion, to.diffusion, t),
            density = Mathf.Lerp(from.density, to.density, t)
        };
    }

    public EnvironmentReverbSettings WithWetScale(float wetScale)
    {
        wetScale = Mathf.Clamp01(wetScale);
        EnvironmentReverbSettings dry = Dry;
        return Lerp(dry, this, wetScale);
    }

    public void Validate()
    {
        room = Mathf.Clamp(room, -10000, 0);
        roomHF = Mathf.Clamp(roomHF, -10000, 0);
        roomLF = Mathf.Clamp(roomLF, -10000, 0);
        decayTime = Mathf.Max(0.1f, decayTime);
        decayHFRatio = Mathf.Clamp(decayHFRatio, 0.1f, 2f);
        reflections = Mathf.Clamp(reflections, -10000, 1000);
        reflectionsDelay = Mathf.Clamp(reflectionsDelay, 0f, 0.3f);
        reverb = Mathf.Clamp(reverb, -10000, 2000);
        reverbDelay = Mathf.Clamp(reverbDelay, 0f, 0.1f);
        hfReference = Mathf.Clamp(hfReference, 1000f, 20000f);
        lfReference = Mathf.Clamp(lfReference, 20f, 1000f);
        diffusion = Mathf.Clamp(diffusion, 0f, 100f);
        density = Mathf.Clamp(density, 0f, 100f);
    }

    public void ApplyTo(AudioReverbZone zone, float minDistance, float maxDistance)
    {
        if (zone == null)
            return;

        zone.reverbPreset = AudioReverbPreset.User;
        zone.minDistance = Mathf.Max(0.01f, minDistance);
        zone.maxDistance = Mathf.Max(zone.minDistance, maxDistance);
        zone.room = room;
        zone.roomHF = roomHF;
        zone.roomLF = roomLF;
        zone.decayTime = decayTime;
        zone.decayHFRatio = decayHFRatio;
        zone.reflections = reflections;
        zone.reflectionsDelay = reflectionsDelay;
        zone.reverb = reverb;
        zone.reverbDelay = reverbDelay;
        zone.HFReference = hfReference;
        zone.LFReference = lfReference;
        zone.diffusion = diffusion;
        zone.density = density;
    }
}

[CreateAssetMenu(fileName = "EnvironmentReverbProfile", menuName = "Audio/Reverb/Environment Reverb Profile")]
public sealed class EnvironmentReverbProfile : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string profileId = string.Empty;

    [Header("Blend")]
    [SerializeField] [Min(0f)] private float transitionTime = 1f;
    [SerializeField] [Range(0f, 1f)] private float wetScale = 1f;

    [Header("Reverb")]
    [SerializeField] private EnvironmentReverbSettings settings = EnvironmentReverbSettings.StoneRoom;

    public string ProfileId => string.IsNullOrWhiteSpace(profileId) ? name : profileId;
    public float TransitionTime => transitionTime;
    public float WetScale => wetScale;
    public EnvironmentReverbSettings Settings => settings.WithWetScale(wetScale);

    private void OnValidate()
    {
        transitionTime = Mathf.Max(0f, transitionTime);
        wetScale = Mathf.Clamp01(wetScale);
        settings.Validate();
    }
}
