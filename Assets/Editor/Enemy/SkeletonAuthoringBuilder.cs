using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class SkeletonAuthoringBuilder
{
    public const string DirectoryPath = "Assets/WorstKnights/Models/Enemies/New Skeleton";
    public const string BodyPath = DirectoryPath + "/SkeletonTesteIA.fbx";
    public const string PrefabDirectoryPath = "Assets/Prefabs/Enemies/New Skeleton";
    public const string BasePath = PrefabDirectoryPath + "/Skeleton Base.prefab";
    public const string SwordsmanPath = PrefabDirectoryPath + "/Skeleton Swordsman.prefab";
    public const string ArcherPath = PrefabDirectoryPath + "/Skeleton Archer.prefab";
    public const string ProjectilePath = PrefabDirectoryPath + "/Skeleton Arrow.prefab";
    private const string OriginalController = "Assets/WorstKnights/Models/Enemies/Skeleton/SkeletonAnimController.controller";

    #pragma warning disable CS0649 // These DTO fields are populated by JsonUtility.
    [Serializable] internal sealed class ExportData
    {
        public float frameRate, frameEnd, releaseFrame, pickupFrame, arrowLength;
        public Bone[] bones;
        public Attachment[] attachments;
        public Clip[] clips;
        public Sample[] samples;
        public MaterialInfo[] materials;
    }
    [Serializable] internal sealed class Bone { public string name, parent; public float[] head, matrix; }
    [Serializable] internal sealed class Attachment { public string asset, bone; public float[] sourceMatrix; }
    [Serializable] internal sealed class Clip { public string name; public float start, end; }
    [Serializable] internal sealed class Sample { public float frame, bowDraw; public float[] arrowMatrix; }
    [Serializable] internal sealed class MaterialInfo { public string name; public float[] baseColor; public float metallic, roughness; }
    #pragma warning restore CS0649

    private static readonly Dictionary<string, Matrix4x4> GearCoordinates = new Dictionary<string, Matrix4x4>();
    private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();
    private static ExportData data;
    private static Matrix4x4 bodyCoordinates;
    private static float calibrationError;

    [MenuItem("Tools/Enemies/Skeleton/Build New Skeleton Variants")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building prefab assets.");
        data = JsonUtility.FromJson<ExportData>(File.ReadAllText(DirectoryPath + "/SkeletonExport.json"));
        foreach (string folder in new[] { "Animations", "Equipment/Prefabs", "Materials" })
            System.IO.Directory.CreateDirectory(DirectoryPath + "/" + folder);
        System.IO.Directory.CreateDirectory(PrefabDirectoryPath);
        AssetDatabase.Refresh();
        foreach (string name in new[] { "Skeleton Base", "Skeleton Swordsman", "Skeleton Archer", "Skeleton Arrow" })
        {
            string oldPath = DirectoryPath + "/Prefabs/" + name + ".prefab";
            string newPath = PrefabDirectoryPath + "/" + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(oldPath) != null && AssetDatabase.LoadAssetAtPath<GameObject>(newPath) == null)
            {
                string error = AssetDatabase.MoveAsset(oldPath, newPath);
                if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            }
        }
        ConfigureImports();
        ExtractClips();
        BuildGearPrefabs();
        BuildProjectile();
        BuildBase();
        BuildVariant(false);
        BuildVariant(true);
        foreach (string guid in AssetDatabase.FindAssets("", new[] { DirectoryPath }))
        {
            Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
        }
        File.WriteAllText(".codex/diagnostics/skeleton-build.txt",
            "BUILD_OK\nBody calibration error=" + calibrationError.ToString("G9")
            + "\nClips=" + string.Join(",", Clips.Keys)
            + "\nBase=" + BasePath + "\nSwordsman=" + SwordsmanPath + "\nArcher=" + ArcherPath
            + "\nProjectile=" + ProjectilePath + "\nCoordinates=" + bodyCoordinates);
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(ArcherPath);
    }

    private static void ConfigureImports()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(BodyPath);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.resampleCurves = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.preserveHierarchy = true;
        ModelImporterClipAnimation[] imported = importer.defaultClipAnimations;
        if (imported.Length != data.clips.Length)
            throw new InvalidOperationException("Expected eight body takes, found: " + string.Join(",", imported.Select(c => c.name)));
        foreach (ModelImporterClipAnimation clip in imported)
        {
            string name = ShortName(clip.name);
            if (!data.clips.Any(c => c.name == name)) throw new InvalidOperationException("Unexpected take: " + clip.name);
            clip.name = "SkeletonTesteIA|" + name;
            clip.loopTime = name == "Idle" || name == "Walk";
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            clip.keepOriginalOrientation = true;
        }
        importer.clipAnimations = imported;
        importer.SaveAndReimport();
        RemapMaterials(BodyPath);
        foreach (string name in new[] { "Sword", "Bow", "Quiver", "Arrow" })
        {
            string path = DirectoryPath + "/Equipment/" + name + ".fbx";
            var gearImporter = (ModelImporter)AssetImporter.GetAtPath(path);
            gearImporter.importAnimation = false;
            gearImporter.importBlendShapes = true;
            gearImporter.importCameras = false;
            gearImporter.importLights = false;
            gearImporter.preserveHierarchy = true;
            gearImporter.SaveAndReimport();
            RemapMaterials(path);
        }
    }

    private static void RemapMaterials(string modelPath)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        Material template = AssetDatabase.LoadAssetAtPath<Material>("Assets/WK_Pack/Materials/Base Colors/WK_Brown.mat");
        foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>())
        {
            Material material;
            if (embedded.name == "Bone")
                material = AssetDatabase.LoadAssetAtPath<Material>("Assets/WK_Pack/Materials/Exclusives/WK_Skeleton_Bones.mat");
            else
            {
                string path = DirectoryPath + "/Materials/" + embedded.name.Replace('/', '_') + ".mat";
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(template) { name = embedded.name };
                    AssetDatabase.CreateAsset(material, path);
                }
                MaterialInfo info = data.materials.FirstOrDefault(m => m.name == embedded.name);
                if (info != null)
                {
                    Color color = new Color(info.baseColor[0], info.baseColor[1], info.baseColor[2], info.baseColor[3]);
                    if (material.HasProperty("_Base_Color")) material.SetColor("_Base_Color", color);
                    else if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                    if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", info.metallic);
                }
                EditorUtility.SetDirty(material);
            }
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), material);
        }
        importer.SaveAndReimport();
    }

    private static void ExtractClips()
    {
        Clips.Clear();
        foreach (AnimationClip source in AssetDatabase.LoadAllAssetsAtPath(BodyPath).OfType<AnimationClip>())
        {
            if (source.name.StartsWith("__preview__")) continue;
            string name = ShortName(source.name);
            if (!data.clips.Any(c => c.name == name)) continue;
            string path = DirectoryPath + "/Animations/SkeletonTesteIA." + name + ".anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(source, clip);
            clip.name = "SkeletonTesteIA." + name;
            clip.hideFlags = HideFlags.None;
            clip.frameRate = data.frameRate;
            EditorUtility.SetDirty(clip);
            Clips.Add(name, clip);
        }
        if (Clips.Count != data.clips.Length) throw new InvalidOperationException("Missing imported body clips.");
    }

    private static void BuildGearPrefabs()
    {
        GearCoordinates.Clear();
        foreach (string name in new[] { "Sword", "Bow", "Quiver", "Arrow" })
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject wrapper = new GameObject(name);
                SceneManager.MoveGameObjectToScene(wrapper, preview);
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(DirectoryPath + "/Equipment/" + name + ".fbx");
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
                instance.transform.SetParent(wrapper.transform, false);
                Vector3 origin = Marker(wrapper.transform, "_CalibrationOrigin");
                Matrix4x4 matrix = Matrix4x4.identity;
                matrix.SetColumn(0, new Vector4((Marker(wrapper.transform, "_CalibrationX") - origin).x,
                    (Marker(wrapper.transform, "_CalibrationX") - origin).y, (Marker(wrapper.transform, "_CalibrationX") - origin).z, 0));
                matrix.SetColumn(1, Marker(wrapper.transform, "_CalibrationY") - origin);
                matrix.SetColumn(2, Marker(wrapper.transform, "_CalibrationZ") - origin);
                matrix.SetColumn(3, new Vector4(origin.x, origin.y, origin.z, 1));
                GearCoordinates[name] = matrix;
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (Transform marker in wrapper.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("_Calibration")).ToArray())
                    Object.DestroyImmediate(marker.gameObject);
                PrefabUtility.SaveAsPrefabAsset(wrapper, GearPath(name));
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }

    private static void BuildProjectile()
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject root = new GameObject("Skeleton Arrow");
            SceneManager.MoveGameObjectToScene(root, preview);
            var projectile = root.AddComponent<EnemyArrowProjectile>();
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GearPath("Arrow")), preview);
            visual.transform.SetParent(root.transform, false);
            ApplyMatrix(visual.transform, GearCoordinates["Arrow"].inverse);
            SetFloat(projectile, "arrowLength", data.arrowLength);
            PrefabUtility.SaveAsPrefabAsset(root, ProjectilePath);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static Matrix4x4 CalibrateBody(Transform model)
    {
        string[] names = { "Root", "Head", "Hand.R", "Foot.R" };
        var original = new Matrix4x4();
        var imported = new Matrix4x4();
        for (int i = 0; i < names.Length; i++)
        {
            Vector3 a = Vector(data.bones.Single(b => b.name == names[i]).head);
            Vector3 b = model.InverseTransformPoint(FindBone(model, names[i]).position);
            original.SetColumn(i, new Vector4(a.x, a.y, a.z, 1));
            imported.SetColumn(i, new Vector4(b.x, b.y, b.z, 1));
        }
        if (Mathf.Abs(original.determinant) < 0.000001f) throw new InvalidOperationException("Degenerate calibration landmarks.");
        Matrix4x4 conversion = imported * original.inverse;
        calibrationError = data.bones.Max(b => Vector3.Distance(conversion.MultiplyPoint3x4(Vector(b.head)),
            model.InverseTransformPoint(FindBone(model, b.name).position)));
        if (calibrationError > 0.002f) throw new InvalidOperationException("Imported bind pose does not match source: " + calibrationError);
        return conversion;
    }

    private static AnimatorController Controller(bool archer)
    {
        string path = DirectoryPath + "/" + (archer ? "SkeletonArcherAnimController.controller" : "SkeletonTesteIAAnimController.controller");
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) == null)
            if (!AssetDatabase.CopyAsset(OriginalController, path)) throw new IOException("Could not copy the source controller.");
        AnimatorController result = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        foreach (AnimatorState state in States(result.layers[0].stateMachine))
        {
            string key = state.name == "Pre-Spawn" ? "1_Pose" : state.name;
            if (key == "Attack_01" && archer) key = "Attack_Bow_01";
            if (Clips.TryGetValue(key, out AnimationClip clip)) state.motion = clip;
            if (state.name == "Attack_01" && archer)
            {
                foreach (AnimatorStateTransition transition in state.transitions)
                { transition.hasExitTime = true; transition.exitTime = 1f; transition.duration = 0.08f; }
            }
        }
        EditorUtility.SetDirty(result);
        return result;
    }

    private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {
        foreach (ChildAnimatorState state in machine.states) yield return state.state;
        foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            foreach (AnimatorState state in States(child.stateMachine)) yield return state;
    }

    private static string ShortName(string name) => name.Substring(name.LastIndexOf('|') + 1);
    private static string GearPath(string name) => DirectoryPath + "/Equipment/Prefabs/" + name + ".prefab";
    private static Vector3 Vector(float[] v) => new Vector3(v[0], v[1], v[2]);
    private static Matrix4x4 Matrix(float[] source)
    {
        var result = new Matrix4x4();
        for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) result[row, col] = source[row * 4 + col];
        return result;
    }
    private static Vector3 Marker(Transform wrapper, string name)
        => wrapper.InverseTransformPoint(wrapper.GetComponentsInChildren<Transform>(true).Single(t => t.name == name).position);
    private static Transform FindBone(Transform model, string name)
    {
        Transform root = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Root");
        return root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
    }
    private static void ApplyMatrix(Transform target, Matrix4x4 matrix)
    {
        target.localPosition = matrix.GetColumn(3);
        target.localRotation = matrix.rotation;
        target.localScale = matrix.lossyScale;
    }
    private static void SetObject(Object target, string name, Object value)
    {
        var serialized = new SerializedObject(target); SerializedProperty property = serialized.FindProperty(name);
        if (property == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " is missing.");
        property.objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetFloat(Object target, string name, float value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " is missing.");
        property.floatValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetBool(Object target, string name, bool value)
    {
        var serialized = new SerializedObject(target); serialized.FindProperty(name).boolValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
