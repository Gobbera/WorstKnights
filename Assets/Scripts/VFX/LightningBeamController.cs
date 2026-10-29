using System;
using UnityEngine;
using UnityEngine.VFX;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(VisualEffect))]
[AddComponentMenu("VFX/Lightning Beam Controller")]
public sealed class LightningBeamController : MonoBehaviour
{
    private static readonly int LengthId = Shader.PropertyToID("Lightning Length");
    private static readonly int OffsetId = Shader.PropertyToID("Impact Offset");
    private static readonly int WidthId = Shader.PropertyToID("Beam Width");
    private static readonly int ColorId = Shader.PropertyToID("Lightning Color");
    private static readonly int GlowColorId = Shader.PropertyToID("Color 01");
    private static readonly int GlowSizeId = Shader.PropertyToID("Glow Size");
    private static readonly int ImpactSizeId = Shader.PropertyToID("Impact Glow Size");
    private static readonly int NoiseScaleId = Shader.PropertyToID("Noise Scale");
    private static readonly int NoiseSpeedId = Shader.PropertyToID("Noise Speed");
    private static readonly int DistortionId = Shader.PropertyToID("Distortion Amount");
    private static readonly int BoundsCenterId = Shader.PropertyToID("Beam Bounds Center");
    private static readonly int BoundsSizeId = Shader.PropertyToID("Beam Bounds Size");

    [Header("Pontos do raio")]
    [Tooltip("Ponto A. Vazio usa a posição e direção deste objeto.")]
    [SerializeField] private Transform origin;
    [Tooltip("Ponto de mira. Vazio segue o eixo azul (Z) da origem.")]
    [SerializeField] private Transform aimTarget;
    [Tooltip("Ignora os colliders deste personagem.")]
    [SerializeField] private Transform ownerRoot;
    [SerializeField, Min(0.01f)] private float maxDistance = 50f;
    [SerializeField] private LayerMask hitMask = Physics.DefaultRaycastLayers;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Persistência")]
    [SerializeField] private bool playOnEnable = true;
    [Tooltip("Duração em segundos. Zero mantém o raio ligado até StopBeam ou desativar o objeto.")]
    [SerializeField, Min(0f)] private float duration;

    [Header("Aparência")]
    [Tooltip("Multiplicador da grossura, independente do comprimento e dos brilhos.")]
    [SerializeField, Min(0.01f)] private float width = 1f;
    [SerializeField, ColorUsage(false, true)] private Color beamColor = new Color(0.03f, 0.22f, 1f, 1f);
    [SerializeField, Min(0f)] private float intensity = 4f;
    [SerializeField, Min(0f)] private float glowSize = 2f;
    [SerializeField, Min(0f)] private float glowIntensity = 8f;
    [SerializeField] private bool showImpact = true;

    [Header("Ruído")]
    [SerializeField, Min(0.01f)] private float noiseScale = 25f;
    [SerializeField] private Vector2 noiseSpeed = new Vector2(0.8f, 0.5f);
    [SerializeField, Range(0f, 1f)] private float distortion = 0.2f;

    private VisualEffect effect;
    private RaycastHit[] hits = new RaycastHit[16];
    private bool visualActive;
    private bool externalSegment;
    private double startedAt;

    public bool IsPlaying { get; private set; }
    public Vector3 StartPoint { get; private set; }
    public Vector3 EndPoint { get; private set; }
    public bool HasHit { get; private set; }
    public RaycastHit LastHit { get; private set; }
    private double Clock => Application.IsPlaying(gameObject) ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;

    private void OnEnable()
    {
        if (Application.IsPlaying(gameObject) && playOnEnable) StartBeam();
    }

    private void OnValidate()
    {
        maxDistance = Mathf.Max(0.01f, maxDistance);
        duration = Mathf.Max(0f, duration);
        width = Mathf.Max(0.01f, width);
        intensity = Mathf.Max(0f, intensity);
        glowSize = Mathf.Max(0f, glowSize);
        glowIntensity = Mathf.Max(0f, glowIntensity);
        noiseScale = Mathf.Max(0.01f, noiseScale);
    }

    private void LateUpdate() { RefreshBeam(); }
    private void OnDisable() { StopBeam(); }

    public bool StartBeam(Transform source, Transform target, Transform owner = null)
    {
        origin = source;
        aimTarget = target;
        ownerRoot = owner;
        return StartBeam();
    }

    [ContextMenu("Iniciar raio contínuo")]
    public bool StartBeam()
    {
        if (!isActiveAndEnabled) return false;
        if (effect == null) effect = GetComponent<VisualEffect>();
        if (effect == null || effect.visualEffectAsset == null || !effect.HasFloat(WidthId)) return false;
        ClearVisual();
        externalSegment = false;
        IsPlaying = true;
        startedAt = Clock;
        RefreshBeam();
        return IsPlaying;
    }

