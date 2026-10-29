using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class BeholderAttackExecutionTests
{
    private GameObject root, target, wall;
    private BeholderAttackController controller;
    private BeholderLightningBeamAttack definition;
    private BeholderAttackContext context;
    private BeholderAttackExecution execution;
    private PlayerHealth player;
    private Vector3 targetPosition;

    [SetUp]
    public void Setup()
    {
        root = new GameObject("Execution fixture");
        root.transform.position = new Vector3(70000f, 0f, 70000f);
        var origin = new GameObject("Origin");
        origin.transform.SetParent(root.transform, false);
        origin.transform.localPosition = Vector3.up * 1.4f;
        var vision = root.AddComponent<EnemyVision>();
        controller = root.AddComponent<BeholderAttackController>();
        target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        targetPosition = origin.transform.position + Vector3.forward * 10f;
        target.transform.position = targetPosition;
        player = target.AddComponent<PlayerHealth>();
        typeof(PlayerHealth).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(player, null);
        var health = new SerializedObject(player);
        health.FindProperty("damageImmunityDuration").floatValue = 0f;
        health.FindProperty("damageKnockback").FindPropertyRelative("enabled").boolValue = false;
        health.ApplyModifiedPropertiesWithoutUndo();
        definition = ScriptableObject.CreateInstance<BeholderLightningBeamAttack>();
        definition.originName = "Origin";
        definition.duration = 5f;
        definition.damagePerTick = 2f;
        definition.hitInterval = 0.5f;
        definition.vfxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFX/Lightning Strike/Variations/Lightning Beam Purple.prefab").GetComponent<LightningBeamController>();
        context = new BeholderAttackContext(controller, definition, vision, player, targetPosition);
        execution = definition.CreateExecution(context);
        Physics.SyncTransforms();
    }
    [TearDown]
    public void Cleanup()
    {
        execution?.Cancel(); execution?.Dispose(); context?.Dispose();
        controller.Presentation.Dispose();
        Object.DestroyImmediate(root); Object.DestroyImmediate(target);
        if (wall != null) Object.DestroyImmediate(wall);
        Object.DestroyImmediate(definition);
    }

    [Test]
    public void Beam_UsesTickIntervalsAndFirstColliderWithoutAccumulatingMissedDamage()
    {
        float initial = player.CurrentHealth;
        execution.Begin();
        Assert.That(player.CurrentHealth, Is.EqualTo(initial - 2f));
        execution.Tick(0.2f);
        Assert.That(player.CurrentHealth, Is.EqualTo(initial - 2f));
        target.transform.position += Vector3.right * 4f;
        Physics.SyncTransforms();
        execution.Tick(0.4f);
        target.transform.position = targetPosition;
        Physics.SyncTransforms();
        execution.Tick(0.2f);
        Assert.That(player.CurrentHealth, Is.EqualTo(initial - 2f));
        execution.Tick(0.31f);
        Assert.That(player.CurrentHealth, Is.EqualTo(initial - 4f));
        wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = Vector3.Lerp(context.OriginPosition, targetPosition, 0.5f);
        Physics.SyncTransforms();
        execution.Tick(0.5f);
        Assert.That(context.LastSegment.Hit.collider, Is.EqualTo(wall.GetComponent<Collider>()));
        Assert.That(player.CurrentHealth, Is.EqualTo(initial - 4f));
        execution.Cancel();
        execution.Tick(1f);
        Assert.That(controller.Presentation.Beam.IsPlaying, Is.False);
        Assert.That(player.CurrentHealth, Is.EqualTo(initial - 4f));
    }

    [Test]
    public void SharedAsset_KeepsSeparateTimersAndVisualsForEachBeholder()
    {
        var other = new GameObject("Second execution owner");
        var otherOrigin = new GameObject("Origin");
        otherOrigin.transform.SetParent(other.transform);
        otherOrigin.transform.position = context.OriginPosition + Vector3.right * 3f;
        var otherController = other.AddComponent<BeholderAttackController>();
        var otherContext = new BeholderAttackContext(otherController, definition, null, player, targetPosition);
        var otherExecution = definition.CreateExecution(otherContext);
        try
        {
            definition.damagePerTick = 0f;
            execution.Begin(); otherExecution.Begin();
            Assert.That(controller.Presentation.Beam, Is.Not.SameAs(otherController.Presentation.Beam));
            execution.Tick(4f); otherExecution.Tick(1f);
            execution.Tick(1.1f);
            Assert.That(execution.IsFinished, Is.True);
            Assert.That(otherExecution.IsFinished, Is.False);
            Assert.That(otherController.Presentation.Beam.IsPlaying, Is.True);
            Assert.That(definition.duration, Is.EqualTo(5f));
        }
        finally { otherExecution.Cancel(); otherExecution.Dispose(); otherContext.Dispose(); otherController.Presentation.Dispose(); Object.DestroyImmediate(other); }
    }

    [Test]
    public void ExternalBeamPresentation_UsesAuthorityEndpointsInsteadOfCastingAgain()
    {
        definition.damagePerTick = 0f;
        execution.Begin();
        var visual = controller.Presentation.Beam;
        Vector3 end = targetPosition + Vector3.forward * 20f;
        visual.PlayBetween(context.OriginPosition, end, false);
        visual.RefreshBeam();
        Assert.That(visual.EndPoint, Is.EqualTo(end));
        Assert.That(visual.LastHit.collider, Is.Null);
        Assert.That(visual.HasHit, Is.False);
    }

    [Test]
    public void Tracking_FollowsVisibleTargetAndKeepsLastVisiblePointWhenOccluded()
    {
        definition.damagePerTick = 0f;
        definition.aimMode = BeholderBeamAimMode.FollowVisibleTarget;
        definition.trackingSpeed = 0f;
        execution.Begin();
        target.transform.position += Vector3.right;
        Physics.SyncTransforms();
        execution.Tick(0.1f);
        Assert.That(Vector3.Distance(context.AimPoint, target.transform.position), Is.LessThan(0.001f));
        Vector3 marked = context.AimPoint;
        target.transform.position = root.transform.position + Vector3.back * 10f;
        Physics.SyncTransforms();
        execution.Tick(0.1f);
        Assert.That(context.AimPoint, Is.EqualTo(marked));
    }

    [Test]
    public void Tracking_TurnsBoneWithTheBeamAtLimitedSpeedAndAllowsADodge()
    {
        var bone = new GameObject("Beam bone").transform;
        bone.SetParent(root.transform, false);
        bone.localRotation = Quaternion.Euler(12f, 30f, -20f);
        var eye = new GameObject("Eye").transform;
        eye.SetParent(bone, false);
        var end = new GameObject("Eye_end").transform;
        end.SetParent(eye, false);
        end.localPosition = Vector3.up * 100f; // Imported eye axis differs from Unity +Z.
        Quaternion animated = bone.localRotation;
        definition.aimBoneName = bone.name;
        definition.damagePerTick = 0f;
        definition.aimMode = BeholderBeamAimMode.FollowVisibleTarget;
        definition.trackingSpeed = 20f;
        execution.Begin();
        Vector3 before = (context.LastSegment.End - context.LastSegment.Start).normalized;
        target.transform.position += Vector3.right * 4f;
        Physics.SyncTransforms();
        execution.Tick(0.1f);
        Vector3 direction = (context.LastSegment.End - context.LastSegment.Start).normalized;
        Assert.That(Vector3.Angle(before, direction), Is.EqualTo(2f).Within(0.01f));
        Assert.That(Vector3.Angle(end.position - eye.position, direction), Is.LessThan(0.03f));
        Assert.That(context.LastSegment.HasHit, Is.False, "Player can move outside the slowly correcting beam");
        execution.Tick(1.2f);
        Assert.That(context.LastSegment.Hit.collider, Is.EqualTo(target.GetComponent<Collider>()));
        execution.Cancel();
        Assert.That(Quaternion.Angle(bone.localRotation, animated), Is.LessThan(0.01f));
    }

    [Test]
    public void RemotePresentation_ReappliesBoneDirectionBetweenSegmentsAndReleasesOnStop()
    {
        var bone = new GameObject("Remote beam bone").transform;
        bone.SetParent(root.transform, false);
        var end = new GameObject("Eye_end").transform;
        end.SetParent(bone, false);
        end.localPosition = Vector3.up * 100f;
        definition.aimBoneName = bone.name;
        Vector3 direction = new Vector3(1f, 0.2f, 1f).normalized;
        controller.Presentation.ShowBeam(definition, context.OriginPosition, context.OriginPosition + direction * 10f, false);
        bone.localRotation = Quaternion.identity; // Animator evaluates between network updates.
        controller.Presentation.Tick(Time.time);
        Assert.That(Vector3.Angle(end.position - bone.position, direction), Is.LessThan(0.03f));
        controller.Presentation.StopBeam();
        Assert.That(Quaternion.Angle(bone.localRotation, Quaternion.identity), Is.LessThan(0.01f));
    }

    [TestCase("cancel")]
    [TestCase("disable")]
    [TestCase("death")]
    [TestCase("state")]
    public void Controller_InterruptionsStopSustainedVfxAndRestoreLayerSpeed(string interruption)
    {
        var stage = new GameObject("Interruption fixture");
        stage.SetActive(false);
        var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beholder/Beholder.prefab"), stage.transform);
        var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BeholderLightningBeamAttack>("Assets/WorstKnights/Models/Enemies/Beholder/Attacks/Attack_Eye_03.R.asset"));
        try
        {
            settings.attackAudio = null; settings.dialogues = Array.Empty<AudioCue>();
            enemy.transform.position = root.transform.position;
            enemy.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
            var attack = enemy.GetComponent<BeholderAttackController>();
            typeof(BeholderAttackController).GetField("attacks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(attack, new[] { new BeholderAttackController.AttackDefinition { settings = settings } });
            stage.SetActive(true);
            var animator = enemy.GetComponent<EnemySetup>().Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0f);
            var vision = enemy.GetComponent<EnemyVision>();
            target.transform.position = vision.EyePosition + vision.EyeForward * 6f;
            Physics.SyncTransforms();
            var brain = enemy.GetComponent<EnemyBrain>();
            brain.ChangeState(EnemyState.Attack);
            Assert.That(attack.TryStartAttack(player), Is.True);
            animator.Update(0f); animator.Update(2.8f); attack.TickAttack(0f);
            Assert.That(attack.Presentation.Beam.IsPlaying, Is.True);
            if (interruption == "cancel") attack.CancelAttack();
            else if (interruption == "disable")
            {
                attack.enabled = false;
                // Plain EditMode does not run MonoBehaviour lifecycle automatically.
                typeof(BeholderAttackController).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(attack, null);
            }
            else if (interruption == "death") { enemy.GetComponent<EnemyHealth>().ApplyReplicatedState(0f, false); attack.TickAttack(0f); }
            else brain.ChangeState(EnemyState.Chase);
            Assert.That(attack.IsAttacking, Is.False);
            Assert.That(attack.Presentation.Beam.IsPlaying, Is.False);
            Assert.That(animator.GetFloat(settings.SpeedParameter), Is.EqualTo(1f));
        }
        finally
        {
            var actor = enemy.GetComponent<BeholderAttackController>();
            actor.ActiveContext?.Dispose(); actor.Presentation.Dispose();
            Object.DestroyImmediate(stage); Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void NetworkSegments_RejectUnknownSenderEvenForAnActiveAttack()
    {
        ForceActivePresentation();
        typeof(BeholderAttackController).GetMethod("RpcSegment", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(controller, new object[] { 0, context.OriginPosition, targetPosition, true, default(Photon.Pun.PhotonMessageInfo) });
        Assert.That(controller.Presentation.Beam, Is.Null);
    }

    [Test]
    public void AuthorityWithoutExecution_CancelsInsteadOfResumingRemoteDamage()
    {
        ForceActivePresentation();
        controller.TickAttack(0.1f);
        Assert.That(controller.IsAttacking, Is.False);
        Assert.That(controller.Phase, Is.EqualTo(BeholderAttackPhase.None));
    }

    private void ForceActivePresentation()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        ((BeholderAttackScheduler)typeof(BeholderAttackController).GetField("scheduler", flags).GetValue(controller)).ApplyStart(0, 5f, 8f);
        typeof(BeholderAttackController).GetField("playingAttack", flags).SetValue(controller, definition);
        typeof(BeholderAttackController).GetProperty("Phase").SetValue(controller, BeholderAttackPhase.Execution);
    }
}
