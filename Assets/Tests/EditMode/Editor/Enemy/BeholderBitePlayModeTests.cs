using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class BeholderBitePlayModeTests
{
    [UnityTest]
    public IEnumerator Bite_UsesRealAnimationWithoutVfxAndWaitsForAnInRangeTarget()
    {
        yield return new EnterPlayMode();
        var root = new GameObject("Live bite validation"); root.SetActive(false);
        var vision = root.AddComponent<EnemyVision>();
        var attack = root.AddComponent<BeholderAttackController>();
        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beholder/Beholder.prefab"), root.transform);
        Isolate(model);
        var animator = model.GetComponent<EnemySetup>().Animator;
        animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var relay in model.GetComponentsInChildren<BeholderAttackAnimationEvents>())
        {
            var data = new SerializedObject(relay);
            data.FindProperty("attackController").objectReferenceValue = attack; data.ApplyModifiedPropertiesWithoutUndo();
        }
        var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BeholderBiteAttack>("Assets/WorstKnights/Models/Enemies/Beholder/Attacks/Attack_Bite.asset"));
        settings.attackAudio = null; settings.dialogues = Array.Empty<AudioCue>();
        settings.damage = 20f; settings.hitTime = 1f;
        typeof(BeholderAttackController).GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(attack, new[] { new BeholderAttackController.AttackDefinition { settings = settings } });
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var targetBody = target.AddComponent<Rigidbody>(); targetBody.useGravity = false; targetBody.freezeRotation = true;
        var player = target.AddComponent<PlayerHealth>();
        var health = new SerializedObject(player);
        health.FindProperty("damageImmunityDuration").floatValue = 0f;
        health.FindProperty("damageKnockback").FindPropertyRelative("enabled").boolValue = false;
        health.ApplyModifiedPropertiesWithoutUndo();
        try
        {
            root.SetActive(true); Isolate(model); model.transform.localPosition = Vector3.zero;
            animator.enabled = true;
            yield return null;
            // This standalone test player has no network owner; isolate its local physics explicitly.
            targetBody.isKinematic = false;
            targetBody.useGravity = false;
            target.transform.position = vision.EyePosition + vision.EyeForward * 10f;
            Physics.SyncTransforms();
            Assert.That(vision.CanSeeTarget(player, out _), Is.True);
            Assert.That(attack.TryStartAttack(player), Is.False, "Visible but out-of-range target cannot start a bite");
            Transform chin = Array.Find(model.GetComponentsInChildren<Transform>(), bone => bone.name == "Chin");
            Transform jaw = Array.Find(model.GetComponentsInChildren<Transform>(), bone => bone.name == "Jaw.L");
            target.transform.position = chin.position + vision.EyeForward * 1.5f;
            Physics.SyncTransforms();
            Assert.That(attack.TryStartAttack(player), Is.True);
            float initialHealth = player.CurrentHealth;
            float beganAt = Time.time, deadline = Time.time + 4f;
            Vector3 jawAtStart = jaw.localPosition;
            float maximumJawTravel = 0f;
            float maximumPushSpeed = 0f;
            yield return null; yield return null;
            Assert.That(attack.Phase, Is.EqualTo(BeholderAttackPhase.Execution));
            Assert.That(attack.GlobalCooldownRemaining, Is.Zero);
            Assert.That(player.CurrentHealth, Is.EqualTo(initialHealth));
            Assert.That(attack.OwnsBone(jaw), Is.True);
            Transform tentacle = Array.Find(model.GetComponentsInChildren<Transform>(), bone => bone.name == "Root.Tentacule_03.R");
            Assert.That(attack.OwnsBone(tentacle), Is.False);
            while (attack.IsAttacking && Time.time < deadline)
            {
                maximumJawTravel = Mathf.Max(maximumJawTravel, jaw.parent.TransformVector(jaw.localPosition - jawAtStart).magnitude);
                maximumPushSpeed = Mathf.Max(maximumPushSpeed, Vector3.ProjectOnPlane(targetBody.linearVelocity, Vector3.up).magnitude);
                yield return null;
            }
            Assert.That(attack.IsAttacking, Is.False);
            Assert.That(player.CurrentHealth, Is.EqualTo(initialHealth - 20f), "Real animation applies exactly one twenty-damage bite");
            Assert.That(maximumPushSpeed, Is.EqualTo(settings.knockbackStrength).Within(0.15f), "Accepted bite adds one light horizontal push");
            Assert.That(Time.time - beganAt, Is.GreaterThanOrEqualTo(settings.animation.length - 0.05f));
            Assert.That(maximumJawTravel, Is.GreaterThan(0.01f), "The real bite animates jaw translation on the masked layer");
            Assert.That(attack.GlobalCooldownRemaining, Is.GreaterThan(7f));
            Assert.That(attack.Presentation.Beam, Is.Null); Assert.That(attack.Presentation.Strike, Is.Null);
            Assert.That(animator.speed, Is.EqualTo(1f));
            Assert.That(root.transform.position, Is.EqualTo(Vector3.zero));
        }
        finally { Object.Destroy(root); Object.Destroy(target); Object.Destroy(settings); }
        yield return new ExitPlayMode();
    }

    static void Isolate(GameObject model)
    {
        foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(behaviour is BeholderAttackAnimationEvents)) behaviour.enabled = false;
        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
        foreach (UnityEngine.AI.NavMeshAgent agent in model.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled = false;
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator RestoreEditor() { if (Application.isPlaying) yield return new ExitPlayMode(); }
}
