using System.Collections;
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class SkeletonArcherPlayModeTests
{
    private GameObject stage;
    private GameObject actor;
    private PlayerHealth target;
    private EnemyArcherCombat combat;
    private EnemyBrain actorBrain;

    [UnitySetUp]
    public IEnumerator EnterIsolatedRuntime()
    {
        yield return new EnterPlayMode();
        // Disable only runtime instances. ExitPlayMode restores the user's scene.
        for (int i = 0; i < SceneManager.sceneCount; i++)
            foreach (GameObject root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
        Scene validation = SceneManager.CreateScene("Skeleton Archer Runtime Validation");
        SceneManager.SetActiveScene(validation);
        stage = new GameObject("Skeleton validation stage");
        stage.transform.position = new Vector3(10000, 0, 10000);
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.SetParent(stage.transform, false);
        floor.transform.localPosition = new Vector3(0, -0.5f, 0);
        floor.transform.localScale = new Vector3(40, 1, 40);
        NavMeshSurface surface = stage.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        stage.SetActive(false);
        actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonAuthoringBuilder.ArcherPath), stage.transform);
        actor.transform.localPosition = new Vector3(0, 0.08f, 0);
        actorBrain = actor.GetComponent<EnemyBrain>();
        actorBrain.enabled = false;
        var brain = new SerializedObject(actor.GetComponent<EnemyBrain>());
        brain.FindProperty("requireSpawnAnimation").boolValue = false;
        brain.FindProperty("alertBeforeChaseOnFirstTargetDetection").boolValue = false;
        brain.FindProperty("defaultState").intValue = (int)EnemyState.Idle;
        brain.ApplyModifiedPropertiesWithoutUndo();
        var presentation = new SerializedObject(actor.GetComponent<EnemyAnimationController>());
        presentation.FindProperty("playPreSpawnAndSpawn").boolValue = false;
        presentation.ApplyModifiedPropertiesWithoutUndo();
        GameObject player = new GameObject("Archer test player");
        player.transform.SetParent(stage.transform, false);
        player.transform.localPosition = new Vector3(0, 0, 10);
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) player.layer = playerLayer;
        var capsule = player.AddComponent<CapsuleCollider>();
        capsule.height = 1.8f; capsule.radius = 0.35f; capsule.center = Vector3.up * 0.9f;
        var body = player.AddComponent<Rigidbody>();
        body.isKinematic = true; body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeAll;
        target = player.AddComponent<PlayerHealth>();
        combat = actor.GetComponent<EnemyArcherCombat>();
        var vision = new SerializedObject(actor.GetComponent<EnemyVision>());
        vision.FindProperty("targetLayers").intValue = 1 << player.layer;
        vision.FindProperty("obstacleLayers").intValue = Physics.DefaultRaycastLayers;
        vision.ApplyModifiedPropertiesWithoutUndo();
        stage.SetActive(true);
        Physics.SyncTransforms();
        yield return null;
    }

    [UnityTest]
    public IEnumerator ArcherPreparesThenReleasesOneArrowThatDamagesThePlayerOnce()
    {
        actorBrain.enabled = true;
        float deadline = Time.time + 5f;
        while (combat.ShotSequence == 0 && Time.time < deadline) yield return null;
        Assert.That(combat.ShotSequence, Is.EqualTo(1), "A visible in-range player should start the shot.");
        float started = Time.time;
        float until = Time.time + 0.65f;
        while (Time.time < until) yield return null;
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth), "Windup must not deal melee damage.");
        Assert.That(Object.FindObjectsByType<EnemyArrowProjectile>(FindObjectsInactive.Include).Length, Is.Zero);
        deadline = Time.time + 5f;
        while (target.CurrentHealth == target.MaxHealth && Time.time < deadline) yield return null;
        Assert.That(Time.time - started, Is.GreaterThan(2f), "The authored release occurs at frame 57.");
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth - 12f));
        combat.enabled = false;
        until = Time.time + 0.4f;
        while (Time.time < until) yield return null;
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth - 12f), "One projectile cannot apply repeated damage.");
    }

    [UnityTest]
    public IEnumerator StunDuringPreparationCannotReleaseADelayedArrow()
    {
        actorBrain.enabled = true;
        float deadline = Time.time + 5f;
        while (combat.ShotSequence == 0 && Time.time < deadline) yield return null;
        Assert.That(combat.IsAttacking, Is.True);
        actor.GetComponent<EnemyBrain>().ApplyStun(4f);
        float until = Time.time + 3.4f;
        while (Time.time < until) yield return null;
        Assert.That(combat.IsAttacking, Is.False);
        Assert.That(Object.FindObjectsByType<EnemyArrowProjectile>(FindObjectsInactive.Include).Length, Is.Zero);
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth));
    }

    [UnityTest]
    public IEnumerator AWallAddedDuringPreparationPreventsTheShotFromHittingThePlayer()
    {
        actorBrain.enabled = true;
        float deadline = Time.time + 5f;
        while (combat.ShotSequence == 0 && Time.time < deadline) yield return null;
        Assert.That(combat.IsAttacking, Is.True);
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.SetParent(stage.transform, false);
        wall.transform.localPosition = new Vector3(0, 1.5f, 5f);
        wall.transform.localScale = new Vector3(8, 3, 0.05f);
        Physics.SyncTransforms();
        float until = Time.time + 3.5f;
        while (Time.time < until) yield return null;
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth));
    }

    [UnityTest]
    public IEnumerator AClosePlayerMakesTheArcherRetreatAlongTheNavMesh()
    {
        target.transform.localPosition = new Vector3(0, 0, 2.5f);
        Physics.SyncTransforms();
        actorBrain.enabled = true;
        float startingDistance = Vector3.Distance(actor.transform.position, target.transform.position);
        float deadline = Time.time + 3f;
        while (Vector3.Distance(actor.transform.position, target.transform.position) < startingDistance + 1f
            && Time.time < deadline) yield return null;
        Assert.That(Vector3.Distance(actor.transform.position, target.transform.position), Is.GreaterThan(startingDistance + 1f));
        Assert.That(actor.GetComponent<NavMeshAgent>().isOnNavMesh, Is.True);
    }

    [UnityTest]
    public IEnumerator AClosePlayerDuringCooldownDoesNotResetTheRetreatPathEveryTick()
    {
        actorBrain.enabled = true;
        float deadline = Time.time + 5f;
        while (combat.ShotSequence == 0 && Time.time < deadline) yield return null;
        Assert.That(combat.IsAttacking, Is.True);
        while (combat.IsAttacking && Time.time < deadline) yield return null;
        target.transform.position = actor.transform.position + Vector3.forward * 2.5f;
        Physics.SyncTransforms();
        float initial = Vector3.Distance(actor.transform.position, target.transform.position);
        deadline = Time.time + 2f;
        while (Vector3.Distance(actor.transform.position, target.transform.position) < initial + 0.75f
            && Time.time < deadline) yield return null;
        Assert.That(Vector3.Distance(actor.transform.position, target.transform.position), Is.GreaterThanOrEqualTo(initial + 0.75f));
    }

    [UnityTest]
    public IEnumerator ExistingPerceptionAndCombatContractsStillPassWithRuntimeInitialization()
    {
        int passed = 0, failed = 0;
        string path = ".codex/diagnostics/skeleton-perception-contracts.txt";
        File.WriteAllText(path, "Existing perception and combat contracts executed on the Unity main thread in Play Mode.\n");
        foreach (Type type in new[] { typeof(EnemyPerceptionTests) })
        {
            object fixture = Activator.CreateInstance(type);
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!Attribute.IsDefined(method, typeof(TestAttribute))) continue;
                try { method.Invoke(fixture, null); passed++; File.AppendAllText(path, "PASS " + type.Name + "." + method.Name + "\n"); }
                catch (Exception exception)
                {
                    failed++;
                    File.AppendAllText(path, "FAIL " + type.Name + "." + method.Name + " " + (exception.InnerException ?? exception) + "\n");
                }
            }
        }
        File.AppendAllText(path, "passed=" + passed + " failed=" + failed + "\n");
        Assert.That(failed, Is.Zero, "See " + path);
        yield return null;
    }

    [UnityTest]
    public IEnumerator AGuardingShieldStopsTheArrowWithoutPlayerDamage()
    {
        GameObject shield = new GameObject("Archer shield block test");
        shield.transform.SetParent(target.transform, false);
        shield.transform.localPosition = new Vector3(0, 0.9f, -0.65f);
        var collider = shield.AddComponent<BoxCollider>();
        collider.size = new Vector3(1.4f, 1.8f, 0.1f);
        var guard = shield.AddComponent<ShieldGuardHitbox>();
        guard.SetOwnerHealth(target); guard.SetGuardState(true, true);
        Physics.SyncTransforms();
        actorBrain.enabled = true;
        float deadline = Time.time + 6f;
        EnemyArrowProjectile impacted = null;
        while (Time.time < deadline && impacted == null)
        {
            impacted = System.Array.Find(Object.FindObjectsByType<EnemyArrowProjectile>(FindObjectsInactive.Include), p => p.HasImpacted);
            yield return null;
        }
        Assert.That(impacted, Is.Not.Null, "The shield trigger should consume the physical arrow.");
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth));
        Assert.That(guard.IsBlocking, Is.True);
    }

    [UnityTest]
    public IEnumerator APlayerHitEmbedsFollowsAndFadesBeforeCleanup()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonAuthoringBuilder.ProjectilePath);
        var arrow = Object.Instantiate(asset, stage.transform.position + new Vector3(0, 0.9f, 6),
            Quaternion.identity).GetComponent<EnemyArrowProjectile>();
        SceneManager.MoveGameObjectToScene(arrow.gameObject, stage.scene);
        var settings = new SerializedObject(arrow);
        settings.FindProperty("gravity").floatValue = 0f;
        settings.FindProperty("impactLifetime").floatValue = 1.2f;
        settings.FindProperty("impactFadeOutDuration").floatValue = 0.6f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Vector3 contact = Vector3.zero;
        arrow.Impacted += (projectile, point, normal) => contact = point;
        arrow.Launch(actor, Vector3.forward * 22f, 12f, true);
        float deadline = Time.time + 1f;
        while (!arrow.HasImpacted && Time.time < deadline) yield return null;
        Assert.That(arrow.HasImpacted, Is.True);
        Assert.That(arrow.ImpactTarget, Is.EqualTo(target.transform));
        Vector3 tip = arrow.transform.position + arrow.transform.forward * arrow.ArrowLength;
        Assert.That(Vector3.Dot(tip - contact, arrow.transform.forward), Is.EqualTo(0.6f).Within(0.002f));
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth - 12f));
        Assert.That(arrow.GetComponent<SpawnedDestructibleFadeOut>(), Is.Not.Null);

        float impactedAt = Time.time;
        Vector3 localPosition = target.transform.InverseTransformPoint(arrow.transform.position);
        Quaternion localRotation = Quaternion.Inverse(target.transform.rotation) * arrow.transform.rotation;
        target.transform.SetPositionAndRotation(target.transform.position + new Vector3(2, 1, -1),
            Quaternion.Euler(0, 75, 0));
        float until = impactedAt + 0.2f;
        while (Time.time < until) yield return null;
        Assert.That(Vector3.Distance(arrow.transform.position, target.transform.TransformPoint(localPosition)), Is.LessThan(0.002f));
        Assert.That(Quaternion.Angle(arrow.transform.rotation, target.transform.rotation * localRotation), Is.LessThan(0.01f));

        Renderer visual = arrow.GetComponentInChildren<Renderer>();
        Material sharedMaterial = visual.sharedMaterial;
        Assert.That(sharedMaterial.GetFloat("_FadeAlpha"), Is.EqualTo(1f).Within(0.001f));
        target.gameObject.SetActive(false);
        until = impactedAt + 0.85f;
        while (Time.time < until) yield return null;
        Assert.That(arrow != null, Is.True);
        Assert.That(visual.sharedMaterial.GetFloat("_FadeAlpha"), Is.InRange(0.01f, 0.99f));
        Assert.That(sharedMaterial.GetFloat("_FadeAlpha"), Is.EqualTo(1f).Within(0.001f));
        until = impactedAt + 1.4f;
        while (Time.time < until) yield return null;
        Assert.That(arrow == null, Is.True);
    }

    [UnityTest]
    public IEnumerator AReplicatedEmbeddedArrowFollowsWithoutApplyingDamage()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonAuthoringBuilder.ProjectilePath);
        var arrow = Object.Instantiate(asset, stage.transform.position, Quaternion.identity).GetComponent<EnemyArrowProjectile>();
        SceneManager.MoveGameObjectToScene(arrow.gameObject, stage.scene);
        arrow.Launch(actor, Vector3.forward * 22f, 12f, false);
        Vector3 position = target.transform.position + new Vector3(0, 0.9f, -0.9f);
        Quaternion rotation = Quaternion.Euler(10, 0, 0);
        arrow.ApplyReplicatedImpactPose(position, rotation, target.transform);
        arrow.ApplyReplicatedImpact(position + Vector3.left * 10f, Vector3.up);
        Vector3 localPosition = target.transform.InverseTransformPoint(position);
        target.transform.SetPositionAndRotation(target.transform.position + Vector3.right * 3f,
            Quaternion.Euler(0, 90, 0));
        float until = Time.time + 0.2f;
        while (Time.time < until) yield return null;
        Assert.That(arrow.HasImpacted, Is.True);
        Assert.That(Vector3.Distance(arrow.transform.position, target.transform.TransformPoint(localPosition)), Is.LessThan(0.002f));
        Assert.That(Quaternion.Angle(arrow.transform.rotation, target.transform.rotation * rotation), Is.LessThan(0.01f));
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth));
        Object.Destroy(arrow.gameObject);
    }

    [UnityTest]
    public IEnumerator AReplicatedVisualArrowCannotDealDamageLocally()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonAuthoringBuilder.ProjectilePath);
        var arrow = Object.Instantiate(asset, stage.transform.position + Vector3.up, Quaternion.identity).GetComponent<EnemyArrowProjectile>();
        SceneManager.MoveGameObjectToScene(arrow.gameObject, stage.scene);
        typeof(EnemyArrowProjectile).GetField("gravity", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(arrow, 0f);
        arrow.Launch(actor, Vector3.forward * 22f, 12f, false);
        File.WriteAllText(".codex/diagnostics/skeleton-remote-projectile-state.txt", ProjectileState(arrow));
        float until = Time.time + 0.65f;
        while (Time.time < until) yield return null;
        File.AppendAllText(".codex/diagnostics/skeleton-remote-projectile-state.txt", ProjectileState(arrow));
        Assert.That(arrow.transform.position.z, Is.GreaterThan(target.transform.position.z));
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth));
        arrow.ApplyReplicatedImpact(target.transform.position + Vector3.up, Vector3.back);
        Vector3 stopped = arrow.transform.position;
        until = Time.time + 0.1f;
        while (Time.time < until) yield return null;
        Assert.That(arrow.transform.position, Is.EqualTo(stopped));
    }

    private static string ProjectileState(EnemyArrowProjectile arrow)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return "time=" + Time.time + " position=" + arrow.transform.position + " velocity=" + arrow.Velocity
            + " enabled=" + arrow.enabled + " active=" + arrow.gameObject.activeInHierarchy + " scene=" + arrow.gameObject.scene.name
            + " launched=" + typeof(EnemyArrowProjectile).GetField("launched", flags).GetValue(arrow)
            + " age=" + typeof(EnemyArrowProjectile).GetField("age", flags).GetValue(arrow)
            + " terminal=" + arrow.HasImpacted + "\n";
    }

    [UnityTearDown]
    public IEnumerator RestoreEditor()
    {
        if (stage != null) Object.Destroy(stage);
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
}
