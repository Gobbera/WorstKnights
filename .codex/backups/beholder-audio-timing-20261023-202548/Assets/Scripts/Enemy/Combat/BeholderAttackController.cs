using System;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(110)]
[AddComponentMenu("Enemy/Beholder Attack Controller")]
public class BeholderAttackController : MonoBehaviour
{
    [Serializable]
    public sealed class AttackDefinition
    {
        public string name = "Attack_Eye_03";
        public bool enabled = true;
        [Tooltip("Maior valor tem prioridade. Empates seguem a ordem da lista.")]
        public int priority;
        public bool canMove;
        [Min(0f)] public float movementSpeed = 3.25f;
        [Min(0f)] public float cooldown = 10f;
        [Min(0f), InspectorName("Aim Lock Lead Time (s)")]
        [Tooltip("Quanto tempo antes do disparo a mira trava na última posição visível do alvo. Valores maiores dão mais tempo para esquivar; 0 acompanha até disparar.")]
        public float aimLockLeadTime = 0.5f;
        public AudioCue attackAudio;
        public AudioCue[] dialogues = Array.Empty<AudioCue>();
        public AnimationClip animation;
        public AvatarMask mask;
        public Transform tentacleRoot;
        public string LayerName => mask != null ? mask.name.Replace('.', '_') : string.Empty;
        public int StateHash => Animator.StringToHash(LayerName + "." + (animation != null ? animation.name : string.Empty));
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
    [SerializeField] private BeholderLightningVfxTest lightning;
    [SerializeField] private BeholderDialogueController dialogue;

    private readonly BeholderAttackScheduler scheduler = new BeholderAttackScheduler();
    private PhotonView photonView;
    private AudioSource attackAudioSource;
    private PlayerHealth attackTarget;
    private AttackDefinition playingAttack;
    private Vector3 aimPosition;
    private int playingLayer = -1;
    private int attackSequence;
    private float startedAt;
    private float effectTime;
    private bool aimLocked;
    private bool effectQueued;
    private bool effectFired;

    public bool IsAttacking => scheduler.ActiveIndex >= 0;
    public int ActiveAttackIndex => scheduler.ActiveIndex;
    public int AttackSequence => attackSequence;
    public AttackDefinition ActiveAttack => IsAttacking ? playingAttack : null;
    public float GlobalCooldownRemaining => Mathf.Max(0f, scheduler.GlobalAvailableAt - Time.time);
    public bool CanStartAttack => isActiveAndEnabled && !IsAttacking && HasAuthority
        && (enemyHealth == null || enemyHealth.IsAlive) && HasReadyAttack(Time.time);
    private bool HasAuthority => enemySetup == null || enemySetup.HasAuthority;

    private void Awake() { ResolveReferences(); }

    public AudioCue[] GetAttackDialogues(int index)
    {
        return attacks != null && index >= 0 && index < attacks.Length ? attacks[index]?.dialogues : null;
    }

    private bool HasReadyAttack(float now)
    {
        if (attacks == null) return false;
        for (int i = 0; i < attacks.Length; i++)
            if (HasAnimation(attacks[i]) && scheduler.IsReady(i, now)) return true;
        return false;
    }

    private bool HasAnimation(AttackDefinition attack)
    {
        if (attack == null || !attack.enabled || attack.animation == null || attack.mask == null
            || animator == null || !animator.isActiveAndEnabled) return false;
        int layer = animator.GetLayerIndex(attack.LayerName);
        return layer >= 0 && animator.HasState(layer, attack.StateHash);
    }

    public bool CanAttackTarget(PlayerHealth target)
    {
        ResolveReferences();
        return SelectAttack(target, Time.time) >= 0;
    }

    private int SelectAttack(PlayerHealth target, float now)
    {
        if (!isActiveAndEnabled || !HasAuthority || (enemyHealth != null && !enemyHealth.IsAlive)
            || target == null || !target.CanBeTargetedByEnemy || enemyVision == null
            || !enemyVision.CanSeeTarget(target, out _)) return -1;
        return scheduler.SelectReady(attacks, i => HasAnimation(attacks[i]) && MeetsAttackRules(i, target), now);
    }

    protected virtual bool MeetsAttackRules(int index, PlayerHealth target) { return true; }

