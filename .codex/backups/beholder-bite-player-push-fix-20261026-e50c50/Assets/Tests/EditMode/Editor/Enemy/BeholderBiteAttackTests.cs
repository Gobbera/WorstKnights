using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class BeholderBiteAttackTests
{
    const string Folder = "Assets/WorstKnights/Models/Enemies/Beholder/";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void Mask_EnablesOnlyTheFourteenFaceAndHipBones()
    {
        var settings = AssetDatabase.LoadAssetAtPath<BeholderBiteAttack>(Folder + "Attacks/Attack_Bite.asset");
        var expected = new[] { "Hip", "Center Eye", "Cheek.L", "Cheek.R", "Chin", "Forehead", "Jaw.L", "Jaw.R", "Nose", "Tongue_01", "Tongue_02", "Tongue_03", "Tongue_04.L", "Tongue_04.R" };
        var active = new List<string>();
        for (int i = 0; i < settings.mask.transformCount; i++)
            if (settings.mask.GetTransformActive(i)) active.Add(settings.mask.GetTransformPath(i));
        CollectionAssert.AreEquivalent(expected, active.Select(path => path.Split('/').Last()).ToArray());
        Assert.That(active.All(path => path.StartsWith("Beholder_Armature/Root/Hip") && !path.Contains("Tentacule")), Is.True);
        Assert.That(settings.RequiresVfx || settings.HasPresentation, Is.False);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "Beholder_Anim_Controller.controller");
        Assert.That(BeholderAttackControllerEditor.GetConfigurationIssue(settings, controller), Is.Null);
        Assert.That(settings.animation.length, Is.EqualTo(2.5f).Within(0.001f));
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beholder/Beholder.prefab")
            .GetComponent<BeholderAttackController>().GetSettings(1), Is.SameAs(settings));
    }

    [Test]
    public void Range_UsesTheTargetColliderSurfaceAndRejectsInvalidTargets()
    {
        var settings = ScriptableObject.CreateInstance<BeholderBiteAttack>();
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var player = target.AddComponent<PlayerHealth>();
        typeof(PlayerHealth).GetMethod("Awake", Private).Invoke(player, null);
        try
        {
            settings.range = 3f;
            target.transform.position = Vector3.forward * 3.4f;
            Physics.SyncTransforms();
            Assert.That(settings.MeetsTargetConditions(Vector3.zero, player), Is.True, "Collider is in range although its center is outside");
            target.transform.position = Vector3.forward * 3.6f;
            Physics.SyncTransforms();
            Assert.That(settings.MeetsTargetConditions(Vector3.zero, player), Is.False);
            Object.DestroyImmediate(player);
            Assert.That(settings.MeetsTargetConditions(Vector3.zero, player), Is.False);
        }
        finally { Object.DestroyImmediate(target); Object.DestroyImmediate(settings); }
    }

    [Test]
    public void Selection_ChoosesBiteUpCloseAndLeavesEyeAttacksAvailableAtDistance()
    {
        WithActor((enemy, attack, animator, target, bite) =>
        {
            var vision = enemy.GetComponent<EnemyVision>();
            target.transform.position = vision.EyePosition + vision.EyeForward * 10f;
            Physics.SyncTransforms();
            Assert.That(bite.MeetsTargetConditions(enemy.transform.Find("Beholder_Armature/Root/Hip/Chin").position, target.GetComponent<PlayerHealth>()), Is.False);
            Assert.That(attack.TryStartAttack(target.GetComponent<PlayerHealth>()), Is.True);
            Assert.That(attack.ActiveAttackIndex, Is.EqualTo(1), "Ineligible high-priority bite does not block the beam");
        });
        WithActor((enemy, attack, animator, target, bite) =>
        {
            Assert.That(attack.TryStartAttack(target.GetComponent<PlayerHealth>()), Is.True);
            Assert.That(attack.ActiveAttackIndex, Is.EqualTo(2));
            Assert.That(attack.ActiveAttack, Is.SameAs(bite));
        });
    }

    [Test]
    public void Bite_PlaysWithoutVfxAndStartsCooldownOnlyWhenItsAnimationFinishes()
    {
        WithActor((enemy, attack, animator, target, bite) =>
        {
            Assert.That(attack.TryStartAttack(target.GetComponent<PlayerHealth>()), Is.True);
            animator.Update(0f); attack.TickAttack(0f);
            Assert.That(attack.Phase, Is.EqualTo(BeholderAttackPhase.Execution));
            Assert.That(attack.OwnsBone(enemy.transform.Find("Beholder_Armature/Root/Hip")), Is.True);
            Assert.That(attack.OwnsBone(enemy.transform.Find("Beholder_Armature/Root/Hip/Root.Tentacule_03.R")), Is.False);
            animator.Update(1f); attack.TickAttack(100f);
            Assert.That(attack.Phase, Is.EqualTo(BeholderAttackPhase.Execution), "Animation clock controls the bite duration");
            Assert.That(attack.GlobalCooldownRemaining, Is.Zero);
            Assert.That(attack.TryStartAttack(target.GetComponent<PlayerHealth>()), Is.False);
            Assert.That(attack.Presentation.Beam, Is.Null); Assert.That(attack.Presentation.Strike, Is.Null);
            animator.Update(1.6f); attack.TickAttack(0f);
            Assert.That(attack.Phase, Is.EqualTo(BeholderAttackPhase.Recovery));
            Assert.That(attack.GlobalCooldownRemaining, Is.EqualTo(8f).Within(0.01f));
            attack.TickAttack(0f);
            Assert.That(attack.IsAttacking, Is.False);
            Assert.That(animator.speed, Is.EqualTo(1f));
        });
    }

    [Test]
    public void ProceduralTracking_YieldsHipAndCenterEyeButLeavesTentacleEyesAvailable()
    {
        WithActor((enemy, attack, animator, target, bite) =>
        {
            Assert.That(attack.TryStartAttack(target.GetComponent<PlayerHealth>()), Is.True);
            animator.Update(0f); animator.Update(1f); attack.TickAttack(0f);
            var procedural = enemy.GetComponent<BeholderProceduralAnimationController>();
            typeof(BeholderProceduralAnimationController).GetMethod("Awake", Private).Invoke(procedural, null);
            var hip = enemy.transform.Find("Beholder_Armature/Root/Hip");
            var center = hip.Find("Center Eye");
            var eye = enemy.GetComponentsInChildren<Transform>().First(bone => bone.name == "Eye.Tentacule_03.R");
            Quaternion hipPose = hip.localRotation, centerPose = center.localRotation, eyePose = eye.localRotation;
            typeof(BeholderProceduralAnimationController).GetField("currentAimPosition", Private).SetValue(procedural, hip.position + enemy.transform.right * 10f + Vector3.up * 4f);
            typeof(BeholderProceduralAnimationController).GetField("currentEyeWeight", Private).SetValue(procedural, 1f);
            typeof(BeholderProceduralAnimationController).GetField("hasCurrentAimPosition", Private).SetValue(procedural, true);
            typeof(BeholderProceduralAnimationController).GetMethod("ApplyHipTracking", Private).Invoke(procedural, new object[] { true });
            typeof(BeholderProceduralAnimationController).GetMethod("ApplyEyeTracking", Private).Invoke(procedural, null);
            Assert.That(Quaternion.Angle(hip.localRotation, hipPose), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(center.localRotation, centerPose), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(eye.localRotation, eyePose), Is.GreaterThan(1f));
        });
    }

    [Test]
    public void Cancellation_EndsBiteWithoutVfxAndStartsItsCooldown()
    {
        WithActor((enemy, attack, animator, target, bite) =>
        {
            Assert.That(attack.TryStartAttack(target.GetComponent<PlayerHealth>()), Is.True);
            animator.Update(0f); attack.TickAttack(0f);
            var execution = attack.ActiveExecution;
            attack.CancelAttack();
            execution.Tick(1f);
            Assert.That(execution.IsFinished, Is.True);
            Assert.That(attack.IsAttacking, Is.False);
            Assert.That(attack.GlobalCooldownRemaining, Is.EqualTo(8f).Within(0.01f));
            Assert.That(attack.Presentation.Beam, Is.Null); Assert.That(attack.Presentation.Strike, Is.Null);
        });
    }

    [TestCase("hit")]
    [TestCase("range")]
    [TestCase("wall")]
    [TestCase("cancel")]
    public void Damage_AppliesTwentyOnceAtImpactAndCanBeAvoided(string outcome)
    {
        WithActor((enemy, attack, animator, target, bite) =>
        {
            bite.damage = 20f; bite.hitTime = 1f;
            var player = target.GetComponent<PlayerHealth>();
            float initial = player.CurrentHealth;
            Assert.That(attack.TryStartAttack(player), Is.True);
            animator.Update(0f); attack.TickAttack(0f);
            var execution = attack.ActiveExecution;
            animator.Update(0.5f); attack.TickAttack(100f);
            Assert.That(player.CurrentHealth, Is.EqualTo(initial), "No damage before the impact animation time");
            GameObject wall = null;
            try
            {
                if (outcome == "cancel") attack.CancelAttack();
                animator.Update(0.51f);
                if (outcome == "range") target.transform.position = attack.ActiveContext.OriginPosition + enemy.transform.forward * 10f;
                if (outcome == "wall")
                {
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = Vector3.Lerp(attack.ActiveContext.OriginPosition, target.transform.position, 0.5f);
                    wall.transform.localScale = Vector3.one * 0.5f;
                }
                Physics.SyncTransforms();
                if (outcome == "cancel") execution.Tick(1f);
                else attack.TickAttack(0f);
                float expected = initial - (outcome == "hit" ? 20f : 0f);
                Assert.That(player.CurrentHealth, Is.EqualTo(expected));
                if (wall != null) { Object.DestroyImmediate(wall); wall = null; }
                if (attack.ActiveContext != null)
                    target.transform.position = attack.ActiveContext.OriginPosition + enemy.transform.forward * 1.5f;
                Physics.SyncTransforms();
                for (int i = 0; i < 3; i++) { animator.Update(0.1f); attack.TickAttack(0f); }
                Assert.That(player.CurrentHealth, Is.EqualTo(expected), "A missed or successful bite never applies a later second hit");
            }
            finally { if (wall != null) Object.DestroyImmediate(wall); }
        });
    }

    [Test]
    public void Damage_AlsoWorksWhenTheMouthIsAlreadyInsideTheTargetCollider()
    {
        WithActor((enemy, attack, animator, target, bite) =>
        {
            var player = target.GetComponent<PlayerHealth>();
            float initial = player.CurrentHealth;
            Assert.That(attack.TryStartAttack(player), Is.True);
            animator.Update(0f); attack.TickAttack(0f);
            animator.Update(1.01f);
            target.transform.position = attack.ActiveContext.OriginPosition;
            Physics.SyncTransforms(); attack.TickAttack(0f);
            Assert.That(player.CurrentHealth, Is.EqualTo(initial - 20f));
        });
    }

    [TestCase("hit")]
    [TestCase("immune")]
    [TestCase("zero")]
    [TestCase("miss")]
    public void Knockback_PushesAwayOnlyForAnAcceptedBite(string outcome)
    {
        WithActor((enemy, attack, animator, target, bite) =>
        {
            var body = AttachKnockbackTarget(target);
            var player = target.GetComponent<PlayerHealth>();
            var movement = target.GetComponent<PlayerMovement>();
            bite.knockbackStrength = outcome == "zero" ? 0f : 2.5f;
            bite.knockbackControlLockDuration = 0.12f;
            if (outcome == "immune")
            {
                var health = new SerializedObject(player);
                health.FindProperty("damageImmunityDuration").floatValue = 10f;
                health.ApplyModifiedPropertiesWithoutUndo();
                player.ReceiveDamage(new DamageInfo(1f, null, CombatAlignment.Neutral, target.transform.position, Vector3.zero));
            }
            float initial = player.CurrentHealth;
            Assert.That(attack.TryStartAttack(player), Is.True);
            animator.Update(0f); attack.TickAttack(0f);
            animator.Update(1.01f);
            if (outcome == "miss") target.transform.position = attack.ActiveContext.OriginPosition + enemy.transform.forward * 10f;
            Physics.SyncTransforms(); attack.TickAttack(0f);
            bool damaged = outcome == "hit" || outcome == "zero";
            bool pushed = outcome == "hit";
            Assert.That(player.CurrentHealth, Is.EqualTo(initial - (damaged ? 20f : 0f)));
            Assert.That(movement.IsMovementControlLocked, Is.EqualTo(pushed));
            Vector3 pendingVelocity = body.GetAccumulatedForce() / body.mass * Time.fixedDeltaTime;
            if (pushed)
            {
                Vector3 away = Vector3.ProjectOnPlane(target.transform.position - enemy.transform.position, Vector3.up).normalized;
                Assert.That(pendingVelocity.magnitude, Is.EqualTo(2.5f).Within(0.01f));
                Assert.That(Vector3.Dot(pendingVelocity.normalized, away), Is.GreaterThan(0.99f));
                Assert.That(pendingVelocity.y, Is.EqualTo(0f).Within(0.001f));
            }
            else Assert.That(pendingVelocity.sqrMagnitude, Is.LessThan(0.0001f));
        });
    }

    [Test]
    public void Knockback_OwnerRpcAppliesOneImpulseWithoutGenericDamagePush()
    {
        WithActor((enemy, attack, animator, target, bite) =>
        {
            var body = AttachKnockbackTarget(target);
            var player = target.GetComponent<PlayerHealth>();
            var health = new SerializedObject(player);
            health.FindProperty("damageKnockback").FindPropertyRelative("enabled").boolValue = true;
            health.FindProperty("damageKnockback").FindPropertyRelative("horizontalStrength").floatValue = 20f;
            health.ApplyModifiedPropertiesWithoutUndo();
            float initial = player.CurrentHealth;
            typeof(PlayerHealth).GetMethod("RpcReceiveDamageWithKnockback", Private).Invoke(player, new object[] {
                20f, 0, (int)CombatAlignment.Enemy, (int)PlayerDamageAnimationType.None, (int)PlayerCameraImpactType.None,
                target.transform.position, Vector3.forward, Vector3.forward * 2.5f, 0.12f });
            Assert.That(player.CurrentHealth, Is.EqualTo(initial - 20f));
            Vector3 pendingVelocity = body.GetAccumulatedForce() / body.mass * Time.fixedDeltaTime;
            Assert.That(Vector3.Distance(pendingVelocity, Vector3.forward * 2.5f), Is.LessThan(0.01f));
        });
    }

    static Rigidbody AttachKnockbackTarget(GameObject target)
    {
        var body = target.GetComponent<Rigidbody>() ?? target.AddComponent<Rigidbody>();
        body.isKinematic = false; body.useGravity = false; body.linearVelocity = Vector3.zero;
        var movement = target.GetComponent<PlayerMovement>() ?? target.AddComponent<PlayerMovement>(); movement.enabled = false;
        typeof(PlayerMovement).GetField("rb", Private).SetValue(movement, body);
        typeof(PlayerHealth).GetField("playerMovement", Private).SetValue(target.GetComponent<PlayerHealth>(), movement);
        return body;
    }

    static void WithActor(Action<GameObject, BeholderAttackController, Animator, GameObject, BeholderBiteAttack> verify)
    {
        var stage = new GameObject("Bite fixture"); stage.SetActive(false);
        var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beholder/Beholder.prefab"), stage.transform);
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var attack = enemy.GetComponent<BeholderAttackController>();
        var configurations = new[] { "Attack_Eye_03.L", "Attack_Eye_03.R", "Attack_Bite" }
            .Select(name => Object.Instantiate(AssetDatabase.LoadAssetAtPath<BeholderAttackDefinition>(Folder + "Attacks/" + name + ".asset"))).ToArray();
        try
        {
            foreach (var settings in configurations) { settings.attackAudio = null; settings.dialogues = Array.Empty<AudioCue>(); }
            enemy.transform.position = new Vector3(30000f, 0f, 30000f);
            enemy.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
            typeof(BeholderAttackController).GetField("attacks", Private).SetValue(attack, configurations.Select(settings => new BeholderAttackController.AttackDefinition { settings = settings }).ToArray());
            stage.SetActive(true);
            var animator = enemy.GetComponent<EnemySetup>().Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0f);
            var chin = enemy.transform.Find("Beholder_Armature/Root/Hip/Chin");
            target.transform.position = chin.position + enemy.GetComponent<EnemyVision>().EyeForward * 1.5f;
            var player = target.AddComponent<PlayerHealth>(); typeof(PlayerHealth).GetMethod("Awake", Private).Invoke(player, null);
            var health = new SerializedObject(player);
            health.FindProperty("damageImmunityDuration").floatValue = 0f;
            health.FindProperty("damageKnockback").FindPropertyRelative("enabled").boolValue = false;
            health.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms(); enemy.GetComponent<EnemyBrain>().ChangeState(EnemyState.Attack);
            verify(enemy, attack, animator, target, (BeholderBiteAttack)configurations[2]);
        }
        finally
        {
            attack.CancelAttack(); attack.Presentation.Dispose();
            Object.DestroyImmediate(target); Object.DestroyImmediate(stage);
            foreach (var settings in configurations) Object.DestroyImmediate(settings);
        }
    }
}
