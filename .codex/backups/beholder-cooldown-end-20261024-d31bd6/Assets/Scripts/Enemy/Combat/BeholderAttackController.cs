using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public enum BeholderAttackPhase { None, Preparation, Execution, Recovery }

[DisallowMultipleComponent]
[DefaultExecutionOrder(110)]
[AddComponentMenu("Enemy/Beholder Attack Controller")]
public class BeholderAttackController : MonoBehaviour
{
    public sealed class AudioTimeAttribute : PropertyAttribute { }

    [Serializable]
    public sealed class AttackDefinition
    {
        public bool enabled = true;
        public BeholderAttackDefinition settings;
        public int priority => settings != null ? settings.priority : int.MinValue;
    }

    [Header("Ataques")]
    [SerializeField, Min(0f)] private float globalCooldown = 8f;
    [SerializeField] private AttackDefinition[] attacks = Array.Empty<AttackDefinition>();
    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyVision enemyVision;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private Animator animator;
    [SerializeField] private BeholderDialogueController dialogue;

    private readonly BeholderAttackScheduler scheduler = new BeholderAttackScheduler();
    private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
    private readonly HashSet<int> speedParameters = new HashSet<int>();
    private RuntimeAnimatorController cachedAnimatorController;
    private PhotonView photonView;
    private AudioSource attackAudioSource;
    private PlayerHealth attackTarget;
    private BeholderAttackDefinition playingAttack;
    private BeholderAttackContext context;
    private BeholderAttackExecution execution;
    private BeholderAttackPresentation presentation;
    private BeholderAttackSegment lastSegment;
    private Transform tentacleRoot;
    private Vector3 aimPosition;
    private int playingLayer = -1;
    private int attackSequence;
    private float startedAt, effectTime, recoveryElapsed, nextBeamReplicationAt;
    private bool aimLocked, effectQueued, effectFired, attackAudioPlayed, dialoguePlayed;

    public bool IsAttacking => scheduler.ActiveIndex >= 0;
    public int ActiveAttackIndex => scheduler.ActiveIndex;
    public int AttackSequence => attackSequence;
    public BeholderAttackDefinition ActiveAttack => IsAttacking ? playingAttack : null;
    public BeholderAttackPhase Phase { get; private set; }
    public BeholderAttackExecution ActiveExecution => execution;
    public BeholderAttackContext ActiveContext => context;
    public BeholderAttackPresentation Presentation => presentation ??= new BeholderAttackPresentation(transform);
    public BeholderAttackSegment LastSegment => context != null ? context.LastSegment : lastSegment;
    public float GlobalCooldownRemaining => Mathf.Max(0f, scheduler.GlobalAvailableAt - Time.time);
    public bool CanStartAttack => isActiveAndEnabled && !IsAttacking && IsAuthority
        && (enemyHealth == null || enemyHealth.IsAlive) && HasReadyAttack(Time.time);
    public bool IsAuthority => enemySetup == null || enemySetup.HasAuthority;

    private void Awake() { ResolveReferences(); }
    private void LateUpdate() { TickAttack(Time.deltaTime); }

    public AudioCue[] GetAttackDialogues(int index) => GetSettings(index)?.dialogues;
    public BeholderAttackDefinition GetSettings(int index) =>
        attacks != null && index >= 0 && index < attacks.Length ? attacks[index]?.settings : null;

    private bool HasReadyAttack(float now)
    {
        if (attacks == null) return false;
        for (int i = 0; i < attacks.Length; i++)
            if (attacks[i] != null && attacks[i].enabled && HasAnimation(attacks[i].settings) && scheduler.IsReady(i, now)) return true;
        return false;
    }

    private bool HasAnimation(BeholderAttackDefinition attack)
    {
        if (attack == null || !attack.HasPresentation || attack.animation == null || attack.mask == null
            || string.IsNullOrWhiteSpace(attack.animatorState) || animator == null || !animator.isActiveAndEnabled
            || ResolveBone(attack.originName) == null || ResolveBone(attack.tentacleRootName) == null) return false;
        int layer = animator.GetLayerIndex(attack.LayerName);
        return layer >= 0 && animator.HasState(layer, attack.StateHash)
            && (!attack.HoldAnimation || speedParameters.Contains(Animator.StringToHash(attack.SpeedParameter)));
    }

