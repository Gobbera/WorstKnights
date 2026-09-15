using UnityEngine;

[CreateAssetMenu(fileName = "FootstepCharacterAudioProfile", menuName = "Audio/Footstep Character Profile")]
public class FootstepCharacterAudioProfile : ScriptableObject
{
    [Header("Fallback")]
    [SerializeField] private AudioCue defaultCue;

    [Header("Movement")]
    [SerializeField] private AudioCue walk;
    [SerializeField] private AudioCue sprint;
    [SerializeField] private AudioCue crouch;
    [SerializeField] private AudioCue enemyWalk;

    [Header("Actions")]
    [SerializeField] private AudioCue jump;
    [SerializeField] private AudioCue land;

    public AudioCue DefaultCue => defaultCue;
    public AudioCue Walk => walk;
    public AudioCue Sprint => sprint;
    public AudioCue Crouch => crouch;
    public AudioCue EnemyWalk => enemyWalk;
    public AudioCue Jump => jump;
    public AudioCue Land => land;

    public AudioCue GetCue(FootstepGait gait)
    {
        AudioCue gaitCue = ResolveGaitCue(gait);
        if (gaitCue != null)
            return gaitCue;

        return defaultCue != null ? defaultCue : walk;
    }

    private AudioCue ResolveGaitCue(FootstepGait gait)
    {
        switch (gait)
        {
            case FootstepGait.Sprint:
                return sprint != null ? sprint : walk;
            case FootstepGait.Crouch:
                return crouch != null ? crouch : walk;
            case FootstepGait.EnemyWalk:
                return enemyWalk != null ? enemyWalk : walk;
            case FootstepGait.Jump:
                return jump != null ? jump : walk;
            case FootstepGait.Land:
                return land != null ? land : walk;
            case FootstepGait.Walk:
            default:
                return walk;
        }
    }
}
