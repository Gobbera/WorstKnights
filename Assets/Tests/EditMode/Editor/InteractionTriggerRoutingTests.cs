using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

public sealed class InteractionTriggerRoutingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> objects = new List<Object>();
    private InteractionTriggerSystem system;
    private InteractionTriggerRouter router;
    private InteractionTriggerTestTarget first, second;

    [SetUp]
    public void SetUp()
    {
        var root = Create("Routing Trigger"); root.SetActive(false);
        system = root.AddComponent<InteractionTriggerSystem>();
        Set(system, "localOnly", true); Set(system, "interactionCooldown", 0f);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InteractionTriggerAuthoringUtility.LeverPath);
        Set(system, "mechanism", Object.Instantiate(prefab, root.transform).GetComponent<InteractionTriggerMechanism>());
        first = Create("First target").AddComponent<InteractionTriggerTestTarget>();
        second = Create("Second target").AddComponent<InteractionTriggerTestTarget>();
        router = root.AddComponent<InteractionTriggerRouter>();
        Set(router, "localOnly", true);
        Set(router, "routes", new[] { new InteractionTriggerRouter.Route { name = "First", target = first }, new InteractionTriggerRouter.Route { name = "Second", target = second } });
        Set(system, "linkedSystem", router);
        root.SetActive(true);
        Call(system, "Initialize");
        Call(router, "Initialize");
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void RouteChangeDuringPressDoesNotReplayAndReleasesOriginalTarget()
    {
        Assert.That(system.TryUse(null), Is.True);
        router.SelectRoute(1);
        Assert.That(second.States, Is.Empty);
        Release();
        Assert.That(first.States, Is.EqualTo(new[] { true, false }));
        Assert.That(second.States, Is.Empty);
        Assert.That(system.TryUse(null), Is.True);
        Assert.That(second.Actions, Is.EqualTo(1));
    }

    [Test]
    public void SynchronousOutputCanSwitchRouteWithoutRedirectingItsCurrentPress()
    {
        system.OnActivated.AddListener(() => router.SelectRoute(1));
        system.TryUse(null); Release();
        Assert.That(first.States, Is.EqualTo(new[] { true, false }));
        Assert.That(second.States, Is.Empty);
        system.TryUse(null);
        Assert.That(second.Actions, Is.EqualTo(1));
    }

    [Test]
    public void BusyOriginalTargetHoldsReleaseAfterRouteChanges()
    {
        system.TryUse(null); first.Busy = true; router.SelectRoute(1);
        Release(); Assert.That(system.IsActive, Is.True);
        Assert.That(system.TryUse(null), Is.False);
        first.Busy = false; Release();
        Assert.That(system.IsActive, Is.False);
        Assert.That(second.States, Is.Empty);
    }

    [Test]
    public void BusyNewTargetDoesNotPreventOriginalReleaseButBlocksTheNextPress()
    {
        system.TryUse(null); second.Busy = true; router.SelectRoute(1); Release();
        Assert.That(system.IsActive, Is.False);
        Assert.That(system.TryUse(null), Is.False);
        second.Busy = false; Assert.That(system.TryUse(null), Is.True);
    }

    [Test]
    public void ToggleReleasesOldTargetBeforeNewRouteActivates()
    {
        Set(system, "activationMode", InteractionTriggerMode.Toggle);
        system.TryUse(null); router.SelectRoute(1); system.TryUse(null);
        Assert.That(first.States, Is.EqualTo(new[] { true, false }));
        Assert.That(second.States, Is.Empty);
        system.TryUse(null); Assert.That(second.States, Is.EqualTo(new[] { true }));
    }

    [Test]
    public void AlternateABUsesTheSelectedRouteOnBothActionPositions()
    {
        Set(system, "activationMode", InteractionTriggerMode.AlternateAB);
        system.TryUse(null); router.SelectRoute(1); system.TryUse(null);
        Assert.That(first.States, Is.EqualTo(new[] { true }));
        Assert.That(second.States, Is.EqualTo(new[] { false }));
        Assert.That(second.Actions, Is.EqualTo(1));
    }

    [Test]
    public void PressureRouteChangeWaitsForOccupantReleaseWithoutActivatingNewTarget()
    {
        Set(system, "triggerType", InteractionTriggerType.PressureButton);
        var pressure = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(InteractionTriggerAuthoringUtility.PressurePath), system.transform);
        Set(system, "mechanism", pressure.GetComponent<InteractionTriggerMechanism>());
        Call(system, "ChangeState", true, null, true);
        router.SelectRoute(1); Release();
        Assert.That(first.States, Is.EqualTo(new[] { true, false }));
        Assert.That(second.States, Is.Empty);
        Call(system, "ChangeState", true, null, true);
        Assert.That(second.Actions, Is.EqualTo(1));
    }

    [Test]
    public void InvalidRouteAndNestedRouterDoNotChangeSelectionOrEmitCommands()
    {
        router.SelectRoute(-1); router.SelectRoute(99);
        Set(router, "routes", new[] { new InteractionTriggerRouter.Route { target = first }, new InteractionTriggerRouter.Route { target = router } });
        router.SelectRoute(1);
        Assert.That(router.SelectedRoute, Is.Zero);
        Assert.That(first.States, Is.Empty);
    }

    [Test]
    public void MissingDisabledAndUnavailableDestinationsBlockTheTrigger()
    {
        first.Ready = false; Assert.That(system.TryUse(null), Is.False);
        first.Ready = true; first.enabled = false; Assert.That(system.TryUse(null), Is.False);
        first.enabled = true; Object.DestroyImmediate(first); Assert.That(system.TryUse(null), Is.False);
        Assert.That(system.IsActive, Is.False);
    }

    [Test]
    public void RestoredRoutingPreservesHeldDestinationWithoutReplayingGameplay()
    {
        Call(router, "ApplyNetworkState", (object)new object[] { 5, 1, 0 });
        Assert.That(router.SelectedRoute, Is.EqualTo(1));
        Assert.That(router.HeldRoute, Is.Zero);
        Call(router, "ApplyNetworkState", (object)new object[] { 4, 0, -1 });
        Call(router, "ApplyNetworkState", (object)new object[] { 6, 99, -1 });
        Call(router, "ApplyNetworkState", (object)new object[] { 6, 0, -2 });
        Assert.That(router.SelectedRoute, Is.EqualTo(1));
        Assert.That(first.States.Count + second.States.Count, Is.Zero);
        Call(system, "ChangeState", false, null, false);
        Assert.That(first.States, Is.EqualTo(new[] { false }));
        Assert.That(second.States, Is.Empty);
        Assert.That(system.TryUse(null), Is.True);
        Assert.That(second.Actions, Is.EqualTo(1));
    }

    [Test]
    public void SignalFollowStateTracksPressAndRelease()
    {
        var signal = Signal(SignalTriggerAction.FollowState);
        system.TryUse(null); Assert.That(signal.IsActive, Is.True);
        Release(); Assert.That(signal.IsActive, Is.False);
    }

    [Test]
    public void SignalToggleRunsOncePerMomentaryPress()
    {
        var signal = Signal(SignalTriggerAction.Toggle);
        system.TryUse(null); Release(); Assert.That(signal.IsActive, Is.True);
        system.TryUse(null); Release(); Assert.That(signal.IsActive, Is.False);
    }

    [Test]
    public void SignalActivateLatchesAfterReleaseAndDeactivateHandlesActions()
    {
        var signal = Signal(SignalTriggerAction.Activate);
        system.TryUse(null); Release(); Assert.That(signal.IsActive, Is.True);
        Set(signal.GetComponent<SignalInteractionTriggerTarget>(), "action", SignalTriggerAction.Deactivate);
        system.TryUse(null); Release(); Assert.That(signal.IsActive, Is.False);
    }

    [Test]
    public void SignalAdapterRespectsDisabledAndMissingSignal()
    {
        var signal = Signal(SignalTriggerAction.Activate);
        signal.enabled = false; Assert.That(system.TryUse(null), Is.False);
        Object.DestroyImmediate(signal); Assert.That(system.TryUse(null), Is.False);
    }

    [Test]
    public void SceneSetupIsIdempotentAndPreservesPlatformPathAndExistingOutputs()
    {
        var puzzle = Create("Puzzle").AddComponent<StatuePuzzleController>();
        var platform = Create("Platform").AddComponent<PlatformController>();
        var path = new List<PlatformController.PlatformMovementPoint> { new PlatformController.PlatformMovementPoint { axis = PlatformController.PlatformAxis.Y, direction = PlatformController.PlatformDirection.Negative, distance = 15.5f, speed = 2.5f } };
        Set(platform, "movementPoints", path); Set(platform, "motionMode", PlatformController.PlatformMotionMode.OneWay);
        platform.enabled = false;
        int extra = 0; puzzle.OnPuzzleSolved.AddListener(() => extra++);
        var setEnabled = (UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityAction<bool>), platform, typeof(Behaviour).GetProperty("enabled").GetSetMethod());
        UnityEventTools.AddBoolPersistentListener(puzzle.OnPuzzleSolved, setEnabled, false);
        Set(system, "linkedSystem", puzzle);
        var firstRouter = InteractionTriggerRoutingSetup.Configure(system, puzzle, platform);
        Assert.That(InteractionTriggerRoutingSetup.Configure(system, puzzle, platform), Is.SameAs(firstRouter));
        Assert.That(platform.enabled, Is.True, "The signal must gate movement while the controller stays enabled.");
        Assert.That(puzzle.OnPuzzleSolved.GetPersistentEventCount(), Is.EqualTo(1), "Replace the conflicting legacy platform enable/disable output, preserving other outputs.");
        Assert.That(new SerializedObject(firstRouter).FindProperty("routes").arraySize, Is.EqualTo(4), "Existing routes must be preserved.");
        Assert.That(new SerializedObject(platform).FindProperty("activationSignals").arraySize, Is.EqualTo(1));
        Assert.That(new SerializedObject(platform).FindProperty("movementPoints").GetArrayElementAtIndex(0).FindPropertyRelative("distance").floatValue, Is.EqualTo(15.5f));
        Assert.That(InteractionTriggerRoutingSetup.ReferencesPuzzle(system, puzzle), Is.True);
        puzzle.OnPuzzleSolved.Invoke(); Assert.That(extra, Is.EqualTo(1));
    }

    [Test]
    public void ImmediateHandoffActivatesBAndSubsequentUsesAlternateWithoutAnotherPuzzleCheck()
    {
        UseABRoute();
        int uses = 0; system.OnUsed.AddListener(() => uses++);
        system.TryUse(null); router.SelectRouteAndActivate(1);
        Assert.That(first.States, Is.EqualTo(new[] { true, false }));
        Assert.That(second.States, Is.EqualTo(new[] { true }));
        Assert.That(system.Mode, Is.EqualTo(InteractionTriggerMode.AlternateAB));
        Release(); Assert.That(system.IsActive, Is.True, "AB must not auto-release like Momentary.");
        Assert.That(uses, Is.EqualTo(1), "Handoff must not duplicate the original use event.");
        system.TryUse(null); system.TryUse(null);
        Assert.That(second.States, Is.EqualTo(new[] { true, false, true }));
        Assert.That(first.Actions, Is.EqualTo(1));
    }

    [Test]
    public void SynchronousSolveHandoffRunsOnceAndKeepsContextAsASnapshot()
    {
        UseABRoute();
        var before = new InteractionTriggerContext(system, null);
        system.OnActivated.AddListener(() => router.SelectRouteAndActivate(1));
        system.TryUse(null); router.SelectRouteAndActivate(1);
        Assert.That(second.Actions, Is.EqualTo(1));
        Assert.That(before.IsActive, Is.False);
        Assert.That(before.Mode, Is.EqualTo(InteractionTriggerMode.Momentary));
    }

    [Test]
    public void ExternalCompletionCanActivateANewRouteWithoutAPreviousPress()
    {
        UseABRoute(); router.SelectRouteAndActivate(1);
        Assert.That(first.States, Is.Empty);
        Assert.That(second.States, Is.EqualTo(new[] { true }));
        Assert.That(system.IsActive, Is.True);
    }

    [Test]
    public void RestoredSelectedRouteRestoresABPolicyWithoutReplayingActivation()
    {
        UseABRoute();
        Call(router, "ApplyNetworkState", (object)new object[] { 5, 1, -1 });
        Call(system, "ApplyNetworkState", (object)new object[] { 5, true, true, -10d, -10d, false }, true);
        Assert.That(system.Mode, Is.EqualTo(InteractionTriggerMode.AlternateAB));
        Assert.That(second.States, Is.Empty);
        Assert.That(system.TryUse(null), Is.True);
        Assert.That(second.States, Is.EqualTo(new[] { false }));
    }

    [Test]
    public void DeferredSelectionAppliesOverrideOnlyAfterOriginalRelease()
    {
        UseABRoute(); system.TryUse(null); router.SelectRoute(1);
        Assert.That(system.Mode, Is.EqualTo(InteractionTriggerMode.Momentary));
        Release(); Assert.That(system.IsActive, Is.False);
        Assert.That(system.Mode, Is.EqualTo(InteractionTriggerMode.AlternateAB));
        Assert.That(second.States, Is.Empty);
    }

    [Test]
    public void CallControlCannotBypassTheMomentaryPuzzleStage()
    {
        var call = CallControl();
        Assert.That(call.TryUse(null), Is.False);
        Assert.That(first.States, Is.Empty);
        Assert.That(system.IsActive || call.IsActive, Is.False);
    }

    [Test]
    public void CallSelectsBOnTheMainTriggerAndReleaseKeepsTheElevatorCalled()
    {
        UseABRoute(); router.SelectRouteAndActivate(1); system.TryUse(null);
        var call = CallControl();
        Assert.That(call.TryUse(null), Is.True);
        Assert.That(system.IsActive, Is.True);
        Assert.That(second.States, Is.EqualTo(new[] { true, false, true }));
        Set(call, "changedAt", -10000d); Call(call, "RefreshTrigger", 1f);
        Assert.That(call.IsActive, Is.False);
        Assert.That(system.IsActive, Is.True);
        Assert.That(second.States, Is.EqualTo(new[] { true, false, true }));
    }

    [Test]
    public void RepeatedCallAtBIsIgnoredAndTheMainLeversNextUseSelectsA()
    {
        UseABRoute(); router.SelectRouteAndActivate(1); system.TryUse(null);
        var call = CallControl(); call.TryUse(null);
        Set(call, "changedAt", -10000d); Call(call, "RefreshTrigger", 1f);
        Assert.That(call.TryUse(null), Is.False);
        Assert.That(system.IsActive, Is.True);
        Assert.That(system.TryUse(null), Is.True);
        Assert.That(system.IsActive, Is.False);
        Assert.That(second.States, Is.EqualTo(new[] { true, false, true, false }));
    }

    [Test]
    public void StateTargetSupportsSetAAndToggleForOtherControls()
    {
        Set(system, "activationMode", InteractionTriggerMode.Toggle);
        system.TryUse(null);
        var call = CallControl(); var adapter = call.GetComponent<InteractionTriggerStateTarget>();
        Set(adapter, "command", InteractionTriggerStateCommand.SetA);
        Assert.That(call.TryUse(null), Is.True); Assert.That(system.IsActive, Is.False);
        Set(call, "changedAt", -10000d); Call(call, "RefreshTrigger", 1f);
        Set(adapter, "command", InteractionTriggerStateCommand.Toggle);
        Assert.That(call.TryUse(null), Is.True); Assert.That(system.IsActive, Is.True);
    }

    [Test]
    public void CallSetupRemovesCopiedPuzzleRouterWithoutMovingOrReplacingTheMechanism()
    {
        var call = CallControl(); var mechanism = call.Mechanism; var position = call.transform.position;
        var copied = call.gameObject.AddComponent<InteractionTriggerRouter>(); Set(call, "linkedSystem", copied);
        var adapter = ElevatorCallControlSetup.Configure(call, system);
        Assert.That(ElevatorCallControlSetup.Configure(call, system), Is.SameAs(adapter));
        Assert.That(copied == null, Is.True);
        Assert.That(call.Mechanism, Is.SameAs(mechanism));
        Assert.That(call.transform.position, Is.EqualTo(position));
        Assert.That(call.LinkedSystem, Is.SameAs(adapter));
        Assert.That(new SerializedObject(adapter).FindProperty("controlledTrigger").objectReferenceValue, Is.SameAs(system));
        Assert.That(call.Mode, Is.EqualTo(InteractionTriggerMode.Momentary));
    }

    private InteractionTriggerSystem CallControl()
    {
        var root = Create("Elevator Call"); root.SetActive(false);
        var call = root.AddComponent<InteractionTriggerSystem>();
        Set(call, "localOnly", true); Set(call, "interactionCooldown", 0f);
        Set(call, "mechanism", Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(InteractionTriggerAuthoringUtility.LeverPath), root.transform).GetComponent<InteractionTriggerMechanism>());
        var adapter = root.AddComponent<InteractionTriggerStateTarget>(); Set(adapter, "controlledTrigger", system);
        Set(call, "linkedSystem", adapter);
        root.SetActive(true); Call(call, "Initialize");
        return call;
    }

    private void UseABRoute() => Set(router, "routes", new[]
    {
        new InteractionTriggerRouter.Route { target = first },
        new InteractionTriggerRouter.Route { target = second, overrideActivationMode = true, activationMode = InteractionTriggerMode.AlternateAB }
    });

    private DoorSignalSource Signal(SignalTriggerAction action)
    {
        var signal = Create("Activation Signal").AddComponent<DoorSignalSource>();
        Set(signal, "prototypeLocalOnly", true);
        var adapter = signal.gameObject.AddComponent<SignalInteractionTriggerTarget>();
        Set(adapter, "signalSource", signal); Set(adapter, "action", action);
        Set(router, "routes", new[] { new InteractionTriggerRouter.Route { target = adapter } });
        return signal;
    }

    private void Release()
    {
        Set(system, "changedAt", -10000d); Set(system, "readyAt", -10000d);
        Call(system, "RefreshTrigger", 1f);
    }
    private GameObject Create(string name)
    {
        var root = new GameObject(name); root.transform.position = new Vector3(2600, 2600, 2600); objects.Add(root); return root;
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Call(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, Private).Invoke(target, arguments);
}
