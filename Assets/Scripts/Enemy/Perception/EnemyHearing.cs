using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyHearing : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;

    [Header("Hearing")]
    [SerializeField] private bool enableHearing = true;
    [SerializeField] [Min(0f)] private float hearingMultiplier = 1f;
    [SerializeField] [Min(0f)] private float minimumAudibleIntensity = 0.05f;

    [Header("Player Movement")]
    [Tooltip("Checks the player's movement radius directly; Hearing Multiplier only affects NoiseSystem events.")]
    [SerializeField] private bool detectPlayerMovement;
    [SerializeField] [Min(0.05f)] private float playerMovementScanInterval = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool drawHearingGizmo = true;
    [SerializeField] [Min(0f)] private float previewHearingRadius = 12f;
    [SerializeField] private Color hearingGizmoColor = new Color(0.2f, 0.65f, 1f, 0.25f);

    private NoiseEvent pendingNoise;
    private bool hasPendingNoise;
    private bool pendingFromPlayerMovement;
    private float nextPlayerMovementScanTime;
    private Collider bodyCollider;

    public bool EnableHearing => enableHearing;
    public float HearingMultiplier => Mathf.Max(0f, hearingMultiplier);
    public bool HasPendingNoise => hasPendingNoise;
    public NoiseEvent PendingNoise => pendingNoise;
    public event Action<PlayerHealth, Vector3> PlayerHeard;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        NoiseSystem.NoiseEmitted += HandleNoiseEmitted;
    }

    private void OnDisable()
    {
        NoiseSystem.NoiseEmitted -= HandleNoiseEmitted;
        hasPendingNoise = false;
        pendingFromPlayerMovement = false;
    }

    public bool TryConsumeNoise(out NoiseEvent noiseEvent)
    {
        noiseEvent = pendingNoise;
        if (!hasPendingNoise)
            return false;

        hasPendingNoise = false;
        pendingFromPlayerMovement = false;
        return true;
    }

    public void ClearPendingPlayerMovementNoise()
    {
        if (pendingFromPlayerMovement)
        {
            hasPendingNoise = false;
            pendingFromPlayerMovement = false;
        }
    }

    public void ScanMovingPlayers()
    {
        if (!enableHearing || !detectPlayerMovement || !isActiveAndEnabled
            || (enemySetup != null && !enemySetup.HasAuthority)
            || Time.time < nextPlayerMovementScanTime)
            return;

        nextPlayerMovementScanTime = Time.time + Mathf.Max(0.05f, playerMovementScanInterval);
        EnemyTargetRegistry.PruneInvalidTargets();
        if (EnemyTargetRegistry.Count == 0)
            EnemyTargetRegistry.RefreshFromScene();

        PlayerHealth nearestPlayer = null;
        PlayerMovementNoiseSource nearestSource = null;
        float nearestDistanceSqr = float.PositiveInfinity;
        float nearestRadius = 0f;
        Collider listenerBody = ResolveBodyCollider();

        for (int i = 0; i < EnemyTargetRegistry.Count; i++)
        {
            PlayerHealth player = EnemyTargetRegistry.GetTarget(i);
            if (player == null || !player.CanBeTargetedByEnemy)
                continue;

            PlayerMovementNoiseSource source = player.GetComponent<PlayerMovementNoiseSource>();
            if (source == null || !source.isActiveAndEnabled)
                continue;

            float radius = source.CurrentRadius;
            if (radius <= 0f)
                continue;

            Collider playerBody = player.GetComponent<CapsuleCollider>();
            float distanceSqr;
            if (listenerBody != null && playerBody != null && playerBody.enabled)
            {
                float gap = PlanarBodyGap(listenerBody.bounds, playerBody.bounds);
                distanceSqr = gap * gap;
            }
            else
            {
                Vector3 planarOffset = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up);
                distanceSqr = planarOffset.sqrMagnitude;
            }
            if (distanceSqr > radius * radius)
                continue;

            PlayerHeard?.Invoke(player, player.transform.position);
            if (distanceSqr >= nearestDistanceSqr)
                continue;

            nearestPlayer = player;
            nearestSource = source;
            nearestDistanceSqr = distanceSqr;
            nearestRadius = radius;
        }

        if (nearestPlayer != null)
            RegisterPendingNoise(new NoiseEvent(nearestPlayer.transform.position, nearestRadius, 1f,
                nearestSource.CurrentNoiseType, nearestPlayer.gameObject), fromPlayerMovement: true);
    }

    public static float PlanarBodyGap(Bounds a, Bounds b)
    {
        float x = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
        float z = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
        return Mathf.Sqrt(x * x + z * z);
    }

    public bool CanHear(NoiseEvent noiseEvent)
    {
        if (!enableHearing || noiseEvent.Intensity < minimumAudibleIntensity)
            return false;

        float audibleRadius = noiseEvent.Radius * Mathf.Max(0f, noiseEvent.Intensity) * HearingMultiplier;
        if (audibleRadius <= 0.0001f)
            return false;

        Vector3 planarOffset = Vector3.ProjectOnPlane(noiseEvent.Position - transform.position, Vector3.up);
        return planarOffset.sqrMagnitude <= audibleRadius * audibleRadius;
    }

    private void HandleNoiseEmitted(NoiseEvent noiseEvent)
    {
        if (!CanHear(noiseEvent) || (enemySetup != null && !enemySetup.HasAuthority))
            return;

        PlayerHealth player = noiseEvent.Source != null
            ? noiseEvent.Source.GetComponentInParent<PlayerHealth>() : null;
        if (player != null && player.CanBeTargetedByEnemy)
            PlayerHeard?.Invoke(player, noiseEvent.Position);

        RegisterPendingNoise(noiseEvent);
    }

    private void RegisterPendingNoise(NoiseEvent noiseEvent, bool fromPlayerMovement = false)
    {
        if (!hasPendingNoise)
        {
            pendingNoise = noiseEvent;
            hasPendingNoise = true;
            pendingFromPlayerMovement = fromPlayerMovement;
            return;
        }

        bool incomingFromPlayer = noiseEvent.Source != null
            && noiseEvent.Source.GetComponentInParent<PlayerHealth>() != null;
        bool pendingFromPlayer = pendingNoise.Source != null
            && pendingNoise.Source.GetComponentInParent<PlayerHealth>() != null;
        if (incomingFromPlayer != pendingFromPlayer)
        {
            if (incomingFromPlayer)
            {
                pendingNoise = noiseEvent;
                pendingFromPlayerMovement = fromPlayerMovement;
            }
            return;
        }

        float currentDistanceSqr = Vector3.ProjectOnPlane(pendingNoise.Position - transform.position, Vector3.up).sqrMagnitude;
        float candidateDistanceSqr = Vector3.ProjectOnPlane(noiseEvent.Position - transform.position, Vector3.up).sqrMagnitude;
        if (noiseEvent.Intensity > pendingNoise.Intensity || candidateDistanceSqr < currentDistanceSqr)
        {
            pendingNoise = noiseEvent;
            pendingFromPlayerMovement = fromPlayerMovement;
        }
    }

    private void ResolveReferences()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();
    }

    private Collider ResolveBodyCollider()
    {
        if (bodyCollider != null && bodyCollider.enabled && !bodyCollider.isTrigger)
            return bodyCollider;

        foreach (Collider candidate in GetComponents<Collider>())
        {
            if (candidate != null && candidate.enabled && !candidate.isTrigger)
            {
                bodyCollider = candidate;
                return bodyCollider;
            }
        }

        bodyCollider = null;
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawHearingGizmo || !enableHearing)
            return;

        Gizmos.color = hearingGizmoColor;
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, previewHearingRadius) * HearingMultiplier);
    }
}
