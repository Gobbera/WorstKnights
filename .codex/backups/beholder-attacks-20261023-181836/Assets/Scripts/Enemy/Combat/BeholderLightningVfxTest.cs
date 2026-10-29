using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyVision))]
[DefaultExecutionOrder(100)]
[AddComponentMenu("Enemy/Beholder Lightning VFX Test")]
public sealed class BeholderLightningVfxTest : MonoBehaviour
{
    private const string EyeName = "Eye_03.L.Mesh";

    [Header("Teste visual")]
    [SerializeField] private bool enableTest = true;
    [SerializeField, Min(0.1f)] private float cooldown = 10f;
    [SerializeField, Min(0.6f)] private float effectLifetime = 2f;

    [Header("References")]
    [SerializeField] private Transform eyeOrigin;
    [SerializeField] private LightningStrikeController lightningPrefab;
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyVision enemyVision;
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyHealth enemyHealth;

    private PhotonView photonView;
    private Renderer eyeRenderer;
    private Mesh bakedEyeMesh;
    private LightningStrikeController activeEffect;
    private float nextShotTime;
    private float hideEffectTime;

    public Transform EyeOrigin => eyeOrigin;
    public Vector3 EyePosition => ResolveEyePosition();
    public LightningStrikeController ActiveEffect => activeEffect;
    public int ShotSequence { get; private set; }
    public float CooldownRemaining => Mathf.Max(0f, nextShotTime - Time.time);

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        cooldown = Mathf.Max(0.1f, cooldown);
        effectLifetime = Mathf.Max(0.6f, effectLifetime);
    }

    private void LateUpdate()
    {
        if (activeEffect != null && activeEffect.gameObject.activeSelf && Time.time >= hideEffectTime)
            activeEffect.gameObject.SetActive(false);

        TryFireVisibleTarget(Time.time);
    }

    [ContextMenu("Testar raio (respeita visao e cooldown)")]
    public void FireIfPlayerVisible()
    {
        TryFireVisibleTarget(Time.time);
    }

    private bool TryFireVisibleTarget(float now)
    {
        if (!enableTest || !isActiveAndEnabled || now < nextShotTime)
            return false;

        ResolveReferences();
        if ((enemySetup != null && !enemySetup.HasAuthority)
            || (enemyHealth != null && !enemyHealth.IsAlive)
            || eyeOrigin == null || lightningPrefab == null || enemyVision == null)
            return false;

        PlayerHealth preferredTarget = enemyBrain != null ? enemyBrain.CurrentTarget : null;
        if (!enemyVision.TryGetVisibleTarget(preferredTarget, out PlayerHealth target, out _)
            || !enemyVision.CanSeeTarget(target, out Vector3 targetPosition))
            return false;

        EnsureEffectInstance();
        activeEffect.gameObject.SetActive(true);
        Vector3 shotOrigin = ResolveEyePosition();
        activeEffect.FireFrom(shotOrigin, targetPosition - shotOrigin, out _, transform);
        nextShotTime = now + Mathf.Max(0.1f, cooldown);
        hideEffectTime = Time.time + Mathf.Max(0.6f, effectLifetime);
        ShotSequence++;

        if (PhotonNetwork.InRoom && photonView != null && photonView.ViewID != 0)
            photonView.RPC(nameof(RpcPlayLightningVfx), RpcTarget.Others,
                activeEffect.StartPoint, activeEffect.EndPoint, activeEffect.HasHit);

        return true;
    }

    private void EnsureEffectInstance()
    {
        if (activeEffect != null)
            return;

        // Configure an inactive clone before enabling it, so the prefab cannot fire on its own.
        GameObject staging = new GameObject("Lightning staging");
        staging.SetActive(false);
        activeEffect = Instantiate(lightningPrefab, staging.transform);
        activeEffect.name = "Beholder Lightning Strike (VFX)";
        activeEffect.gameObject.SetActive(false);
        activeEffect.SetPlayOnEnable(false);
        activeEffect.transform.SetParent(null, false);
        activeEffect.transform.localScale = Vector3.one;
        ReleaseObject(staging);
    }

    [PunRPC]
    private void RpcPlayLightningVfx(Vector3 start, Vector3 end, bool hasImpact, PhotonMessageInfo info)
    {
        ResolveReferences();
        if (!enableTest || !isActiveAndEnabled || lightningPrefab == null || photonView == null)
            return;

        var authority = photonView.Owner ?? PhotonNetwork.MasterClient;
        if (info.Sender != authority)
            return;

        EnsureEffectInstance();
        activeEffect.gameObject.SetActive(true);
        activeEffect.PlayBetween(start, end, hasImpact);
        hideEffectTime = Time.time + Mathf.Max(0.6f, effectLifetime);
        ShotSequence++;
    }

    private void ResolveReferences()
    {
        if (enemySetup == null) enemySetup = GetComponent<EnemySetup>();
        if (enemyVision == null) enemyVision = GetComponent<EnemyVision>();
        if (enemyBrain == null) enemyBrain = GetComponent<EnemyBrain>();
        if (enemyHealth == null) enemyHealth = GetComponent<EnemyHealth>();
        if (photonView == null) photonView = GetComponent<PhotonView>();
        if (eyeOrigin != null) return;

        foreach (Transform candidate in GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name != EyeName) continue;
            eyeOrigin = candidate;
            break;
        }
    }

    private Vector3 ResolveEyePosition()
    {
        if (eyeOrigin == null) return transform.position;
        if (eyeRenderer == null || eyeRenderer.transform != eyeOrigin)
            eyeRenderer = eyeOrigin.GetComponent<Renderer>();

        // The named eye is a skinned mesh with its Transform at the model origin.
        // Bake only when shooting to locate the animated eye itself, including tentacle motion.
        if (eyeRenderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
        {
            if (bakedEyeMesh == null) bakedEyeMesh = new Mesh { name = "Lightning eye origin" };
            // Imported eye transforms have scale 100. Compensate before TransformPoint applies it.
            skin.BakeMesh(bakedEyeMesh, true);
            return eyeOrigin.TransformPoint(bakedEyeMesh.bounds.center);
        }
        return eyeRenderer != null ? eyeRenderer.bounds.center : eyeOrigin.position;
    }

    private void OnDisable()
    {
        if (activeEffect != null)
            activeEffect.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (activeEffect != null)
            ReleaseObject(activeEffect.gameObject);
        if (bakedEyeMesh != null)
            ReleaseObject(bakedEyeMesh);
    }

    private static void ReleaseObject(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
