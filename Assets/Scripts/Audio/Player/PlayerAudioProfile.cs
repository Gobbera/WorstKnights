using UnityEngine;

public partial class PlayerAudioProfile
{
    [Header("Player Actions")]
    [SerializeField] private AudioCue crouchDown;
    [SerializeField] private AudioCue standUp;
    [SerializeField] private AudioCue kick;
    [SerializeField] private AudioCue runStart;
    [SerializeField] private AudioCue damaged;

    [Header("Local Player Feedback")]
    [SerializeField] private AudioCue lowStaminaBreathing;
    [SerializeField] private AudioCue lowHealthHeartbeat;

    public AudioCue CrouchDown => crouchDown;
    public AudioCue StandUp => standUp;
    public AudioCue Kick => kick;
    public AudioCue RunStart => runStart;
    public AudioCue Damaged => damaged;
    public AudioCue LowStaminaBreathing => lowStaminaBreathing;
    public AudioCue LowHealthHeartbeat => lowHealthHeartbeat;
}
