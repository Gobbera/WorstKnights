using UnityEngine;

public partial class PlatformController
{
    public const string StartAudioObjectName = "Platform Start Audio";
    public const string MovementAudioObjectName = "Platform Movement Audio";
    public const string StopAudioObjectName = "Platform Stop Audio";
    private AudioCue preparedStartCue, preparedMovementCue, preparedStopCue;
    private AudioCue activeMovementCue;
    private bool audioWasMoving, restoreMovementAudio;

    private void PrepareMovementAudio()
    {
        if (!Application.IsPlaying(gameObject)) return;
        PrepareAudioSource(ref movementAudioSource, MovementAudioObjectName, null, null);
        PrepareAudioSource(ref startAudioSource, StartAudioObjectName, movementAudioSource, null);
        PrepareAudioSource(ref stopAudioSource, StopAudioObjectName, movementAudioSource, startAudioSource);
        PrepareAudioCue(startAudioCue, ref preparedStartCue);
        PrepareAudioCue(movementAudioCue, ref preparedMovementCue);
        PrepareAudioCue(stopAudioCue, ref preparedStopCue);
    }

    private void PrepareAudioSource(ref AudioSource source, string objectName, AudioSource other, AudioSource another)
    {
        if (source == null || source == other || source == another)
        {
            var anchor = transform.Find(objectName);
            if (anchor == null)
            {
                anchor = new GameObject(objectName).transform;
                anchor.SetParent(transform, false);
            }
            source = anchor.GetComponent<AudioSource>();
            if (source == null || source == other || source == another) source = anchor.gameObject.AddComponent<AudioSource>();
        }
        source.playOnAwake = false;
        source.dopplerLevel = 0f;
    }

    private static void PrepareAudioCue(AudioCue cue, ref AudioCue prepared)
    {
        if (prepared == cue) return;
        if (cue != null && cue.Clips != null)
            foreach (var clip in cue.Clips)
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        prepared = cue;
    }

    private void SyncMovementAudio(double now, bool suppressOneShots = false)
    {
        if (!Application.IsPlaying(gameObject)) return;
        if (movingPart != null)
        {
            FollowAudioSource(startAudioSource);
            FollowAudioSource(movementAudioSource);
            FollowAudioSource(stopAudioSource);
        }
        suppressOneShots |= restoreMovementAudio;
        restoreMovementAudio = false;
        if (isBroken) { StopMovementAudio(); return; }
        bool moving = IsMovingAtTime(now);
        if (moving && !audioWasMoving && !suppressOneShots)
        {
            StopAudioSource(stopAudioSource);
            PlayStageAudio(startAudioCue, startAudioSource);
        }
        else if (!moving && audioWasMoving)
        {
            StopAudioSource(startAudioSource);
            StopMovementLoop();
            if (!suppressOneShots && HasReachedAudioEndpoint(now)) PlayStageAudio(stopAudioCue, stopAudioSource);
        }
        audioWasMoving = moving;
        if (moving) SyncMovementLoop();
        else StopMovementLoop();
    }

    private bool HasReachedAudioEndpoint(double now)
    {
        if (isBroken || !HasValidMovementPath()) return false;
        double elapsed = GetCurrentMovementElapsedTime(now);
        if (motionMode == PlatformMotionMode.TwoPosition)
            return movingTowardEnd ? elapsed >= totalForwardDuration - TimeEpsilon : elapsed <= TimeEpsilon;
        return motionMode == PlatformMotionMode.OneWay && elapsed >= totalForwardDuration - TimeEpsilon;
    }

    private void FollowAudioSource(AudioSource source)
    {
        if (source != null && source.transform != movingPart && !movingPart.IsChildOf(source.transform))
            source.transform.position = movingPart.position;
    }

    private void SyncMovementLoop()
    {
        if (movementAudioCue == null || movementAudioSource == null) { StopMovementLoop(); return; }
        if (activeMovementCue == movementAudioCue && movementAudioSource.isPlaying) return;
        StopMovementLoop();
        var clip = movementAudioCue.GetRandomClip();
        if (clip == null) return;
        movementAudioCue.ConfigureAudioSource(movementAudioSource, clip, forceLoop: true);
        movementAudioSource.Play();
        activeMovementCue = movementAudioCue;
    }

    private static void PlayStageAudio(AudioCue cue, AudioSource source)
    {
        if (cue == null || source == null) return;
        var clip = cue.GetRandomClip();
        if (clip == null) return;
        cue.ConfigureAudioSource(source, clip);
        source.loop = false;
        source.Play();
    }

    private void StopMovementLoop()
    {
        StopAudioSource(movementAudioSource);
        activeMovementCue = null;
    }

    private static void StopAudioSource(AudioSource source)
    {
        if (source == null || source.clip == null) return;
        source.Stop();
        source.clip = null;
    }

    private void StopMovementAudio()
    {
        StopAudioSource(startAudioSource);
        StopMovementLoop();
        StopAudioSource(stopAudioSource);
        audioWasMoving = false;
    }
}
