using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class InventoryItemAuthoringUtilityTests
{
    [Test]
    public void EnsureRootCollisionFromPickup_CreatesRootCollisionAndReactionStack()
    {
        GameObject root = new GameObject("TestPickup");
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(root.transform, false);

        WorldPickupItem pickupItem = root.AddComponent<WorldPickupItem>();
        try
        {
            Assert.That(
                InventoryItemAuthoringUtility.RebuildPickupColliderFromRenderers(pickupItem, ItemPickupColliderShape.Box),
                Is.True);

            GameObject legacyDropCollision = new GameObject(InventoryItemAuthoringUtility.DropCollisionName);
            legacyDropCollision.transform.SetParent(root.transform, false);
            legacyDropCollision.AddComponent<BoxCollider>();

            InventoryItemAuthoringUtility.EnsureRootCollisionFromPickup(pickupItem);
            InventoryItemAuthoringUtility.EnsureItemReactionSetup(root);

            BoxCollider rootCollider = root.GetComponent<BoxCollider>();
            Assert.That(rootCollider, Is.Not.Null);
            Assert.That(rootCollider.isTrigger, Is.False);
            Assert.That(rootCollider.enabled, Is.True);
            Assert.That(root.transform.Find(InventoryItemAuthoringUtility.DropCollisionName), Is.Null);

            Transform pickupTrigger = root.transform.Find(InventoryItemAuthoringUtility.PickupTriggerName);
            Assert.That(pickupTrigger, Is.Not.Null);
            Collider pickupCollider = pickupTrigger.GetComponent<Collider>();
            Assert.That(pickupCollider, Is.Not.Null);
            Assert.That(pickupCollider.isTrigger, Is.True);

            ReactionSignalReceiver receiver = root.GetComponent<ReactionSignalReceiver>();
            ReactionSignalEmitter emitter = root.GetComponent<ReactionSignalEmitter>();
            CollisionReactionSignalBridge collisionBridge = root.GetComponent<CollisionReactionSignalBridge>();

            Assert.That(receiver, Is.Not.Null);
            Assert.That(emitter, Is.Not.Null);
            Assert.That(collisionBridge, Is.Not.Null);
            AssertObjectReference(emitter, "signalReceiver", receiver);
            AssertObjectReference(collisionBridge, "signalEmitter", emitter);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ApplyItemDefinitionData_SavesAudioCues()
    {
        ItemDefinition itemDefinition = ScriptableObject.CreateInstance<ItemDefinition>();
        AudioCue inventoryCue = ScriptableObject.CreateInstance<AudioCue>();
        AudioCue useCue = ScriptableObject.CreateInstance<AudioCue>();
        try
        {
            InventoryItemAuthoringUtility.ApplyItemDefinitionData(
                itemDefinition,
                new ItemDefinitionAuthoringData
                {
                    ItemName = "Test Item",
                    UiSprite = null,
                    InventorySelectAudioCue = inventoryCue,
                    UseAudioCue = useCue,
                    HandRequirement = HandRequirement.Any,
                    PreferredHand = HandType.Right,
                    UseType = ItemUseType.None
                });

            Assert.That(itemDefinition.InventorySelectAudioCue, Is.SameAs(inventoryCue));
            Assert.That(itemDefinition.UseAudioCue, Is.SameAs(useCue));
            AssertObjectReference(itemDefinition, "inventorySelectAudioCue", inventoryCue);
            AssertObjectReference(itemDefinition, "useAudioCue", useCue);
        }
        finally
        {
            Object.DestroyImmediate(itemDefinition);
            Object.DestroyImmediate(inventoryCue);
            Object.DestroyImmediate(useCue);
        }
    }

    [Test]
    public void WorldPickupItem_ExposesAudioCuesAndFallsBackToItemDefinition()
    {
        GameObject root = new GameObject("TestPickup");
        ItemDefinition itemDefinition = ScriptableObject.CreateInstance<ItemDefinition>();
        AudioCue fallbackSelectCue = ScriptableObject.CreateInstance<AudioCue>();
        AudioCue fallbackUseCue = ScriptableObject.CreateInstance<AudioCue>();
        AudioCue pickupSelectCue = ScriptableObject.CreateInstance<AudioCue>();
        AudioCue pickupUseCue = ScriptableObject.CreateInstance<AudioCue>();
        try
        {
            InventoryItemAuthoringUtility.ApplyItemDefinitionData(
                itemDefinition,
                new ItemDefinitionAuthoringData
                {
                    ItemName = "Test Item",
                    InventorySelectAudioCue = fallbackSelectCue,
                    UseAudioCue = fallbackUseCue,
                    HandRequirement = HandRequirement.Any,
                    PreferredHand = HandType.Right,
                    UseType = ItemUseType.None
                });

            WorldPickupItem pickupItem = root.AddComponent<WorldPickupItem>();
            pickupItem.SetItemDefinition(itemDefinition);

            Assert.That(pickupItem.InventorySelectAudioCue, Is.SameAs(fallbackSelectCue));
            Assert.That(pickupItem.UseAudioCue, Is.SameAs(fallbackUseCue));

            pickupItem.SetAudioCues(pickupSelectCue, pickupUseCue);

            Assert.That(pickupItem.InventorySelectAudioCue, Is.SameAs(pickupSelectCue));
            Assert.That(pickupItem.UseAudioCue, Is.SameAs(pickupUseCue));
            AssertObjectReference(pickupItem, "inventorySelectAudioCue", pickupSelectCue);
            AssertObjectReference(pickupItem, "useAudioCue", pickupUseCue);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(itemDefinition);
            Object.DestroyImmediate(fallbackSelectCue);
            Object.DestroyImmediate(fallbackUseCue);
            Object.DestroyImmediate(pickupSelectCue);
            Object.DestroyImmediate(pickupUseCue);
        }
    }

    [Test]
    public void ShieldGuardHitbox_OnlyControlsColliderAfterGuardStateIsApplied()
    {
        GameObject root = new GameObject("Shield");
        BoxCollider shieldCollider = root.AddComponent<BoxCollider>();
        ShieldGuardHitbox guardHitbox = root.AddComponent<ShieldGuardHitbox>();
        try
        {
            Assert.That(shieldCollider.enabled, Is.True);
            Assert.That(shieldCollider.isTrigger, Is.False);
            Assert.That(guardHitbox.ContainsBlockCollider(shieldCollider), Is.True);

            guardHitbox.SetGuardState(guarding: true, blocking: false);

            Assert.That(shieldCollider.enabled, Is.False);
            Assert.That(shieldCollider.isTrigger, Is.False);

            guardHitbox.SetGuardState(guarding: true, blocking: true);

            Assert.That(shieldCollider.enabled, Is.True);
            Assert.That(shieldCollider.isTrigger, Is.True);

            guardHitbox.SetGuardState(guarding: false, blocking: false);

            Assert.That(shieldCollider.enabled, Is.False);
            Assert.That(shieldCollider.isTrigger, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ShieldGuardHitbox_UsesConfiguredBlockActivationDelayOverride()
    {
        GameObject root = new GameObject("Shield");
        ShieldGuardHitbox guardHitbox = root.AddComponent<ShieldGuardHitbox>();
        try
        {
            Assert.That(guardHitbox.TryGetBlockActivationDelay(out _), Is.False);

            SerializedObject serializedObject = new SerializedObject(guardHitbox);
            serializedObject.FindProperty("overrideBlockActivationDelay").boolValue = true;
            serializedObject.FindProperty("blockActivationDelay").floatValue = 0.75f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(guardHitbox.TryGetBlockActivationDelay(out float activationDelay), Is.True);
            Assert.That(activationDelay, Is.EqualTo(0.75f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ShieldGuardHitbox_CanBlockWithExplicitOwnerOutsidePlayerHierarchy()
    {
        GameObject playerRoot = new GameObject("Player");
        GameObject shieldRoot = new GameObject("Shield");
        PlayerHealth playerHealth = playerRoot.AddComponent<PlayerHealth>();
        BoxCollider shieldCollider = shieldRoot.AddComponent<BoxCollider>();
        ShieldGuardHitbox guardHitbox = shieldRoot.AddComponent<ShieldGuardHitbox>();
        try
        {
            guardHitbox.SetOwnerHealth(playerHealth);
            guardHitbox.SetGuardState(guarding: true, blocking: true);

            Assert.That(guardHitbox.OwnerHealth, Is.SameAs(playerHealth));
            Assert.That(
                guardHitbox.CanBlockEnemyHit(null, shieldRoot.transform.position, Vector3.forward, shieldCollider),
                Is.True);
        }
        finally
        {
            Object.DestroyImmediate(shieldRoot);
            Object.DestroyImmediate(playerRoot);
        }
    }

    [Test]
    public void ShieldGuardHitbox_BlocksEnemyHitOnlyWhenShieldIsBeforeTarget()
    {
        GameObject playerRoot = new GameObject("Player");
        GameObject enemyRoot = new GameObject("Enemy");
        GameObject shieldRoot = new GameObject("Shield");
        PlayerHealth playerHealth = playerRoot.AddComponent<PlayerHealth>();
        BoxCollider shieldCollider = shieldRoot.AddComponent<BoxCollider>();
        ShieldGuardHitbox guardHitbox = shieldRoot.AddComponent<ShieldGuardHitbox>();
        try
        {
            shieldCollider.size = Vector3.one;
            guardHitbox.SetOwnerHealth(playerHealth);
            guardHitbox.SetGuardState(guarding: true, blocking: true);

            shieldRoot.transform.position = new Vector3(0f, 0f, 1f);
            Physics.SyncTransforms();

            Assert.That(
                ShieldGuardHitbox.TryBlockEnemyHit(
                    playerHealth,
                    enemyRoot,
                    Vector3.zero,
                    new Vector3(0f, 0f, 2f),
                    Vector3.forward,
                    0.05f,
                    out Vector3 blockedHitPoint),
                Is.True);
            Assert.That(blockedHitPoint.z, Is.LessThan(2f));

            shieldRoot.transform.position = new Vector3(0f, 0f, 3f);
            Physics.SyncTransforms();

            Assert.That(
                ShieldGuardHitbox.TryBlockEnemyHit(
                    playerHealth,
                    enemyRoot,
                    Vector3.zero,
                    new Vector3(0f, 0f, 2f),
                    Vector3.forward,
                    0.05f,
                    out _),
                Is.False);
        }
        finally
        {
            Object.DestroyImmediate(shieldRoot);
            Object.DestroyImmediate(enemyRoot);
            Object.DestroyImmediate(playerRoot);
        }
    }

    private static void AssertObjectReference(Object target, string propertyName, Object expectedValue)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        Assert.That(property, Is.Not.Null, $"{target.name} deve ter a propriedade serializada '{propertyName}'.");
        Assert.That(property.objectReferenceValue, Is.SameAs(expectedValue));
    }
}
