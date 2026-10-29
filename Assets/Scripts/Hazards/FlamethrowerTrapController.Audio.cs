using UnityEngine;

public sealed partial class FlamethrowerTrapController
{
    private AudioSource flameAudioSource;
    private AudioCue activeFlameAudioCue;
    private AudioCue missingClipWarningCue;

    private void SyncFlameAudio()
    {
        if (!Application.IsPlaying(gameObject) || !isActiveAndEnabled || flameAudioCue == null)
        {
            StopFlameAudio();
            return;
        }

        if (flameAudioSource != null)
        {
            flameAudioSource.transform.position = JetOrigin;
            // A non-looping cue plays once per emission, even after its clip finishes.
            if (activeFlameAudioCue == flameAudioCue) return;
        }

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

        if (flameAudioSource == null)
        {
            var audioObject = new GameObject("Flamethrower Audio");
            audioObject.transform.SetParent(transform, false);
            flameAudioSource = audioObject.AddComponent<AudioSource>();
            flameAudioSource.playOnAwake = false;
            flameAudioSource.dopplerLevel = 0f;
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
