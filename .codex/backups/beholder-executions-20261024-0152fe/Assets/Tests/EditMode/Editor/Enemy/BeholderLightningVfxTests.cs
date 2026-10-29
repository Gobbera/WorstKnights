using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class BeholderLightningVfxTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private readonly Vector3 origin = new Vector3(20000f, 0f, 20000f);
    private BeholderLightningVfxTest attack;
    private EnemyVision vision;
    private PlayerHealth player;
    private Transform eye;

    [SetUp]
    public void SetUp()
    {
        GameObject enemy = Create("Beholder test", origin);
        eye = Create("Eye_03.L.Mesh", origin + Vector3.up * 1.4f).transform;
        eye.SetParent(enemy.transform, true);
        vision = enemy.AddComponent<EnemyVision>();
        attack = enemy.AddComponent<BeholderLightningVfxTest>();
        Set(attack, "lightningPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/VFX/Lightning Strike/Lightning Strike.prefab").GetComponent<LightningStrikeController>());
        Set(attack, "eyeOrigin", eye);
        Set(vision, "eyeTransform", eye);
        GameObject target = Create("Visible player", origin + new Vector3(0f, 1.4f, 8f));
        target.AddComponent<BoxCollider>();
        player = target.AddComponent<PlayerHealth>();
        EnemyTargetRegistry.Register(player);
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        EnemyTargetRegistry.Unregister(player);
        if (attack != null)
            typeof(BeholderLightningVfxTest).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(attack, null);
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void BeholderPrefab_HasExactEyeAndTenSecondCooldown()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beholder/Beholder.prefab");
        var configured = prefab.GetComponent<BeholderLightningVfxTest>();
        Assert.That(configured, Is.Not.Null);
        var settings = new SerializedObject(configured);
        Assert.That(settings.FindProperty("eyeOrigin").objectReferenceValue.name, Is.EqualTo("Eye_03.L.Mesh"));
        Assert.That(settings.FindProperty("lightningPrefab").objectReferenceValue, Is.Not.Null);
        Assert.That(settings.FindProperty("cooldown").floatValue, Is.EqualTo(10f));
        Assert.That(settings.FindProperty("enableTest").boolValue, Is.True);
    }

    [Test]
    public void VisiblePlayer_FiresFromEyeAndIgnoresBeholderCollidersWithoutDamage()
    {
        GameObject ownCollider = Create("Beholder body", origin + new Vector3(0f, 1.4f, 2f));
        ownCollider.transform.SetParent(attack.transform, true);
        ownCollider.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        float health = player.CurrentHealth;

        Assert.That(Fire(0f), Is.True);
        Assert.That(attack.ShotSequence, Is.EqualTo(1));
        Assert.That(attack.ActiveEffect.StartPoint, Is.EqualTo(eye.position));
        Assert.That(attack.ActiveEffect.LastHit.collider, Is.EqualTo(player.GetComponent<Collider>()));
        Assert.That(player.CurrentHealth, Is.EqualTo(health));
    }

    [Test]
    public void VisiblePlayer_CannotFireAgainBeforeTenSeconds()
    {
        Assert.That(Fire(0f), Is.True);
        Assert.That(Fire(9.999f), Is.False);
        Assert.That(Fire(10f), Is.True);
        Assert.That(attack.ShotSequence, Is.EqualTo(2));
    }

    [Test]
    public void MeshWithOffsetPivot_EmitsFromEyeGeometry()
    {
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(1f, -0.1f, 0f), new Vector3(1.2f, 0.1f, 0f), new Vector3(1.4f, -0.1f, 0f) },
            triangles = new[] { 0, 1, 2 }
        };
        try
        {
            mesh.RecalculateBounds();
            eye.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = eye.gameObject.AddComponent<MeshRenderer>();
            Vector3 expected = renderer.bounds.center;
            Assert.That(Fire(0f), Is.True);
            Assert.That(attack.ActiveEffect.StartPoint, Is.EqualTo(expected));
            Assert.That(attack.ActiveEffect.StartPoint, Is.Not.EqualTo(eye.position));
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void SkinnedEyeWithImportedScale_EmitsAtRenderedEyeInsteadOfScalingTwice()
    {
        eye.localScale = Vector3.one * 100f;
        Transform bone = Create("Eye bone", eye.position).transform;
        bone.SetParent(eye, false);
        bone.localPosition = Vector3.zero;
        bone.localScale = Vector3.one;
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(0.01f, -0.001f, 0f), new Vector3(0.012f, 0.001f, 0f), new Vector3(0.014f, -0.001f, 0f) },
            triangles = new[] { 0, 1, 2 },
            bindposes = new[] { bone.worldToLocalMatrix * eye.localToWorldMatrix },
            boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f } }
        };
        try
        {
            mesh.RecalculateBounds();
            var skin = eye.gameObject.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.rootBone = bone;
            skin.bones = new[] { bone };
            skin.localBounds = mesh.bounds;
            Vector3 expected = eye.TransformPoint(mesh.bounds.center);
            Assert.That(Fire(0f), Is.True);
            Assert.That(Vector3.Distance(attack.ActiveEffect.StartPoint, expected), Is.LessThan(0.01f));
            Assert.That(attack.ActiveEffect.LastHit.collider, Is.EqualTo(player.GetComponent<Collider>()));
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void WallAfterVisibilityWasCached_PreventsShotAndDoesNotRestartCooldown()
    {
        Assert.That(Fire(0f), Is.True);
        GameObject wall = Create("Wall", origin + new Vector3(0f, 1.4f, 4f));
        wall.AddComponent<BoxCollider>();
        Physics.SyncTransforms();

        Assert.That(Fire(10f), Is.False);
        wall.SetActive(false);
        Physics.SyncTransforms();
        Assert.That(Fire(10.1f), Is.True);
    }

    [Test]
    public void PlayerBehindBeholder_DoesNotTriggerTheVfx()
    {
        player.transform.position = origin + new Vector3(0f, 1.4f, -8f);
        Physics.SyncTransforms();
        Assert.That(Fire(0f), Is.False);
        Assert.That(attack.ShotSequence, Is.Zero);
    }

    [Test]
    public void DisabledTest_DoesNotShoot()
    {
        Set(attack, "enableTest", false);
        Assert.That(Fire(0f), Is.False);
        Assert.That(attack.ActiveEffect, Is.Null);
    }

    [Test]
    public void ShotStaysInWorldSpaceWhenBeholderMoves()
    {
        Assert.That(Fire(0f), Is.True);
        var effect = attack.ActiveEffect;
        Vector3 start = effect.transform.position;
        attack.transform.position += Vector3.right * 3f;
        Assert.That(effect.transform.parent, Is.Null);
        Assert.That(effect.transform.position, Is.EqualTo(start));
    }

    private bool Fire(float now)
    {
        return (bool)typeof(BeholderLightningVfxTest).GetMethod("TryFireVisibleTarget",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(attack, new object[] { now });
    }

    private GameObject Create(string name, Vector3 position)
    {
        var item = new GameObject(name);
        item.transform.position = position;
        objects.Add(item);
        return item;
    }

    private static void Set(Object target, string name, Object value)
    {
        var settings = new SerializedObject(target);
        settings.FindProperty(name).objectReferenceValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(Object target, string name, bool value)
    {
        var settings = new SerializedObject(target);
        settings.FindProperty(name).boolValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }
}
