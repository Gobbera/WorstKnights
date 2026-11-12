using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class SkeletonArcherTests
{
    [TestCase(8f, 0f)]
    [TestCase(16f, 2f)]
    [TestCase(12f, -3f)]
    public void BallisticLowArcReachesTheRequestedPoint(float range, float height)
    {
        Vector3 start = new Vector3(2, 1.5f, -3);
        Vector3 target = start + new Vector3(range * 0.6f, height, range * 0.8f);
        Assert.That(EnemyArcherBallistics.TryGetLaunchVelocity(start, target, 22f, 9.81f, out Vector3 velocity), Is.True);
        float duration = range / Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
        Vector3 actual = start + velocity * duration + Vector3.down * (4.905f * duration * duration);
        Assert.That(Vector3.Distance(actual, target), Is.LessThan(0.0001f));
        Assert.That(velocity.magnitude, Is.EqualTo(22f).Within(0.0001f));
    }

    [Test]
    public void UnreachableOrInvalidBallisticsCannotCreateProjectiles()
    {
        Assert.That(EnemyArcherBallistics.TryGetLaunchVelocity(Vector3.zero, Vector3.forward * 100f, 5f, 9.81f, out _), Is.False);
        Assert.That(EnemyArcherBallistics.TryGetLaunchVelocity(Vector3.zero, Vector3.forward, 0f, 9.81f, out _), Is.False);
        Assert.That(EnemyArcherBallistics.TryGetLaunchVelocity(new Vector3(float.NaN, 0, 0), Vector3.forward, 22f, 9.81f, out _), Is.False);
        Assert.That(EnemyArcherBallistics.TryGetLaunchVelocity(Vector3.zero, Vector3.zero, 22f, 9.81f, out _), Is.False);
    }

    [Test]
    public void RetreatHysteresisDoesNotOscillateAtThePreferredMinimum()
    {
        Assert.That(EnemyArcherBallistics.ShouldRetreat(3.9f, false, 4f, 6.5f), Is.True);
        Assert.That(EnemyArcherBallistics.ShouldRetreat(4.1f, false, 4f, 6.5f), Is.False);
        Assert.That(EnemyArcherBallistics.ShouldRetreat(4.1f, true, 4f, 6.5f), Is.True);
        Assert.That(EnemyArcherBallistics.ShouldRetreat(6.6f, true, 4f, 6.5f), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void VariantsShareTheBaseAndAllControllerBindingsResolve(bool archer)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(archer ? SkeletonAuthoringBuilder.ArcherPath : SkeletonAuthoringBuilder.SwordsmanPath);
        Assert.That(asset, Is.Not.Null);
        Assert.That(PrefabUtility.GetPrefabAssetType(asset), Is.EqualTo(PrefabAssetType.Variant));
        GameObject baseAsset = PrefabUtility.GetCorrespondingObjectFromSource(asset);
        Assert.That(AssetDatabase.GetAssetPath(baseAsset), Is.EqualTo(SkeletonAuthoringBuilder.BasePath));
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
            Animator animator = instance.GetComponentInChildren<Animator>(true);
            Assert.That(instance.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(instance.GetComponent<EnemyAttack>().AttackEnabled, Is.EqualTo(!archer));
            Assert.That(instance.GetComponent<EnemyArcherCombat>() != null, Is.EqualTo(archer));
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips.Distinct())
            {
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(Transform) && binding.type != typeof(SkinnedMeshRenderer)) continue;
                    Transform target = string.IsNullOrEmpty(binding.path) ? animator.transform : animator.transform.Find(binding.path);
                    Assert.That(target, Is.Not.Null, clip.name + " -> " + binding.path);
                    if (binding.type == typeof(SkinnedMeshRenderer)) Assert.That(target.GetComponent<SkinnedMeshRenderer>(), Is.Not.Null);
                }
            }
            Assert.That(animator.transform.Find("Skeleton/Root"), Is.Not.Null);
            Assert.That(instance.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m != null)), Is.True);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    [Test]
    public void BowDrawAndPresentationArrowMatchExportedTimingAfterMovingTheActor()
    {
        var source = JsonUtility.FromJson<SkeletonAuthoringBuilder.ExportData>(File.ReadAllText(SkeletonAuthoringBuilder.DirectoryPath + "/SkeletonExport.json"));
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonAuthoringBuilder.ArcherPath), preview);
            Animator animator = instance.GetComponentInChildren<Animator>(true);
            Transform arrow = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "ArrowVisual");
            arrow.gameObject.SetActive(true);
            SkinnedMeshRenderer bow = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(r => r.sharedMesh.GetBlendShapeIndex("Bow_Draw_IA") >= 0);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SkeletonAuthoringBuilder.DirectoryPath + "/Animations/SkeletonTesteIA.Attack_Bow_01.anim");
            GameObject calibration = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonAuthoringBuilder.DirectoryPath + "/Equipment/Arrow.fbx"), preview);
            Transform[] markers = calibration.GetComponentsInChildren<Transform>();
            Vector3 origin = markers.Single(t => t.name == "_CalibrationOrigin").position;
            Matrix4x4 conversion = Matrix4x4.identity;
            conversion.SetColumn(0, markers.Single(t => t.name == "_CalibrationX").position - origin);
            conversion.SetColumn(1, markers.Single(t => t.name == "_CalibrationY").position - origin);
            conversion.SetColumn(2, markers.Single(t => t.name == "_CalibrationZ").position - origin);
            conversion.SetColumn(3, new Vector4(origin.x, origin.y, origin.z, 1));
            instance.transform.SetPositionAndRotation(new Vector3(21, 3, -17), Quaternion.Euler(0, 63, 0));
            foreach (float frame in new[] { 24f, 32f, 46f, 52f, 56f, 57f })
            {
                clip.SampleAnimation(animator.gameObject, frame / source.frameRate);
                var sample = source.samples.Single(s => s.frame == frame);
                Vector3 sourceNock = new Vector3(sample.arrowMatrix[3], sample.arrowMatrix[7], sample.arrowMatrix[11]);
                Vector3 expected = animator.transform.TransformPoint(conversion.MultiplyPoint3x4(sourceNock));
                Vector3 actual = arrow.TransformPoint(conversion.MultiplyPoint3x4(Vector3.zero));
                Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.002f), "Frame " + frame);
                Assert.That(bow.GetBlendShapeWeight(bow.sharedMesh.GetBlendShapeIndex("Bow_Draw_IA")),
                    Is.EqualTo(sample.bowDraw * 100f).Within(0.02f));
            }
            Assert.That(clip.length, Is.EqualTo(76f / 24f).Within(0.002f));
            clip.SampleAnimation(animator.gameObject, 60f / 24f);
            Assert.That(arrow.localScale.sqrMagnitude, Is.LessThan(0.000001f), "The authoring preview must hide the released presentation arrow.");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    [Test]
    public void SweptArrowHitsAThinWallAndIgnoresItsOwner()
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject owner = new GameObject("Arrow owner test");
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject arrow = new GameObject("Swept arrow test");
        try
        {
            // Keep this default-physics query fixture far from the user's loaded map.
            Vector3 offset = new Vector3(10000, 10000, 10000);
            owner.transform.position = offset; owner.AddComponent<SphereCollider>().radius = 1.5f;
            wall.transform.position = offset + Vector3.forward * 3f;
            wall.transform.localScale = new Vector3(3, 3, 0.025f);
            arrow.transform.position = offset;
            var projectile = arrow.AddComponent<EnemyArrowProjectile>();
            var serialized = new SerializedObject(projectile);
            serialized.FindProperty("gravity").floatValue = 0f; serialized.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();
            projectile.Launch(owner, Vector3.forward * 100f, 12f, true);
            typeof(EnemyArrowProjectile).GetMethod("SimulateFlight", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(projectile, new object[] { 0.05f });
            Assert.That(projectile.HasImpacted, Is.True);
            Assert.That(arrow.transform.position.z - offset.z, Is.GreaterThan(1.5f));
            Assert.That(arrow.transform.position.z - offset.z, Is.LessThan(3f));
        }
        finally
        {
            Object.DestroyImmediate(owner); Object.DestroyImmediate(wall); Object.DestroyImmediate(arrow);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
