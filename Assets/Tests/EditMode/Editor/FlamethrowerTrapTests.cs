using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

public sealed class FlamethrowerTrapTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<GameObject> objects = new List<GameObject>();
    FlamethrowerTrapController trap;
    double started;
    Vector3 Origin => new Vector3(1000f, 1000f, 1000f);

    [SetUp]
    public void SetUp()
    {
        GameObject root = Create("Trap", Vector3.zero);
        root.SetActive(false);
        GameObject pivot = new GameObject("Flamethrower_Pivot");
        pivot.transform.SetParent(root.transform, false);
        GameObject visual = new GameObject("Flamethrower VFX");
        visual.transform.SetParent(pivot.transform, false);
        var effect = visual.AddComponent<VisualEffect>();
        effect.initialEventName = "OnStop";
        effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(FlamethrowerTrapAuthoringUtility.GraphPath);
        trap = root.AddComponent<FlamethrowerTrapController>();
        Set("flamethrowerPivot", pivot.transform);
        Set("flameEffect", effect);
        var lightObject = new GameObject(FlamethrowerTrapController.EndLightObjectName);
        lightObject.transform.SetParent(root.transform, false);
        var light = lightObject.AddComponent<Light>();
        light.enabled = false;
        Set("endLight", light);
        Set("localEulerAngles", Vector3.zero);
        Set("activationDelay", 0f);
        Set("firingDuration", 2f);
        Set("fillTime", 0.1f);
        Set("cooldown", 0.5f);
        Set("range", 8f);
        Set("startRadius", 0.1f);
        Set("endRadius", 0.6f);
        Set("damagePerSecond", 20f);
        Set("damageTickInterval", 0.25f);
        root.SetActive(true);
        Assert.That(trap.TryTriggerTrap(), Is.True, "Prepared VFX must support the controller.");
        started = (double)typeof(FlamethrowerTrapController).GetField("activationTime", Private).GetValue(trap);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void DamagesOnlyActorsInsideTheFlameAndWithinRange()
    {
        PlayerHealth inside = Player(new Vector3(0f, 0f, 3f));
        PlayerHealth side = Player(new Vector3(3f, 0f, 3f));
        PlayerHealth behind = Player(new Vector3(0f, 0f, -2f));
        PlayerHealth beyond = Player(new Vector3(0f, 0f, 10f));
        HitAt(0.5f);
        Assert.That(inside.CurrentHealth, Is.EqualTo(95f));
        Assert.That(side.CurrentHealth, Is.EqualTo(100f));
        Assert.That(behind.CurrentHealth, Is.EqualTo(100f));
        Assert.That(beyond.CurrentHealth, Is.EqualTo(100f));
    }

    [Test]
    public void WallShortensVisualJetAndPreventsDamageBehindIt()
    {
        PlayerHealth player = Player(Vector3.forward * 4f);
        var wall = Create("Wall", Vector3.forward * 2f).AddComponent<BoxCollider>();
        wall.size = new Vector3(4f, 4f, 0.5f);
        HitAt(0.5f);
        Assert.That(trap.ResolvedLength, Is.EqualTo(1.74f).Within(0.02f));
        Assert.That(player.CurrentHealth, Is.EqualTo(100f));
        Assert.That(trap.FlameEffect.GetFloat("Jet Range"), Is.EqualTo(trap.ResolvedLength).Within(0.001f));
    }

    [Test]
    public void OffAxisCoverBlocksDamageEvenIfCenterRayIsClear()
    {
        PlayerHealth player = Player(new Vector3(0.4f, 0f, 4f));
        player.GetComponent<BoxCollider>().size = Vector3.one * 0.2f;
        var wall = Create("Side Cover", new Vector3(0.2f, 0f, 2f)).AddComponent<BoxCollider>();
        wall.size = new Vector3(0.2f, 4f, 0.3f);
        HitAt(0.5f);
        Assert.That(trap.ResolvedLength, Is.EqualTo(8f).Within(0.001f));
        Assert.That(player.CurrentHealth, Is.EqualTo(100f));
    }

    [Test]
    public void MultipleCollidersReceiveOneTickAndIntervalIsRespected()
    {
        PlayerHealth player = Player(Vector3.forward * 3f);
        var extra = new GameObject("Extra Collider");
        extra.transform.SetParent(player.transform, false);
        extra.AddComponent<BoxCollider>();
        HitAt(0.5f);
        HitAt(0.51f);
        Assert.That(player.CurrentHealth, Is.EqualTo(95f));
        HitAt(0.76f);
        Assert.That(player.CurrentHealth, Is.EqualTo(90f));
    }

    [Test]
    public void PlayersAndEnemiesCanBothBeDamagedWithoutKnockback()
    {
        PlayerHealth player = Player(Vector3.forward * 2f);
        DamageInfo applied = default;
        player.Damaged += (health, damage) => applied = damage;
        var enemy = Create("Enemy", Vector3.forward * 4f).AddComponent<EnemyHealth>();
        enemy.gameObject.AddComponent<BoxCollider>();
        typeof(EnemyHealth).GetMethod("Awake", Private).Invoke(enemy, null);
        HitAt(0.5f);
        Assert.That(player.CurrentHealth, Is.EqualTo(95f));
        Assert.That(enemy.CurrentHealth, Is.EqualTo(95f));
        Assert.That(player.GetComponent<Rigidbody>().linearVelocity.sqrMagnitude, Is.LessThan(0.0001f));
        Assert.That(applied.PlayerDamageAnimation, Is.EqualTo(PlayerDamageAnimationType.None));
        Assert.That(applied.PlayerCameraImpact, Is.EqualTo(PlayerCameraImpactType.None));
    }

    [Test]
    public void DamageDoesNotReachTheEndBeforeTheFlameFront()
    {
        Set("fillTime", 1f);
        PlayerHealth player = Player(Vector3.forward * 6f);
        HitAt(0.2f);
        Assert.That(player.CurrentHealth, Is.EqualTo(100f));
        HitAt(1.1f);
        Assert.That(player.CurrentHealth, Is.EqualTo(95f));
    }

    [Test]
    public void SweepChangesBothVisualDirectionAndDamageDirection()
    {
        Set("angularAmplitude", new Vector3(0f, 90f, 0f));
        Set("movementPeriod", 4f);
        PlayerHealth right = Player(Vector3.right * 3f);
        PlayerHealth forward = Player(Vector3.forward * 3f);
        HitAt(1f);
        Assert.That(Vector3.Dot(trap.JetDirection, Vector3.right), Is.GreaterThan(0.999f));
        Assert.That(Vector3.Dot(trap.FlameEffect.transform.forward, trap.JetDirection), Is.GreaterThan(0.999f));
        Assert.That(right.CurrentHealth, Is.EqualTo(95f));
        Assert.That(forward.CurrentHealth, Is.EqualTo(100f));
    }

    [Test]
    public void WarningAndExpiredDurationDoNotCauseDamage()
    {
        Set("activationDelay", 1f);
        PlayerHealth player = Player(Vector3.forward * 3f);
        HitAt(0.5f);
        Assert.That(trap.Phase, Is.EqualTo(FlamethrowerTrapController.TrapPhase.Warning));
        Assert.That(player.CurrentHealth, Is.EqualTo(100f));
        HitAt(3.1f);
        Assert.That(trap.Phase, Is.EqualTo(FlamethrowerTrapController.TrapPhase.Cooldown));
        Assert.That(player.CurrentHealth, Is.EqualTo(100f));
        Assert.That(trap.FlameEffect.pause, Is.True);
    }

    [Test]
    public void RepeatedTriggersDoNotStackAndStopCancelsDamage()
    {
        Assert.That(trap.TryTriggerTrap(), Is.False);
        PlayerHealth player = Player(Vector3.forward * 3f);
        trap.StopTrap();
        HitAt(0.5f);
        Assert.That(player.CurrentHealth, Is.EqualTo(100f));
        Assert.That(trap.IsEmitting, Is.False);
    }

    [Test]
    public void TargetTypeFiltersAreIndependent()
    {
        Set("damagePlayers", false);
        PlayerHealth player = Player(Vector3.forward * 2f);
        var enemy = Create("Enemy", Vector3.forward * 4f).AddComponent<EnemyHealth>();
        enemy.gameObject.AddComponent<BoxCollider>();
        typeof(EnemyHealth).GetMethod("Awake", Private).Invoke(enemy, null);
        HitAt(0.5f);
        Assert.That(player.CurrentHealth, Is.EqualTo(100f));
        Assert.That(enemy.CurrentHealth, Is.EqualTo(95f));
    }

    [Test]
    public void MovingDispenserReturnsToItsAuthoredPoseWhenStopped()
    {
        Vector3 position = trap.transform.localPosition;
        Quaternion rotation = trap.transform.localRotation;
        Set("movingPart", trap.transform);
        Set("positionAmplitude", Vector3.up * 0.5f);
        Set("angularAmplitude", new Vector3(0f, 30f, 0f));
        Set("movementPeriod", 4f);
        HitAt(1f);
        Assert.That(Vector3.Distance(trap.transform.localPosition, position + Vector3.up * 0.5f), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(trap.transform.localRotation, rotation), Is.EqualTo(30f).Within(0.01f));
        trap.StopTrap();
        Assert.That(Vector3.Distance(trap.transform.localPosition, position), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(trap.transform.localRotation, rotation), Is.LessThan(0.01f));
    }

    [Test]
    public void DisablingTrapCancelsEmissionAndDamage()
    {
        PlayerHealth player = Player(Vector3.forward * 3f);
        trap.enabled = false;
        HitAt(0.5f);
        Assert.That(player.CurrentHealth, Is.EqualTo(100f));
        Assert.That(trap.FlameEffect.pause, Is.True);
        Assert.That(trap.Phase, Is.EqualTo(FlamethrowerTrapController.TrapPhase.Idle));
    }

    [Test]
    public void NaturalEndFadesExistingParticlesWithoutExtendingDamage()
    {
        Set("fadeOutDuration", 1f);
        PlayerHealth player = Player(Vector3.forward * 3f);
        HitAt(1.9f);
        float health = player.CurrentHealth;
        HitAt(2f);
        Assert.That(trap.IsEmitting, Is.False);
        Assert.That(trap.IsFadingOut, Is.True);
        Assert.That(trap.FlameEffect.pause, Is.False);
        Assert.That(trap.FlameEffect.GetFloat("Jet Opacity"), Is.EqualTo(1f).Within(0.001f));
        HitAt(2.5f);
        Assert.That(trap.FlameEffect.GetFloat("Jet Opacity"), Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(player.CurrentHealth, Is.EqualTo(health));
        HitAt(3f);
        Assert.That(trap.IsFadingOut, Is.False);
        Assert.That(trap.FlameEffect.pause, Is.True);
        Assert.That(trap.FlameEffect.GetFloat("Jet Opacity"), Is.Zero);
    }

    [Test]
    public void ManualStopFadesOnceAndPreservesTheLastJetPose()
    {
        Set("fadeOutDuration", 1f);
        Set("movingPart", trap.transform);
        Set("angularAmplitude", Vector3.up * 30f);
        Set("movementPeriod", 4f);
        HitAt(1f);
        Vector3 origin = trap.JetOrigin;
        Quaternion rotation = trap.JetRotation;
        trap.StopTrap();
        double stopped = (double)typeof(FlamethrowerTrapController).GetField("activationTime", Private).GetValue(trap);
        Assert.That(trap.IsFadingOut, Is.True);
        TickAt(stopped + 0.25d);
        float early = trap.FlameEffect.GetFloat("Jet Opacity");
        TickAt(stopped + 0.5d);
        Assert.That(trap.FlameEffect.GetFloat("Jet Opacity"), Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(early, Is.GreaterThan(0.5f));
        Assert.That(Vector3.Distance(trap.FlameEffect.transform.position, origin), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(trap.FlameEffect.transform.rotation, rotation), Is.LessThan(0.01f));
        TickAt(stopped + 1.01d);
        Assert.That(trap.FlameEffect.pause, Is.True);
    }

    [Test]
    public void ZeroFadeDurationPreservesImmediateStop()
    {
        Set("fadeOutDuration", 0f);
        HitAt(0.5f);
        trap.StopTrap();
        Assert.That(trap.IsFadingOut, Is.False);
        Assert.That(trap.FlameEffect.pause, Is.True);
    }

    [Test]
    public void NewTriggerReplacesTheFadeAndRestoresFullOpacity()
    {
        HitAt(0.5f);
        trap.StopTrap();
        Assert.That(trap.IsFadingOut, Is.True);
        Assert.That(trap.TryTriggerTrap(), Is.True);
        Assert.That(trap.IsEmitting, Is.True);
        Assert.That(trap.IsFadingOut, Is.False);
        Assert.That(trap.FlameEffect.GetFloat("Jet Opacity"), Is.EqualTo(1f));
    }

    [Test]
    public void EndLightFollowsTheFlameFrontAndStaysOutsideObliqueWalls()
    {
        Set("fillTime", 1f);
        var wall = Create("Angled Wall", Vector3.forward * 3f).AddComponent<BoxCollider>();
        wall.size = new Vector3(8f, 8f, 0.5f);
        wall.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        HitAt(0.2f);
        Assert.That(trap.EndLight.enabled, Is.True);
        Assert.That(Vector3.Distance(trap.JetOrigin, trap.EndLight.transform.position), Is.LessThan(trap.ResolvedLength * 0.3f));
        HitAt(1.1f);
        Assert.That(wall.Raycast(new Ray(trap.JetOrigin, trap.JetDirection), out RaycastHit hit, trap.Range), Is.True);
        Assert.That(Vector3.Dot(trap.EndLight.transform.position - hit.point, hit.normal), Is.GreaterThan(0.19f));
        Vector3 impactLight = trap.EndLight.transform.position;
        Set("angularAmplitude", Vector3.up * 90f);
        Set("movementPeriod", 4f);
        HitAt(1f);
        Assert.That(Vector3.Distance(trap.EndLight.transform.position, impactLight), Is.GreaterThan(1f));
    }

    [Test]
    public void EndLightFadesAtTheLastEndpointAndTurnsOffWithTheFire()
    {
        Set("fadeOutDuration", 1f);
        HitAt(1.9f);
        float fullIntensity = trap.EndLight.intensity;
        Vector3 endpoint = trap.EndLight.transform.position;
        Assert.That(fullIntensity, Is.GreaterThan(0f));
        HitAt(2.5f);
        Assert.That(trap.IsEmitting, Is.False);
        Assert.That(trap.EndLight.enabled, Is.True);
        Assert.That(trap.EndLight.intensity, Is.EqualTo(fullIntensity * 0.5f).Within(0.001f));
        Assert.That(Vector3.Distance(trap.EndLight.transform.position, endpoint), Is.LessThan(0.001f));
        HitAt(3.01f);
        Assert.That(trap.EndLight.enabled, Is.False);
        Assert.That(trap.EndLight.intensity, Is.Zero);
    }

    [Test]
    public void DispenserPrefabUsesTheExistingPivotAndDoesNotAutoPlay()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlamethrowerTrapAuthoringUtility.PrefabPath);
        var controller = prefab.GetComponent<FlamethrowerTrapController>();
        Assert.That(controller, Is.Not.Null);
        var data = new SerializedObject(controller);
        var pivot = (Transform)data.FindProperty("flamethrowerPivot").objectReferenceValue;
        Assert.That(pivot.name, Is.EqualTo("Flamethrower_Pivot"));
        var effect = prefab.GetComponentInChildren<VisualEffect>(true);
        Assert.That(effect.initialEventName, Is.EqualTo("OnStop"));
        Assert.That(effect.visualEffectAsset, Is.SameAs(AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(FlamethrowerTrapAuthoringUtility.GraphPath)));
        var audio = (AudioSource)data.FindProperty("flameAudioSource").objectReferenceValue;
        Assert.That(audio, Is.Not.Null, "AudioSource must exist in the saved prefab before activation.");
        Assert.That(audio.transform.parent, Is.EqualTo(prefab.transform));
        Assert.That(audio.name, Is.EqualTo(FlamethrowerTrapController.FlameAudioObjectName));
        Assert.That(audio.playOnAwake, Is.False);
        Assert.That(audio.dopplerLevel, Is.Zero);
        Assert.That(prefab.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(1));
    }

    [Test]
    public void AudioAuthoringReusesDedicatedSourceWithoutChangingOtherAudio()
    {
        var unrelated = new GameObject("Other Audio");
        unrelated.transform.SetParent(trap.transform, false);
        AudioSource other = unrelated.AddComponent<AudioSource>();
        other.volume = 0.37f;
        var dedicated = new GameObject(FlamethrowerTrapController.FlameAudioObjectName);
        dedicated.transform.SetParent(trap.transform, false);
        AudioSource source = dedicated.AddComponent<AudioSource>();

        FlamethrowerTrapAuthoringUtility.PrepareAudioSource(trap.gameObject);
        FlamethrowerTrapAuthoringUtility.PrepareAudioSource(trap.gameObject);

        var data = new SerializedObject(trap);
        Assert.That(data.FindProperty("flameAudioSource").objectReferenceValue, Is.SameAs(source));
        Assert.That(trap.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(2));
        Assert.That(other.volume, Is.EqualTo(0.37f).Within(0.001f));
        Assert.That(source.playOnAwake, Is.False);
    }

    [Test]
    public void EditorPreviewDoesNotCreateRuntimeAudio()
    {
        typeof(FlamethrowerTrapController).GetMethod("PrepareFlameAudio", Private).Invoke(trap, null);
        Assert.That(trap.GetComponentsInChildren<AudioSource>(true), Is.Empty);
    }

    [Test]
    public void FlamethrowerClipIsPreparedBeforePlayback()
    {
        var importer = (AudioImporter)AssetImporter.GetAtPath("Assets/SFX/Flamethrower.mp3");
        Assert.That(importer.defaultSampleSettings.preloadAudioData, Is.True);
        Assert.That(importer.loadInBackground, Is.True);
    }

    [Test]
    public void ShaderWarmupContainsOnlyCapturedFlamethrowerRenderStates()
    {
        var states = Resources.Load<UnityEngine.Experimental.Rendering.GraphicsStateCollection>(FlamethrowerTrapController.ShaderWarmupResourceName);
        Assert.That(states, Is.Not.Null, "DX12 warmup must ship with the captured VFX states.");
        Assert.That(states.graphicsDeviceType, Is.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Direct3D12));
        Assert.That(states.qualityLevelName, Is.EqualTo("PC"));
        Assert.That(states.totalGraphicsStateCount, Is.GreaterThan(0));
        var variants = new List<UnityEngine.Experimental.Rendering.GraphicsStateCollection.ShaderVariant>();
        states.GetVariants(variants);
        Assert.That(variants, Is.Not.Empty);
        foreach (var variant in variants)
        {
            Assert.That(variant.shader, Is.Not.Null);
            Assert.That(variant.shader.name, Does.Contain("Flamethrower"), "World/UI shader states must not be included in the targeted warmup.");
        }
    }

    private void HitAt(float elapsed)
        => TickAt(started + elapsed);

    private void TickAt(double time)
    {
        Physics.SyncTransforms();
        typeof(FlamethrowerTrapController).GetMethod("RefreshAtTime", Private).Invoke(trap, new object[] { time });
        typeof(FlamethrowerTrapController).GetMethod("ApplyDamageTick", Private).Invoke(trap, new object[] { time });
    }
    private PlayerHealth Player(Vector3 offset)
    {
        var player = Create("Player", offset).AddComponent<PlayerHealth>();
        player.gameObject.AddComponent<BoxCollider>();
        typeof(PlayerHealth).GetMethod("Awake", Private).Invoke(player, null);
        player.GetComponent<Rigidbody>().isKinematic = false;
        return player;
    }
    private GameObject Create(string name, Vector3 offset)
    {
        var result = new GameObject(name);
        result.transform.position = Origin + offset;
        objects.Add(result);
        return result;
    }
    private void Set(string name, object value) => typeof(FlamethrowerTrapController).GetField(name, Private).SetValue(trap, value);
}
