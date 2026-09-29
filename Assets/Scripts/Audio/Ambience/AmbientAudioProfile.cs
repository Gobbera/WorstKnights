using System;
using UnityEngine;

[CreateAssetMenu(fileName = "AmbientAudioProfile", menuName = "Audio/Ambience/Profile")]
public sealed class AmbientAudioProfile : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string profileId = string.Empty;

    [Header("Loop Layers")]
    [SerializeField] private AudioCue primaryLoop;
    [SerializeField] [Range(0f, 2f)] private float primaryLoopVolume = 1f;
    [SerializeField] private AudioCue secondaryLoop;
    [SerializeField] [Range(0f, 2f)] private float secondaryLoopVolume = 1f;
    [SerializeField] [Min(0f)] private float fadeTime = 2f;

    [Header("Random One-Shots")]
    [SerializeField] private AudioCue[] randomOneShots = Array.Empty<AudioCue>();
    [SerializeField] private Vector2 randomIntervalRange = new Vector2(12f, 28f);
    [SerializeField] [Range(0f, 1f)] private float randomChance = 1f;

    public string ProfileId => string.IsNullOrWhiteSpace(profileId) ? name : profileId;
    public AudioCue PrimaryLoop => primaryLoop;
    public float PrimaryLoopVolume => primaryLoopVolume;
    public AudioCue SecondaryLoop => secondaryLoop;
    public float SecondaryLoopVolume => secondaryLoopVolume;
    public float FadeTime => fadeTime;
    public AudioCue[] RandomOneShots => randomOneShots;
    public Vector2 RandomIntervalRange => randomIntervalRange;
    public float RandomChance => randomChance;
    public bool HasPrimaryLoop => primaryLoop != null && primaryLoop.HasPlayableClip();
    public bool HasSecondaryLoop => secondaryLoop != null && secondaryLoop.HasPlayableClip();
    public bool HasRandomOneShots => HasPlayableRandomOneShot();

    public AudioCue GetRandomOneShot(AudioCue lastCue = null, bool avoidImmediateRepeat = true)
    {
        if (randomOneShots == null || randomOneShots.Length == 0)
            return null;

        int playableCount = 0;
        for (int i = 0; i < randomOneShots.Length; i++)
        {
            if (IsPlayableRandomCue(randomOneShots[i]))
                playableCount++;
        }

        if (playableCount == 0)
            return null;

        int startIndex = UnityEngine.Random.Range(0, randomOneShots.Length);
        AudioCue fallbackCue = null;
        for (int i = 0; i < randomOneShots.Length; i++)
        {
            AudioCue cue = randomOneShots[(startIndex + i) % randomOneShots.Length];
            if (!IsPlayableRandomCue(cue))
                continue;

            if (fallbackCue == null)
                fallbackCue = cue;

            if (!avoidImmediateRepeat || playableCount <= 1 || cue != lastCue)
                return cue;
        }

        return fallbackCue;
    }

    public float GetRandomInterval()
    {
        Vector2 orderedRange = GetOrderedRange(randomIntervalRange);
        return UnityEngine.Random.Range(orderedRange.x, orderedRange.y);
    }

    private bool HasPlayableRandomOneShot()
    {
        if (randomOneShots == null || randomOneShots.Length == 0)
            return false;

        for (int i = 0; i < randomOneShots.Length; i++)
        {
            if (IsPlayableRandomCue(randomOneShots[i]))
                return true;
        }

        return false;
    }

    private static bool IsPlayableRandomCue(AudioCue cue)
    {
        return cue != null && cue.HasPlayableClip();
    }

    private void OnValidate()
    {
        primaryLoopVolume = Mathf.Max(0f, primaryLoopVolume);
        secondaryLoopVolume = Mathf.Max(0f, secondaryLoopVolume);
        fadeTime = Mathf.Max(0f, fadeTime);
        randomIntervalRange = GetOrderedRange(randomIntervalRange);
        randomIntervalRange.x = Mathf.Max(0.1f, randomIntervalRange.x);
        randomIntervalRange.y = Mathf.Max(randomIntervalRange.x, randomIntervalRange.y);
        randomChance = Mathf.Clamp01(randomChance);

        if (randomOneShots == null)
            randomOneShots = Array.Empty<AudioCue>();
    }

    private static Vector2 GetOrderedRange(Vector2 range)
    {
        return new Vector2(
            Mathf.Min(range.x, range.y),
            Mathf.Max(range.x, range.y));
    }
}
