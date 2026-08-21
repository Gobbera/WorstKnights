using System;
using UnityEngine;
using UnityEngine.Audio;

public enum AudioSpace
{
    TwoD = 0,
    ThreeD = 1
}

public enum AudioReplicationMode
{
    LocalOnly = 0,
    WorldLocalOnly = 1,
    WorldReplicated = 2
}

public enum AudioPlaybackAnchor
{
    PositionSnapshot = 0,
    FollowTransform = 1
}

[CreateAssetMenu(fileName = "AudioCue", menuName = "Audio/Audio Cue")]
public class AudioCue : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string cueId = string.Empty;

    [Header("Clips")]
    [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();

    [Header("Routing")]
    [SerializeField] private AudioMixerGroup mixerGroup;
    [SerializeField] private AudioSpace space = AudioSpace.ThreeD;
    [SerializeField] private AudioReplicationMode replication = AudioReplicationMode.LocalOnly;
    [SerializeField] private AudioPlaybackAnchor anchor = AudioPlaybackAnchor.PositionSnapshot;

    [Header("Playback")]
    [SerializeField] private bool loop;
    [SerializeField] [Min(0f)] private float baseVolume = 1f;
    [SerializeField] private Vector2 randomVolumeRange = Vector2.one;
    [SerializeField] private float basePitch = 1f;
    [SerializeField] private Vector2 randomPitchRange = Vector2.one;
    [SerializeField] [Range(0f, 1f)] private float spatialBlend = 1f;
    [SerializeField] [Range(0f, 360f)] private float spread;
    [SerializeField] [Min(0.01f)] private float minDistance = 1f;
    [SerializeField] [Min(0.01f)] private float maxDistance = 15f;
    [SerializeField] [Min(0f)] private float cooldown;

    public string CueId => string.IsNullOrWhiteSpace(cueId) ? name : cueId;
    public AudioClip[] Clips => clips;
    public AudioMixerGroup MixerGroup => mixerGroup;
    public AudioSpace Space => space;
    public AudioReplicationMode Replication => replication;
    public AudioPlaybackAnchor Anchor => anchor;
    public bool Loop => loop;
    public float BaseVolume => baseVolume;
    public Vector2 RandomVolumeRange => randomVolumeRange;
    public float BasePitch => basePitch;
    public Vector2 RandomPitchRange => randomPitchRange;
    public float SpatialBlend => spatialBlend;
    public float Spread => spread;
    public float MinDistance => minDistance;
    public float MaxDistance => maxDistance;
    public float Cooldown => cooldown;
    public bool Is3D => space == AudioSpace.ThreeD;

    public bool HasPlayableClip()
    {
        if (clips == null || clips.Length == 0)
            return false;

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                return true;
        }

        return false;
    }

    public AudioClip GetRandomClip()
    {
        if (clips == null || clips.Length == 0)
            return null;

        int startIndex = UnityEngine.Random.Range(0, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip clip = clips[(startIndex + i) % clips.Length];
            if (clip != null)
                return clip;
        }

        return null;
    }

    public float GetRandomizedVolume()
    {
        Vector2 range = GetOrderedRange(randomVolumeRange);
        float multiplier = UnityEngine.Random.Range(range.x, range.y);
        return Mathf.Max(0f, baseVolume * multiplier);
    }

    public float GetRandomizedPitch()
    {
        Vector2 range = GetOrderedRange(randomPitchRange);
        float multiplier = UnityEngine.Random.Range(range.x, range.y);
        return Mathf.Clamp(basePitch * multiplier, -3f, 3f);
    }

    private void OnValidate()
    {
        baseVolume = Mathf.Max(0f, baseVolume);
        minDistance = Mathf.Max(0.01f, minDistance);
        maxDistance = Mathf.Max(minDistance, maxDistance);
        cooldown = Mathf.Max(0f, cooldown);
        randomVolumeRange = GetOrderedRange(randomVolumeRange);
        randomPitchRange = GetOrderedRange(randomPitchRange);

        if (!Is3D)
        {
            spatialBlend = 0f;
            spread = 0f;
        }
    }

    private static Vector2 GetOrderedRange(Vector2 range)
    {
        return new Vector2(
            Mathf.Min(range.x, range.y),
            Mathf.Max(range.x, range.y));
    }
}

[CreateAssetMenu(fileName = "PlayerAudioProfile", menuName = "Audio/Player Audio Profile")]
public partial class PlayerAudioProfile : ScriptableObject
{
    [Serializable]
    public sealed class FootstepSettings
    {
        [SerializeField] private AudioCue cue;
        [SerializeField] [Min(0f)] private float minPlanarSpeed = 0.15f;
        [SerializeField] [Min(0.1f)] private float metersPerStep = 1.6f;

        public AudioCue Cue => cue;
        public float MinPlanarSpeed => minPlanarSpeed;
        public float MetersPerStep => metersPerStep;
    }

    [Header("Footsteps")]
    [SerializeField] private FootstepSettings walk = new FootstepSettings();
    [SerializeField] private FootstepSettings sprint = new FootstepSettings();
    [SerializeField] private FootstepSettings crouch = new FootstepSettings();

    [Header("Actions")]
    [SerializeField] private AudioCue attack;
    [SerializeField] private AudioCue jump;
    [SerializeField] private AudioCue land;

    [Header("Anchors")]
    [SerializeField] [Min(-2f)] private float footstepHeightOffset;

    public FootstepSettings Walk => walk;
    public FootstepSettings Sprint => sprint;
    public FootstepSettings Crouch => crouch;
    public AudioCue Attack => attack;
    public AudioCue Jump => jump;
    public AudioCue Land => land;
    public float FootstepHeightOffset => footstepHeightOffset;

    public FootstepSettings GetFootstepSettings(MovementState movementState)
    {
        switch (movementState)
        {
            case MovementState.sprinting:
                return sprint;
            case MovementState.crouching:
                return crouch;
            case MovementState.walking:
            default:
                return walk;
        }
    }
}
