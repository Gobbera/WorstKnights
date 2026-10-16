using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class AtmosphericAdaptationControllerTests
{
    [Test]
    public void SampleLightInfluence_IgnoresLightBehindWall()
    {
        GameObject controllerObject = new GameObject("Atmospheric Adaptation Test");
        GameObject lightObject = new GameObject("Point Light");
        GameObject wallObject = null;

        try
        {
            AtmosphericAdaptationController controller = controllerObject.AddComponent<AtmosphericAdaptationController>();
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = 2.2f;
            light.range = 6f;
            lightObject.transform.position = new Vector3(0f, 0f, 3f);

            SetPrivateField(controller, "cachedLights", new[] { light });
            SetPrivateField(controller, "nextLightRefreshTime", float.MaxValue);

            float visibleInfluence = SampleLightInfluence(controller, Vector3.zero);

            wallObject = new GameObject("Wall");
            wallObject.transform.position = new Vector3(0f, 0f, 1.5f);
            BoxCollider wall = wallObject.AddComponent<BoxCollider>();
            wall.size = Vector3.one;
            Physics.SyncTransforms();

            float blockedInfluence = SampleLightInfluence(controller, Vector3.zero);

            Assert.That(visibleInfluence, Is.GreaterThan(0.1f));
            Assert.That(blockedInfluence, Is.EqualTo(0f));
        }
        finally
        {
            if (wallObject != null)
                Object.DestroyImmediate(wallObject);
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(controllerObject);
        }
    }

    [Test]
    public void SampleLightInfluence_RespondsToNearbyTorchStrength()
    {
        GameObject controllerObject = new GameObject("Atmospheric Adaptation Test");
        GameObject lightObject = new GameObject("Torch Light");

        try
        {
            AtmosphericAdaptationController controller = controllerObject.AddComponent<AtmosphericAdaptationController>();
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = 0.4f;
            light.range = 6f;
            lightObject.transform.position = new Vector3(0f, 0f, 2f);

            SetPrivateField(controller, "cachedLights", new[] { light });
            SetPrivateField(controller, "nextLightRefreshTime", float.MaxValue);
            SetPrivateField(controller, "intensityForFullLight", 0.25f);

            Assert.That(SampleLightInfluence(controller, Vector3.zero), Is.GreaterThan(0.6f));
        }
        finally
        {
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(controllerObject);
        }
    }

    private static float SampleLightInfluence(AtmosphericAdaptationController controller, Vector3 position)
    {
        MethodInfo method = typeof(AtmosphericAdaptationController).GetMethod(
            "SampleLightInfluence",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return (float)method.Invoke(controller, new object[] { position });
    }

    private static void SetPrivateField(AtmosphericAdaptationController controller, string name, object value)
    {
        FieldInfo field = typeof(AtmosphericAdaptationController).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(controller, value);
    }
}
