using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Photon.Pun;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class RemotePlayerLookPoseTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> objects = new List<Object>();
    private PlayerMovement movement;
    private Rigidbody body;
    private Transform spine, chest, head;
    private Animator animator;
    private const float FrameTime = 1f / 60f;

    [SetUp]
    public void SetUp()
    {
        var root = Track(new GameObject("Remote look regression player"));
        root.SetActive(false);
        root.AddComponent<CapsuleCollider>();
        movement = root.AddComponent<PlayerMovement>();
        body = root.GetComponent<Rigidbody>();
        body.isKinematic = true;
        Set("rb", body);
        Set("photonView", root.GetComponent<PhotonView>());
        spine = Bone("Spine", root.transform);
        chest = Bone("Chest", spine);
        head = Bone("Head", chest);
        Set("spineBone", spine); Set("chestBone", chest); Set("headBone", head);
        Set("headPitchLimit", 0f); Set("chestPitchLimit", 30f); Set("spinePitchLimit", 25f);
        Set("horizontalHeadLookLimit", 60f);
        Set("attackAimPoseWeight", 1f); Set("attackAimPitchLimit", 60f);
        Set("hasNetworkState", true);
        State(MovementState.idle);
        Packet(0f, 0f);
        Call("ResetRemoteLookTargets");
        Frames(90);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void PacketChangesMoveHeadAndTorsoAcrossFramesInsteadOfSnapping()
    {
        Packet(40f, 45f);
        Step();
        float firstYaw = Value("appliedLookYawOffset");
        float firstPitch = Value("appliedLookPitch");
        Assert.That(firstYaw, Is.InRange(0.01f, 39f));
        Assert.That(firstPitch, Is.InRange(0.01f, 44f));
        Assert.That(Quaternion.Angle(Quaternion.identity, head.localRotation), Is.InRange(0.01f, 39f));
        Assert.That(Quaternion.Angle(Quaternion.identity, chest.localRotation), Is.InRange(0.01f, 30f));

        Step();
        Assert.That(Value("appliedLookYawOffset"), Is.GreaterThan(firstYaw));
        Assert.That(Value("appliedLookPitch"), Is.GreaterThan(firstPitch));
        Assert.That(Value("appliedLookYawOffset"), Is.LessThan(40f));
        Frames(90);
        Assert.That(Value("appliedLookYawOffset"), Is.EqualTo(40f).Within(0.01f));
        Assert.That(Value("appliedLookPitch"), Is.EqualTo(45f).Within(0.01f));
        Assert.That(Value("networkLookYawOffset"), Is.EqualTo(40f));
        Assert.That(Value("networkLookPitch"), Is.EqualTo(45f));
    }

    [Test]
    public void TenHertzUpdatesContinueMovingOnFramesWithoutPackets()
    {
        float previous = 0f;
        float largestStep = 0f;
        int movingFramesWithoutPacket = 0;
        for (int frame = 0; frame < 30; frame++)
        {
            if (frame % 6 == 0) Packet((frame / 6 + 1) * 10f, (frame / 6 + 1) * 10f);
            Step();
            float current = Value("appliedLookPitch");
            float change = current - previous;
            largestStep = Mathf.Max(largestStep, Mathf.Abs(change));
            if (frame % 6 != 0 && change > 0.001f) movingFramesWithoutPacket++;
            previous = current;
        }
        Assert.That(movingFramesWithoutPacket, Is.EqualTo(25));
        Assert.That(largestStep, Is.LessThan(5f), "A ten-degree packet step must not become a ten-degree rendered jump.");
        Frames(90);
        Assert.That(Value("appliedLookPitch"), Is.EqualTo(50f).Within(0.01f));
        float resting = Value("appliedLookPitch");
        Frames(60);
        Assert.That(Value("appliedLookPitch"), Is.EqualTo(resting).Within(0.001f));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(120)]
    public void ResponseConvergesAtDifferentRenderFrameRates(int frameRate)
    {
        Packet(-45f, -50f);
        Step(1f / frameRate);
        Assert.That(Value("appliedLookYawOffset"), Is.InRange(-44f, -0.01f));
        for (int frame = 1; frame < frameRate; frame++) Step(1f / frameRate);
        Assert.That(Value("appliedLookYawOffset"), Is.EqualTo(-45f).Within(0.01f));
        Assert.That(Value("appliedLookPitch"), Is.EqualTo(-50f).Within(0.01f));
    }

    [Test]
    public void DirectionReversalRemainsBoundedAndSettlesAtTheNewTarget()
    {
        Packet(50f, 50f); Frames(60);
        Packet(-50f, -50f);
        Step();
        Assert.That(Value("appliedLookYawOffset"), Is.InRange(30f, 50.01f));
        Assert.That(Value("appliedLookPitch"), Is.InRange(30f, 50.01f));
        Frames(90);
        Assert.That(Value("appliedLookYawOffset"), Is.EqualTo(-50f).Within(0.01f));
        Assert.That(Value("appliedLookPitch"), Is.EqualTo(-50f).Within(0.01f));
    }

    [Test]
    public void YawCrossingTheAngleSeamUsesTheShortArc()
    {
        Packet(179f, 0f); Call("ResetRemoteLookTargets");
        Packet(-179f, 0f); Call("SmoothRemoteLookTargets", FrameTime);
        float first = Value("smoothedRemoteLookYawOffset");
        Assert.That(Mathf.DeltaAngle(179f, first), Is.InRange(0.001f, 1f));
        for (int frame = 0; frame < 90; frame++) Call("SmoothRemoteLookTargets", FrameTime);
        Assert.That(Mathf.Abs(Mathf.DeltaAngle(-179f, Value("smoothedRemoteLookYawOffset"))), Is.LessThan(0.01f));
    }

    [Test]
    public void WalkingReleaseAndIdleReacquisitionDoNotSnapOnTheSecondFrame()
    {
        Packet(45f, 45f); Frames(90);
        State(MovementState.walking); Frames(10);
        float releasedYaw = Value("appliedLookYawOffset");
        Assert.That(releasedYaw, Is.InRange(0.01f, 44f));
        State(MovementState.idle); Step();
        float first = Value("appliedLookYawOffset");
        Step();
        float second = Value("appliedLookYawOffset");
        Assert.That(Mathf.Abs(first - releasedYaw), Is.LessThan(3f));
        Assert.That(Mathf.Abs(second - first), Is.LessThan(4f));
        Assert.That(second, Is.LessThan(44f));
        Frames(10);
        Assert.That(Value("appliedLookYawOffset"), Is.GreaterThan(releasedYaw));
        Frames(90);
        Assert.That(Value("appliedLookYawOffset"), Is.EqualTo(45f).Within(0.01f));
    }

    [Test]
    public void AttackAimAndItsReleaseShareTheFilteredPitch()
    {
        ConfigureAttackAnimator();
        Packet(0f, 40f); Frames(90);
        Assert.That(Value("appliedAttackAimPitch"), Is.EqualTo(40f).Within(0.01f));

        animator.Play("Empty", 1, 0f); animator.Update(0f);
        Packet(0f, -30f);
        for (int frame = 0; frame < 20; frame++)
        {
            Step();
            Assert.That(Value("appliedLookPitch") + Value("appliedAttackAimPitch"),
                Is.EqualTo(Value("smoothedRemoteLookPitch")).Within(0.02f),
                "The attack release must transfer the rendered pitch to head look without a raw-packet jump.");
            if (frame == 0) Assert.That(Value("appliedLookPitch") + Value("appliedAttackAimPitch"), Is.GreaterThan(20f));
        }
        Frames(120);
        Assert.That(Value("appliedAttackAimPitch"), Is.EqualTo(0f).Within(0.01f));
        Assert.That(Value("appliedLookPitch"), Is.EqualTo(-30f).Within(0.01f));
    }

    [Test]
    public void ZeroFilterTimeIsImmediateAndZeroElapsedTimeDoesNotAdvanceIt()
    {
        Packet(30f, 40f);
        Step(0f);
        Assert.That(Value("smoothedRemoteLookYawOffset"), Is.Zero);
        Assert.That(Value("smoothedRemoteLookPitch"), Is.Zero);
        Set("remoteLookSmoothTime", 0f); Step();
        Assert.That(Value("appliedLookYawOffset"), Is.EqualTo(30f).Within(0.01f));
        Assert.That(Value("appliedLookPitch"), Is.EqualTo(40f).Within(0.01f));
    }

    [Test]
    public void FirstSnapshotAndTeleportResetTheFilterToTheReceivedDirection()
    {
        Packet(35f, 20f); Set("remoteLookTargetsInitialized", false); Step();
        Assert.That(Value("smoothedRemoteLookYawOffset"), Is.EqualTo(35f));
        Assert.That(Value("smoothedRemoteLookPitch"), Is.EqualTo(20f));
        Packet(-20f, -30f); Set("networkPosition", new Vector3(100f, 0f, 0f));
        Set("networkRotation", Quaternion.Euler(0f, 90f, 0f));
        Call("InterpolateRemoteTransform", 0.02f);
        Assert.That(body.position, Is.EqualTo(new Vector3(100f, 0f, 0f)));
        Assert.That(Value("smoothedRemoteLookYawOffset"), Is.EqualTo(-20f));
        Assert.That(Value("smoothedRemoteLookPitch"), Is.EqualTo(-30f));
    }

    [Test]
    public void RemoteFilterSettingsDoNotDelayTheLocalCameraOrChangeReceivedTargets()
    {
        var cameraObject = new GameObject("FP_Camera"); cameraObject.transform.SetParent(movement.transform, false);
        cameraObject.AddComponent<Camera>();
        var look = cameraObject.AddComponent<MouseLook>();
        typeof(PhotonView).GetProperty("IsMine").SetValue(movement.GetComponent<PhotonView>(), true);
        typeof(MouseLook).GetField("viewYaw", Private).SetValue(look, 25f);
        typeof(MouseLook).GetField("viewPitch", Private).SetValue(look, 20f);
        Set("mouseLook", look); Set("remoteLookSmoothTime", 2f);
        Packet(-40f, -40f);
        Call("UpdateLocalLookPose");
        Assert.That(Value("lookYawOffset"), Is.EqualTo(25f).Within(0.001f));
        Assert.That(Value("lookPitch"), Is.EqualTo(20f).Within(0.001f));
        Assert.That(Mathf.DeltaAngle(25f, cameraObject.transform.localEulerAngles.y), Is.EqualTo(0f).Within(0.001f));
        Assert.That(Mathf.DeltaAngle(-20f, cameraObject.transform.localEulerAngles.x), Is.EqualTo(0f).Within(0.001f));
        Assert.That(Value("networkLookYawOffset"), Is.EqualTo(-40f));
        Assert.That(Value("networkLookPitch"), Is.EqualTo(-40f));
    }

    [Test]
    public void SharedPlayerPrefabHasTheAuthoredRemoteResponseTime()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab");
        var data = new SerializedObject(prefab.GetComponent<PlayerMovement>());
        Assert.That(data.FindProperty("remoteLookSmoothTime").floatValue, Is.EqualTo(0.08f).Within(0.001f));
    }

    private void ConfigureAttackAnimator()
    {
        var controller = Track(new AnimatorController());
        controller.AddLayer("Base Layer"); controller.AddLayer("Upper Body Attack");
        foreach (var layer in controller.layers) objects.Add(layer.stateMachine);
        objects.Add(controller.layers[0].stateMachine.AddState("Idle"));
        var attack = controller.layers[1].stateMachine.AddState("Attack"); objects.Add(attack);
        objects.Add(controller.layers[1].stateMachine.AddState("Empty"));
        controller.layers[1].stateMachine.defaultState = attack;
        var actor = Track(new GameObject("Attack layer regression animator"));
        animator = actor.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.Rebind(); animator.SetLayerWeight(1, 1f);
        animator.Play("Upper Body Attack.Attack", 1, 0f); animator.Update(0f);
        Assert.That(animator.GetLayerWeight(1), Is.EqualTo(1f));
        Assert.That(animator.GetCurrentAnimatorStateInfo(1).shortNameHash, Is.EqualTo(Animator.StringToHash("Attack")));
        Set("modelAnimator", animator); Set("upperBodyAttackLayerIndex", 1);
    }

    private void Packet(float yaw, float pitch) { Set("networkLookYawOffset", yaw); Set("networkLookPitch", pitch); }
    private void State(MovementState state) => typeof(PlayerMovement).GetProperty("CurrentState").SetValue(movement, state);
    private void Frames(int count) { for (int frame = 0; frame < count; frame++) Step(); }
    private void Step(float deltaTime = FrameTime)
    {
        // Animator evaluation restores the base pose before each procedural overlay.
        spine.localRotation = chest.localRotation = head.localRotation = Quaternion.identity;
        Call("UpdateRemoteLookPose", deltaTime);
    }
    private T Track<T>(T value) where T : Object { objects.Add(value); return value; }
    private Transform Bone(string name, Transform parent)
    {
        var bone = new GameObject(name).transform; bone.SetParent(parent, false); return bone;
    }
    private void Set(string field, object value) => typeof(PlayerMovement).GetField(field, Private).SetValue(movement, value);
    private float Value(string field) => (float)typeof(PlayerMovement).GetField(field, Private).GetValue(movement);
    private void Call(string method, params object[] args) => typeof(PlayerMovement).GetMethod(method, Private).Invoke(movement, args);
}
