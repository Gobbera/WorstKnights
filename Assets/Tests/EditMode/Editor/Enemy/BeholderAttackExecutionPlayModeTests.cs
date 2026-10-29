using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class BeholderAttackExecutionPlayModeTests
{
    [UnityTest]
    public IEnumerator ContinuousAttack_HoldsOnlyItsLayer_FinishesAndCleansUpOnDisable()
    {
        yield return new EnterPlayMode();
        var root = new GameObject("Live beam attack validation");
        root.SetActive(false);
        var vision = root.AddComponent<EnemyVision>();
        var attack = root.AddComponent<BeholderAttackController>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beholder/Beholder.prefab");
        var model = Object.Instantiate(prefab, root.transform);
        var animator = model.GetComponent<EnemySetup>().Animator;
        // Keep the real model hierarchy, but isolate navigation and physics in the empty test scene.
        foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(behaviour is BeholderAttackAnimationEvents)) behaviour.enabled = false;
        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
        foreach (UnityEngine.AI.NavMeshAgent agent in model.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled = false;
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        animator.enabled = true;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        foreach (var relay in model.GetComponentsInChildren<BeholderAttackAnimationEvents>())
        {
            var data = new SerializedObject(relay);
            var receiver = data.FindProperty("attackController");
            Assert.That(receiver, Is.Not.Null, "Animation relay binding");
            receiver.objectReferenceValue = attack;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        var source = AssetDatabase.LoadAssetAtPath<BeholderLightningBeamAttack>("Assets/WorstKnights/Models/Enemies/Beholder/Attacks/Attack_Eye_03.R.asset");
        Assert.That(source != null, Is.True, "Continuous attack asset");
        var settings = Object.Instantiate(source);
        settings.attackAudio = null; settings.dialogues = Array.Empty<AudioCue>();
        settings.duration = 3f; settings.damagePerTick = 0f;
        settings.aimMode = BeholderBeamAimMode.FollowVisibleTarget; settings.trackingSpeed = 20f;
        var clip = AudioClip.Create("Beam loop validation", 44100, 1, 44100, false);
        var cue = ScriptableObject.CreateInstance<AudioCue>();
        var audio = new SerializedObject(cue);
        audio.FindProperty("clips").arraySize = 1;
        audio.FindProperty("clips").GetArrayElementAtIndex(0).objectReferenceValue = clip;
        audio.FindProperty("baseVolume").floatValue = 0f;
        audio.ApplyModifiedPropertiesWithoutUndo();
        settings.loopAudio = cue;
        typeof(BeholderAttackController).GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(attack, new[] { new BeholderAttackController.AttackDefinition { settings = settings } });
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        target.GetComponent<Renderer>().enabled = false;
        var player = target.AddComponent<PlayerHealth>();
        var cameraObject = new GameObject("Beam validation camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(new Vector3(0f, 15f, 4f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
        camera.orthographic = true; camera.orthographicSize = 8f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        var texture = new RenderTexture(1024, 768, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = texture;
        try
        {
            root.SetActive(true);
            // EnemySetup.Awake configures simulation even when its component is disabled.
            // Reapply isolation after all Awake callbacks and before the first physics step.
            foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is BeholderAttackAnimationEvents)) behaviour.enabled = false;
            foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
            foreach (UnityEngine.AI.NavMeshAgent agent in model.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled = false;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            model.transform.localPosition = Vector3.zero;
            animator.enabled = true;
            target.transform.position = vision.EyePosition + vision.EyeForward * 6f;
            Physics.SyncTransforms();
            yield return null;
            Assert.That(attack.TryStartAttack(player), Is.True);
            Assert.That(attack.GlobalCooldownRemaining, Is.Zero);
            float deadline = Time.time + 5f;
            while (attack.Phase != BeholderAttackPhase.Execution && Time.time < deadline) yield return null;
            Assert.That(attack.Phase, Is.EqualTo(BeholderAttackPhase.Execution));
            int layer = animator.GetLayerIndex(settings.LayerName);
            float held = animator.GetCurrentAnimatorStateInfo(layer).normalizedTime;
            float baseTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            float activeAt = Time.time;
            // Avoid capturing settings: EnterPlayMode reloads cannot restore compiler-generated closures.
            Transform aimingBone = Array.Find(model.GetComponentsInChildren<Transform>(), t => t.name == "Bone.005.Tentacule_03.R");
            Assert.That(aimingBone, Is.Not.Null);
            Transform eyeEnd = Array.Find(aimingBone.GetComponentsInChildren<Transform>(), t => t.name.EndsWith("_end"));
            Assert.That(eyeEnd, Is.Not.Null);
            while (Time.time < activeAt + 1.5f) { camera.Render(); yield return null; }
            Assert.That(attack.IsAttacking && attack.Presentation.Beam.IsPlaying, Is.True);
            Assert.That(attack.GlobalCooldownRemaining, Is.Zero, "Sustained execution has not started cooldown");
            Assert.That(animator.GetCurrentAnimatorStateInfo(layer).normalizedTime, Is.EqualTo(held).Within(0.02f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(baseTime));
            var loop = Array.Find(root.GetComponentsInChildren<AudioSource>(), s => s.gameObject.name == "Beholder Attack Loop Audio");
            Assert.That(loop != null && loop.isPlaying, Is.True);
            Vector3 before = (attack.LastSegment.End - attack.LastSegment.Start).normalized;
            target.transform.position += Vector3.right * 2f;
            Physics.SyncTransforms();
            float movedAt = Time.time;
            yield return null; yield return null;
            Vector3 direction = (attack.LastSegment.End - attack.LastSegment.Start).normalized;
            Assert.That(Vector3.Angle(before, direction), Is.LessThanOrEqualTo(settings.trackingSpeed * (Time.time - movedAt) + 1f));
            Assert.That(attack.LastSegment.Hit.collider, Is.Not.EqualTo(target.GetComponent<Collider>()), "Fast movement escapes the beam before it catches up");
            Assert.That(Vector3.Angle(eyeEnd.position - eyeEnd.parent.position, direction), Is.LessThan(0.1f), "Real rig eye and beam point together");
            while (attack.Phase == BeholderAttackPhase.Execution && attack.LastSegment.Hit.collider != target.GetComponent<Collider>()
                && Time.time < movedAt + 1.4f) yield return null;
            Assert.That(attack.LastSegment.Hit.collider, Is.EqualTo(target.GetComponent<Collider>()));
            Assert.That(Capture(camera, texture, "Temp/beholder-continuous-attack.png"), Is.GreaterThan(100));
            while (attack.Phase == BeholderAttackPhase.Execution && Time.time < deadline + 4f) yield return null;
            Assert.That(attack.Phase, Is.EqualTo(BeholderAttackPhase.Recovery));
            Assert.That(attack.GlobalCooldownRemaining, Is.EqualTo(8f).Within(0.25f), "Full global cooldown begins after the beam stops");
            while (attack.IsAttacking && Time.time < deadline + 4f) yield return null;
            Assert.That(attack.IsAttacking, Is.False);
            Assert.That(attack.Presentation.Beam.IsPlaying || loop.isPlaying, Is.False);
            Assert.That(animator.GetFloat(settings.SpeedParameter), Is.EqualTo(1f));
            Assert.That(attack.GlobalCooldownRemaining, Is.GreaterThan(6f));
            Assert.That(attack.CanStartAttack, Is.False);
            deadline = Time.time + Mathf.Max(settings.cooldown, attack.GlobalCooldownRemaining) + 2f;
            while (!attack.CanStartAttack && Time.time < deadline) yield return null;
            Assert.That(attack.CanStartAttack, Is.True, "Attack becomes available after the cooldown from execution end");
            Assert.That(vision.CanSeeTarget(player, out _), Is.True, "Isolated target remains visible before the second attack");
            Assert.That(attack.TryStartAttack(player), Is.True);
            deadline = Time.time + 5f;
            while (attack.Phase != BeholderAttackPhase.Execution && Time.time < deadline) yield return null;
            Assert.That(attack.Phase, Is.EqualTo(BeholderAttackPhase.Execution));
            attack.enabled = false;
            yield return null;
            Assert.That(attack.IsAttacking, Is.False);
            Assert.That(attack.GlobalCooldownRemaining, Is.GreaterThan(7.5f), "Interruption starts cooldown at that moment");
            Assert.That(attack.Presentation.Beam.IsPlaying || loop.isPlaying, Is.False);
            Assert.That(Capture(camera, texture, "Temp/beholder-continuous-stopped.png"), Is.LessThan(20));
        }
        finally
        {
            camera.targetTexture = null; texture.Release();
            Object.Destroy(texture); Object.Destroy(cameraObject); Object.Destroy(root); Object.Destroy(target);
            Object.Destroy(settings); Object.Destroy(cue); Object.Destroy(clip);
        }
        yield return new ExitPlayMode();
    }
    [UnityTearDown]
    public IEnumerator RestoreEditor() { if (Application.isPlaying) yield return new ExitPlayMode(); }

    private static int Capture(Camera camera, RenderTexture texture, string path)
    {
        camera.Render();
        RenderTexture previous = RenderTexture.active; RenderTexture.active = texture;
        var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        try
        {
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
            int count = 0; foreach (Color32 pixel in image.GetPixels32()) if (pixel.r + pixel.g + pixel.b > 100) count++;
            File.WriteAllBytes(path, image.EncodeToPNG()); return count;
        }
        finally { RenderTexture.active = previous; Object.Destroy(image); }
    }
}
