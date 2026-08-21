using System;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView))]
public class PlayerPickupInteractor : MonoBehaviour
{
    private const string FirstPersonCameraName = "FP_Camera";
    private const int MaxHits = 8;
    private const string PasscodeFieldControlName = "DoorPasscodeField";
    private const float PasscodePanelWidth = 320f;
    private const float PasscodePanelHeight = 140f;

    [FormerlySerializedAs("pickupKey")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] [Min(0.5f)] private float pickupDistance = 3f;
    [SerializeField] [Min(0f)] private float pickupSphereRadius = 0.25f;
    [FormerlySerializedAs("pickupMask")]
    [SerializeField] private LayerMask interactionMask = Physics.DefaultRaycastLayers;
    [SerializeField] private Camera interactionCamera;
    [SerializeField] private HandEquipmentController handEquipmentController;
    [SerializeField] private PhotonView photonView;
    [Header("Pickup Outline")]
    [SerializeField] private bool highlightPickupUnderAim = true;
    [SerializeField] [Min(0.5f)] private float outlineDistance = 3f;
    [SerializeField] [Min(0f)] private float outlineRayRadius;
    [SerializeField] private LayerMask outlineMask = Physics.DefaultRaycastLayers;

    private readonly RaycastHit[] sphereCastHits = new RaycastHit[MaxHits];
    private readonly RaycastHit[] outlineHits = new RaycastHit[MaxHits];
    private DoorController activePasscodeDoor;
    private string passcodeBuffer = string.Empty;
    private bool focusPasscodeField;
    private WorldPickupItem highlightedPickup;
    private Outline highlightedOutline;
    private bool highlightedOutlineWasEnabled;

    public HandEquipmentController HandEquipmentController
    {
        get
        {
            ResolveReferences();
            return handEquipmentController;
        }
    }

    public bool IsInteractionUiOpen => activePasscodeDoor != null;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (!HasLocalAuthority())
        {
            ClearPickupOutline();
            return;
        }

        if (activePasscodeDoor != null)
        {
            ClearPickupOutline();

            if (!activePasscodeDoor.isActiveAndEnabled || !activePasscodeDoor.IsLocked)
                CancelPasscodeEntry();

            return;
        }

        UpdatePickupOutline();

