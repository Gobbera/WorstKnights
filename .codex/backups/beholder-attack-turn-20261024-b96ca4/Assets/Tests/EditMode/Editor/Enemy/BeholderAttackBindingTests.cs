using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class BeholderAttackBindingTests
{
    private const string Folder = "Assets/WorstKnights/Models/Enemies/Beholder/";
    private const string PrefabPath = "Assets/Prefabs/Enemies/Beholder/Beholder.prefab";
    private readonly List<Object> configurations = new List<Object>();
    [TearDown]
    public void Cleanup() { foreach (Object configuration in configurations) Object.DestroyImmediate(configuration); configurations.Clear(); }

    [Test]
    public void RenamingTheClip_DoesNotChangeItsAnimatorBinding()
    {
        var clip = new AnimationClip { name = "Attack_Eye_03.L" };
        var attack = ScriptableObject.CreateInstance<BeholderLightningStrikeAttack>();
        configurations.Add(clip); configurations.Add(attack);
        attack.animation = clip;
        attack.mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder + "Animations/AvatarMasks/Tentacule_03.L.mask");
        int hash = attack.StateHash;
        clip.name = "Renamed attack clip";
        Assert.That(attack.StateHash, Is.EqualTo(hash));
    }

    [Test]
    public void InspectorValidation_ExplainsMissingLayerStateAndWrongMotion()
    {
        var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<BeholderAttackController>().GetSettings(0));
        configurations.Add(settings);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "Beholder_Anim_Controller.controller");
        Assert.That(BeholderAttackControllerEditor.GetConfigurationIssue(settings, controller), Is.Null);
        settings.animatorState = "MissingState";
        Assert.That(BeholderAttackControllerEditor.GetConfigurationIssue(settings, controller), Does.Contain("Tentacule_03_L.MissingState"));
        settings.animatorState = "Attack";
        settings.animation = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Animations/Attack_Eye_03.R.anim");
        Assert.That(BeholderAttackControllerEditor.GetConfigurationIssue(settings, controller), Does.Contain("Motion"));
        settings.animation = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Animations/Attack_Eye_03.L.anim");
        settings.mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder + "Animations/AvatarMasks/Tentacule_01.L.mask");
        Assert.That(BeholderAttackControllerEditor.GetConfigurationIssue(settings, controller), Does.Contain("Tentacule_01_L"));
    }

    [TestCase("L", 0f)]
    [TestCase("R", 0f)]
    [TestCase("R", 1.5f)]
    public void BothAttacks_StartAndFinishWhileAnInvalidEntryIsSkipped(string side, float recovery)
    {
        var stage = new GameObject("Attack binding test stage");
        stage.SetActive(false);
        var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), stage.transform);
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            enemy.transform.position = new Vector3(40000f, 0f, 40000f);
            enemy.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
            var attacks = enemy.GetComponent<BeholderAttackController>();
            var definition = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BeholderAttackDefinition>(Folder + "Attacks/Attack_Eye_03." + side + ".asset"));
            configurations.Add(definition);
            definition.recoveryDuration = recovery;
            definition.attackAudio = null; definition.dialogues = Array.Empty<AudioCue>();
            var invalid = Object.Instantiate(definition);
            configurations.Add(invalid);
            invalid.animatorState = "Missing"; invalid.priority = int.MaxValue;
            typeof(BeholderAttackController).GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(attacks, new[] {
                    new BeholderAttackController.AttackDefinition { settings = invalid },
                    new BeholderAttackController.AttackDefinition { settings = definition }
                });
            stage.SetActive(true);
            Animator animator = enemy.GetComponent<EnemySetup>().Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents = true;
            animator.Rebind();
            animator.Update(0f);
            EnemyVision vision = enemy.GetComponent<EnemyVision>();
            target.transform.position = vision.EyePosition + vision.EyeForward * 6f;
            var player = target.AddComponent<PlayerHealth>();
            Physics.SyncTransforms();
            enemy.GetComponent<EnemyBrain>().ChangeState(EnemyState.Attack);
            Assert.That(attacks.TryStartAttack(player), Is.True, side);
            Assert.That(attacks.GlobalCooldownRemaining, Is.Zero, "Cooldown starts when execution ends");
            Assert.That(attacks.ActiveAttackIndex, Is.EqualTo(1));
            int layer = animator.GetLayerIndex(definition.LayerName);
            animator.Update(0f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(layer).fullPathHash, Is.EqualTo(definition.StateHash));
            Assert.That(animator.GetLayerWeight(layer), Is.EqualTo(1f));
            var root = Array.Find(enemy.GetComponentsInChildren<Transform>(true), t => t.name == definition.tentacleRootName);
            Assert.That(attacks.OwnsBone(root), Is.True);
            animator.Update(2.8f);
            attacks.TickAttack(0f);
            Assert.That(Vector3.Distance(attacks.ActiveContext.OriginPosition, enemy.transform.position), Is.LessThan(10f), "Eye origin respects the complete imported transform hierarchy");
            Assert.That(attacks.LastSegment.Hit.collider, Is.EqualTo(target.GetComponent<Collider>()));
            Assert.That(attacks.Presentation.ShotSequence, Is.EqualTo(side == "L" ? 1 : 0));
            if (side == "R")
            {
                float heldTime = animator.GetCurrentAnimatorStateInfo(layer).normalizedTime;
                Assert.That(attacks.Presentation.Beam.IsPlaying, Is.True);
                for (int i = 0; i < 2; i++) { animator.Update(1f); attacks.TickAttack(1f); }
                Assert.That(attacks.IsAttacking, Is.True, "Active beam outlasts the original clip duration");
                Assert.That(animator.GetCurrentAnimatorStateInfo(layer).normalizedTime, Is.EqualTo(heldTime).Within(0.001f));
                Assert.That(animator.speed, Is.EqualTo(1f), "Base animation must not be paused");
                Assert.That(attacks.GlobalCooldownRemaining, Is.Zero, "Active beam does not consume cooldown");
                attacks.TickAttack(1.01f);
                Assert.That(attacks.Presentation.Beam.IsPlaying, Is.False);
            }
            Assert.That(attacks.GlobalCooldownRemaining, Is.EqualTo(8f).Within(0.01f));
            animator.Update(0.8f);
            attacks.TickAttack(0.8f);
            if (recovery > 0.8f)
            {
                Assert.That(attacks.IsAttacking, Is.True, "Minimum recovery keeps the attack lock after the clip ends");
                Assert.That(attacks.CanStartAttack, Is.False);
                attacks.TickAttack(recovery - 0.8f + 0.01f);
            }
            Assert.That(attacks.IsAttacking, Is.False);
            Assert.That(attacks.GlobalCooldownRemaining, Is.GreaterThan(0f));
        }
        finally
        {
            var actor = enemy.GetComponent<BeholderAttackController>();
            actor.ActiveContext?.Dispose(); actor.Presentation.Dispose();
            Object.DestroyImmediate(target); Object.DestroyImmediate(stage);
        }
    }
}
