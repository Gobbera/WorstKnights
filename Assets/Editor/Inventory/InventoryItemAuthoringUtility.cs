using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public enum ItemPickupColliderShape
{
    Box = 0,
    Sphere = 1,
    Capsule = 2
}

public struct ItemDefinitionAuthoringData
{
    public string ItemName;
    public Sprite UiSprite;
    public HandRequirement HandRequirement;
    public HandType PreferredHand;
    public ItemUseType UseType;
    public float HealAmount;
    public float BaseDamage;
    public bool ConsumeOnUse;
    public bool CanBeSold;
    public int SellPrice;
}

public static class InventoryItemAuthoringUtility
{
    public const string ItemDefinitionsFolder = "Assets/Resources/Items";
    public const string ItemPrefabsFolder = "Assets/Prefabs/Items";
    public const string ModelChildName = "Model";
    public const string PickupTriggerName = "PickupTrigger";
    public const string DropCollisionName = "DropCollision";
    public const string RightGripName = "GripPoints_TP_Right";
    public const string LeftGripName = "GripPoints_TP_Left";
    public const string FirstPersonRightGripName = "GripPoints_FPS_Right";
    public const string FirstPersonLeftGripName = "GripPoints_FPS_Left";

    private const float MinColliderSize = 0.05f;
    private const float DefaultItemRigidbodyMass = 0.35f;
    private const string LegacySharedGripName = "GripPoint";
    private const string LegacyRightGripName = "GripPoint_Right";
    private const string LegacyLeftGripName = "GripPoint_Left";
    private const string LegacyFirstPersonRightGripName = "GripPoint_FPS_Right";
    private const string LegacyFirstPersonLeftGripName = "GripPoint_FPS_Left";

    public static bool IsItemPrefabAsset(GameObject prefabAsset)
    {
        if (prefabAsset == null)
            return false;

        string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
        return !string.IsNullOrWhiteSpace(prefabPath)
            && prefabPath.StartsWith(ItemPrefabsFolder, StringComparison.OrdinalIgnoreCase)
            && prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
    }

    public static ItemDefinition CreateItemDefinition(ItemDefinitionAuthoringData data, out string assetPath)
    {
        EnsureFolder(ItemDefinitionsFolder);

        string safeName = SanitizeAssetName(data.ItemName, "New Item");
        assetPath = AssetDatabase.GenerateUniqueAssetPath($"{ItemDefinitionsFolder}/{safeName}.asset");

        ItemDefinition itemDefinition = ScriptableObject.CreateInstance<ItemDefinition>();
        AssetDatabase.CreateAsset(itemDefinition, assetPath);
        ApplyItemDefinitionData(itemDefinition, data);
        AssetDatabase.SaveAssets();
        return itemDefinition;
    }

    public static void ApplyItemDefinitionData(ItemDefinition itemDefinition, ItemDefinitionAuthoringData data)
    {
        if (itemDefinition == null)
            return;

        SerializedObject serializedItem = new SerializedObject(itemDefinition);
        serializedItem.FindProperty("itemName").stringValue = string.IsNullOrWhiteSpace(data.ItemName) ? itemDefinition.name : data.ItemName.Trim();
        serializedItem.FindProperty("uiSprite").objectReferenceValue = data.UiSprite;
        serializedItem.FindProperty("handRequirement").enumValueIndex = (int)data.HandRequirement;
        serializedItem.FindProperty("preferredHand").enumValueIndex = (int)ResolveStoredPreferredHand(data.HandRequirement, data.PreferredHand);
        serializedItem.FindProperty("useType").enumValueIndex = (int)data.UseType;
        serializedItem.FindProperty("healAmount").floatValue = Mathf.Max(0f, data.HealAmount);
        serializedItem.FindProperty("baseDamage").floatValue = Mathf.Max(0f, data.BaseDamage);
        serializedItem.FindProperty("consumeOnUse").boolValue = data.ConsumeOnUse;
        serializedItem.FindProperty("canBeSold").boolValue = data.CanBeSold;
        serializedItem.FindProperty("sellPrice").intValue = Mathf.Max(0, data.SellPrice);
        serializedItem.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(itemDefinition);
    }

