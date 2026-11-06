using UnityEngine;

public sealed partial class FlamethrowerTrapController
{
    public const string FlameAudioObjectName = "Flamethrower Audio";
    private AudioSource preparedFlameAudioSource;
    private AudioCue preparedFlameAudioCue;
    private AudioCue activeFlameAudioCue;
    private AudioCue missingClipWarningCue;

    private void PrepareFlameAudio()
    {
        if (!Application.IsPlaying(gameObject)) return;

        if (flameAudioSource == null)
        {
            // Compatibility for older dispensers: create their dedicated source at startup.
            Transform existing = transform.Find(FlameAudioObjectName);
            if (existing == null)
            {
                existing = new GameObject(FlameAudioObjectName).transform;
                existing.SetParent(transform, false);
            }
            flameAudioSource = existing.GetComponent<AudioSource>();
            if (flameAudioSource == null) flameAudioSource = existing.gameObject.AddComponent<AudioSource>();
        }

        if (preparedFlameAudioSource == flameAudioSource && preparedFlameAudioCue == flameAudioCue) return;
        flameAudioSource.playOnAwake = false;
        flameAudioSource.dopplerLevel = 0f;
        if (flameAudioCue != null && flameAudioCue.Clips != null)
            foreach (AudioClip clip in flameAudioCue.Clips)
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        preparedFlameAudioSource = flameAudioSource;
        preparedFlameAudioCue = flameAudioCue;
    }

    private void SyncFlameAudio()
    {
        if (!Application.IsPlaying(gameObject) || !isActiveAndEnabled || flameAudioCue == null || flameAudioSource == null)
        {
            StopFlameAudio();
            return;
        }

        flameAudioSource.transform.position = JetOrigin;
        // A non-looping cue plays once per emission, even after its clip finishes.
        if (activeFlameAudioCue == flameAudioCue) return;

        StopFlameAudio();
        AudioClip clip = flameAudioCue.GetRandomClip();
        if (clip == null)
        {
            if (missingClipWarningCue != flameAudioCue)
            {
                missingClipWarningCue = flameAudioCue;
                Debug.LogWarning("[FlamethrowerTrap] Flame Audio Cue esta sem AudioClip valido.", this);
            }
            return;
        }

        flameAudioSource.transform.position = JetOrigin;
        flameAudioCue.ConfigureAudioSource(flameAudioSource, clip);
        flameAudioSource.Play();
        activeFlameAudioCue = flameAudioCue;
        missingClipWarningCue = null;
    }

    private void StopFlameAudio()
    {
        if (flameAudioSource != null && (activeFlameAudioCue != null || flameAudioSource.clip != null))
        {
            flameAudioSource.Stop();
            flameAudioSource.clip = null;
        }
        activeFlameAudioCue = null;
    }
}
