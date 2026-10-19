using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Loot/Loot Spawner")]
public sealed class LootSpawner : MonoBehaviourPunCallbacks
{
    [Serializable]
    public sealed class LootOption
    {
        public WorldPickupItem prefab;
        [Range(0f, 100f)] public float chancePercent = 100f;
    }

    [Serializable]
    public sealed class SpawnSlot
    {
        public Transform spawnPoint;
        public List<LootOption> options = new List<LootOption>();
    }

    [Serializable]
    public sealed class LootGroup
    {
        public DoorController door;
        public List<SpawnSlot> slots = new List<SpawnSlot>();
    }

    private sealed class PlacedPickup
    {
        public WorldPickupItem item;
        public Transform anchor;
        public Rigidbody rootBody;
        public int groupIndex;
    }

    private const string RoomKeyPrefix = "loot:";
    private const string ItemIdPrefix = "loot-item:";

    [SerializeField] private List<LootGroup> groups = new List<LootGroup>
    {
        new LootGroup { slots = new List<SpawnSlot> { new SpawnSlot() } }
    };

    private readonly List<PlacedPickup> placedPickups = new List<PlacedPickup>();
    private readonly HashSet<DoorController> subscribedDoors = new HashSet<DoorController>();
    private string roomKey;
    private string itemContainerId;
    private bool selectionApplied;

    private void Awake()
    {
        string sceneId = SceneNetworkStateIdUtility.BuildSceneObjectId(transform);
        roomKey = string.IsNullOrWhiteSpace(sceneId) ? string.Empty : RoomKeyPrefix + sceneId;
        itemContainerId = string.IsNullOrEmpty(roomKey) ? Guid.NewGuid().ToString("N") : roomKey;
        TryInitializeLoot();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        SubscribeDoors();
        RefreshAvailability();
    }

    public override void OnDisable()
    {
        foreach (DoorController door in subscribedDoors)
        {
            if (door == null)
                continue;

            door.OpenStateChanged -= HandleDoorStateChanged;
        }

        subscribedDoors.Clear();
        base.OnDisable();
    }

    private void Start()
    {
        TryInitializeLoot();
        RefreshAvailability();
    }

    private void LateUpdate()
    {
        for (int i = 0; i < placedPickups.Count; i++)
        {
            PlacedPickup placed = placedPickups[i];
            if (placed.item == null || placed.item.HasEverBeenEquipped || placed.anchor == null)
                continue;

            Vector3 position = placed.anchor.position;
            Quaternion rotation = placed.anchor.rotation;
            Transform itemTransform = placed.item.transform;
            if ((itemTransform.position - position).sqrMagnitude > 0.000001f
                || Quaternion.Angle(itemTransform.rotation, rotation) > 0.01f)
            {
                itemTransform.SetPositionAndRotation(position, rotation);
            }

            if (placed.rootBody != null && placed.rootBody.isKinematic
                && ((placed.rootBody.position - position).sqrMagnitude > 0.000001f
                    || Quaternion.Angle(placed.rootBody.rotation, rotation) > 0.01f))
            {
                placed.rootBody.position = position;
                placed.rootBody.rotation = rotation;
            }
        }
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        TryInitializeLoot();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        base.OnMasterClientSwitched(newMasterClient);
        if (PhotonNetwork.IsMasterClient)
            TryInitializeLoot();
    }

    public override void OnRoomPropertiesUpdate(Hashtable changedProperties)
    {
        base.OnRoomPropertiesUpdate(changedProperties);
        if (selectionApplied || string.IsNullOrEmpty(roomKey) || changedProperties == null)
            return;

        if (changedProperties.TryGetValue(roomKey, out object value))
            ApplySelection(value);
    }

    public static int SelectOptionIndex(IReadOnlyList<LootOption> options, float unitRoll)
    {
        float total = TotalChance(options);
        float draw = Mathf.Clamp(unitRoll, 0f, 0.999999f) * Mathf.Max(100f, total);

        if (options == null)
            return -1;

        for (int i = 0; i < options.Count; i++)
        {
            LootOption option = options[i];
            if (option == null || option.prefab == null)
                continue;

            float chance = Mathf.Clamp(option.chancePercent, 0f, 100f);
            if (draw < chance)
                return i;

            draw -= chance;
        }

        return -1;
    }

    private static float TotalChance(IReadOnlyList<LootOption> options)
    {
        if (options == null)
            return 0f;

        float total = 0f;
        for (int i = 0; i < options.Count; i++)
        {
            LootOption option = options[i];
            if (option != null && option.prefab != null)
                total += Mathf.Clamp(option.chancePercent, 0f, 100f);
        }

        return total;
    }

    private int SlotCount()
    {
        int count = 0;
        if (groups == null)
            return count;

        for (int i = 0; i < groups.Count; i++)
            count += groups[i]?.slots?.Count ?? 0;

        return count;
    }

