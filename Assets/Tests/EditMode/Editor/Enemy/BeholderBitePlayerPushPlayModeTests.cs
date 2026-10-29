using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class BeholderBitePlayerPushPlayModeTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTest]
    public IEnumerator Bite_PushesTheRealGroundedPlayerWhileStanding()
    {
        yield return new EnterPlayMode();
        yield return VerifyPush(false);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Bite_PushesTheRealGroundedPlayerWhileWalkingTowardTheBeholder()
    {
        yield return new EnterPlayMode();
        yield return VerifyPush(true);
        yield return new ExitPlayMode();
    }

    static IEnumerator VerifyPush(bool walking)
    {
        var stage = new GameObject("Real player bite push validation"); stage.SetActive(false);
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab"), stage.transform);
        Isolate(player);
        var movement = player.GetComponent<PlayerMovement>();
        var health = player.GetComponent<PlayerHealth>();
        var body = player.GetComponent<Rigidbody>();
        var capsule = player.GetComponent<CapsuleCollider>();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Bite push test floor"; floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(40f, 1f, 40f);
        var attacker = new GameObject("Bite push test Beholder");
        var controller = attacker.AddComponent<BeholderAttackController>();
        var chin = new GameObject("Chin"); chin.transform.SetParent(attacker.transform, false);
        var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BeholderBiteAttack>("Assets/WorstKnights/Models/Enemies/Beholder/Attacks/Attack_Bite.asset"));
        BeholderAttackContext context = null;
        try
        {
            stage.SetActive(true); Isolate(player);
            typeof(PlayerMovement).GetField("photonView", Private).SetValue(movement, null);
            typeof(PlayerHealth).GetField("photonView", Private).SetValue(health, null);
            body.isKinematic = false; body.detectCollisions = true; body.useGravity = true;
            capsule.enabled = true;
            player.transform.SetPositionAndRotation(new Vector3(0f, 1.05f, 0f), Quaternion.Euler(0f, 180f, 0f));
            var orientation = (Transform)typeof(PlayerMovement).GetField("orientation", Private).GetValue(movement);
            orientation.localRotation = Quaternion.identity;
            foreach (MouseLook look in player.GetComponentsInChildren<MouseLook>(true))
                typeof(MouseLook).GetField("viewYaw", Private).SetValue(look, 180f);
            Physics.SyncTransforms();
            float settleUntil = Time.time + 0.6f;
            while (Time.time < settleUntil)
            {
                movement.SetState(walking ? MovementState.walking : MovementState.idle);
                movement.Move(walking ? Vector2.up : Vector2.zero);
                yield return null;
            }
            Assert.That(movement.IsGrounded, Is.True, "Real player capsule stands on the test floor");
            float incomingSpeed = body.linearVelocity.z;
            if (walking) Assert.That(incomingSpeed, Is.LessThan(-1f), "Walking input is active before impact");
            attacker.transform.position = player.transform.position - Vector3.forward * 1.5f;
            context = new BeholderAttackContext(controller, settings, null, health, player.transform.position);
            float initialHealth = health.CurrentHealth, initialZ = player.transform.position.z;
            Assert.That(context.TryApplyMeleeDamage(20f, 3f, 2.5f, 0.12f), Is.True);
            float hitAt = Time.time, maximumDisplacement = 0f, maximumSpeed = 0f;
            while (Time.time < hitAt + 0.14f)
            {
                movement.Move(walking ? Vector2.up : Vector2.zero);
                maximumDisplacement = Mathf.Max(maximumDisplacement, player.transform.position.z - initialZ);
                maximumSpeed = Mathf.Max(maximumSpeed, body.linearVelocity.z);
                yield return null;
            }
            File.AppendAllText("Temp/beholder-bite-real-player-metrics.txt", $"walking={walking}; incoming={incomingSpeed}; outwardPeak={maximumSpeed}; displacement={maximumDisplacement}; healthLoss={initialHealth-health.CurrentHealth}\n");
            Assert.That(health.CurrentHealth, Is.EqualTo(initialHealth - 20f));
            Assert.That(maximumSpeed, Is.GreaterThan(2f), "Controller must preserve an outward push despite incoming movement");
            Assert.That(maximumDisplacement, Is.GreaterThan(0.2f), "Ground drag and input must not erase the light push");
            float resumeUntil = Time.time + 0.35f;
            while (Time.time < resumeUntil)
            {
                movement.Move(walking ? Vector2.up : Vector2.zero);
                yield return null;
            }
            Assert.That(movement.IsMovementControlLocked, Is.False);
            if (walking) Assert.That(body.linearVelocity.z, Is.LessThan(-0.5f), "Normal movement resumes after the brief push");
        }
        finally
        {
            context?.Dispose(); controller.Presentation.Dispose();
            Object.Destroy(stage); Object.Destroy(floor); Object.Destroy(attacker); Object.Destroy(settings);
        }
    }

    static void Isolate(GameObject player)
    {
        foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = behaviour == player.GetComponent<PlayerMovement>() || behaviour == player.GetComponent<PlayerHealth>();
        foreach (Animator animator in player.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        foreach (Camera camera in player.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (AudioListener listener in player.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        foreach (Collider collider in player.GetComponentsInChildren<Collider>(true)) collider.enabled = collider == player.GetComponent<CapsuleCollider>();
        foreach (Rigidbody child in player.GetComponentsInChildren<Rigidbody>(true))
            if (child != player.GetComponent<Rigidbody>()) { child.isKinematic = true; child.useGravity = false; child.detectCollisions = false; }
    }

    [UnityTearDown]
    public IEnumerator RestoreEditor() { if (Application.isPlaying) yield return new ExitPlayMode(); }
}