        if (Input.GetKeyDown(interactKey))
            TryInteract();
    }

    private void OnDisable()
    {
        ClearPickupOutline();
    }

    private void OnGUI()
    {
        if (!HasLocalAuthority() || activePasscodeDoor == null)
            return;

        Rect panelRect = new Rect(
            (Screen.width - PasscodePanelWidth) * 0.5f,
            (Screen.height - PasscodePanelHeight) * 0.5f,
            PasscodePanelWidth,
            PasscodePanelHeight);

        GUI.Box(panelRect, string.Empty);
        GUI.Label(
            new Rect(panelRect.x + 16f, panelRect.y + 12f, panelRect.width - 32f, 36f),
            $"{activePasscodeDoor.DisplayName}\nDigite a senha para destrancar.");

        GUI.SetNextControlName(PasscodeFieldControlName);
        passcodeBuffer = GUI.TextField(
            new Rect(panelRect.x + 16f, panelRect.y + 56f, panelRect.width - 32f, 24f),
            passcodeBuffer,
            32);

        if (focusPasscodeField)
        {
            GUI.FocusControl(PasscodeFieldControlName);
            focusPasscodeField = false;
        }

        if (GUI.Button(new Rect(panelRect.x + 16f, panelRect.y + 96f, 136f, 28f), "Confirmar"))
            SubmitPasscode();

        if (GUI.Button(new Rect(panelRect.xMax - 152f, panelRect.y + 96f, 136f, 28f), "Cancelar"))
            CancelPasscodeEntry();

        Event currentEvent = Event.current;
        if (currentEvent.type != EventType.KeyDown)
            return;

        if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
        {
            SubmitPasscode();
            currentEvent.Use();
        }
        else if (currentEvent.keyCode == KeyCode.Escape)
        {
            CancelPasscodeEntry();
            currentEvent.Use();
        }
    }

    public bool TryInteract()
    {
        if (!TryResolveInteractionCamera())
            return false;

        int hitCount = PerformInteractionCast();
        if (TryFindBestInteractable(hitCount, out IPlayerInteractable interactable))
            return interactable.TryInteract(this);

        return TryPickupItemFromHits(hitCount);
    }

    public bool TryPickupItem()
    {
        if (!TryResolveInteractionCamera())
            return false;

        return TryPickupItemFromHits(PerformInteractionCast());
    }

    public void BeginPasscodeEntry(DoorController door)
    {
        if (door == null)
            return;

        activePasscodeDoor = door;
        passcodeBuffer = string.Empty;
        focusPasscodeField = true;
    }

    public void CancelPasscodeEntry()
    {
        activePasscodeDoor = null;
        passcodeBuffer = string.Empty;
        focusPasscodeField = false;
    }

    private void SubmitPasscode()
    {
        if (activePasscodeDoor == null)
            return;

        DoorController targetDoor = activePasscodeDoor;
        bool unlockedDoor = targetDoor.TrySubmitPasscode(passcodeBuffer, this);
        if (unlockedDoor || !targetDoor.IsLocked)
            CancelPasscodeEntry();
    }

    private bool TryResolveInteractionCamera()
    {
        ResolveReferences();

        if (interactionCamera != null)
            return true;

        Debug.LogWarning("[PlayerPickupInteractor] Nenhuma camera local foi encontrada para interacao.", gameObject);
        return false;
    }

    private int PerformInteractionCast()
    {
        Vector3 origin = interactionCamera.transform.position;
        Vector3 direction = interactionCamera.transform.forward;
        return Physics.SphereCastNonAlloc(
            origin,
            pickupSphereRadius,
            direction,
            sphereCastHits,
            pickupDistance,
            DestructibleDebrisCollision.ExcludeDebrisLayer(interactionMask.value),
            QueryTriggerInteraction.Collide);
    }

    private bool TryPickupItemFromHits(int hitCount)
    {
        ResolveReferences();

        if (handEquipmentController == null)
        {
            Debug.LogWarning("[PlayerPickupInteractor] HandEquipmentController nao encontrado.", gameObject);
            return false;
        }

        WorldPickupItem bestPickup = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = sphereCastHits[i];
            if (hit.collider == null)
                continue;

            if (hit.collider.transform.IsChildOf(transform))
                continue;

            WorldPickupItem pickupItem = hit.collider.GetComponentInParent<WorldPickupItem>();
            if (pickupItem == null || !pickupItem.gameObject.activeInHierarchy)
                continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                bestPickup = pickupItem;
            }
        }

        if (bestPickup == null)
        {
            Debug.Log("[PlayerPickupInteractor] Nenhum item coletavel encontrado na frente do jogador.");
            return false;
        }

        bool wasHighlightedPickup = bestPickup == highlightedPickup;
        if (wasHighlightedPickup)
            ClearPickupOutline(forceDisable: true);

        bool equipped = handEquipmentController.TryEquipWorldItem(bestPickup);
        if (!equipped && wasHighlightedPickup)
            UpdatePickupOutline();

        return equipped;
    }

    private bool TryFindBestInteractable(int hitCount, out IPlayerInteractable bestInteractable)
    {
        bestInteractable = null;
        int bestPriority = int.MinValue;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = sphereCastHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                continue;

            if (!TryGetInteractableFromCollider(hit.collider, out IPlayerInteractable interactable))
                continue;

            bool shouldReplace = interactable.InteractionPriority > bestPriority;
            if (!shouldReplace
                && interactable.InteractionPriority == bestPriority
                && hit.distance < bestDistance)
            {
                shouldReplace = true;
            }

            if (!shouldReplace)
                continue;

            bestInteractable = interactable;
            bestPriority = interactable.InteractionPriority;
            bestDistance = hit.distance;
        }

        return bestInteractable != null;
    }

    private static bool TryGetInteractableFromCollider(Collider collider, out IPlayerInteractable bestInteractable)
    {
        bestInteractable = null;
        if (collider == null)
            return false;

        MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>(true);
        int bestPriority = int.MinValue;

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is not IPlayerInteractable interactable)
                continue;

            if (interactable.InteractionPriority <= bestPriority)
                continue;

            bestPriority = interactable.InteractionPriority;
            bestInteractable = interactable;
        }

        return bestInteractable != null;
    }

    private void ResolveReferences()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (handEquipmentController == null)
            handEquipmentController = GetComponent<HandEquipmentController>();

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

    private bool HasLocalAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private void UpdatePickupOutline()
    {
        if (!highlightPickupUnderAim)
        {
            ClearPickupOutline();
            return;
        }

        ResolveReferences();
        if (interactionCamera == null)
        {
            ClearPickupOutline();
            return;
        }

        SetHighlightedPickup(FindPickupUnderAim());
    }

    private WorldPickupItem FindPickupUnderAim()
    {
        Transform cameraTransform = interactionCamera.transform;
        Vector3 origin = cameraTransform.position;
        Vector3 direction = cameraTransform.forward;

        int hitCount = outlineRayRadius > 0f
            ? Physics.SphereCastNonAlloc(
                origin,
                outlineRayRadius,
                direction,
                outlineHits,
                outlineDistance,
                DestructibleDebrisCollision.ExcludeDebrisLayer(outlineMask.value),
                QueryTriggerInteraction.Collide)
            : Physics.RaycastNonAlloc(
                origin,
                direction,
                outlineHits,
                outlineDistance,
                DestructibleDebrisCollision.ExcludeDebrisLayer(outlineMask.value),
                QueryTriggerInteraction.Collide);

        WorldPickupItem closestPickup = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = outlineHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            TryGetPickupFromHit(hit, out closestPickup);
        }

        return closestPickup;
    }

    private bool TryGetPickupFromHit(RaycastHit hit, out WorldPickupItem pickupItem)
    {
        pickupItem = null;

        if (hit.collider == null)
            return false;

        if (hit.collider.transform.IsChildOf(transform))
            return false;

        WorldPickupItem candidate = hit.collider.GetComponentInParent<WorldPickupItem>();
        if (!IsValidPickupOutlineTarget(candidate))
            return false;

        pickupItem = candidate;
        return true;
    }

    private static bool IsValidPickupOutlineTarget(WorldPickupItem pickupItem)
    {
        return pickupItem != null
            && pickupItem.gameObject.activeInHierarchy
            && !pickupItem.IsEquipped
            && !pickupItem.IsPresentationClone;
    }

    private void SetHighlightedPickup(WorldPickupItem pickupItem)
    {
        if (highlightedPickup == pickupItem)
            return;

        ClearPickupOutline();
        if (pickupItem == null)
            return;

        Outline outline = ResolvePickupOutline(pickupItem);
        if (outline == null)
            return;

        highlightedPickup = pickupItem;
        highlightedOutline = outline;
        highlightedOutlineWasEnabled = outline.enabled;

        outline.enabled = true;
    }

    private Outline ResolvePickupOutline(WorldPickupItem pickupItem)
    {
        Outline outline = pickupItem.GetComponent<Outline>();
        if (outline == null)
            outline = pickupItem.GetComponentInChildren<Outline>(true);

        return outline;
    }

    private void ClearPickupOutline(bool forceDisable = false)
    {
        if (highlightedOutline != null)
            highlightedOutline.enabled = !forceDisable && highlightedOutlineWasEnabled;

        highlightedPickup = null;
        highlightedOutline = null;
        highlightedOutlineWasEnabled = false;
    }
}