    public bool TryStartAttack(PlayerHealth target)
    {
        ResolveReferences();
        int index = SelectAttack(target, Time.time);
        if (index < 0 || !scheduler.TryStart(index, attacks[index].cooldown, globalCooldown, Time.time)) return false;
        attackTarget = target;
        enemyVision.CanSeeTarget(target, out aimPosition);
        attackSequence++;
        BeginAnimation(index, 0f);
        dialogue?.PlayAttackDialogue(attacks[index].dialogues, index);
        if (CanSendRpc()) photonView.RPC(nameof(RpcBeginAttack), RpcTarget.Others, index, attackSequence, PhotonNetwork.Time);
        return true;
    }

    private void BeginAnimation(int index, float elapsed)
    {
        if (playingLayer >= 0) animator.SetLayerWeight(playingLayer, 0f);
        playingAttack = attacks[index];
        playingLayer = animator.GetLayerIndex(playingAttack.LayerName);
        startedAt = Time.time - elapsed;
        effectTime = playingAttack.animation.length;
        foreach (AnimationEvent signal in playingAttack.animation.events)
            if (signal.functionName == nameof(BeholderAttackAnimationEvents.OnBeholderAttackEffect)
                && signal.stringParameter == playingAttack.mask.name)
                effectTime = Mathf.Min(effectTime, signal.time);
        aimLocked = false;
        effectQueued = false;
        effectFired = elapsed >= playingAttack.animation.length;
        animator.SetLayerWeight(playingLayer, 1f);
        animator.Play(playingAttack.StateHash, playingLayer, elapsed / Mathf.Max(0.001f, playingAttack.animation.length));
        UpdateAim(animator.speed > 0f ? Mathf.Max(0f, effectTime / animator.speed - elapsed) : float.PositiveInfinity);
    }

    public void QueueAnimationEffect(string maskName)
    {
        if (IsAttacking && playingAttack != null && playingAttack.mask.name == maskName && HasAuthority && !effectFired)
            effectQueued = true;
    }

    private void LateUpdate()
    {
        if (!IsAttacking)
        {
            FadeLayerOut();
            return;
        }
        if ((enemyHealth != null && !enemyHealth.IsAlive)
            || (HasAuthority && enemyBrain != null && enemyBrain.CurrentState != EnemyState.Attack))
        {
            CancelAttack();
            return;
        }
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(playingLayer);
        UpdateAim(SecondsUntilEffect(state));

        if (effectQueued && !effectFired && HasAuthority)
        {
            effectQueued = false;
            effectFired = true;
            lightning?.EmitAt(aimPosition);
            PlayAttackAudio(scheduler.ActiveIndex);
        }

        if ((state.fullPathHash == playingAttack.StateHash && state.normalizedTime >= 1f)
            || Time.time - startedAt > playingAttack.animation.length / Mathf.Max(0.01f, animator.speed) + 0.5f)
            scheduler.Complete();
    }

    private float SecondsUntilEffect(AnimatorStateInfo state)
    {
        float speed = animator.speed;
        float clipTime = Mathf.Max(0f, Time.time - startedAt) * speed;
        if (state.fullPathHash == playingAttack.StateHash)
        {
            clipTime = state.normalizedTime * playingAttack.animation.length;
            speed *= state.speed * state.speedMultiplier;
        }
        if (effectQueued || clipTime >= effectTime) return 0f;
        return speed > 0f ? (effectTime - clipTime) / speed : float.PositiveInfinity;
    }

    private void UpdateAim(float secondsUntilEffect)
    {
        if (!HasAuthority || aimLocked || effectFired) return;
        if (attackTarget != null && enemyVision != null
            && enemyVision.CanSeeTarget(attackTarget, out Vector3 visiblePosition)) aimPosition = visiblePosition;
        aimLocked = effectQueued || secondsUntilEffect <= Mathf.Max(0f, playingAttack.aimLockLeadTime);
    }

    public bool OwnsBone(Transform bone)
    {
        return playingAttack != null && playingAttack.tentacleRoot != null && bone != null
            && (IsAttacking || (playingLayer >= 0 && animator != null && animator.GetLayerWeight(playingLayer) > 0f))
            && bone.IsChildOf(playingAttack.tentacleRoot);
    }

    private void FadeLayerOut()
    {
        if (animator == null || playingLayer < 0) return;
        animator.SetLayerWeight(playingLayer, Mathf.MoveTowards(animator.GetLayerWeight(playingLayer), 0f, Time.deltaTime / 0.1f));
        if (animator.GetLayerWeight(playingLayer) <= 0f) { playingAttack = null; playingLayer = -1; }
    }

