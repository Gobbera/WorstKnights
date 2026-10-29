using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StatuePuzzleSetupUtility
{
    private static readonly string[] StatueNames = { "Statue_Axe", "Statue_Hammer", "Statue_Spear", "Statue_Sword" };
    private static readonly string[] PlateNames = { "Plate Mothen Hedar", "Plate Balthor Sabrath", "Plate Angrad Veyr", "Plate Eldric Ferden" };

    [MenuItem("Tools/Puzzles/Setup Statue Puzzle In Active Scene")]
    public static void SetupActiveScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Configure o puzzle fora do Play Mode.");

        Scene scene = SceneManager.GetActiveScene();
        Transform[] transforms = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var statues = StatueNames.Select(name => transforms.SingleOrDefault(t => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (statues.Any(t => t == null))
            throw new InvalidOperationException("A cena precisa conter as quatro estatuas: " + string.Join(", ", StatueNames));

        Transform[] bases = transforms.Where(t => t.name.StartsWith("Statue_Base", StringComparison.OrdinalIgnoreCase)
            && t.GetComponentsInChildren<Transform>(true).Any(child => child.name == "Plate_Pivot")).ToArray();
        if (bases.Length < 4)
            throw new InvalidOperationException("Nao foram encontradas quatro bases com Plate_Pivot.");

        // Validate the whole mapping before making any scene changes.
        var assigned = new HashSet<Transform>();
        var matchedBases = new Transform[4];
        for (int i = 0; i < statues.Length; i++)
        {
            Transform nearest = bases.OrderBy(t => HorizontalDistance(t.position, statues[i].position)).First();
            if (!assigned.Add(nearest) || HorizontalDistance(nearest.position, statues[i].position) > 4f)
                throw new InvalidOperationException("Associacao ambigua entre estatuas e bases; confira as posicoes na cena.");
            matchedBases[i] = nearest;
        }

        ConfigurePlatePrefabs();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup Statue Puzzle");

        StatuePuzzleController puzzle = transforms.Select(t => t.GetComponent<StatuePuzzleController>()).FirstOrDefault(p => p != null);
        if (puzzle == null)
        {
            var root = new GameObject("Statue Puzzle");
            Undo.RegisterCreatedObjectUndo(root, "Create Statue Puzzle");
            SceneManager.MoveGameObjectToScene(root, scene);
            puzzle = Undo.AddComponent<StatuePuzzleController>(root);
        }

        var slots = new StatuePuzzleSlot[4];
        for (int i = 0; i < slots.Length; i++)
        {
            Transform baseTransform = matchedBases[i];
            Transform pivot = baseTransform.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Plate_Pivot");
            StatuePuzzleSlot slot = baseTransform.GetComponent<StatuePuzzleSlot>();
            if (slot == null)
                slot = Undo.AddComponent<StatuePuzzleSlot>(baseTransform.gameObject);
            var data = new SerializedObject(slot);
            data.FindProperty("expectedIdentity").enumValueIndex = i;
            data.FindProperty("platePivot").objectReferenceValue = pivot;
            data.FindProperty("puzzle").objectReferenceValue = puzzle;
            data.ApplyModifiedProperties();
            FitSlotToSurface(slot);
            PrefabUtility.RecordPrefabInstancePropertyModifications(slot);
            slots[i] = slot;
            Debug.Log($"[StatuePuzzleSetup] {statues[i].name} -> {baseTransform.name} -> {(StatueIdentity)i}; pivot={pivot.position}", slot);
        }

        var puzzleData = new SerializedObject(puzzle);
        SerializedProperty slotList = puzzleData.FindProperty("slots");
        slotList.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; i++)
            slotList.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        puzzleData.ApplyModifiedProperties();

        StatuePuzzleSubmitInteractable submit = transforms.Select(t => t.GetComponent<StatuePuzzleSubmitInteractable>()).FirstOrDefault(s => s != null);
        if (submit == null)
        {
            GameObject button = new GameObject("Statue Puzzle Button");
            button.name = "Statue Puzzle Button";
            Undo.RegisterCreatedObjectUndo(button, "Create Statue Puzzle Button");
            button.transform.SetParent(puzzle.transform, false);
            Vector3 center = slots.Aggregate(Vector3.zero, (sum, slot) => sum + slot.PlatePivot.position) / 4f;
            Vector3 outward = slots.Aggregate(Vector3.zero, (sum, slot) => sum + slot.PlatePivot.right).normalized;
            button.transform.position = new Vector3(center.x, matchedBases[0].position.y, center.z) + outward * 2f;
            GameObject stand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stand.name = "Pedestal";
            stand.transform.SetParent(button.transform, false);
            stand.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            stand.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.name = "Button";
            top.transform.SetParent(button.transform, false);
            top.transform.localPosition = Vector3.up * 0.975f;
            top.transform.localScale = new Vector3(0.4f, 0.15f, 0.4f);
            submit = Undo.AddComponent<StatuePuzzleSubmitInteractable>(button);
        }
        var submitData = new SerializedObject(submit);
        submitData.FindProperty("puzzle").objectReferenceValue = puzzle;
        submitData.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = puzzle.gameObject;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }

    public static void FitSlotToSurface(StatuePuzzleSlot slot)
    {
        Transform pivot = slot.PlatePivot;
        if (pivot == null)
            return;

        Physics.SyncTransforms();
        Vector3 outward = pivot.right;
        Ray ray = new Ray(pivot.position + outward * 0.5f, -outward);
        foreach (Collider collider in slot.GetComponents<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || !collider.Raycast(ray, out RaycastHit hit, 1f))
                continue;
            Quaternion correction = Quaternion.Inverse(pivot.rotation)
                * Quaternion.FromToRotation(outward, hit.normal) * pivot.rotation;
            var data = new SerializedObject(slot);
            data.FindProperty("placementOffset").vector3Value = pivot.InverseTransformPoint(hit.point + hit.normal * 0.002f);
            data.FindProperty("surfaceEulerAngles").vector3Value = correction.eulerAngles;
            data.ApplyModifiedProperties();
            break;
        }
    }

    private static void ConfigurePlatePrefabs()
    {
        for (int i = 0; i < PlateNames.Length; i++)
        {
            string path = "Assets/Prefabs/Items/" + PlateNames[i] + ".prefab";
            GameObject prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (prefab.GetComponent<WorldPickupItem>() == null)
                    throw new InvalidOperationException(path + " precisa de WorldPickupItem.");
                StatuePuzzlePlate plate = prefab.GetComponent<StatuePuzzlePlate>();
                if (plate == null)
                    plate = prefab.AddComponent<StatuePuzzlePlate>();
                var data = new SerializedObject(plate);
                data.FindProperty("identity").enumValueIndex = i;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefab, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }
    }
}
