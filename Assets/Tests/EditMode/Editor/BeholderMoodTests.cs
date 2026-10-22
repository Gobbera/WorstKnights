using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BeholderMoodTests
{
    [Test]
    public void ResolveMood_UsesNonOverlappingUpperThresholds()
    {
        Assert.That(Resolve(49, BeholderMood.Neutral), Is.EqualTo(BeholderMood.Neutral));
        Assert.That(Resolve(50, BeholderMood.Neutral), Is.EqualTo(BeholderMood.Irritated));
        Assert.That(Resolve(100, BeholderMood.Irritated), Is.EqualTo(BeholderMood.Angry));
        Assert.That(Resolve(150, BeholderMood.Angry), Is.EqualTo(BeholderMood.Infuriated));
        Assert.That(Resolve(200, BeholderMood.Neutral), Is.EqualTo(BeholderMood.Infuriated));
    }

    [Test]
    public void ResolveMood_RequiresFivePointRetreatBelowThreshold()
    {
        Assert.That(Resolve(95, BeholderMood.Angry), Is.EqualTo(BeholderMood.Angry));
        Assert.That(Resolve(94, BeholderMood.Angry), Is.EqualTo(BeholderMood.Irritated));
        Assert.That(Resolve(45, BeholderMood.Irritated), Is.EqualTo(BeholderMood.Irritated));
        Assert.That(Resolve(44, BeholderMood.Irritated), Is.EqualTo(BeholderMood.Neutral));
        Assert.That(Resolve(145, BeholderMood.Infuriated), Is.EqualTo(BeholderMood.Infuriated));
        Assert.That(Resolve(144, BeholderMood.Infuriated), Is.EqualTo(BeholderMood.Angry));
    }

    [Test]
    public void BeholderPrefab_HasAuthoritativeMoodAndSeparateVoiceSource()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beholder/Beholder.prefab");
        Assert.That(prefab, Is.Not.Null);
        BeholderMoodController mood = prefab.GetComponent<BeholderMoodController>();
        BeholderDialogueController dialogue = prefab.GetComponent<BeholderDialogueController>();
        EnemyNetworkSync network = prefab.GetComponent<EnemyNetworkSync>();
        Assert.That(mood, Is.Not.Null);
        Assert.That(dialogue, Is.Not.Null);
        Assert.That(network, Is.Not.Null);

        SerializedObject moodData = new SerializedObject(mood);
        Assert.That(moodData.FindProperty("hysteresis").intValue, Is.EqualTo(5));
        SerializedProperty rules = moodData.FindProperty("eventRules");
        Assert.That(rules.arraySize, Is.EqualTo(1));
        Assert.That(rules.GetArrayElementAtIndex(0).FindPropertyRelative("eventId").stringValue,
            Is.EqualTo(BeholderMoodController.DamageReceivedEvent));
        Assert.That(rules.GetArrayElementAtIndex(0).FindPropertyRelative("points").intValue, Is.EqualTo(10));

        SerializedObject dialogueData = new SerializedObject(dialogue);
        AudioSource voice = dialogueData.FindProperty("voiceSource").objectReferenceValue as AudioSource;
        Assert.That(voice, Is.Not.Null);
        Assert.That(voice, Is.Not.SameAs(prefab.GetComponent<AudioSource>()));
        Assert.That(voice.transform.parent, Is.SameAs(prefab.transform));
        Assert.That(dialogueData.FindProperty("linesByState").arraySize, Is.EqualTo(9));
        Assert.That(dialogueData.FindProperty("chaseSearchTransitionCooldown").floatValue,
            Is.EqualTo(2f).Within(0.001f));

        SerializedProperty alertLines = dialogueData.FindProperty("linesByState").GetArrayElementAtIndex(2);
        Assert.That(alertLines.FindPropertyRelative("state").enumValueIndex, Is.EqualTo((int)EnemyState.Alert));
        Assert.That(alertLines.FindPropertyRelative("neutral").arraySize, Is.GreaterThan(0));

        SerializedObject brainData = new SerializedObject(prefab.GetComponent<EnemyBrain>());
        Assert.That(brainData.FindProperty("searchToReflectionDelay").floatValue,
            Is.EqualTo(4f).Within(0.001f));
        Assert.That(brainData.FindProperty("useLastSeenSearchFlow").boolValue, Is.True);
        Assert.That(brainData.FindProperty("lostSightGraceDuration").floatValue,
            Is.EqualTo(1.75f).Within(0.001f));
        Assert.That(brainData.FindProperty("doorPassageMemoryRadius"), Is.Null);

        Assert.That(moodData.FindProperty("neutral").FindPropertyRelative("searchPersistenceMultiplier").floatValue,
            Is.EqualTo(0.5f));
        Assert.That(moodData.FindProperty("irritated").FindPropertyRelative("searchPersistenceMultiplier").floatValue,
            Is.EqualTo(1f));
        Assert.That(moodData.FindProperty("angry").FindPropertyRelative("searchPersistenceMultiplier").floatValue,
            Is.EqualTo(2f));
        Assert.That(moodData.FindProperty("infuriated").FindPropertyRelative("searchPersistenceMultiplier").floatValue,
            Is.EqualTo(3f));
        Assert.That(moodData.FindProperty("neutral").FindPropertyRelative("chasePersistenceMultiplier").floatValue,
            Is.EqualTo(1f));
        Assert.That(moodData.FindProperty("irritated").FindPropertyRelative("chasePersistenceMultiplier").floatValue,
            Is.EqualTo(1.35f));
        Assert.That(moodData.FindProperty("angry").FindPropertyRelative("chasePersistenceMultiplier").floatValue,
            Is.EqualTo(1.75f));
        Assert.That(moodData.FindProperty("infuriated").FindPropertyRelative("chasePersistenceMultiplier").floatValue,
            Is.EqualTo(2.25f));

        Assert.That(moodData.FindProperty("enemyVision").objectReferenceValue,
            Is.SameAs(prefab.GetComponent<EnemyVision>()));
        Assert.That(moodData.FindProperty("enemyHearing").objectReferenceValue,
            Is.SameAs(prefab.GetComponent<EnemyHearing>()));
        AssertPerceptionProfile(moodData.FindProperty("neutral"), 1f, 1f, 1f, 0f);
        AssertPerceptionProfile(moodData.FindProperty("irritated"), 7f / 6f, 1.07f, 1.1f, 1f);
        AssertPerceptionProfile(moodData.FindProperty("angry"), 4f / 3f, 1.13f, 1.2f, 2f);
        AssertPerceptionProfile(moodData.FindProperty("infuriated"), 1.5f, 1.2f, 1.3f, 3f);

        SerializedObject networkData = new SerializedObject(network);
        Assert.That(networkData.FindProperty("beholderMood").objectReferenceValue, Is.SameAs(mood));
        Assert.That(networkData.FindProperty("beholderDialogue").objectReferenceValue, Is.SameAs(dialogue));
    }

    [Test]
    public void ChaseGrace_UsesMoodMultiplierAndOneSecondMinimum()
    {
        GameObject enemy = new GameObject("Enemy");
        try
        {
            BeholderMoodController mood = enemy.AddComponent<BeholderMoodController>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            var settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("lostSightGraceDuration").floatValue = 1.25f;
            settings.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(brain.ResolveLostSightGraceDuration(), Is.EqualTo(1.25f).Within(0.001f));
            mood.ApplyReplicatedState(100, BeholderMood.Angry);
            Assert.That(brain.ResolveLostSightGraceDuration(), Is.EqualTo(2.1875f).Within(0.001f));

            settings.FindProperty("lostSightGraceDuration").floatValue = 0.1f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(brain.ResolveLostSightGraceDuration(), Is.EqualTo(1f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void MoodPerception_UsesApprovedRangesAndRestoresNeutralBaselines()
    {
        GameObject enemy = new GameObject("Beholder");
        try
        {
            EnemyVision vision = enemy.AddComponent<EnemyVision>();
            EnemyHearing hearing = enemy.AddComponent<EnemyHearing>();
            var visionSettings = new SerializedObject(vision);
            visionSettings.FindProperty("viewDistance").floatValue = 12f;
            visionSettings.FindProperty("viewAngle").floatValue = 100f;
            visionSettings.FindProperty("detectionUpdateInterval").floatValue = 0.15f;
            visionSettings.ApplyModifiedPropertiesWithoutUndo();
            var hearingSettings = new SerializedObject(hearing);
            hearingSettings.FindProperty("hearingMultiplier").floatValue = 1.4f;
            hearingSettings.ApplyModifiedPropertiesWithoutUndo();

            BeholderMoodController mood = enemy.AddComponent<BeholderMoodController>();
            AssertPerception(vision, hearing, 12f, 100f, 0.5f, 7f, 10f, 14f);

            mood.ApplyReplicatedState(50, BeholderMood.Irritated);
            AssertPerception(vision, hearing, 14f, 107f, 0.55f, 7.7f, 11f, 15f);

            mood.ApplyReplicatedState(100, BeholderMood.Angry);
            AssertPerception(vision, hearing, 16f, 113f, 0.6f, 8.4f, 12f, 16f);

            mood.ApplyReplicatedState(150, BeholderMood.Infuriated);
            AssertPerception(vision, hearing, 18f, 120f, 0.65f, 9.1f, 13f, 17f);
            Assert.That(hearing.ResolveAudibleRadius(100f), Is.EqualTo(103f).Within(0.001f));
            Assert.That(hearing.CanHear(new NoiseEvent(new Vector3(16.9f, 0f, 0f),
                10f, 1f, NoiseType.Footstep, null)), Is.True);
            Assert.That(hearing.CanHear(new NoiseEvent(new Vector3(17.1f, 0f, 0f),
                10f, 1f, NoiseType.Footstep, null)), Is.False);

            mood.ApplyReplicatedState(0, BeholderMood.Neutral);
            AssertPerception(vision, hearing, 12f, 100f, 0.5f, 7f, 10f, 14f);
            Assert.That(visionSettings.FindProperty("viewDistance").floatValue, Is.EqualTo(12f));
            Assert.That(hearingSettings.FindProperty("hearingMultiplier").floatValue, Is.EqualTo(1.4f));
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void DialogueBank_SelectsAudioCuesByMood()
    {
        AudioCue neutral = ScriptableObject.CreateInstance<AudioCue>();
        AudioCue irritated = ScriptableObject.CreateInstance<AudioCue>();
        AudioCue angry = ScriptableObject.CreateInstance<AudioCue>();
        AudioCue infuriated = ScriptableObject.CreateInstance<AudioCue>();
        try
        {
            var bank = new BeholderDialogueController.StateLines
            {
                neutral = new[] { neutral },
                irritated = new[] { irritated },
                angry = new[] { angry },
                infuriated = new[] { infuriated }
            };

            Assert.That(bank.GetCues(BeholderMood.Neutral), Is.EqualTo(new[] { neutral }));
            Assert.That(bank.GetCues(BeholderMood.Irritated), Is.EqualTo(new[] { irritated }));
            Assert.That(bank.GetCues(BeholderMood.Angry), Is.EqualTo(new[] { angry }));
            Assert.That(bank.GetCues(BeholderMood.Infuriated), Is.EqualTo(new[] { infuriated }));
        }
        finally
        {
            Object.DestroyImmediate(neutral);
            Object.DestroyImmediate(irritated);
            Object.DestroyImmediate(angry);
            Object.DestroyImmediate(infuriated);
        }
    }

    [Test]
    public void DialogueBank_UsesNeutralCuesUntilMoodHasItsOwnCues()
    {
        AudioCue neutral = ScriptableObject.CreateInstance<AudioCue>();
        try
        {
            var bank = new BeholderDialogueController.StateLines
            {
                neutral = new[] { neutral }
            };

            Assert.That(bank.GetCues(BeholderMood.Irritated), Is.EqualTo(new[] { neutral }));
            Assert.That(bank.GetCues(BeholderMood.Angry), Is.EqualTo(new[] { neutral }));
            Assert.That(bank.GetCues(BeholderMood.Infuriated), Is.EqualTo(new[] { neutral }));
        }
        finally
        {
            Object.DestroyImmediate(neutral);
        }
    }

    [Test]
    public void AudioCue_UsesReplicatedVolumeAndPitchWhenProvided()
    {
        GameObject sourceObject = new GameObject("Test Voice");
        AudioCue cue = ScriptableObject.CreateInstance<AudioCue>();
        try
        {
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            cue.ConfigureAudioSource(source, null, 0.35f, 1.25f);

            Assert.That(source.volume, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(source.pitch, Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(source.spatialBlend, Is.EqualTo(cue.SpatialBlend).Within(0.001f));
            Assert.That(source.minDistance, Is.EqualTo(cue.MinDistance).Within(0.001f));
            Assert.That(source.maxDistance, Is.EqualTo(cue.MaxDistance).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(cue);
        }
    }

    private static BeholderMood Resolve(int points, BeholderMood current)
    {
        return BeholderMoodController.ResolveMood(points, current, 50, 100, 150, 5);
    }

    private static void AssertPerceptionProfile(SerializedProperty profile,
        float distance, float angle, float hearing, float cap)
    {
        Assert.That(profile.FindPropertyRelative("visionDistanceMultiplier").floatValue,
            Is.EqualTo(distance).Within(0.001f));
        Assert.That(profile.FindPropertyRelative("visionAngleMultiplier").floatValue,
            Is.EqualTo(angle).Within(0.001f));
        Assert.That(profile.FindPropertyRelative("hearingRadiusMultiplier").floatValue,
            Is.EqualTo(hearing).Within(0.001f));
        Assert.That(profile.FindPropertyRelative("hearingBonusCap").floatValue,
            Is.EqualTo(cap).Within(0.001f));
    }

    private static void AssertPerception(EnemyVision vision, EnemyHearing hearing,
        float distance, float angle, float crouch, float walk, float sprint, float genericNoise)
    {
        Assert.That(vision.ViewDistance, Is.EqualTo(distance).Within(0.001f));
        Assert.That(vision.ViewAngle, Is.EqualTo(angle).Within(0.001f));
        Assert.That(vision.DetectionUpdateInterval, Is.EqualTo(0.15f).Within(0.001f));
        Assert.That(hearing.ResolveAudibleRadius(0f), Is.Zero);
        Assert.That(hearing.ResolveAudibleRadius(0.5f), Is.EqualTo(crouch).Within(0.001f));
        Assert.That(hearing.ResolveAudibleRadius(7f), Is.EqualTo(walk).Within(0.001f));
        Assert.That(hearing.ResolveAudibleRadius(10f), Is.EqualTo(sprint).Within(0.001f));
        Assert.That(hearing.ResolveAudibleRadius(14f), Is.EqualTo(genericNoise).Within(0.001f));
    }
}
