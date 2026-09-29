using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("Audio/Ambience/Ambient Zone")]
public sealed class AmbientZone : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string zoneName = "Ambient Zone";

    [Header("Profile")]
    [SerializeField] private AmbientAudioController controller;
    [SerializeField] private AmbientAudioProfile profile;
    [SerializeField] private int priority;
    [SerializeField] private bool overrideFadeTime;
    [SerializeField] [Min(0f)] private float fadeTime = 2f;

    [Header("Detection")]
    [SerializeField] private LayerMask playerDetectionMask = Physics.DefaultRaycastLayers;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging;

    private readonly Dictionary<int, int> localPlayerOverlapCounts = new Dictionary<int, int>();
    private Collider triggerCollider;
    private bool registeredWithController;
    private bool warnedMissingController;

    public string DisplayName => string.IsNullOrWhiteSpace(zoneName) ? gameObject.name : zoneName;
    public AmbientAudioProfile Profile => profile;
    public int Priority => priority;
    public bool HasFadeTimeOverride => overrideFadeTime;
    public float FadeTime => fadeTime;

    private void Reset()
    {
        EnsureTriggerCollider();
    }

    private void Awake()
    {
        EnsureTriggerCollider();
    }

    private void OnDisable()
    {
        if (registeredWithController)
            ResolveController()?.ExitZone(this);

        registeredWithController = false;
        localPlayerOverlapCounts.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!TryGetLocalPlayerKey(other, out int playerKey))
            return;

        localPlayerOverlapCounts.TryGetValue(playerKey, out int overlapCount);
        localPlayerOverlapCounts[playerKey] = overlapCount + 1;

        if (overlapCount == 0)
            RegisterZoneIfNeeded();
    }

    private void OnTriggerStay(Collider other)
    {
        if (!TryGetLocalPlayerKey(other, out int playerKey))
            return;

        if (localPlayerOverlapCounts.ContainsKey(playerKey))
            return;

        localPlayerOverlapCounts[playerKey] = 1;
        RegisterZoneIfNeeded();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryGetLocalPlayerKey(other, out int playerKey))
            return;

        if (!localPlayerOverlapCounts.TryGetValue(playerKey, out int overlapCount))
            return;

        overlapCount = Mathf.Max(0, overlapCount - 1);
        if (overlapCount > 0)
        {
            localPlayerOverlapCounts[playerKey] = overlapCount;
            return;
        }

        localPlayerOverlapCounts.Remove(playerKey);
        if (localPlayerOverlapCounts.Count == 0)
            UnregisterZoneIfNeeded();
    }

    private void RegisterZoneIfNeeded()
    {
        if (registeredWithController || profile == null)
            return;

        AmbientAudioController resolvedController = ResolveController();
        if (resolvedController == null)
        {
            if (!warnedMissingController)
            {
                Debug.LogWarning($"[AmbientZone] '{DisplayName}' nao encontrou AmbientAudioController na cena.", gameObject);
                warnedMissingController = true;
            }

            return;
        }

        resolvedController.EnterZone(this);
        registeredWithController = true;

        if (verboseLogging)
            Debug.Log($"[AmbientZone] Perfil '{profile.name}' ativo em '{DisplayName}'.", gameObject);
    }

    private void UnregisterZoneIfNeeded()
    {
        if (!registeredWithController)
            return;

        AmbientAudioController resolvedController = ResolveController();
        if (resolvedController != null)
            resolvedController.ExitZone(this);

        registeredWithController = false;
    }

    private AmbientAudioController ResolveController()
    {
        if (controller != null)
            return controller;

        AmbientAudioController[] controllers = FindObjectsByType<AmbientAudioController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < controllers.Length; i++)
        {
            AmbientAudioController candidate = controllers[i];
            if (candidate != null && candidate.gameObject.scene == gameObject.scene)
            {
                controller = candidate;
                return controller;
            }
        }

        return null;
    }

    private bool TryGetLocalPlayerKey(Collider other, out int playerKey)
    {
        playerKey = 0;

        if (other == null)
            return false;

        PlayerSetup playerSetup = other.GetComponentInParent<PlayerSetup>();
        if (playerSetup == null)
            return false;

        PhotonView playerPhotonView = playerSetup.GetComponent<PhotonView>();
        if (playerPhotonView != null && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && !playerPhotonView.IsMine)
            return false;

        int colliderLayerMaskBit = 1 << other.gameObject.layer;
        int playerLayerMaskBit = 1 << playerSetup.gameObject.layer;
        int layerMaskValue = playerDetectionMask.value;
        if ((layerMaskValue & colliderLayerMaskBit) == 0 && (layerMaskValue & playerLayerMaskBit) == 0)
            return false;

        playerKey = playerPhotonView != null && playerPhotonView.ViewID != 0
            ? playerPhotonView.ViewID
            : playerSetup.GetHashCode();
        return true;
    }

    private void EnsureTriggerCollider()
    {
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null && !triggerCollider.isTrigger)
            triggerCollider.isTrigger = true;
    }

    private void OnValidate()
    {
        fadeTime = Mathf.Max(0f, fadeTime);
        EnsureTriggerCollider();
    }

    private void OnDrawGizmosSelected()
    {
        Collider previewCollider = triggerCollider != null ? triggerCollider : GetComponent<Collider>();
        if (previewCollider == null)
            return;

        Bounds bounds = previewCollider.bounds;
        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.16f);
        Gizmos.DrawCube(bounds.center, bounds.size);
        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.85f);
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }
}
