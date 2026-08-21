using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum EquippedItemPerspective
{
    ThirdPerson = 0,
    FirstPerson = 1
}

[DisallowMultipleComponent]
public class WorldPickupItem : MonoBehaviour
{
    private static readonly Dictionary<string, WorldPickupItem> SceneItemRegistry = new Dictionary<string, WorldPickupItem>(StringComparer.Ordinal);
    private static bool createAsPresentationClone;
    private const string PickupColliderHostName = "PickupTrigger";
    private const string DropCollisionHostName = "DropCollision";
    private const string LegacyGripPointObjectName = "GripPoint";
    private const string RightGripPointObjectName = "GripPoints_TP_Right";
    private const string LeftGripPointObjectName = "GripPoints_TP_Left";
    private const string FirstPersonRightGripPointObjectName = "GripPoints_FPS_Right";
    private const string FirstPersonLeftGripPointObjectName = "GripPoints_FPS_Left";
    private const string LegacyRightGripPointObjectName = "GripPoint_Right";
    private const string LegacyLeftGripPointObjectName = "GripPoint_Left";
    private const string LegacyFirstPersonRightGripPointObjectName = "GripPoint_FPS_Right";
    private const string LegacyFirstPersonLeftGripPointObjectName = "GripPoint_FPS_Left";
    private const float DefaultDropPhysicsMass = 0.35f;

    [Serializable]
    private sealed class EquippedHandPose
    {
        public Transform gripPoint;
    }

    [Header("Item Data")]
    [SerializeField] private ItemDefinition itemDefinition;
    [Header("Networking")]
    [SerializeField] private string networkSceneId = string.Empty;
    [SerializeField] [HideInInspector] private Transform pickupColliderHost;
    [Header("Grip Points")]
    [SerializeField] private EquippedHandPose rightHandPose = new EquippedHandPose();
    [SerializeField] private EquippedHandPose leftHandPose = new EquippedHandPose();
    [SerializeField] private EquippedHandPose firstPersonRightHandPose = new EquippedHandPose();
    [SerializeField] private EquippedHandPose firstPersonLeftHandPose = new EquippedHandPose();
    [FormerlySerializedAs("equippedGripPoint")]
    [SerializeField] [HideInInspector] private Transform legacyEquippedGripPoint;
    [SerializeField] [HideInInspector] private bool legacyAuthoringMigrated;

    private Transform worldParentBeforeEquip;
    private int worldSiblingIndexBeforeEquip;
    private Vector3 worldScaleBeforeEquip = Vector3.one;
    private bool hasWorldStateBeforeEquip;
    private Vector3 equippedBaseWorldScale = Vector3.one;
    private Vector3 equippedDesiredWorldScale = Vector3.one;
    private bool usePresetEquippedBaseWorldScale;
    private Collider[] cachedColliders = Array.Empty<Collider>();
    private bool[] cachedColliderEnabledStates = Array.Empty<bool>();
    private Rigidbody[] cachedRigidbodies = Array.Empty<Rigidbody>();
    private bool[] cachedRigidbodyKinematicStates = Array.Empty<bool>();
    private bool[] cachedRigidbodyGravityStates = Array.Empty<bool>();
    private bool[] cachedRigidbodyCollisionStates = Array.Empty<bool>();
    private RigidbodyInterpolation[] cachedRigidbodyInterpolationModes = Array.Empty<RigidbodyInterpolation>();
    private CollisionDetectionMode[] cachedRigidbodyCollisionDetectionModes = Array.Empty<CollisionDetectionMode>();
    private RigidbodyConstraints[] cachedRigidbodyConstraints = Array.Empty<RigidbodyConstraints>();
    private Collider pickupCollider;
    private Collider generatedDropCollider;
    private Rigidbody rootDropRigidbody;
    private bool hasRuntimeDropRigidbody;
    private Collider[] ignoredDropOwnerColliders = Array.Empty<Collider>();
    private readonly Dictionary<Renderer, bool> originalRendererVisibilityStates = new Dictionary<Renderer, bool>();

    public ItemDefinition ItemDefinition => itemDefinition;
    public string ItemName => itemDefinition != null ? itemDefinition.ItemName : gameObject.name;
    public bool IsEquipped { get; private set; }
    public HandType EquippedHand { get; private set; } = HandType.Right;
    public EquippedItemPerspective EquippedPerspective { get; private set; } = EquippedItemPerspective.ThirdPerson;
    public Transform EquippedSocket { get; private set; }
    public Transform RightHandGripPoint => rightHandPose != null ? rightHandPose.gripPoint : null;
    public Transform LeftHandGripPoint => leftHandPose != null ? leftHandPose.gripPoint : null;
    public Transform FirstPersonRightHandGripPoint => firstPersonRightHandPose != null ? firstPersonRightHandPose.gripPoint : null;
    public Transform FirstPersonLeftHandGripPoint => firstPersonLeftHandPose != null ? firstPersonLeftHandPose.gripPoint : null;
    public Transform PickupColliderHost => pickupColliderHost;
    public bool IsPresentationClone => presentationClone;
    public string NetworkSceneId
    {
        get
        {
            if (presentationClone)
                return string.Empty;

            EnsureNetworkSceneId();
            return networkSceneId;
        }
    }

    [SerializeField] [HideInInspector] private bool presentationClone;

    private void Reset()
    {
        MigrateLegacyAuthoringIfNeeded();
        EnsureDefaultGripReferences();
        RefreshPickupColliderReference();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        if (presentationClone)
            return;

        MigrateLegacyAuthoringIfNeeded();
        EnsureDefaultGripReferences();
        RefreshPickupColliderReference();
    }

    private void Awake()
    {
        if (presentationClone || createAsPresentationClone)
        {
            presentationClone = true;
            PreparePresentationClone();
            return;
        }

        MigrateLegacyAuthoringIfNeeded();
        EnsureDefaultGripReferences();
        RefreshPickupColliderReference();
        EnsureNetworkSceneId();
        RegisterSceneItem();
        ValidateAuthoringState();
        EnableWorldPhysicsAtRest();
    }

    private void OnEnable()
    {
        if (presentationClone)
            return;

        RegisterSceneItem();
    }

    private void OnDestroy()
    {
        if (presentationClone)
            return;

        UnregisterSceneItem();
    }

    public static bool TryFindByNetworkSceneId(string sceneId, out WorldPickupItem pickupItem)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
        {
            pickupItem = null;
            return false;
        }

        if (SceneItemRegistry.TryGetValue(sceneId, out pickupItem) && pickupItem != null && !pickupItem.presentationClone)
            return true;

