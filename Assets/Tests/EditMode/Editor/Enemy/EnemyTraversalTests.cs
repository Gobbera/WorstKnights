using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class EnemyTraversalTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void Route_KeepsTheChosenDirection()
    {
        GameObject volumeObject = CreateVolume(out EnemyTraversalVolume volume);
        try
        {
            var route = new List<Vector3>();

            Assert.That(volume.TryBuildRoute(EnemyTraversalSide.SideA, route, out EnemyTraversalSide exitSide), Is.True);
            Assert.That(exitSide, Is.EqualTo(EnemyTraversalSide.SideB));
            Assert.That(route[0].z, Is.EqualTo(-1f));
            Assert.That(route[1].z, Is.EqualTo(1f));

            Assert.That(volume.TryBuildRoute(EnemyTraversalSide.SideB, route, out exitSide), Is.True);
            Assert.That(exitSide, Is.EqualTo(EnemyTraversalSide.SideA));
            Assert.That(route[0].z, Is.EqualTo(1f));
            Assert.That(route[1].z, Is.EqualTo(-1f));

            Assert.That(volume.TryBuildRoute(
                EnemyTraversalSide.SideA, new Vector3(0.5f, 0f, -0.6f), route, out exitSide), Is.True);
            Assert.That(exitSide, Is.EqualTo(EnemyTraversalSide.SideB));
            Assert.That(route[0].x, Is.EqualTo(0f));
            Assert.That(route[0].z, Is.EqualTo(-0.6f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(volumeObject);
        }
    }

    [Test]
    public void DoorProbe_IgnoresTheLeafBehindTheEnemyButFindsItAhead()
    {
        GameObject volumeObject = CreateVolume(out EnemyTraversalVolume volume);
        GameObject doorObject = CreateDoor(out DoorController door, out Transform leaf);
        try
        {
            var route = new List<Vector3>();
            volume.TryBuildRoute(EnemyTraversalSide.SideA, route, out _);
            Vector3 actorPosition = new Vector3(0f, 1.2f, -0.6f);

            leaf.position = new Vector3(0f, 1.2f, -1f);
            Physics.SyncTransforms();
            Assert.That(volume.IsDoorObstructingTraversalRoute(door, actorPosition, route, 0.25f), Is.False);

            leaf.position = new Vector3(0f, 1.2f, 0f);
            Physics.SyncTransforms();
            Assert.That(volume.IsDoorObstructingTraversalRoute(door, actorPosition, route, 0.25f), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(doorObject);
            Object.DestroyImmediate(volumeObject);
        }
    }

    [Test]
    public void Traversal_PreservesTheNavigationDestination()
    {
        GameObject enemy = new GameObject("Enemy");
        try
        {
            EnemyNavigation navigation = enemy.AddComponent<EnemyNavigation>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            brain.ChangeState(EnemyState.Chase);

            Vector3 destination = new Vector3(0f, 0f, 4f);
            Assert.That(navigation.SetDestination(destination, 3f, 0.5f), Is.True);

            brain.BeginTraversal();
            Assert.That(navigation.HasDestination, Is.True);
            Assert.That(navigation.CurrentDestination, Is.EqualTo(destination));
            Assert.That(navigation.ForceRepath(), Is.False);

            brain.EndTraversal();
            Assert.That(navigation.HasDestination, Is.True);
            Assert.That(navigation.CurrentDestination, Is.EqualTo(destination));
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void Traversal_UsesTheNarrowCapsuleAndRestoresTheBodyBox()
    {
        GameObject enemy = new GameObject("Enemy");
        try
        {
            CapsuleCollider capsule = enemy.AddComponent<CapsuleCollider>();
            BoxCollider bodyBox = enemy.AddComponent<BoxCollider>();
            EnemyTraversalController traversal = enemy.AddComponent<EnemyTraversalController>();

            typeof(EnemyTraversalController).GetMethod("ApplyTemporaryCapsuleRadius", PrivateInstance)
                .Invoke(traversal, null);
            typeof(EnemyTraversalController).GetMethod("ApplyTemporaryBodyCollisionShape", PrivateInstance)
                .Invoke(traversal, null);

            Assert.That(capsule.enabled, Is.True);
            Assert.That(bodyBox.enabled, Is.False);

            bodyBox.enabled = true;
            typeof(EnemyTraversalController).GetMethod("KeepTemporaryBodyCollisionShape", PrivateInstance)
                .Invoke(traversal, null);
            Assert.That(bodyBox.enabled, Is.False);

            typeof(EnemyTraversalController).GetMethod("RestoreBodyCollisionShape", PrivateInstance)
                .Invoke(traversal, null);
            typeof(EnemyTraversalController).GetMethod("RestoreCapsuleRadius", PrivateInstance)
                .Invoke(traversal, null);
            Assert.That(bodyBox.enabled, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void Traversal_IgnoresOnlyTheOpenDoorLeafUntilItEnds()
    {
        GameObject doorObject = CreateDoor(out DoorController door, out Transform leaf);
        GameObject enemy = new GameObject("Enemy");
        try
        {
            CapsuleCollider capsule = enemy.AddComponent<CapsuleCollider>();
            EnemyTraversalController traversal = enemy.AddComponent<EnemyTraversalController>();
            Collider leafCollider = leaf.GetComponent<Collider>();
            typeof(EnemyTraversalController).GetField("activeDoor", PrivateInstance).SetValue(traversal, door);

            typeof(EnemyTraversalController).GetMethod("IgnoreOpenDoorCollision", PrivateInstance)
                .Invoke(traversal, null);
            Assert.That(Physics.GetIgnoreCollision(capsule, leafCollider), Is.True);

            typeof(EnemyTraversalController).GetMethod("RestoreDoorCollision", PrivateInstance)
                .Invoke(traversal, null);
            Assert.That(Physics.GetIgnoreCollision(capsule, leafCollider), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(doorObject);
        }
    }

    [Test]
    public void Traversal_StallTimerUsesProgressTowardTheCurrentPoint()
    {
        GameObject enemy = new GameObject("Enemy");
        try
        {
            EnemyTraversalController traversal = enemy.AddComponent<EnemyTraversalController>();
            var route = (List<Vector3>)typeof(EnemyTraversalController)
                .GetField("activeRoute", PrivateInstance).GetValue(traversal);
            route.Add(new Vector3(0f, 0f, 2f));

            typeof(EnemyTraversalController).GetMethod("ResetRouteProgress", PrivateInstance)
                .Invoke(traversal, null);
            typeof(EnemyTraversalController).GetField("lastRouteProgressTime", PrivateInstance)
                .SetValue(traversal, Time.time - 3f);

            var hasStalled = typeof(EnemyTraversalController).GetMethod("HasRouteStalled", PrivateInstance);
            Assert.That((bool)hasStalled.Invoke(traversal, new object[] { 2f }), Is.True);
            Assert.That((bool)hasStalled.Invoke(traversal, new object[] { 1.9f }), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void ClosedDoorRetry_AllowsReverseAfterCrossingButNotSameDirection()
    {
        GameObject volumeObject = CreateVolume(out EnemyTraversalVolume volume);
        GameObject doorObject = CreateDoor(out DoorController door, out _);
        GameObject enemy = new GameObject("Enemy");
        try
        {
            var volumeSettings = new SerializedObject(volume);
            volumeSettings.FindProperty("doorController").objectReferenceValue = door;
            volumeSettings.ApplyModifiedPropertiesWithoutUndo();

            EnemyNavigation navigation = enemy.AddComponent<EnemyNavigation>();
            EnemyTraversalController traversal = enemy.AddComponent<EnemyTraversalController>();
            var traversalSettings = new SerializedObject(traversal);
            traversalSettings.FindProperty("canOpenDoors").boolValue = true;
            traversalSettings.FindProperty("enemyNavigation").objectReferenceValue = navigation;
            traversalSettings.ApplyModifiedPropertiesWithoutUndo();

            enemy.transform.position = new Vector3(0f, 0f, -0.5f);
            navigation.SetDestination(new Vector3(0f, 0f, 4f), 3f, 0.5f);
            typeof(DoorController).GetField("isOpen", PrivateInstance).SetValue(door, false);
            typeof(DoorController).GetField("openProgress", PrivateInstance).SetValue(door, 0f);

            typeof(EnemyTraversalController).GetField("blockedVolumeUntilExit", PrivateInstance)
                .SetValue(traversal, volume);
            typeof(EnemyTraversalController).GetMethod("BlockCancelledVolume", PrivateInstance)
                .Invoke(traversal, new object[] { volume, false });

            Assert.That(CanRetry(traversal, volume, enemy.transform.position), Is.True);

            typeof(EnemyTraversalController).GetField("blockedVolumeUntilExit", PrivateInstance)
                .SetValue(traversal, volume);
            Assert.That(CanRetry(traversal, volume, enemy.transform.position), Is.False);

            typeof(EnemyTraversalController).GetMethod("BlockCompletedVolume", PrivateInstance)
                .Invoke(traversal, new object[] { volume, EnemyTraversalSide.SideB });
            enemy.transform.position = new Vector3(0f, 0f, 0.8f);
            Assert.That(CanRetry(traversal, volume, enemy.transform.position), Is.False);

            navigation.SetDestination(new Vector3(0f, 0f, -4f), 3f, 0.5f);
            Assert.That(CanRetry(traversal, volume, enemy.transform.position), Is.True);

            typeof(DoorController).GetField("isOpen", PrivateInstance).SetValue(door, true);
            Assert.That(CanRetry(traversal, volume, enemy.transform.position), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(doorObject);
            Object.DestroyImmediate(volumeObject);
        }
    }

    [Test]
    public void ClosedDoorWithDestinationAcross_OpensEvenWhenLeafProbeMisses()
    {
        GameObject volumeObject = CreateVolume(out EnemyTraversalVolume volume);
        GameObject doorObject = CreateDoor(out DoorController door, out Transform leaf);
        GameObject enemy = new GameObject("Enemy");
        try
        {
            var volumeSettings = new SerializedObject(volume);
            volumeSettings.FindProperty("doorController").objectReferenceValue = door;
            volumeSettings.ApplyModifiedPropertiesWithoutUndo();

            EnemyNavigation navigation = enemy.AddComponent<EnemyNavigation>();
            EnemyTraversalController traversal = enemy.AddComponent<EnemyTraversalController>();
            var traversalSettings = new SerializedObject(traversal);
            traversalSettings.FindProperty("canOpenDoors").boolValue = true;
            traversalSettings.FindProperty("enemyNavigation").objectReferenceValue = navigation;
            traversalSettings.ApplyModifiedPropertiesWithoutUndo();

            Vector3 bodyPosition = new Vector3(0f, 0f, -0.5f);
            enemy.transform.position = bodyPosition;
            leaf.position = new Vector3(10f, 1.2f, 0f);
            Physics.SyncTransforms();
            typeof(DoorController).GetField("isOpen", PrivateInstance).SetValue(door, false);
            typeof(DoorController).GetField("openProgress", PrivateInstance).SetValue(door, 0f);

            navigation.SetDestination(new Vector3(0f, 0f, 4f), 3f, 0.5f);
            Assert.That((bool)typeof(EnemyTraversalController)
                .GetMethod("CanPrepareDoorForTraversal", PrivateInstance)
                .Invoke(traversal, new object[] { volume, bodyPosition }), Is.True);

            navigation.SetDestination(new Vector3(0f, 0f, -4f), 3f, 0.5f);
            Assert.That((bool)typeof(EnemyTraversalController)
                .GetMethod("ShouldTreatClosedDoorAsBlocking", PrivateInstance)
                .Invoke(traversal, new object[] { volume, door, bodyPosition }), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(doorObject);
            Object.DestroyImmediate(volumeObject);
        }
    }

    [Test]
    public void CancelledTraversal_DoesNotRequireExitWhenEnemyStayedOnEntrySide()
    {
        GameObject volumeObject = CreateVolume(out EnemyTraversalVolume volume);
        GameObject enemy = new GameObject("Enemy");
        try
        {
            EnemyTraversalController traversal = enemy.AddComponent<EnemyTraversalController>();
            enemy.transform.position = new Vector3(0f, 0f, -0.5f);
            typeof(EnemyTraversalController).GetField("activeVolume", PrivateInstance).SetValue(traversal, volume);
            typeof(EnemyTraversalController).GetField("activeExitSide", PrivateInstance)
                .SetValue(traversal, EnemyTraversalSide.SideB);
            typeof(EnemyTraversalController).GetField("traversing", PrivateInstance).SetValue(traversal, true);

            typeof(EnemyTraversalController).GetMethod("CancelTraversal", PrivateInstance).Invoke(traversal, null);

            Assert.That(typeof(EnemyTraversalController).GetField("blockedVolumeUntilExit", PrivateInstance)
                .GetValue(traversal), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(volumeObject);
        }
    }

    [Test]
    public void ChaseWithoutSight_ApproachesLastKnownPositionBeforeSearching()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject targetObject = new GameObject("Target");
        try
        {
            EnemyNavigation navigation = enemy.AddComponent<EnemyNavigation>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            PlayerHealth target = targetObject.AddComponent<PlayerHealth>();
            var settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.FindProperty("chaseStoppingDistance").floatValue = 1.75f;
            settings.FindProperty("searchStoppingDistance").floatValue = 0.75f;
            settings.ApplyModifiedPropertiesWithoutUndo();

            typeof(EnemyBrain).GetField("currentTarget", PrivateInstance).SetValue(brain, target);
            brain.SetLastKnownTargetPosition(new Vector3(0f, 0f, 4f));
            brain.ChangeState(EnemyState.Chase);

            new EnemyChaseState(brain).Tick(0.02f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Chase));
            Assert.That(navigation.CurrentDestination.z, Is.EqualTo(4f));
            Assert.That(typeof(EnemyNavigation).GetField("currentStoppingDistance", PrivateInstance)
                .GetValue(navigation), Is.EqualTo(0.75f));
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void LostTarget_RemainsInChaseUntilTheLastKnownPositionIsReached()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject targetObject = new GameObject("Target");
        try
        {
            EnemyNavigation navigation = enemy.AddComponent<EnemyNavigation>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            PlayerHealth target = targetObject.AddComponent<PlayerHealth>();
            var settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();

            typeof(EnemyBrain).GetField("currentTarget", PrivateInstance).SetValue(brain, target);
            brain.SetLastKnownTargetPosition(new Vector3(0f, 0f, 4f));
            brain.ChangeState(EnemyState.Chase);
            EnemyStateMachine machine = (EnemyStateMachine)typeof(EnemyBrain)
                .GetField("stateMachine", PrivateInstance).GetValue(brain);

            machine.Tick(0.02f);
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Chase));
            Assert.That(navigation.CurrentDestination.z, Is.EqualTo(4f));

            enemy.transform.position = new Vector3(0f, 0f, 2f);
            machine.Tick(0.02f);
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Chase));
            Assert.That(brain.CurrentTarget, Is.SameAs(target));

            enemy.transform.position = new Vector3(0f, 0f, 3.2f);
            machine.Tick(0.02f);
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Search));
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void StalledChaseWithSearchAreaEntersSearchInsteadOfReflection()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject targetObject = new GameObject("Target");
        GameObject roomObject = new GameObject("Room");
        try
        {
            enemy.AddComponent<EnemyNavigation>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            PlayerHealth target = targetObject.AddComponent<PlayerHealth>();
            roomObject.AddComponent<BoxCollider>().size = new Vector3(20f, 4f, 8f);
            roomObject.AddComponent<EnemySearchArea>();
            new GameObject("Search Point").transform.SetParent(roomObject.transform, false);
            var settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("lastSeenStallTimeout").floatValue = 0.1f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            typeof(EnemyBrain).GetField("currentTarget", PrivateInstance).SetValue(brain, target);
            brain.SetLastKnownTargetPosition(new Vector3(5f, 0f, 0f));
            Physics.SyncTransforms();

            brain.ChangeState(EnemyState.Chase);
            EnemyStateMachine machine = (EnemyStateMachine)typeof(EnemyBrain)
                .GetField("stateMachine", PrivateInstance).GetValue(brain);
            machine.Tick(0.11f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Search));
        }
        finally
        {
            Object.DestroyImmediate(roomObject);
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void RegainedContact_UpdatesPursuitWithoutLeavingChase()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject targetObject = new GameObject("Target");
        try
        {
            EnemyNavigation navigation = enemy.AddComponent<EnemyNavigation>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            PlayerHealth target = targetObject.AddComponent<PlayerHealth>();
            var settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();

            typeof(EnemyBrain).GetField("currentTarget", PrivateInstance).SetValue(brain, target);
            brain.SetLastKnownTargetPosition(new Vector3(0f, 0f, 4f));
            brain.ChangeState(EnemyState.Chase);
            EnemyStateMachine machine = (EnemyStateMachine)typeof(EnemyBrain)
                .GetField("stateMachine", PrivateInstance).GetValue(brain);
            machine.Tick(0.02f);

            targetObject.transform.position = new Vector3(2f, 0f, 6f);
            brain.ReportPlayerContact(target);
            machine.Tick(0.02f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Chase));
            Assert.That(brain.CurrentTarget, Is.SameAs(target));
            Assert.That(navigation.CurrentDestination, Is.EqualTo(targetObject.transform.position));
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void Search_DoesNotStartRoomSweepBeforeReachingTheObservedPosition()
    {
        GameObject enemy = new GameObject("Enemy");
        try
        {
            EnemyNavigation navigation = enemy.AddComponent<EnemyNavigation>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            var settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            brain.SetLastKnownTargetPosition(new Vector3(0f, 0f, 4f));
            brain.ChangeState(EnemyState.Search);

            navigation.SetDestination(new Vector3(0f, 0f, 0.1f), 3f, 0.75f, force: true);
            EnemyStateMachine machine = (EnemyStateMachine)typeof(EnemyBrain)
                .GetField("stateMachine", PrivateInstance).GetValue(brain);
            machine.Tick(0.02f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Search));
            Assert.That(navigation.HasDestination, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void TargetLostDuringAlert_ContinuesToLastSeenPositionInChase()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject targetObject = new GameObject("Target");
        try
        {
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            PlayerHealth target = targetObject.AddComponent<PlayerHealth>();
            var settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.FindProperty("alertDuration").floatValue = 0f;
            settings.ApplyModifiedPropertiesWithoutUndo();

            typeof(EnemyBrain).GetField("currentTarget", PrivateInstance).SetValue(brain, target);
            targetObject.transform.position = new Vector3(0f, 0f, 4f);
            brain.BeginTargetAlert();
            brain.ChangeState(EnemyState.Alert);
            EnemyStateMachine machine = (EnemyStateMachine)typeof(EnemyBrain)
                .GetField("stateMachine", PrivateInstance).GetValue(brain);
            machine.Tick(0.02f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Chase));
            Assert.That(brain.LastKnownTargetPosition.z, Is.EqualTo(4f));
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void SearchArea_UsesChildPointsInHierarchyOrder()
    {
        GameObject areaObject = new GameObject("Search Area");
        try
        {
            EnemySearchArea area = areaObject.AddComponent<EnemySearchArea>();
            Transform first = new GameObject("Point 1").transform;
            Transform second = new GameObject("Point 2").transform;
            first.SetParent(areaObject.transform);
            second.SetParent(areaObject.transform);
            first.position = new Vector3(1f, 0f, 0f);
            second.position = new Vector3(2f, 0f, 0f);

            Assert.That(area.PointCount, Is.EqualTo(2));
            Assert.That(area.TryGetPoint(0, out Vector3 firstPosition), Is.True);
            Assert.That(firstPosition, Is.EqualTo(first.position));
            Assert.That(area.TryGetPoint(1, out Vector3 secondPosition), Is.True);
            Assert.That(secondPosition, Is.EqualTo(second.position));
        }
        finally
        {
            Object.DestroyImmediate(areaObject);
        }
    }

    private static bool CanRetry(EnemyTraversalController traversal, EnemyTraversalVolume volume, Vector3 position)
    {
        return (bool)typeof(EnemyTraversalController)
            .GetMethod("ShouldForceClosedDoorTraversalRetry", PrivateInstance)
            .Invoke(traversal, new object[] { volume, position });
    }

    [Test]
    public void RoomSearch_UsesLinkedDoorSideAndRejectsLockedDoor()
    {
        GameObject volumeObject = CreateVolume(out EnemyTraversalVolume volume);
        GameObject doorObject = CreateDoor(out DoorController door, out _);
        GameObject firstAreaObject = new GameObject("Room A");
        GameObject secondAreaObject = new GameObject("Room B");
        GameObject enemy = new GameObject("Enemy");
        try
        {
            firstAreaObject.transform.position = Vector3.back * 3f;
            secondAreaObject.transform.position = Vector3.forward * 3f;
            firstAreaObject.AddComponent<BoxCollider>().size = new Vector3(8f, 4f, 4f);
            secondAreaObject.AddComponent<BoxCollider>().size = new Vector3(8f, 4f, 4f);
            EnemySearchArea roomA = firstAreaObject.AddComponent<EnemySearchArea>();
            EnemySearchArea roomB = secondAreaObject.AddComponent<EnemySearchArea>();
            var volumeSettings = new SerializedObject(volume);
            volumeSettings.FindProperty("doorController").objectReferenceValue = door;
            volumeSettings.FindProperty("sideASearchArea").objectReferenceValue = roomA;
            volumeSettings.FindProperty("sideBSearchArea").objectReferenceValue = roomB;
            volumeSettings.ApplyModifiedPropertiesWithoutUndo();

            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            enemy.transform.position = Vector3.back * 2f;
            brain.SetLastKnownTargetPosition(enemy.transform.position);

            Assert.That(brain.TryPrepareDoorBetweenAreas(roomA, roomB), Is.True);
            Assert.That(brain.TryGetDoorInvestigation(out EnemyTraversalVolume chosen,
                out EnemyTraversalSide side), Is.True);
            Assert.That(chosen, Is.EqualTo(volume));
            Assert.That(side, Is.EqualTo(EnemyTraversalSide.SideB));
            Assert.That(brain.LastKnownPursuitPosition.z, Is.EqualTo(1f));

            typeof(DoorController).GetField("isLocked", PrivateInstance).SetValue(door, true);
            Assert.That(brain.TryPrepareDoorBetweenAreas(roomA, roomB), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(secondAreaObject);
            Object.DestroyImmediate(firstAreaObject);
            Object.DestroyImmediate(doorObject);
            Object.DestroyImmediate(volumeObject);
        }
    }

    private static GameObject CreateVolume(out EnemyTraversalVolume volume)
    {
        GameObject root = new GameObject("Traversal Volume");
        volume = root.AddComponent<EnemyTraversalVolume>();
        BoxCollider trigger = root.GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(3f, 3f, 3f);
        trigger.center = new Vector3(0f, 1.5f, 0f);

        Transform sideA = new GameObject("Side A").transform;
        Transform sideB = new GameObject("Side B").transform;
        sideA.SetParent(root.transform);
        sideB.SetParent(root.transform);
        sideA.localPosition = new Vector3(0f, 1.2f, -1f);
        sideB.localPosition = new Vector3(0f, 1.2f, 1f);

        var settings = new SerializedObject(volume);
        settings.FindProperty("sideA").objectReferenceValue = sideA;
        settings.FindProperty("sideB").objectReferenceValue = sideB;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    private static GameObject CreateDoor(out DoorController door, out Transform leaf)
    {
        GameObject root = new GameObject("Door");
        leaf = new GameObject("Leaf").transform;
        leaf.SetParent(root.transform);
        BoxCollider collider = leaf.gameObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.4f, 2.4f, 0.1f);

        door = root.AddComponent<DoorController>();
        var settings = new SerializedObject(door);
        settings.FindProperty("movingPart").objectReferenceValue = leaf;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }
}
