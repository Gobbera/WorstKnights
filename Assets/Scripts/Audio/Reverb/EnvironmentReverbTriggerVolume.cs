using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class EnvironmentReverbTriggerVolume : MonoBehaviour, IEnvironmentReverbSource
{
    [Header("Identity")]
    [SerializeField] private string volumeName = "Reverb Volume";

    [Header("Profile")]
    [SerializeField] private EnvironmentReverbManager manager;
    [SerializeField] protected EnvironmentReverbProfile profile;
    [SerializeField] private int priority;
    [SerializeField] private bool overrideTransitionTime;
    [SerializeField] [Min(0f)] private float transitionTime = 1f;

    [Header("Detection")]
    [SerializeField] private LayerMask playerDetectionMask = Physics.DefaultRaycastLayers;

    [Header("Debug Gizmos")]
    [SerializeField] private bool drawGizmosWhenSelected = true;
    [SerializeField] private Color gizmoFillColor = new Color(0.25f, 0.9f, 1f, 0.16f);
    [SerializeField] private Color gizmoWireColor = new Color(0.25f, 0.9f, 1f, 0.9f);

    private readonly Dictionary<int, int> localPlayerOverlapCounts = new Dictionary<int, int>();
    private bool registeredWithManager;
    private bool warnedMissingManager;

    public string DisplayName => string.IsNullOrWhiteSpace(volumeName) ? gameObject.name : volumeName;
    public virtual EnvironmentReverbProfile Profile => profile;
    public int Priority => priority;
    public bool HasTransitionTimeOverride => overrideTransitionTime;
    public float TransitionTime => transitionTime;
    public virtual bool IsReverbSourceActive => isActiveAndEnabled && Profile != null;

    protected virtual void Reset()
    {
        EnsureTriggerColliders();
    }

    protected virtual void Awake()
    {
        EnsureTriggerColliders();
    }

    protected virtual void OnDisable()
    {
        UnregisterFromManager();
        localPlayerOverlapCounts.Clear();
    }

    protected virtual void OnTriggerEnter(Collider other)
    {
        if (!TryGetLocalPlayerKey(other, out int playerKey))
            return;

        localPlayerOverlapCounts.TryGetValue(playerKey, out int overlapCount);
        localPlayerOverlapCounts[playerKey] = overlapCount + 1;

        if (overlapCount == 0)
            RegisterWithManager();
    }

    protected virtual void OnTriggerStay(Collider other)
    {
        if (!TryGetLocalPlayerKey(other, out int playerKey))
            return;

        if (localPlayerOverlapCounts.ContainsKey(playerKey))
            return;

        localPlayerOverlapCounts[playerKey] = 1;
        RegisterWithManager();
    }

    protected virtual void OnTriggerExit(Collider other)
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
            UnregisterFromManager();
    }

    protected virtual void OnValidate()
    {
        transitionTime = Mathf.Max(0f, transitionTime);
        EnsureTriggerColliders();
    }

    protected void RegisterWithManager()
    {
        if (registeredWithManager || Profile == null)
            return;

        EnvironmentReverbManager resolvedManager = ResolveManager();
        if (resolvedManager == null)
        {
            WarnMissingManagerOnce();
            return;
        }

        resolvedManager.EnterSource(this);
        registeredWithManager = true;
    }

    protected void UnregisterFromManager()
    {
        if (!registeredWithManager)
            return;

        EnvironmentReverbManager resolvedManager = ResolveManager();
        if (resolvedManager != null)
            resolvedManager.ExitSource(this);

        registeredWithManager = false;
    }

    protected EnvironmentReverbManager ResolveManager()
    {
        if (manager != null)
            return manager;

        EnvironmentReverbManager[] managers = FindObjectsByType<EnvironmentReverbManager>(FindObjectsInactive.Exclude);
        for (int i = 0; i < managers.Length; i++)
        {
            EnvironmentReverbManager candidate = managers[i];
            if (candidate != null && candidate.gameObject.scene == gameObject.scene)
            {
                manager = candidate;
                return manager;
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

    private void EnsureTriggerColliders()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider volumeCollider = colliders[i];
            if (volumeCollider != null && !volumeCollider.isTrigger)
                volumeCollider.isTrigger = true;
        }
    }

    private void WarnMissingManagerOnce()
    {
        if (warnedMissingManager)
            return;

        warnedMissingManager = true;
        Debug.LogWarning($"[EnvironmentReverbTriggerVolume] '{DisplayName}' nao encontrou EnvironmentReverbManager na cena.", gameObject);
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (!drawGizmosWhenSelected)
            return;

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider volumeCollider = colliders[i];
            if (volumeCollider == null)
                continue;

            Bounds bounds = volumeCollider.bounds;
            Gizmos.color = gizmoFillColor;
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = gizmoWireColor;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