    [ContextMenu("Parar raio contínuo")]
    public void StopBeam()
    {
        IsPlaying = false;
        externalSegment = false;
        HasHit = false;
        LastHit = default;
        ClearVisual();
    }

    public void RefreshBeam()
    {
        if (!IsPlaying) return;
        if (externalSegment) return;
        if (duration > 0f && Clock - startedAt >= duration)
        {
            StopBeam();
            return;
        }
        Transform source = origin != null ? origin : transform;
        Vector3 start = source.position;
        Vector3 offset = aimTarget != null ? aimTarget.position - start : source.forward * maxDistance;
        float distance = Mathf.Min(offset.magnitude, maxDistance);
        if (!IsFinite(start) || !IsFinite(offset) || distance <= 0.0001f)
        {
            HasHit = false;
            LastHit = default;
            ClearVisual();
            return;
        }

        Vector3 direction = offset.normalized;
        int count;
        while ((count = Physics.RaycastNonAlloc(start, direction, hits, distance, hitMask, triggerInteraction)) == hits.Length)
            Array.Resize(ref hits, hits.Length * 2);
        RaycastHit nearest = default;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Transform candidate = hits[i].collider.transform;
            if (candidate.IsChildOf(transform) || (ownerRoot != null && candidate.IsChildOf(ownerRoot))) continue;
            if (hits[i].distance >= nearestDistance) continue;
            nearestDistance = hits[i].distance;
            nearest = hits[i];
        }
        LastHit = nearest;
        HasHit = nearest.collider != null;
        RenderSegment(start, HasHit ? nearest.point : start + direction * distance);
    }

    public void SetPlayOnEnable(bool value) { playOnEnable = value; }

    /// <summary>Apresenta o segmento resolvido pelo ataque; não faz raycast nem controla a duração.</summary>
    public void PlayBetween(Vector3 start, Vector3 end, bool hasImpact)
    {
        if (!isActiveAndEnabled) return;
        if (effect == null) effect = GetComponent<VisualEffect>();
        if (effect == null || effect.visualEffectAsset == null) return;
        externalSegment = true;
        IsPlaying = true;
        HasHit = hasImpact;
        LastHit = default;
        RenderSegment(start, end);
    }

    private void RenderSegment(Vector3 start, Vector3 end)
    {
        StartPoint = start;
        EndPoint = end;
        Vector3 offset = end - start;
        if (!IsFinite(start) || !IsFinite(end) || offset.sqrMagnitude < 0.00000001f)
        {
            ClearVisual();
            return;
        }
        Vector3 direction = offset.normalized;
        Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up;
        transform.SetPositionAndRotation(start, Quaternion.LookRotation(direction, up));
        float length = transform.InverseTransformPoint(EndPoint).z;
        ApplyProperties(length);
        if (!visualActive)
        {
            // The initial OnStop clears old particles; Play emits only one persistent set.
            effect.initialEventName = "OnStop";
            effect.pause = false;
            effect.Reinit();
            effect.Play();
            visualActive = true;
        }
    }

    private void ApplyProperties(float length)
    {
        SetFloat(LengthId, length);
        SetFloat(OffsetId, length);
        SetFloat(WidthId, Mathf.Max(0.01f, width));
        SetFloat(GlowSizeId, glowSize);
        SetFloat(ImpactSizeId, showImpact && HasHit ? glowSize : 0f);
        SetFloat(NoiseScaleId, noiseScale);
        SetFloat(DistortionId, distortion);
        if (effect.HasVector2(NoiseSpeedId)) effect.SetVector2(NoiseSpeedId, noiseSpeed);
        if (effect.HasVector4(ColorId)) effect.SetVector4(ColorId, beamColor * intensity);
        if (effect.HasVector4(GlowColorId)) effect.SetVector4(GlowColorId, beamColor * glowIntensity);
        float padding = Mathf.Max(6f, Mathf.Max(width, glowSize));
        if (effect.HasVector3(BoundsCenterId)) effect.SetVector3(BoundsCenterId, Vector3.forward * (length * 0.5f));
        if (effect.HasVector3(BoundsSizeId)) effect.SetVector3(BoundsSizeId, new Vector3(10f + padding * 2f, 10f + padding * 2f, length + padding * 2f));
    }

    private void SetFloat(int id, float value) { if (effect.HasFloat(id)) effect.SetFloat(id, value); }

    private void ClearVisual()
    {
        if (effect == null || !visualActive) return;
        effect.initialEventName = "OnStop";
        effect.Reinit();
        visualActive = false;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !(float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y)
            || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));
    }
}
