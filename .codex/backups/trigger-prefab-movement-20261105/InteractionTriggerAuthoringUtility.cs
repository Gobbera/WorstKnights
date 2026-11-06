using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class InteractionTriggerAuthoringUtility
{
    public const string Root = "Assets/Prefabs/Mechanisms/Triggers/";
    public const string LeverPath = Root + "Levers/Lever.prefab";
    public const string PressurePath = Root + "Pressure Buttons/Pressure_Button (1).prefab";
    public const string ButtonPath = Root + "Buttons/Button.prefab";
    public static string Category(InteractionTriggerType type) => type == InteractionTriggerType.Lever ? "Levers" : type == InteractionTriggerType.Button ? "Buttons" : "Pressure Buttons";
    public static GameObject[] FindPrefabs(InteractionTriggerType type)
    {
        string folder = Root + Category(type);
        if (!AssetDatabase.IsValidFolder(folder)) return Array.Empty<GameObject>();
        return AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(p => p != null && p.TryGetComponent<InteractionTriggerMechanism>(out var m) && m.Type == type).OrderBy(p => p.name).ToArray();
    }
    public static void BuildMechanism(InteractionTriggerSystem system)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Monte o prefab fora do Play Mode.");
        var data = new SerializedObject(system);
        var prefab = data.FindProperty("mechanismPrefab").objectReferenceValue as GameObject;
        if (prefab == null || !FindPrefabs(system.TriggerType).Contains(prefab)) throw new InvalidOperationException("Prefab fora da categoria selecionada ou sem InteractionTriggerMechanism do tipo correto.");
        var previous = system.Mechanism;
        if (previous != null && PrefabUtility.GetCorrespondingObjectFromSource(previous.gameObject) == prefab) return;
        if (previous != null && !previous.transform.IsChildOf(system.transform)) throw new InvalidOperationException("O mecanismo deve ser filho do Interaction Trigger System.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply interaction trigger prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, system.transform);
        Undo.RegisterCreatedObjectUndo(instance, "Create trigger mechanism");
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = prefab.transform.localScale;
        if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);
        data.Update();
        data.FindProperty("mechanism").objectReferenceValue = instance.GetComponent<InteractionTriggerMechanism>();
        data.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(system);
        EditorSceneManager.MarkSceneDirty(system.gameObject.scene);
        Undo.CollapseUndoOperations(group);
    }
    [MenuItem("GameObject/Mechanisms/Interaction Trigger System", false, 10)]
    public static void Create(MenuCommand command)
    {
        var root = new GameObject("Interaction Trigger System");
        Undo.RegisterCreatedObjectUndo(root, "Create Interaction Trigger System");
        GameObjectUtility.SetParentAndAlign(root, command.context as GameObject);
        var system = Undo.AddComponent<InteractionTriggerSystem>(root);
        Set(system, "mechanismPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(LeverPath));
        if (AssetDatabase.LoadAssetAtPath<GameObject>(LeverPath)?.GetComponent<InteractionTriggerMechanism>() != null) BuildMechanism(system);
        Selection.activeGameObject = root;
    }
    [MenuItem("Tools/Mechanisms/Prepare Interaction Trigger Prefabs")]
    public static void PreparePrefabs()
    {
        PrepareExisting(LeverPath, InteractionTriggerType.Lever);
        PrepareExisting(PressurePath, InteractionTriggerType.PressureButton);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath) == null) CreateButtonPrefab();
        else PrepareExisting(ButtonPath, InteractionTriggerType.Button);
    }
    private static void PrepareExisting(string path, InteractionTriggerType type)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var mechanism = root.GetComponent<InteractionTriggerMechanism>();
            if (mechanism == null) mechanism = root.AddComponent<InteractionTriggerMechanism>();
            var source = root.GetComponent<AudioSource>(); source.playOnAwake = false; source.loop = false; source.dopplerLevel = 0f; source.spatialBlend = 1f;
            Set(mechanism, "mechanismType", (int)type);
            Set(mechanism, "audioSource", source);
            if (type == InteractionTriggerType.Lever)
            {
                Transform arm = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Lever_Arm");
                Set(mechanism, "movingPart", arm);
                FitCollider(root, root.GetComponentsInChildren<MeshFilter>().Single(m => m.name == "Lever" && m.transform != root.transform));
                FitCollider(arm.gameObject, arm.GetComponent<MeshFilter>());
                PrepareBody(arm.gameObject);
            }
            else if (type == InteractionTriggerType.PressureButton)
            {
                var collider = FitCollider(root, root.GetComponent<MeshFilter>());
                PrepareBody(root);
                Set(mechanism, "movingPart", root.transform);
                Set(mechanism, "pressureSurface", collider);
            }
            if (type != InteractionTriggerType.PressureButton && root.GetComponent<InteractionTriggerInteractable>() == null) root.AddComponent<InteractionTriggerInteractable>();
            RemoveLegacyPressure(root, mechanism);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static void CreateButtonPrefab()
    {
        var root = new GameObject("Button");
        try
        {
            var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseObject.name = "Base"; baseObject.transform.SetParent(root.transform, false);
            baseObject.transform.localPosition = Vector3.up * 0.04f;
            baseObject.transform.localScale = new Vector3(0.34f, 0.08f, 0.34f);
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.name = "Button Cap"; cap.transform.SetParent(root.transform, false);
            cap.transform.localPosition = Vector3.up * 0.11f;
            cap.transform.localScale = new Vector3(0.25f, 0.03f, 0.25f);
            Object.DestroyImmediate(cap.GetComponent<Collider>());
            cap.AddComponent<BoxCollider>(); PrepareBody(cap);
            var mechanism = root.AddComponent<InteractionTriggerMechanism>();
            Set(mechanism, "mechanismType", (int)InteractionTriggerType.Button);
            Set(mechanism, "movingPart", cap.transform);
            var source = root.GetComponent<AudioSource>(); source.playOnAwake = false; source.dopplerLevel = 0; source.spatialBlend = 1;
            Set(mechanism, "audioSource", source);
            root.AddComponent<InteractionTriggerInteractable>();
            var lever = AssetDatabase.LoadAssetAtPath<GameObject>(LeverPath);
            var materials = lever.GetComponentsInChildren<MeshRenderer>().Select(r => r.sharedMaterial).ToArray();
            baseObject.GetComponent<MeshRenderer>().sharedMaterial = materials[0];
            cap.GetComponent<MeshRenderer>().sharedMaterial = materials[materials.Length - 1];
            PrefabUtility.SaveAsPrefabAsset(root, ButtonPath);
        }
        finally { Object.DestroyImmediate(root); }
    }
    private static BoxCollider FitCollider(GameObject target, MeshFilter mesh)
    {
        var collider = target.GetComponent<BoxCollider>();
        if (collider == null) collider = target.AddComponent<BoxCollider>();
        Matrix4x4 matrix = target.transform.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
        var bounds = mesh.sharedMesh.bounds;
        var fitted = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2) fitted.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))));
        collider.center = fitted.center; collider.size = fitted.size; collider.isTrigger = false;
        return collider;
    }
    private static void PrepareBody(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>(); if (body == null) body = root.AddComponent<Rigidbody>();
        body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.Interpolate;
    }
    public static void RemoveLegacyPressure(GameObject root, InteractionTriggerMechanism replacement = null)
    {
        foreach (var legacy in root.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null && c.GetType().Name == "PressureButtonController").ToArray())
        {
            if (replacement != null)
            {
                var old = new SerializedObject(legacy).FindProperty("pressAudioCue");
                if (old != null && old.objectReferenceValue != null) Set(replacement, "activationAudioCue", old.objectReferenceValue);
            }
            Object.DestroyImmediate(legacy);
        }
    }
    public static void Set(Object target, string field, Object value)
    {
        var data = new SerializedObject(target); data.FindProperty(field).objectReferenceValue = value; data.ApplyModifiedPropertiesWithoutUndo();
    }
    public static void Set(Object target, string field, int value)
    {
        var data = new SerializedObject(target); data.FindProperty(field).intValue = value; data.ApplyModifiedPropertiesWithoutUndo();
    }
}