        // Static registries can be stale after scene transitions or Editor play-mode
        // reload settings. Rebuild from live scene objects before rejecting an RPC.
        WorldPickupItem[] sceneItems = UnityEngine.Object.FindObjectsByType<WorldPickupItem>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneItems.Length; i++)
        {
            WorldPickupItem candidate = sceneItems[i];
            if (candidate == null || candidate.presentationClone)
                continue;

            candidate.RegisterSceneItem();
            if (string.Equals(candidate.networkSceneId, sceneId, StringComparison.Ordinal))
            {
                pickupItem = candidate;
                return true;
            }

            // Accept the previous hierarchy-based id while clients migrate to
            // serialized scene ids. This also makes mixed Editor/build tests clearer.
            if (string.Equals(candidate.BuildNetworkSceneId(), sceneId, StringComparison.Ordinal))
            {
                pickupItem = candidate;
                return true;
            }
        }

        if (TryParseLegacySceneId(sceneId, out string legacySceneName, out string legacyObjectName))
        {
            WorldPickupItem uniqueLegacyMatch = null;
            for (int i = 0; i < sceneItems.Length; i++)
            {
                WorldPickupItem candidate = sceneItems[i];
                if (candidate == null
                    || candidate.presentationClone
                    || !string.Equals(candidate.gameObject.scene.name, legacySceneName, StringComparison.Ordinal)
                    || !string.Equals(candidate.gameObject.name, legacyObjectName, StringComparison.Ordinal))
                {
                    continue;
                }

                // Name fallback is safe only while it identifies one scene item.
                if (uniqueLegacyMatch != null)
                {
                    pickupItem = null;
                    return false;
                }

                uniqueLegacyMatch = candidate;
            }

            if (uniqueLegacyMatch != null)
            {
                SceneItemRegistry[sceneId] = uniqueLegacyMatch;
                pickupItem = uniqueLegacyMatch;
                return true;
            }
        }

        pickupItem = null;
        return false;
    }

    private static bool TryParseLegacySceneId(string sceneId, out string sceneName, out string objectName)
    {
        sceneName = string.Empty;
        objectName = string.Empty;

        int sceneSeparatorIndex = sceneId.IndexOf(':');
        if (sceneSeparatorIndex <= 0 || sceneSeparatorIndex >= sceneId.Length - 1)
            return false;

        string hierarchyPath = sceneId.Substring(sceneSeparatorIndex + 1);
        int leafSeparatorIndex = hierarchyPath.LastIndexOf('/');
        string leafSegment = leafSeparatorIndex >= 0
            ? hierarchyPath.Substring(leafSeparatorIndex + 1)
            : hierarchyPath;

        int siblingIndexStart = leafSegment.LastIndexOf('[');
        if (siblingIndexStart <= 0 || !leafSegment.EndsWith("]", StringComparison.Ordinal))
            return false;

        sceneName = sceneId.Substring(0, sceneSeparatorIndex);
        objectName = leafSegment.Substring(0, siblingIndexStart);
        return !string.IsNullOrWhiteSpace(sceneName) && !string.IsNullOrWhiteSpace(objectName);
    }

    public void ConfigurePrototype(ItemDefinition definition)
    {
        SetItemDefinition(definition);
    }

    public void SetItemDefinition(ItemDefinition definition)
    {
        itemDefinition = definition;
    }

    public bool ValidateAuthoringState()
    {
        bool isValid = true;

        if (itemDefinition == null)
        {
            Debug.LogWarning($"[WorldPickupItem] '{gameObject.name}' esta sem ItemDefinition.", gameObject);
            isValid = false;
        }

        if (FindPrimaryPickupCollider() == null)
        {
            Debug.LogWarning($"[WorldPickupItem] '{gameObject.name}' nao possui Collider de pickup no root ou em filhos do prefab.", gameObject);
            isValid = false;
        }

        return isValid;
    }

    public bool TryEquipIntoHand(Transform socket, HandType hand)
    {
        return TryEquipIntoHand(socket, hand, EquippedItemPerspective.ThirdPerson);
    }

    public bool TryEquipIntoHand(Transform socket, HandType hand, EquippedItemPerspective perspective)
    {
        if (socket == null)
            return false;

        MigrateLegacyAuthoringIfNeeded();
        EnsureDefaultGripReferences();
        ClearIgnoredDropOwnerCollisions();

        if (!IsEquipped)
        {
            CacheWorldStateBeforeEquip(perspective);
            CachePhysicsState();
        }

        SuppressDropPhysicsBeforeEquip();
        RemoveRuntimeDropRigidbodyForEquip();
        DisablePhysicsForEquip();
        DisablePickupOutlineForEquippedState(destroyOutlineComponents: perspective == EquippedItemPerspective.FirstPerson);

        IsEquipped = true;
        EquippedHand = hand;
        EquippedPerspective = perspective;
        EquippedSocket = socket;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        transform.SetParent(socket, false);
        ApplyCurrentEquippedPose();
        SyncSuppressedRigidbodyTransforms();
        if (DisableEquippedColliders())
            SyncPhysicsTransforms();
        return true;
    }

    public void RefreshEquippedPose()
    {
        if (!IsEquipped || EquippedSocket == null)
            return;

        ApplyCurrentEquippedPose();
        SyncSuppressedRigidbodyTransforms();
        if (DisableEquippedColliders())
            SyncPhysicsTransforms();
    }

    public void DropToWorld(Vector3 worldPosition, Quaternion worldRotation)
    {
        DropToWorld(worldPosition, worldRotation, Vector3.zero, Vector3.zero);
    }

    public void DropToWorld(Vector3 worldPosition, Quaternion worldRotation, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        Vector3 dropWorldScale = ResolveDropWorldScale();

        Transform targetParent = worldParentBeforeEquip;
        transform.SetParent(targetParent, true);
        if (targetParent != null)
            transform.SetSiblingIndex(Mathf.Clamp(worldSiblingIndexBeforeEquip, 0, targetParent.childCount - 1));

        transform.SetPositionAndRotation(worldPosition, worldRotation);
        SetWorldScale(dropWorldScale);
        EnsureDropPhysicsSupport();
        SetEquippedRenderersVisible(true);
        RestorePhysicsAfterDrop();
        EnableWorldDropPhysics(linearVelocity, angularVelocity);

        EquippedSocket = null;
        IsEquipped = false;
        EquippedPerspective = EquippedItemPerspective.ThirdPerson;
        hasWorldStateBeforeEquip = false;
    }

    public void DestroyAfterUse()
    {
        ClearIgnoredDropOwnerCollisions();
        Destroy(gameObject);
    }

    public WorldPickupItem CreateEquippedPresentationClone(Transform socket, HandType hand, EquippedItemPerspective perspective)
    {
        if (socket == null)
            return null;

        bool previousCreateAsPresentationClone = createAsPresentationClone;
        GameObject cloneObject = null;
        createAsPresentationClone = true;
        try
        {
            cloneObject = Instantiate(gameObject);
        }
        finally
        {
            createAsPresentationClone = previousCreateAsPresentationClone;
        }

        if (cloneObject == null)
            return null;

        cloneObject.name = $"{gameObject.name}_{perspective}_Visual";

        WorldPickupItem cloneItem = cloneObject.GetComponent<WorldPickupItem>();
        if (cloneItem == null)
        {
            DestroyPresentationObject(cloneObject);
            return null;
        }

        cloneItem.presentationClone = true;
        cloneItem.PreparePresentationClone();
        cloneItem.CopyAttackTrailAuthoredStatesFrom(this);
        cloneItem.equippedBaseWorldScale = IsEquipped ? equippedBaseWorldScale : GetAbsoluteLossyScale(transform);
        cloneItem.usePresetEquippedBaseWorldScale = true;

        if (!cloneItem.TryEquipIntoHand(socket, hand, perspective))
        {
            DestroyPresentationObject(cloneObject);
            return null;
        }

        cloneItem.SetEquippedRenderersVisible(true);
        return cloneItem;
    }

    private void CopyAttackTrailAuthoredStatesFrom(WorldPickupItem sourceItem)
    {
        if (sourceItem == null || sourceItem == this)
            return;

        WeaponAttackTrail[] sourceTrails = sourceItem.GetComponentsInChildren<WeaponAttackTrail>(true);
        if (sourceTrails == null || sourceTrails.Length == 0)
            return;

        WeaponAttackTrail[] cloneTrails = GetComponentsInChildren<WeaponAttackTrail>(true);
        if (cloneTrails == null || cloneTrails.Length == 0)
            return;

        int trailCount = Mathf.Min(sourceTrails.Length, cloneTrails.Length);
        for (int i = 0; i < trailCount; i++)
        {
            WeaponAttackTrail cloneTrail = cloneTrails[i];
            WeaponAttackTrail sourceTrail = sourceTrails[i];
            if (cloneTrail != null && sourceTrail != null)
                cloneTrail.CopyAuthoredRendererStatesFrom(sourceTrail);
        }
    }

    public void SetGripPoint(HandType hand, EquippedItemPerspective perspective, Transform gripPoint)
    {
        EquippedHandPose handPose = GetHandPose(hand, perspective);
        if (handPose != null)
            handPose.gripPoint = gripPoint;
    }

    public void SetEquippedRenderersVisible(bool visible)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer itemRenderer = renderers[i];
            if (itemRenderer == null)
                continue;

            if (IsRendererManagedByAttackTrail(itemRenderer))
                continue;

            if (!originalRendererVisibilityStates.ContainsKey(itemRenderer))
                originalRendererVisibilityStates[itemRenderer] = itemRenderer.enabled;

            itemRenderer.enabled = visible && originalRendererVisibilityStates[itemRenderer];
        }

        SetEquippedPresentationVisible(visible);
    }

    private static bool IsRendererManagedByAttackTrail(Renderer itemRenderer)
    {
        if (itemRenderer is not ParticleSystemRenderer)
            return false;

        WeaponAttackTrail[] attackTrails = itemRenderer.GetComponentsInParent<WeaponAttackTrail>(true);
        for (int i = 0; i < attackTrails.Length; i++)
        {
            WeaponAttackTrail attackTrail = attackTrails[i];
            if (attackTrail != null && attackTrail.ControlsRenderer(itemRenderer))
                return true;
        }

        return false;
    }

    private void SetEquippedPresentationVisible(bool visible)
    {
        TorchFlameController[] torchFlames = GetComponentsInChildren<TorchFlameController>(true);
        for (int i = 0; i < torchFlames.Length; i++)
        {
            TorchFlameController torchFlame = torchFlames[i];
            if (torchFlame != null)
                torchFlame.SetPresentationVisible(visible);
        }
    }

    public Transform GetGripPoint(HandType hand)
    {
        return GetGripPoint(hand, EquippedItemPerspective.ThirdPerson);
    }

    public Transform GetGripPoint(HandType hand, EquippedItemPerspective perspective)
    {
        EquippedHandPose handPose = GetHandPose(hand, perspective);
        return handPose != null ? handPose.gripPoint : null;
    }

    private void ApplyCurrentEquippedPose()
    {
        transform.localScale = ResolveEquippedLocalScale(EquippedSocket, equippedDesiredWorldScale);

        Transform gripPoint = ResolveGripPoint(EquippedHand, EquippedPerspective);
        if (gripPoint != null)
        {
            TryApplyGripPointLocalPose(gripPoint, EquippedSocket);
            return;
        }

        ApplyDefaultSocketLocalPose();
    }

    private void ApplyDefaultSocketLocalPose()
    {
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    private EquippedHandPose GetHandPose(HandType hand)
    {
        return GetHandPose(hand, EquippedItemPerspective.ThirdPerson);
    }

    private void EnsurePoseContainers()
    {
        if (rightHandPose == null)
            rightHandPose = new EquippedHandPose();

        if (leftHandPose == null)
            leftHandPose = new EquippedHandPose();

        if (firstPersonRightHandPose == null)
            firstPersonRightHandPose = new EquippedHandPose();

        if (firstPersonLeftHandPose == null)
            firstPersonLeftHandPose = new EquippedHandPose();
    }

    private EquippedHandPose GetHandPose(HandType hand, EquippedItemPerspective perspective)
    {
        EnsurePoseContainers();

        if (perspective == EquippedItemPerspective.FirstPerson)
            return hand == HandType.Right ? firstPersonRightHandPose : firstPersonLeftHandPose;

        return hand == HandType.Right ? rightHandPose : leftHandPose;
    }

    private Transform ResolveGripPoint(HandType hand)
    {
        return ResolveGripPoint(hand, EquippedItemPerspective.ThirdPerson);
    }

    private Transform ResolveGripPoint(HandType hand, EquippedItemPerspective perspective)
    {
        Transform primaryGrip = GetGripPoint(hand, perspective);
        if (primaryGrip != null)
            return primaryGrip;

        if (perspective == EquippedItemPerspective.FirstPerson)
        {
            Transform matchingThirdPersonGrip = GetGripPoint(hand, EquippedItemPerspective.ThirdPerson);
            if (matchingThirdPersonGrip != null)
                return matchingThirdPersonGrip;
        }

        HandType oppositeHand = hand == HandType.Right ? HandType.Left : HandType.Right;
        Transform oppositeGrip = GetGripPoint(oppositeHand, perspective);
        if (oppositeGrip != null)
            return oppositeGrip;

        if (perspective == EquippedItemPerspective.FirstPerson)
        {
            Transform oppositeThirdPersonGrip = GetGripPoint(oppositeHand, EquippedItemPerspective.ThirdPerson);
            if (oppositeThirdPersonGrip != null)
                return oppositeThirdPersonGrip;
        }

        return null;
    }

    private void CacheWorldStateBeforeEquip(EquippedItemPerspective perspective)
    {
        worldParentBeforeEquip = transform.parent;
        worldSiblingIndexBeforeEquip = transform.GetSiblingIndex();
        worldScaleBeforeEquip = usePresetEquippedBaseWorldScale
            ? SanitizeScale(equippedBaseWorldScale)
            : GetAbsoluteLossyScale(transform);
        hasWorldStateBeforeEquip = true;

        if (!usePresetEquippedBaseWorldScale)
            equippedBaseWorldScale = worldScaleBeforeEquip;

        equippedDesiredWorldScale = equippedBaseWorldScale;
        usePresetEquippedBaseWorldScale = false;
    }

    private void CachePhysicsState()
    {
        cachedColliders = GetComponentsInChildren<Collider>(true);
        cachedColliderEnabledStates = new bool[cachedColliders.Length];
        for (int i = 0; i < cachedColliders.Length; i++)
        {
            Collider collider = cachedColliders[i];
            cachedColliderEnabledStates[i] = collider != null && collider.enabled;
        }

        cachedRigidbodies = GetComponentsInChildren<Rigidbody>(true);
        cachedRigidbodyKinematicStates = new bool[cachedRigidbodies.Length];
        cachedRigidbodyGravityStates = new bool[cachedRigidbodies.Length];
        cachedRigidbodyCollisionStates = new bool[cachedRigidbodies.Length];
        cachedRigidbodyInterpolationModes = new RigidbodyInterpolation[cachedRigidbodies.Length];
        cachedRigidbodyCollisionDetectionModes = new CollisionDetectionMode[cachedRigidbodies.Length];
        cachedRigidbodyConstraints = new RigidbodyConstraints[cachedRigidbodies.Length];
        for (int i = 0; i < cachedRigidbodies.Length; i++)
        {
            Rigidbody rigidbody = cachedRigidbodies[i];
            if (rigidbody == null)
                continue;

            cachedRigidbodyKinematicStates[i] = rigidbody.isKinematic;
            cachedRigidbodyGravityStates[i] = rigidbody.useGravity;
            cachedRigidbodyCollisionStates[i] = rigidbody.detectCollisions;
            cachedRigidbodyInterpolationModes[i] = rigidbody.interpolation;
            cachedRigidbodyCollisionDetectionModes[i] = rigidbody.collisionDetectionMode;
            cachedRigidbodyConstraints[i] = rigidbody.constraints;
        }
    }

    private void SuppressDropPhysicsBeforeEquip()
    {
        if (rootDropRigidbody == null)
            rootDropRigidbody = GetComponent<Rigidbody>();

        Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
            SuppressRigidbodyForEquip(rigidbodies[i]);

        if (generatedDropCollider != null)
            generatedDropCollider.enabled = false;

        SyncPhysicsTransforms();
    }

    private void DisablePhysicsForEquip()
    {
        DisableEquippedColliders();

        for (int i = 0; i < cachedRigidbodies.Length; i++)
        {
            Rigidbody rigidbody = cachedRigidbodies[i];
            if (rigidbody == null)
                continue;

            SuppressRigidbodyForEquip(rigidbody);
        }

        SyncPhysicsTransforms();
    }

    private void RestorePhysicsAfterDrop()
    {
        for (int i = 0; i < cachedColliders.Length; i++)
        {
            Collider collider = cachedColliders[i];
            if (collider != null)
                collider.enabled = cachedColliderEnabledStates[i];
        }

        for (int i = 0; i < cachedRigidbodies.Length; i++)
        {
            Rigidbody rigidbody = cachedRigidbodies[i];
            if (rigidbody == null)
                continue;

            rigidbody.isKinematic = cachedRigidbodyKinematicStates[i];
            rigidbody.useGravity = cachedRigidbodyGravityStates[i];
            if (i < cachedRigidbodyCollisionStates.Length)
                rigidbody.detectCollisions = cachedRigidbodyCollisionStates[i];
            if (i < cachedRigidbodyInterpolationModes.Length)
                rigidbody.interpolation = cachedRigidbodyInterpolationModes[i];
            if (i < cachedRigidbodyCollisionDetectionModes.Length)
                rigidbody.collisionDetectionMode = cachedRigidbodyCollisionDetectionModes[i];
            if (i < cachedRigidbodyConstraints.Length)
                rigidbody.constraints = cachedRigidbodyConstraints[i];
        }
    }

    private void EnableWorldDropPhysics(Vector3 linearVelocity, Vector3 angularVelocity)
    {
        EnsureDropPhysicsSupport();
        EnableWorldDropColliders();
        ConfigureWorldDropRigidbodies(linearVelocity, angularVelocity, applyVelocities: true);
    }

    private void EnableWorldDropColliders()
    {
        if (generatedDropCollider != null)
        {
            generatedDropCollider.isTrigger = false;
            generatedDropCollider.enabled = true;
        }
        else if (pickupCollider != null)
        {
            pickupCollider.isTrigger = false;
        }
    }

    private void ConfigureWorldDropRigidbodies(Vector3 linearVelocity, Vector3 angularVelocity, bool applyVelocities)
    {
        // Always query the live hierarchy here so repeat pickup/drop cycles include
        // any runtime Rigidbody recreated for the next throw.
        Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>(true);

        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rigidbody = rigidbodies[i];
            if (rigidbody == null)
                continue;

            rigidbody.isKinematic = false;
            rigidbody.useGravity = true;
            rigidbody.detectCollisions = true;
            if (applyVelocities)
            {
                rigidbody.linearVelocity = linearVelocity;
                rigidbody.angularVelocity = angularVelocity;
            }

            rigidbody.WakeUp();
        }
    }

    private void EnableWorldPhysicsAtRest()
    {
        if (!Application.isPlaying || IsEquipped || presentationClone)
            return;

        EnableWorldDropPhysics(Vector3.zero, Vector3.zero);
    }

    private static void SuppressRigidbodyForEquip(Rigidbody rigidbody)
    {
        if (rigidbody == null)
            return;

        if (!rigidbody.isKinematic)
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
        }

        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        rigidbody.detectCollisions = false;
        rigidbody.interpolation = RigidbodyInterpolation.None;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rigidbody.constraints = RigidbodyConstraints.FreezeAll;
        rigidbody.Sleep();
    }

    private void SyncSuppressedRigidbodyTransforms()
    {
        Rigidbody[] rigidbodies = cachedRigidbodies != null && cachedRigidbodies.Length > 0
            ? cachedRigidbodies
            : GetComponentsInChildren<Rigidbody>(true);

        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rigidbody = rigidbodies[i];
            if (rigidbody == null)
                continue;

            rigidbody.position = rigidbody.transform.position;
            rigidbody.rotation = rigidbody.transform.rotation;
            rigidbody.Sleep();
        }
    }

    private bool DisableEquippedColliders()
    {
        if (!IsEquipped && cachedColliders.Length > 0)
            return DisableColliders(cachedColliders);

        Collider[] itemColliders = GetComponentsInChildren<Collider>(true);
        return DisableColliders(itemColliders);
    }

    private static bool DisableColliders(Collider[] colliders)
    {
        if (colliders == null || colliders.Length == 0)
            return false;

        bool changed = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            collider.enabled = false;
            changed = true;
        }

        return changed;
    }

    private static void SyncPhysicsTransforms()
    {
        if (Application.isPlaying)
            Physics.SyncTransforms();
    }

    private Vector3 ResolveEquippedLocalScale(Transform parent, Vector3 desiredWorldScale)
    {
        Vector3 parentWorldScale = GetAbsoluteLossyScale(parent);
        return new Vector3(
            SafeDivide(desiredWorldScale.x, parentWorldScale.x),
            SafeDivide(desiredWorldScale.y, parentWorldScale.y),
            SafeDivide(desiredWorldScale.z, parentWorldScale.z));
    }

    private Vector3 ResolveDropWorldScale()
    {
        if (hasWorldStateBeforeEquip)
            return SanitizeScale(worldScaleBeforeEquip);

        return GetAbsoluteLossyScale(transform);
    }

    private void SetWorldScale(Vector3 desiredWorldScale)
    {
        Vector3 safeWorldScale = SanitizeScale(desiredWorldScale);
        Transform parent = transform.parent;
        if (parent == null)
        {
            transform.localScale = safeWorldScale;
            return;
        }

        Vector3 parentWorldScale = GetAbsoluteLossyScale(parent);
        transform.localScale = new Vector3(
            SafeDivide(safeWorldScale.x, parentWorldScale.x),
            SafeDivide(safeWorldScale.y, parentWorldScale.y),
            SafeDivide(safeWorldScale.z, parentWorldScale.z));
    }

    public void IgnoreCollisionWithColliders(Collider[] ownerColliders)
    {
        ClearIgnoredDropOwnerCollisions();

        if (ownerColliders == null || ownerColliders.Length == 0)
            return;

        Collider[] itemColliders = GetComponentsInChildren<Collider>(true);
        if (itemColliders == null || itemColliders.Length == 0)
            return;

        List<Collider> appliedOwnerColliders = new List<Collider>();
        for (int ownerIndex = 0; ownerIndex < ownerColliders.Length; ownerIndex++)
        {
            Collider ownerCollider = ownerColliders[ownerIndex];
            if (ownerCollider == null)
                continue;

            bool appliedToAnyCollider = false;
            for (int itemIndex = 0; itemIndex < itemColliders.Length; itemIndex++)
            {
                Collider itemCollider = itemColliders[itemIndex];
                if (itemCollider == null || itemCollider.isTrigger)
                    continue;

                Physics.IgnoreCollision(itemCollider, ownerCollider, true);
                appliedToAnyCollider = true;
            }

            if (appliedToAnyCollider)
                appliedOwnerColliders.Add(ownerCollider);
        }

        ignoredDropOwnerColliders = appliedOwnerColliders.ToArray();
    }

    private void ClearIgnoredDropOwnerCollisions()
    {
        if (ignoredDropOwnerColliders == null || ignoredDropOwnerColliders.Length == 0)
            return;

        Collider[] itemColliders = GetComponentsInChildren<Collider>(true);
        for (int ownerIndex = 0; ownerIndex < ignoredDropOwnerColliders.Length; ownerIndex++)
        {
            Collider ownerCollider = ignoredDropOwnerColliders[ownerIndex];
            if (ownerCollider == null)
                continue;

            for (int itemIndex = 0; itemIndex < itemColliders.Length; itemIndex++)
            {
                Collider itemCollider = itemColliders[itemIndex];
                if (itemCollider == null || itemCollider.isTrigger)
                    continue;

                Physics.IgnoreCollision(itemCollider, ownerCollider, false);
            }
        }

        ignoredDropOwnerColliders = Array.Empty<Collider>();
    }

    private void PreparePresentationClone()
    {
        presentationClone = true;
        UnregisterSceneItem();
        ClearIgnoredDropOwnerCollisions();
        cachedColliders = Array.Empty<Collider>();
        cachedColliderEnabledStates = Array.Empty<bool>();
        cachedRigidbodies = Array.Empty<Rigidbody>();
        cachedRigidbodyKinematicStates = Array.Empty<bool>();
        cachedRigidbodyGravityStates = Array.Empty<bool>();
        cachedRigidbodyCollisionStates = Array.Empty<bool>();
        cachedRigidbodyInterpolationModes = Array.Empty<RigidbodyInterpolation>();
        cachedRigidbodyCollisionDetectionModes = Array.Empty<CollisionDetectionMode>();
        cachedRigidbodyConstraints = Array.Empty<RigidbodyConstraints>();
        pickupCollider = null;
        generatedDropCollider = null;
        rootDropRigidbody = null;
        hasRuntimeDropRigidbody = false;
        networkSceneId = string.Empty;
        DisablePickupOutlineForEquippedState(destroyOutlineComponents: true);

        Collider[] cloneColliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cloneColliders.Length; i++)
        {
            Collider cloneCollider = cloneColliders[i];
            if (cloneCollider != null)
                cloneCollider.enabled = false;
        }

        Rigidbody[] cloneRigidbodies = GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < cloneRigidbodies.Length; i++)
        {
            Rigidbody cloneRigidbody = cloneRigidbodies[i];
            if (cloneRigidbody == null)
                continue;

            cloneRigidbody.linearVelocity = Vector3.zero;
            cloneRigidbody.angularVelocity = Vector3.zero;
            cloneRigidbody.isKinematic = true;
            cloneRigidbody.useGravity = false;
            cloneRigidbody.detectCollisions = false;
            cloneRigidbody.interpolation = RigidbodyInterpolation.None;
            cloneRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            cloneRigidbody.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    private void DisablePickupOutlineForEquippedState(bool destroyOutlineComponents)
    {
        Outline[] outlines = GetComponentsInChildren<Outline>(true);
        for (int i = 0; i < outlines.Length; i++)
        {
            Outline outline = outlines[i];
            if (outline == null)
                continue;

            outline.enabled = false;
            if (destroyOutlineComponents)
                DestroyOutlineComponent(outline);
        }

        StripQuickOutlineMaterialsFromRenderers();
    }

    private void StripQuickOutlineMaterialsFromRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer itemRenderer = renderers[i];
            if (itemRenderer == null)
                continue;

            Material[] materials = itemRenderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                continue;

            bool removedOutlineMaterial = false;
            List<Material> filteredMaterials = new List<Material>(materials.Length);
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (IsQuickOutlineMaterial(material))
                {
                    removedOutlineMaterial = true;
                    continue;
                }

                filteredMaterials.Add(material);
            }

            if (removedOutlineMaterial)
                itemRenderer.sharedMaterials = filteredMaterials.ToArray();
        }
    }

    private static bool IsQuickOutlineMaterial(Material material)
    {
        if (material == null)
            return false;

        string materialName = material.name;
        if (materialName.StartsWith("OutlineMask", StringComparison.Ordinal)
            || materialName.StartsWith("OutlineFill", StringComparison.Ordinal))
        {
            return true;
        }

        Shader shader = material.shader;
        if (shader == null)
            return false;

        string shaderName = shader.name;
        return string.Equals(shaderName, "Custom/Outline Mask", StringComparison.Ordinal)
            || string.Equals(shaderName, "Custom/Outline Fill", StringComparison.Ordinal);
    }

    private static void DestroyOutlineComponent(Outline outline)
    {
        if (outline == null)
            return;

        if (Application.isPlaying)
            Destroy(outline);
        else
            DestroyImmediate(outline);
    }

    private bool TryApplyGripPointLocalPose(Transform gripPoint, Transform socket)
    {
        if (gripPoint == null || socket == null)
        {
            ApplyDefaultSocketLocalPose();
            return false;
        }

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        Quaternion gripLocalRotation = Quaternion.Inverse(transform.rotation) * gripPoint.rotation;
        Vector3 gripLocalPosition = transform.InverseTransformPoint(gripPoint.position);
        Quaternion itemLocalRotation = Quaternion.Inverse(gripLocalRotation);

        transform.localRotation = itemLocalRotation;
        transform.localPosition = -(itemLocalRotation * Vector3.Scale(transform.localScale, gripLocalPosition));
        return true;
    }

    private void EnsureDropPhysicsSupport()
    {
        if (!Application.isPlaying)
            return;

        if (pickupCollider == null || pickupCollider == generatedDropCollider)
            pickupCollider = FindPrimaryPickupCollider();

        EnsureDropRigidbody();
        EnsureDropCollider();
    }

    private void EnsureDropRigidbody()
    {
        if (rootDropRigidbody != null)
            return;

        rootDropRigidbody = GetComponent<Rigidbody>();
        if (rootDropRigidbody != null)
        {
            hasRuntimeDropRigidbody = false;
            return;
        }

        rootDropRigidbody = gameObject.AddComponent<Rigidbody>();
        hasRuntimeDropRigidbody = true;
        rootDropRigidbody.mass = DefaultDropPhysicsMass;
        rootDropRigidbody.useGravity = false;
        rootDropRigidbody.isKinematic = true;
        rootDropRigidbody.detectCollisions = false;
        rootDropRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rootDropRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    private void EnsureDropCollider()
    {
        if (generatedDropCollider != null)
            return;

        Collider existingSolidCollider = FindExistingSolidWorldCollider();
        if (existingSolidCollider != null)
        {
            generatedDropCollider = existingSolidCollider;
            return;
        }

        if (!TryCreateDropColliderFromPickupCollider()
            && !TryCreateDropColliderFromRendererBounds())
        {
            pickupCollider = FindPrimaryPickupCollider();
            if (pickupCollider == null)
                return;

            Transform collisionHost = EnsureDropCollisionHost(createIfMissing: true);
            if (collisionHost == null)
                return;

            generatedDropCollider = CreateSolidColliderFromSource(collisionHost.gameObject, pickupCollider);
            if (generatedDropCollider == null)
                return;
        }

        generatedDropCollider.isTrigger = false;
        generatedDropCollider.enabled = false;
    }

    private Collider FindExistingSolidWorldCollider()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider == generatedDropCollider)
                continue;

            if (!collider.isTrigger)
                return collider;
        }

        return null;
    }

    private Collider FindPrimaryPickupCollider()
    {
        Transform host = ResolvePickupColliderHost();
        if (host != null)
        {
            Collider[] hostColliders = host.GetComponents<Collider>();
            for (int i = 0; i < hostColliders.Length; i++)
            {
                Collider collider = hostColliders[i];
                if (collider != null && collider != generatedDropCollider)
                    return collider;
            }
        }

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider == generatedDropCollider)
                continue;

            if (collider.isTrigger)
                return collider;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null && collider != generatedDropCollider)
                return collider;
        }

        return null;
    }

    private bool TryCreateDropColliderFromPickupCollider()
    {
        pickupCollider = FindPrimaryPickupCollider();
        if (pickupCollider == null)
            return false;

        Transform collisionHost = EnsureDropCollisionHost(createIfMissing: true);
        if (collisionHost == null)
            return false;

        CopyDropCollisionHostTransform(pickupCollider.transform, collisionHost);
        generatedDropCollider = CreateSolidColliderFromSource(collisionHost.gameObject, pickupCollider);
        return generatedDropCollider != null;
    }

    private Collider CreateSolidColliderFromSource(GameObject hostObject, Collider sourceCollider)
    {
        if (sourceCollider is BoxCollider sourceBox)
        {
            BoxCollider worldBox = EnsureCollider<BoxCollider>(hostObject);
            CopySharedColliderSettings(sourceBox, worldBox);
            worldBox.center = sourceBox.center;
            worldBox.size = sourceBox.size;
            return worldBox;
        }

        if (sourceCollider is SphereCollider sourceSphere)
        {
            SphereCollider worldSphere = EnsureCollider<SphereCollider>(hostObject);
            CopySharedColliderSettings(sourceSphere, worldSphere);
            worldSphere.center = sourceSphere.center;
            worldSphere.radius = sourceSphere.radius;
            return worldSphere;
        }

        if (sourceCollider is CapsuleCollider sourceCapsule)
        {
            CapsuleCollider worldCapsule = EnsureCollider<CapsuleCollider>(hostObject);
            CopySharedColliderSettings(sourceCapsule, worldCapsule);
            worldCapsule.center = sourceCapsule.center;
            worldCapsule.radius = sourceCapsule.radius;
            worldCapsule.height = sourceCapsule.height;
            worldCapsule.direction = sourceCapsule.direction;
            return worldCapsule;
        }

        return null;
    }

    private void CopyDropCollisionHostTransform(Transform sourceHost, Transform collisionHost)
    {
        if (sourceHost == null || collisionHost == null)
            return;

        if (sourceHost == transform)
        {
            collisionHost.localPosition = Vector3.zero;
            collisionHost.localRotation = Quaternion.identity;
            collisionHost.localScale = Vector3.one;
            return;
        }

        collisionHost.localPosition = sourceHost.localPosition;
        collisionHost.localRotation = sourceHost.localRotation;
        collisionHost.localScale = sourceHost.localScale;
    }

    private static void CopySharedColliderSettings(Collider sourceCollider, Collider targetCollider)
    {
        if (sourceCollider == null || targetCollider == null)
            return;

        targetCollider.sharedMaterial = sourceCollider.sharedMaterial;
    }

    private T EnsureCollider<T>(GameObject colliderHostObject) where T : Collider
    {
        RemoveOtherColliders(colliderHostObject, typeof(T));

        T collider = colliderHostObject.GetComponent<T>();
        if (collider == null)
            collider = colliderHostObject.AddComponent<T>();

        return collider;
    }

    private void RemoveOtherColliders(GameObject colliderHostObject, Type colliderTypeToKeep)
    {
        Collider[] colliders = colliderHostObject.GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null && collider.GetType() != colliderTypeToKeep)
                DestroyColliderComponent(collider);
        }
    }

    private void RefreshPickupColliderReference()
    {
        pickupCollider = FindPrimaryPickupCollider();
        if (pickupCollider != null && pickupCollider.transform != transform)
            pickupColliderHost = pickupCollider.transform;
    }

    private Transform ResolvePickupColliderHost()
    {
        if (pickupColliderHost != null && pickupColliderHost.IsChildOf(transform))
            return pickupColliderHost;

        if (pickupColliderHost != null && !pickupColliderHost.IsChildOf(transform))
            pickupColliderHost = null;

        Transform existingHost = transform.Find(PickupColliderHostName);
        if (existingHost != null)
        {
            pickupColliderHost = existingHost;
            return pickupColliderHost;
        }

        return null;
    }

    private Transform EnsureDropCollisionHost(bool createIfMissing)
    {
        Transform host = transform.Find(DropCollisionHostName);
        if (host != null || !createIfMissing)
            return host;

        GameObject hostObject = new GameObject(DropCollisionHostName);
        hostObject.transform.SetParent(transform, false);
        hostObject.transform.localPosition = Vector3.zero;
        hostObject.transform.localRotation = Quaternion.identity;
        hostObject.transform.localScale = Vector3.one;
        return hostObject.transform;
    }

    private bool TryCreateDropColliderFromRendererBounds()
    {
        if (!TryCalculateLocalRendererBounds(transform, out Bounds rootLocalBounds))
            return false;

        Transform collisionHost = EnsureDropCollisionHost(createIfMissing: true);
        if (collisionHost == null)
            return false;

        collisionHost.localPosition = rootLocalBounds.center;
        collisionHost.localRotation = Quaternion.identity;
        collisionHost.localScale = Vector3.one;

        BoxCollider boxCollider = EnsureCollider<BoxCollider>(collisionHost.gameObject);
        boxCollider.center = Vector3.zero;
        boxCollider.size = EnsureMinimumSize(rootLocalBounds.size);
        boxCollider.isTrigger = false;
        boxCollider.enabled = false;
        generatedDropCollider = boxCollider;
        return true;
    }

    private void DestroyColliderComponent(Collider collider)
    {
        if (collider == null)
            return;

        if (Application.isPlaying)
            Destroy(collider);
        else
            DestroyImmediate(collider);
    }

    private static void DestroyPresentationObject(GameObject targetObject)
    {
        if (targetObject == null)
            return;

        if (Application.isPlaying)
            Destroy(targetObject);
        else
            DestroyImmediate(targetObject);
    }

    private void RemoveRuntimeDropRigidbodyForEquip()
    {
        if (rootDropRigidbody == null)
            rootDropRigidbody = GetComponent<Rigidbody>();

        if (rootDropRigidbody == null || !hasRuntimeDropRigidbody)
            return;

        SuppressRigidbodyForEquip(rootDropRigidbody);
        SyncPhysicsTransforms();

        if (Application.isPlaying)
            Destroy(rootDropRigidbody);
        else
            DestroyImmediate(rootDropRigidbody);

        rootDropRigidbody = null;
        hasRuntimeDropRigidbody = false;
    }

    private static bool TryCalculateLocalRendererBounds(Transform referenceTransform, out Bounds localBounds)
    {
        if (referenceTransform == null)
        {
            localBounds = default;
            return false;
        }

        Renderer[] renderers = referenceTransform.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            localBounds = default;
            return false;
        }

        bool hasBounds = false;
        Bounds worldBounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rendererComponent = renderers[i];
            if (rendererComponent == null)
                continue;

            if (!hasBounds)
            {
                worldBounds = rendererComponent.bounds;
                hasBounds = true;
                continue;
            }

            worldBounds.Encapsulate(rendererComponent.bounds);
        }

        if (!hasBounds)
        {
            localBounds = default;
            return false;
        }

        Vector3[] corners =
        {
            new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.min.z),
            new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.max.z),
            new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.min.z),
            new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.max.z),
            new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.min.z),
            new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.max.z),
            new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.min.z),
            new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.max.z)
        };

        Vector3 firstPoint = referenceTransform.InverseTransformPoint(corners[0]);
        localBounds = new Bounds(firstPoint, Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
            localBounds.Encapsulate(referenceTransform.InverseTransformPoint(corners[i]));

        return true;
    }

    private void MigrateLegacyAuthoringIfNeeded()
    {
        EnsurePoseContainers();

        if (legacyAuthoringMigrated)
            return;

        if (legacyEquippedGripPoint != null)
        {
            if (rightHandPose.gripPoint == null)
                rightHandPose.gripPoint = legacyEquippedGripPoint;

            if (leftHandPose.gripPoint == null)
                leftHandPose.gripPoint = legacyEquippedGripPoint;
        }

        if (rightHandPose.gripPoint == null && firstPersonRightHandPose.gripPoint != null)
            rightHandPose.gripPoint = firstPersonRightHandPose.gripPoint;

        if (leftHandPose.gripPoint == null && firstPersonLeftHandPose.gripPoint != null)
            leftHandPose.gripPoint = firstPersonLeftHandPose.gripPoint;

        legacyAuthoringMigrated = true;
    }

    private void EnsureDefaultGripReferences()
    {
        EnsurePoseContainers();

        HandRequirement handRequirement = itemDefinition != null
            ? itemDefinition.HandRequirement
            : HandRequirement.Any;
        Transform legacyGripPoint = FindNamedChildRecursive(transform, LegacyGripPointObjectName);

        EnsureDefaultGripReference(
            HandType.Right,
            EquippedItemPerspective.ThirdPerson,
            handRequirement,
            RightGripPointObjectName,
            LegacyRightGripPointObjectName,
            legacyGripPoint);
        EnsureDefaultGripReference(
            HandType.Left,
            EquippedItemPerspective.ThirdPerson,
            handRequirement,
            LeftGripPointObjectName,
            LegacyLeftGripPointObjectName,
            legacyGripPoint);
        EnsureDefaultGripReference(
            HandType.Right,
            EquippedItemPerspective.FirstPerson,
            handRequirement,
            FirstPersonRightGripPointObjectName,
            LegacyFirstPersonRightGripPointObjectName,
            fallbackGripPoint: null);
        EnsureDefaultGripReference(
            HandType.Left,
            EquippedItemPerspective.FirstPerson,
            handRequirement,
            FirstPersonLeftGripPointObjectName,
            LegacyFirstPersonLeftGripPointObjectName,
            fallbackGripPoint: null);

        if (legacyEquippedGripPoint == null)
            legacyEquippedGripPoint = legacyGripPoint;
    }

    private void EnsureDefaultGripReference(
        HandType hand,
        EquippedItemPerspective perspective,
        HandRequirement handRequirement,
        string expectedGripName,
        string legacyExpectedGripName,
        Transform fallbackGripPoint)
    {
        EquippedHandPose handPose = GetHandPose(hand, perspective);
        if (handPose == null)
            return;

        if (!ShouldUseGripPoint(handRequirement, hand))
        {
            handPose.gripPoint = null;
            return;
        }

        Transform expectedGripPoint = FindNamedChildRecursive(transform, expectedGripName);
        if (expectedGripPoint == null)
            expectedGripPoint = FindNamedChildRecursive(transform, legacyExpectedGripName);
        if (expectedGripPoint != null
            && ShouldPreferExpectedGripPoint(handPose.gripPoint, expectedGripName))
        {
            handPose.gripPoint = expectedGripPoint;
            return;
        }

        if (handPose.gripPoint == null && fallbackGripPoint != null)
            handPose.gripPoint = fallbackGripPoint;
    }

    private static bool ShouldPreferExpectedGripPoint(Transform configuredGripPoint, string expectedGripName)
    {
        if (configuredGripPoint == null)
            return true;

        return IsKnownGeneratedGripName(configuredGripPoint.name)
            && !string.Equals(configuredGripPoint.name, expectedGripName, StringComparison.Ordinal);
    }

    private static bool IsKnownGeneratedGripName(string gripName)
    {
        return string.Equals(gripName, LegacyGripPointObjectName, StringComparison.Ordinal)
            || string.Equals(gripName, RightGripPointObjectName, StringComparison.Ordinal)
            || string.Equals(gripName, LeftGripPointObjectName, StringComparison.Ordinal)
            || string.Equals(gripName, FirstPersonRightGripPointObjectName, StringComparison.Ordinal)
            || string.Equals(gripName, FirstPersonLeftGripPointObjectName, StringComparison.Ordinal)
            || string.Equals(gripName, LegacyRightGripPointObjectName, StringComparison.Ordinal)
            || string.Equals(gripName, LegacyLeftGripPointObjectName, StringComparison.Ordinal)
            || string.Equals(gripName, LegacyFirstPersonRightGripPointObjectName, StringComparison.Ordinal)
            || string.Equals(gripName, LegacyFirstPersonLeftGripPointObjectName, StringComparison.Ordinal);
    }

    private static bool ShouldUseGripPoint(HandRequirement handRequirement, HandType hand)
    {
        switch (handRequirement)
        {
            case HandRequirement.RightOnly:
                return hand == HandType.Right;
            case HandRequirement.LeftOnly:
                return hand == HandType.Left;
            case HandRequirement.TwoHanded:
            case HandRequirement.Any:
                return true;
            default:
                return true;
        }
    }

    private static Transform FindNamedChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;

            if (string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;

            Transform nestedChild = FindNamedChildRecursive(child, childName);
            if (nestedChild != null)
                return nestedChild;
        }

        return null;
    }

    private void EnsureNetworkSceneId()
    {
        if (presentationClone)
            return;

        if (!string.IsNullOrWhiteSpace(networkSceneId))
            return;

        networkSceneId = BuildNetworkSceneId();
    }

    private void RegisterSceneItem()
    {
        if (presentationClone)
            return;

        EnsureNetworkSceneId();
        if (string.IsNullOrWhiteSpace(networkSceneId))
            return;

        SceneItemRegistry[networkSceneId] = this;
    }

    private void UnregisterSceneItem()
    {
        if (presentationClone)
            return;

        if (string.IsNullOrWhiteSpace(networkSceneId))
            return;

        if (SceneItemRegistry.TryGetValue(networkSceneId, out WorldPickupItem registeredItem) && registeredItem == this)
            SceneItemRegistry.Remove(networkSceneId);
    }

    private string BuildNetworkSceneId()
    {
        string sceneName = gameObject.scene.IsValid() ? gameObject.scene.name : "UnknownScene";
        return $"{sceneName}:{BuildStableHierarchyPath(transform)}";
    }

    private static string BuildStableHierarchyPath(Transform targetTransform)
    {
        if (targetTransform == null)
            return string.Empty;

        List<string> pathSegments = new List<string>();
        Transform current = targetTransform;
        while (current != null)
        {
            pathSegments.Add($"{current.name}[{current.GetSiblingIndex()}]");
            current = current.parent;
        }

        pathSegments.Reverse();
        return string.Join("/", pathSegments);
    }

    private static Vector3 EnsureMinimumSize(Vector3 size)
    {
        const float MinSize = 0.05f;
        return new Vector3(
            Mathf.Max(MinSize, Mathf.Abs(size.x)),
            Mathf.Max(MinSize, Mathf.Abs(size.y)),
            Mathf.Max(MinSize, Mathf.Abs(size.z)));
    }

    private static Vector3 GetAbsoluteLossyScale(Transform targetTransform)
    {
        Vector3 lossyScale = targetTransform != null ? targetTransform.lossyScale : Vector3.one;
        return SanitizeScale(lossyScale);
    }

    private static Vector3 SanitizeScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
    }

    private static float SafeDivide(float value, float divisor)
    {
        return divisor > 0.0001f ? value / divisor : value;
    }
}
