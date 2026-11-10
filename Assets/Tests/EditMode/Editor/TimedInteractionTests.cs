using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Photon.Pun;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class TimedInteractionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> objects = new List<Object>();
    private GameObject player;
    private PlayerPickupInteractor interactor;
    private PlayerInteractionControl control;
    private InteractionTriggerSystem system;
    private InteractionTriggerInteractable interactable;
    private InteractionTriggerTestTarget target;
    private int started, finished;

    [SetUp]
    public void SetUp()
    {
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab")); objects.Add(player);
        player.transform.position = new Vector3(3500, 3501, 3500);
        typeof(PhotonView).GetProperty("IsMine").SetValue(player.GetComponent<PhotonView>(), true);
        interactor = player.GetComponent<PlayerPickupInteractor>(); control = player.GetComponent<PlayerInteractionControl>();
        Call(interactor, "Awake"); Call(control, "Awake"); Call(player.GetComponent<PlayerMovement>(), "Awake");
        var root = new GameObject("Timed Interaction"); objects.Add(root); root.SetActive(false);
        root.transform.position = interactor.InteractionOrigin + Vector3.forward * 0.8f;
        system = root.AddComponent<InteractionTriggerSystem>(); Set(system, "localOnly", true); Set(system, "interactionCooldown", 0f);
        Set(system, "activationMode", InteractionTriggerMode.Toggle);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TimedInteractionAuthoringUtility.VariantPath);
        Assert.That(prefab, Is.Not.Null);
        var mechanism = Object.Instantiate(prefab, root.transform).GetComponent<InteractionTriggerMechanism>();
        Set(system, "mechanism", mechanism); interactable = mechanism.GetComponent<InteractionTriggerInteractable>();
        // Tests choose their own input policy; designer changes to the variant must remain untouched.
        Set(interactable, "rangeMode", InteractionRangeMode.Distance);
        Set(interactable, "interactionDistance", 1.5f);
        Set(interactable, "lockPlayerControls", true);
        Set(interactor, "pickupDistance", 3f);
        target = root.AddComponent<InteractionTriggerTestTarget>(); Set(system, "linkedSystem", target);
        root.SetActive(true); Call(system, "Initialize");
        interactable.OnInteractionStarted.AddListener(actor => { Assert.That(actor, Is.SameAs(player)); started++; });
        interactable.OnInteractionFinished.AddListener(actor => { Assert.That(actor, Is.SameAs(player)); finished++; });
        Set(interactable, "controlLockDuration", 10f);
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        if (interactable != null) PlayerInteractionAreaRegistry.Unregister(interactable);
        for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void VariantInheritsTheOriginalLeverAndPlayerContainsPreparedControl()
    {
        var variant = AssetDatabase.LoadAssetAtPath<GameObject>(TimedInteractionAuthoringUtility.VariantPath);
        Assert.That(PrefabUtility.GetPrefabAssetType(variant), Is.EqualTo(PrefabAssetType.Variant));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(variant), Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(InteractionTriggerAuthoringUtility.LeverPath)));
        Assert.That(new SerializedObject(variant.GetComponent<InteractionTriggerInteractable>()).FindProperty("lockPlayerControls").boolValue, Is.True);
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab").GetComponent<PlayerInteractionControl>(), Is.Not.Null);
    }

    [Test]
    public void OutOfRangeHasNoFocusPromptUseOrPlayerLock()
    {
        system.transform.position = interactor.InteractionOrigin + Vector3.forward * 2.5f; Physics.SyncTransforms();
        Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
        Assert.That(Prompt(), Is.False);
        Assert.That(interactable.TryInteract(interactor), Is.False);
        Assert.That(system.TryUse(player), Is.False);
        Assert.That(control.IsLocked, Is.False); Assert.That(started, Is.Zero);
        var focus = Call(interactor, "ResolveInteractionFocus");
        Assert.That(focus.GetType().GetProperty("Interactable").GetValue(focus), Is.Null);
    }

    [Test]
    public void PerObjectRangeCannotExceedThePlayersGlobalReach()
    {
        Set(interactable, "interactionDistance", 10f);
        system.transform.position = interactor.InteractionOrigin + Vector3.forward * 5f; Physics.SyncTransforms();
        Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
    }

    [Test]
    public void RejectedTargetDoesNotLockOrEmitInteractionEvents()
    {
        target.Ready = false;
        Assert.That(interactable.TryInteract(interactor), Is.False);
        Assert.That(control.IsLocked, Is.False); Assert.That(started + finished, Is.Zero);
    }

    [Test]
    public void AcceptedInteractionBlocksGameplayAndExpiresWithOneCompletion()
    {
        Assert.That(interactable.TryInteract(interactor), Is.True);
        Assert.That(control.IsLocked, Is.True); Assert.That(started, Is.EqualTo(1)); Assert.That(finished, Is.Zero);
        var input = player.GetComponent<PlayerInputHandler>(); Set(input, "movementInput", Vector2.one);
        Assert.That(input.MovementInput, Is.EqualTo(Vector2.zero));
        var movement = player.GetComponent<PlayerMovement>();
        Assert.That(movement.TryAttack(), Is.False); Assert.That(movement.TryKick(), Is.False);
        Assert.That(movement.CanChangeInventorySlots(), Is.False); Assert.That(movement.CanBeginInventoryItemAction(), Is.False);
        Assert.That(interactor.TryInteract(), Is.False); Assert.That(interactor.TryPickupItem(), Is.False);
        Assert.That(interactable.TryInteract(interactor), Is.False);
        Set(control, "lockedUntil", -10000f); Call(control, "Update");
        Assert.That(control.IsLocked, Is.False); Assert.That(finished, Is.EqualTo(1));
        Call(control, "Update"); Assert.That(finished, Is.EqualTo(1));
        Assert.That(input.MovementInput, Is.EqualTo(Vector2.one));
    }

    [Test]
    public void LockIsOptionalAndZeroDurationDoesNotTrapThePlayer()
    {
        Set(interactable, "lockPlayerControls", false);
        Assert.That(interactable.TryInteract(interactor), Is.True);
        Assert.That(control.IsLocked, Is.False); Assert.That(started, Is.EqualTo(1)); Assert.That(finished, Is.EqualTo(1));
        Set(interactable, "lockPlayerControls", true); Set(interactable, "controlLockDuration", 0f);
        Assert.That(interactable.TryInteract(interactor), Is.True);
        Assert.That(control.IsLocked, Is.False); Assert.That(started, Is.EqualTo(2)); Assert.That(finished, Is.EqualTo(2));
    }

    [Test]
    public void DisablingTheInteractableReleasesItsPlayerWithoutDisablingPlayerComponents()
    {
        Assert.That(interactable.TryInteract(interactor), Is.True);
        Call(interactable, "OnDisable");
        Assert.That(control.IsLocked, Is.False); Assert.That(finished, Is.EqualTo(1));
        Assert.That(player.GetComponent<PlayerMovement>().enabled, Is.True);
        Assert.That(player.GetComponent<PlayerInputHandler>().enabled, Is.True);
    }

    [Test]
    public void ReleasingAnotherOwnerCannotClearAnExistingLock()
    {
        var other = new GameObject("Other owner"); objects.Add(other); var owner = other.AddComponent<InteractionTriggerTestTarget>();
        Assert.That(control.TryLock(owner, 10f), Is.True);
        control.Release(interactable);
        Assert.That(control.IsLocked, Is.True);
        control.Release(owner); Assert.That(control.IsLocked, Is.False);
    }

    [Test]
    public void PhysicsStopsPlanarMomentumOnceAndPreservesVerticalVelocityAndExternalPushes()
    {
        Assert.That(interactable.TryInteract(interactor), Is.True);
        var body = player.GetComponent<Rigidbody>(); body.isKinematic = false; body.linearVelocity = new Vector3(3, 2, 4);
        Call(control, "FixedUpdate"); Assert.That(body.linearVelocity, Is.EqualTo(Vector3.up * 2));
        body.linearVelocity = new Vector3(1, 2, 0); Call(control, "FixedUpdate");
        Assert.That(body.linearVelocity, Is.EqualTo(new Vector3(1, 2, 0)));
    }

    [Test]
    public void ExpiryDoesNotRemoveAnExistingDamageMovementLock()
    {
        Assert.That(interactable.TryInteract(interactor), Is.True);
        var movement = player.GetComponent<PlayerMovement>(); Set(movement, "damageKnockbackControlLockUntil", Time.time + 100f);
        Set(control, "lockedUntil", -10000f); Call(control, "Update");
        Assert.That(control.IsLocked, Is.False); Assert.That(movement.IsMovementControlLocked, Is.True);
    }

    [Test]
    public void AcceptedSequenceCannotReplayPlayerFeedbackOrLockTwice()
    {
        Assert.That(interactable.TryInteract(interactor), Is.True);
        int sequence = (int)system.GetType().GetField("stateSequence", Private).GetValue(system);
        Call(interactable, "NotifyAccepted", interactor, sequence);
        Assert.That(started, Is.EqualTo(1));
        Assert.That(finished, Is.Zero);
    }

    [Test]
    public void InitialNetworkRestoreDoesNotReplayPlayerLockAndRejectsInvalidActorMetadata()
    {
        Call(system, "ApplyNetworkState", (object)new object[] { 4, true, true, 1d, 1d, true, 0 }, true);
        Assert.That(control.IsLocked, Is.False); Assert.That(started, Is.Zero);
        Call(system, "ApplyNetworkState", (object)new object[] { 5, false, false, 1d, 1d, true, "bad" }, false);
        Assert.That(system.IsActive, Is.True);
    }

    [Test]
    public void PlayerDeathReleasesTheInteractionLock()
    {
        Assert.That(interactable.TryInteract(interactor), Is.True);
        player.GetComponent<PlayerHealth>().ApplyReplicatedState(0, false);
        Call(control, "Update");
        Assert.That(control.IsLocked, Is.False); Assert.That(finished, Is.EqualTo(1));
    }

    [Test]
    public void DifferentPlayersKeepIndependentLocksAndSourceDisableReleasesBoth()
    {
        interactable.OnInteractionStarted.RemoveAllListeners(); interactable.OnInteractionFinished.RemoveAllListeners();
        var second = new GameObject("Second player"); objects.Add(second); second.transform.position = interactor.InteractionOrigin;
        var otherInteractor = second.AddComponent<PlayerPickupInteractor>();
        typeof(PhotonView).GetProperty("IsMine").SetValue(second.GetComponent<PhotonView>(), true);
        var otherControl = second.GetComponent<PlayerInteractionControl>(); Call(otherControl, "Awake");
        Assert.That(interactable.TryInteract(interactor), Is.True);
        Assert.That(interactable.TryInteract(otherInteractor), Is.True);
        Assert.That(control.IsLocked && otherControl.IsLocked, Is.True);
        Call(interactable, "OnDisable");
        Assert.That(control.IsLocked || otherControl.IsLocked, Is.False);
    }

    [Test]
    public void BoxModeUsesBodyPositionWhenTheCameraIsOutsideTheBox()
    {
        var box = BoxAt(player.transform.position, Vector3.one * 0.4f);
        Assert.That(box.bounds.Contains(interactor.InteractionOrigin), Is.False);
        Assert.That(interactable.IsInInteractionRange(interactor), Is.True);
        Assert.That(Prompt(), Is.True);
        Assert.That(interactable.TryInteract(interactor), Is.True);
        Assert.That(control.IsLocked, Is.True);
    }

    [Test]
    public void BoxModeRejectsBodyOutsideEvenWhenNearTheMechanism()
    {
        BoxAt(player.transform.position + Vector3.right * 4, Vector3.one);
        Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
        Assert.That(Prompt(), Is.False); Assert.That(interactable.TryInteract(interactor), Is.False);
        Assert.That(system.TryUse(player), Is.False); Assert.That(started, Is.Zero);
    }

    [Test]
    public void BoxModeSupportsLocalCenterRotationAndNonUniformScale()
    {
        var box = BoxAt(player.transform.position, new Vector3(2, 2, 4));
        box.transform.rotation = Quaternion.Euler(20, 45, 10); box.transform.localScale = new Vector3(2, 0.5f, 3);
        box.center = new Vector3(1, 2, -1);
        player.transform.position = box.transform.TransformPoint(box.center + new Vector3(0.9f, 0.9f, 1.9f));
        Assert.That(interactable.IsInInteractionRange(interactor), Is.True);
        player.transform.position = box.transform.TransformPoint(box.center + new Vector3(1.1f, 0, 0));
        Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
    }

    [Test]
    public void MissingDisabledInactiveAndZeroSizedBoxesRejectInteraction()
    {
        var box = BoxAt(player.transform.position, Vector3.one);
        box.enabled = false; Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
        box.enabled = true; box.gameObject.SetActive(false); Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
        box.gameObject.SetActive(true); box.size = Vector3.zero; Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
        box.size = Vector3.one; box.transform.localScale = Vector3.zero; Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
        Set(interactable, "interactionBox", null); Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
    }

    [Test]
    public void InsideBoxAllowsAimFocusBeyondNumericReachAndLeavingRemovesIt()
    {
        BoxAt(player.transform.position, Vector3.one * 2);
        system.transform.position = interactor.InteractionOrigin + Vector3.forward * 5; Physics.SyncTransforms();
        Assert.That(interactable.IsInInteractionRange(interactor), Is.True);
        Assert.That(Focused(), Is.SameAs(interactable));
        Assert.That(Prompt(), Is.True);
        player.transform.position += Vector3.right * 3; Physics.SyncTransforms();
        Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
        Assert.That(Focused(), Is.Null); Assert.That(Prompt(), Is.False);
    }

    [Test]
    public void InteractionBoxCannotBeUsedAsAnAimProxy()
    {
        var box = BoxAt(player.transform.position, new Vector3(2, 0.4f, 2));
        box.gameObject.layer = 0;
        system.transform.position = interactor.InteractionOrigin + Vector3.right * 4;
        box.transform.SetParent(interactable.transform, true); box.transform.position = player.transform.position;
        player.GetComponentsInChildren<Camera>(true).First(c => c.name == "FP_Camera").transform.rotation = Quaternion.Euler(45, 0, 0);
        Physics.SyncTransforms();
        Assert.That(interactable.IsInInteractionRange(interactor), Is.True);
        Assert.That(Focused(), Is.Null, "Looking at the box must not substitute aiming at the mechanism.");
    }

    [Test]
    public void BoxAimIsBlockedByWalls()
    {
        BoxAt(player.transform.position, Vector3.one * 2);
        system.transform.position = interactor.InteractionOrigin + Vector3.forward * 5;
        var wall = new GameObject("Area interaction wall"); objects.Add(wall);
        wall.transform.position = interactor.InteractionOrigin + Vector3.forward * 2;
        wall.AddComponent<BoxCollider>().size = new Vector3(3, 3, 0.2f); Physics.SyncTransforms();
        Assert.That(Focused(), Is.Null);
    }

    [Test]
    public void ExpandedBoxSearchDoesNotExtendOrdinaryInteractableReach()
    {
        BoxAt(player.transform.position, Vector3.one * 2);
        system.transform.position = interactor.InteractionOrigin + Vector3.right * 6;
        var other = new GameObject("Ordinary distant interactable"); objects.Add(other);
        other.transform.position = interactor.InteractionOrigin + Vector3.forward * 4.5f;
        other.AddComponent<BoxCollider>().size = Vector3.one * 0.5f; other.AddComponent<PickupFocusTestInteractable>();
        Physics.SyncTransforms(); Assert.That(Focused(), Is.Null);
    }

    [Test]
    public void ExpandedBoxSearchDoesNotExtendPickupReach()
    {
        BoxAt(player.transform.position, Vector3.one * 2);
        system.transform.position = interactor.InteractionOrigin + Vector3.right * 6;
        var other = new GameObject("Distant pickup"); objects.Add(other);
        other.transform.position = interactor.InteractionOrigin + Vector3.forward * 4.5f;
        other.AddComponent<BoxCollider>().size = Vector3.one * 0.5f; other.AddComponent<WorldPickupItem>();
        Physics.SyncTransforms(); var focus = Call(interactor, "ResolveInteractionFocus");
        Assert.That(focus.GetType().GetProperty("Pickup").GetValue(focus), Is.Null);
    }

    [Test]
    public void LinkedAreaColliderDoesNotInflateDistanceModeRange()
    {
        var box = BoxAt(player.transform.position, Vector3.one * 10);
        box.transform.SetParent(interactable.transform, true);
        Set(interactable, "rangeMode", InteractionRangeMode.Distance); Call(interactable, "OnEnable");
        system.transform.position = interactor.InteractionOrigin + Vector3.forward * 5; Physics.SyncTransforms();
        Assert.That(box.bounds.Contains(interactor.InteractionOrigin), Is.True);
        Assert.That(interactable.IsInInteractionRange(interactor), Is.False);
        Assert.That(PlayerInteractionAreaRegistry.GetSearchDistance(interactor, 3), Is.EqualTo(3));
    }

    [Test]
    public void EditorBoxCreationIsIdempotentAndKeepsPhysicalCollidersSolid()
    {
        var physical = interactable.GetComponentsInChildren<BoxCollider>().First(c => !c.isTrigger);
        var box = InteractionTriggerInteractableEditor.CreateInteractionBox(interactable);
        Assert.That(InteractionTriggerInteractableEditor.CreateInteractionBox(interactable), Is.SameAs(box));
        Assert.That(box.isTrigger, Is.True); Assert.That(physical.isTrigger, Is.False);
        Assert.That(box.transform.parent, Is.SameAs(interactable.transform));
    }

    private BoxCollider BoxAt(Vector3 position, Vector3 size)
    {
        var root = new GameObject("Interaction Box"); objects.Add(root); root.transform.position = position;
        root.layer = LayerMask.NameToLayer("Ignore Raycast");
        var box = root.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = size;
        Set(interactable, "rangeMode", InteractionRangeMode.BoxCollider); Set(interactable, "interactionBox", box); Call(interactable, "OnEnable");
        Physics.SyncTransforms(); return box;
    }
    private object Focused()
    {
        Physics.SyncTransforms(); var focus = Call(interactor, "ResolveInteractionFocus");
        return focus.GetType().GetProperty("Interactable").GetValue(focus);
    }

    private bool Prompt() => interactable.TryGetInteractionPrompt(new PlayerInteractionPromptContext(interactor, KeyCode.E, "E"), out _);
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private).SetValue(value, data);
    private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Private).Invoke(value, args);
}
