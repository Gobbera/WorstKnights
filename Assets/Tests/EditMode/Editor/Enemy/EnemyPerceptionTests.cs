using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class EnemyPerceptionTests
{
    [Test]
    public void EnemyHearing_ConsumesNoiseWhenEventIsAudible()
    {
        GameObject enemy = new GameObject("Enemy");
        EnemyHearing hearing = enemy.AddComponent<EnemyHearing>();
        try
        {
            SerializedObject serializedObject = new SerializedObject(hearing);
            serializedObject.FindProperty("enableHearing").boolValue = true;
            serializedObject.FindProperty("hearingMultiplier").floatValue = 1f;
            serializedObject.FindProperty("minimumAudibleIntensity").floatValue = 0.05f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            NoiseSystem.EmitNoise(new Vector3(3f, 0f, 0f), radius: 5f, intensity: 1f, NoiseType.Footstep);

            Assert.That(hearing.TryConsumeNoise(out NoiseEvent noiseEvent), Is.True);
            Assert.That(noiseEvent.Position, Is.EqualTo(new Vector3(3f, 0f, 0f)));
            Assert.That(hearing.TryConsumeNoise(out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyVision_DoesNotSeeTargetThroughObstacle()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject player = new GameObject("Player");
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);

        EnemyVision vision = enemy.AddComponent<EnemyVision>();
        CapsuleCollider playerCollider = player.AddComponent<CapsuleCollider>();
        PlayerHealth playerHealth = player.AddComponent<PlayerHealth>();

        try
        {
            enemy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            player.transform.position = new Vector3(0f, 0f, 5f);
            playerCollider.center = Vector3.up;
            playerCollider.height = 2f;
            playerCollider.radius = 0.35f;

            obstacle.transform.position = new Vector3(0f, 1f, 2.5f);
            obstacle.transform.localScale = new Vector3(2f, 2f, 0.25f);

            SerializedObject serializedObject = new SerializedObject(vision);
            serializedObject.FindProperty("enableVision").boolValue = true;
            serializedObject.FindProperty("viewDistance").floatValue = 10f;
            serializedObject.FindProperty("viewAngle").floatValue = 90f;
            serializedObject.FindProperty("targetLayers").intValue = Physics.DefaultRaycastLayers;
            serializedObject.FindProperty("obstacleLayers").intValue = Physics.DefaultRaycastLayers;
            serializedObject.FindProperty("eyeHeightFallback").floatValue = 1f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            Physics.SyncTransforms();

            Assert.That(vision.CanSeeTarget(playerHealth, out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(obstacle);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyVision_CanSenseCloseTargetOutsideViewCone()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject player = new GameObject("Player");

        EnemyVision vision = enemy.AddComponent<EnemyVision>();
        CapsuleCollider playerCollider = player.AddComponent<CapsuleCollider>();
        PlayerHealth playerHealth = player.AddComponent<PlayerHealth>();

        try
        {
            enemy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            player.transform.position = new Vector3(0f, 0f, -1f);
            playerCollider.center = Vector3.up;
            playerCollider.height = 2f;
            playerCollider.radius = 0.35f;

            SerializedObject serializedObject = new SerializedObject(vision);
            serializedObject.FindProperty("enableVision").boolValue = true;
            serializedObject.FindProperty("viewDistance").floatValue = 10f;
            serializedObject.FindProperty("viewAngle").floatValue = 45f;
            serializedObject.FindProperty("targetLayers").intValue = Physics.DefaultRaycastLayers;
            serializedObject.FindProperty("obstacleLayers").intValue = Physics.DefaultRaycastLayers;
            serializedObject.FindProperty("eyeHeightFallback").floatValue = 1f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            Physics.SyncTransforms();

            Assert.That(vision.CanSeeTarget(playerHealth, out _), Is.False);
            Assert.That(vision.CanSenseTargetInRadius(playerHealth, 1.6f, requireLineOfSight: true, out _), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
        }
    }
}
