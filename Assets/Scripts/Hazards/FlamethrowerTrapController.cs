using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.VFX;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("World/Hazards/Flamethrower Trap Controller")]
public sealed partial class FlamethrowerTrapController : MonoBehaviourPunCallbacks
{
    public enum TrapPhase { Idle, Warning, Firing, Cooldown }

    [Header("Dispenser")]
    [SerializeField] private Transform flamethrowerPivot;
    [SerializeField] private VisualEffect flameEffect;
    [Tooltip("Opcional: parte do dispenser que acompanha o movimento. Vazio movimenta apenas a saida do fogo.")]
    [SerializeField] private Transform movingPart;
    [Tooltip("A boca do modelo atual aponta para -Z; o VFX emite em +Z.")]
    [SerializeField] private Vector3 localEulerAngles = new Vector3(0f, 180f, 0f);
    [SerializeField] private Vector3 localPositionOffset;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float activationDelay = 0.35f;
    [SerializeField, Min(0.05f)] private float firingDuration = 4f;
    [SerializeField, Min(0f)] private float cooldown = 1f;
    [Tooltip("Tempo para o fogo alcancar o fim do jato; acompanha a menor vida das chamas no grafico.")]
    [SerializeField, Min(0.01f)] private float fillTime = 1.1f;
    [Tooltip("Tempo para dissipar visualmente as chamas depois de encerrar o disparo. Zero corta imediatamente; nao prolonga o dano.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.75f;

    [Header("Audio")]
    [Tooltip("Som emitido na saida do fogo durante o disparo. Ative Loop no AudioCue para audio continuo.")]
    [SerializeField] private AudioCue flameAudioCue;
    [Tooltip("AudioSource dedicado em um filho da armadilha, preparado no prefab antes do disparo.")]
    [SerializeField] private AudioSource flameAudioSource;

    [Header("End Light")]
    [SerializeField] private bool endLightEnabled = true;
    [SerializeField] private Light endLight;
    [SerializeField] private Color endLightColor = new Color(1f, 0.35f, 0.08f);
    [SerializeField, Min(0f)] private float endLightIntensity = 2.5f;
    [SerializeField, Min(0.01f)] private float endLightRange = 4f;
    [Tooltip("Afasta a luz da superficie atingida para iluminar o lado exposto da parede.")]
    [SerializeField, Min(0f)] private float endLightSurfaceOffset = 0.2f;

    [Header("Jet Shape (world units)")]
    [SerializeField, Min(0.1f)] private float range = 10f;
    [SerializeField, Min(0.01f)] private float startRadius = 0.2f;
    [SerializeField, Min(0.01f)] private float endRadius = 1.1f;

    [Header("Movement")]
    [Tooltip("Amplitude angular em graus: X inclina, Y faz varredura lateral, Z gira o eixo do jato.")]
    [SerializeField] private Vector3 angularAmplitude;
    [Tooltip("Amplitude de deslocamento local em metros.")]
    [SerializeField] private Vector3 positionAmplitude;
    [SerializeField, Min(0.05f)] private float movementPeriod = 3f;

    [Header("Damage")]
    [SerializeField, Min(0f)] private float damagePerSecond = 25f;
    [SerializeField, Min(0.05f)] private float damageTickInterval = 0.25f;
    [SerializeField] private bool damagePlayers = true;
    [SerializeField] private bool damageEnemies = true;
    [Tooltip("Feedback opcional por tick. None evita interromper a movimentacao continuamente com o dano de fogo.")]
    [SerializeField] private PlayerDamageAnimationType playerDamageAnimation = PlayerDamageAnimationType.None;
    [SerializeField] private PlayerCameraImpactType playerCameraImpact = PlayerCameraImpactType.None;
    [SerializeField] private LayerMask targetMask = Physics.DefaultRaycastLayers;
    [SerializeField] private LayerMask obstructionMask = Physics.DefaultRaycastLayers;

    [Header("Networking")]
    [SerializeField] private bool localOnly;

    private double activationTime;
    private bool activationActive;
    private bool visualPlaying;
    private bool movementCaptured;
    private Transform capturedMovingPart;
    private Vector3 movingBasePosition;
    private Quaternion movingBaseRotation;
    private Collider[] overlaps = new Collider[32];
    private RaycastHit[] obstructionHits = new RaycastHit[32];
    private readonly HashSet<CombatHealth> damagedThisTick = new HashSet<CombatHealth>();
    private readonly Dictionary<CombatHealth, double> nextDamageTime = new Dictionary<CombatHealth, double>();

    public TrapPhase Phase { get; private set; }
    public bool IsEmitting => Phase == TrapPhase.Firing;
    public bool IsCycleActive => activationActive && Clock < activationTime + activationDelay + firingDuration + cooldown;
    public bool IsFadingOut => visualFading;
    public float Range => range;
    public float ResolvedLength { get; private set; }
    public float DamageLength { get; private set; }
    public Vector3 JetOrigin { get; private set; }
    public Quaternion JetRotation { get; private set; } = Quaternion.identity;
    public Vector3 JetDirection => JetRotation * Vector3.forward;
    public VisualEffect FlameEffect => flameEffect;
    public Light EndLight => endLight;
    private bool Online => Application.IsPlaying(gameObject) && !localOnly && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;
    private double Clock => Online ? PhotonNetwork.Time
        : Application.IsPlaying(gameObject) ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;

    private void Awake()
    {
        ResolveReferences();
        PrepareFlameAudio();
        EnsureNetworkKey();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        ResolveReferences();
        PrepareFlameAudio();
        EnsureNetworkKey();
        ClearVisuals();
        if (Online) { stateSequence = 0; RestoreNetworkState(); }
    }

    private void Start() => RestoreNetworkState();
    private void Reset() => ResolveReferences();

    private void OnValidate()
    {
        activationDelay = Mathf.Max(0f, activationDelay);
        firingDuration = Mathf.Max(0.05f, firingDuration);
        cooldown = Mathf.Max(0f, cooldown);
        fillTime = Mathf.Max(0.01f, fillTime);
        fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
        range = Mathf.Max(0.1f, range);
        startRadius = Mathf.Max(0.01f, startRadius);
        endRadius = Mathf.Max(startRadius, endRadius);
        damagePerSecond = Mathf.Max(0f, damagePerSecond);
        damageTickInterval = Mathf.Max(0.05f, damageTickInterval);
        movementPeriod = Mathf.Max(0.05f, movementPeriod);
        endLightIntensity = Mathf.Max(0f, endLightIntensity);
        endLightRange = Mathf.Max(0.01f, endLightRange);
        endLightSurfaceOffset = Mathf.Max(0f, endLightSurfaceOffset);
    }

    private void Update()
    {
        PrepareFlameAudio();
        RefreshTrap();
    }

    private void FixedUpdate()
    {
        RefreshTrap();
        if (IsEmitting)
            ApplyDamageTick(Clock);
    }

    public override void OnDisable()
    {
        if (Online && PhotonNetwork.IsMasterClient && activationActive)
        {
            activationActive = false;
            activationTime = Clock;
            stateSequence++;
            PublishNetworkState();
        }
        activationActive = false;
        Phase = TrapPhase.Idle;
        ClearVisuals();
        RestoreMovement();
        nextDamageTime.Clear();
        base.OnDisable();
    }

    [ContextMenu("Trigger Trap")]
    public void TriggerTrap() => TryTriggerTrap();

    public bool TryTriggerTrap()
    {
        if (!isActiveAndEnabled) return false;
        if (Online && !PhotonNetwork.IsMasterClient) return RequestMasterTrigger();
        if (IsCycleActive) return false;
        ResolveReferences();
        if (!HasCompatibleEffect())
        {
            Debug.LogWarning("[FlamethrowerTrap] Configure o pivot e o VFX preparado para o controlador.", this);
            return false;
        }
        ApplyActivation(++stateSequence, Clock, true);
        PublishNetworkState();
        return true;
    }

    [ContextMenu("Stop Trap")]
    public void StopTrap()
    {
        if (Online && !PhotonNetwork.IsMasterClient) return;
        ApplyActivation(++stateSequence, Clock, false);
        PublishNetworkState();
    }

    private void ApplyActivation(int sequence, double startedAt, bool active)
    {
        stateSequence = sequence;
        activationTime = startedAt;
        activationActive = active;
        nextDamageTime.Clear();
        if (active) ClearVisuals();
        RestoreMovement();
        CaptureMovement();
        RefreshTrap();
    }

    public void RefreshTrap() => RefreshAtTime(Clock);

    private void RefreshAtTime(double now)
    {
        double elapsed = now - activationTime;
        if (!activationActive || elapsed < 0d)
            Phase = TrapPhase.Idle;
        else if (elapsed < activationDelay)
            Phase = TrapPhase.Warning;
        else if (elapsed < activationDelay + firingDuration)
            Phase = TrapPhase.Firing;
        else if (elapsed < activationDelay + firingDuration + cooldown)
            Phase = TrapPhase.Cooldown;
        else
            Phase = TrapPhase.Idle;

        if (!IsEmitting)
        {
            RestoreMovement();
            ResolvedLength = DamageLength = 0f;
            if (Phase == TrapPhase.Warning || elapsed < 0d) ClearVisuals();
            else RefreshVisualFade(now, activationActive ? activationTime + activationDelay + firingDuration : activationTime);
            return;
        }

        float firingElapsed = (float)(elapsed - activationDelay);
        float wave = Mathf.Sin(firingElapsed * Mathf.PI * 2f / movementPeriod);
        Vector3 angularOffset = angularAmplitude * wave;
        Vector3 positionOffset = positionAmplitude * wave;
        if (movementCaptured && capturedMovingPart != movingPart) RestoreMovement();
        if (movingPart != null)
        {
            CaptureMovement();
            movingPart.localPosition = movingBasePosition + positionOffset;
            movingPart.localRotation = movingBaseRotation * Quaternion.Euler(angularOffset);
            positionOffset = angularOffset = Vector3.zero;
        }
        Transform pivot = flamethrowerPivot != null ? flamethrowerPivot : transform;
        JetOrigin = pivot.TransformPoint(localPositionOffset + positionOffset);
        JetRotation = pivot.rotation * Quaternion.Euler(localEulerAngles) * Quaternion.Euler(angularOffset);
        ResolvedLength = ResolveObstructedLength(JetOrigin, JetDirection, range);
        DamageLength = ResolvedLength * Mathf.Clamp01(firingElapsed / fillTime);
        RenderJet();
    }

    private void ResolveReferences()
    {
        if (flamethrowerPivot == null)
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                if (child.name == "Flamethrower_Pivot") { flamethrowerPivot = child; break; }
        if (flameEffect == null) flameEffect = GetComponentInChildren<VisualEffect>(true);
        if (endLight == null) endLight = transform.Find(EndLightObjectName)?.GetComponent<Light>();
        if (flameAudioSource == null) flameAudioSource = transform.Find(FlameAudioObjectName)?.GetComponent<AudioSource>();
    }

    private void CaptureMovement()
    {
        if (movementCaptured || movingPart == null) return;
        movingBasePosition = movingPart.localPosition;
        movingBaseRotation = movingPart.localRotation;
        capturedMovingPart = movingPart;
        movementCaptured = true;
    }

    private void RestoreMovement()
    {
        if (!movementCaptured) return;
        if (capturedMovingPart != null)
        {
            capturedMovingPart.localPosition = movingBasePosition;
            capturedMovingPart.localRotation = movingBaseRotation;
        }
        capturedMovingPart = null;
        movementCaptured = false;
    }
}
