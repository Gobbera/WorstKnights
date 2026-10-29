using System;
using UnityEngine;

public abstract class BeholderAttackDefinition : ScriptableObject
{
    [Header("Seleção e movimento")]
    [Tooltip("Maior valor vence. Empates seguem a ordem da lista.")]
    public int priority;
    public bool canMove;
    [Min(0f)] public float movementSpeed = 4.1f;
    [Min(0f)] public float cooldown = 10f;

    [Header("Animação e origem")]
    public AnimationClip animation;
    public AvatarMask mask;
    public string animatorState = "Attack";
    public string originName = "Eye_03.L.Mesh";
    public string tentacleRootName = "Root.Tentacule_03.L";
    [Tooltip("Usado quando o clip não possui um evento OnBeholderAttackEffect para esta máscara.")]
    [BeholderAttackController.AudioTime, Min(0f)] public float activationTime = 2.75f;
    [Tooltip("Tempo mínimo de recuperação após a execução. A animação restante também precisa terminar.")]
    [Min(0f)] public float recoveryDuration;
    [Min(0f), InspectorName("Aim Lock Lead Time (s)")]
    public float aimLockLeadTime = 0.5f;

    [Header("Áudio e diálogos")]
    public AudioCue attackAudio;
    [BeholderAttackController.AudioTime] public float attackAudioTime = 2.75f;
    public AudioCue[] dialogues = Array.Empty<AudioCue>();
    [BeholderAttackController.AudioTime] public float dialogueTime;

    public string LayerName => mask != null ? mask.name.Replace('.', '_') : string.Empty;
    public int StateHash => Animator.StringToHash(LayerName + "." + animatorState);
    public string SpeedParameter => LayerName + "_AttackSpeed";
    public virtual bool HoldAnimation => false;
    public virtual AudioCue SustainedAudio => null;
    public virtual bool StreamsPresentation => false;
    public abstract bool HasPresentation { get; }
    public abstract BeholderAttackExecution CreateExecution(BeholderAttackContext context);
    public abstract void PresentSegment(BeholderAttackPresentation presentation, Vector3 start, Vector3 end, bool hit);

    public float ResolveActivationTime()
    {
        if (animation == null) return 0f;
        float result = Mathf.Clamp(activationTime, 0f, animation.length);
        bool found = false;
        foreach (AnimationEvent signal in animation.events)
        {
            if (signal.functionName != nameof(BeholderAttackAnimationEvents.OnBeholderAttackEffect)
                || mask == null || signal.stringParameter != mask.name) continue;
            result = found ? Mathf.Min(result, signal.time) : signal.time;
            found = true;
        }
        return Mathf.Clamp(result, 0f, animation.length);
    }
}
