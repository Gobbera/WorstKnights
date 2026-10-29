using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.VFX;

public sealed class LightningBeamPlayModeTests
{
    [UnityTest]
    public IEnumerator BothPresets_RemainVisibleForSixSeconds_TrackAndStopCleanly()
    {
        yield return new EnterPlayMode();
        var stage = new GameObject("Beam runtime validation");
        stage.SetActive(false);
        var blue = CreateBeam(stage, "Blue", -2f);
        var purple = CreateBeam(stage, "Purple", 2f);
        var cameraObject = new GameObject("Beam validation camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(new Vector3(0f, 10f, 6f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
        camera.orthographic = true;
        camera.orthographicSize = 8f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.006f, 0.009f, 0.016f);
        camera.allowHDR = true;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var texture = new RenderTexture(1024, 768, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = texture;
        var volumeObject = new GameObject("Beam validation volume");
        var volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
        Bloom bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.3f);
        bloom.threshold.Override(1f);
        volume.sharedProfile = profile;
        try
        {
            stage.SetActive(true);
            Physics.SyncTransforms();
            float started = Time.time;
            while (Time.time < started + 1f)
            {
                camera.Render();
                yield return null;
            }
            VisualEffect blueEffect = blue.GetComponent<VisualEffect>();
            while (Time.time < started + 6f)
            {
                camera.Render();
                yield return null;
            }
            Assert.That(blue.IsPlaying && purple.IsPlaying, Is.True);
            int visible = Capture(camera, texture, "Temp/lightning-beam-presets.png");
            Assert.That(visible, Is.GreaterThan(200), "Persistent beams must remain rendered after six seconds");

            var blueSettings = new SerializedObject(blue);
            var source = (Transform)blueSettings.FindProperty("origin").objectReferenceValue;
            var target = (Transform)blueSettings.FindProperty("aimTarget").objectReferenceValue;
            source.position += Vector3.right;
            target.position += Vector3.left + Vector3.back * 4f;
            Physics.SyncTransforms();
            blue.RefreshBeam();
            Assert.That(blue.StartPoint, Is.EqualTo(source.position));
            Assert.That(blue.LastHit.collider, Is.EqualTo(target.GetComponent<Collider>()));
            yield return null;
            Capture(camera, texture, "Temp/lightning-beam-moved.png");
            target.GetComponent<Collider>().enabled = false;
            Physics.SyncTransforms();
            blue.RefreshBeam();
            Assert.That(blue.HasHit, Is.False);
            Assert.That(blueEffect.GetFloat("Impact Glow Size"), Is.Zero);
            target.GetComponent<Collider>().enabled = true;
            Physics.SyncTransforms();
            blue.RefreshBeam();
            Assert.That(blueEffect.GetFloat("Impact Glow Size"), Is.GreaterThan(0f));

            blue.StopBeam();
            purple.StopBeam();
            camera.Render();
            yield return null;
            camera.Render();
            yield return null;
            Assert.That(Capture(camera, texture, "Temp/lightning-beam-stopped.png"), Is.LessThan(20));
        }
        finally
        {
            camera.targetTexture = null;
            texture.Release();
            Object.Destroy(texture);
            Object.Destroy(stage);
            Object.Destroy(cameraObject);
            Object.Destroy(volumeObject);
            Object.Destroy(profile);
        }
        yield return new ExitPlayMode();
    }

    [UnityTearDown]
    public IEnumerator RestoreEditor()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    private static LightningBeamController CreateBeam(GameObject stage, string preset, float x)
    {
        var source = new GameObject(preset + " origin").transform;
        source.SetParent(stage.transform);
        source.position = new Vector3(x, 0f, 0f);
        var target = new GameObject(preset + " collider").transform;
        target.SetParent(stage.transform);
        target.position = new Vector3(x, 0f, 12f);
        target.gameObject.AddComponent<BoxCollider>().size = Vector3.one * 0.5f;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFX/Lightning Strike/Variations/Lightning Beam " + preset + ".prefab");
        var instance = Object.Instantiate(prefab, stage.transform);
        var beam = instance.GetComponent<LightningBeamController>();
        var settings = new SerializedObject(beam);
        settings.FindProperty("origin").objectReferenceValue = source;
        settings.FindProperty("aimTarget").objectReferenceValue = target;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return beam;
    }

    private static int Capture(Camera camera, RenderTexture target, string path)
    {
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            int visible = 0, left = 0, right = 0, blue = 0, purple = 0;
            Color32[] pixels = image.GetPixels32();
            // Inspect the middle half, excluding both endpoint glows: the bodies must render.
            for (int y = image.height / 4; y < image.height * 3 / 4; y++)
            for (int x = 0; x < image.width; x++)
            {
                Color32 pixel = pixels[y * image.width + x];
                if (pixel.r + pixel.g + pixel.b <= 100) continue;
                visible++;
                if (x < image.width / 2)
                {
                    left++;
                    if (pixel.b > pixel.r + 10) blue++;
                }
                else
                {
                    right++;
                    if (pixel.r > pixel.g + 10 && pixel.b > pixel.g + 10) purple++;
                }
            }
            File.WriteAllBytes(path, image.EncodeToPNG());
            if (path.EndsWith("presets.png"))
            {
                Assert.That(left, Is.GreaterThan(100), "Blue beam body is visible");
                Assert.That(right, Is.GreaterThan(left * 1.5f), "Purple beam is visibly thicker");
                Assert.That(blue, Is.GreaterThan(100), "Blue tint survives rendering");
                Assert.That(purple, Is.GreaterThan(100), "Purple tint survives rendering");
            }
            return visible;
        }
        finally
        {
            RenderTexture.active = previous;
            Object.Destroy(image);
        }
    }
}