    public bool CanAttackTarget(PlayerHealth target)
    {
        ResolveReferences();
        return SelectAttack(target, Time.time) >= 0;
    }

    private int SelectAttack(PlayerHealth target, float now)
    {
        if (!isActiveAndEnabled || !IsAuthority || (enemyHealth != null && !enemyHealth.IsAlive)
            || target == null || !target.CanBeTargetedByEnemy || enemyVision == null
            || !enemyVision.CanSeeTarget(target, out _)) return -1;
        return scheduler.SelectReady(attacks, i => HasAnimation(attacks[i].settings) && MeetsAttackRules(i, target), now);
    }

    protected virtual bool MeetsAttackRules(int index, PlayerHealth target) { return true; }

    public bool TryStartAttack(PlayerHealth target)
    {
        ResolveReferences();
        int index = SelectAttack(target, Time.time);
        BeholderAttackDefinition definition = GetSettings(index);
        if (index < 0 || !scheduler.TryStart(index, definition.cooldown, globalCooldown, Time.time)) return false;
        attackTarget = target;
        enemyVision.CanSeeTarget(target, out aimPosition);
        attackSequence++;
        if (CanSendRpc()) photonView.RPC(nameof(RpcBeginAttack), RpcTarget.Others, index, attackSequence, PhotonNetwork.Time);
        BeginAnimation(index, 0f);
        return true;
    }

    private void BeginAnimation(int index, float elapsed)
    {
        if (playingLayer >= 0)
        {
            SetMaskedSpeed(1f);
            animator.SetLayerWeight(playingLayer, 0f);
        }
        playingAttack = GetSettings(index);
        playingLayer = animator.GetLayerIndex(playingAttack.LayerName);
        tentacleRoot = ResolveBone(playingAttack.tentacleRootName);
        startedAt = Time.time - elapsed;
        effectTime = playingAttack.ResolveActivationTime();
        aimLocked = effectQueued = effectFired = attackAudioPlayed = dialoguePlayed = false;
        recoveryElapsed = 0f;
        nextBeamReplicationAt = 0f;
        Phase = BeholderAttackPhase.Preparation;
        context?.Dispose();
        context = new BeholderAttackContext(this, playingAttack, enemyVision, attackTarget, aimPosition);
        execution = IsAuthority ? playingAttack.CreateExecution(context) : null;
        SetMaskedSpeed(1f);
        animator.SetLayerWeight(playingLayer, 1f);
        float clipTime = playingAttack.HoldAnimation ? Mathf.Min(elapsed * animator.speed, effectTime) : elapsed * animator.speed;
        animator.Play(playingAttack.StateHash, playingLayer, clipTime / Mathf.Max(0.001f, playingAttack.animation.length));
        UpdateAim(Mathf.Max(0f, effectTime - clipTime) / Mathf.Max(0.01f, animator.speed));
        TickAudio(clipTime);
    }

    public void QueueAnimationEffect(string maskName)
    {
        if (IsAttacking && Phase == BeholderAttackPhase.Preparation && playingAttack.mask.name == maskName
            && IsAuthority && !effectFired) effectQueued = true;
    }

    public void TickAttack(float deltaTime)
    {
        if (enemyHealth != null && !enemyHealth.IsAlive)
        {
            CancelAttack();
            return;
        }
        presentation?.Tick(Time.time);
        if (!IsAttacking) { FadeLayerOut(); return; }
        if (IsAuthority && enemyBrain != null && enemyBrain.CurrentState != EnemyState.Attack)
        {
            CancelAttack();
            return;
        }
        if (!IsAuthority) return;
        // An ownership handoff must cancel a remote presentation, never resume damage without its execution.
        if (execution == null) { CancelAttack(); return; }
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(playingLayer);
        TickAudio(ClipTime(state));
        if (Phase == BeholderAttackPhase.Preparation)
        {
            UpdateAim(SecondsUntilEffect(state));
            if (effectQueued || ClipTime(state) >= effectTime) StartExecution(state);
        }
        else if (Phase == BeholderAttackPhase.Execution)
        {
            execution.Tick(Mathf.Max(0f, deltaTime));
            if (execution.IsFinished) BeginRecovery();
        }
        else if (Phase == BeholderAttackPhase.Recovery)
        {
            recoveryElapsed += Mathf.Max(0f, deltaTime);
            if (state.fullPathHash == playingAttack.StateHash && state.normalizedTime >= 1f
                && recoveryElapsed >= Mathf.Max(0f, playingAttack.recoveryDuration)) FinishAttack(false);
        }
        if (IsAttacking && state.fullPathHash != playingAttack.StateHash && Time.time - startedAt > 0.5f)
            CancelAttack();
    }

