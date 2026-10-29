using System.Linq;
using System.Reflection;
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
        Assert.That(definition.FindPropertyRelative("aimLockLeadTime").floatValue, Is.EqualTo(0.5f));
        Assert.That(definition.FindPropertyRelative("tentacleRoot").objectReferenceValue.name, Is.EqualTo("Root.Tentacule_03.L"));
        var dialogue = new SerializedObject(prefab.GetComponent<BeholderDialogueController>());
        var banks = dialogue.FindProperty("linesByState");
        for (int i = 0; i < banks.arraySize; i++)
            Assert.That(banks.GetArrayElementAtIndex(i).FindPropertyRelative("state").intValue, Is.Not.EqualTo((int)EnemyState.Attack));
    }

    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    public void Aim_TracksUntilConfiguredLeadThenKeepsWorldPosition(float lead)
    {
        var enemy = new GameObject("Aim test");
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            enemy.transform.position = new Vector3(40000f, 0f, 40000f);
            var vision = enemy.AddComponent<EnemyVision>();
            var controller = enemy.AddComponent<BeholderAttackController>();
            var player = target.AddComponent<PlayerHealth>();
            SetField(controller, "enemyVision", vision);
            SetField(controller, "attackTarget", player);
            SetField(controller, "playingAttack", new BeholderAttackController.AttackDefinition { aimLockLeadTime = lead });
            target.transform.position = vision.EyePosition + Vector3.forward * 6f;
            Physics.SyncTransforms();
            UpdateAim(controller, lead + 0.1f);
            target.transform.position += Vector3.right;
            Physics.SyncTransforms();
            UpdateAim(controller, lead);
            Assert.That((Vector3)GetField(controller, "aimPosition"), Is.EqualTo(target.transform.position));
            Assert.That((bool)GetField(controller, "aimLocked"), Is.True);
            Vector3 marked = target.transform.position;
            target.transform.position += Vector3.right * 2f;
            Physics.SyncTransforms();
            UpdateAim(controller, 0f);
            Assert.That((Vector3)GetField(controller, "aimPosition"), Is.EqualTo(marked));
        }
        finally
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void Aim_LostVisibilityBeforeLockKeepsLastVisiblePosition()
    {
        var enemy = new GameObject("Aim visibility test");
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            enemy.transform.position = new Vector3(40000f, 0f, 40000f);
            var vision = enemy.AddComponent<EnemyVision>();
            var controller = enemy.AddComponent<BeholderAttackController>();
            SetField(controller, "enemyVision", vision);
            SetField(controller, "attackTarget", target.AddComponent<PlayerHealth>());
            SetField(controller, "playingAttack", new BeholderAttackController.AttackDefinition { aimLockLeadTime = 0.5f });
            target.transform.position = vision.EyePosition + Vector3.forward * 6f;
            Physics.SyncTransforms();
            UpdateAim(controller, 1f);
            Vector3 lastSeen = target.transform.position;
            target.transform.position = vision.EyePosition + Vector3.back * 6f;
            Physics.SyncTransforms();
            UpdateAim(controller, 0.5f);
            Assert.That((Vector3)GetField(controller, "aimPosition"), Is.EqualTo(lastSeen));
            target.transform.position = lastSeen + Vector3.right;
            Physics.SyncTransforms();
            UpdateAim(controller, 0f);
            Assert.That((Vector3)GetField(controller, "aimPosition"), Is.EqualTo(lastSeen));
        }
        finally
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(enemy);
        }
    }

    private static void UpdateAim(BeholderAttackController controller, float remaining)
    {
        typeof(BeholderAttackController).GetMethod("UpdateAim", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, new object[] { remaining });
    }

    [TestCase(0f, false)]
    [TestCase(0.5f, true)]
    public void AnimatedPrefab_MovingTargetCanDodgeAfterAimLock(float lead, bool shouldMiss)
    {
        var staging = new GameObject("Aim prefab staging");
        staging.SetActive(false);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beholder/Beholder.prefab");
        var enemy = Object.Instantiate(prefab, staging.transform);
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        BeholderLightningVfxTest emitter = enemy.GetComponent<BeholderLightningVfxTest>();
        try
        {
            enemy.SetActive(false);
            enemy.transform.SetParent(null);
            enemy.transform.position = new Vector3(40000f, 0f, 40000f);
            var agent = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            enemy.SetActive(true);
            var animator = enemy.GetComponent<EnemySetup>().Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents = false;
            animator.Rebind();
            animator.Update(0f);
            var vision = enemy.GetComponent<EnemyVision>();
            var controller = enemy.GetComponent<BeholderAttackController>();
            var definition = ((BeholderAttackController.AttackDefinition[])GetField(controller, "attacks"))[0];
            definition.aimLockLeadTime = lead;
            target.transform.position = vision.EyePosition + vision.EyeForward * 6f;
            var player = target.AddComponent<PlayerHealth>();
            Physics.SyncTransforms();
            enemy.GetComponent<EnemyBrain>().ChangeState(EnemyState.Attack);
            Assert.That(controller.TryStartAttack(player), Is.True);
            Assert.That((float)GetField(controller, "effectTime"), Is.EqualTo(2.75f));
            animator.Update(0f);
            animator.Update(2.2f);
            LateTick(controller);
            Assert.That((bool)GetField(controller, "aimLocked"), Is.False);
            target.transform.position += Vector3.right * 0.5f;
            Physics.SyncTransforms();
            animator.Update(0.06f);
            LateTick(controller);
            AnimatorStateInfo sampled = animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex(definition.LayerName));
            Assert.That((bool)GetField(controller, "aimLocked"), Is.EqualTo(shouldMiss),
                $"clipTime={sampled.normalizedTime * definition.animation.length}; speed={sampled.speed}; multiplier={sampled.speedMultiplier}");
            Vector3 marked = (Vector3)GetField(controller, "aimPosition");
            animator.Update(0.5f);
            Vector3 dodge = Vector3.Cross(marked - emitter.EyePosition, Vector3.up).normalized;
            if (dodge.sqrMagnitude < 0.001f) dodge = Vector3.right;
            target.transform.position += dodge * 3f;
            Physics.SyncTransforms();
            controller.QueueAnimationEffect(definition.mask.name);
            LateTick(controller);
            Assert.That(emitter.ShotSequence, Is.EqualTo(1));
            Assert.That(emitter.ActiveEffect.LastHit.collider == target.GetComponent<Collider>(), Is.EqualTo(!shouldMiss));
            if (shouldMiss)
            {
                Assert.That((Vector3)GetField(controller, "aimPosition"), Is.EqualTo(marked));
                Vector3 actual = (emitter.ActiveEffect.EndPoint - emitter.ActiveEffect.StartPoint).normalized;
                Vector3 expected = (marked - emitter.ActiveEffect.StartPoint).normalized;
                Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.001f));
            }
        }
        finally
        {
            typeof(BeholderLightningVfxTest).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(emitter, null);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(staging);
        }
    }

    private static void LateTick(BeholderAttackController controller)
    {
        typeof(BeholderAttackController).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
    }

    private static object GetField(object instance, string name)
    {
        return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }

    private static void SetField(object instance, string name, object value)
    {
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }
}