    public static GameObject CreateItemPrefab(
        ItemDefinition itemDefinition,
        GameObject modelSource,
        ItemPickupColliderShape colliderShape,
        Color outlineColor,
        float outlineWidth,
        bool addRigidbody,
        out string prefabPath)
    {
        EnsureFolder(ItemPrefabsFolder);

        string itemName = itemDefinition != null ? itemDefinition.ItemName : "New Item";
        string safeName = SanitizeAssetName(itemName, "New Item");
        prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{ItemPrefabsFolder}/{safeName}.prefab");

        GameObject root = new GameObject(safeName);
        try
        {
            WorldPickupItem pickupItem = root.AddComponent<WorldPickupItem>();
            pickupItem.SetItemDefinition(itemDefinition);

            InstantiateModelChild(root.transform, modelSource);
            EnsureOutline(root, outlineColor, outlineWidth);
            if (addRigidbody)
                EnsureRigidbody(root);
            EnsureGripPoints(pickupItem);
            RebuildPickupColliderFromRenderers(pickupItem, colliderShape);
            EnsureDropCollisionFromPickup(pickupItem);

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return prefabAsset;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static void ApplyBasicPrefabSetup(GameObject prefabAsset, ItemDefinition itemDefinition, Color outlineColor, float outlineWidth)
    {
        string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
        if (string.IsNullOrWhiteSpace(prefabPath))
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            WorldPickupItem pickupItem = root.GetComponent<WorldPickupItem>();
            if (pickupItem == null)
                pickupItem = root.AddComponent<WorldPickupItem>();

            if (itemDefinition != null)
                pickupItem.SetItemDefinition(itemDefinition);

            EnsureOutline(root, outlineColor, outlineWidth);
            EnsureRigidbody(root);
            EnsureGripPoints(pickupItem);
            EnsureDropCollisionFromPickup(pickupItem);
            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(pickupItem);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static bool EnsureGripPointsOnPrefab(GameObject prefabAsset)
    {
        string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
        if (string.IsNullOrWhiteSpace(prefabPath))
            return false;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            WorldPickupItem pickupItem = root.GetComponent<WorldPickupItem>();
            if (pickupItem == null)
                return false;

            EnsureGripPoints(pickupItem);
            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(pickupItem);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return true;
    }

    public static bool RebuildPickupColliderFromRenderers(WorldPickupItem pickupItem, ItemPickupColliderShape colliderShape)
    {
        if (pickupItem == null || !TryCalculateLocalRendererBounds(pickupItem.transform, out Bounds rootLocalBounds))
            return false;

        Transform host = EnsureDirectChild(pickupItem.transform, PickupTriggerName);
        host.localPosition = rootLocalBounds.center;
        host.localRotation = Quaternion.identity;
        host.localScale = Vector3.one;

        Collider pickupCollider = ApplyPickupCollider(host.gameObject, colliderShape, rootLocalBounds);
        pickupCollider.isTrigger = true;
        pickupCollider.enabled = true;

        RemoveLegacyRootPickupColliders(pickupItem.transform, host);
        AssignPickupColliderHost(pickupItem, host);
        EditorUtility.SetDirty(pickupItem);
        EditorUtility.SetDirty(host.gameObject);
        return true;
    }

    public static void EnsureDropCollisionFromPickup(WorldPickupItem pickupItem)
    {
        if (pickupItem == null)
            return;

        Collider pickupCollider = ResolvePickupCollider(pickupItem);
        if (pickupCollider == null)
            return;

        Transform dropHost = EnsureDirectChild(pickupItem.transform, DropCollisionName);
        CopyLocalTransform(pickupCollider.transform, dropHost);

        Collider dropCollider = CopyCollider(dropHost.gameObject, pickupCollider);
        if (dropCollider == null)
            return;

        dropCollider.isTrigger = false;
        dropCollider.enabled = true;
        EditorUtility.SetDirty(dropHost.gameObject);
    }

    public static Rigidbody EnsureRigidbody(GameObject root)
    {
        if (root == null)
            return null;

        Rigidbody itemRigidbody = root.GetComponent<Rigidbody>();
        if (itemRigidbody == null)
            itemRigidbody = root.AddComponent<Rigidbody>();

        itemRigidbody.mass = DefaultItemRigidbodyMass;
        itemRigidbody.useGravity = true;
        itemRigidbody.isKinematic = false;
        itemRigidbody.detectCollisions = true;
        itemRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        itemRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        EditorUtility.SetDirty(itemRigidbody);
        EditorUtility.SetDirty(root);
        return itemRigidbody;
    }

    public static void EnsureGripPoints(WorldPickupItem pickupItem)
    {
        if (pickupItem == null)
            return;

        HandRequirement handRequirement = pickupItem.ItemDefinition != null
            ? pickupItem.ItemDefinition.HandRequirement
            : HandRequirement.Any;
        Transform sharedLegacyGrip = FindDirectChildByName(pickupItem.transform, LegacySharedGripName);
        Transform rightGrip = null;
        Transform leftGrip = null;
        Transform firstPersonRightGrip = null;
        Transform firstPersonLeftGrip = null;

        if (ShouldUseGripPoint(handRequirement, HandType.Right))
        {
            rightGrip = ResolveOrCreateGripPoint(
                pickupItem,
                HandType.Right,
                EquippedItemPerspective.ThirdPerson,
                sharedLegacyGrip);
            firstPersonRightGrip = ResolveOrCreateGripPoint(
                pickupItem,
                HandType.Right,
                EquippedItemPerspective.FirstPerson,
                rightGrip != null ? rightGrip : sharedLegacyGrip);
        }

        if (ShouldUseGripPoint(handRequirement, HandType.Left))
        {
            leftGrip = ResolveOrCreateGripPoint(
                pickupItem,
                HandType.Left,
                EquippedItemPerspective.ThirdPerson,
                sharedLegacyGrip);
            firstPersonLeftGrip = ResolveOrCreateGripPoint(
                pickupItem,
                HandType.Left,
                EquippedItemPerspective.FirstPerson,
                leftGrip != null ? leftGrip : sharedLegacyGrip);
        }

        pickupItem.SetGripPoint(HandType.Right, EquippedItemPerspective.ThirdPerson, rightGrip);
        pickupItem.SetGripPoint(HandType.Left, EquippedItemPerspective.ThirdPerson, leftGrip);
        pickupItem.SetGripPoint(HandType.Right, EquippedItemPerspective.FirstPerson, firstPersonRightGrip);
        pickupItem.SetGripPoint(HandType.Left, EquippedItemPerspective.FirstPerson, firstPersonLeftGrip);
        EditorUtility.SetDirty(pickupItem);
    }

    public static bool ShouldUseGripPoint(HandRequirement handRequirement, HandType hand)
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

    public static HandType ResolveStoredPreferredHand(HandRequirement handRequirement, HandType preferredHand)
    {
        switch (handRequirement)
        {
            case HandRequirement.RightOnly:
                return HandType.Right;
            case HandRequirement.LeftOnly:
                return HandType.Left;
            default:
                return preferredHand;
        }
    }

    private static Transform ResolveOrCreateGripPoint(
        WorldPickupItem pickupItem,
        HandType hand,
        EquippedItemPerspective perspective,
        Transform sourceGrip)
    {
        string gripName = GetGripName(hand, perspective);
        Transform expectedGrip = FindDirectChildByName(pickupItem.transform, gripName);
        if (expectedGrip != null)
            return expectedGrip;

        Transform configuredGrip = GetConfiguredGripPoint(pickupItem, hand, perspective);
        Transform legacyNamedGrip = FindDirectChildByName(
            pickupItem.transform,
            GetLegacyGripName(hand, perspective));
        Transform source = configuredGrip ?? legacyNamedGrip ?? sourceGrip;
        if (CanRenameGeneratedGrip(source, pickupItem.transform))
        {
            Undo.RecordObject(source.gameObject, "Rename Item Grip");
            source.gameObject.name = gripName;
            EditorUtility.SetDirty(source.gameObject);
            return source;
        }

        Transform createdGrip = EnsureDirectChild(pickupItem.transform, gripName);
        if (source != null)
            CopyLocalTransform(source, createdGrip);

        return createdGrip;
    }

    public static bool TryApplyGripPreview(WorldPickupItem pickupItem, Transform socket, HandType hand)
    {
        return TryApplyGripPreview(pickupItem, socket, hand, EquippedItemPerspective.ThirdPerson);
    }

    public static bool TryApplyGripPreview(
        WorldPickupItem pickupItem,
        Transform socket,
        HandType hand,
        EquippedItemPerspective perspective)
    {
        if (pickupItem == null || socket == null)
            return false;

        Vector3 desiredWorldScale = GetAbsoluteLossyScale(pickupItem.transform);
        pickupItem.transform.SetParent(socket, true);
        pickupItem.transform.localScale = ResolveEquippedLocalScale(socket, desiredWorldScale);

        Transform gripPoint = ResolvePreviewGripPoint(pickupItem, hand, perspective);
        if (gripPoint == null)
        {
            pickupItem.transform.localPosition = Vector3.zero;
            pickupItem.transform.localRotation = Quaternion.identity;
            return true;
        }

        return TryApplyGripPointLocalPose(pickupItem.transform, gripPoint, socket);
    }

    private static Transform ResolvePreviewGripPoint(
        WorldPickupItem pickupItem,
        HandType hand,
        EquippedItemPerspective perspective)
    {
        if (pickupItem == null)
            return null;

        Transform gripPoint = pickupItem.GetGripPoint(hand, perspective);
        if (gripPoint != null || perspective == EquippedItemPerspective.ThirdPerson)
            return gripPoint;

        gripPoint = pickupItem.GetGripPoint(hand, EquippedItemPerspective.ThirdPerson);
        if (gripPoint != null)
            return gripPoint;

        HandType oppositeHand = hand == HandType.Right ? HandType.Left : HandType.Right;
        gripPoint = pickupItem.GetGripPoint(oppositeHand, perspective);
        if (gripPoint != null)
            return gripPoint;

        return pickupItem.GetGripPoint(oppositeHand, EquippedItemPerspective.ThirdPerson);
    }

    public static bool TryComputeGripLocalPose(WorldPickupItem pickupItem, Transform socket, out Vector3 localPosition, out Quaternion localRotation)
    {
        localPosition = Vector3.zero;
        localRotation = Quaternion.identity;

        if (pickupItem == null || socket == null)
            return false;

        localPosition = pickupItem.transform.InverseTransformPoint(socket.position);
        localRotation = Quaternion.Inverse(pickupItem.transform.rotation) * socket.rotation;
        return true;
    }

    public static Transform ResolveAuthoringGrip(Transform runtimeGrip, WorldPickupItem authoringTarget)
    {
        if (runtimeGrip == null || authoringTarget == null)
            return null;

        if (runtimeGrip.root == authoringTarget.transform.root)
            return runtimeGrip;

        return PrefabUtility.GetCorrespondingObjectFromSource(runtimeGrip) as Transform;
    }

    public static Transform ResolveWritableAuthoringGrip(
        Transform runtimeGrip,
        WorldPickupItem authoringTarget,
        HandType hand)
    {
        return ResolveWritableAuthoringGrip(runtimeGrip, authoringTarget, hand, EquippedItemPerspective.ThirdPerson);
    }

    public static Transform ResolveWritableAuthoringGrip(
        Transform runtimeGrip,
        WorldPickupItem authoringTarget,
        HandType hand,
        EquippedItemPerspective perspective)
    {
        Transform authoringGrip = ResolveAuthoringGrip(runtimeGrip, authoringTarget);
        return authoringGrip != null
            ? authoringGrip
            : CreateAuthoringGrip(authoringTarget, hand, perspective);
    }

    public static string SanitizeAssetName(string value, string fallback)
    {
        string source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            source = source.Replace(invalidChar, '_');

        return string.IsNullOrWhiteSpace(source) ? fallback : source;
    }

    private static Transform CreateAuthoringGrip(
        WorldPickupItem authoringTarget,
        HandType hand,
        EquippedItemPerspective perspective)
    {
        if (authoringTarget == null)
            return null;

        string gripName = GetGripName(hand, perspective);
        Transform existingGrip = FindDirectChildByName(authoringTarget.transform, gripName);
        if (existingGrip != null)
            return existingGrip;

        GameObject gripObject = new GameObject(gripName);
        Undo.RegisterCreatedObjectUndo(gripObject, "Create Item Grip");
        gripObject.transform.SetParent(authoringTarget.transform, false);
        gripObject.transform.localPosition = Vector3.zero;
        gripObject.transform.localRotation = Quaternion.identity;
        gripObject.transform.localScale = Vector3.one;
        EditorUtility.SetDirty(gripObject);
        return gripObject.transform;
    }

    private static Transform GetConfiguredGripPoint(WorldPickupItem pickupItem, HandType hand)
    {
        return GetConfiguredGripPoint(pickupItem, hand, EquippedItemPerspective.ThirdPerson);
    }

    private static Transform GetConfiguredGripPoint(
        WorldPickupItem pickupItem,
        HandType hand,
        EquippedItemPerspective perspective)
    {
        SerializedObject serializedPickup = new SerializedObject(pickupItem);
        string posePropertyName = GetPosePropertyName(hand, perspective);
        SerializedProperty gripProperty = serializedPickup
            .FindProperty(posePropertyName)
            ?.FindPropertyRelative("gripPoint");

        return gripProperty != null ? gripProperty.objectReferenceValue as Transform : null;
    }

    private static string GetGripName(HandType hand, EquippedItemPerspective perspective)
    {
        if (perspective == EquippedItemPerspective.FirstPerson)
            return hand == HandType.Right ? FirstPersonRightGripName : FirstPersonLeftGripName;

        return hand == HandType.Right ? RightGripName : LeftGripName;
    }

    private static string GetPosePropertyName(HandType hand, EquippedItemPerspective perspective)
    {
        if (perspective == EquippedItemPerspective.FirstPerson)
            return hand == HandType.Right ? "firstPersonRightHandPose" : "firstPersonLeftHandPose";

        return hand == HandType.Right ? "rightHandPose" : "leftHandPose";
    }

    private static string GetLegacyGripName(HandType hand, EquippedItemPerspective perspective)
    {
        if (perspective == EquippedItemPerspective.FirstPerson)
            return hand == HandType.Right ? LegacyFirstPersonRightGripName : LegacyFirstPersonLeftGripName;

        return hand == HandType.Right ? LegacyRightGripName : LegacyLeftGripName;
    }

    private static bool CanRenameGeneratedGrip(Transform gripPoint, Transform root)
    {
        if (gripPoint == null || root == null || gripPoint.parent != root)
            return false;

        string gripName = gripPoint.name;
        return string.Equals(gripName, LegacyRightGripName, StringComparison.Ordinal)
            || string.Equals(gripName, LegacyLeftGripName, StringComparison.Ordinal)
            || string.Equals(gripName, LegacyFirstPersonRightGripName, StringComparison.Ordinal)
            || string.Equals(gripName, LegacyFirstPersonLeftGripName, StringComparison.Ordinal);
    }

    private static void InstantiateModelChild(Transform root, GameObject modelSource)
    {
        GameObject modelChild = new GameObject(ModelChildName);
        modelChild.transform.SetParent(root, false);

        if (modelSource == null)
            return;

        GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelSource) as GameObject;
        if (modelInstance == null)
            modelInstance = UnityEngine.Object.Instantiate(modelSource);

        modelInstance.name = modelSource.name;
        modelInstance.transform.SetParent(modelChild.transform, false);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.identity;
        modelInstance.transform.localScale = Vector3.one;
    }

    public static Outline EnsureOutline(GameObject root, Color outlineColor, float outlineWidth)
    {
        Outline outline = root.GetComponent<Outline>();
        if (outline == null)
            outline = root.AddComponent<Outline>();

        outline.OutlineMode = Outline.Mode.OutlineVisible;
        outline.OutlineColor = outlineColor;
        outline.OutlineWidth = Mathf.Clamp(outlineWidth, 0f, 10f);
        outline.enabled = false;
        EditorUtility.SetDirty(outline);
        return outline;
    }

    private static Collider ApplyPickupCollider(GameObject hostObject, ItemPickupColliderShape colliderShape, Bounds rootLocalBounds)
    {
        switch (colliderShape)
        {
            case ItemPickupColliderShape.Sphere:
            {
                SphereCollider sphereCollider = EnsureCollider<SphereCollider>(hostObject);
                sphereCollider.center = Vector3.zero;
                sphereCollider.radius = Mathf.Max(MinColliderSize, Mathf.Max(rootLocalBounds.extents.x, rootLocalBounds.extents.y, rootLocalBounds.extents.z));
                return sphereCollider;
            }
            case ItemPickupColliderShape.Capsule:
            {
                CapsuleCollider capsuleCollider = EnsureCollider<CapsuleCollider>(hostObject);
                capsuleCollider.center = Vector3.zero;
                capsuleCollider.direction = 1;
                capsuleCollider.radius = Mathf.Max(MinColliderSize, Mathf.Max(rootLocalBounds.extents.x, rootLocalBounds.extents.z));
                capsuleCollider.height = Mathf.Max(capsuleCollider.radius * 2f, Mathf.Max(MinColliderSize, rootLocalBounds.size.y));
                return capsuleCollider;
            }
            default:
            {
                BoxCollider boxCollider = EnsureCollider<BoxCollider>(hostObject);
                boxCollider.center = Vector3.zero;
                boxCollider.size = EnsureMinimumSize(rootLocalBounds.size);
                return boxCollider;
            }
        }
    }

    private static Collider CopyCollider(GameObject hostObject, Collider sourceCollider)
    {
        if (sourceCollider is BoxCollider sourceBox)
        {
            BoxCollider boxCollider = EnsureCollider<BoxCollider>(hostObject);
            boxCollider.center = sourceBox.center;
            boxCollider.size = sourceBox.size;
            boxCollider.sharedMaterial = sourceBox.sharedMaterial;
            return boxCollider;
        }

        if (sourceCollider is SphereCollider sourceSphere)
        {
            SphereCollider sphereCollider = EnsureCollider<SphereCollider>(hostObject);
            sphereCollider.center = sourceSphere.center;
            sphereCollider.radius = sourceSphere.radius;
            sphereCollider.sharedMaterial = sourceSphere.sharedMaterial;
            return sphereCollider;
        }

        if (sourceCollider is CapsuleCollider sourceCapsule)
        {
            CapsuleCollider capsuleCollider = EnsureCollider<CapsuleCollider>(hostObject);
            capsuleCollider.center = sourceCapsule.center;
            capsuleCollider.radius = sourceCapsule.radius;
            capsuleCollider.height = sourceCapsule.height;
            capsuleCollider.direction = sourceCapsule.direction;
            capsuleCollider.sharedMaterial = sourceCapsule.sharedMaterial;
            return capsuleCollider;
        }

        return null;
    }

    private static T EnsureCollider<T>(GameObject hostObject) where T : Collider
    {
        RemoveOtherColliders(hostObject, typeof(T));

        T collider = hostObject.GetComponent<T>();
        if (collider == null)
            collider = hostObject.AddComponent<T>();

        return collider;
    }

    private static void RemoveOtherColliders(GameObject hostObject, Type colliderTypeToKeep)
    {
        Collider[] colliders = hostObject.GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null && collider.GetType() != colliderTypeToKeep)
                UnityEngine.Object.DestroyImmediate(collider);
        }
    }