    private void StartExecution(AnimatorStateInfo state)
    {
        Phase = BeholderAttackPhase.Execution;
        aimLocked = effectFired = true;
        effectQueued = false;
        context.AimPoint = aimPosition;
        if (playingAttack.HoldAnimation) SetMaskedSpeed(0f);
        SendPhase(state.normalizedTime);
        PlayLoopAudio();
        execution.Begin();
        if (execution.IsFinished) BeginRecovery();
    }

    private void BeginRecovery()
    {
        Phase = BeholderAttackPhase.Recovery;
        recoveryElapsed = 0f;
        presentation?.StopBeam();
        SetMaskedSpeed(1f);
        SendPhase(animator.GetCurrentAnimatorStateInfo(playingLayer).normalizedTime);
    }

    private void SendPhase(float normalizedTime)
    {
        if (CanSendRpc()) photonView.RPC(nameof(RpcSetPhase), RpcTarget.Others, attackSequence, (int)Phase, normalizedTime);
    }

    private float SecondsUntilEffect(AnimatorStateInfo state)
    {
        float speed = animator.speed;
        float clipTime = ClipTime(state);
        if (state.fullPathHash == playingAttack.StateHash) speed *= state.speed * state.speedMultiplier;
        if (effectQueued || clipTime >= effectTime) return 0f;
        return speed > 0f ? (effectTime - clipTime) / speed : float.PositiveInfinity;
    }

    private float ClipTime(AnimatorStateInfo state) => state.fullPathHash == playingAttack.StateHash
        ? Mathf.Max(0f, state.normalizedTime) * playingAttack.animation.length
        : Mathf.Max(0f, Time.time - startedAt) * animator.speed;

    private void TickAudio(float clipTime)
    {
        if (!IsAuthority || !IsAttacking) return;
        float duration = playingAttack.animation.length;
        if (!dialoguePlayed && clipTime >= Mathf.Clamp(playingAttack.dialogueTime, 0f, duration))
        {
            dialoguePlayed = true;
            dialogue?.PlayAttackDialogue(playingAttack.dialogues, scheduler.ActiveIndex);
        }
        if (!attackAudioPlayed && clipTime >= Mathf.Clamp(playingAttack.attackAudioTime, 0f, duration))
        {
            attackAudioPlayed = true;
            PlayAttackAudio(scheduler.ActiveIndex);
        }
    }

    private void UpdateAim(float secondsUntilEffect)
    {
        if (!IsAuthority || aimLocked || effectFired) return;
        if (attackTarget != null && enemyVision != null && enemyVision.CanSeeTarget(attackTarget, out Vector3 visiblePosition))
            aimPosition = visiblePosition;
        if (context != null) context.AimPoint = aimPosition;
        aimLocked = effectQueued || secondsUntilEffect <= Mathf.Max(0f, playingAttack.aimLockLeadTime);
    }

    public bool OwnsBone(Transform bone) => playingAttack != null && tentacleRoot != null && bone != null
        && (IsAttacking || (playingLayer >= 0 && animator != null && animator.GetLayerWeight(playingLayer) > 0f))
        && bone.IsChildOf(tentacleRoot);

    private void SetMaskedSpeed(float speed)
    {
        if (animator != null && playingAttack != null && speedParameters.Contains(Animator.StringToHash(playingAttack.SpeedParameter)))
            animator.SetFloat(playingAttack.SpeedParameter, speed);
    }

