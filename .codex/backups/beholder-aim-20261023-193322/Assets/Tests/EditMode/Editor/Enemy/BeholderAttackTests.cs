using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public sealed class BeholderAttackTests
{
    private const string Animations = "Assets/WorstKnights/Models/Enemies/Beholder/Animations";

    [Test]
    public void AttackA10AttackB5Global8_UsesBothCooldownsFromStart()
    {
        var schedule = new BeholderAttackScheduler();
        Assert.That(schedule.TryStart(0, 10f, 8f, 0f), Is.True);
        schedule.Complete();
        Assert.That(schedule.IsReady(1, 7.999f), Is.False);
        Assert.That(schedule.IsReady(0, 8f), Is.False);
        Assert.That(schedule.TryStart(1, 5f, 8f, 8f), Is.True);
        schedule.Complete();
        Assert.That(schedule.IsReady(0, 10f), Is.False);
        Assert.That(schedule.IsReady(1, 13f), Is.False);
        Assert.That(schedule.IsReady(0, 16f), Is.True);
        Assert.That(schedule.IsReady(1, 16f), Is.True);
    }

    [Test]
    public void ActiveAttack_BlocksEveryOtherAttackEvenWithZeroCooldowns()
    {
        var schedule = new BeholderAttackScheduler();
        Assert.That(schedule.TryStart(0, 0f, 0f, 0f), Is.True);
        Assert.That(schedule.TryStart(1, 0f, 0f, 10f), Is.False);
        schedule.Complete();
        Assert.That(schedule.TryStart(1, 0f, 0f, 10f), Is.True);
    }

    [Test]
    public void InterruptedAttack_KeepsAlreadyStartedCooldowns()
    {
        var schedule = new BeholderAttackScheduler();
        schedule.TryStart(0, 10f, 8f, 0f);
        schedule.Complete();
        Assert.That(schedule.IsReady(0, 9f), Is.False);
        Assert.That(schedule.IsReady(0, 10f), Is.True);
        Assert.That(schedule.IsReady(1, 7f), Is.False);
    }

    [Test]
    public void Priority_SelectsHighestEligibleReadyAttackAndUsesListOrderForTies()
    {
        var attacks = new[]
        {
            new BeholderAttackController.AttackDefinition { priority = 10 },
            new BeholderAttackController.AttackDefinition { priority = 100 },
            new BeholderAttackController.AttackDefinition { priority = 100 }
        };
        var schedule = new BeholderAttackScheduler();
        Assert.That(schedule.SelectReady(attacks, i => true, 0f), Is.EqualTo(1));
        Assert.That(schedule.SelectReady(attacks, i => i != 1, 0f), Is.EqualTo(2));
        attacks[2].enabled = false;
        Assert.That(schedule.SelectReady(attacks, i => i != 1, 0f), Is.Zero);
        schedule.TryStart(1, 10f, 8f, 0f);
        schedule.Complete();
        Assert.That(schedule.SelectReady(attacks, i => true, 8f), Is.Zero);
    }

    [Test]
    public void EveryTentacleMask_OnlyEnablesItsBranch()
    {
        var guids = AssetDatabase.FindAssets("t:AvatarMask", new[] { Animations + "/AvatarMasks" });
        Assert.That(guids.Length, Is.EqualTo(6));
        foreach (string guid in guids)
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(AssetDatabase.GUIDToAssetPath(guid));
            int active = 0;
            for (int i = 0; i < mask.transformCount; i++)
            {
                if (!mask.GetTransformActive(i)) continue;
                active++;
                string path = mask.GetTransformPath(i);
                Assert.That(path.Contains("/Root." + mask.name), Is.True, path);
            }
            Assert.That(active, Is.GreaterThan(1), mask.name);
        }
    }

    [Test]
    public void AnimationEvent_EmitsAtFrame66AndMaskedLayerKeepsBaseController()
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Animations + "/Attack_Eye_03.anim");
        AnimationEvent signal = AnimationUtility.GetAnimationEvents(clip).Single(e => e.functionName == "OnBeholderAttackEffect");
        Assert.That(signal.time * clip.frameRate, Is.EqualTo(66f).Within(0.0001f));
        Assert.That(signal.stringParameter, Is.EqualTo("Tentacule_03.L"));
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/WorstKnights/Models/Enemies/Beholder/Beholder_Anim_Controller.controller");
        var layer = controller.layers.Single(l => l.name == "Tentacule_03_L");
        Assert.That(layer.avatarMask.name, Is.EqualTo("Tentacule_03.L"));
        Assert.That(layer.defaultWeight, Is.Zero);
        Assert.That(controller.layers[0].stateMachine.states.Any(s => s.state.name == "Idle"), Is.True);
        Assert.That(layer.stateMachine.states.Single(s => s.state.name == clip.name).state.motion, Is.EqualTo(clip));
    }

    [Test]
    public void BeholderPrefab_UsesControllerAndRetiresLegacyAttackDialogueBank()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beholder/Beholder.prefab");
        Assert.That(prefab.GetComponent<BeholderAttackController>(), Is.Not.Null);
        var data = new SerializedObject(prefab.GetComponent<BeholderAttackController>());
        Assert.That(data.FindProperty("globalCooldown").floatValue, Is.EqualTo(8f));
        var definition = data.FindProperty("attacks").GetArrayElementAtIndex(0);
        Assert.That(definition.FindPropertyRelative("cooldown").floatValue, Is.EqualTo(10f));
        Assert.That(definition.FindPropertyRelative("tentacleRoot").objectReferenceValue.name, Is.EqualTo("Root.Tentacule_03.L"));
        var dialogue = new SerializedObject(prefab.GetComponent<BeholderDialogueController>());
        var banks = dialogue.FindProperty("linesByState");
        for (int i = 0; i < banks.arraySize; i++)
            Assert.That(banks.GetArrayElementAtIndex(i).FindPropertyRelative("state").intValue, Is.Not.EqualTo((int)EnemyState.Attack));
    }
}