    private static Transform EnsureDirectChild(Transform root, string childName)
    {
        Transform existingChild = FindDirectChildByName(root, childName);
        if (existingChild != null)
            return existingChild;

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(root, false);
        childObject.transform.localPosition = Vector3.zero;
        childObject.transform.localRotation = Quaternion.identity;
        childObject.transform.localScale = Vector3.one;
        EditorUtility.SetDirty(childObject);
        return childObject.transform;
    }

    private static Transform FindDirectChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != null && string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;
        }

        return null;
    }

    private static Collider ResolvePickupCollider(WorldPickupItem pickupItem)
    {
        if (pickupItem == null)
            return null;

        Transform host = pickupItem.PickupColliderHost;
        if (host != null)
        {
            Collider hostCollider = host.GetComponent<Collider>();
            if (hostCollider != null)
                return hostCollider;
        }

        Collider[] colliders = pickupItem.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null && collider.isTrigger)
                return collider;
        }

        return colliders.Length > 0 ? colliders[0] : null;
    }

    private static void AssignPickupColliderHost(WorldPickupItem pickupItem, Transform host)
    {
        SerializedObject serializedPickup = new SerializedObject(pickupItem);
        SerializedProperty hostProperty = serializedPickup.FindProperty("pickupColliderHost");
        if (hostProperty != null)
            hostProperty.objectReferenceValue = host;

        serializedPickup.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void RemoveLegacyRootPickupColliders(Transform root, Transform activeHost)
    {
        if (root == null || activeHost == null || activeHost == root)
            return;

        Collider[] rootColliders = root.GetComponents<Collider>();
        for (int i = 0; i < rootColliders.Length; i++)
        {
            Collider collider = rootColliders[i];
            if (collider != null && collider.isTrigger)
                UnityEngine.Object.DestroyImmediate(collider);
        }
    }

    private static void CopyLocalTransform(Transform source, Transform target)
    {
        if (source == null || target == null)
            return;

        if (source.parent == target.parent)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
            return;
        }

        target.position = source.position;
        target.rotation = source.rotation;
        target.localScale = source.localScale;
    }

    private static bool TryApplyGripPointLocalPose(Transform itemTransform, Transform gripPoint, Transform socket)
    {
        if (itemTransform == null || gripPoint == null || socket == null)
            return false;

        itemTransform.localPosition = Vector3.zero;
        itemTransform.localRotation = Quaternion.identity;

        Quaternion gripLocalRotation = Quaternion.Inverse(itemTransform.rotation) * gripPoint.rotation;
        Vector3 gripLocalPosition = itemTransform.InverseTransformPoint(gripPoint.position);
        Quaternion itemLocalRotation = Quaternion.Inverse(gripLocalRotation);

        itemTransform.localRotation = itemLocalRotation;
        itemTransform.localPosition = -(itemLocalRotation * Vector3.Scale(itemTransform.localScale, gripLocalPosition));
        return true;
    }

    private static Vector3 ResolveEquippedLocalScale(Transform parent, Vector3 desiredWorldScale)
    {
        Vector3 parentWorldScale = GetAbsoluteLossyScale(parent);
        return new Vector3(
            SafeDivide(desiredWorldScale.x, parentWorldScale.x),
            SafeDivide(desiredWorldScale.y, parentWorldScale.y),
            SafeDivide(desiredWorldScale.z, parentWorldScale.z));
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

    private static Vector3 EnsureMinimumSize(Vector3 size)
    {
        return new Vector3(
            Mathf.Max(MinColliderSize, Mathf.Abs(size.x)),
            Mathf.Max(MinColliderSize, Mathf.Abs(size.y)),
            Mathf.Max(MinColliderSize, Mathf.Abs(size.z)));
    }

    private static Vector3 GetAbsoluteLossyScale(Transform targetTransform)
    {
        Vector3 lossyScale = targetTransform != null ? targetTransform.lossyScale : Vector3.one;
        return new Vector3(
            Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x)),
            Mathf.Max(0.0001f, Mathf.Abs(lossyScale.y)),
            Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z)));
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) > 0.0001f ? value / divisor : value;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string currentPath = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = $"{currentPath}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
                AssetDatabase.CreateFolder(currentPath, parts[i]);

            currentPath = nextPath;
        }
    }
}
