using UnityEngine;

public partial class PlatformController
{
    public const string MovementAudioObjectName = "Platform Movement Audio";
    private AudioCue preparedMovementCue;
    private AudioSource preparedMovementSource;
    private AudioCue activeMovementCue;

    private void PrepareMovementAudio()
    {
        if (!Application.IsPlaying(gameObject)) return;
        if (movementAudioSource == null)
        {
            var anchor = transform.Find(MovementAudioObjectName);
            if (anchor == null)
            {
                anchor = new GameObject(MovementAudioObjectName).transform;
                anchor.SetParent(transform, false);
            }
            movementAudioSource = anchor.GetComponent<AudioSource>();
            if (movementAudioSource == null) movementAudioSource = anchor.gameObject.AddComponent<AudioSource>();
        }
        movementAudioSource.playOnAwake = false;
        movementAudioSource.dopplerLevel = 0f;
        if (preparedMovementCue == movementAudioCue && preparedMovementSource == movementAudioSource) return;
        if (movementAudioCue != null && movementAudioCue.Clips != null)
            foreach (var clip in movementAudioCue.Clips)
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        preparedMovementCue = movementAudioCue;
        preparedMovementSource = movementAudioSource;
    }

    private void SyncMovementAudio(double now)
    {
        if (!Application.IsPlaying(gameObject)) return;
        if (!IsMovingAtTime(now) || movementAudioCue == null || movementAudioSource == null)
        {
            StopMovementAudio();
            return;
        }
        if (movingPart != null) movementAudioSource.transform.position = movingPart.position;
        if (activeMovementCue == movementAudioCue && movementAudioSource.isPlaying) return;
        StopMovementAudio();
        var clip = movementAudioCue.GetRandomClip();
        if (clip == null) return;
        movementAudioCue.ConfigureAudioSource(movementAudioSource, clip, forceLoop: true);
        movementAudioSource.Play();
        activeMovementCue = movementAudioCue;
    }

    private void StopMovementAudio()
    {
        if (movementAudioSource != null && (activeMovementCue != null || movementAudioSource.clip != null))
        {
            movementAudioSource.Stop();
            movementAudioSource.clip = null;
        }
        activeMovementCue = null;
    }
}
