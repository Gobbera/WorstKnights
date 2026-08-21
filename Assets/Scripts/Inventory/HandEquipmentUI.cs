using System;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class HandEquipmentUI : MonoBehaviour
{
    private static readonly string[] InventoryRootNames = { "Inventory", "Invetory" };

    [Serializable]
    private sealed class SlotView
    {
        public RectTransform slotRoot;
        public Image slotImage;

        private bool defaultsCached;
        private Sprite emptySprite;
        private Color emptyColor;

        public void ResolveAndCacheDefaults()
        {
            if (slotRoot != null && slotImage == null)
                slotImage = slotRoot.GetComponent<Image>();

            if (slotImage == null || defaultsCached)
                return;

            emptySprite = slotImage.sprite;
            emptyColor = slotImage.color;
            defaultsCached = true;
        }

        public void Apply(ItemDefinition itemDefinition, bool isActive)
        {
            ResolveAndCacheDefaults();

            if (slotRoot != null)
                slotRoot.localScale = isActive ? Vector3.one * 1.1f : Vector3.one;

            if (slotImage == null)
                return;

            if (itemDefinition == null)
            {
                slotImage.sprite = emptySprite;
                slotImage.color = emptyColor;
                return;
            }

            slotImage.sprite = itemDefinition.UiSprite != null ? itemDefinition.UiSprite : emptySprite;
            slotImage.color = itemDefinition.UiSprite != null ? Color.white : emptyColor;
        }
    }

    [SerializeField] private HandEquipmentController targetController;
    [SerializeField] private SlotView slot1LeftHand = new SlotView();
    [SerializeField] private SlotView slot2LeftHand = new SlotView();
    [SerializeField] private SlotView slot1RightHand = new SlotView();
    [SerializeField] private SlotView slot2RightHand = new SlotView();

    public static void BindLocalPlayer(HandEquipmentController controller)
    {
        if (controller == null)
            return;

        HandEquipmentUI ui = FindOrCreateSceneController();
        if (ui == null)
        {
            Debug.LogWarning("[HandEquipmentUI] Nao foi possivel encontrar o root da UI Inventory/Invetory.", controller);
            return;
        }

        ui.SetTarget(controller);
    }

    private void Awake()
    {
        ResolveSceneReferences();
        RefreshVisuals();
    }

    private void Update()
    {
        ResolveSceneReferences();

        if (targetController == null)
            targetController = FindLocalController();

        RefreshVisuals();
    }

    private void SetTarget(HandEquipmentController controller)
    {
        targetController = controller;
        ResolveSceneReferences();
        RefreshVisuals();
    }

    private void ResolveSceneReferences()
    {
        Transform inventoryRoot = transform;
        if (!IsInventoryRoot(inventoryRoot))
        {
            inventoryRoot = FindSceneTransformByNames(InventoryRootNames);
            if (inventoryRoot == null)
                return;
        }

        TryResolveSlot(inventoryRoot, "Slot1LeftHand", slot1LeftHand);
        TryResolveSlot(inventoryRoot, "Slot2LeftHand", slot2LeftHand);
        TryResolveSlot(inventoryRoot, "Slot1RightHand", slot1RightHand);
        TryResolveSlot(inventoryRoot, "Slot2RightHand", slot2RightHand);
    }

    private void RefreshVisuals()
    {
        if (targetController == null)
            return;

        slot1RightHand.Apply(targetController.GetItem(HandType.Right, 0), targetController.GetActiveSlotIndex(HandType.Right) == 0);
        slot2RightHand.Apply(targetController.GetItem(HandType.Right, 1), targetController.GetActiveSlotIndex(HandType.Right) == 1);
        slot1LeftHand.Apply(targetController.GetItem(HandType.Left, 0), targetController.GetActiveSlotIndex(HandType.Left) == 0);
        slot2LeftHand.Apply(targetController.GetItem(HandType.Left, 1), targetController.GetActiveSlotIndex(HandType.Left) == 1);
    }

    private static void TryResolveSlot(Transform root, string slotName, SlotView slotView)
    {
        if (slotView.slotRoot == null)
        {
            Transform slotTransform = FindChildByName(root, slotName);
            if (slotTransform != null)
                slotView.slotRoot = slotTransform as RectTransform;
        }

        slotView.ResolveAndCacheDefaults();
    }

    private static HandEquipmentUI FindOrCreateSceneController()
    {
        Transform inventoryRoot = FindSceneTransformByNames(InventoryRootNames);
        if (inventoryRoot == null)
            return null;

        HandEquipmentUI controller = inventoryRoot.GetComponent<HandEquipmentUI>();
        if (controller == null)
            controller = inventoryRoot.gameObject.AddComponent<HandEquipmentUI>();

        return controller;
    }

    private static HandEquipmentController FindLocalController()
    {
        HandEquipmentController[] controllers = FindObjectsByType<HandEquipmentController>(FindObjectsInactive.Include);
        for (int i = 0; i < controllers.Length; i++)
        {
            HandEquipmentController controller = controllers[i];
            if (controller == null)
                continue;

            PhotonView controllerView = controller.GetComponent<PhotonView>();
            if (controllerView == null || controllerView.IsMine)
                return controller;
        }

        return null;
    }

    private static Transform FindSceneTransformByNames(string[] objectNames)
    {
        Transform[] sceneTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform sceneTransform = sceneTransforms[i];
            if (sceneTransform == null)
                continue;

            for (int j = 0; j < objectNames.Length; j++)
            {
                if (string.Equals(sceneTransform.name, objectNames[j], StringComparison.Ordinal))
                    return sceneTransform;
            }
        }

        return null;
    }

    private static Transform FindChildByName(Transform root, string objectName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, objectName, StringComparison.Ordinal))
                return child;
        }

        return null;
    }

    private static bool IsInventoryRoot(Transform candidate)
    {
        if (candidate == null)
            return false;

        for (int i = 0; i < InventoryRootNames.Length; i++)
        {
            if (string.Equals(candidate.name, InventoryRootNames[i], StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
