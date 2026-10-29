using System;
using UnityEngine;

public sealed class BeholderAttackPresentation : IDisposable
{
    private readonly Transform owner;
    private LightningStrikeController strikePrefab;
    private LightningBeamController beamPrefab;
    private float strikeHideAt;
    private AudioSource loopSource;
    public LightningStrikeController Strike { get; private set; }
    public LightningBeamController Beam { get; private set; }
    public int ShotSequence { get; private set; }

    public BeholderAttackPresentation(Transform owner) { this.owner = owner; }
    public void ShowStrike(BeholderLightningStrikeAttack settings, Vector3 start, Vector3 end, bool hit)
    {
        if (Strike == null || strikePrefab != settings.vfxPrefab)
        {
            if (Strike != null) Release(Strike.gameObject);
            Strike = Create(settings.vfxPrefab);
            strikePrefab = settings.vfxPrefab;
            Strike.SetPlayOnEnable(false);
        }
        Strike.gameObject.SetActive(true);
        Strike.PlayBetween(start, end, hit);
        strikeHideAt = Time.time + Mathf.Max(0.1f, settings.visualLifetime);
        ShotSequence++;
    }
    public void ShowBeam(BeholderLightningBeamAttack settings, Vector3 start, Vector3 end, bool hit)
    {
        if (Beam == null || beamPrefab != settings.vfxPrefab)
        {
            if (Beam != null) Release(Beam.gameObject);
            Beam = Create(settings.vfxPrefab);
            beamPrefab = settings.vfxPrefab;
            Beam.SetPlayOnEnable(false);
        }
        Beam.gameObject.SetActive(true);
        Beam.PlayBetween(start, end, hit);
        if (loopSource != null) loopSource.transform.position = start;
    }
    public void StopBeam()
    {
        if (Beam != null) { Beam.StopBeam(); Beam.gameObject.SetActive(false); }
        StopLoop();
    }
    public void PlayLoop(AudioCue cue, int clipIndex, float volume, float pitch, Vector3 position)
    {
        if (cue == null || clipIndex < 0 || clipIndex >= cue.Clips.Length || cue.Clips[clipIndex] == null) return;
        if (loopSource == null)
        {
            var root = new GameObject("Beholder Attack Loop Audio");
            root.transform.SetParent(owner, false);
            loopSource = root.AddComponent<AudioSource>();
            loopSource.playOnAwake = false;
        }
        loopSource.transform.position = position;
        cue.ConfigureAudioSource(loopSource, cue.Clips[clipIndex], volume, pitch);
        loopSource.loop = true;
        loopSource.Play();
    }
    public void StopLoop() { if (loopSource != null) loopSource.Stop(); }
    public void Tick(float now)
    {
        if (Strike != null && Strike.gameObject.activeSelf && now >= strikeHideAt) Strike.gameObject.SetActive(false);
    }
    public void Clear()
    {
        StopBeam();
        if (Strike != null) Strike.gameObject.SetActive(false);
    }
    public void Dispose()
    {
        Clear();
        if (Strike != null) Release(Strike.gameObject);
        if (Beam != null) Release(Beam.gameObject);
        if (loopSource != null) Release(loopSource.gameObject);
    }
    private static T Create<T>(T prefab) where T : Component
    {
        var staging = new GameObject("Beholder VFX staging");
        staging.SetActive(false);
        T instance = UnityEngine.Object.Instantiate(prefab, staging.transform);
        instance.gameObject.SetActive(false);
        instance.transform.SetParent(null, false);
        instance.transform.localScale = Vector3.one;
        Release(staging);
        return instance;
    }
    private static void Release(UnityEngine.Object target)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(target);
        else UnityEngine.Object.DestroyImmediate(target);
    }
}
