using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class RoomVoiceSettingsPanel : MonoBehaviour
{
    private const string ResourcePath = "UI/RoomVoiceSettingsPanel";
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private RectTransform content;
    [SerializeField] private VoiceParticipantRow rowTemplate;
    [SerializeField] private GameObject emptyMessage;
    [SerializeField] private ScrollRect scroll;

    private readonly List<RoomVoiceParticipant> participants = new List<RoomVoiceParticipant>();
    private readonly Dictionary<int, VoiceParticipantRow> rows = new Dictionary<int, VoiceParticipantRow>();
    private readonly HashSet<int> seen = new HashSet<int>();
    private readonly List<int> removed = new List<int>();
    private float nextRefresh;

    public static RoomVoiceSettingsPanel EnsureForMenu(SettingsMenu menu)
    {
        var existing = menu.GetComponentInChildren<RoomVoiceSettingsPanel>(true);
        if (existing != null) return existing;
        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"RoomVoiceSettingsPanel: prefab 'Resources/{ResourcePath}' not found.");
            return null;
        }
        Transform parent = menu.resolutionDropdown != null ? menu.resolutionDropdown.transform.parent.parent : menu.transform;
        float lowest = -502f;
        foreach (Transform child in parent)
            if (child is RectTransform row && child.name.StartsWith("TXT_"))
                lowest = Mathf.Min(lowest, row.anchoredPosition.y);
        var instance = Instantiate(prefab, parent, false);
        instance.name = "Room Voice Settings";
        var rect = (RectTransform)instance.transform;
        rect.anchoredPosition = new Vector2(0f, lowest - 55f);
        return instance.GetComponent<RoomVoiceSettingsPanel>();
    }

    private void OnEnable() { RefreshForRoom(); }

    private void Update()
    {
        var overlay = GetComponentInParent<InGameSettingsMenuController>();
        if (overlay != null && !overlay.IsOpen) return;
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        RefreshForRoom();
    }

    public void RefreshForRoom()
    {
        if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
        bool inRoom = PhotonNetwork.InRoom;
        panelGroup.alpha = inRoom ? 1f : 0f;
        panelGroup.interactable = panelGroup.blocksRaycasts = inRoom;
        participants.Clear();
        if (inRoom && VivoxVoiceManager.Instance != null)
            VivoxVoiceManager.Instance.CopyRoomVoiceParticipants(participants);
        PresentParticipants(participants);
    }

    public static void RefreshForMenu(GameObject menu)
    {
        foreach (var panel in menu.GetComponentsInChildren<RoomVoiceSettingsPanel>(true)) panel.RefreshForRoom();
    }

    private void PresentParticipants(IReadOnlyList<RoomVoiceParticipant> entries)
    {
        seen.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            seen.Add(entry.ActorNumber);
            if (!rows.TryGetValue(entry.ActorNumber, out VoiceParticipantRow row))
            {
                row = Instantiate(rowTemplate, content, false);
                row.name = $"Voice Player {entry.ActorNumber}";
                rows.Add(entry.ActorNumber, row);
            }
            row.Bind(entry, SetVolume, SetMuted);
            row.gameObject.SetActive(true);
            if (row.transform.GetSiblingIndex() != i + 1) row.transform.SetSiblingIndex(i + 1);
        }
        removed.Clear();
        foreach (var pair in rows) if (!seen.Contains(pair.Key)) removed.Add(pair.Key);
        foreach (int actor in removed)
        {
            GameObject root = rows[actor].gameObject;
            root.SetActive(false);
            if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
            rows.Remove(actor);
        }
        emptyMessage.SetActive(entries.Count == 0);
        scroll.gameObject.SetActive(entries.Count > 0);
        if (scroll.verticalScrollbar != null) scroll.verticalScrollbar.gameObject.SetActive(entries.Count > 3);
    }

    private void SetVolume(int actor, float value)
    {
        VivoxVoiceManager.Instance?.SetParticipantVolume(actor, value);
    }

    private void SetMuted(int actor, bool muted)
    {
        VivoxVoiceManager.Instance?.SetParticipantMuted(actor, muted);
        RefreshForRoom();
    }
}