    private void FadeLayerOut()
    {
        if (animator == null || playingLayer < 0) return;
        animator.SetLayerWeight(playingLayer, Mathf.MoveTowards(animator.GetLayerWeight(playingLayer), 0f, Time.deltaTime / 0.1f));
        if (animator.GetLayerWeight(playingLayer) <= 0f) { playingAttack = null; tentacleRoot = null; playingLayer = -1; }
    }

    public void CancelAttack()
    {
        bool wasAttacking = IsAttacking;
        if (wasAttacking) FinishAttack(true);
        presentation?.Clear();
        if (attackAudioSource != null) attackAudioSource.Stop();
    }

    private void FinishAttack(bool cancelled)
    {
        if (cancelled) execution?.Cancel();
        execution?.Dispose();
        execution = null;
        if (context != null) { lastSegment = context.LastSegment; context.Dispose(); context = null; }
        SetMaskedSpeed(1f);
        presentation?.StopBeam();
        if (cancelled) presentation?.Clear();
        scheduler.Complete();
        Phase = BeholderAttackPhase.None;
        effectQueued = false;
        if (IsAuthority && CanSendRpc()) photonView.RPC(nameof(RpcEndAttack), RpcTarget.Others, attackSequence, cancelled);
    }

    public void PresentSegment(BeholderAttackDefinition definition, BeholderAttackSegment segment)
    {
        if (!IsAuthority) return;
        definition.PresentSegment(Presentation, segment.Start, segment.End, segment.HasHit);
        if (definition.StreamsPresentation)
        {
            if (Time.time < nextBeamReplicationAt) return;
            nextBeamReplicationAt = Time.time + 0.05f;
        }
        if (CanSendRpc()) photonView.RPC(nameof(RpcSegment), RpcTarget.Others, attackSequence, segment.Start, segment.End, segment.HasHit);
    }

    public void StopPresentedBeam() { presentation?.StopBeam(); }

    private void PlayAttackAudio(int index)
    {
        AudioCue cue = GetSettings(index)?.attackAudio;
        AudioClip clip = cue != null ? cue.GetRandomClip() : null;
        if (clip == null) return;
        float volume = cue.GetRandomizedVolume(), pitch = cue.GetRandomizedPitch();
        int clipIndex = Array.IndexOf(cue.Clips, clip);
        ApplyAttackAudio(index, clipIndex, volume, pitch);
        if (CanSendRpc()) photonView.RPC(nameof(RpcAttackAudio), RpcTarget.Others, index, attackSequence, clipIndex, volume, pitch);
    }

    private void ApplyAttackAudio(int index, int clipIndex, float volume, float pitch)
    {
        AudioCue cue = GetSettings(index)?.attackAudio;
        if (cue == null || clipIndex < 0 || clipIndex >= cue.Clips.Length || cue.Clips[clipIndex] == null) return;
        if (attackAudioSource == null)
        {
            var source = new GameObject("Beholder Attack Audio");
            source.transform.SetParent(transform, false);
            attackAudioSource = source.AddComponent<AudioSource>();
            attackAudioSource.playOnAwake = false;
        }
        attackAudioSource.transform.position = context != null ? context.OriginPosition : transform.position;
        cue.ConfigureAudioSource(attackAudioSource, cue.Clips[clipIndex], volume, pitch);
        attackAudioSource.loop = false;
        attackAudioSource.Play();
    }

    private void PlayLoopAudio()
    {
        AudioCue cue = playingAttack.SustainedAudio;
        AudioClip clip = cue != null ? cue.GetRandomClip() : null;
        if (clip == null) return;
        int index = Array.IndexOf(cue.Clips, clip);
        float volume = cue.GetRandomizedVolume(), pitch = cue.GetRandomizedPitch();
        presentation.PlayLoop(cue, index, volume, pitch, context.OriginPosition);
        if (CanSendRpc()) photonView.RPC(nameof(RpcLoopAudio), RpcTarget.Others, attackSequence, index, volume, pitch);
    }

    [PunRPC]
    private void RpcBeginAttack(int index, int sequence, double networkStartedAt, PhotonMessageInfo info)
    {
        ResolveReferences();
        if (!IsAuthoritySender(info) || sequence <= attackSequence || !HasAnimation(GetSettings(index))) return;
        if (IsAttacking) FinishAttack(true);
        attackSequence = sequence;
        float elapsed = Mathf.Max(0f, (float)(PhotonNetwork.Time - networkStartedAt));
        scheduler.ApplyStart(index, GetSettings(index).cooldown, globalCooldown, Time.time - elapsed);
        BeginAnimation(index, elapsed);
    }

