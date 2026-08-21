using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAnimationController : MonoBehaviour
{
    private const string IdleStatePath = "Base Layer.Idle";
    private const string WalkStatePath = "Base Layer.Walk";
    private const string TakeDamageStatePath = "Base Layer.Take Damage";
    private const string PreSpawnStatePath = "Base Layer.Pre-Spawn";
    private const string SpawnStatePath = "Base Layer.Spawn";
    private const string DeathStatePath = "Base Layer.Death";
    private const float LocomotionTransitionDuration = 0.1f;
    private const float TakeDamageTransitionDuration = 0.06f;
    private const float SpawnTransitionDuration = 0.05f;
    private const float DeathTransitionDuration = 0.08f;
    private const float AnimationCompletionPadding = 0.05f;

    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private Animator animator;
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyMotor enemyMotor;
    [SerializeField] private EnemyAttack enemyAttack;
    [SerializeField] private EnemyHealth enemyHealth;

    [Header("Spawn / Death")]
    [SerializeField] private bool playPreSpawnAndSpawn = true;
    [SerializeField] [Min(0f)] private float spawnFallbackDuration = 1f;
    [SerializeField] [Min(0f)] private float deathFallbackDuration = 1f;
    [SerializeField] [Min(0f)] private float deathFadeOutStartDelay = 0f;
    [SerializeField] [Min(0f)] private float deathFadeOutDuration = 1f;

    private readonly Dictionary<string, AnimatorControllerParameterType> parameterTypes = new Dictionary<string, AnimatorControllerParameterType>();
    private RuntimeAnimatorController cachedRuntimeAnimatorController;
    private int idleStateHash;
    private int walkStateHash;
    private int takeDamageStateHash;
    private int preSpawnStateHash;
    private int spawnStateHash;
    private int deathStateHash;
    private bool hasIdleState;
    private bool hasWalkState;
    private bool hasTakeDamageState;
    private bool hasPreSpawnState;
    private bool hasSpawnState;
    private bool hasDeathState;
    private int lastAttackSequence;
    private int lastDamageSequence;
    private bool wasAlive = true;
    private bool spawnPlaybackStarted;
    private float spawnPlaybackStartedAt;
    private float spawnPlaybackDuration;
    private bool deathSequenceStarted;

    private void Awake()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyBrain == null)
            enemyBrain = GetComponent<EnemyBrain>();

        if (enemyMotor == null)
            enemyMotor = GetComponent<EnemyMotor>();

        if (enemyAttack == null)
            enemyAttack = GetComponent<EnemyAttack>();

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (animator == null)
            animator = enemySetup != null ? enemySetup.Animator : GetComponentInChildren<Animator>(true);

        RefreshAnimatorConfiguration();
        wasAlive = enemyHealth == null || enemyHealth.IsAlive;
        lastAttackSequence = enemyAttack != null ? enemyAttack.AttackSequence : 0;
        lastDamageSequence = enemyHealth != null ? enemyHealth.DamageSequence : 0;
    }

    private void Update()
    {
        EnsureAnimatorConfiguration();
        if (animator == null)
            return;

        bool isAlive = enemyHealth == null || enemyHealth.IsAlive;
        EnemyState state = enemyBrain != null ? enemyBrain.CurrentState : (isAlive ? EnemyState.Idle : EnemyState.Dead);
        float planarSpeed = enemyMotor != null ? enemyMotor.CurrentPlanarSpeed : 0f;
        float normalizedSpeed = enemyMotor != null && enemyMotor.MoveSpeed > 0.0001f
            ? planarSpeed / enemyMotor.MoveSpeed
            : 0f;
        bool isMoving = planarSpeed > 0.05f;

        SetBoolIfExists("IsAlive", isAlive);
        SetBoolIfExists("Alive", isAlive);
        SetBoolIfExists("IsDead", !isAlive);
        SetBoolIfExists("Dead", !isAlive);
        SetBoolIfExists("IsMoving", isMoving);
        SetBoolIfExists("Moving", isMoving);
        SetBoolIfExists("IsAttacking", state == EnemyState.Attacking);
        SetBoolIfExists("Attacking", state == EnemyState.Attacking);
        SetBoolIfExists("IsChasing", state == EnemyState.Chasing);
        SetBoolIfExists("Chasing", state == EnemyState.Chasing);
        SetIntegerIfExists("State", (int)state);
        SetFloatIfExists("MoveSpeed", planarSpeed);
        SetFloatIfExists("Speed", planarSpeed);
        SetFloatIfExists("NormalizedSpeed", normalizedSpeed);

        if (!isAlive)
        {
            PlayDeathSequenceIfNeeded();
            wasAlive = isAlive;
            return;
        }

        if (HandleSpawnState(state))
        {
            wasAlive = isAlive;
            return;
        }

        if (isAlive && enemyHealth != null && enemyHealth.DamageSequence != lastDamageSequence)
        {
            lastDamageSequence = enemyHealth.DamageSequence;
            if (enemyAttack == null || enemyAttack.CanDamageInterruptCurrentAttack(animator))
                PlayTakeDamageAnimation();
        }

        ApplySimpleLocomotionFallback(isAlive, state, isMoving);

        if (enemyAttack != null && enemyAttack.AttackSequence != lastAttackSequence)
        {
            lastAttackSequence = enemyAttack.AttackSequence;
            SetTriggerIfExists("Attack");
            SetTriggerIfExists("AttackTrigger");
        }

        wasAlive = isAlive;
    }

    public bool TryGetDeathSequenceDestroyDelay(out float delay)
    {
        EnsureAnimatorConfiguration();

        float deathDuration = ResolveDeathAnimationDuration();
        float fadeStartDelay = Mathf.Max(0f, deathFadeOutStartDelay);
        float fadeDuration = Mathf.Max(0f, deathFadeOutDuration);
        delay = deathDuration + fadeStartDelay + fadeDuration;
        return hasDeathState || deathDuration > 0f || fadeStartDelay > 0f || fadeDuration > 0f;
    }

    private void EnsureAnimatorConfiguration()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (animator == null)
            animator = enemySetup != null ? enemySetup.Animator : GetComponentInChildren<Animator>(true);

        if (animator == null)
            return;

        if (animator.runtimeAnimatorController != cachedRuntimeAnimatorController)
            RefreshAnimatorConfiguration();
    }

    private void RefreshAnimatorConfiguration()
    {
        cachedRuntimeAnimatorController = animator != null ? animator.runtimeAnimatorController : null;
        CacheAnimatorParameters();
        CacheLocomotionStates();
    }

    private void CacheAnimatorParameters()
    {
        parameterTypes.Clear();
        if (animator == null)
            return;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
            parameterTypes[parameters[i].name] = parameters[i].type;
    }

    private void CacheLocomotionStates()
    {
        idleStateHash = Animator.StringToHash(IdleStatePath);
        walkStateHash = Animator.StringToHash(WalkStatePath);
        takeDamageStateHash = Animator.StringToHash(TakeDamageStatePath);
        preSpawnStateHash = Animator.StringToHash(PreSpawnStatePath);
        spawnStateHash = Animator.StringToHash(SpawnStatePath);
        deathStateHash = Animator.StringToHash(DeathStatePath);
        hasIdleState = animator != null && animator.HasState(0, idleStateHash);
        hasWalkState = animator != null && animator.HasState(0, walkStateHash);
        hasTakeDamageState = animator != null && animator.HasState(0, takeDamageStateHash);
        hasPreSpawnState = animator != null && animator.HasState(0, preSpawnStateHash);
        hasSpawnState = animator != null && animator.HasState(0, spawnStateHash);
        hasDeathState = animator != null && animator.HasState(0, deathStateHash);
    }

    private void ApplySimpleLocomotionFallback(bool isAlive, EnemyState state, bool isMoving)
    {
        if (animator == null
            || !isAlive
            || state == EnemyState.Attacking
            || state == EnemyState.Dead
            || state == EnemyState.PreSpawn
            || state == EnemyState.Spawning)
            return;

        if (!hasIdleState || !hasWalkState || IsTakeDamageStateActive())
            return;

        int targetStateHash = isMoving ? walkStateHash : idleStateHash;
        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (currentState.fullPathHash == targetStateHash)
            return;

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
            if (nextState.fullPathHash == targetStateHash)
                return;
        }

        animator.CrossFadeInFixedTime(targetStateHash, LocomotionTransitionDuration, 0, 0f);
    }

    private void PlayTakeDamageAnimation()
    {
        if (animator == null)
            return;

        if (hasTakeDamageState)
        {
            if (IsTakeDamageStateActive())
            {
                animator.Play(takeDamageStateHash, 0, 0f);
                return;
            }

            animator.CrossFadeInFixedTime(takeDamageStateHash, TakeDamageTransitionDuration, 0, 0f);
            return;
        }

        ResetTriggerIfExists("TakeDamage");
        ResetTriggerIfExists("Damage");
        ResetTriggerIfExists("Hit");
        SetTriggerIfExists("TakeDamage");
        SetTriggerIfExists("Damage");
        SetTriggerIfExists("Hit");
    }

    private bool HandleSpawnState(EnemyState state)
    {
        if (!playPreSpawnAndSpawn)
            return false;

        if (state == EnemyState.PreSpawn)
        {
            spawnPlaybackStarted = false;
            if (hasPreSpawnState)
                CrossFadeStateIfNeeded(preSpawnStateHash, SpawnTransitionDuration);

            return true;
        }

        if (state != EnemyState.Spawning)
            return false;

        if (!hasSpawnState)
        {
            enemyBrain?.CompleteSpawnSequence();
            return true;
        }

        if (!spawnPlaybackStarted)
            StartSpawnPlayback();

        if (IsSpawnPlaybackComplete())
        {
            spawnPlaybackStarted = false;
            enemyBrain?.CompleteSpawnSequence();
        }

        return true;
    }

    private void StartSpawnPlayback()
    {
        spawnPlaybackStarted = true;
        spawnPlaybackStartedAt = Time.time;
        spawnPlaybackDuration = ResolveClipDuration("Spawn", spawnFallbackDuration);
        animator.CrossFadeInFixedTime(spawnStateHash, SpawnTransitionDuration, 0, 0f);
    }

    private bool IsSpawnPlaybackComplete()
    {
        if (animator == null)
            return true;

        if (Time.time >= spawnPlaybackStartedAt + Mathf.Max(0f, spawnPlaybackDuration) + AnimationCompletionPadding)
            return true;

        if (animator.IsInTransition(0))
            return false;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        return currentState.fullPathHash == spawnStateHash && currentState.normalizedTime >= 1f;
    }

    private void PlayDeathSequenceIfNeeded()
    {
        if (deathSequenceStarted)
            return;

        deathSequenceStarted = true;
        spawnPlaybackStarted = false;

        if (hasDeathState)
            animator.CrossFadeInFixedTime(deathStateHash, DeathTransitionDuration, 0, 0f);
        else
        {
            SetTriggerIfExists("Death");
            SetTriggerIfExists("Die");
            SetTriggerIfExists("Dead");
        }

        ConfigureDeathFadeOut();
    }

    private void ConfigureDeathFadeOut()
    {
        if (deathFadeOutDuration <= 0f || enemyHealth == null || !enemyHealth.DestroysOnDeath)
            return;

        float lifetime = ResolveDeathAnimationDuration() + Mathf.Max(0f, deathFadeOutStartDelay) + deathFadeOutDuration;
        if (lifetime <= 0f)
            return;

        SpawnedDestructibleFadeOut fadeOut = GetComponent<SpawnedDestructibleFadeOut>();
        if (fadeOut == null)
            fadeOut = gameObject.AddComponent<SpawnedDestructibleFadeOut>();

        fadeOut.Initialize(gameObject, lifetime, deathFadeOutDuration);
    }

    private float ResolveDeathAnimationDuration()
    {
        return ResolveClipDuration("Death", deathFallbackDuration);
    }

    private float ResolveClipDuration(string clipNameFragment, float fallbackDuration)
    {
        RuntimeAnimatorController runtimeController = animator != null ? animator.runtimeAnimatorController : null;
        if (runtimeController != null)
        {
            AnimationClip[] clips = runtimeController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null)
                    continue;

                if (clip.name.IndexOf(clipNameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return Mathf.Max(0f, clip.length);
            }
        }

        return Mathf.Max(0f, fallbackDuration);
    }

    private void CrossFadeStateIfNeeded(int targetStateHash, float transitionDuration)
    {
        if (animator == null)
            return;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (currentState.fullPathHash == targetStateHash)
            return;

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
            if (nextState.fullPathHash == targetStateHash)
                return;
        }

        animator.CrossFadeInFixedTime(targetStateHash, transitionDuration, 0, 0f);
    }

    private bool IsTakeDamageStateActive()
    {
        if (animator == null || !hasTakeDamageState)
            return false;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (currentState.fullPathHash == takeDamageStateHash)
            return true;

        if (!animator.IsInTransition(0))
            return false;

        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
        return nextState.fullPathHash == takeDamageStateHash;
    }

    private void SetBoolIfExists(string parameterName, bool value)
    {
        if (HasParameter(parameterName, AnimatorControllerParameterType.Bool))
            animator.SetBool(parameterName, value);
    }

    private void SetFloatIfExists(string parameterName, float value)
    {
        if (HasParameter(parameterName, AnimatorControllerParameterType.Float))
            animator.SetFloat(parameterName, value);
    }

    private void SetIntegerIfExists(string parameterName, int value)
    {
        if (HasParameter(parameterName, AnimatorControllerParameterType.Int))
            animator.SetInteger(parameterName, value);
    }

    private void SetTriggerIfExists(string parameterName)
    {
        if (HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
            animator.SetTrigger(parameterName);
    }

    private void ResetTriggerIfExists(string parameterName)
    {
        if (HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
            animator.ResetTrigger(parameterName);
    }

    private bool HasParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        return animator != null
            && parameterTypes.TryGetValue(parameterName, out AnimatorControllerParameterType foundType)
            && foundType == parameterType;
    }
}
