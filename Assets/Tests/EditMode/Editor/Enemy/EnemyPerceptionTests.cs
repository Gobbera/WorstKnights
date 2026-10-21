using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class EnemyPerceptionTests
{
    [Test]
    public void EnemyBrain_CurrentRoomTracksColliderMembershipWithoutDetectingPlayers()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject roomAObject = new GameObject("Room A");
        GameObject roomBObject = new GameObject("Room B");
        try
        {
            roomAObject.AddComponent<BoxCollider>().size = new Vector3(8f, 4f, 8f);
            roomBObject.transform.position = Vector3.right * 10f;
            roomBObject.AddComponent<BoxCollider>().size = new Vector3(8f, 4f, 8f);
            EnemySearchArea roomA = roomAObject.AddComponent<EnemySearchArea>();
            EnemySearchArea roomB = roomBObject.AddComponent<EnemySearchArea>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            Physics.SyncTransforms();

            Assert.That(brain.CurrentRoom, Is.SameAs(roomA));
            Assert.That(brain.HasLastKnownTargetPosition, Is.False);
            enemy.transform.position = Vector3.right * 5f;
            Physics.SyncTransforms();
            Assert.That(brain.CurrentRoom, Is.SameAs(roomA));
            enemy.transform.position = Vector3.right * 10f;
            Physics.SyncTransforms();
            Assert.That(brain.CurrentRoom, Is.SameAs(roomB));
            Assert.That(brain.HasLastKnownTargetPosition, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(roomBObject);
            Object.DestroyImmediate(roomAObject);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void SearchArea_UsesAllAttachedBoundsAndExplicitChildBounds()
    {
        GameObject areaObject = new GameObject("L-shaped Room");
        GameObject childObject = new GameObject("Room Extension");
        try
        {
            BoxCollider main = areaObject.AddComponent<BoxCollider>();
            main.size = new Vector3(4f, 4f, 4f);
            BoxCollider attached = areaObject.AddComponent<BoxCollider>();
            attached.center = new Vector3(5f, 0f, 0f);
            attached.size = new Vector3(4f, 4f, 4f);
            EnemySearchArea area = areaObject.AddComponent<EnemySearchArea>();

            childObject.transform.SetParent(areaObject.transform, false);
            childObject.transform.localPosition = new Vector3(0f, 0f, 5f);
            BoxCollider child = childObject.AddComponent<BoxCollider>();
            child.size = new Vector3(4f, 4f, 4f);
            var settings = new SerializedObject(area);
            SerializedProperty extra = settings.FindProperty("additionalAreaBounds");
            extra.arraySize = 1;
            extra.GetArrayElementAtIndex(0).objectReferenceValue = child;
            settings.ApplyModifiedPropertiesWithoutUndo();
            area.enabled = false;
            area.enabled = true;
            Physics.SyncTransforms();

            Assert.That(area.ContainsPoint(Vector3.zero), Is.True);
            Assert.That(area.ContainsPoint(new Vector3(5f, 0f, 0f)), Is.True);
            Assert.That(area.ContainsPoint(new Vector3(0f, 0f, 5f)), Is.True);
            Assert.That(area.ContainsPoint(new Vector3(10f, 0f, 10f)), Is.False);
            Assert.That(EnemySearchArea.FindContaining(new Vector3(5f, 0f, 0f), areaObject.scene), Is.SameAs(area));
            Assert.That(EnemySearchArea.FindContaining(new Vector3(0f, 0f, 5f), areaObject.scene), Is.SameAs(area));

            attached.enabled = false;
            Assert.That(area.ContainsPoint(new Vector3(5f, 0f, 0f)), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(childObject);
            Object.DestroyImmediate(areaObject);
        }
    }

    [Test]
    public void EnemySearch_StalledWithKnownAreaStartsRoomSweep()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject roomObject = new GameObject("Room");
        try
        {
            enemy.AddComponent<EnemyNavigation>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            roomObject.AddComponent<BoxCollider>().size = new Vector3(30f, 4f, 8f);
            roomObject.AddComponent<EnemySearchArea>();
            new GameObject("Search Point").transform.SetParent(roomObject.transform, false);
            var settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("lastSeenStallTimeout").floatValue = 3f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            brain.SetLastKnownTargetPosition(new Vector3(10f, 0f, 0f));
            Physics.SyncTransforms();

            brain.ChangeState(EnemyState.Search);
            EnemySearchState search = new EnemySearchState(brain);
            search.Enter();
            search.Tick(3f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Search));
        }
        finally
        {
            Object.DestroyImmediate(roomObject);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void InvestigationMemory_VisitsOtherPlayerBeforeSearchingOriginalAndNextRoom()
    {
        GameObject firstAreaObject = new GameObject("Room A");
        GameObject secondAreaObject = new GameObject("Room B");
        GameObject firstPlayerObject = new GameObject("First Player");
        GameObject secondPlayerObject = new GameObject("Second Player");
        try
        {
            firstAreaObject.transform.position = Vector3.zero;
            secondAreaObject.transform.position = Vector3.right * 10f;
            firstAreaObject.AddComponent<BoxCollider>().size = new Vector3(8f, 4f, 8f);
            secondAreaObject.AddComponent<BoxCollider>().size = new Vector3(8f, 4f, 8f);
            EnemySearchArea roomA = firstAreaObject.AddComponent<EnemySearchArea>();
            EnemySearchArea roomB = secondAreaObject.AddComponent<EnemySearchArea>();
            new GameObject("Point A").transform.SetParent(firstAreaObject.transform, false);
            new GameObject("Point B").transform.SetParent(secondAreaObject.transform, false);
            PlayerHealth first = firstPlayerObject.AddComponent<PlayerHealth>();
            PlayerHealth second = secondPlayerObject.AddComponent<PlayerHealth>();
            firstPlayerObject.transform.position = Vector3.right;
            secondPlayerObject.transform.position = Vector3.right * 2f;
            Physics.SyncTransforms();

            var memory = new EnemyInvestigationMemory();
            memory.Record(first, firstPlayerObject.transform.position, 0f, firstPlayerObject.scene);
            memory.Record(second, secondPlayerObject.transform.position, 0.1f, secondPlayerObject.scene);
            memory.BeginInvestigation(first);
            memory.MarkPointVisited(first);

            Assert.That(memory.TryGetNextPoint(first, 1f, 45f, out PlayerHealth next,
                out Vector3 nextPosition), Is.True);
            Assert.That(next, Is.EqualTo(second));
            Assert.That(nextPosition, Is.EqualTo(secondPlayerObject.transform.position));
            memory.MarkPointVisited(second);
            Assert.That(memory.TryGetNextPoint(second, 1f, 45f, out _, out _), Is.False);

            firstPlayerObject.transform.position = Vector3.right * 11f;
            Physics.SyncTransforms();
            memory.ObserveRoomChanges(2f, 0.2f, 45f, firstPlayerObject.scene);
            memory.ObserveRoomChanges(2.25f, 0.2f, 45f, firstPlayerObject.scene);
            var route = new List<EnemySearchArea>();
            memory.BuildSearchRoute(route, 3f, 45f);

            Assert.That(route, Is.EqualTo(new[] { roomA, roomB }));
            var snapshots = new List<EnemyInvestigationMemory.Snapshot>();
            memory.Capture(snapshots, 3f, 45f);
            var restored = new EnemyInvestigationMemory();
            restored.Restore(snapshots, memory.PrimaryArea, memory.PrimaryPlayer, 4f, 1f);
            restored.BuildSearchRoute(route, 4f, 45f);
            Assert.That(route, Is.EqualTo(new[] { roomA, roomB }));
        }
        finally
        {
            Object.DestroyImmediate(secondPlayerObject);
            Object.DestroyImmediate(firstPlayerObject);
            Object.DestroyImmediate(secondAreaObject);
            Object.DestroyImmediate(firstAreaObject);
        }
    }

    [Test]
    public void InvestigationMemory_IgnoresBriefBorderCrossingAndExpiresOldEvidence()
    {
        GameObject firstAreaObject = new GameObject("Room A");
        GameObject secondAreaObject = new GameObject("Room B");
        GameObject playerObject = new GameObject("Player");
        try
        {
            firstAreaObject.AddComponent<BoxCollider>().size = new Vector3(8f, 4f, 8f);
            secondAreaObject.transform.position = Vector3.right * 10f;
            secondAreaObject.AddComponent<BoxCollider>().size = new Vector3(8f, 4f, 8f);
            EnemySearchArea roomA = firstAreaObject.AddComponent<EnemySearchArea>();
            secondAreaObject.AddComponent<EnemySearchArea>();
            new GameObject("Point A").transform.SetParent(firstAreaObject.transform, false);
            new GameObject("Point B").transform.SetParent(secondAreaObject.transform, false);
            PlayerHealth player = playerObject.AddComponent<PlayerHealth>();
            playerObject.transform.position = Vector3.right;
            Physics.SyncTransforms();

            var memory = new EnemyInvestigationMemory();
            memory.Record(player, playerObject.transform.position, 0f, playerObject.scene);
            memory.BeginInvestigation(player);
            playerObject.transform.position = Vector3.right * 11f;
            Physics.SyncTransforms();
            memory.ObserveRoomChanges(1f, 0.2f, 45f, playerObject.scene);
            playerObject.transform.position = Vector3.right;
            Physics.SyncTransforms();
            memory.ObserveRoomChanges(1.1f, 0.2f, 45f, playerObject.scene);

            var route = new List<EnemySearchArea>();
            memory.BuildSearchRoute(route, 2f, 45f);
            Assert.That(route, Is.EqualTo(new[] { roomA }));
            Assert.That(memory.TryGetNextPoint(null, 46f, 45f, out _, out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(secondAreaObject);
            Object.DestroyImmediate(firstAreaObject);
        }
    }

    [Test]
    public void InvestigationMemory_OverlappingRoomBoundsDoNotTriggerEarlyTransition()
    {
        GameObject firstAreaObject = new GameObject("Room A");
        GameObject secondAreaObject = new GameObject("Room B");
        GameObject playerObject = new GameObject("Player");
        try
        {
            firstAreaObject.AddComponent<BoxCollider>().size = new Vector3(10f, 4f, 8f);
            secondAreaObject.transform.position = Vector3.right * 6f;
            secondAreaObject.AddComponent<BoxCollider>().size = new Vector3(10f, 4f, 8f);
            EnemySearchArea roomA = firstAreaObject.AddComponent<EnemySearchArea>();
            EnemySearchArea roomB = secondAreaObject.AddComponent<EnemySearchArea>();
            new GameObject("Point A").transform.SetParent(firstAreaObject.transform, false);
            new GameObject("Point B").transform.SetParent(secondAreaObject.transform, false);
            PlayerHealth player = playerObject.AddComponent<PlayerHealth>();
            Physics.SyncTransforms();

            var memory = new EnemyInvestigationMemory();
            memory.Record(player, Vector3.zero, 0f, playerObject.scene);
            memory.BeginInvestigation(player);
            playerObject.transform.position = Vector3.right * 2f;
            Physics.SyncTransforms();
            memory.ObserveRoomChanges(1f, 0.2f, 45f, playerObject.scene);
            memory.ObserveRoomChanges(2f, 0.2f, 45f, playerObject.scene);
            var route = new List<EnemySearchArea>();
            memory.BuildSearchRoute(route, 2f, 45f);
            Assert.That(route, Is.EqualTo(new[] { roomA }));

            playerObject.transform.position = Vector3.right * 6f;
            Physics.SyncTransforms();
            memory.ObserveRoomChanges(3f, 0.2f, 45f, playerObject.scene);
            memory.ObserveRoomChanges(3.3f, 0.2f, 45f, playerObject.scene);
            memory.BuildSearchRoute(route, 4f, 45f);
            Assert.That(route, Is.EqualTo(new[] { roomA, roomB }));
        }
        finally
        {
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(secondAreaObject);
            Object.DestroyImmediate(firstAreaObject);
        }
    }

    [Test]
    public void EnemyBrain_HeardSecondPlayerBecomesNextPointWithoutRestartingChase()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject firstPlayer = new GameObject("First Player");
        GameObject secondPlayer = new GameObject("Second Player");
        try
        {
            enemy.AddComponent<EnemyHearing>();
            enemy.AddComponent<EnemyPerception>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            PlayerHealth first = firstPlayer.AddComponent<PlayerHealth>();
            PlayerHealth second = secondPlayer.AddComponent<PlayerHealth>();
            firstPlayer.transform.position = Vector3.forward;
            secondPlayer.transform.position = Vector3.right * 2f;

            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();

            brain.ReportPlayerContact(first);
            brain.ChangeState(EnemyState.Chase);
            NoiseSystem.EmitNoise(secondPlayer.transform.position, 5f, 1f,
                NoiseType.Footstep, secondPlayer);
            brain.BeginLastKnownInvestigation();

            Assert.That(brain.CurrentTarget, Is.EqualTo(first));
            brain.ReportPlayerContact(second);
            Assert.That(brain.CurrentTarget, Is.EqualTo(first));

            Assert.That(brain.TryPursueNextPlayerEvidence(), Is.True);
            Assert.That(brain.CurrentTarget, Is.EqualTo(second));
            Assert.That(brain.LastKnownTargetPosition, Is.EqualTo(secondPlayer.transform.position));
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Chase));
            Assert.That(brain.ActiveAlertSequence, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(secondPlayer);
            Object.DestroyImmediate(firstPlayer);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyBrain_ChaseDoesNotAbandonHiddenPrimaryForVisibleSecondary()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject primaryObject = new GameObject("Primary");
        GameObject secondaryObject = new GameObject("Secondary");
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            enemy.AddComponent<EnemyVision>();
            enemy.AddComponent<EnemyPerception>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            PlayerHealth primary = primaryObject.AddComponent<PlayerHealth>();
            primaryObject.AddComponent<CapsuleCollider>();
            secondaryObject.AddComponent<PlayerHealth>();
            secondaryObject.AddComponent<CapsuleCollider>();
            primaryObject.transform.position = new Vector3(0f, 0f, 8f);
            secondaryObject.transform.position = new Vector3(3f, 0f, 3f);
            obstacle.transform.position = new Vector3(0f, 1f, 4f);
            obstacle.transform.localScale = new Vector3(1f, 2f, 0.3f);

            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            brain.ReportPlayerContact(primary);
            brain.ChangeState(EnemyState.Chase);
            Physics.SyncTransforms();

            Assert.That(brain.TryRefreshVisibleTarget(), Is.False);
            Assert.That(brain.CurrentTarget, Is.EqualTo(primary));
        }
        finally
        {
            Object.DestroyImmediate(obstacle);
            Object.DestroyImmediate(secondaryObject);
            Object.DestroyImmediate(primaryObject);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemySearch_HeardPlayerResumesChaseWithoutAnotherAlert()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject playerObject = new GameObject("Player");
        try
        {
            enemy.AddComponent<EnemyHearing>();
            enemy.AddComponent<EnemyPerception>();
            EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
            PlayerHealth player = playerObject.AddComponent<PlayerHealth>();
            playerObject.transform.position = Vector3.right * 2f;

            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            brain.BeginInvestigationAlert(Vector3.right);
            brain.SetLastKnownTargetPosition(Vector3.right);
            brain.ChangeState(EnemyState.Search);
            NoiseSystem.EmitNoise(playerObject.transform.position, 5f, 1f,
                NoiseType.Footstep, playerObject);

            new EnemySearchState(brain).Tick(0.02f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Chase));
            Assert.That(brain.CurrentTarget, Is.EqualTo(player));
            Assert.That(brain.ActiveAlertSequence, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyHearing_CrouchRadiusStartsAtBodyEdge()
    {
        Bounds enemyBody = new Bounds(new Vector3(0f, 1.3f, 0f), new Vector3(1f, 1.5f, 1f));
        Bounds playerBody = new Bounds(new Vector3(1.4f, 0f, 0f), new Vector3(1f, 2f, 1f));

        Assert.That(EnemyHearing.PlanarBodyGap(enemyBody, playerBody), Is.EqualTo(0.4f).Within(0.001f));
        Assert.That(EnemyHearing.PlanarBodyGap(enemyBody, playerBody), Is.LessThan(0.5f));

        playerBody.center = new Vector3(1.6f, 0f, 0f);
        Assert.That(EnemyHearing.PlanarBodyGap(enemyBody, playerBody), Is.GreaterThan(0.5f));
    }

    [Test]
    public void PlayerMovementNoiseSource_UsesMovementRadiiOnlyWhileMovingOnGround()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab");
        Assert.That(playerPrefab, Is.Not.Null);
        PlayerMovementNoiseSource source = playerPrefab.GetComponent<PlayerMovementNoiseSource>();
        Assert.That(source, Is.Not.Null);

        Assert.That(source.ResolveRadius(MovementState.idle, 0f, true), Is.Zero);
        Assert.That(source.ResolveRadius(MovementState.crouching, 0f, true), Is.Zero);
        Assert.That(source.ResolveRadius(MovementState.walking, 0.1f, true), Is.Zero);
        Assert.That(source.ResolveRadius(MovementState.crouching, 1f, true), Is.EqualTo(0.5f));
        Assert.That(source.ResolveRadius(MovementState.walking, 3f, true), Is.EqualTo(7f));
        Assert.That(source.ResolveRadius(MovementState.sprinting, 7f, true), Is.EqualTo(10f));
        Assert.That(source.ResolveRadius(MovementState.walking, 3f, false), Is.Zero);
        Assert.That(source.ResolveRadius(MovementState.air, 3f, true), Is.Zero);

        GameObject beholderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Beholder/Beholder.prefab");
        Assert.That(beholderPrefab, Is.Not.Null);
        EnemyHearing hearing = beholderPrefab.GetComponent<EnemyHearing>();
        Assert.That(hearing, Is.Not.Null);
        Assert.That(new SerializedObject(hearing).FindProperty("detectPlayerMovement").boolValue, Is.True);
    }

    [Test]
    public void EnemyHearing_ConsumesNoiseWhenEventIsAudible()
    {
        GameObject enemy = new GameObject("Enemy");
        EnemyHearing hearing = enemy.AddComponent<EnemyHearing>();
        try
        {
            SerializedObject serializedObject = new SerializedObject(hearing);
            serializedObject.FindProperty("enableHearing").boolValue = true;
            serializedObject.FindProperty("hearingMultiplier").floatValue = 1f;
            serializedObject.FindProperty("minimumAudibleIntensity").floatValue = 0.05f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            NoiseSystem.EmitNoise(new Vector3(3f, 0f, 0f), radius: 5f, intensity: 1f, NoiseType.Footstep);
            hearing.ClearPendingPlayerMovementNoise();

            Assert.That(hearing.TryConsumeNoise(out NoiseEvent noiseEvent), Is.True);
            Assert.That(noiseEvent.Position, Is.EqualTo(new Vector3(3f, 0f, 0f)));
            Assert.That(hearing.TryConsumeNoise(out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyVision_DoesNotSeeTargetThroughObstacle()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject player = new GameObject("Player");
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);

        EnemyVision vision = enemy.AddComponent<EnemyVision>();
        CapsuleCollider playerCollider = player.AddComponent<CapsuleCollider>();
        PlayerHealth playerHealth = player.AddComponent<PlayerHealth>();

        try
        {
            enemy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            player.transform.position = new Vector3(0f, 0f, 5f);
            playerCollider.center = Vector3.up;
            playerCollider.height = 2f;
            playerCollider.radius = 0.35f;

            obstacle.transform.position = new Vector3(0f, 1f, 2.5f);
            obstacle.transform.localScale = new Vector3(2f, 2f, 0.25f);

            SerializedObject serializedObject = new SerializedObject(vision);
            serializedObject.FindProperty("enableVision").boolValue = true;
            serializedObject.FindProperty("viewDistance").floatValue = 10f;
            serializedObject.FindProperty("viewAngle").floatValue = 90f;
            serializedObject.FindProperty("targetLayers").intValue = Physics.DefaultRaycastLayers;
            serializedObject.FindProperty("obstacleLayers").intValue = Physics.DefaultRaycastLayers;
            serializedObject.FindProperty("eyeHeightFallback").floatValue = 1f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            Physics.SyncTransforms();

            Assert.That(vision.CanSeeTarget(playerHealth, out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(obstacle);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyVision_CanSenseCloseTargetOutsideViewCone()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject player = new GameObject("Player");

        EnemyVision vision = enemy.AddComponent<EnemyVision>();
        CapsuleCollider playerCollider = player.AddComponent<CapsuleCollider>();
        PlayerHealth playerHealth = player.AddComponent<PlayerHealth>();

        try
        {
            enemy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.forward));
            player.transform.position = new Vector3(0f, 0f, -1f);
            playerCollider.center = Vector3.up;
            playerCollider.height = 2f;
            playerCollider.radius = 0.35f;

            SerializedObject serializedObject = new SerializedObject(vision);
            serializedObject.FindProperty("enableVision").boolValue = true;
            serializedObject.FindProperty("viewDistance").floatValue = 10f;
            serializedObject.FindProperty("viewAngle").floatValue = 45f;
            serializedObject.FindProperty("targetLayers").intValue = Physics.DefaultRaycastLayers;
            serializedObject.FindProperty("obstacleLayers").intValue = Physics.DefaultRaycastLayers;
            serializedObject.FindProperty("eyeHeightFallback").floatValue = 1f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            Physics.SyncTransforms();

            Assert.That(vision.CanSeeTarget(playerHealth, out _), Is.False);
            Assert.That(vision.CanSenseTargetInRadius(playerHealth, 1.6f, requireLineOfSight: true, out _), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyAttack_Disabled_DoesNotStartCommonAttack()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject player = new GameObject("Player");

        enemy.AddComponent<EnemyHealth>();
        enemy.AddComponent<EnemySetup>();
        EnemyAttack attack = enemy.AddComponent<EnemyAttack>();
        PlayerHealth playerHealth = player.AddComponent<PlayerHealth>();

        try
        {
            SerializedObject serializedObject = new SerializedObject(attack);
            serializedObject.FindProperty("attackEnabled").boolValue = false;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(attack.CanStartAttack, Is.False);
            Assert.That(attack.AttackStartRange, Is.EqualTo(0f));
            Assert.That(attack.TryAttack(playerHealth, Vector3.forward), Is.False);
            Assert.That(attack.AttackSequence, Is.EqualTo(0));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyBrain_LastSeenSearch_DoesNotTreatCloseAwarenessAsVision()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject player = new GameObject("Player");
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);

        enemy.AddComponent<EnemyVision>();
        enemy.AddComponent<EnemyPerception>();
        EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
        player.AddComponent<CapsuleCollider>();
        player.AddComponent<PlayerHealth>();

        try
        {
            enemy.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            player.transform.position = new Vector3(0f, 0f, 5f);
            obstacle.transform.position = new Vector3(0f, 1f, 2.5f);
            obstacle.transform.localScale = new Vector3(2f, 2f, 0.25f);

            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("closeAwarenessRadius").floatValue = 8f;
            settings.FindProperty("closeAwarenessRequiresLineOfSight").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();

            Assert.That(brain.TryRefreshCloseAwarenessTarget(), Is.True);
            Vector3 lastKnown = brain.LastKnownTargetPosition;
            Assert.That(brain.TryRefreshPursuitTarget(), Is.False);
            Assert.That(brain.LastKnownTargetPosition, Is.EqualTo(lastKnown));

            settings.Update();
            settings.FindProperty("useLastSeenSearchFlow").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(brain.TryRefreshPursuitTarget(), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(obstacle);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyBrain_LastSeenSearch_SwitchesToClearlyCloserVisiblePlayer()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject firstPlayer = new GameObject("First Player");
        GameObject secondPlayer = null;

        enemy.AddComponent<EnemyVision>();
        enemy.AddComponent<EnemyPerception>();
        EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
        firstPlayer.AddComponent<CapsuleCollider>();
        PlayerHealth firstHealth = firstPlayer.AddComponent<PlayerHealth>();

        try
        {
            firstPlayer.transform.position = new Vector3(0f, 0f, 8f);
            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("closeAwarenessRadius").floatValue = 10f;
            settings.FindProperty("closeAwarenessRequiresLineOfSight").boolValue = false;
            settings.FindProperty("targetSwitchDistanceAdvantage").floatValue = 2f;
            settings.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(brain.TryRefreshCloseAwarenessTarget(), Is.True);
            Assert.That(brain.CurrentTarget, Is.EqualTo(firstHealth));

            secondPlayer = new GameObject("Second Player");
            secondPlayer.AddComponent<CapsuleCollider>();
            PlayerHealth secondHealth = secondPlayer.AddComponent<PlayerHealth>();
            secondPlayer.transform.position = new Vector3(1f, 0f, 3f);
            Physics.SyncTransforms();

            Assert.That(brain.TryRefreshVisibleTarget(), Is.True);
            Assert.That(brain.CurrentTarget, Is.EqualTo(secondHealth));
        }
        finally
        {
            if (secondPlayer != null)
                Object.DestroyImmediate(secondPlayer);
            Object.DestroyImmediate(firstPlayer);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemySearch_ReacquiringVisiblePlayerTriggersAlertDespiteCombatMemory()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject player = new GameObject("Player");

        enemy.AddComponent<EnemyVision>();
        enemy.AddComponent<EnemyPerception>();
        EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
        player.AddComponent<CapsuleCollider>();
        player.AddComponent<PlayerHealth>();

        try
        {
            player.transform.position = new Vector3(0f, 0f, 5f);
            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            brain.RegisterCombatAwareness(player.transform.position);
            brain.ChangeState(EnemyState.Search);
            Physics.SyncTransforms();

            new EnemySearchState(brain).Tick(0.02f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Alert));
            Assert.That(brain.ActiveAlertSequence, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyBrain_ContactAlertsOnceUntilReflectionCompletes()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject player = new GameObject("Player");
        EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
        PlayerHealth playerHealth = player.AddComponent<PlayerHealth>();

        try
        {
            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();

            brain.ReportPlayerContact(playerHealth);
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Alert));
            Assert.That(brain.ActiveAlertSequence, Is.EqualTo(1));
            Assert.That(brain.CanTriggerAlert, Is.False);

            brain.ReportPlayerContact(playerHealth);
            Assert.That(brain.ActiveAlertSequence, Is.EqualTo(1));

            brain.ChangeState(EnemyState.Search);
            brain.ReportPlayerContact(playerHealth);
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Chase));
            Assert.That(brain.ActiveAlertSequence, Is.EqualTo(1));

            brain.ChangeState(EnemyState.Reflection);
            Assert.That(brain.CanTriggerAlert, Is.False);
            brain.NotifyReflectionCompleted();
            brain.ChangeState(EnemyState.Idle);
            brain.ReportPlayerContact(playerHealth);
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Alert));
            Assert.That(brain.ActiveAlertSequence, Is.EqualTo(2));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemySearch_MovementNoiseDoesNotRestartAlertCycle()
    {
        GameObject enemy = new GameObject("Enemy");
        enemy.AddComponent<EnemyHearing>();
        enemy.AddComponent<EnemyPerception>();
        EnemyBrain brain = enemy.AddComponent<EnemyBrain>();

        try
        {
            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();

            brain.BeginInvestigationAlert(Vector3.right);
            brain.ChangeState(EnemyState.Alert);
            brain.SetLastKnownTargetPosition(Vector3.right);
            brain.ChangeState(EnemyState.Search);
            NoiseSystem.EmitNoise(Vector3.right, 2f, 1f, NoiseType.Footstep);

            new EnemySearchState(brain).Tick(0.02f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Search));
            Assert.That(brain.ActiveAlertSequence, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyAlert_SeeingPlayerAfterHearingKeepsSingleAlert()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject player = new GameObject("Player");
        enemy.AddComponent<EnemyVision>();
        enemy.AddComponent<EnemyPerception>();
        EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
        player.AddComponent<CapsuleCollider>();
        player.AddComponent<PlayerHealth>();

        try
        {
            player.transform.position = new Vector3(0f, 0f, 5f);
            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();

            brain.BeginInvestigationAlert(player.transform.position);
            brain.ChangeState(EnemyState.Alert);
            Physics.SyncTransforms();

            EnemyAlertState alert = new EnemyAlertState(brain);
            alert.Enter();
            alert.Tick(0.02f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Alert));
            Assert.That(brain.HasActiveTargetAlert, Is.True);
            Assert.That(brain.ActiveAlertSequence, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemySearch_ReacquiringVisiblePlayerAfterAlertChasesWithoutAnotherAlert()
    {
        GameObject enemy = new GameObject("Enemy");
        GameObject player = new GameObject("Player");
        enemy.AddComponent<EnemyVision>();
        enemy.AddComponent<EnemyPerception>();
        EnemyBrain brain = enemy.AddComponent<EnemyBrain>();
        player.AddComponent<CapsuleCollider>();
        player.AddComponent<PlayerHealth>();

        try
        {
            player.transform.position = new Vector3(0f, 0f, 5f);
            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("requireSpawnAnimation").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();

            brain.BeginInvestigationAlert(player.transform.position);
            brain.ChangeState(EnemyState.Alert);
            brain.SetLastKnownTargetPosition(player.transform.position);
            brain.ChangeState(EnemyState.Search);
            Physics.SyncTransforms();

            new EnemySearchState(brain).Tick(0.02f);

            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Chase));
            Assert.That(brain.ActiveAlertSequence, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemySearch_StalledBeforeLastSeenPointGoesDirectlyToReflection()
    {
        GameObject enemy = new GameObject("Enemy");
        EnemyBrain brain = enemy.AddComponent<EnemyBrain>();

        try
        {
            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("lastSeenStallTimeout").floatValue = 3f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            brain.SetLastKnownTargetPosition(new Vector3(20f, 0f, 0f));

            brain.ChangeState(EnemyState.Search);
            EnemySearchState search = new EnemySearchState(brain);
            search.Enter();

            search.Tick(2f);
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Search));
            search.Tick(1f);
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Reflection));
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemySearch_LongLastSeenRouteDoesNotTimeoutWhileMakingProgress()
    {
        GameObject enemy = new GameObject("Enemy");
        enemy.AddComponent<EnemyNavigation>();
        EnemyBrain brain = enemy.AddComponent<EnemyBrain>();

        try
        {
            SerializedObject settings = new SerializedObject(brain);
            settings.FindProperty("useLastSeenSearchFlow").boolValue = true;
            settings.FindProperty("lastSeenStallTimeout").floatValue = 3f;
            settings.FindProperty("lastSeenProgressDistance").floatValue = 0.5f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            brain.SetLastKnownTargetPosition(new Vector3(100f, 0f, 0f));

            brain.ChangeState(EnemyState.Search);
            EnemySearchState search = new EnemySearchState(brain);
            search.Enter();

            for (int i = 0; i < 10; i++)
            {
                enemy.transform.position += Vector3.right * 0.6f;
                search.Tick(1f);
                Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Search));
            }

            search.Tick(2f);
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Search));
            search.Tick(1f);
            Assert.That(brain.CurrentState, Is.EqualTo(EnemyState.Reflection));
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void EnemyMotor_DesiredVelocityBelowStopDistanceStillMoves()
    {
        GameObject enemy = new GameObject("Enemy");
        EnemyMotor motor = enemy.AddComponent<EnemyMotor>();

        try
        {
            SerializedObject settings = new SerializedObject(motor);
            settings.FindProperty("moveSpeed").floatValue = 4.1f;
            settings.FindProperty("stopDistance").floatValue = 1.75f;
            settings.ApplyModifiedPropertiesWithoutUndo();

            motor.MoveInDirection(new Vector3(0.5f, 0f, 0f), 1f);
            Assert.That(motor.CurrentPlanarSpeed, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(enemy.transform.position.x, Is.EqualTo(0.5f).Within(0.001f));

            motor.MoveTowards(Vector3.right, 1f);
            Assert.That(motor.CurrentPlanarSpeed, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }
}