    [PunRPC]
    private void RpcSetPhase(int sequence, int phase, float normalizedTime, PhotonMessageInfo info)
    {
        if (!IsAuthoritySender(info) || sequence != attackSequence || !IsAttacking
            || (phase != (int)BeholderAttackPhase.Execution && phase != (int)BeholderAttackPhase.Recovery)) return;
        Phase = (BeholderAttackPhase)phase;
        if (Phase == BeholderAttackPhase.Recovery) presentation.StopBeam();
        SetMaskedSpeed(Phase == BeholderAttackPhase.Execution && playingAttack.HoldAnimation ? 0f : 1f);
        animator.Play(playingAttack.StateHash, playingLayer, normalizedTime);
    }

    [PunRPC]
    private void RpcSegment(int sequence, Vector3 start, Vector3 end, bool hit, PhotonMessageInfo info)
    {
        if (!IsAuthoritySender(info) || sequence != attackSequence || !IsAttacking || Phase != BeholderAttackPhase.Execution) return;
        playingAttack.PresentSegment(Presentation, start, end, hit);
    }

    [PunRPC]
    private void RpcEndAttack(int sequence, bool cancelled, PhotonMessageInfo info)
    {
        if (IsAuthoritySender(info) && sequence == attackSequence && IsAttacking) FinishAttack(cancelled);
    }

    [PunRPC]
    private void RpcAttackAudio(int index, int sequence, int clipIndex, float volume, float pitch, PhotonMessageInfo info)
    {
        if (IsAuthoritySender(info) && sequence == attackSequence && IsAttacking) ApplyAttackAudio(index, clipIndex, volume, pitch);
    }

    [PunRPC]
    private void RpcLoopAudio(int sequence, int clipIndex, float volume, float pitch, PhotonMessageInfo info)
    {
        if (IsAuthoritySender(info) && sequence == attackSequence && Phase == BeholderAttackPhase.Execution && IsAttacking)
            presentation.PlayLoop(playingAttack.SustainedAudio, clipIndex, volume, pitch, context.OriginPosition);
    }

    private bool CanSendRpc() => PhotonNetwork.InRoom && photonView != null && photonView.ViewID != 0;
    private bool IsAuthoritySender(PhotonMessageInfo info) => photonView != null && info.Sender == (photonView.Owner ?? PhotonNetwork.MasterClient);

    private void OnDisable()
    {
        CancelAttack();
        if (animator != null && playingLayer >= 0) animator.SetLayerWeight(playingLayer, 0f);
        playingAttack = null; tentacleRoot = null; playingLayer = -1;
    }
    private void OnDestroy() { context?.Dispose(); presentation?.Dispose(); }

    private Transform ResolveBone(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return bones.TryGetValue(name, out Transform bone) ? bone : null;
    }

    private void ResolveReferences()
    {
        if (enemySetup == null) enemySetup = GetComponent<EnemySetup>();
        if (enemyBrain == null) enemyBrain = GetComponent<EnemyBrain>();
        if (enemyVision == null) enemyVision = GetComponent<EnemyVision>();
        if (enemyHealth == null) enemyHealth = GetComponent<EnemyHealth>();
        if (dialogue == null) dialogue = GetComponent<BeholderDialogueController>();
        if (photonView == null) photonView = GetComponent<PhotonView>();
        if (animator == null) animator = enemySetup != null ? enemySetup.Animator : GetComponentInChildren<Animator>(true);
        if (presentation == null) presentation = new BeholderAttackPresentation(transform);
        if (bones.Count == 0)
            foreach (Transform bone in GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(bone.name)) bones.Add(bone.name, bone);
        if (animator != null && (cachedAnimatorController != animator.runtimeAnimatorController || speedParameters.Count == 0))
        {
            cachedAnimatorController = animator.runtimeAnimatorController;
            speedParameters.Clear();
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Float) speedParameters.Add(parameter.nameHash);
        }
    }
}
