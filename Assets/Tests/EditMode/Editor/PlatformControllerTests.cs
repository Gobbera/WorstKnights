using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class PlatformControllerTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private PlatformController platform;
    private DoorSignalSource signal;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Two Position Test Platform");
        platform = root.AddComponent<PlatformController>();
        signal = root.AddComponent<DoorSignalSource>();
        Set(signal, "prototypeLocalOnly", true);
        Set(platform, "prototypeLocalOnly", true);
        Set(platform, "activationMode", PlatformController.PlatformActivationMode.SignalSource);
        Set(platform, "activationSignals", new[] { signal });
        Set(platform, "motionMode", PlatformController.PlatformMotionMode.TwoPosition);
        Set(platform, "movementPoints", new List<PlatformController.PlatformMovementPoint> { Point(PlatformController.PlatformAxis.Y, 4, 2, PlatformController.PlatformDirection.Negative) });
        Call("Awake");
    }

    [TearDown]
    public void TearDown() { if (root != null) Object.DestroyImmediate(root); }

    [Test]
    public void InactiveSignalStaysAtAWithoutPublishingChangesEveryFrame()
    {
        Step(0); Assert.That(Step(1), Is.False); Assert.That(Step(2), Is.False);
        Position(Vector3.zero); Assert.That(Moving(2), Is.False);
    }

    [Test]
    public void ActiveSignalMovesToBStopsAndDoesNotAutomaticallyReturn()
    {
        signal.Activate(); Step(0); Step(1); Position(Vector3.down * 2);
        Assert.That(Moving(1), Is.True);
        Step(2); Position(Vector3.down * 4); Assert.That(Moving(2), Is.False);
        Assert.That(Step(10), Is.False); Position(Vector3.down * 4);
    }

    [Test]
    public void SignalControlsRepeatedCompleteRoundTrips()
    {
        for (int i = 0; i < 3; i++)
        {
            double time = i * 6;
            signal.Activate(); Step(time); Step(time + 2); Position(Vector3.down * 4);
            signal.Deactivate(); Step(time + 3); Step(time + 5); Position(Vector3.zero);
            Assert.That(Moving(time + 5), Is.False);
        }
    }

    [Test]
    public void ReversingDuringTravelPreservesPositionAndSpeed()
    {
        signal.Activate(); Step(0); Step(0.75); Position(Vector3.down * 1.5f);
        signal.Deactivate(); Step(0.75); Position(Vector3.down * 1.5f);
        Step(1.25); Position(Vector3.down * 0.5f);
        signal.Activate(); Step(1.25); Position(Vector3.down * 0.5f);
        Step(1.75); Position(Vector3.down * 1.5f);
    }

    [Test]
    public void MultiSegmentPathReturnsInReverseOrderAtEachSegmentsSpeed()
    {
        Set(platform, "movementPoints", new List<PlatformController.PlatformMovementPoint>
        {
            Point(PlatformController.PlatformAxis.X, 2, 1), Point(PlatformController.PlatformAxis.Y, 4, 2)
        });
        Call("Awake");
        signal.Activate(); Step(0); Step(4); Position(new Vector3(2, 4, 0));
        signal.Deactivate(); Step(4); Step(5); Position(new Vector3(2, 2, 0));
        Step(7); Position(Vector3.right); Step(8); Position(Vector3.zero);
    }

    [Test]
    public void AlwaysActiveTwoPositionStaysAtB()
    {
        Set(platform, "activationMode", PlatformController.PlatformActivationMode.AlwaysActive);
        Step(0); Step(2); Step(20); Position(Vector3.down * 4);
    }

    [Test]
    public void OneWayStillCompletesOnlyOnceAfterSignalRelease()
    {
        Set(platform, "motionMode", PlatformController.PlatformMotionMode.OneWay);
        signal.Activate(); Step(0); signal.Deactivate(); Step(1); Step(3); Position(Vector3.down * 4);
        Assert.That(Step(5), Is.False);
        signal.Activate(); Step(8); Position(Vector3.down * 4);
    }

    [Test]
    public void PingPongStillAutomaticallyReturnsAndPausesOnSignalRelease()
    {
        Set(platform, "motionMode", PlatformController.PlatformMotionMode.PingPong);
        signal.Activate(); Step(0); Step(3); Position(Vector3.down * 2);
        signal.Deactivate(); Step(3); Step(10); Position(Vector3.down * 2);
    }

    [Test]
    public void StaticMotionDoesNotMoveOnActivation()
    {
        Set(platform, "motionMode", PlatformController.PlatformMotionMode.Static);
        signal.Activate(); Step(0); Step(3); Position(Vector3.zero); Assert.That(Moving(3), Is.False);
    }

    [Test]
    public void SnapshotRestoresReverseProgressAndRejectsOlderDirection()
    {
        var snapshot = Decode("5|2|10|1|0|0|INF|INF|0");
        Assert.That(Call("EncodeStateSnapshot", snapshot).ToString().EndsWith("|0"), Is.True);
        Call("ApplyRoomSyncedState", snapshot);
        Call("ApplyMotionPoseForCurrentState", 11d); Position(Vector3.down * 2);
        Call("ApplyRoomSyncedState", Decode("4|0|10|1|0|0|INF|INF|1"));
        Call("ApplyMotionPoseForCurrentState", 11d); Position(Vector3.down * 2);
        Assert.That(Moving(11), Is.True);
    }

    [Test]
    public void LegacySnapshotDefaultsToForwardAndInvalidDirectionIsRejected()
    {
        var snapshot = Decode("1|0|10|1|0|0|INF|INF");
        Call("ApplyRoomSyncedState", snapshot); Call("ApplyMotionPoseForCurrentState", 11d); Position(Vector3.down * 2);
        var args = new object[] { "2|0|10|1|0|0|INF|INF|bad", null };
        Assert.That((bool)typeof(PlatformController).GetMethod("TryDecodeStateSnapshot", Private).Invoke(platform, args), Is.False);
    }

    [Test]
    public void BreakStopsMotionAndRespawnRestoresOriginalEndpoint()
    {
        signal.Activate(); Step(0); Step(1);
        Call("BreakPlatform", 1d); Assert.That(Moving(1), Is.False);
        Step(4); Position(Vector3.zero); Assert.That(platform.IsBroken, Is.False);
    }

    [Test]
    public void EditModePreparationDoesNotCreateAnAudioSource()
    {
        Call("PrepareMovementAudio");
        Assert.That(root.GetComponentsInChildren<AudioSource>(true), Is.Empty);
    }

    private object Decode(string state)
    {
        var args = new object[] { state, null };
        Assert.That((bool)typeof(PlatformController).GetMethod("TryDecodeStateSnapshot", Private).Invoke(platform, args), Is.True);
        return args[1];
    }
    private bool Step(double time)
    {
        bool changed = (bool)Call("UpdateAuthoritativeState", time);
        Call("ApplyMotionPoseForCurrentState", time);
        return changed;
    }
    private bool Moving(double time) => (bool)Call("IsMovingAtTime", time);
    private void Position(Vector3 expected) => Assert.That(Vector3.Distance(root.transform.localPosition, expected), Is.LessThan(0.001f));
    private object Call(string method, params object[] args) => typeof(PlatformController).GetMethod(method, Private).Invoke(platform, args);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static PlatformController.PlatformMovementPoint Point(PlatformController.PlatformAxis axis, float distance, float speed, PlatformController.PlatformDirection direction = PlatformController.PlatformDirection.Positive)
        => new PlatformController.PlatformMovementPoint { axis = axis, direction = direction, distance = distance, speed = speed };
}