    private void TryInitializeLoot()
    {
        if (selectionApplied)
            return;

        if (!PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                return;

            ApplySelection(RollAllSlots());
            return;
        }

        if (PhotonNetwork.OfflineMode)
        {
            ApplySelection(RollAllSlots());
            return;
        }

        if (string.IsNullOrEmpty(roomKey))
        {
            Debug.LogWarning("[LootSpawner] O objeto precisa pertencer a uma cena salva para sincronizar o saque.", this);
            return;
        }

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(roomKey, out object existing))
        {
            ApplySelection(existing);
            return;
        }

        if (!PhotonNetwork.IsMasterClient)
            return;

        int[] choices = RollAllSlots();
        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { roomKey, choices } }))
        {
            Debug.LogWarning("[LootSpawner] Nao foi possivel publicar o saque da sala.", this);
            return;
        }

        ApplySelection(choices);
    }

    private int[] RollAllSlots()
    {
        int[] choices = new int[SlotCount()];
        if (groups == null)
            return choices;

        int choiceIndex = 0;
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            LootGroup group = groups[groupIndex];
            if (group?.slots == null)
                continue;

            for (int slotIndex = 0; slotIndex < group.slots.Count; slotIndex++)
            {
                SpawnSlot slot = group.slots[slotIndex];
                float total = TotalChance(slot?.options);
                if (total > 100.001f)
                    Debug.LogWarning($"[LootSpawner] Grupo {groupIndex + 1}, slot {slotIndex + 1} de '{name}' soma {total:0.##}%. As chances serao normalizadas e nao havera chance de vazio.", this);

                choices[choiceIndex++] = SelectOptionIndex(slot?.options, UnityEngine.Random.value);
            }
        }

        return choices;
    }

    private void ApplySelection(object value)
    {
        if (selectionApplied)
            return;

        if (value is not int[] choices || choices.Length != SlotCount())
        {
            Debug.LogWarning($"[LootSpawner] Configuracao de saque incompativel para '{name}'.", this);
            return;
        }

        selectionApplied = true;
        if (groups == null)
            return;

        int choiceIndex = 0;
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            LootGroup group = groups[groupIndex];
            if (group?.slots == null)
                continue;

            for (int slotIndex = 0; slotIndex < group.slots.Count; slotIndex++)
            {
                int selectedIndex = choices[choiceIndex++];
                SpawnSlot slot = group.slots[slotIndex];
                if (slot == null || selectedIndex < 0)
                    continue;

                if (slot.options == null || selectedIndex >= slot.options.Count)
                {
                    Debug.LogWarning($"[LootSpawner] Opcao {selectedIndex} nao existe no grupo {groupIndex + 1}, slot {slotIndex + 1} de '{name}'.", this);
                    continue;
                }

                SpawnPickup(groupIndex, slotIndex, slot, slot.options[selectedIndex]);
            }
        }
    }

    private void SpawnPickup(int groupIndex, int slotIndex, SpawnSlot slot, LootOption option)
    {
        if (option == null || option.prefab == null)
            return;

        if (option.prefab.ItemDefinition == null)
        {
            Debug.LogWarning($"[LootSpawner] Item do grupo {groupIndex + 1}, slot {slotIndex + 1} de '{name}' nao possui ItemDefinition.", this);
            return;
        }

        Transform point = slot.spawnPoint != null ? slot.spawnPoint : transform;
        WorldPickupItem item = Instantiate(option.prefab, point.position, point.rotation);
        item.AssignRuntimeNetworkSceneId(ItemIdPrefix + itemContainerId + ":" + groupIndex + ":" + slotIndex);
        Rigidbody[] bodies = item.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody body = bodies[i];
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.isKinematic = true;
            body.useGravity = false;
        }

        item.AttachToLootAnchor(point);
        item.SetPickupAvailable(IsGroupAvailable(groupIndex));
        placedPickups.Add(new PlacedPickup
        {
            item = item,
            anchor = point,
            rootBody = item.GetComponent<Rigidbody>(),
            groupIndex = groupIndex
        });
    }

    private void SubscribeDoors()
    {
        if (groups == null)
            return;

        for (int i = 0; i < groups.Count; i++)
        {
            DoorController door = groups[i]?.door;
            if (door == null || !subscribedDoors.Add(door))
                continue;

            door.OpenStateChanged += HandleDoorStateChanged;
        }
    }

    private void HandleDoorStateChanged(DoorController door)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i]?.door == door)
                RefreshGroupAvailability(i);
        }
    }

    private bool IsGroupAvailable(int groupIndex)
    {
        DoorController door = groups[groupIndex]?.door;
        return door == null || door.IsOpen;
    }

    private void RefreshAvailability()
    {
        if (groups == null)
            return;

        for (int i = 0; i < groups.Count; i++)
            RefreshGroupAvailability(i);
    }

    private void RefreshGroupAvailability(int groupIndex)
    {
        bool available = IsGroupAvailable(groupIndex);
        for (int i = 0; i < placedPickups.Count; i++)
        {
            PlacedPickup placed = placedPickups[i];
            if (placed.groupIndex == groupIndex && placed.item != null && !placed.item.HasEverBeenEquipped)
                placed.item.SetPickupAvailable(available);
        }
    }
}
