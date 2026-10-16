using UnityEngine;

public partial class PlayerAudioController
{
    private const float DefaultLowHealthThreshold = 0.3f;
    private const float DefaultLowHealthPulseSpeed = 1.15f;
    private const float DefaultLowStaminaThreshold = 0.5f;
    private const float FirstHeartbeatPhase = 0.08f;
    private const float SecondHeartbeatPhase = 0.23f;
    private const float SecondHeartbeatVolume = 0.65f;
    private const float BreathingFadeSpeed = 2f;

    [SerializeField] private PlayerHealth playerHealth;

    private PlayerPostProcessingFeedbackController feedbackController;
    private PlayerHealth subscribedHealth;
    private AudioSource breathingSource;
    private AudioCue breathingCue;
    private float breathingBaseVolume;
    private int heartbeatCycle;
    private bool heartbeatActive;
    private bool firstHeartbeatPlayed;
    private bool secondHeartbeatPlayed;
    private bool warnedNonLoopingBreathing;
    private bool warnedLoopingHeartbeat;

    private void EnableFeedbackAudio()
    {
        if (playerHealth == null)
            playerHealth = GetComponent<PlayerHealth>();

        if (feedbackController == null)
            feedbackController = GetComponentInChildren<PlayerPostProcessingFeedbackController>(true);

        SubscribeToDamage();
    }

    private void DisableFeedbackAudio()
    {
        if (subscribedHealth != null)
            subscribedHealth.Damaged -= HandleDamaged;

        subscribedHealth = null;
        heartbeatActive = false;
        StopBreathing();
    }

    private void SubscribeToDamage()
    {
        if (!HasLocalFeedbackAuthority() || playerHealth == null || subscribedHealth == playerHealth)
            return;

        if (subscribedHealth != null)
            subscribedHealth.Damaged -= HandleDamaged;

        subscribedHealth = playerHealth;
        subscribedHealth.Damaged += HandleDamaged;
    }

    private void HandleDamaged(PlayerHealth health, DamageInfo damageInfo)
    {
        if (HasLocalFeedbackAuthority())
            PlayAttached(profile != null ? profile.Damaged : null, transform);
    }

    private bool HasLocalFeedbackAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private void UpdateFeedbackAudio(float deltaTime)
    {
        if (!HasLocalFeedbackAuthority() || profile == null)
        {
            heartbeatActive = false;
            StopBreathing();
            return;
        }

        if (subscribedHealth == null)
            SubscribeToDamage();

        UpdateBreathing(deltaTime);
        UpdateHeartbeat();
    }

    private void UpdateBreathing(float deltaTime)
    {
        AudioCue cue = profile.LowStaminaBreathing;
        if (cue == null || !cue.HasPlayableClip() || playerMovement == null ||
            playerMovement.MaxStamina <= 0.001f || (playerHealth != null && !playerHealth.IsAlive))
        {
            StopBreathing();
            return;
        }

        if (!cue.Loop)
        {
            if (!warnedNonLoopingBreathing)
            {
                Debug.LogWarning("Low Stamina Breathing AudioCue must have Loop enabled.", cue);
                warnedNonLoopingBreathing = true;
            }

            StopBreathing();
            return;
        }

        float threshold = feedbackController != null
            ? feedbackController.LowStaminaStartThreshold
            : DefaultLowStaminaThreshold;
        float severity = Mathf.Clamp01((threshold - playerMovement.StaminaNormalized) / Mathf.Max(0.001f, threshold));
        if (severity > 0f && (breathingSource == null || !breathingSource.isPlaying || breathingCue != cue))
            StartBreathing(cue);

        if (breathingSource == null || !breathingSource.isPlaying)
            return;

        float targetVolume = breathingBaseVolume * severity;
        breathingSource.volume = Mathf.MoveTowards(
            breathingSource.volume,
            targetVolume,
            breathingBaseVolume * BreathingFadeSpeed * deltaTime);

        if (severity <= 0f && breathingSource.volume <= 0.001f)
            StopBreathing();
    }

    private void StartBreathing(AudioCue cue)
    {
        if (breathingSource == null)
        {
            GameObject sourceObject = new GameObject("LowStaminaBreathingAudio");
            sourceObject.transform.SetParent(transform, false);
            breathingSource = sourceObject.AddComponent<AudioSource>();
            breathingSource.playOnAwake = false;
            breathingSource.dopplerLevel = 0f;
        }

        breathingSource.Stop();
        cue.ConfigureAudioSource(breathingSource, cue.GetRandomClip());
        breathingCue = cue;
        breathingBaseVolume = breathingSource.volume;
        breathingSource.volume = 0f;
        breathingSource.Play();
    }

    private void StopBreathing()
    {
        if (breathingSource != null)
            breathingSource.Stop();

        breathingCue = null;
    }

    private void UpdateHeartbeat()
    {
        AudioCue cue = profile.LowHealthHeartbeat;
        if (cue == null || !cue.HasPlayableClip() || playerHealth == null || !playerHealth.IsAlive ||
            playerHealth.MaxHealth <= 0.001f)
        {
            heartbeatActive = false;
            return;
        }

        if (cue.Loop)
        {
            if (!warnedLoopingHeartbeat)
            {
                Debug.LogWarning("Low Health Heartbeat AudioCue must have Loop disabled; use a single-beat clip.", cue);
                warnedLoopingHeartbeat = true;
            }

            heartbeatActive = false;
            return;
        }

        float threshold = feedbackController != null
            ? feedbackController.LowHealthThreshold
            : DefaultLowHealthThreshold;
        float health = playerHealth.CurrentHealth / playerHealth.MaxHealth;
        float severity = Mathf.Clamp01((threshold - health) / Mathf.Max(0.001f, threshold));
        if (severity <= 0.001f)
        {
            heartbeatActive = false;
            return;
        }

        float speed = feedbackController != null
            ? feedbackController.LowHealthPulseSpeed
            : DefaultLowHealthPulseSpeed;
        float cycleTime = Time.time * Mathf.Max(0.01f, speed);
        int currentCycle = Mathf.FloorToInt(cycleTime);
        float phase = cycleTime - currentCycle;

        if (!heartbeatActive)
        {
            heartbeatActive = true;
            heartbeatCycle = currentCycle;
            firstHeartbeatPlayed = phase >= FirstHeartbeatPhase;
            secondHeartbeatPlayed = phase >= SecondHeartbeatPhase;
        }
        else if (heartbeatCycle != currentCycle)
        {
            heartbeatCycle = currentCycle;
            firstHeartbeatPlayed = false;
            secondHeartbeatPlayed = false;
        }

        if (!firstHeartbeatPlayed && phase >= FirstHeartbeatPhase)
        {
            PlayHeartbeat(cue, severity);
            firstHeartbeatPlayed = true;
        }

        if (!secondHeartbeatPlayed && phase >= SecondHeartbeatPhase)
        {
            PlayHeartbeat(cue, severity * SecondHeartbeatVolume);
            secondHeartbeatPlayed = true;
        }
    }

    private void PlayHeartbeat(AudioCue cue, float strength)
    {
        if (audioEmitter == null)
            return;

        AudioSource source = audioEmitter.PlayAttached(cue, transform);
        if (source != null)
            source.volume *= strength;
    }
}
