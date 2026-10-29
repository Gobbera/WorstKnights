using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Photon.Pun;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class StatuePuzzleTests
{
    private readonly List<Object> objects = new List<Object>();
    private StatuePuzzleController puzzle;
    private StatuePuzzleSlot[] slots;
    private StatuePuzzlePlate[] plates;
    private int solved;
    private int failed;

    [SetUp]
    public void SetUp()
    {
        puzzle = Create("Puzzle").AddComponent<StatuePuzzleController>();
        slots = new StatuePuzzleSlot[4];
        plates = new StatuePuzzlePlate[4];
        for (int i = 0; i < 4; i++)
        {
            GameObject baseObject = Create("Base" + i);
            Transform pivot = new GameObject("Plate_Pivot").transform;
            pivot.SetParent(baseObject.transform, false);
            pivot.localPosition = new Vector3(i, 1, 2);
            pivot.localRotation = Quaternion.Euler(0, 45 * i, 0);
            slots[i] = baseObject.AddComponent<StatuePuzzleSlot>();
            Set(slots[i], "expectedIdentity", (StatueIdentity)i);
            Set(slots[i], "platePivot", pivot);
            Set(slots[i], "puzzle", puzzle);

            GameObject plateObject = Create("Plate" + i);
            plateObject.AddComponent<BoxCollider>();
            plateObject.AddComponent<Rigidbody>();
            // Awake validates the definition, so configure while the object is inactive.
            plateObject.SetActive(false);
            WorldPickupItem pickup = plateObject.AddComponent<WorldPickupItem>();
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
            objects.Add(item);
            pickup.SetItemDefinition(item);
            plateObject.SetActive(true);
            plates[i] = plateObject.AddComponent<StatuePuzzlePlate>();
            Set(plates[i], "identity", (StatueIdentity)i);
        }
        Set(puzzle, "slots", slots);
        puzzle.OnPuzzleSolved.AddListener(() => solved++);
        puzzle.OnPuzzleFailed.AddListener(() => failed++);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null)
                Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void EmptyOrIncompleteArrangement_FailsOnlyWhenSubmitted()
    {
        Assert.That(slots[0].TryPlacePlate(plates[0]), Is.True);
        Assert.That(failed, Is.Zero);
        puzzle.CheckSolution();
        Assert.That(failed, Is.EqualTo(1));
        Assert.That(solved, Is.Zero);
        Assert.That(puzzle.IsSolved, Is.False);
    }

    [Test]
    public void SwappedPlates_AreAcceptedButFailValidation()
    {
        for (int i = 0; i < 4; i++)
            Assert.That(slots[i].TryPlacePlate(plates[(i + 1) % 4]), Is.True);
        Assert.That(failed, Is.Zero);
        puzzle.CheckSolution();
        Assert.That(failed, Is.EqualTo(1));
        Assert.That(puzzle.IsSolved, Is.False);
    }

    [Test]
    public void CorrectArrangement_SolvesOnceAndLocksPlates()
    {
        for (int i = 0; i < 4; i++)
            slots[i].TryPlacePlate(plates[i]);
        Assert.That(solved, Is.Zero);
        puzzle.CheckSolution();
        puzzle.CheckSolution();
        Assert.That(solved, Is.EqualTo(1));
        Assert.That(failed, Is.Zero);
        Assert.That(puzzle.IsSolved, Is.True);
        foreach (StatuePuzzlePlate plate in plates)
            Assert.That(plate.Pickup.CanBePickedUp, Is.False);
    }

    [Test]
    public void Placement_SnapsPreservesScaleAndFreezesPhysics()
    {
        plates[0].transform.localScale = Vector3.one * 0.7f;
        slots[0].transform.localScale = new Vector3(2, 3, 4);
        Assert.That(slots[0].TryPlacePlate(plates[0]), Is.True);
        Assert.That(Vector3.Distance(plates[0].transform.position, slots[0].PlatePivot.position), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(plates[0].transform.rotation, slots[0].PlatePivot.rotation * plates[0].PlacementRotation), Is.LessThan(0.001f));
        Assert.That(Vector3.Distance(plates[0].transform.lossyScale, Vector3.one * 0.7f), Is.LessThan(0.001f));
        Assert.That(plates[0].GetComponent<Rigidbody>().isKinematic, Is.True);
        Assert.That(slots[0].TryPlacePlate(plates[1]), Is.False);
    }

    [Test]
    public void RemovingThenDroppingPlate_DoesNotReturnItToSlot()
    {
        slots[0].TryPlacePlate(plates[0]);
        Transform hand = Create("Hand").transform;
        Assert.That(plates[0].Pickup.TryEquipIntoHand(hand, HandType.Right), Is.True);
        Assert.That(slots[0].CurrentPlate, Is.Null);
        plates[0].Pickup.DropToWorld(new Vector3(10, 2, 0), Quaternion.identity);
        Assert.That(plates[0].transform.parent, Is.Null);
        Assert.That(plates[0].GetComponent<Rigidbody>().isKinematic, Is.False);
        Assert.That(slots[0].CurrentPlate, Is.Null);
    }

    [Test]
    public void DuplicateSlotConfiguration_DoesNotSolve()
    {
        for (int i = 0; i < 4; i++)
            slots[i].TryPlacePlate(plates[i]);
        Set(puzzle, "slots", new[] { slots[0], slots[0], slots[2], slots[3] });
        puzzle.CheckSolution();
        Assert.That(failed, Is.EqualTo(1));
        Assert.That(puzzle.IsSolved, Is.False);
    }

    [Test]
    public void FailedAttempt_CanBeCorrectedAndSubmittedAgain()
    {
        slots[0].TryPlacePlate(plates[1]);
        puzzle.CheckSolution();
        plates[1].Pickup.DropToWorld(Vector3.zero, Quaternion.identity);
        for (int i = 0; i < 4; i++)
            Assert.That(slots[i].TryPlacePlate(plates[i]), Is.True);
        puzzle.CheckSolution();
        Assert.That(failed, Is.EqualTo(1));
        Assert.That(solved, Is.EqualTo(1));
    }

    [Test]
    public void InventoryPlacement_ReleasesOnlySelectedHandAndKeepsOriginalPickup()
    {
        var player = Create("Equipment");
        player.SetActive(false);
        var equipment = player.AddComponent<HandEquipmentController>();
        Transform right = Create("RightHandSocket").transform;
        right.SetParent(player.transform);
        Transform left = Create("LeftHandSocket").transform;
        left.SetParent(player.transform);
        Set(equipment, "rightHandSocket", right);
        Set(equipment, "leftHandSocket", left);
        Set(equipment, "createFirstPersonItemVisuals", false);
        player.SetActive(true);
        typeof(PhotonView).GetProperty("IsMine").SetValue(player.GetComponent<PhotonView>(), true);
        Assert.That(equipment.TryEquipWorldItem(plates[0].Pickup), Is.True);
        HandType hand = plates[0].Pickup.EquippedHand;
        Assert.That(equipment.TryEquipWorldItem(plates[1].Pickup), Is.True);
        HandType otherHand = plates[1].Pickup.EquippedHand;
        Assert.That(otherHand, Is.Not.EqualTo(hand));
        Assert.That(equipment.TryPlaceActiveStatuePlate(hand, slots[0]), Is.True);
        Assert.That(equipment.GetActivePickup(hand), Is.Null);
        Assert.That(equipment.GetActivePickup(otherHand), Is.SameAs(plates[1].Pickup));
        Assert.That(slots[0].CurrentPlate, Is.SameAs(plates[0]));
        Assert.That(plates[0].Pickup.IsEquipped, Is.False);
        Assert.That(equipment.TryEquipWorldItem(plates[0].Pickup), Is.True);
        Assert.That(slots[0].CurrentPlate, Is.Null);
    }

    [Test]
    public void AllPlatePrefabs_HaveExpectedIdentities()
    {
        string[] names = { "Plate Mothen Hedar", "Plate Balthor Sabrath", "Plate Angrad Veyr", "Plate Eldric Ferden" };
        for (int i = 0; i < 4; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Items/" + names[i] + ".prefab");
            Assert.That(prefab.GetComponent<StatuePuzzlePlate>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<StatuePuzzlePlate>().Identity, Is.EqualTo((StatueIdentity)i));
        }
    }

    [Test]
    public void All24Permutations_OnlyTheMatchingArrangementIsCorrect()
    {
        int correct = 0;
        int combinations = 0;
        for (int a = 0; a < 4; a++)
        for (int b = 0; b < 4; b++)
        for (int c = 0; c < 4; c++)
        for (int d = 0; d < 4; d++)
        {
            var order = new[] { a, b, c, d };
            if (new HashSet<int>(order).Count != 4)
                continue;
            foreach (StatuePuzzlePlate plate in plates)
                plate.Pickup.DropToWorld(Vector3.zero, Quaternion.identity);
            for (int i = 0; i < 4; i++)
                Assert.That(slots[i].TryPlacePlate(plates[order[i]]), Is.True);
            if (puzzle.HasCorrectArrangement())
                correct++;
            combinations++;
        }
        Assert.That(combinations, Is.EqualTo(24));
        Assert.That(correct, Is.EqualTo(1));
    }

    [Test]
    public void ActualBase_AllFourPlatesFollowSurfaceWithoutClipping()
    {
        var baseObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/WK_Pack/Snaps/Objects/Statue_Base.prefab"));
        objects.Add(baseObject);
        baseObject.transform.SetPositionAndRotation(new Vector3(20, 0, 0), Quaternion.Euler(0, 90, 0));
        StatuePuzzleSlot slot = baseObject.AddComponent<StatuePuzzleSlot>();
        StatuePuzzleSetupUtility.FitSlotToSurface(slot);
        Transform pivot = slot.PlatePivot;
        Assert.That(baseObject.GetComponent<Collider>().Raycast(
            new Ray(pivot.position + pivot.right, -pivot.right), out RaycastHit surface, 2f), Is.True);
        string[] names = { "Plate Mothen Hedar", "Plate Balthor Sabrath", "Plate Angrad Veyr", "Plate Eldric Ferden" };
        foreach (string name in names)
        {
            var plateObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Items/" + name + ".prefab"));
            objects.Add(plateObject);
            var plate = plateObject.GetComponent<StatuePuzzlePlate>();
            Assert.That(slot.TryPlacePlate(plate), Is.True);
            Assert.That(Vector3.Dot(plate.transform.up, surface.normal), Is.GreaterThan(0.999f));
            Assert.That(Vector3.Dot(plate.transform.forward, Vector3.up), Is.GreaterThan(0.9f));
            foreach (MeshFilter mesh in plateObject.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                    Assert.That(Vector3.Dot(mesh.transform.TransformPoint(vertex) - surface.point, surface.normal),
                        Is.GreaterThan(0.0005f), name + " intersects the base.");
            plate.Pickup.DropToWorld(Vector3.zero, Quaternion.identity);
        }
    }

    private GameObject Create(string name)
    {
        var result = new GameObject(name);
        objects.Add(result);
        return result;
    }

    [Test]
    public void TestFeedback_FailedAttemptDamagesOnlyTheSubmittingPlayerBy50()
    {
        var feedback = puzzle.gameObject.AddComponent<StatuePuzzleTestFeedback>();
        puzzle.OnPuzzleFailed.AddListener(feedback.PrintFailedAndDamage);
        PlayerHealth submitter = CreateTestPlayer("Submitter");
        PlayerHealth bystander = CreateTestPlayer("Bystander");
        var equipment = submitter.gameObject.AddComponent<HandEquipmentController>();
        typeof(PhotonView).GetProperty("IsMine").SetValue(submitter.GetComponent<PhotonView>(), true);
        var logs = new List<string>();
        Application.LogCallback capture = (message, trace, type) => logs.Add(message);
        Application.logMessageReceived += capture;
        try
        {
            Assert.That(equipment.TryRequestStatuePuzzleCheck(puzzle), Is.True);
        }
        finally
        {
            Application.logMessageReceived -= capture;
        }
        Assert.That(puzzle.SubmittingPlayer, Is.SameAs(submitter));
        Assert.That(submitter.CurrentHealth, Is.EqualTo(50f));
        Assert.That(bystander.CurrentHealth, Is.EqualTo(100f));
        Assert.That(logs, Does.Contain("Falhou!"));
    }

    [Test]
    public void TestFeedback_SolvedAttemptPrintsSuccessWithoutDamagingPlayer()
    {
        var feedback = puzzle.gameObject.AddComponent<StatuePuzzleTestFeedback>();
        puzzle.OnPuzzleSolved.AddListener(feedback.PrintSolved);
        puzzle.OnPuzzleFailed.AddListener(feedback.PrintFailedAndDamage);
        PlayerHealth submitter = CreateTestPlayer("Submitter");
        var equipment = submitter.gameObject.AddComponent<HandEquipmentController>();
        typeof(PhotonView).GetProperty("IsMine").SetValue(submitter.GetComponent<PhotonView>(), true);
        for (int i = 0; i < 4; i++)
            slots[i].TryPlacePlate(plates[i]);
        var logs = new List<string>();
        Application.LogCallback capture = (message, trace, type) => logs.Add(message);
        Application.logMessageReceived += capture;
        try
        {
            Assert.That(equipment.TryRequestStatuePuzzleCheck(puzzle), Is.True);
        }
        finally
        {
            Application.logMessageReceived -= capture;
        }
        Assert.That(submitter.CurrentHealth, Is.EqualTo(100f));
        Assert.That(logs, Does.Contain("Acertou!"));
        Assert.That(logs, Does.Not.Contain("Falhou!"));
    }

    private PlayerHealth CreateTestPlayer(string name)
    {
        var player = Create(name).AddComponent<PlayerHealth>();
        typeof(PlayerHealth).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
        player.GetComponent<Rigidbody>().isKinematic = false;
        return player;
    }

    private static void Set(object instance, string name, object value)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Missing serialized field: " + name);
        field.SetValue(instance, value);
    }
}
