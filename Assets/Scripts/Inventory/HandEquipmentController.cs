using System;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView))]
public class HandEquipmentController : MonoBehaviour
{
    private const int SlotsPerHand = 2;
    private const int InvalidSlotIndex = -1;
    private const float MinSocketScaleMagnitude = 0.0001f;
    private const string RightHandSocketName = "RightHandSocket";
    private const string LeftHandSocketName = "LeftHandSocket";
    private const string DropPointName = "Drop Point";
    private const string LegacyRightHandSocketName = "HandSocket";
    private const string FirstPersonCameraName = "FP_Camera";
    private const string FirstPersonModelRootName = "FPS_Model";
    private const string FirstPersonViewLayerName = "FirstPersonView";
    private const string FirstPersonRightHandBoneName = "Hand.R";
    private const string FirstPersonLeftHandBoneName = "Hand.L";
    private const string DefaultTorchItemResourcePath = "Items/Torch";
    private const string TorchItemFallbackName = "Torch";
    private const float DeathDropScatterRadius = 0.45f;
    private const float DeathDropVelocityMultiplier = 0.45f;

    private static readonly string[] FirstPersonRightHandSocketPaths =
    {
        "KnightFPS/Armature/Root/Arm_Upper.R/Arm_Lower.R/Hand.R/RightHandSocket",
        "Armature/Root/Arm_Upper.R/Arm_Lower.R/Hand.R/RightHandSocket",
        "KnightFPS/Armature/Root/Arm_Upper.R/Arm_Lower.R/Hand.R",
        "Armature/Root/Arm_Upper.R/Arm_Lower.R/Hand.R"
    };

    private static readonly string[] FirstPersonLeftHandSocketPaths =
    {
        "KnightFPS/Armature/Root/Arm_Upper.L/Arm_Lower.L/Hand.L/LeftHandSocket",
        "Armature/Root/Arm_Upper.L/Arm_Lower.L/Hand.L/LeftHandSocket",
        "KnightFPS/Armature/Root/Arm_Upper.L/Arm_Lower.L/Hand.L",
        "Armature/Root/Arm_Upper.L/Arm_Lower.L/Hand.L"
    };

    [Header("Third Person Item Sockets")]
    [SerializeField] private Transform rightHandSocket;
    [SerializeField] private Transform leftHandSocket;

    [Header("References")]
    [SerializeField] private Transform dropPoint;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerMeleeAttack playerMeleeAttack;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PhotonView photonView;
    [SerializeField] private ItemDefinition torchItemDefinition;
    [SerializeField] private bool prototypeLocalOnly;

    [Header("First Person Item Sockets")]
    [SerializeField] private bool createFirstPersonItemVisuals = true;
    [SerializeField] private bool hideThirdPersonItemForOwner = true;
    [SerializeField] private Transform firstPersonModelRoot;
    [SerializeField] private Transform rightHandFirstPersonSocket;
    [SerializeField] private Transform leftHandFirstPersonSocket;
    [Header("Drop")]
    [SerializeField] private KeyCode dropKey = KeyCode.G;
    [SerializeField] [Min(0.25f)] private float dropDistance = 1.25f;
    [SerializeField] [Min(0.05f)] private float dropHeightOffset = 0.1f;
    [SerializeField] [Min(0.25f)] private float dropGroundProbeHeight = 1f;
    [SerializeField] [Min(0.25f)] private float dropGroundProbeDistance = 3f;
    [SerializeField] [Min(0f)] private float dropForwardVelocity = 2.5f;
    [SerializeField] [Min(0f)] private float dropUpwardVelocity = 1.2f;
    [SerializeField] [Range(0f, 1f)] private float dropInheritedPlanarVelocityFactor = 0.35f;
    [SerializeField] [Min(0f)] private float dropTumbleDegreesPerSecond = 540f;
    [SerializeField] [Min(0f)] private float dropYawDegreesPerSecond = 180f;
    [SerializeField] private LayerMask dropGroundMask = Physics.DefaultRaycastLayers;
    [SerializeField] private Camera interactionCamera;

    private readonly ItemDefinition[] rightHandSlots = new ItemDefinition[SlotsPerHand];
    private readonly ItemDefinition[] leftHandSlots = new ItemDefinition[SlotsPerHand];
    private readonly WorldPickupItem[] rightHandPickupSources = new WorldPickupItem[SlotsPerHand];
    private readonly WorldPickupItem[] leftHandPickupSources = new WorldPickupItem[SlotsPerHand];
    private readonly WorldPickupItem[] rightHandFirstPersonVisuals = new WorldPickupItem[SlotsPerHand];
    private readonly WorldPickupItem[] leftHandFirstPersonVisuals = new WorldPickupItem[SlotsPerHand];
    private readonly WorldPickupItem[] rightHandFirstPersonVisualSources = new WorldPickupItem[SlotsPerHand];
    private readonly WorldPickupItem[] leftHandFirstPersonVisualSources = new WorldPickupItem[SlotsPerHand];

    private int activeRightHandIndex;
    private int activeLeftHandIndex;
    private HandType lastInteractedHand = HandType.Right;
    private HandType pendingMeleeTrailHand = HandType.Right;
    private int pendingMeleeTrailSlotIndex = InvalidSlotIndex;
    private int lastMeleeTrailAttackSequence;
    private bool hasPendingMeleeTrail;

    public event Action StateChanged;

    public bool SuppressesDefaultPrimaryAttack => true;
    public Transform RightHandSocket => rightHandSocket;
    public Transform LeftHandSocket => leftHandSocket;
    public Transform RightHandFirstPersonSocket => rightHandFirstPersonSocket;
    public Transform LeftHandFirstPersonSocket => leftHandFirstPersonSocket;
    public Transform DropPoint => dropPoint;
    public bool PrototypeLocalOnly => prototypeLocalOnly;

    public bool HasActiveEquippedLightSource()
    {
        return HasActiveEquippedLightSource(HandType.Right)
            || HasActiveEquippedLightSource(HandType.Left);
    }

    private void Awake()
    {
        ResolveReferences();
        lastMeleeTrailAttackSequence = playerMovement != null ? playerMovement.AttackAnimationSequence : 0;
    }

    private void Start()
    {
        ResolveReferences();
        lastMeleeTrailAttackSequence = playerMovement != null ? playerMovement.AttackAnimationSequence : 0;

        if (HasLocalAuthority())
            HandEquipmentUI.BindLocalPlayer(this);

        RefreshEquippedVisuals();
        NotifyStateChanged();
    }

    private void Update()
    {
        UpdatePendingMeleeTrailPlayback();

        if (!HasLocalAuthority())
            return;

        HandleRuntimeInput();
    }

    public void HandleRuntimeInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            ToggleActiveSlot(HandType.Right);

        if (Input.GetKeyDown(KeyCode.Alpha2))
            ToggleActiveSlot(HandType.Left);

        if (Input.GetMouseButtonDown(0))
            UseActiveItem(HandType.Right);

        if (Input.GetMouseButtonDown(1))
            UseActiveItem(HandType.Left);