    public void CancelAttack()
    {
        bool wasAttacking = IsAttacking;
        scheduler.Complete();
        effectQueued = false;
        if (wasAttacking && HasAuthority && CanSendRpc()) photonView.RPC(nameof(RpcCancelAttack), RpcTarget.Others, attackSequence);
    }

    private void PlayAttackAudio(int index)
    {
        AudioCue cue = attacks[index].attackAudio;
        AudioClip clip = cue != null ? cue.GetRandomClip() : null;
        if (clip == null) return;
        float volume = cue.GetRandomizedVolume();
        float pitch = cue.GetRandomizedPitch();
        int clipIndex = Array.IndexOf(cue.Clips, clip);
        ApplyAttackAudio(index, clipIndex, volume, pitch);
        if (CanSendRpc()) photonView.RPC(nameof(RpcAttackAudio), RpcTarget.Others, index, clipIndex, volume, pitch);
    }

    private void ApplyAttackAudio(int index, int clipIndex, float volume, float pitch)
    {
        if (index < 0 || index >= attacks.Length) return;
        AudioCue cue = attacks[index]?.attackAudio;
        if (cue == null || clipIndex < 0 || clipIndex >= cue.Clips.Length || cue.Clips[clipIndex] == null) return;
        if (attackAudioSource == null)
        {
            var source = new GameObject("Beholder Attack Audio");
            source.transform.SetParent(transform, false);
            attackAudioSource = source.AddComponent<AudioSource>();
            attackAudioSource.playOnAwake = false;
        }
        attackAudioSource.transform.position = lightning != null ? lightning.EyePosition : transform.position;
        cue.ConfigureAudioSource(attackAudioSource, cue.Clips[clipIndex], volume, pitch);
        attackAudioSource.loop = false;
        attackAudioSource.Play();
    }

    [PunRPC]
    private void RpcBeginAttack(int index, int sequence, double networkStartedAt, PhotonMessageInfo info)
    {
        ResolveReferences();
        if (!IsAuthoritySender(info) || sequence <= attackSequence || index < 0 || index >= attacks.Length || !HasAnimation(attacks[index])) return;
        attackSequence = sequence;
        float elapsed = Mathf.Max(0f, (float)(PhotonNetwork.Time - networkStartedAt));
        scheduler.ApplyStart(index, attacks[index].cooldown, globalCooldown, Time.time - elapsed);
        BeginAnimation(index, elapsed);
    }

    [PunRPC]
    private void RpcCancelAttack(int sequence, PhotonMessageInfo info)
    {
        if (IsAuthoritySender(info) && sequence == attackSequence) { scheduler.Complete(); effectQueued = false; }
    }

    [PunRPC]
    private void RpcAttackAudio(int index, int clipIndex, float volume, float pitch, PhotonMessageInfo info)
    {
        if (IsAuthoritySender(info)) ApplyAttackAudio(index, clipIndex, volume, pitch);
    }

    private bool CanSendRpc() { return PhotonNetwork.InRoom && photonView != null && photonView.ViewID != 0; }
    private bool IsAuthoritySender(PhotonMessageInfo info)
    {
        return photonView != null && info.Sender == (photonView.Owner ?? PhotonNetwork.MasterClient);
    }

    private void OnDisable()
    {
        CancelAttack();
        if (animator != null && playingLayer >= 0) animator.SetLayerWeight(playingLayer, 0f);
        playingAttack = null;
        playingLayer = -1;
    }

    private void ResolveReferences()
    {
        if (enemySetup == null) enemySetup = GetComponent<EnemySetup>();
        if (enemyBrain == null) enemyBrain = GetComponent<EnemyBrain>();
        if (enemyVision == null) enemyVision = GetComponent<EnemyVision>();
        if (enemyHealth == null) enemyHealth = GetComponent<EnemyHealth>();
        if (lightning == null) lightning = GetComponent<BeholderLightningVfxTest>();
        if (dialogue == null) dialogue = GetComponent<BeholderDialogueController>();
        if (photonView == null) photonView = GetComponent<PhotonView>();
        if (animator == null) animator = enemySetup != null ? enemySetup.Animator : GetComponentInChildren<Animator>(true);
    }
}
