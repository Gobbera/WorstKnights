using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

public sealed class LightningBeamTests
{
    private const string Folder = "Assets/VFX/Lightning Strike/Variations/";
    private GameObject root;
    private GameObject source;
    private GameObject target;
    private GameObject obstacles;
    private LightningBeamController beam;
    private VisualEffect effect;
    private readonly Vector3 origin = new Vector3(30000f, 0f, 30000f);

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Beam test");
        root.SetActive(false);
        effect = root.AddComponent<VisualEffect>();
        effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Folder + "VFX_Lightning Beam.vfx");
        effect.initialEventName = "OnStop";
        beam = root.AddComponent<LightningBeamController>();
        source = new GameObject("Beam source");
        source.transform.position = origin;
        target = new GameObject("Beam aim");
        target.transform.position = origin + Vector3.forward * 12f;
        obstacles = new GameObject("Beam obstacles");
        root.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(source);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(obstacles);
    }

    [Test]
    public void Presets_HaveDistinctColorsAndThicknessAndKeepStrikeSeparate()
    {
        var blue = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Lightning Beam Blue.prefab").GetComponent<LightningBeamController>());
        var purple = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Lightning Beam Purple.prefab").GetComponent<LightningBeamController>());
        Assert.That(blue.FindProperty("width").floatValue, Is.LessThan(purple.FindProperty("width").floatValue));
        Assert.That(blue.FindProperty("beamColor").colorValue, Is.Not.EqualTo(purple.FindProperty("beamColor").colorValue));
        Assert.That(blue.FindProperty("duration").floatValue, Is.Zero);
        var strike = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFX/Lightning Strike/Lightning Strike.prefab");
        Assert.That(strike.GetComponent<LightningStrikeController>(), Is.Not.Null);
        Assert.That(strike.GetComponent<LightningBeamController>(), Is.Null);
    }

    [Test]
    public void MovingEndpointsAndWall_RecomputeTheSegmentAndImpact()
    {
        Collider wall = AddBox(origin + Vector3.forward * 4f);
        Physics.SyncTransforms();
        Assert.That(beam.StartBeam(source.transform, target.transform), Is.True);
        Assert.That(beam.LastHit.collider, Is.EqualTo(wall));
        Assert.That(beam.EndPoint.z - origin.z, Is.EqualTo(3.5f).Within(0.001f));
        Assert.That(effect.GetFloat("Impact Glow Size"), Is.GreaterThan(0f));
        wall.transform.position += Vector3.right * 20f;
        source.transform.position += Vector3.right;
        target.transform.position += Vector3.up * 2f;
        Physics.SyncTransforms();
        beam.RefreshBeam();
        Assert.That(beam.StartPoint, Is.EqualTo(source.transform.position));
        Assert.That(Vector3.Distance(beam.EndPoint, target.transform.position), Is.LessThan(0.001f));
        Assert.That(beam.HasHit, Is.False);
        Assert.That(effect.GetFloat("Impact Glow Size"), Is.Zero);
    }

    [Test]
    public void OwnerFiltering_GrowsTheRaycastBufferWithoutLosingTheNearestWall()
    {
        for (int i = 0; i < 24; i++) AddBox(origin + Vector3.forward * (1f + i * 0.2f));
        var foreign = new GameObject("Foreign wall");
        foreign.transform.position = origin + Vector3.forward * 8f;
        Collider wall = foreign.AddComponent<BoxCollider>();
        try
        {
            Physics.SyncTransforms();
            beam.StartBeam(source.transform, target.transform, obstacles.transform);
            Assert.That(beam.LastHit.collider, Is.EqualTo(wall));
        }
        finally { Object.DestroyImmediate(foreign); }
    }

    [Test]
    public void AppearanceChanges_DoNotStretchTheLengthOrRestartPlayback()
    {
        beam.StartBeam(source.transform, target.transform);
        double began = (double)Field("startedAt");
        var settings = new SerializedObject(beam);
        settings.FindProperty("width").floatValue = 4f;
        settings.FindProperty("beamColor").colorValue = Color.magenta;
        settings.FindProperty("intensity").floatValue = 2f;
        settings.FindProperty("noiseScale").floatValue = 40f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        beam.RefreshBeam();
        Assert.That(effect.GetFloat("Beam Width"), Is.EqualTo(4f));
        Assert.That(effect.GetVector4("Lightning Color"), Is.EqualTo((Vector4)(Color.magenta * 2f)));
        Assert.That(effect.GetFloat("Noise Scale"), Is.EqualTo(40f));
        Assert.That(effect.GetFloat("Lightning Length"), Is.EqualTo(12f).Within(0.001f));
        Assert.That((double)Field("startedAt"), Is.EqualTo(began));
    }

    [TestCase(0f, true)]
    [TestCase(1f, false)]
    public void Duration_ZeroPersistsAndPositiveDurationStops(float duration, bool remainsActive)
    {
        var settings = new SerializedObject(beam);
        settings.FindProperty("duration").floatValue = duration;
        settings.ApplyModifiedPropertiesWithoutUndo();
        beam.StartBeam(source.transform, target.transform);
        typeof(LightningBeamController).GetField("startedAt", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(beam, Time.realtimeSinceStartupAsDouble - 2f);
        beam.RefreshBeam();
        Assert.That(beam.IsPlaying, Is.EqualTo(remainsActive));
        beam.StopBeam();
        Assert.That(beam.IsPlaying, Is.False);
        Assert.That(effect.initialEventName, Is.EqualTo("OnStop"));
    }

    [Test]
    public void DisabledComponent_StopsAndCanBeStartedAgain()
    {
        beam.StartBeam(source.transform, target.transform);
        beam.enabled = false;
        Assert.That(beam.IsPlaying, Is.False);
        beam.enabled = true;
        Assert.That(beam.StartBeam(source.transform, target.transform), Is.True);
    }

    [Test]
    public void CoincidentPoints_HideUntilTheTargetMovesAway()
    {
        target.transform.position = source.transform.position;
        beam.StartBeam(source.transform, target.transform);
        Assert.That(beam.IsPlaying, Is.True);
        Assert.That((bool)Field("visualActive"), Is.False);
        target.transform.position += Vector3.up * 8f;
        beam.RefreshBeam();
        Assert.That(Vector3.Dot(beam.transform.forward, Vector3.up), Is.GreaterThan(0.999f));
        Assert.That((bool)Field("visualActive"), Is.True);
    }

    [Test]
    public void ForwardWithoutTarget_UsesMaximumRange()
    {
        beam.StartBeam(source.transform, null);
        Assert.That(Vector3.Distance(beam.StartPoint, beam.EndPoint), Is.EqualTo(50f).Within(0.001f));
        Assert.That(beam.HasHit, Is.False);
    }

    [Test]
    public void TriggersAndIgnoredLayers_DoNotBlockTheBeam()
    {
        AddBox(origin + Vector3.forward * 2f).isTrigger = true;
        AddBox(origin + Vector3.forward * 3f).gameObject.layer = 2;
        Collider wall = AddBox(origin + Vector3.forward * 6f);
        Physics.SyncTransforms();
        beam.StartBeam(source.transform, target.transform);
        Assert.That(beam.LastHit.collider, Is.EqualTo(wall));
    }

    private Collider AddBox(Vector3 position)
    {
        var box = new GameObject("Obstacle");
        box.transform.SetParent(obstacles.transform, false);
        box.transform.position = position;
        return box.AddComponent<BoxCollider>();
    }

    private object Field(string name)
    {
        return typeof(LightningBeamController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(beam);
    }
}
