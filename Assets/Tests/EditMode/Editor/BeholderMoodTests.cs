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
}
