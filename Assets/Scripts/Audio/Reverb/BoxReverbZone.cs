using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
[AddComponentMenu("Audio/Reverb/Box Reverb Zone")]
public sealed class BoxReverbZone : MonoBehaviour
{
    [Header("Reverb")]
    [SerializeField] private string zoneName = "Box Reverb Zone";
    [SerializeField] private AudioReverbPreset preset = AudioReverbPreset.Stoneroom;
    [SerializeField] [Range(0f, 1f)] private float wetScale = 1f;
    [SerializeField] private int priority;
    [SerializeField] [Min(0f)] private float transitionTime = 0.75f;

    [Header("Detection")]
    [SerializeField] private LayerMask playerDetectionMask = Physics.DefaultRaycastLayers;

    [Header("Debug Gizmos")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool drawOnlyWhenSelected = true;
    [SerializeField] private Color gizmoFillColor = new Color(0.25f, 0.9f, 1f, 0.14f);
    [SerializeField] private Color gizmoWireColor = new Color(0.25f, 0.9f, 1f, 0.9f);

    private readonly Dictionary<int, int> localPlayerOverlapCounts = new Dictionary<int, int>();
    private BoxReverbZoneManager manager;
    private bool registeredWithManager;

    public string DisplayName => string.IsNullOrWhiteSpace(zoneName) ? gameObject.name : zoneName;
    public AudioReverbPreset Preset => preset;
    public float WetScale => wetScale;
    public int Priority => priority;
    public float TransitionTime => transitionTime;
    public bool IsZoneActive => isActiveAndEnabled;

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
        UnregisterFromManager();
        localPlayerOverlapCounts.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!TryGetLocalPlayerKey(other, out int playerKey))
            return;

        localPlayerOverlapCounts.TryGetValue(playerKey, out int overlapCount);
        localPlayerOverlapCounts[playerKey] = overlapCount + 1;

        if (overlapCount == 0)
            RegisterWithManager();
    }

    private void OnTriggerStay(Collider other)
    {
        if (!TryGetLocalPlayerKey(other, out int playerKey))
            return;

        if (localPlayerOverlapCounts.ContainsKey(playerKey))
            return;

        localPlayerOverlapCounts[playerKey] = 1;
        RegisterWithManager();
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
            UnregisterFromManager();
    }

    private void OnValidate()
    {
        wetScale = Mathf.Clamp01(wetScale);
        transitionTime = Mathf.Max(0f, transitionTime);
        EnsureTriggerCollider();
    }

    private void RegisterWithManager()
    {
        if (registeredWithManager)
            return;

        manager = BoxReverbZoneManager.GetOrCreateForScene(gameObject);
        if (manager == null)
            return;

        manager.EnterZone(this);
        registeredWithManager = true;
    }

    private void UnregisterFromManager()
    {
        if (!registeredWithManager)
            return;

        if (manager != null)
            manager.ExitZone(this);

        registeredWithManager = false;
    }

    private bool TryGetLocalPlayerKey(Collider other, out int playerKey)
    {
        playerKey = 0;

        if (other == null)
            return false;

        PlayerSetup playerSetup = other.GetComponentInParent<PlayerSetup>();
        if (playerSetup == null)
            return false;

        PhotonView photonView = playerSetup.GetComponent<PhotonView>();
        if (photonView != null && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && !photonView.IsMine)
            return false;

        int colliderLayerMaskBit = 1 << other.gameObject.layer;
        int playerLayerMaskBit = 1 << playerSetup.gameObject.layer;
        int layerMaskValue = playerDetectionMask.value;
        if ((layerMaskValue & colliderLayerMaskBit) == 0 && (layerMaskValue & playerLayerMaskBit) == 0)
            return false;

        playerKey = photonView != null && photonView.ViewID != 0
            ? photonView.ViewID
            : playerSetup.GetHashCode();
        return true;
    }

    private void EnsureTriggerCollider()
    {
        BoxCollider boxCollider = GetComponent<BoxCollider>();
        if (boxCollider != null && !boxCollider.isTrigger)
            boxCollider.isTrigger = true;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || drawOnlyWhenSelected)
            return;

        DrawBoxGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !drawOnlyWhenSelected)
            return;

        DrawBoxGizmo();
    }

    private void DrawBoxGizmo()
    {
        BoxCollider boxCollider = GetComponent<BoxCollider>();
        if (boxCollider == null)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(boxCollider.transform.position, boxCollider.transform.rotation, boxCollider.transform.lossyScale);

        Gizmos.color = gizmoFillColor;
        Gizmos.DrawCube(boxCollider.center, boxCollider.size);
        Gizmos.color = gizmoWireColor;
        Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);

        Gizmos.matrix = previousMatrix;
    }
}
