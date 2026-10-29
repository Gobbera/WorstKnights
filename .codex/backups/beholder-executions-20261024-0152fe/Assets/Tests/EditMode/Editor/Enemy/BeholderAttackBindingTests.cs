using System;
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

    [Test]
    public void RenamingTheClip_DoesNotChangeItsAnimatorBinding()
    {
        var clip = new AnimationClip { name = "Attack_Eye_03.L" };
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder + "Animations/AvatarMasks/Tentacule_03.L.mask");
        try
        {
            var attack = new BeholderAttackController.AttackDefinition { animation = clip, mask = mask };
            int hash = attack.StateHash;
            clip.name = "Renamed attack clip";
            Assert.That(attack.StateHash, Is.EqualTo(hash));
        }
        finally { Object.DestroyImmediate(clip); }
    }

    [Test]
    public void InspectorValidation_ExplainsMissingLayerStateAndWrongMotion()
    {
        var data = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<BeholderAttackController>());
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "Beholder_Anim_Controller.controller");
        SerializedProperty attack = data.FindProperty("attacks").GetArrayElementAtIndex(0);
        Assert.That(BeholderAttackControllerEditor.GetConfigurationIssue(attack, controller), Is.Null);
        attack.FindPropertyRelative("animatorState").stringValue = "MissingState";
        Assert.That(BeholderAttackControllerEditor.GetConfigurationIssue(attack, controller), Does.Contain("Tentacule_03_L.MissingState"));
        data.Update();
        attack.FindPropertyRelative("animation").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Animations/Attack_Eye_03.R.anim");
        Assert.That(BeholderAttackControllerEditor.GetConfigurationIssue(attack, controller), Does.Contain("Motion"));
        data.Update();
        attack.FindPropertyRelative("mask").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder + "Animations/AvatarMasks/Tentacule_01.L.mask");
        Assert.That(BeholderAttackControllerEditor.GetConfigurationIssue(attack, controller), Does.Contain("Tentacule_01_L"));
    }

    [TestCase("L")]
    [TestCase("R")]
    public void BothAttacks_StartAndFinishWhileAnInvalidEntryIsSkipped(string side)
    {
        var stage = new GameObject("Attack binding test stage");
        stage.SetActive(false);
        var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), stage.transform);
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var emitter = enemy.GetComponent<BeholderLightningVfxTest>();
        try
        {
            enemy.transform.position = new Vector3(40000f, 0f, 40000f);
            enemy.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
            var attacks = enemy.GetComponent<BeholderAttackController>();
            var definition = new BeholderAttackController.AttackDefinition
            {
                name = "Attack_Eye_03." + side,
                animation = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Animations/Attack_Eye_03." + side + ".anim"),
                mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder + "Animations/AvatarMasks/Tentacule_03." + side + ".mask"),
                tentacleRoot = Array.Find(enemy.GetComponentsInChildren<Transform>(true), t => t.name == "Root.Tentacule_03." + side)
            };
            var invalid = new BeholderAttackController.AttackDefinition
            {
                animation = definition.animation, mask = definition.mask, animatorState = "Missing", priority = int.MaxValue
            };
            typeof(BeholderAttackController).GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(attacks, new[] { invalid, definition });
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
            Assert.That(attacks.ActiveAttackIndex, Is.EqualTo(1));
            int layer = animator.GetLayerIndex(definition.LayerName);
            animator.Update(0f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(layer).fullPathHash, Is.EqualTo(definition.StateHash));
            Assert.That(animator.GetLayerWeight(layer), Is.EqualTo(1f));
            Assert.That(attacks.OwnsBone(definition.tentacleRoot), Is.True);
            animator.Update(2.8f);
            Tick(attacks);
            Assert.That(emitter.ShotSequence, Is.EqualTo(side == "L" ? 1 : 0), "Only the left clip has a VFX event");
            animator.Update(0.8f);
            Tick(attacks);
            Assert.That(attacks.IsAttacking, Is.False);
            Assert.That(attacks.GlobalCooldownRemaining, Is.GreaterThan(0f));
        }
        finally
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(stage);
        }
    }

    private static void Tick(BeholderAttackController controller)
    {
        typeof(BeholderAttackController).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
    }
}
