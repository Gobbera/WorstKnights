using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioEmitter))]
public class FootstepPlaybackController : MonoBehaviour
{
    private const string DefaultSurfaceResolverResourcePath = "Audio/FootstepSurfaceResolver_Default";

    [Header("References")]
    [SerializeField] private AudioEmitter audioEmitter;
    [FormerlySerializedAs("footstepSurfaceResolver")]
    [FormerlySerializedAs("surfaceResolver")]
    [SerializeField] private FootstepSurfaceResolver surfaceResolver;
    [FormerlySerializedAs("footstepCharacterProfile")]
    [FormerlySerializedAs("characterFootstepProfile")]
    [SerializeField] private FootstepCharacterAudioProfile characterProfile;
    [SerializeField] private Transform footstepAnchor;

    [Header("Ground Probe")]
    [FormerlySerializedAs("footstepGroundMask")]
    [SerializeField] private LayerMask groundMask = Physics.DefaultRaycastLayers;
    [FormerlySerializedAs("footstepProbeOriginLift")]
    [SerializeField] [Min(0f)] private float probeOriginLift = 0.35f;
    [FormerlySerializedAs("footstepProbeDistance")]
    [SerializeField] [Min(0.05f)] private float probeDistance = 1.5f;
    [SerializeField] [Min(-2f)] private float footstepHeightOffset;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging;

    private readonly RaycastHit[] groundHits = new RaycastHit[8];
    private Rigidbody rb;
    private Collider rootCollider;
    private bool hasTriedLoadSurfaceResolver;
    private bool hasLoggedMissingCue;

    public AudioEmitter AudioEmitter => audioEmitter;
    public FootstepSurfaceResolver SurfaceResolver => surfaceResolver;
    public FootstepCharacterAudioProfile CharacterProfile => characterProfile;
    public Transform FootstepAnchor => footstepAnchor != null ? footstepAnchor : transform;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        hasLoggedMissingCue = false;
    }

    public bool PlayFootstep(FootstepGait gait, AudioCue surfaceFallbackCue = null)
    {
        return PlayFootstep(gait, surfaceFallbackCue, 0f);
    }

    public bool PlayFootstep(FootstepGait gait, AudioCue surfaceFallbackCue, float additionalHeightOffset)
    {
        Vector3 footstepPosition = GetFootstepPosition(additionalHeightOffset);
        return PlayFootstep(gait, footstepPosition, surfaceFallbackCue);
    }

    public bool PlayFootstep(FootstepGait gait, Vector3 footstepPosition, AudioCue surfaceFallbackCue = null)
    {
        ResolveReferences();

        if (audioEmitter == null)
            return false;

        AudioCue surfaceCue = ResolveSurfaceCue(footstepPosition, gait, surfaceFallbackCue);
        AudioCue characterCue = ResolveCharacterCue(gait);

        bool hasConfiguredCue = false;
        if (surfaceCue != null)
        {
            audioEmitter.PlayAtPosition(surfaceCue, footstepPosition);
            hasConfiguredCue = true;
        }

        if (characterCue != null && characterCue != surfaceCue)
        {
            audioEmitter.PlayAtPosition(characterCue, footstepPosition);
            hasConfiguredCue = true;
        }

        if (!hasConfiguredCue)
            LogMissingCueOnce(gait);

        return hasConfiguredCue;
    }

    public Vector3 GetFootstepPosition(float additionalHeightOffset = 0f)
    {
        Transform anchor = FootstepAnchor;
        return anchor.position + Vector3.up * (footstepHeightOffset + additionalHeightOffset);
    }

    public AudioCue ResolveSurfaceCue(Vector3 footstepPosition, FootstepGait gait, AudioCue fallbackCue = null)
    {
        ResolveReferences();

        if (surfaceResolver == null)
            return fallbackCue;

        if (FootstepSurfaceUtility.TryFindGroundHit(
            footstepPosition,
            groundMask,
            probeOriginLift,
            probeDistance,
            transform,
            rb,
            rootCollider,
            groundHits,
            out RaycastHit groundHit))
        {
            return surfaceResolver.ResolveCue(groundHit, gait, fallbackCue);
        }

        return surfaceResolver.ResolveDefaultCue(gait, fallbackCue);
    }

    public AudioCue ResolveCharacterCue(FootstepGait gait)
    {
        return characterProfile != null ? characterProfile.GetCue(gait) : null;
    }

    private void ResolveReferences()
    {
        if (audioEmitter == null)
            audioEmitter = GetComponent<AudioEmitter>();

        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (rootCollider == null)
            rootCollider = GetComponent<Collider>();

        if (footstepAnchor == null)
            footstepAnchor = transform;

        if (surfaceResolver == null && !hasTriedLoadSurfaceResolver)
        {
            hasTriedLoadSurfaceResolver = true;
            surfaceResolver = Resources.Load<FootstepSurfaceResolver>(DefaultSurfaceResolverResourcePath);
        }
    }

    private void LogMissingCueOnce(FootstepGait gait)
    {
        if (!verboseLogging || hasLoggedMissingCue)
            return;

        hasLoggedMissingCue = true;
        Debug.Log($"[FootstepPlaybackController] Nenhum AudioCue de passo configurado para '{gait}'.", gameObject);
    }
}
