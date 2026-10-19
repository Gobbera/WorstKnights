using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class LootSpawnerTests
{
    private GameObject firstObject;
    private GameObject secondObject;

    [SetUp]
    public void SetUp()
    {
        firstObject = new GameObject("First loot option");
        secondObject = new GameObject("Second loot option");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(firstObject);
        Object.DestroyImmediate(secondObject);
    }

    [Test]
    public void SelectOptionIndex_UsesRemainingPercentageForEmptySlot()
    {
        List<LootSpawner.LootOption> options = CreateOptions(30f, 20f);

        Assert.That(LootSpawner.SelectOptionIndex(options, 0.1f), Is.EqualTo(0));
        Assert.That(LootSpawner.SelectOptionIndex(options, 0.35f), Is.EqualTo(1));
        Assert.That(LootSpawner.SelectOptionIndex(options, 0.9f), Is.EqualTo(-1));
    }

    [Test]
    public void SelectOptionIndex_AlwaysSelectsSingleCertainOption()
    {
        List<LootSpawner.LootOption> options = CreateOptions(100f, 0f);

        Assert.That(LootSpawner.SelectOptionIndex(options, 0f), Is.EqualTo(0));
        Assert.That(LootSpawner.SelectOptionIndex(options, 1f), Is.EqualTo(0));
    }

    [Test]
    public void SelectOptionIndex_NormalizesOptionsThatExceedOneHundredPercent()
    {
        List<LootSpawner.LootOption> options = CreateOptions(80f, 80f);

        Assert.That(LootSpawner.SelectOptionIndex(options, 0.25f), Is.EqualTo(0));
        Assert.That(LootSpawner.SelectOptionIndex(options, 0.75f), Is.EqualTo(1));
    }

    [Test]
    public void SelectOptionIndex_EmptyListSelectsNothing()
    {
        Assert.That(LootSpawner.SelectOptionIndex(new List<LootSpawner.LootOption>(), 0.5f), Is.EqualTo(-1));
    }

    [Test]
    public void PickupAvailability_CanBeChangedWithoutEquipping()
    {
        WorldPickupItem item = firstObject.AddComponent<WorldPickupItem>();

        item.SetPickupAvailable(false);
        Assert.That(item.CanBePickedUp, Is.False);
        item.SetPickupAvailable(true);
        Assert.That(item.CanBePickedUp, Is.True);
    }

    [Test]
    public void ChestPrefab_HasOneGatedGroupAndPreservesConfiguredChance()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WK_Pack/Snaps/Objects/Chest.prefab");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<LootSpawner>(true).Length, Is.EqualTo(1));
        LootSpawner spawner = FindSpawner(prefab, "Pivot_Asset");
        SerializedProperty groups = GetGroups(spawner);
        Assert.That(groups.arraySize, Is.EqualTo(1));
        SerializedProperty group = groups.GetArrayElementAtIndex(0);
        Assert.That(group.FindPropertyRelative("door").objectReferenceValue, Is.Not.Null);
        SerializedProperty slots = group.FindPropertyRelative("slots");
        Assert.That(slots.arraySize, Is.EqualTo(1));
        SerializedProperty slot = slots.GetArrayElementAtIndex(0);
        Assert.That(slot.FindPropertyRelative("spawnPoint").objectReferenceValue, Is.Null);
        SerializedProperty option = slot.FindPropertyRelative("options").GetArrayElementAtIndex(0);
        Assert.That(option.FindPropertyRelative("chancePercent").floatValue, Is.EqualTo(50f));
    }

    [Test]
    public void CabinetPrefab_UsesDifferentDoorsForItsTwoLootGroups()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WK_Pack/Snaps/Objects/Chest_Drawers_01.prefab");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<LootSpawner>(true).Length, Is.EqualTo(2));
        SerializedProperty firstGroups = GetGroups(FindSpawner(prefab, "Pivot_Asset_01"));
        SerializedProperty secondGroups = GetGroups(FindSpawner(prefab, "Pivot_Asset_02"));
        Assert.That(firstGroups.arraySize, Is.EqualTo(1));
        Assert.That(secondGroups.arraySize, Is.EqualTo(1));

        SerializedProperty firstGroup = firstGroups.GetArrayElementAtIndex(0);
        SerializedProperty secondGroup = secondGroups.GetArrayElementAtIndex(0);
        Object firstDoor = firstGroup.FindPropertyRelative("door").objectReferenceValue;
        Object secondDoor = secondGroup.FindPropertyRelative("door").objectReferenceValue;
        Assert.That(firstDoor, Is.Not.Null);
        Assert.That(secondDoor, Is.Not.Null);
        Assert.That(firstDoor, Is.Not.SameAs(secondDoor));
        Assert.That(firstGroup.FindPropertyRelative("slots").arraySize, Is.EqualTo(1));
        Assert.That(secondGroup.FindPropertyRelative("slots").arraySize, Is.EqualTo(1));
    }

    [Test]
    public void SlidingDrawerPrefab_SpawnsBothItemsAboveTheirMovingDrawerFloors()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WK_Pack/Snaps/Objects/Chest_Drawer_02.prefab");
        Assert.That(prefab, Is.Not.Null);
        LootSpawner[] spawners = prefab.GetComponentsInChildren<LootSpawner>(true);
        Assert.That(spawners.Length, Is.EqualTo(2));
        DoorController firstDoor = null;

        foreach (LootSpawner spawner in spawners)
        {
            SerializedProperty group = GetGroups(spawner).GetArrayElementAtIndex(0);
            DoorController door = group.FindPropertyRelative("door").objectReferenceValue as DoorController;
            Assert.That(door, Is.Not.Null);
            if (firstDoor == null)
                firstDoor = door;
            else
                Assert.That(door, Is.Not.SameAs(firstDoor));

            Transform movingPart = new SerializedObject(door).FindProperty("movingPart").objectReferenceValue as Transform;
            Assert.That(movingPart, Is.Not.Null);
            Assert.That(spawner.transform.parent, Is.SameAs(movingPart));

            SerializedProperty slot = group.FindPropertyRelative("slots").GetArrayElementAtIndex(0);
            Transform spawnPoint = slot.FindPropertyRelative("spawnPoint").objectReferenceValue as Transform;
            Assert.That(spawnPoint == null || spawnPoint == spawner.transform, Is.True);

            WorldPickupItem item = slot.FindPropertyRelative("options").GetArrayElementAtIndex(0)
                .FindPropertyRelative("prefab").objectReferenceValue as WorldPickupItem;
            Assert.That(item, Is.Not.Null);
            BoxCollider itemCollider = item.GetComponent<BoxCollider>();
            Assert.That(itemCollider, Is.Not.Null);

            BoxCollider floor = null;
            foreach (BoxCollider collider in movingPart.GetComponents<BoxCollider>())
            {
                if (collider.size.z > 0.5f)
                    floor = collider;
            }

            Assert.That(floor, Is.Not.Null);
            float itemBottom = spawner.transform.localPosition.y + itemCollider.center.y - itemCollider.size.y * 0.5f;
            float floorTop = floor.center.y + floor.size.y * 0.5f;
            Assert.That(itemBottom, Is.GreaterThan(floorTop), $"{door.name}: item abaixo do piso da gaveta.");
        }
    }

    [Test]
    public void TablePrefab_UsesUngatedPivotAboveItsSurface()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WK_Pack/Snaps/Objects/Table.prefab");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<LootSpawner>(true).Length, Is.EqualTo(1));
        LootSpawner spawner = FindSpawner(prefab, "Pivot_Asset");
        SerializedProperty group = GetGroups(spawner).GetArrayElementAtIndex(0);
        Assert.That(group.FindPropertyRelative("door").objectReferenceValue, Is.Null);
        Assert.That(group.FindPropertyRelative("slots").GetArrayElementAtIndex(0)
            .FindPropertyRelative("spawnPoint").objectReferenceValue, Is.Null);
    }

    [Test]
    public void StandalonePivotPrefab_HasAnUngatedEmptySlot()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Objetcs/Pivot_Asset.prefab");
        Assert.That(prefab, Is.Not.Null);
        SerializedProperty group = GetGroups(FindSpawner(prefab, "Pivot_Asset")).GetArrayElementAtIndex(0);
        Assert.That(group.FindPropertyRelative("door").objectReferenceValue, Is.Null);
        SerializedProperty slot = group.FindPropertyRelative("slots").GetArrayElementAtIndex(0);
        Assert.That(slot.FindPropertyRelative("spawnPoint").objectReferenceValue, Is.Null);
        Assert.That(slot.FindPropertyRelative("options").arraySize, Is.Zero);
    }

    private static LootSpawner FindSpawner(GameObject prefab, string pivotName)
    {
        foreach (LootSpawner spawner in prefab.GetComponentsInChildren<LootSpawner>(true))
        {
            if (spawner.name == pivotName)
                return spawner;
        }

        Assert.Fail($"LootSpawner ausente em {pivotName}.");
        return null;
    }

    private static SerializedProperty GetGroups(LootSpawner spawner)
    {
        SerializedProperty groups = new SerializedObject(spawner).FindProperty("groups");
        Assert.That(groups, Is.Not.Null);
        return groups;
    }

    private List<LootSpawner.LootOption> CreateOptions(float firstChance, float secondChance)
    {
        return new List<LootSpawner.LootOption>
        {
            new LootSpawner.LootOption
            {
                prefab = firstObject.AddComponent<WorldPickupItem>(),
                chancePercent = firstChance
            },
            new LootSpawner.LootOption
            {
                prefab = secondObject.AddComponent<WorldPickupItem>(),
                chancePercent = secondChance
            }
        };
    }
}
