using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

public sealed class LightningStrikeTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private readonly Vector3 testOrigin = new Vector3(20000f, 20000f, 20000f);
    private LightningStrikeController strike;
    private VisualEffect effect;

    [SetUp]
    public void SetUp()
    {
        GameObject root = CreateObject("Lightning test");
        root.SetActive(false);
        effect = root.AddComponent<VisualEffect>();
        effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(
            "Assets/VFX/Lightning Strike/VFX_Lightning Strike.vfx");
        Assert.That(effect.visualEffectAsset, Is.Not.Null);
        effect.initialEventName = "OnStop";
        strike = root.AddComponent<LightningStrikeController>();
        SetField("playOnEnable", false);
        root.transform.position = testOrigin;
        root.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [TestCase(0.25f)]
    [TestCase(6f)]
    [TestCase(50f)]
    [TestCase(120f)]
    public void Segment_UpdatesLengthImpactAndBoundsWithoutScalingFlashes(float distance)
    {
        Vector3 end = testOrigin + Vector3.forward * distance;
        strike.PlayBetween(testOrigin, end);

        Assert.That(effect.HasFloat("Lightning Length"), Is.True, "VFX graph must compile and expose length.");
        Assert.That(effect.GetFloat("Lightning Length"), Is.EqualTo(distance).Within(0.001f));
        Assert.That(effect.GetFloat("Impact Offset"), Is.EqualTo(distance).Within(0.001f));
        Assert.That(strike.transform.localScale, Is.EqualTo(Vector3.one));
        Bounds bounds = new Bounds(effect.GetVector3("Beam Bounds Center"), effect.GetVector3("Beam Bounds Size"));
        Assert.That(bounds.Contains(Vector3.zero), Is.True);
        Assert.That(bounds.Contains(Vector3.forward * distance), Is.True);
    }

    [Test]
    public void Fire_StopsAtNearWallBeforeTarget()
    {
        Collider wall = CreateBox(4f);
        CreateBox(10f);
        Physics.SyncTransforms();

        Assert.That(strike.FireAt(testOrigin, testOrigin + Vector3.forward * 20f, out RaycastHit hit), Is.True);
        Assert.That(hit.collider, Is.EqualTo(wall));
        Assert.That(strike.EndPoint.z - testOrigin.z, Is.EqualTo(3.5f).Within(0.001f));
        Assert.That(effect.GetBool("Has Impact"), Is.True);
    }

    [Test]
    public void Fire_IgnoresOwnerAndUsesNextCollider()
    {
        GameObject owner = CreateObject("Shooter");
        CreateBox(2f).transform.SetParent(owner.transform, true);
        Collider target = CreateBox(8f);
        Physics.SyncTransforms();

        Assert.That(strike.FireFrom(testOrigin, Vector3.forward, out RaycastHit hit, owner.transform), Is.True);
        Assert.That(hit.collider, Is.EqualTo(target));
    }

    [Test]
    public void Fire_IgnoresTriggersByDefault()
    {
        CreateBox(2f).isTrigger = true;
        Collider target = CreateBox(8f);
        Physics.SyncTransforms();

        strike.FireFrom(testOrigin, Vector3.forward, out RaycastHit hit);
        Assert.That(hit.collider, Is.EqualTo(target));
    }

    [Test]
    public void Fire_RespectsLayerMask()
    {
        CreateBox(2f).gameObject.layer = 2; // Ignore Raycast
        Collider target = CreateBox(8f);
        Physics.SyncTransforms();

        strike.FireFrom(testOrigin, Vector3.forward, out RaycastHit hit);
        Assert.That(hit.collider, Is.EqualTo(target));
    }

    [Test]
    public void Miss_UsesMaximumRangeAndDisablesImpact()
    {
        CreateBox(60f);
        Physics.SyncTransforms();

        Assert.That(strike.FireFrom(testOrigin, Vector3.forward, out RaycastHit hit), Is.False);
        Assert.That(hit.collider, Is.Null);
        Assert.That(Vector3.Distance(strike.StartPoint, strike.EndPoint), Is.EqualTo(50f).Within(0.001f));
        Assert.That(effect.GetBool("Has Impact"), Is.False);
    }

    [Test]
    public void AimPoint_DoesNotHitObjectsBeyondTheRequestedPoint()
    {
        CreateBox(10f);
        Physics.SyncTransforms();

        Assert.That(strike.FireAt(testOrigin, testOrigin + Vector3.forward * 5f, out _), Is.False);
        Assert.That(strike.EndPoint.z - testOrigin.z, Is.EqualTo(5f).Within(0.001f));
    }

    [Test]
    public void Segment_HandlesVerticalShotsAndScaledInstances()
    {
        strike.transform.localScale = new Vector3(2f, 2f, 2f);
        Vector3 end = testOrigin + Vector3.up * 20f;
        strike.PlayBetween(testOrigin, end);

        Assert.That(Vector3.Dot(strike.transform.forward, Vector3.up), Is.GreaterThan(0.999f));
        Vector3 localEnd = Vector3.forward * effect.GetFloat("Lightning Length");
        Assert.That(Vector3.Distance(strike.transform.TransformPoint(localEnd), end), Is.LessThan(0.001f));
    }

    [Test]
    public void InvalidDirection_DoesNotWriteInvalidVfxValues()
    {
        strike.PlayBetween(testOrigin, testOrigin + Vector3.forward * 6f);
        Assert.That(strike.FireFrom(testOrigin, Vector3.zero, out _), Is.False);
        Assert.That(strike.FireFrom(testOrigin, new Vector3(float.NaN, 0f, 0f), out _), Is.False);
        Assert.That(effect.GetFloat("Lightning Length"), Is.EqualTo(6f));
    }

    private GameObject CreateObject(string name)
    {
        GameObject item = new GameObject(name);
        objects.Add(item);
        return item;
    }

    private Collider CreateBox(float distance)
    {
        GameObject box = CreateObject("Raycast obstacle");
        box.transform.position = testOrigin + Vector3.forward * distance;
        return box.AddComponent<BoxCollider>();
    }

    private void SetField(string name, bool value)
    {
        SerializedObject serialized = new SerializedObject(strike);
        serialized.FindProperty(name).boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
