using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
public class AudioEmitter : MonoBehaviour
{
    [SerializeField] private Transform playbackAnchor;
    [SerializeField] private bool stopLoopingCueOnDisable = true;

    private readonly Dictionary<AudioCue, float> lastPlaybackTimes = new Dictionary<AudioCue, float>();
    private AudioSource activeLoopingSource;
    private AudioCue activeLoopingCue;

    public Transform PlaybackAnchor => playbackAnchor != null ? playbackAnchor : transform;

    private void OnDisable()
    {
        if (stopLoopingCueOnDisable)
            StopLoopingCue();
    }

    public AudioSource Play(AudioCue cue)
    {
        if (!CanPlay(cue))
            return null;

        Transform anchor = PlaybackAnchor;
        AudioSource source = cue.Is3D && cue.Anchor == AudioPlaybackAnchor.FollowTransform
            ? GameAudioService.Instance.PlayCue(cue, anchor)
            : GameAudioService.Instance.PlayCue(cue, anchor.position);

        RegisterPlayback(cue, source);
        return source;
    }

    public AudioSource PlayAtPosition(AudioCue cue, Vector3 worldPosition)
    {
        if (!CanPlay(cue))
            return null;

        AudioSource source = GameAudioService.Instance.PlayCue(cue, worldPosition);
        RegisterPlayback(cue, source);
        return source;
    }

    public AudioSource PlayAttached(AudioCue cue, Transform anchor)
    {
        if (!CanPlay(cue))
            return null;

        Transform resolvedAnchor = anchor != null ? anchor : PlaybackAnchor;
        AudioSource source = GameAudioService.Instance.PlayCue(cue, resolvedAnchor);
        RegisterPlayback(cue, source);
        return source;
    }

    public void StopLoopingCue()
    {
        if (activeLoopingSource == null)
            return;

        GameAudioService.Stop(activeLoopingSource);
        activeLoopingSource = null;
        activeLoopingCue = null;
    }

    private bool CanPlay(AudioCue cue)
    {
        if (cue == null)
            return false;

        if (!cue.HasPlayableClip())
        {
            Debug.LogWarning($"[AudioEmitter] '{cue.name}' nao possui nenhum AudioClip valido.", cue);
            return false;
        }

        if (cue.Cooldown <= 0f)
            return true;

        float now = Time.unscaledTime;
        if (!lastPlaybackTimes.TryGetValue(cue, out float lastTime))
            return true;

        return now - lastTime >= cue.Cooldown;
    }

    private void RegisterPlayback(AudioCue cue, AudioSource source)
    {
        if (cue == null || source == null)
            return;

        lastPlaybackTimes[cue] = Time.unscaledTime;

        if (!cue.Loop)
            return;

        if (activeLoopingSource != null && activeLoopingSource != source)
            GameAudioService.Stop(activeLoopingSource);

        activeLoopingCue = cue;
        activeLoopingSource = source;
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(AudioEmitter))]
[RequireComponent(typeof(PhotonView))]
public partial class PlayerAudioController : MonoBehaviour
{
    private const string DefaultProfileResourcePath = "Audio/PlayerAudioProfile_Default";
    private const string RightHandSocketName = "RightHandSocket";
    private const string LegacyRightHandSocketName = "HandSocket";

    [Header("References")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private AudioEmitter audioEmitter;
    [SerializeField] private PhotonView photonView;
    [SerializeField] private PlayerAudioProfile profile;
    [SerializeField] private Transform footstepAnchor;
    [SerializeField] private Transform attackAnchor;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging;

    private int lastAttackSequence;
    private int lastJumpSequence;
    private int lastLandingSequence;
    private float footstepDistanceAccumulator;
    private bool hasInitializedSequenceState;

    private void Awake()
    {
        ResolveReferences();
        InitializeSequenceState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        InitializeSequenceState();
        ResetFootstepState();
    }

    private void Update()
    {
        ResolveReferences();
        UpdateActionAudio();
        UpdateFootsteps(Time.deltaTime);
    }

    private void ResolveReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (audioEmitter == null)
            audioEmitter = GetComponent<AudioEmitter>();

        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (profile == null)
            profile = Resources.Load<PlayerAudioProfile>(DefaultProfileResourcePath);

        if (footstepAnchor == null)
            footstepAnchor = transform;

        if (attackAnchor == null)
            attackAnchor = FindChildTransformByName(RightHandSocketName) ?? FindChildTransformByName(LegacyRightHandSocketName) ?? transform;
    }

