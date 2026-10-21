using System;
using UnityEngine;

public enum BeholderMood
{
    Neutral,
    Irritated,
    Angry,
    Infuriated
}

[DisallowMultipleComponent]
public sealed class BeholderMoodController : MonoBehaviour
{
    public const string DamageReceivedEvent = "DamageReceived";

    [Serializable]
    public struct MoodEventRule
    {
        public string eventId;
        public int points;
    }

    [Serializable]
    public struct MoodProfile
    {
        [Min(0f)] public float damageMultiplier;
        [Min(0f)] public float movementSpeedMultiplier;
        [Min(0f)] public float searchPersistenceMultiplier;
        [Min(0f)] public float chasePersistenceMultiplier;
        public Color eyeColor;
        [Range(0f, 1f)] public float eyeColorBlend;
        [Min(0f)] public float eyeIntensityMultiplier;

        public MoodProfile(float damage, float speed, Color color, float blend, float intensity,
            float searchPersistence, float chasePersistence)
        {
            damageMultiplier = damage;
            movementSpeedMultiplier = speed;
            searchPersistenceMultiplier = searchPersistence;
            eyeColor = color;
            eyeColorBlend = blend;
            eyeIntensityMultiplier = intensity;
            chasePersistenceMultiplier = chasePersistence;
            searchPersistenceMultiplier = searchPersistence;
        }
    }

    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyAttack enemyAttack;
    [SerializeField] private BeholderEyeGlowController eyeGlow;

    [Header("Score Thresholds")]
    [SerializeField, Min(3)] private int maxPoints = 200;
    [SerializeField, Min(1)] private int irritatedAt = 50;
    [SerializeField, Min(2)] private int angryAt = 100;
    [SerializeField, Min(3)] private int infuriatedAt = 150;
    [SerializeField, Min(0)] private int hysteresis = 5;

    [Header("Events")]
    [SerializeField] private MoodEventRule[] eventRules =
    {
        new MoodEventRule { eventId = DamageReceivedEvent, points = 10 }
    };

    [Header("Mood Profiles")]
    [SerializeField] private MoodProfile neutral = new MoodProfile(1f, 1f, Color.white, 0f, 1f, 0.5f, 1f);
    [SerializeField] private MoodProfile irritated = new MoodProfile(1.1f, 1.05f, new Color(1f, 0.6f, 0.25f), 0.25f, 1.15f, 1f, 1.35f);
    [SerializeField] private MoodProfile angry = new MoodProfile(1.25f, 1.12f, new Color(1f, 0.25f, 0.1f), 0.45f, 1.35f, 2f, 1.75f);
    [SerializeField] private MoodProfile infuriated = new MoodProfile(1.5f, 1.2f, new Color(1f, 0.08f, 0.04f), 0.7f, 1.6f, 3f, 2.25f);

    private int points;
    private BeholderMood mood;

    public int Points => points;
    public BeholderMood CurrentMood => mood;
    public float SearchPersistenceMultiplier => Mathf.Max(0f, ResolveProfile().searchPersistenceMultiplier);
    public float ChasePersistenceMultiplier => Mathf.Max(0f, ResolveProfile().chasePersistenceMultiplier);
    public event Action<int, BeholderMood> ScoreChanged;
    public event Action<BeholderMood> MoodChanged;

    private void Awake()
    {
        ResolveReferences();
        ApplyProfile();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (enemyHealth != null)
            enemyHealth.DamageApplied += OnDamageApplied;

        ApplyProfile();
    }

    private void OnDisable()
    {
        if (enemyHealth != null)
            enemyHealth.DamageApplied -= OnDamageApplied;
    }

    public bool ApplyEvent(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId) || eventRules == null)
            return false;

        for (int i = 0; i < eventRules.Length; i++)
        {
            if (string.Equals(eventRules[i].eventId, eventId, StringComparison.Ordinal))
                return AddPoints(eventRules[i].points);
        }

        return false;
    }

    public bool AddPoints(int delta)
    {
        ResolveReferences();
        if (enemySetup != null && !enemySetup.HasAuthority)
            return false;

        int updatedPoints = (int)Math.Max(0L, Math.Min((long)maxPoints, (long)points + delta));
        if (updatedPoints == points)
            return false;

        BeholderMood previousMood = mood;
        points = updatedPoints;
        mood = ResolveMood(points, mood, irritatedAt, angryAt, infuriatedAt, hysteresis);
        if (mood != previousMood)
        {
            ApplyProfile();
            MoodChanged?.Invoke(mood);
        }

        ScoreChanged?.Invoke(points, mood);
        return true;
    }

    public void ApplyReplicatedState(int replicatedPoints, BeholderMood replicatedMood)
    {
        int clampedPoints = Mathf.Clamp(replicatedPoints, 0, maxPoints);
        BeholderMood clampedMood = (BeholderMood)Mathf.Clamp((int)replicatedMood, 0, (int)BeholderMood.Infuriated);
        if (points == clampedPoints && mood == clampedMood)
            return;

        bool moodChanged = mood != clampedMood;
        points = clampedPoints;
        mood = clampedMood;
        if (moodChanged)
        {
            ApplyProfile();
            MoodChanged?.Invoke(mood);
        }

        ScoreChanged?.Invoke(points, mood);
    }

    public static BeholderMood ResolveMood(int score, BeholderMood current, int irritated, int angry, int infuriated, int deadband)
    {
        irritated = Mathf.Max(1, irritated);
        angry = Mathf.Max(irritated + 1, angry);
        infuriated = Mathf.Max(angry + 1, infuriated);
        int level = Mathf.Clamp((int)current, 0, 3);
        int[] thresholds = { 0, irritated, angry, infuriated };

        while (level < 3 && score >= thresholds[level + 1])
            level++;

        while (level > 0 && score < thresholds[level] - Mathf.Max(0, deadband))
            level--;

        return (BeholderMood)level;
    }

    private void OnDamageApplied(DamageInfo damageInfo)
    {
        ApplyEvent(DamageReceivedEvent);
    }

    private void ApplyProfile()
    {
        ResolveReferences();
        MoodProfile profile = ResolveProfile();

        enemyBrain?.SetEmotionalSpeedMultiplier(profile.movementSpeedMultiplier);
        enemyAttack?.SetEmotionalDamageMultiplier(profile.damageMultiplier);
        eyeGlow?.SetMoodVisual(profile.eyeColor, profile.eyeColorBlend, profile.eyeIntensityMultiplier);
    }

    private MoodProfile ResolveProfile()
    {
        return mood switch
        {
            BeholderMood.Irritated => irritated,
            BeholderMood.Angry => angry,
            BeholderMood.Infuriated => infuriated,
            _ => neutral
        };
    }

    private void ResolveReferences()
    {
        if (enemySetup == null) enemySetup = GetComponent<EnemySetup>();
        if (enemyHealth == null) enemyHealth = GetComponent<EnemyHealth>();
        if (enemyBrain == null) enemyBrain = GetComponent<EnemyBrain>();
        if (enemyAttack == null) enemyAttack = GetComponent<EnemyAttack>();
        if (eyeGlow == null) eyeGlow = GetComponent<BeholderEyeGlowController>();
    }

    private void OnValidate()
    {
        maxPoints = Mathf.Max(3, maxPoints);
        irritatedAt = Mathf.Clamp(irritatedAt, 1, maxPoints - 2);
        angryAt = Mathf.Clamp(angryAt, irritatedAt + 1, maxPoints - 1);
        infuriatedAt = Mathf.Clamp(infuriatedAt, angryAt + 1, maxPoints);
        hysteresis = Mathf.Max(0, hysteresis);
    }
}
