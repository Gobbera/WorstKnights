using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BeholderHipTrackingTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [Test]
    public void Prefab_ResolvesHipAndUsesAsymmetricLimits()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Enemies/Beholder/Beholder.prefab");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.transform.Find("Beholder_Armature/Root/Hip"), Is.Not.Null);

        var settings = new SerializedObject(prefab.GetComponent<BeholderProceduralAnimationController>());
        Assert.That(settings.FindProperty("enableHipTracking").boolValue, Is.True);
        Assert.That(settings.FindProperty("maxHipYaw").floatValue, Is.EqualTo(12f));
        Assert.That(settings.FindProperty("maxHipPitchUp").floatValue, Is.EqualTo(28f));
        Assert.That(settings.FindProperty("maxHipPitchDown").floatValue, Is.EqualTo(14f));
    }

    [Test]
    public void ResolveHipAngles_ClampsYawAndPitchIndependently()
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject root = new GameObject("Beholder");
        SceneManager.MoveGameObjectToScene(root, preview);
        GameObject hip = new GameObject("Hip");
        hip.transform.SetParent(root.transform);
        try
        {
            MethodInfo resolve = typeof(BeholderProceduralAnimationController)
                .GetMethod("ResolveHipAngles", PrivateStatic);
            Assert.That(resolve, Is.Not.Null);

            Vector2 above = (Vector2)resolve.Invoke(null, new object[] {
                root.transform, hip.transform, new Vector3(100f, 100f, 100f), 12f, 28f, 14f
            });
            Vector2 below = (Vector2)resolve.Invoke(null, new object[] {
                root.transform, hip.transform, new Vector3(-100f, -100f, 100f), 12f, 28f, 14f
            });

            Assert.That(above.x, Is.EqualTo(12f).Within(0.001f));
            Assert.That(above.y, Is.EqualTo(28f).Within(0.001f));
            Assert.That(below.x, Is.EqualTo(-12f).Within(0.001f));
            Assert.That(below.y, Is.EqualTo(-14f).Within(0.001f));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    [Test]
    public void HipOverlay_DoesNotAccumulateAndRestoresAnimatedPose()
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject root = new GameObject("Beholder");
        SceneManager.MoveGameObjectToScene(root, preview);
        GameObject hip = new GameObject("Hip");
        hip.transform.SetParent(root.transform);
        Quaternion animatedPose = Quaternion.Euler(0f, 18f, 0f);
        hip.transform.localRotation = animatedPose;
        try
        {
            var controller = root.AddComponent<BeholderProceduralAnimationController>();
            var settings = new SerializedObject(controller);
            settings.FindProperty("hipSmoothTime").floatValue = 0f;
            settings.FindProperty("maxHipYaw").floatValue = 12f;
            settings.FindProperty("maxHipPitchUp").floatValue = 28f;
            settings.FindProperty("maxHipPitchDown").floatValue = 14f;
            settings.FindProperty("hipBone").objectReferenceValue = hip.transform;
            settings.ApplyModifiedPropertiesWithoutUndo();

            typeof(BeholderProceduralAnimationController)
                .GetField("currentAimPosition", PrivateInstance)
                .SetValue(controller, new Vector3(10f, 10f, 10f));
            MethodInfo apply = typeof(BeholderProceduralAnimationController)
                .GetMethod("ApplyHipTracking", PrivateInstance);
            Assert.That(apply, Is.Not.Null);

            apply.Invoke(controller, new object[] { true });
            Quaternion firstTrackedPose = hip.transform.localRotation;
            Assert.That(Quaternion.Angle(animatedPose, firstTrackedPose), Is.GreaterThan(1f));
            Assert.That(root.transform.rotation, Is.EqualTo(Quaternion.identity));

            apply.Invoke(controller, new object[] { true });
            Assert.That(Quaternion.Angle(firstTrackedPose, hip.transform.localRotation), Is.LessThan(0.001f));

            apply.Invoke(controller, new object[] { false });
            Assert.That(Quaternion.Angle(animatedPose, hip.transform.localRotation), Is.LessThan(0.001f));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
