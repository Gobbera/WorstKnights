using UnityEngine;
using UnityEngine.VFX;

[DisallowMultipleComponent]
[RequireComponent(typeof(VisualEffect))]
[AddComponentMenu("VFX/Lightning Strike Controller")]
public sealed class LightningStrikeController : MonoBehaviour
{
    private static readonly int LengthId = Shader.PropertyToID("Lightning Length");
    private static readonly int ImpactOffsetId = Shader.PropertyToID("Impact Offset");
    private static readonly int BoundsCenterId = Shader.PropertyToID("Beam Bounds Center");
    private static readonly int BoundsSizeId = Shader.PropertyToID("Beam Bounds Size");
    private static readonly int HasImpactId = Shader.PropertyToID("Has Impact");

    [Header("Disparo")]
    [Tooltip("Ponto A. Se vazio, usa a posicao deste objeto.")]
    [SerializeField] private Transform origin;
    [Tooltip("Alvo opcional para mirar. O primeiro obstaculo antes dele interrompe o raio.")]
    [SerializeField] private Transform aimTarget;
    [Tooltip("Raiz do personagem que dispara. Seus colliders serao ignorados.")]
    [SerializeField] private Transform ownerRoot;
    [SerializeField, Min(0.01f)] private float maxDistance = 50f;
    [SerializeField] private LayerMask hitMask = Physics.DefaultRaycastLayers;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Reproducao")]
    [SerializeField] private bool playOnEnable = true;
    [Tooltip("Tempo para destruir a instancia depois de um disparo. Zero permite reutiliza-la.")]
    [SerializeField, Min(0f)] private float destroyAfterSeconds;

    [Header("Visual")]
    [Tooltip("Margem local dos bounds para flashes, distorcao e particulas de impacto.")]
    [SerializeField, Min(3f)] private float boundsPadding = 6f;

    private VisualEffect visualEffect;

    public Vector3 StartPoint { get; private set; }
    public Vector3 EndPoint { get; private set; }
    public bool HasHit { get; private set; }
    public RaycastHit LastHit { get; private set; }

    public void SetPlayOnEnable(bool value)
    {
        playOnEnable = value;
    }

    private void Awake()
    {
        visualEffect = GetComponent<VisualEffect>();
    }

    private void OnEnable()
    {
        if (playOnEnable)
            Fire();
    }

    private void OnValidate()
    {
        maxDistance = Mathf.Max(0.01f, maxDistance);
        destroyAfterSeconds = Mathf.Max(0f, destroyAfterSeconds);
        boundsPadding = Mathf.Max(3f, boundsPadding);
    }

    /// <summary>Dispara usando Origin, Aim Target (ou o forward de Origin) e o alcance do Inspector.</summary>
    [ContextMenu("Disparar Lightning Strike")]
    public void Fire()
    {
        Transform source = origin != null ? origin : transform;
        if (aimTarget != null)
            FireAt(source.position, aimTarget.position, out _, ownerRoot);
        else
            FireFrom(source.position, source.forward, out _, ownerRoot);
    }

    /// <summary>Disparo instantaneo ate o primeiro collider valido ou o alcance maximo.</summary>
    public bool FireFrom(Vector3 worldOrigin, Vector3 direction, out RaycastHit hit, Transform owner = null)
    {
        return CastAndPlay(worldOrigin, direction, maxDistance, owner, out hit);
    }

    /// <summary>Mira em um ponto, respeitando obstaculos e o alcance maximo.</summary>
    public bool FireAt(Vector3 worldOrigin, Vector3 targetPoint, out RaycastHit hit, Transform owner = null)
    {
        Vector3 offset = targetPoint - worldOrigin;
        return CastAndPlay(worldOrigin, offset, Mathf.Min(offset.magnitude, maxDistance), owner, out hit);
    }

    /// <summary>Reproduz um segmento ja resolvido, por exemplo em um cliente remoto.</summary>
    public void PlayBetween(Vector3 worldOrigin, Vector3 worldEnd, bool showImpact = true)
    {
        HasHit = false;
        LastHit = default;
        ApplySegment(worldOrigin, worldEnd, showImpact);
    }

