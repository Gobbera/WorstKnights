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
    [Test]
    public void AttackTurning_UpdatesFacingAtTheVisionEdgeAndReacquiresWithoutSnappingTheBeam()
    {
        WithTurningAttack((brain, attack, vision, target) =>
        {
            Vector3 startPosition = brain.transform.position;
            Vector3 facing = vision.EyeForward;
            float edgeAngle = vision.ViewAngle * 0.5f - 5f;
            target.transform.position = vision.EyePosition + Quaternion.AngleAxis(edgeAngle, Vector3.up) * facing * 6f;
            Physics.SyncTransforms();
            Assert.That(vision.CanSeeTarget(target.GetComponent<PlayerHealth>(), out Vector3 visible), Is.True);
            brain.TrackCurrentTargetDuringAttack(0.02f);
            Assert.That(Vector3.Distance(brain.LastKnownTargetPosition, visible), Is.LessThan(0.001f));
            Assert.That(Vector3.Angle(facing, brain.transform.forward), Is.GreaterThan(1f));
            Physics.SyncTransforms();
            attack.TickAttack(0.02f);
            Vector3 beamDirection = (attack.LastSegment.End - attack.LastSegment.Start).normalized;
            Assert.That(Vector3.Angle(beamDirection, target.transform.position - attack.LastSegment.Start), Is.GreaterThan(10f), "Body turning must preserve the slow beam's aim error");

            Vector3 lastSeen = brain.LastKnownTargetPosition;
            target.transform.position = vision.EyePosition + Quaternion.AngleAxis(edgeAngle + 25f, Vector3.up) * facing * 6f;
            Physics.SyncTransforms();
            var player = target.GetComponent<PlayerHealth>();
            Assert.That(vision.CanSeeTarget(player, out _), Is.False, "Player crossed the cone edge");
            brain.TrackCurrentTargetDuringAttack(0.02f);
            Assert.That(brain.LastKnownTargetPosition, Is.EqualTo(lastSeen), "No live hidden position is used for turning");
            for (int i = 0; i < 30; i++)
            {
                Physics.SyncTransforms();
                brain.TrackCurrentTargetDuringAttack(0.02f);
                attack.TickAttack(0.02f);
            }
            Physics.SyncTransforms();
            Assert.That(vision.CanSeeTarget(player, out visible), Is.True, "Turning toward last seen brings the cone back over the player");
            Assert.That(Vector3.Distance(brain.LastKnownTargetPosition, visible), Is.LessThan(0.001f));
            Assert.That(brain.transform.position, Is.EqualTo(startPosition), "Can Move remains disabled");
            Assert.That(attack.Phase, Is.EqualTo(BeholderAttackPhase.Execution));
        });
    }

    [TestCase("wall")]
    [TestCase("range")]
    [TestCase("dead")]
    public void AttackTurning_DoesNotRefreshFromAnUnseenOrInvalidTarget(string reason)
    {
        WithTurningAttack((brain, attack, vision, target) =>
        {
            Vector3 lastSeen = brain.LastKnownTargetPosition;
            Vector3 facing = vision.EyeForward;
            target.transform.position = vision.EyePosition + Quaternion.AngleAxis(25f, Vector3.up) * facing * 6f;
            GameObject wall = null;
            try
            {
                if (reason == "wall")
                {
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = Vector3.Lerp(vision.EyePosition, target.transform.position, 0.5f);
                }
                else if (reason == "range") target.transform.position = vision.EyePosition + facing * (vision.ViewDistance + 3f);
                else Object.DestroyImmediate(target.GetComponent<PlayerHealth>());
                Physics.SyncTransforms();
                brain.TrackCurrentTargetDuringAttack(0.1f);
                Assert.That(brain.LastKnownTargetPosition, Is.EqualTo(lastSeen));
                Assert.That(attack.IsAttacking, Is.True);
            }
            finally { if (wall != null) Object.DestroyImmediate(wall); }
        });
    }

    private void WithTurningAttack(Action<EnemyBrain, BeholderAttackController, EnemyVision, GameObject> verify)
    {
        var stage = new GameObject("Attack turning fixture");
        stage.SetActive(false);
        var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), stage.transform);
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BeholderLightningBeamAttack>(Folder + "Attacks/Attack_Eye_03.R.asset"));
        configurations.Add(settings);
        var attack = enemy.GetComponent<BeholderAttackController>();
        try
        {
            settings.attackAudio = null; settings.dialogues = Array.Empty<AudioCue>();
            settings.duration = 10f; settings.damagePerTick = 0f; settings.canMove = false;
            settings.trackingSpeed = 20f; settings.aimMode = BeholderBeamAimMode.FollowVisibleTarget;
            enemy.transform.position = new Vector3(20000f, 0f, 20000f);
            enemy.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
            typeof(BeholderAttackController).GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(attack, new[] { new BeholderAttackController.AttackDefinition { settings = settings } });
            stage.SetActive(true);
            // Plain EditMode has no physics step to commit Rigidbody.MoveRotation; use the motor's transform path.
            typeof(EnemyMotor).GetField("enemySetup", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(enemy.GetComponent<EnemyMotor>(), null);
            var animator = enemy.GetComponent<EnemySetup>().Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0f);
            var vision = enemy.GetComponent<EnemyVision>();
            var data = new SerializedObject(vision);
            data.FindProperty("viewAngle").floatValue = 110f;
            data.ApplyModifiedPropertiesWithoutUndo();
            target.transform.position = vision.EyePosition + vision.EyeForward * 6f;
            var player = target.AddComponent<PlayerHealth>();
            typeof(PlayerHealth).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
            var brain = enemy.GetComponent<EnemyBrain>();
            brain.ChangeState(EnemyState.Attack);
            typeof(EnemyBrain).GetField("currentTarget", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(brain, player);
            brain.SetLastKnownTargetPosition(target.transform.position);
            Physics.SyncTransforms();
            Assert.That(attack.TryStartAttack(player), Is.True);
            animator.Update(0f); animator.Update(settings.ResolveActivationTime() + 0.05f); attack.TickAttack(0f);
            Assert.That(attack.Phase, Is.EqualTo(BeholderAttackPhase.Execution));
            verify(brain, attack, vision, target);
        }
        finally
        {
            attack.CancelAttack(); attack.Presentation.Dispose();
            Object.DestroyImmediate(target); Object.DestroyImmediate(stage);
        }
    }
}
