using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class PlayerMeleeArcAimTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<GameObject> objects = new List<GameObject>();
    readonly Vector3 origin = new Vector3(3200, 3200, 3200);
    PlayerMeleeAttack melee;
    Transform camera;
    PlayerMeleeAttack.AttackConfig config;
    int floorReactions;

    [SetUp]
    public void SetUp()
    {
        floorReactions = 0;
        var root = Create("Arc test player", origin);
        root.SetActive(false);
        camera = new GameObject("FP_Camera").transform;
        camera.SetParent(root.transform, false);
        camera.localPosition = new Vector3(0, 0.758f, 0.45f);
        camera.gameObject.AddComponent<Camera>().enabled = false;
        melee = root.AddComponent<PlayerMeleeAttack>();
        Set(melee, "attackOrigin", camera); Set(melee, "attackDirection", camera);
        Set(melee, "useVerticalCameraAim", true);
        config = new PlayerMeleeAttack.AttackConfig();
        Set(config, "hitShape", PlayerMeleeAttackShape.Arc);
        Set(config, "arcRange", 1.8f); Set(config, "arcAngle", 80f); Set(config, "arcHalfHeight", 0.12f);
    }
    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }
    [Test]
    public void LookingDownDamagesTheLowerAimedTargetInsteadOfTheHorizontalTarget()
    {
        Aim(55);
        var lower = Target(camera.position + camera.forward * 1.3f);
        var horizontal = Target(camera.position + Vector3.forward * 1.3f);
        Hit();
        Assert.That(lower.Hits, Is.EqualTo(1)); Assert.That(horizontal.Hits, Is.Zero);
        Assert.That(lower.LastDamage.HitDirection.y, Is.LessThan(-0.8f));
    }
    [Test]
    public void LookingUpDamagesTheUpperAimedTargetInsteadOfTheHorizontalTarget()
    {
        Aim(-55);
        var upper = Target(camera.position + camera.forward * 1.3f);
        var horizontal = Target(camera.position + Vector3.forward * 1.3f);
        Hit();
        Assert.That(upper.Hits, Is.EqualTo(1)); Assert.That(horizontal.Hits, Is.Zero);
        Assert.That(upper.LastDamage.HitDirection.y, Is.GreaterThan(0.8f));
    }
    [Test]
    public void TiltedArcPreservesItsLateralOpeningAtDifferentPitchAndYawAngles()
    {
        foreach (float pitch in new[] { -65f, 0f, 65f })
        {
            Aim(pitch, 37f);
            Quaternion rotation = Frame();
            var inside = Target(camera.position + rotation * (Quaternion.Euler(0, 35, 0) * Vector3.forward) * 1.2f);
            var outside = Target(camera.position + rotation * (Quaternion.Euler(0, 48, 0) * Vector3.forward) * 1.2f);
            Hit();
            Assert.That(inside.Hits, Is.EqualTo(1), "Inside opening at pitch " + pitch);
            Assert.That(outside.Hits, Is.Zero, "Outside opening at pitch " + pitch);
            inside.gameObject.SetActive(false); outside.gameObject.SetActive(false);
        }
    }
    [Test]
    public void HalfHeightIsMeasuredRelativeToTheAimedPlane()
    {
        Aim(65, 22);
        Quaternion rotation = Frame();
        var inside = Target(camera.position + rotation * new Vector3(0, 0.06f, 1.2f));
        var outside = Target(camera.position + rotation * new Vector3(0, 0.35f, 1.2f));
        Hit();
        Assert.That(inside.Hits, Is.EqualTo(1)); Assert.That(outside.Hits, Is.Zero);
    }
    [Test]
    public void ZeroHalfHeightDisablesOnlyTheAdditionalPlaneThicknessLimit()
    {
        Aim(-45);
        Set(config, "arcHalfHeight", 0f);
        var target = Target(camera.position + Frame() * new Vector3(0, 0.5f, 1.1f));
        Hit(); Assert.That(target.Hits, Is.EqualTo(1));
    }
    [Test]
    public void DisablingVerticalAimKeepsTheArcHorizontal()
    {
        Aim(60);
        Set(melee, "useVerticalCameraAim", false);
        var horizontal = Target(camera.position + Vector3.forward * 1.2f);
        var lower = Target(camera.position + camera.forward * 1.2f);
        Hit();
        Assert.That(horizontal.Hits, Is.EqualTo(1)); Assert.That(lower.Hits, Is.Zero);
        Assert.That(Mathf.Abs(horizontal.LastDamage.HitDirection.y), Is.LessThan(0.001f));
    }
    [Test]
    public void NearAndExactlyVerticalDirectionsRemainFiniteAndCanHit()
    {
        foreach (float pitch in new[] { -90f, -89f, 89f, 90f })
        {
            Aim(pitch, 123);
            var target = Target(camera.position + camera.forward * 1.2f);
            Hit();
            Assert.That(target.Hits, Is.EqualTo(1), "Vertical pitch " + pitch);
            Assert.That(float.IsNaN(target.LastDamage.HitPoint.x) || float.IsNaN(target.LastDamage.HitDirection.y), Is.False);
            target.gameObject.SetActive(false);
        }
    }
    [Test]
    public void TargetsBehindTheAimedArcAndBeyondRangeAreRejected()
    {
        Aim(-45);
        var behind = Target(camera.position - camera.forward * 1.2f);
        var beyond = Target(camera.position + camera.forward * 2.3f);
        Hit();
        Assert.That(behind.Hits, Is.Zero); Assert.That(beyond.Hits, Is.Zero);
    }
    [Test]
    public void ImpactPointUsesTheAimedColliderAndDirection()
    {
        Aim(70, 50);
        var target = Target(camera.position + camera.forward * 1.2f);
        Hit();
        Assert.That(target.Hits, Is.EqualTo(1));
        Assert.That(Vector3.Distance(target.GetComponent<Collider>().ClosestPoint(target.LastDamage.HitPoint), target.LastDamage.HitPoint), Is.LessThan(0.001f));
        Assert.That(Vector3.Dot(target.LastDamage.HitDirection, camera.forward), Is.GreaterThan(0.999f));
    }
    [Test]
    public void MultipleCollidersReceiveOneHitPerAttackWindow()
    {
        Aim(50);
        var target = Target(camera.position + camera.forward * 1.2f);
        target.gameObject.AddComponent<SphereCollider>().radius = 0.05f;
        Hit();
        Assert.That(target.Hits, Is.EqualTo(1));
    }
    [Test]
    public void BallAndBoxContinueFollowingVerticalAim()
    {
        Aim(-60);
        foreach (var shape in new[] { PlayerMeleeAttackShape.Ball, PlayerMeleeAttackShape.Box })
        {
            Set(config, "hitShape", shape); Set(config, "hitRadius", 0.15f); Set(config, "targetDistance", 1.2f);
            Set(config, "boxCenterDistance", 1.2f); Set(config, "boxSize", Vector3.one * 0.3f);
            var upper = Target(camera.position + camera.forward * 1.2f);
            var horizontal = Target(camera.position + Vector3.forward * 1.2f);
            Hit();
            Assert.That(upper.Hits, Is.EqualTo(1), shape.ToString()); Assert.That(horizontal.Hits, Is.Zero);
            upper.gameObject.SetActive(false); horizontal.gameObject.SetActive(false);
        }
    }
    [Test]
    public void UnlimitedArcHeightReproducesHorizontalFloorImpactReactions()
    {
        Aim(0); Set(config, "arcHalfHeight", 0f);
        CreateReactionFloor(); Hit();
        Assert.That(floorReactions, Is.EqualTo(1), "An unlimited arc can accept the nearby floor from its broad-phase sphere.");
    }
    [Test]
    public void AuthoredHorizontalComboHitsForwardTargetsWithoutTriggeringFloorReactions()
    {
        Aim(0); CreateReactionFloor();
        var forward = Target(camera.position + Vector3.forward * 1.2f);
        foreach (var authored in AuthoredConfigs()) { config = authored; Hit(); }
        Assert.That(forward.Hits, Is.EqualTo(3));
        Assert.That(floorReactions, Is.Zero, "Facing forward must not emit the floor Hit reaction.");
    }
    [Test]
    public void AuthoredDownwardComboStillTriggersTheRealFloorImpactReaction()
    {
        Aim(89); CreateReactionFloor();
        foreach (var authored in AuthoredConfigs()) { config = authored; Hit(); }
        Assert.That(floorReactions, Is.EqualTo(3), "Each downward combo step should produce one actual floor reaction.");
    }
    [Test]
    public void AuthoredUpwardComboDoesNotTriggerFloorImpactReactions()
    {
        Aim(-75); CreateReactionFloor();
        foreach (var authored in AuthoredConfigs()) { config = authored; Hit(); }
        Assert.That(floorReactions, Is.Zero);
    }
    [Test]
    public void EveryAuthoredPrefabComboStepCanReachStandingGroundWhenAimedDown()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab");
        var source = prefab.GetComponent<PlayerMeleeAttack>();
        var configs = (List<PlayerMeleeAttack.AttackConfig>)typeof(PlayerMeleeAttack).GetField("attackConfigs", Private).GetValue(source);
        Aim(89); Set(melee, "useVerticalCameraAim", true);
        var ground = Target(new Vector3(camera.position.x, origin.y - 1.05f, camera.position.z));
        ground.GetComponent<BoxCollider>().size = new Vector3(0.4f, 0.1f, 0.4f);
        foreach (var authored in configs)
        {
            config = authored; Hit();
            Assert.That(ground.Hits, Is.EqualTo(authored.ComboStep), "Ground must be reachable by combo " + authored.ComboStep);
        }
    }
    private void Aim(float pitch, float yaw = 0) => camera.rotation = Quaternion.Euler(pitch, yaw, 0);
    private static List<PlayerMeleeAttack.AttackConfig> AuthoredConfigs()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab");
        return (List<PlayerMeleeAttack.AttackConfig>)typeof(PlayerMeleeAttack).GetField("attackConfigs", Private).GetValue(prefab.GetComponent<PlayerMeleeAttack>());
    }
    private void CreateReactionFloor()
    {
        var floor = Create("Reaction floor", new Vector3(camera.position.x, origin.y - 1.05f, camera.position.z));
        floor.AddComponent<BoxCollider>().size = new Vector3(20, 0.1f, 20);
        var receiver = floor.AddComponent<ReactionSignalReceiver>();
        var entry = new ReactionSignalReceiver.ReactionSignalEntry { signalId = "Hit" };
        entry.onSignalReceived.AddListener(() => floorReactions++);
        Set(receiver, "signalEntries", new List<ReactionSignalReceiver.ReactionSignalEntry> { entry });
        var bridge = floor.AddComponent<ImpactReactionSignalBridge>();
        typeof(ImpactReactionSignalRelayBase).GetField("signalReceiver", Private).SetValue(bridge, receiver);
        typeof(ImpactReactionSignalRelayBase).GetField("broadcastInMultiplayer", Private).SetValue(bridge, false);
    }
    private Quaternion Frame() => (Quaternion)typeof(PlayerMeleeAttack).GetMethod("ResolveAttackRotation", Private).Invoke(melee, new object[] { camera.forward });
    private void Hit()
    {
        Physics.SyncTransforms();
        typeof(PlayerMeleeAttack).GetMethod("ScheduleHitboxWindow", Private).Invoke(melee, new object[] { config });
        typeof(PlayerMeleeAttack).GetMethod("TryHitTargets", Private).Invoke(melee, new object[] { config });
    }
    private ArcAimTestReceiver Target(Vector3 position)
    {
        var root = Create("Arc target", position);
        root.AddComponent<BoxCollider>().size = Vector3.one * 0.04f;
        return root.AddComponent<ArcAimTestReceiver>();
    }
    private GameObject Create(string name, Vector3 position)
    {
        var root = new GameObject(name); root.transform.position = position; objects.Add(root); return root;
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
}

public sealed class ArcAimTestReceiver : MonoBehaviour, IDamageable
{
    public bool IsAlive => true;
    public CombatAlignment Alignment => CombatAlignment.Neutral;
    public int Hits;
    public DamageInfo LastDamage;
    public void ApplyDamage(DamageInfo damageInfo) { Hits++; LastDamage = damageInfo; }
}