    private bool CastAndPlay(Vector3 worldOrigin, Vector3 direction, float distance, Transform owner, out RaycastHit hit)
    {
        hit = default;
        HasHit = false;
        LastHit = default;
        if (!IsFinite(worldOrigin) || !IsFinite(direction) || direction.sqrMagnitude < 0.000001f
            || float.IsNaN(distance) || float.IsInfinity(distance) || distance <= 0f)
            return false;

        direction.Normalize();
        Transform ignoredRoot = owner != null ? owner : ownerRoot;
        RaycastHit[] hits = Physics.RaycastAll(worldOrigin, direction, distance, hitMask, triggerInteraction);
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform candidate = hits[i].collider.transform;
            if (candidate.IsChildOf(transform) || (ignoredRoot != null && candidate.IsChildOf(ignoredRoot)))
                continue;

            // RaycastAll nao garante ordem; a oclusao depende da menor distancia valida.
            if (hits[i].distance < nearestDistance)
            {
                nearestDistance = hits[i].distance;
                hit = hits[i];
            }
        }

        HasHit = hit.collider != null;
        LastHit = hit;
        ApplySegment(worldOrigin, HasHit ? hit.point : worldOrigin + direction * distance, HasHit);
        return HasHit;
    }

    private void ApplySegment(Vector3 worldOrigin, Vector3 worldEnd, bool showImpact)
    {
        if (!IsFinite(worldOrigin) || !IsFinite(worldEnd))
            return;

        Vector3 offset = worldEnd - worldOrigin;
        if (offset.sqrMagnitude < 0.000001f)
            return;

        if (visualEffect == null)
            visualEffect = GetComponent<VisualEffect>();
        if (visualEffect == null || visualEffect.visualEffectAsset == null)
            return;

        StartPoint = worldOrigin;
        EndPoint = worldEnd;
        Vector3 direction = offset.normalized;
        Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up;
        transform.SetPositionAndRotation(worldOrigin, Quaternion.LookRotation(direction, up));

        // Only length changes. Transform scale would also stretch flashes and impact particles.
        float localLength = transform.InverseTransformPoint(worldEnd).z;
        if (localLength <= 0f)
            return;

        SetFloat(LengthId, localLength);
        SetFloat(ImpactOffsetId, localLength);
        if (visualEffect.HasBool(HasImpactId))
            visualEffect.SetBool(HasImpactId, showImpact);
        if (visualEffect.HasVector3(BoundsCenterId))
            visualEffect.SetVector3(BoundsCenterId, Vector3.forward * (localLength * 0.5f));
        if (visualEffect.HasVector3(BoundsSizeId))
            visualEffect.SetVector3(BoundsSizeId, new Vector3(10f + boundsPadding * 2f,
                10f + boundsPadding * 2f, localLength + boundsPadding * 2f));

        // Reinit sends the initial event once, resetting the old shot without a second Play event.
        visualEffect.initialEventName = "OnPlay";
        visualEffect.pause = false;
        visualEffect.Reinit();

        CancelInvoke(nameof(DestroyAfterShot));
        if (Application.isPlaying && destroyAfterSeconds > 0f)
            Invoke(nameof(DestroyAfterShot), destroyAfterSeconds);
    }

    private void SetFloat(int property, float value)
    {
        if (visualEffect.HasFloat(property))
            visualEffect.SetFloat(property, value);
    }

    private void DestroyAfterShot()
    {
        Destroy(gameObject);
    }

    private static bool IsFinite(Vector3 value)
    {
        return !(float.IsNaN(value.x) || float.IsInfinity(value.x)
            || float.IsNaN(value.y) || float.IsInfinity(value.y)
            || float.IsNaN(value.z) || float.IsInfinity(value.z));
    }

    private void OnDrawGizmosSelected()
    {
        Transform source = origin != null ? origin : transform;
        Vector3 offset = aimTarget != null ? aimTarget.position - source.position : source.forward * maxDistance;
        Vector3 end = source.position + Vector3.ClampMagnitude(offset, maxDistance);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(source.position, end);
        Gizmos.DrawWireSphere(source.position, 0.1f);
        Gizmos.DrawWireSphere(end, 0.1f);
    }
}