    private void InitializeSequenceState()
    {
        if (playerMovement == null)
            return;

        lastAttackSequence = playerMovement.AttackAnimationSequence;
        lastJumpSequence = playerMovement.JumpAnimationSequence;
        lastLandingSequence = playerMovement.LandingAnimationSequence;
        hasInitializedSequenceState = true;
    }

    private void UpdateActionAudio()
    {
        if (playerMovement == null)
            return;

        if (!hasInitializedSequenceState)
            InitializeSequenceState();

        if (playerMovement.AttackAnimationSequence != lastAttackSequence)
        {
            lastAttackSequence = playerMovement.AttackAnimationSequence;
            PlayAttached(profile != null ? profile.Attack : null, attackAnchor);
        }

        if (playerMovement.JumpAnimationSequence != lastJumpSequence)
        {
            lastJumpSequence = playerMovement.JumpAnimationSequence;
            PlayAttached(profile != null ? profile.Jump : null, footstepAnchor);
            ResetFootstepState();
        }

        if (playerMovement.LandingAnimationSequence != lastLandingSequence)
        {
            lastLandingSequence = playerMovement.LandingAnimationSequence;
            PlayAttached(profile != null ? profile.Land : null, footstepAnchor);
            ResetFootstepState();
        }
    }

    private void UpdateFootsteps(float deltaTime)
    {
        if (playerMovement == null || profile == null || deltaTime <= 0f)
            return;

        if (!ShouldEmitFootsteps(playerMovement.CurrentState, out PlayerAudioProfile.FootstepSettings settings))
        {
            ResetFootstepState();
            return;
        }

        if (settings == null || settings.Cue == null)
            return;

        float metersPerStep = Mathf.Max(0.1f, settings.MetersPerStep);
        footstepDistanceAccumulator += Mathf.Max(0f, playerMovement.PlanarSpeed) * deltaTime;

        while (footstepDistanceAccumulator >= metersPerStep)
        {
            Vector3 footstepPosition = GetFootstepPosition();
            audioEmitter.PlayAtPosition(settings.Cue, footstepPosition);
            footstepDistanceAccumulator -= metersPerStep;
        }
    }

    private bool ShouldEmitFootsteps(MovementState movementState, out PlayerAudioProfile.FootstepSettings settings)
    {
        settings = null;

        if (playerMovement == null || profile == null)
            return false;

        if (!playerMovement.IsGrounded || playerMovement.IsJumpQueued)
            return false;

        switch (movementState)
        {
            case MovementState.walking:
            case MovementState.sprinting:
            case MovementState.crouching:
                break;
            default:
                return false;
        }

        settings = profile.GetFootstepSettings(movementState);
        if (settings == null)
            return false;

        return playerMovement.PlanarSpeed >= Mathf.Max(0f, settings.MinPlanarSpeed);
    }

    private Vector3 GetFootstepPosition()
    {
        Transform anchor = footstepAnchor != null ? footstepAnchor : transform;
        float heightOffset = profile != null ? profile.FootstepHeightOffset : 0f;
        return anchor.position + Vector3.up * heightOffset;
    }

    private void PlayAttached(AudioCue cue, Transform anchor)
    {
        if (cue == null || audioEmitter == null)
        {
            if (cue == null && verboseLogging && profile == null)
                Debug.Log("[PlayerAudioController] Nenhum PlayerAudioProfile/Cue configurado ainda para este evento.", gameObject);

            return;
        }

        audioEmitter.PlayAttached(cue, anchor != null ? anchor : transform);
    }

    private void ResetFootstepState()
    {
        footstepDistanceAccumulator = 0f;
    }

    private Transform FindChildTransformByName(string childName)
    {
        if (string.IsNullOrWhiteSpace(childName))
            return null;

        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform childTransform = childTransforms[i];
            if (childTransform != null && string.Equals(childTransform.name, childName, StringComparison.Ordinal))
                return childTransform;
        }

        return null;
    }
}