        if (Input.GetKeyDown(dropKey))
            TryDropActiveItem();
    }

    public ItemDefinition GetItem(HandType hand, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotsPerHand)
            return null;

        return GetSlots(hand)[slotIndex];
    }

    public int GetActiveSlotIndex(HandType hand)
    {
        return hand == HandType.Right ? activeRightHandIndex : activeLeftHandIndex;
    }

    public bool HasItem(ItemDefinition itemDefinition)
    {
        if (itemDefinition == null)
            return false;

        for (int i = 0; i < rightHandSlots.Length; i++)
        {
            if (rightHandSlots[i] == itemDefinition || leftHandSlots[i] == itemDefinition)
                return true;
        }

        return false;
    }

    public bool TryDropActiveItem()
    {
        ResolveReferences();

        if (!CanChangeInventorySlots("Drop de item"))
            return false;

        if (!TryResolveDropHand(out HandType hand))
        {
            Debug.Log("[HandEquipmentController] Nenhum item ativo foi encontrado para dropar.");
            return false;
        }

        return TryDropActiveItem(hand);
    }

    public int DropAllEquippedItemsOnDeath()
    {
        ResolveReferences();

        if (!HasLocalAuthority())
            return 0;

        int totalDropCount = CountDeathDroppableSlots();
        if (totalDropCount <= 0)
            return 0;

        int dropOrder = 0;
        int droppedCount = 0;
        droppedCount += DropAllEquippedItemsOnDeath(HandType.Right, totalDropCount, ref dropOrder);
        droppedCount += DropAllEquippedItemsOnDeath(HandType.Left, totalDropCount, ref dropOrder);

        if (droppedCount > 0)
        {
            RefreshEquippedVisuals();
            NotifyStateChanged();
            BroadcastInventorySnapshotIfNeeded();
            Debug.Log($"[HandEquipmentController] {droppedCount} item(ns) dropado(s) ao morrer.");
        }

        return droppedCount;
    }

    public int DropActiveEquippedItemsOnRagdoll()
    {
        ResolveReferences();

        if (!HasLocalAuthority())
            return 0;

        int totalDropCount = CountActiveEquippedSlots();
        if (totalDropCount <= 0)
            return 0;

        int dropOrder = 0;
        int droppedCount = 0;
        droppedCount += DropActiveEquippedItemOnRagdoll(HandType.Right, totalDropCount, ref dropOrder);
        droppedCount += DropActiveEquippedItemOnRagdoll(HandType.Left, totalDropCount, ref dropOrder);

        if (droppedCount > 0)
        {
            RefreshEquippedVisuals();
            NotifyStateChanged();
            BroadcastInventorySnapshotIfNeeded();
            Debug.Log($"[HandEquipmentController] {droppedCount} item(ns) ativo(s) dropado(s) ao entrar em ragdoll.");
        }

        return droppedCount;
    }

    public bool TryEquipWorldItem(WorldPickupItem pickupItem)
    {
        ResolveReferences();

        if (!HasLocalAuthority())
            return false;

        if (pickupItem == null)
        {
            Debug.LogWarning("[HandEquipmentController] Nenhum pickup valido foi informado.");
            return false;
        }

        if (!CanChangeInventorySlots("Equipar item"))
            return false;

        ItemDefinition itemDefinition = pickupItem.ItemDefinition;
        if (itemDefinition == null)
        {
            Debug.LogWarning($"[HandEquipmentController] '{pickupItem.gameObject.name}' esta sem ItemDefinition.", pickupItem);
            return false;
        }

        if (!TryResolveEquipRequest(itemDefinition, out HandType hand, out int slotIndex))
            return false;

        if (ShouldUseNetworkSync())
        {
            string pickupSceneId = pickupItem.NetworkSceneId;
            if (string.IsNullOrWhiteSpace(pickupSceneId))
            {
                Debug.LogWarning($"[HandEquipmentController] '{pickupItem.ItemName}' nao possui um identificador de rede estavel.", pickupItem);
                return false;
            }

            photonView.RPC(nameof(RpcApplyEquipPickup), RpcTarget.AllBufferedViaServer, pickupSceneId, (int)hand, slotIndex);
            return true;
        }

        return ApplyEquipIntoSlot(hand, slotIndex, pickupItem, logOnFailure: true, focusInEditor: true);
    }

    public void ToggleActiveSlot(HandType hand)
    {
        ResolveReferences();

        if (!HasLocalAuthority())
            return;

        if (!CanChangeInventorySlots("Troca de slot"))
            return;

        int nextSlotIndex = GetNextSlotIndex(hand);
        if (ShouldUseNetworkSync())
        {
            photonView.RPC(nameof(RpcApplySetActiveSlot), RpcTarget.AllBufferedViaServer, (int)hand, nextSlotIndex);
            return;
        }

        ApplySetActiveSlot(hand, nextSlotIndex, logResult: true);
    }

    public bool UseActiveItem(HandType hand)
    {
        ResolveReferences();
        lastInteractedHand = hand;

        int activeIndex = GetActiveSlotIndex(hand);
        ItemDefinition itemDefinition = GetItem(hand, activeIndex);
        if (itemDefinition == null)
        {
            Debug.Log($"[HandEquipmentController] Nenhum item equipado no Slot {activeIndex + 1} da mao {GetHandLabel(hand)}.");
            return false;
        }

        switch (itemDefinition.UseType)
        {
            case ItemUseType.Weapon:
            case ItemUseType.MeleeWeapon:
                return UseMeleeWeapon(hand, itemDefinition);
            case ItemUseType.Consumable:
                return UseConsumable(hand, itemDefinition);
            case ItemUseType.Tool:
                return UseTool(hand, itemDefinition);
            case ItemUseType.SellOnly:
                Debug.Log($"[HandEquipmentController] '{itemDefinition.ItemName}' esta marcado como SellOnly e nao pode ser usado pela mao {GetHandLabel(hand)}.");
                return false;
            default:
                if (!CanBeginInventoryItemAction($"Uso de '{itemDefinition.ItemName}'"))
                    return false;

                BeginInventoryItemActionLock();
                Debug.Log($"[HandEquipmentController] Item usado: {itemDefinition.ItemName} pela mao {GetHandLabel(hand)}.");
                if (itemDefinition.ConsumeOnUse)
                    ConsumeActiveSlot(hand, $"{itemDefinition.ItemName} foi consumido.");
                return true;
        }
    }

    private bool UseMeleeWeapon(HandType hand, ItemDefinition itemDefinition)
    {
        if (playerMovement == null)
        {
            Debug.LogWarning($"[HandEquipmentController] Nao foi possivel usar '{itemDefinition.ItemName}': PlayerMovement nao encontrado.", gameObject);
            return false;
        }

        int activeIndex = GetActiveSlotIndex(hand);
        int previousAttackSequence = playerMovement.AttackAnimationSequence;
        if (!playerMovement.TryAttack())
            return false;

        if (playerMeleeAttack != null)
            playerMeleeAttack.SetNextAttackItem(itemDefinition);

        HandleAcceptedMeleeTrailRequest(hand, activeIndex, previousAttackSequence);
        Debug.Log($"[HandEquipmentController] Item usado: {itemDefinition.ItemName} pela mao {GetHandLabel(hand)}.");
        return true;
    }

    private bool UseConsumable(HandType hand, ItemDefinition itemDefinition)
    {
        if (playerHealth == null)
        {
            Debug.LogWarning($"[HandEquipmentController] Nao foi possivel usar '{itemDefinition.ItemName}': PlayerHealth nao encontrado.", gameObject);
            return false;
        }

        if (!CanBeginInventoryItemAction($"Uso de '{itemDefinition.ItemName}'"))
            return false;

        float restoredHealth = playerHealth.RestoreHealth(itemDefinition.HealAmount);
        if (itemDefinition.HealAmount > 0f && restoredHealth <= 0f)
        {
            Debug.Log($"[HandEquipmentController] '{itemDefinition.ItemName}' nao foi usado: vida ja esta cheia.");
            return false;
        }

        if (itemDefinition.HealAmount > 0f)
        {
            Debug.Log($"[HandEquipmentController] Item usado: {itemDefinition.ItemName} pela mao {GetHandLabel(hand)}. Vida restaurada: {restoredHealth:0.##}.");
        }
        else
        {
            Debug.Log($"[HandEquipmentController] Item usado: {itemDefinition.ItemName} pela mao {GetHandLabel(hand)}.");
        }

        BeginInventoryItemActionLock();

        if (itemDefinition.ConsumeOnUse)
            ConsumeActiveSlot(hand, $"{itemDefinition.ItemName} foi consumido apos o uso.");

        return true;
    }

    private bool UseTool(HandType hand, ItemDefinition itemDefinition)
    {
        if (IsTorchItem(itemDefinition))
        {
            if (!TryUseTorchTool(hand, itemDefinition, out bool torchIsLit))
                return false;

            Debug.Log($"[HandEquipmentController] Tocha {(torchIsLit ? "acesa" : "apagada")} pela mao {GetHandLabel(hand)}.");
            return true;
        }

        if (!CanBeginInventoryItemAction($"Uso de '{itemDefinition.ItemName}'"))
            return false;

        BeginInventoryItemActionLock();
        Debug.Log($"[HandEquipmentController] Item usado: {itemDefinition.ItemName} pela mao {GetHandLabel(hand)}.");
        if (itemDefinition.ConsumeOnUse)
            ConsumeActiveSlot(hand, $"{itemDefinition.ItemName} foi consumido.");

        return true;
    }

    private bool TryUseTorchTool(HandType hand, ItemDefinition itemDefinition, out bool torchIsLit)
    {
        torchIsLit = false;

        if (!IsTorchItem(itemDefinition))
            return false;

        int activeIndex = GetActiveSlotIndex(hand);
        WorldPickupItem pickupSource = GetPickupSources(hand)[activeIndex];
        TorchFlameController torchFlame = ResolveTorchFlameController(pickupSource);
        if (torchFlame == null)
        {
            Debug.LogWarning($"[HandEquipmentController] Nao foi possivel alternar '{itemDefinition.ItemName}': TorchFlameController nao encontrado no item equipado.", pickupSource);
            return false;
        }

        torchIsLit = !torchFlame.IsLit;

        if (ShouldUseNetworkSync())
        {
            string pickupSceneId = pickupSource != null ? pickupSource.NetworkSceneId : string.Empty;
            if (string.IsNullOrWhiteSpace(pickupSceneId))
            {
                Debug.LogWarning($"[HandEquipmentController] Nao foi possivel sincronizar a tocha '{itemDefinition.ItemName}': pickup sem identificador de rede.", pickupSource);
                return false;
            }

            photonView.RPC(nameof(RpcApplyTorchLitState), RpcTarget.AllBufferedViaServer, pickupSceneId, (int)hand, activeIndex, torchIsLit);
            return true;
        }

        return ApplyTorchLitState(string.Empty, hand, activeIndex, torchIsLit);
    }

    private bool ApplyTorchLitState(string pickupSceneId, HandType hand, int slotIndex, bool lit)
    {
        if (!IsValidSlotIndex(slotIndex))
            return false;

        WorldPickupItem pickupSource = null;
        if (!string.IsNullOrWhiteSpace(pickupSceneId))
            WorldPickupItem.TryFindByNetworkSceneId(pickupSceneId, out pickupSource);

        if (pickupSource == null)
            pickupSource = GetPickupSources(hand)[slotIndex];

        if (pickupSource == null)
            return false;

        bool applied = SetTorchFlameLit(pickupSource, lit);

        WorldPickupItem firstPersonVisual = GetFirstPersonVisuals(hand)[slotIndex];
        if (firstPersonVisual != null)
            applied |= SetTorchFlameLit(firstPersonVisual, lit);

        if (applied)
            NotifyStateChanged();

        return applied;
    }

    private static bool SetTorchFlameLit(WorldPickupItem pickupItem, bool lit)
    {
        TorchFlameController torchFlame = ResolveTorchFlameController(pickupItem);
        if (torchFlame == null)
            return false;

        torchFlame.SetLit(lit);
        return true;
    }

    private static void SyncTorchFlameState(WorldPickupItem sourceItem, WorldPickupItem visualItem)
    {
        TorchFlameController sourceFlame = ResolveTorchFlameController(sourceItem);
        TorchFlameController visualFlame = ResolveTorchFlameController(visualItem);
        if (sourceFlame == null || visualFlame == null)
            return;

        visualFlame.SetLit(sourceFlame.IsLit);
    }

    private static TorchFlameController ResolveTorchFlameController(WorldPickupItem pickupItem)
    {
        return pickupItem != null ? pickupItem.GetComponentInChildren<TorchFlameController>(true) : null;
    }

    private bool TryEquipAnyHand(ItemDefinition itemDefinition, WorldPickupItem pickupItem)
    {
        HandType firstHand = itemDefinition.PreferredHand;
        HandType secondHand = firstHand == HandType.Right ? HandType.Left : HandType.Right;

        if (TryEquipIntoActiveSlot(firstHand, itemDefinition, pickupItem, logOnFailure: false))
            return true;

        if (TryEquipIntoActiveSlot(secondHand, itemDefinition, pickupItem, logOnFailure: false))
            return true;

        int rightSlot = activeRightHandIndex + 1;
        int leftSlot = activeLeftHandIndex + 1;
        string rightItemName = rightHandSlots[activeRightHandIndex] != null ? rightHandSlots[activeRightHandIndex].ItemName : "vazio";
        string leftItemName = leftHandSlots[activeLeftHandIndex] != null ? leftHandSlots[activeLeftHandIndex].ItemName : "vazio";

        Debug.Log($"[HandEquipmentController] Nao foi possivel equipar '{itemDefinition.ItemName}': Slot {rightSlot} da mao direita = {rightItemName}, Slot {leftSlot} da mao esquerda = {leftItemName}.");
        return false;
    }

    private bool TryEquipIntoActiveSlot(HandType hand, ItemDefinition itemDefinition, WorldPickupItem pickupItem, bool logOnFailure = true)
    {
        if (itemDefinition == null || pickupItem == null)
            return false;

        if (!itemDefinition.CanEquipInHand(hand))
        {
            if (logOnFailure)
                Debug.Log($"[HandEquipmentController] Mao incompativel: '{itemDefinition.ItemName}' nao pode ser equipado na mao {GetHandLabel(hand)}.");

            return false;
        }

        ItemDefinition[] slots = GetSlots(hand);
        int activeIndex = GetActiveSlotIndex(hand);
        if (slots[activeIndex] != null)
        {
            if (logOnFailure)
                Debug.Log($"[HandEquipmentController] Slot ocupado: Slot {activeIndex + 1} da mao {GetHandLabel(hand)} ja contem '{slots[activeIndex].ItemName}'.");

            return false;
        }

        Transform targetSocket = GetSocket(hand);
        if (targetSocket == null)
        {
            Debug.LogWarning($"[HandEquipmentController] Socket da mao {GetHandLabel(hand)} nao encontrado para equipar '{pickupItem.ItemName}'.", gameObject);
            return false;
        }

        if (!pickupItem.TryEquipIntoHand(targetSocket, hand))
        {
            Debug.LogWarning($"[HandEquipmentController] Falha ao equipar '{pickupItem.ItemName}' na mao {GetHandLabel(hand)}.", pickupItem);
            return false;
        }

        slots[activeIndex] = itemDefinition;
        GetPickupSources(hand)[activeIndex] = pickupItem;
        lastInteractedHand = hand;

        if (playerMovement != null)
            playerMovement.TriggerPickupAnimation(hand);

        Debug.Log($"[HandEquipmentController] Item equipado: {itemDefinition.ItemName} no Slot {activeIndex + 1} da mao {GetHandLabel(hand)}.");
        RefreshEquippedVisuals();
        FocusEquippedItemForAuthoring(pickupItem);
        NotifyStateChanged();
        return true;
    }

    private void ConsumeActiveSlot(HandType hand, string reason)
    {
        ResolveReferences();

        int activeIndex = GetActiveSlotIndex(hand);
        WorldPickupItem pickupSource = GetPickupSources(hand)[activeIndex];
        if (ShouldUseNetworkSync() && HasLocalAuthority())
        {
            string pickupSceneId = pickupSource != null ? pickupSource.NetworkSceneId : string.Empty;
            photonView.RPC(nameof(RpcApplyConsumePickup), RpcTarget.AllBufferedViaServer, pickupSceneId, (int)hand, activeIndex);
            return;
        }

        if (pickupSource != null)
            pickupSource.DestroyAfterUse();

        ClearSlotReference(hand, activeIndex, reason);
    }

    private void ClearSlotReference(
        HandType hand,
        int slotIndex,
        string reason,
        bool refreshInventoryState = true,
        bool broadcastInventorySnapshot = true)
    {
        if (slotIndex < 0 || slotIndex >= SlotsPerHand)
            return;

        ItemDefinition[] slots = GetSlots(hand);
        slots[slotIndex] = null;
        GetPickupSources(hand)[slotIndex] = null;

        Debug.Log($"[HandEquipmentController] Slot liberado: mao {GetHandLabel(hand)}, Slot {slotIndex + 1}. Motivo: {reason}");

        if (refreshInventoryState)
        {
            RefreshEquippedVisuals();
            NotifyStateChanged();
        }

        if (broadcastInventorySnapshot)
            BroadcastInventorySnapshotIfNeeded();
    }

    private ItemDefinition[] GetSlots(HandType hand)
    {
        return hand == HandType.Right ? rightHandSlots : leftHandSlots;
    }

    private WorldPickupItem[] GetPickupSources(HandType hand)
    {
        return hand == HandType.Right ? rightHandPickupSources : leftHandPickupSources;
    }

    private void HandleAcceptedMeleeTrailRequest(HandType hand, int slotIndex, int previousAttackSequence)
    {
        if (playerMovement == null)
            return;

        int currentAttackSequence = playerMovement.AttackAnimationSequence;
        if (currentAttackSequence != previousAttackSequence)
        {
            lastMeleeTrailAttackSequence = currentAttackSequence;
            hasPendingMeleeTrail = false;
            PlayMeleeWeaponTrail(hand, slotIndex, broadcast: true);
            return;
        }

        pendingMeleeTrailHand = hand;
        pendingMeleeTrailSlotIndex = slotIndex;
        lastMeleeTrailAttackSequence = currentAttackSequence;
        hasPendingMeleeTrail = true;
    }

    private void UpdatePendingMeleeTrailPlayback()
    {
        if (!hasPendingMeleeTrail || playerMovement == null)
            return;

        int currentAttackSequence = playerMovement.AttackAnimationSequence;
        if (currentAttackSequence == lastMeleeTrailAttackSequence)
            return;

        HandType hand = pendingMeleeTrailHand;
        int slotIndex = pendingMeleeTrailSlotIndex;
        hasPendingMeleeTrail = false;
        pendingMeleeTrailSlotIndex = InvalidSlotIndex;
        lastMeleeTrailAttackSequence = currentAttackSequence;
        PlayMeleeWeaponTrail(hand, slotIndex, broadcast: true);
    }

    private void PlayMeleeWeaponTrail(HandType hand, int slotIndex, bool broadcast)
    {
        if (!IsValidSlotIndex(slotIndex))
            return;

        ItemDefinition itemDefinition = GetItem(hand, slotIndex);
        if (!SupportsMeleeWeaponTrail(itemDefinition))
            return;

        WorldPickupItem pickupSource = GetPickupSources(hand)[slotIndex];
        WorldPickupItem firstPersonVisual = GetFirstPersonVisuals(hand)[slotIndex];
        if (ShouldUseFirstPersonItemVisuals()
            && pickupSource != null
            && (firstPersonVisual == null || !firstPersonVisual.gameObject.activeSelf))
        {
            RefreshFirstPersonVisual(hand, slotIndex, pickupSource, shouldBeActive: true);
            firstPersonVisual = GetFirstPersonVisuals(hand)[slotIndex];
        }

        bool shouldPlayThirdPersonTrail = pickupSource != null
            && (!HasLocalAuthority() || !hideThirdPersonItemForOwner || firstPersonVisual == null);

        if (shouldPlayThirdPersonTrail)
            PlayWeaponTrail(pickupSource);

        if (firstPersonVisual != null)
            PlayWeaponTrail(firstPersonVisual);

        if (broadcast && ShouldUseNetworkSync() && HasLocalAuthority() && photonView != null)
            photonView.RPC(nameof(RpcPlayMeleeWeaponTrail), RpcTarget.Others, (int)hand, slotIndex);
    }

    private static void PlayWeaponTrail(WorldPickupItem pickupItem)
    {
        if (pickupItem == null)
            return;

        WeaponAttackTrail[] weaponTrails = pickupItem.GetComponentsInChildren<WeaponAttackTrail>(true);
        if (weaponTrails == null || weaponTrails.Length == 0)
            return;

        for (int i = 0; i < weaponTrails.Length; i++)
        {
            WeaponAttackTrail weaponTrail = weaponTrails[i];
            if (weaponTrail != null)
                weaponTrail.PlayAttackTrail();
        }
    }

    private static bool SupportsMeleeWeaponTrail(ItemDefinition itemDefinition)
    {
        if (itemDefinition == null)
            return false;

        return itemDefinition.UseType == ItemUseType.Weapon
            || itemDefinition.UseType == ItemUseType.MeleeWeapon;
    }

    private void ResolveReferences()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (playerMeleeAttack == null)
            playerMeleeAttack = GetComponent<PlayerMeleeAttack>();

        if (playerHealth == null)
            playerHealth = GetComponent<PlayerHealth>();

        if (torchItemDefinition == null)
            torchItemDefinition = Resources.Load<ItemDefinition>(DefaultTorchItemResourcePath);

        ResolveHandSockets();
        ResolveFirstPersonItemSockets();
        ResolveDropPoint();
        ResolveInteractionCamera();
    }

    private void ResolveHandSockets()
    {
        if (rightHandSocket != null && leftHandSocket != null)
        {
            EnsureUsableSocketScale(rightHandSocket, HandType.Right);
            EnsureUsableSocketScale(leftHandSocket, HandType.Left);
            return;
        }

        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform childTransform = childTransforms[i];
            if (childTransform == null)
                continue;

            if (rightHandSocket == null
                && (string.Equals(childTransform.name, RightHandSocketName, StringComparison.Ordinal)
                    || string.Equals(childTransform.name, LegacyRightHandSocketName, StringComparison.Ordinal)))
            {
                rightHandSocket = childTransform;
                continue;
            }

            if (leftHandSocket == null
                && string.Equals(childTransform.name, LeftHandSocketName, StringComparison.Ordinal))
            {
                leftHandSocket = childTransform;
            }
        }

        EnsureUsableSocketScale(rightHandSocket, HandType.Right);
        EnsureUsableSocketScale(leftHandSocket, HandType.Left);
    }

    private void ResolveFirstPersonItemSockets()
    {
        if (!createFirstPersonItemVisuals)
            return;

        if (firstPersonModelRoot == null)
            firstPersonModelRoot = FindChildTransformByName(transform, FirstPersonModelRootName);

        if (rightHandFirstPersonSocket == null)
            rightHandFirstPersonSocket = ResolveFirstPersonHandSocket(HandType.Right);

        if (leftHandFirstPersonSocket == null)
            leftHandFirstPersonSocket = ResolveFirstPersonHandSocket(HandType.Left);

        EnsureUsableSocketScale(rightHandFirstPersonSocket, HandType.Right);
        EnsureUsableSocketScale(leftHandFirstPersonSocket, HandType.Left);
    }

    private Transform ResolveFirstPersonHandSocket(HandType hand)
    {
        Transform searchRoot = firstPersonModelRoot != null ? firstPersonModelRoot : FindChildTransformByName(transform, FirstPersonModelRootName);
        if (searchRoot == null)
            return null;

        string socketName = hand == HandType.Right ? RightHandSocketName : LeftHandSocketName;
        Transform namedSocket = FindChildTransformByName(searchRoot, socketName);
        if (namedSocket != null)
            return namedSocket;

        string[] candidatePaths = hand == HandType.Right
            ? FirstPersonRightHandSocketPaths
            : FirstPersonLeftHandSocketPaths;
        for (int i = 0; i < candidatePaths.Length; i++)
        {
            Transform candidate = searchRoot.Find(candidatePaths[i]);
            if (candidate != null)
                return candidate;
        }

        string targetName = hand == HandType.Right ? FirstPersonRightHandBoneName : FirstPersonLeftHandBoneName;
        return FindChildTransformByName(searchRoot, targetName);
    }

    private static Transform FindChildTransformByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        Transform[] childTransforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform childTransform = childTransforms[i];
            if (childTransform != null && string.Equals(childTransform.name, targetName, StringComparison.Ordinal))
                return childTransform;
        }

        return null;
    }

    private void ResolveDropPoint()
    {
        if (dropPoint != null)
            return;

        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform childTransform = childTransforms[i];
            if (childTransform == null)
                continue;

            if (string.Equals(childTransform.name, DropPointName, StringComparison.Ordinal))
            {
                dropPoint = childTransform;
                return;
            }
        }
    }

    private bool HasLocalAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private bool ShouldUseNetworkSync()
    {
        return !prototypeLocalOnly
            && photonView != null
            && PhotonNetwork.InRoom
            && !PhotonNetwork.OfflineMode;
    }

    private void ResolveInteractionCamera()
    {
        if (interactionCamera != null)
            return;

        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera playerCamera = cameras[i];
            if (playerCamera == null)
                continue;

            if (string.Equals(playerCamera.gameObject.name, FirstPersonCameraName, StringComparison.Ordinal))
            {
                interactionCamera = playerCamera;
                return;
            }

            if (interactionCamera == null && playerCamera.enabled)
                interactionCamera = playerCamera;
        }
    }

    private bool TryResolveDropHand(out HandType hand)
    {
        bool rightHasItem = HasActiveItem(HandType.Right);
        bool leftHasItem = HasActiveItem(HandType.Left);

        if (rightHasItem && leftHasItem)
        {
            if (HasActiveItem(lastInteractedHand))
            {
                hand = lastInteractedHand;
                Debug.Log($"[HandEquipmentController] Drop resolvido pela ultima mao interagida: {GetHandLabel(hand)}.");
                return true;
            }

            hand = lastInteractedHand == HandType.Right ? HandType.Left : HandType.Right;
            return HasActiveItem(hand);
        }

        if (rightHasItem)
        {
            hand = HandType.Right;
            return true;
        }

        if (leftHasItem)
        {
            hand = HandType.Left;
            return true;
        }

        hand = lastInteractedHand;
        return false;
    }

    private bool HasActiveItem(HandType hand)
    {
        return GetItem(hand, GetActiveSlotIndex(hand)) != null;
    }

    private bool HasActiveEquippedLightSource(HandType hand)
    {
        int activeIndex = GetActiveSlotIndex(hand);
        if (!IsValidSlotIndex(activeIndex))
            return false;

        WorldPickupItem firstPersonVisual = GetFirstPersonVisuals(hand)[activeIndex];
        if (HasActiveLightSource(firstPersonVisual))
            return true;

        WorldPickupItem pickupSource = GetPickupSources(hand)[activeIndex];
        return HasActiveLightSource(pickupSource);
    }

    private static bool HasActiveLightSource(WorldPickupItem pickupItem)
    {
        if (pickupItem == null || !pickupItem.gameObject.activeInHierarchy)
            return false;

        Light[] lights = pickupItem.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            Light itemLight = lights[i];
            if (itemLight == null)
                continue;

            if (itemLight.isActiveAndEnabled
                && itemLight.intensity > 0.0001f
                && itemLight.range > 0.0001f)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryDropActiveItem(HandType hand)
    {
        ResolveReferences();

        if (!HasLocalAuthority())
            return false;

        int activeIndex = GetActiveSlotIndex(hand);
        ItemDefinition itemDefinition = GetItem(hand, activeIndex);
        if (itemDefinition == null)
        {
            Debug.Log($"[HandEquipmentController] Nenhum item equipado no Slot {activeIndex + 1} da mao {GetHandLabel(hand)} para dropar.");
            return false;
        }

        return TryDropInventorySlot(
            hand,
            activeIndex,
            scatterAroundPlayer: false,
            scatterIndex: 0,
            scatterCount: 1,
            refreshInventoryState: true,
            broadcastInventorySnapshot: true);
    }

    private int DropAllEquippedItemsOnDeath(HandType hand, int totalDropCount, ref int dropOrder)
    {
        int droppedCount = 0;
        for (int slotIndex = 0; slotIndex < SlotsPerHand; slotIndex++)
        {
            ItemDefinition itemDefinition = GetItem(hand, slotIndex);
            if (itemDefinition == null)
                continue;

            int scatterIndex = dropOrder;
            dropOrder++;

            if (TryDropInventorySlot(
                hand,
                slotIndex,
                scatterAroundPlayer: true,
                scatterIndex: scatterIndex,
                scatterCount: totalDropCount,
                refreshInventoryState: false,
                broadcastInventorySnapshot: false))
            {
                droppedCount++;
            }
        }

        return droppedCount;
    }

    private int DropActiveEquippedItemOnRagdoll(HandType hand, int totalDropCount, ref int dropOrder)
    {
        int activeIndex = GetActiveSlotIndex(hand);
        ItemDefinition itemDefinition = GetItem(hand, activeIndex);
        if (itemDefinition == null)
            return 0;

        int scatterIndex = dropOrder;
        dropOrder++;

        return TryDropInventorySlot(
            hand,
            activeIndex,
            scatterAroundPlayer: true,
            scatterIndex: scatterIndex,
            scatterCount: totalDropCount,
            refreshInventoryState: false,
            broadcastInventorySnapshot: false)
            ? 1
            : 0;
    }

    private bool TryDropInventorySlot(
        HandType hand,
        int slotIndex,
        bool scatterAroundPlayer,
        int scatterIndex,
        int scatterCount,
        bool refreshInventoryState,
        bool broadcastInventorySnapshot)
    {
        if (!IsValidSlotIndex(slotIndex))
            return false;

        ItemDefinition itemDefinition = GetItem(hand, slotIndex);
        if (itemDefinition == null)
            return false;

        WorldPickupItem pickupSource = GetPickupSources(hand)[slotIndex];
        if (pickupSource == null)
        {
            Debug.LogWarning($"[HandEquipmentController] Nao foi possivel dropar '{itemDefinition.ItemName}' da mao {GetHandLabel(hand)}: pickup de origem nao foi preservado.");
            return false;
        }

        ResolveDropPose(pickupSource, out Vector3 dropPosition, out Quaternion dropRotation, out Vector3 dropDirection);
        if (scatterAroundPlayer)
            ApplyDeathDropScatter(ref dropPosition, ref dropDirection, scatterIndex, scatterCount);

        ResolveDropLaunch(hand, dropDirection, out Vector3 dropLinearVelocity, out Vector3 dropAngularVelocity);
        if (scatterAroundPlayer)
        {
            dropLinearVelocity *= DeathDropVelocityMultiplier;
            dropAngularVelocity *= DeathDropVelocityMultiplier;
        }

        if (ShouldUseNetworkSync())
        {
            string pickupSceneId = pickupSource.NetworkSceneId;
            if (string.IsNullOrWhiteSpace(pickupSceneId))
            {
                Debug.LogWarning($"[HandEquipmentController] Nao foi possivel sincronizar o drop de '{itemDefinition.ItemName}': pickup sem identificador de rede.", pickupSource);
                return false;
            }

            ApplyDroppedPickup(
                pickupSource,
                hand,
                slotIndex,
                dropPosition,
                dropRotation,
                dropLinearVelocity,
                dropAngularVelocity,
                refreshInventoryState,
                broadcastInventorySnapshot: false);

            photonView.RPC(nameof(RpcApplyDropPickup), RpcTarget.OthersBuffered, pickupSceneId, (int)hand, slotIndex, dropPosition, dropRotation, dropLinearVelocity, dropAngularVelocity);

            if (broadcastInventorySnapshot)
                BroadcastInventorySnapshotIfNeeded();

            return true;
        }

        ApplyDroppedPickup(
            pickupSource,
            hand,
            slotIndex,
            dropPosition,
            dropRotation,
            dropLinearVelocity,
            dropAngularVelocity,
            refreshInventoryState,
            broadcastInventorySnapshot);
        return true;
    }

    private int CountDeathDroppableSlots()
    {
        return CountDeathDroppableSlots(HandType.Right) + CountDeathDroppableSlots(HandType.Left);
    }

    private int CountDeathDroppableSlots(HandType hand)
    {
        int count = 0;
        for (int slotIndex = 0; slotIndex < SlotsPerHand; slotIndex++)
        {
            if (GetItem(hand, slotIndex) != null)
                count++;
        }

        return count;
    }

    private int CountActiveEquippedSlots()
    {
        int count = 0;
        if (HasActiveItem(HandType.Right))
            count++;

        if (HasActiveItem(HandType.Left))
            count++;

        return count;
    }

    private void ApplyDroppedPickup(
        WorldPickupItem pickupItem,
        HandType hand,
        int slotIndex,
        Vector3 dropPosition,
        Quaternion dropRotation,
        Vector3 dropLinearVelocity,
        Vector3 dropAngularVelocity,
        bool refreshInventoryState = true,
        bool broadcastInventorySnapshot = true)
    {
        if (pickupItem == null)
            return;

        string itemName = pickupItem.ItemName;
        pickupItem.DropToWorld(dropPosition, dropRotation, dropLinearVelocity, dropAngularVelocity);
        pickupItem.IgnoreCollisionWithColliders(GetComponentsInChildren<Collider>(true));

        if (TryFindPickupSlot(pickupItem, out HandType equippedHand, out int equippedSlot))
            ClearSlotReference(equippedHand, equippedSlot, $"{itemName} foi dropado.", refreshInventoryState, broadcastInventorySnapshot);
        else if (IsValidSlotIndex(slotIndex))
            ClearSlotReference(hand, slotIndex, $"{itemName} foi dropado.", refreshInventoryState, broadcastInventorySnapshot);

        lastInteractedHand = hand;
        int slotNumber = IsValidSlotIndex(slotIndex) ? slotIndex + 1 : 0;
        Debug.Log($"[HandEquipmentController] Item dropado: {itemName} do Slot {slotNumber} da mao {GetHandLabel(hand)}.");
    }

    private void ResolveDropPose(WorldPickupItem pickupSource, out Vector3 dropPosition, out Quaternion dropRotation, out Vector3 dropDirection)
    {
        ResolveDropPoint();
        ResolveInteractionCamera();

        Transform directionSource = interactionCamera != null
            ? interactionCamera.transform
            : dropPoint != null ? dropPoint : transform;
        Vector3 planarForward = Vector3.ProjectOnPlane(directionSource.forward, Vector3.up);
        if (planarForward.sqrMagnitude <= 0.0001f)
            planarForward = Vector3.ProjectOnPlane(dropPoint != null ? dropPoint.forward : transform.forward, Vector3.up);
        if (planarForward.sqrMagnitude <= 0.0001f)
            planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (planarForward.sqrMagnitude <= 0.0001f)
            planarForward = Vector3.forward;

        planarForward.Normalize();
        dropDirection = planarForward;

        if (dropPoint != null)
        {
            dropPosition = dropPoint.position;
            dropRotation = pickupSource != null ? pickupSource.transform.rotation : Quaternion.identity;
            return;
        }

        Vector3 candidatePosition = transform.position
            + Vector3.up * dropHeightOffset
            + planarForward * Mathf.Max(0.25f, dropDistance);

        ProjectDropPositionToGround(ref candidatePosition);
        dropPosition = candidatePosition;
        dropRotation = pickupSource != null ? pickupSource.transform.rotation : Quaternion.identity;
    }

    private void ApplyDeathDropScatter(ref Vector3 dropPosition, ref Vector3 dropDirection, int scatterIndex, int scatterCount)
    {
        Vector3 planarDirection = Vector3.ProjectOnPlane(dropDirection, Vector3.up);
        if (planarDirection.sqrMagnitude <= 0.0001f)
            planarDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (planarDirection.sqrMagnitude <= 0.0001f)
            planarDirection = Vector3.forward;

        planarDirection.Normalize();
        int safeScatterCount = Mathf.Max(1, scatterCount);
        float angle = safeScatterCount > 1
            ? 360f * Mathf.Clamp(scatterIndex, 0, safeScatterCount - 1) / safeScatterCount
            : 0f;
        Vector3 scatteredDirection = Quaternion.AngleAxis(angle, Vector3.up) * planarDirection;
        dropPosition += scatteredDirection * DeathDropScatterRadius;
        dropDirection = scatteredDirection;
        ProjectDropPositionToGround(ref dropPosition);
    }

    private void ProjectDropPositionToGround(ref Vector3 candidatePosition)
    {
        Vector3 rayOrigin = candidatePosition + Vector3.up * Mathf.Max(0.25f, dropGroundProbeHeight);
        float rayDistance = dropGroundProbeHeight + Mathf.Max(0.25f, dropGroundProbeDistance);
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit groundHit, rayDistance, dropGroundMask, QueryTriggerInteraction.Ignore))
            candidatePosition = groundHit.point + Vector3.up * dropHeightOffset;
    }

    private void ResolveDropLaunch(HandType hand, Vector3 dropDirection, out Vector3 linearVelocity, out Vector3 angularVelocity)
    {
        Vector3 releaseDirection = dropDirection.sqrMagnitude > 0.0001f ? dropDirection.normalized : transform.forward;
        Vector3 inheritedPlanarVelocity = Vector3.zero;
        if (playerMovement != null)
            inheritedPlanarVelocity = Vector3.ProjectOnPlane(playerMovement.PlanarVelocity, Vector3.up) * Mathf.Clamp01(dropInheritedPlanarVelocityFactor);

        linearVelocity = releaseDirection * Mathf.Max(0f, dropForwardVelocity)
            + Vector3.up * Mathf.Max(0f, dropUpwardVelocity)
            + inheritedPlanarVelocity;

        Vector3 tumbleAxis = Vector3.Cross(Vector3.up, releaseDirection);
        if (tumbleAxis.sqrMagnitude <= 0.0001f)
            tumbleAxis = transform.right;

        tumbleAxis.Normalize();

        float handSpinSign = hand == HandType.Right ? 1f : -1f;
        angularVelocity =
            tumbleAxis * (Mathf.Max(0f, dropTumbleDegreesPerSecond) * Mathf.Deg2Rad * handSpinSign)
            + Vector3.up * (Mathf.Max(0f, dropYawDegreesPerSecond) * Mathf.Deg2Rad * -handSpinSign);
    }

    private void RefreshEquippedVisuals()
    {
        ResolveReferences();
        RefreshHandVisuals(HandType.Right);
        RefreshHandVisuals(HandType.Left);
        UpdateHeldHandAnimationState();
    }

    private void RefreshHandVisuals(HandType hand)
    {
        int activeIndex = GetActiveSlotIndex(hand);
        WorldPickupItem[] pickupSources = GetPickupSources(hand);
        for (int i = 0; i < pickupSources.Length; i++)
        {
            WorldPickupItem pickupSource = pickupSources[i];
            bool shouldBeActive = i == activeIndex && GetItem(hand, i) != null;
            bool hasFirstPersonVisual = RefreshFirstPersonVisual(hand, i, pickupSource, shouldBeActive);

            if (pickupSource == null)
                continue;

            if (pickupSource.gameObject.activeSelf != shouldBeActive)
                pickupSource.gameObject.SetActive(shouldBeActive);

            if (shouldBeActive)
            {
                pickupSource.RefreshEquippedPose();
                bool shouldShowThirdPersonItem = !HasLocalAuthority()
                    || !hideThirdPersonItemForOwner
                    || !hasFirstPersonVisual;
                pickupSource.SetEquippedRenderersVisible(shouldShowThirdPersonItem);
            }
            else
            {
                pickupSource.SetEquippedRenderersVisible(true);
            }
        }
    }

    private bool RefreshFirstPersonVisual(HandType hand, int slotIndex, WorldPickupItem pickupSource, bool shouldBeActive)
    {
        WorldPickupItem[] visuals = GetFirstPersonVisuals(hand);
        WorldPickupItem[] visualSources = GetFirstPersonVisualSources(hand);
        WorldPickupItem currentVisual = visuals[slotIndex];

        if (!ShouldUseFirstPersonItemVisuals() || !shouldBeActive || pickupSource == null)
        {
            if (pickupSource == null || !ShouldUseFirstPersonItemVisuals())
                DestroyFirstPersonVisual(hand, slotIndex);
            else if (currentVisual != null && currentVisual.gameObject.activeSelf)
                currentVisual.gameObject.SetActive(false);

            if (pickupSource == null)
                visualSources[slotIndex] = null;

            return false;
        }

        Transform targetSocket = GetFirstPersonSocket(hand);
        if (targetSocket == null)
        {
            DestroyFirstPersonVisual(hand, slotIndex);
            return false;
        }

        if (currentVisual != null && visualSources[slotIndex] != pickupSource)
        {
            DestroyFirstPersonVisual(hand, slotIndex);
            currentVisual = null;
        }

        if (currentVisual == null)
        {
            currentVisual = pickupSource.CreateEquippedPresentationClone(targetSocket, hand, EquippedItemPerspective.FirstPerson);
            if (currentVisual == null)
                return false;

            visuals[slotIndex] = currentVisual;
            visualSources[slotIndex] = pickupSource;
        }

        if (!currentVisual.gameObject.activeSelf)
            currentVisual.gameObject.SetActive(true);

        if (currentVisual.EquippedSocket != targetSocket
            || currentVisual.EquippedHand != hand
            || currentVisual.EquippedPerspective != EquippedItemPerspective.FirstPerson)
        {
            currentVisual.TryEquipIntoHand(targetSocket, hand, EquippedItemPerspective.FirstPerson);
        }

        ApplyFirstPersonVisualLayer(currentVisual, targetSocket);
        currentVisual.RefreshEquippedPose();
        SyncTorchFlameState(pickupSource, currentVisual);
        currentVisual.SetEquippedRenderersVisible(true);
        return true;
    }

    private bool ShouldUseFirstPersonItemVisuals()
    {
        return createFirstPersonItemVisuals && HasLocalAuthority();
    }

    private void DestroyFirstPersonVisual(HandType hand, int slotIndex)
    {
        WorldPickupItem[] visuals = GetFirstPersonVisuals(hand);
        WorldPickupItem[] visualSources = GetFirstPersonVisualSources(hand);
        WorldPickupItem currentVisual = visuals[slotIndex];
        if (currentVisual != null)
        {
            if (Application.isPlaying)
                Destroy(currentVisual.gameObject);
            else
                DestroyImmediate(currentVisual.gameObject);
        }

        visuals[slotIndex] = null;
        visualSources[slotIndex] = null;
    }

    private WorldPickupItem[] GetFirstPersonVisuals(HandType hand)
    {
        return hand == HandType.Right ? rightHandFirstPersonVisuals : leftHandFirstPersonVisuals;
    }

    private WorldPickupItem[] GetFirstPersonVisualSources(HandType hand)
    {
        return hand == HandType.Right ? rightHandFirstPersonVisualSources : leftHandFirstPersonVisualSources;
    }

    private Transform GetSocket(HandType hand)
    {
        ResolveHandSockets();
        return hand == HandType.Right ? rightHandSocket : leftHandSocket;
    }

    private Transform GetFirstPersonSocket(HandType hand)
    {
        ResolveFirstPersonItemSockets();
        return hand == HandType.Right ? rightHandFirstPersonSocket : leftHandFirstPersonSocket;
    }

    private static void ApplyFirstPersonVisualLayer(WorldPickupItem visualItem, Transform targetSocket)
    {
        if (visualItem == null)
            return;

        int firstPersonLayer = LayerMask.NameToLayer(FirstPersonViewLayerName);
        if (firstPersonLayer < 0 && targetSocket != null)
            firstPersonLayer = targetSocket.gameObject.layer;

        if (firstPersonLayer < 0)
            return;

        SetLayerRecursively(visualItem.transform, firstPersonLayer);
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null || layer < 0)
            return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    private void EnsureUsableSocketScale(Transform socket, HandType hand)
    {
        if (socket == null)
            return;

        Vector3 localScale = socket.localScale;
        if (HasUsableScale(localScale))
            return;

        socket.localScale = Vector3.one;
        Debug.LogWarning(
            $"[HandEquipmentController] Socket da mao {GetHandLabel(hand)} estava com escala zero em '{socket.name}' e foi reajustado para (1, 1, 1) para manter o item visivel.",
            socket);
    }

    private static bool HasUsableScale(Vector3 localScale)
    {
        return Mathf.Abs(localScale.x) > MinSocketScaleMagnitude
            && Mathf.Abs(localScale.y) > MinSocketScaleMagnitude
            && Mathf.Abs(localScale.z) > MinSocketScaleMagnitude;
    }

    private void UpdateHeldHandAnimationState()
    {
        if (!HasLocalAuthority())
            return;

        if (playerMovement == null)
            return;

        playerMovement.SetHandAnimationState(
            HasActiveItem(HandType.Right),
            HasActiveItem(HandType.Left),
            IsLeftTorchEquipped());
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }

    private bool CanChangeInventorySlots(string actionLabel)
    {
        ResolveReferences();

        if (playerMovement == null || playerMovement.CanChangeInventorySlots())
            return true;

        Debug.Log($"[HandEquipmentController] {actionLabel} bloqueado: aguarde a acao atual terminar antes de mexer nos slots.");
        return false;
    }

    private bool CanBeginInventoryItemAction(string actionLabel)
    {
        ResolveReferences();

        if (playerMovement == null || playerMovement.CanBeginInventoryItemAction())
            return true;

        Debug.Log($"[HandEquipmentController] {actionLabel} bloqueado: outra acao do jogador ainda esta em andamento.");
        return false;
    }

    private void BeginInventoryItemActionLock()
    {
        if (playerMovement != null)
            playerMovement.BeginInventoryItemActionLock();
    }

    private void BroadcastInventorySnapshotIfNeeded()
    {
        if (!ShouldUseNetworkSync() || !HasLocalAuthority() || photonView == null)
            return;

        photonView.RPC(
            nameof(RpcApplyInventorySnapshot),
            RpcTarget.Others,
            activeRightHandIndex,
            activeLeftHandIndex,
            GetPickupSceneId(HandType.Right, 0),
            GetPickupSceneId(HandType.Right, 1),
            GetPickupSceneId(HandType.Left, 0),
            GetPickupSceneId(HandType.Left, 1));
    }

    private string GetPickupSceneId(HandType hand, int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
            return string.Empty;

        WorldPickupItem pickupSource = GetPickupSources(hand)[slotIndex];
        return pickupSource != null ? pickupSource.NetworkSceneId : string.Empty;
    }

    private void ApplyInventorySnapshot(
        int rightActiveSlotIndex,
        int leftActiveSlotIndex,
        string rightSlot0PickupId,
        string rightSlot1PickupId,
        string leftSlot0PickupId,
        string leftSlot1PickupId)
    {
        ResolveReferences();

        bool hasChanges = false;

        int resolvedRightActiveSlotIndex = IsValidSlotIndex(rightActiveSlotIndex)
            ? rightActiveSlotIndex
            : activeRightHandIndex;
        int resolvedLeftActiveSlotIndex = IsValidSlotIndex(leftActiveSlotIndex)
            ? leftActiveSlotIndex
            : activeLeftHandIndex;

        if (activeRightHandIndex != resolvedRightActiveSlotIndex)
        {
            activeRightHandIndex = resolvedRightActiveSlotIndex;
            hasChanges = true;
        }

        if (activeLeftHandIndex != resolvedLeftActiveSlotIndex)
        {
            activeLeftHandIndex = resolvedLeftActiveSlotIndex;
            hasChanges = true;
        }

        hasChanges |= SyncInventorySlotFromSnapshot(HandType.Right, 0, rightSlot0PickupId);
        hasChanges |= SyncInventorySlotFromSnapshot(HandType.Right, 1, rightSlot1PickupId);
        hasChanges |= SyncInventorySlotFromSnapshot(HandType.Left, 0, leftSlot0PickupId);
        hasChanges |= SyncInventorySlotFromSnapshot(HandType.Left, 1, leftSlot1PickupId);

        if (!hasChanges)
            return;

        RefreshEquippedVisuals();
        NotifyStateChanged();
    }

    private bool SyncInventorySlotFromSnapshot(HandType hand, int slotIndex, string pickupSceneId)
    {
        if (!IsValidSlotIndex(slotIndex))
            return false;

        ItemDefinition[] slots = GetSlots(hand);
        WorldPickupItem[] pickupSources = GetPickupSources(hand);
        WorldPickupItem currentPickup = pickupSources[slotIndex];

        if (string.IsNullOrWhiteSpace(pickupSceneId))
        {
            if (slots[slotIndex] == null && currentPickup == null)
                return false;

            Transform targetSocket = GetSocket(hand);
            if (currentPickup != null
                && currentPickup.IsEquipped
                && targetSocket != null
                && currentPickup.EquippedSocket == targetSocket)
            {
                currentPickup.gameObject.SetActive(false);
            }

            slots[slotIndex] = null;
            pickupSources[slotIndex] = null;
            return true;
        }

        if (!WorldPickupItem.TryFindByNetworkSceneId(pickupSceneId, out WorldPickupItem pickupItem))
        {
            Debug.LogWarning($"[HandEquipmentController] Snapshot remoto nao encontrou pickup com id '{pickupSceneId}'.");
            return false;
        }

        ItemDefinition pickupDefinition = pickupItem.ItemDefinition;
        if (pickupDefinition == null)
            return false;

        Transform expectedSocket = GetSocket(hand);
        bool slotAlreadyMatches = currentPickup == pickupItem
            && slots[slotIndex] == pickupDefinition
            && pickupItem.IsEquipped
            && expectedSocket != null
            && pickupItem.EquippedSocket == expectedSocket;

        if (slotAlreadyMatches)
            return false;

        if (currentPickup != null && currentPickup != pickupItem)
        {
            slots[slotIndex] = null;
            pickupSources[slotIndex] = null;
        }

        return ApplyEquipIntoSlot(hand, slotIndex, pickupItem, logOnFailure: false, focusInEditor: false);
    }

    private static string GetHandLabel(HandType hand)
    {
        return hand == HandType.Right ? "direita" : "esquerda";
    }

    private bool IsLeftTorchEquipped()
    {
        ItemDefinition activeLeftItem = GetItem(HandType.Left, GetActiveSlotIndex(HandType.Left));
        return IsTorchItem(activeLeftItem);
    }

    private bool IsTorchItem(ItemDefinition itemDefinition)
    {
        if (itemDefinition == null)
            return false;

        if (torchItemDefinition != null && itemDefinition == torchItemDefinition)
            return true;

        return string.Equals(itemDefinition.ItemName, TorchItemFallbackName, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveEquipRequest(ItemDefinition itemDefinition, out HandType hand, out int slotIndex)
    {
        hand = HandType.Right;
        slotIndex = InvalidSlotIndex;

        if (itemDefinition == null)
            return false;

        switch (itemDefinition.HandRequirement)
        {
            case HandRequirement.RightOnly:
                hand = HandType.Right;
                return TryResolveEquipIntoActiveSlot(hand, itemDefinition, out slotIndex, logOnFailure: true);
            case HandRequirement.LeftOnly:
                hand = HandType.Left;
                return TryResolveEquipIntoActiveSlot(hand, itemDefinition, out slotIndex, logOnFailure: true);
            case HandRequirement.Any:
            {
                HandType firstHand = itemDefinition.PreferredHand;
                HandType secondHand = firstHand == HandType.Right ? HandType.Left : HandType.Right;

                if (TryResolveEquipIntoActiveSlot(firstHand, itemDefinition, out slotIndex, logOnFailure: false))
                {
                    hand = firstHand;
                    return true;
                }

                if (TryResolveEquipIntoActiveSlot(secondHand, itemDefinition, out slotIndex, logOnFailure: false))
                {
                    hand = secondHand;
                    return true;
                }

                int rightSlot = activeRightHandIndex + 1;
                int leftSlot = activeLeftHandIndex + 1;
                string rightItemName = rightHandSlots[activeRightHandIndex] != null ? rightHandSlots[activeRightHandIndex].ItemName : "vazio";
                string leftItemName = leftHandSlots[activeLeftHandIndex] != null ? leftHandSlots[activeLeftHandIndex].ItemName : "vazio";

                Debug.Log($"[HandEquipmentController] Nao foi possivel equipar '{itemDefinition.ItemName}': Slot {rightSlot} da mao direita = {rightItemName}, Slot {leftSlot} da mao esquerda = {leftItemName}.");
                return false;
            }
            case HandRequirement.TwoHanded:
                Debug.LogWarning($"[HandEquipmentController] '{itemDefinition.ItemName}' e TwoHanded. Essa regra ainda nao foi implementada.");
                return false;
            default:
                Debug.LogWarning($"[HandEquipmentController] HandRequirement desconhecido para '{itemDefinition.ItemName}'.");
                return false;
        }
    }

    private bool TryResolveEquipIntoActiveSlot(HandType hand, ItemDefinition itemDefinition, out int slotIndex, bool logOnFailure)
    {
        slotIndex = GetActiveSlotIndex(hand);

        if (itemDefinition == null)
            return false;

        if (!itemDefinition.CanEquipInHand(hand))
        {
            if (logOnFailure)
                Debug.Log($"[HandEquipmentController] Mao incompativel: '{itemDefinition.ItemName}' nao pode ser equipado na mao {GetHandLabel(hand)}.");

            return false;
        }

        ItemDefinition[] slots = GetSlots(hand);
        if (slots[slotIndex] != null)
        {
            if (logOnFailure)
                Debug.Log($"[HandEquipmentController] Slot ocupado: Slot {slotIndex + 1} da mao {GetHandLabel(hand)} ja contem '{slots[slotIndex].ItemName}'.");

            return false;
        }

        Transform targetSocket = GetSocket(hand);
        if (targetSocket != null)
            return true;

        if (logOnFailure)
            Debug.LogWarning($"[HandEquipmentController] Socket da mao {GetHandLabel(hand)} nao encontrado para equipar '{itemDefinition.ItemName}'.", gameObject);

        return false;
    }

    private bool ApplySetActiveSlot(HandType hand, int slotIndex, bool logResult)
    {
        if (!IsValidSlotIndex(slotIndex))
            return false;

        ResolveReferences();

        int previousSlotIndex = GetActiveSlotIndex(hand);
        ItemDefinition previousActiveItem = IsValidSlotIndex(previousSlotIndex)
            ? GetItem(hand, previousSlotIndex)
            : null;
        WorldPickupItem previousActivePickup = IsValidSlotIndex(previousSlotIndex)
            ? GetPickupSources(hand)[previousSlotIndex]
            : null;

        lastInteractedHand = hand;

        if (hand == HandType.Right)
            activeRightHandIndex = slotIndex;
        else
            activeLeftHandIndex = slotIndex;

        bool shouldTriggerDrawAnimation = ShouldTriggerDrawAnimation(
            hand,
            previousSlotIndex,
            slotIndex,
            previousActiveItem,
            previousActivePickup);

        if (logResult)
            Debug.Log($"[HandEquipmentController] Slot alternado: mao {GetHandLabel(hand)} agora esta no Slot {slotIndex + 1}.");

        RefreshEquippedVisuals();

        if (shouldTriggerDrawAnimation)
            playerMovement.TriggerDrawAnimation(hand);

        NotifyStateChanged();
        BroadcastInventorySnapshotIfNeeded();
        return true;
    }

    private bool ShouldTriggerDrawAnimation(
        HandType hand,
        int previousSlotIndex,
        int nextSlotIndex,
        ItemDefinition previousActiveItem,
        WorldPickupItem previousActivePickup)
    {
        if (!HasLocalAuthority() || playerMovement == null)
            return false;

        if (previousSlotIndex == nextSlotIndex)
            return false;

        ItemDefinition nextActiveItem = GetItem(hand, nextSlotIndex);
        if (nextActiveItem == null)
            return false;

        WorldPickupItem nextActivePickup = GetPickupSources(hand)[nextSlotIndex];
        if (nextActivePickup != null)
            return nextActivePickup != previousActivePickup;

        return nextActiveItem != previousActiveItem;
    }

    private bool ApplyEquipIntoSlot(HandType hand, int slotIndex, WorldPickupItem pickupItem, bool logOnFailure, bool focusInEditor)
    {
        ResolveReferences();

        if (!IsValidSlotIndex(slotIndex) || pickupItem == null)
            return false;

        ItemDefinition itemDefinition = pickupItem.ItemDefinition;
        if (itemDefinition == null)
        {
            if (logOnFailure)
                Debug.LogWarning($"[HandEquipmentController] '{pickupItem.gameObject.name}' esta sem ItemDefinition.", pickupItem);

            return false;
        }

        if (!itemDefinition.CanEquipInHand(hand))
        {
            if (logOnFailure)
                Debug.Log($"[HandEquipmentController] Mao incompativel: '{itemDefinition.ItemName}' nao pode ser equipado na mao {GetHandLabel(hand)}.");

            return false;
        }

        ItemDefinition[] slots = GetSlots(hand);
        WorldPickupItem[] pickupSources = GetPickupSources(hand);
        Transform targetSocket = GetSocket(hand);
        bool slotAlreadyMatches = pickupSources[slotIndex] == pickupItem
            && slots[slotIndex] == itemDefinition
            && pickupItem.IsEquipped
            && targetSocket != null
            && pickupItem.EquippedSocket == targetSocket;
        if (slotAlreadyMatches)
        {
            RefreshEquippedVisuals();
            NotifyStateChanged();
            return true;
        }

        if (pickupItem.IsEquipped && pickupSources[slotIndex] != pickupItem)
        {
            if (logOnFailure)
                Debug.Log($"[HandEquipmentController] '{itemDefinition.ItemName}' ja foi equipado por outro jogador ou slot.");

            return false;
        }

        if (slots[slotIndex] != null && pickupSources[slotIndex] != pickupItem)
        {
            if (logOnFailure)
                Debug.Log($"[HandEquipmentController] Slot ocupado: Slot {slotIndex + 1} da mao {GetHandLabel(hand)} ja contem '{slots[slotIndex].ItemName}'.");

            return false;
        }

        if (targetSocket == null)
        {
            if (logOnFailure)
                Debug.LogWarning($"[HandEquipmentController] Socket da mao {GetHandLabel(hand)} nao encontrado para equipar '{pickupItem.ItemName}'.", gameObject);

            return false;
        }

        if (!pickupItem.TryEquipIntoHand(targetSocket, hand))
        {
            if (logOnFailure)
                Debug.LogWarning($"[HandEquipmentController] Falha ao equipar '{pickupItem.ItemName}' na mao {GetHandLabel(hand)}.", pickupItem);

            return false;
        }

        slots[slotIndex] = itemDefinition;
        pickupSources[slotIndex] = pickupItem;
        lastInteractedHand = hand;

        if (HasLocalAuthority() && playerMovement != null)
            playerMovement.TriggerPickupAnimation(hand);

        Debug.Log($"[HandEquipmentController] Item equipado: {itemDefinition.ItemName} no Slot {slotIndex + 1} da mao {GetHandLabel(hand)}.");
        RefreshEquippedVisuals();
        if (focusInEditor && HasLocalAuthority())
            FocusEquippedItemForAuthoring(pickupItem);
        NotifyStateChanged();
        BroadcastInventorySnapshotIfNeeded();
        return true;
    }

    private bool TryFindPickupSlot(WorldPickupItem pickupItem, out HandType hand, out int slotIndex)
    {
        hand = HandType.Right;
        slotIndex = InvalidSlotIndex;

        if (pickupItem == null)
            return false;

        for (int i = 0; i < rightHandPickupSources.Length; i++)
        {
            if (rightHandPickupSources[i] != pickupItem)
                continue;

            hand = HandType.Right;
            slotIndex = i;
            return true;
        }

        for (int i = 0; i < leftHandPickupSources.Length; i++)
        {
            if (leftHandPickupSources[i] != pickupItem)
                continue;

            hand = HandType.Left;
            slotIndex = i;
            return true;
        }

        return false;
    }

    private bool IsValidSlotIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < SlotsPerHand;
    }

    private int GetNextSlotIndex(HandType hand)
    {
        return (GetActiveSlotIndex(hand) + 1) % SlotsPerHand;
    }

    private bool IsInventoryActionAuthorized(PhotonMessageInfo info)
    {
        return photonView == null
            || photonView.Owner == null
            || info.Sender == null
            || photonView.OwnerActorNr == info.Sender.ActorNumber;
    }

    [PunRPC]
    private void RpcApplySetActiveSlot(int handValue, int slotIndex, PhotonMessageInfo info)
    {
        if (!IsInventoryActionAuthorized(info))
            return;

        ApplySetActiveSlot((HandType)handValue, slotIndex, logResult: true);
    }

    [PunRPC]
    private void RpcApplyEquipPickup(string pickupSceneId, int handValue, int slotIndex, PhotonMessageInfo info)
    {
        if (!IsInventoryActionAuthorized(info))
            return;

        if (!WorldPickupItem.TryFindByNetworkSceneId(pickupSceneId, out WorldPickupItem pickupItem))
        {
            Debug.LogWarning($"[HandEquipmentController] Pickup com id '{pickupSceneId}' nao foi encontrado para equipar.");
            return;
        }

        ApplyEquipIntoSlot((HandType)handValue, slotIndex, pickupItem, logOnFailure: true, focusInEditor: false);
    }

    [PunRPC]
    private void RpcApplyDropPickup(string pickupSceneId, int handValue, int slotIndex, Vector3 dropPosition, Quaternion dropRotation, Vector3 dropLinearVelocity, Vector3 dropAngularVelocity, PhotonMessageInfo info)
    {
        if (!IsInventoryActionAuthorized(info))
            return;

        WorldPickupItem pickupItem = null;
        if (!string.IsNullOrWhiteSpace(pickupSceneId))
            WorldPickupItem.TryFindByNetworkSceneId(pickupSceneId, out pickupItem);

        if (pickupItem == null && IsValidSlotIndex(slotIndex))
            pickupItem = GetPickupSources((HandType)handValue)[slotIndex];

        if (pickupItem == null)
        {
            Debug.LogWarning($"[HandEquipmentController] Pickup com id '{pickupSceneId}' nao foi encontrado para drop.");
            return;
        }

        ApplyDroppedPickup(pickupItem, (HandType)handValue, slotIndex, dropPosition, dropRotation, dropLinearVelocity, dropAngularVelocity);
    }

    [PunRPC]
    private void RpcApplyConsumePickup(string pickupSceneId, int handValue, int slotIndex, PhotonMessageInfo info)
    {
        if (!IsInventoryActionAuthorized(info))
            return;

        WorldPickupItem pickupItem = null;
        if (!string.IsNullOrWhiteSpace(pickupSceneId))
            WorldPickupItem.TryFindByNetworkSceneId(pickupSceneId, out pickupItem);

        if (pickupItem == null && IsValidSlotIndex(slotIndex))
            pickupItem = GetPickupSources((HandType)handValue)[slotIndex];

        string itemName = pickupItem != null ? pickupItem.ItemName : GetItem((HandType)handValue, slotIndex)?.ItemName ?? "Item";
        if (pickupItem != null)
            pickupItem.DestroyAfterUse();

        if (pickupItem != null && TryFindPickupSlot(pickupItem, out HandType equippedHand, out int equippedSlot))
            ClearSlotReference(equippedHand, equippedSlot, $"{itemName} foi consumido apos o uso.");
        else if (IsValidSlotIndex(slotIndex))
            ClearSlotReference((HandType)handValue, slotIndex, $"{itemName} foi consumido apos o uso.");
    }

    [PunRPC]
    private void RpcApplyTorchLitState(string pickupSceneId, int handValue, int slotIndex, bool lit, PhotonMessageInfo info)
    {
        if (!IsInventoryActionAuthorized(info))
            return;

        ApplyTorchLitState(pickupSceneId, (HandType)handValue, slotIndex, lit);
    }

    [PunRPC]
    private void RpcPlayMeleeWeaponTrail(int handValue, int slotIndex, PhotonMessageInfo info)
    {
        if (!IsInventoryActionAuthorized(info))
            return;

        PlayMeleeWeaponTrail((HandType)handValue, slotIndex, broadcast: false);
    }

    [PunRPC]
    private void RpcApplyInventorySnapshot(
        int rightActiveSlotIndex,
        int leftActiveSlotIndex,
        string rightSlot0PickupId,
        string rightSlot1PickupId,
        string leftSlot0PickupId,
        string leftSlot1PickupId,
        PhotonMessageInfo info)
    {
        if (!IsInventoryActionAuthorized(info))
            return;

        ApplyInventorySnapshot(
            rightActiveSlotIndex,
            leftActiveSlotIndex,
            rightSlot0PickupId,
            rightSlot1PickupId,
            leftSlot0PickupId,
            leftSlot1PickupId);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void FocusEquippedItemForAuthoring(WorldPickupItem pickupItem)
    {
#if UNITY_EDITOR
        if (pickupItem == null)
            return;

        UnityEditor.Selection.activeGameObject = pickupItem.gameObject;
        UnityEditor.EditorGUIUtility.PingObject(pickupItem.gameObject);
#endif
    }
}
