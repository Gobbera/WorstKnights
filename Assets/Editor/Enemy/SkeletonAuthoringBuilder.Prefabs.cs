using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class SkeletonAuthoringBuilder
{
    private static void BuildBase()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy.prefab");
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(original, scene);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "Skeleton Base";
            Transform modelContainer = root.transform.Find("Model");
            Animator previousAnimator = modelContainer.GetComponentInChildren<Animator>(true);
            GameObject previousModel = previousAnimator.gameObject;
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath), scene);
            model.name = "SkeletonTesteIA";
            model.transform.SetParent(modelContainer, false);
            bodyCoordinates = CalibrateBody(model.transform);
            Vector3 forward = Vector3.ProjectOnPlane(bodyCoordinates.MultiplyVector(new Vector3(0, -1, 0)), Vector3.up);
            modelContainer.localRotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward);
            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = Controller(false);
            animator.applyRootMotion = false;
            animator.cullingMode = previousAnimator.cullingMode;
            RetargetRootReferences(root, previousModel.transform, model.transform, previousAnimator, animator);
            Object.DestroyImmediate(previousModel);
            Socket(FindBone(model.transform, "Hand.L"), "LeftHandSocket");
            Socket(FindBone(model.transform, "Hand.R"), "RightHandSocket");
            Socket(FindBone(model.transform, "Spine_2"), "BackSocket");
            SceneAnimationPosePreview preview = model.AddComponent<SceneAnimationPosePreview>();
            ConfigurePreview(preview, animator, false);
            WireEnemy(root, animator);
            SetObject(root.GetComponent<EnemyAttack>(), "weaponRoot", FindBone(model.transform, "Hand.R"));
            SetVisualLayers(root, SourceVisualLayer(), false);
            Clips["1_Pose"].SampleAnimation(model, 0f);
            PrefabUtility.SaveAsPrefabAsset(root, BasePath);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void BuildVariant(bool archer)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath), scene);
            root.name = archer ? "Skeleton Archer" : "Skeleton Swordsman";
            Animator animator = root.GetComponentInChildren<Animator>(true);
            Transform model = animator.transform;
            animator.runtimeAnimatorController = Controller(archer);
            Clips["1_Pose"].SampleAnimation(model.gameObject, 0f);
            if (!archer)
            {
                Transform sword = AttachGear(model, "Sword", "RightHandSocket", scene);
                SetObject(root.GetComponent<EnemyAttack>(), "weaponRoot", sword);
                EnemyAnimationController presentation = root.GetComponent<EnemyAnimationController>();
                SetFloat(presentation, "attackAudioDelay", 0.5f);
                SetObject(presentation, "attackAudioAnchor", sword);
            }
            else
            {
                AttachGear(model, "Bow", "LeftHandSocket", scene);
                AttachGear(model, "Quiver", "BackSocket", scene);
                Transform hand = FindBone(model, "Hand.R");
                Transform socket = hand.Find("RightHandSocket");
                GameObject arrow = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GearPath("Arrow")), scene);
                arrow.name = "ArrowVisual";
                arrow.transform.SetParent(socket, false);
                AddAccessoryCurves(model, arrow.transform);
                var combat = root.AddComponent<EnemyArcherCombat>();
                SetObject(combat, "enemySetup", root.GetComponent<EnemySetup>());
                SetObject(combat, "brain", root.GetComponent<EnemyBrain>());
                SetObject(combat, "health", root.GetComponent<EnemyHealth>());
                SetObject(combat, "vision", root.GetComponent<EnemyVision>());
                SetObject(combat, "animator", animator);
                SetObject(combat, "attackClip", Clips["Attack_Bow_01"]);
                SetObject(combat, "presentationArrow", arrow.transform);
                SetObject(combat, "aimSpine", FindBone(model, "Spine_3"));
                SetObject(combat, "projectilePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath).GetComponent<EnemyArrowProjectile>());
                SetFloat(combat, "releaseNormalizedTime", data.releaseFrame / data.frameEnd);
                SetObject(root.GetComponent<EnemyBrain>(), "archerCombat", combat);
                SetObject(root.GetComponent<EnemySetup>(), "archerCombat", combat);
                SetObject(root.GetComponent<EnemyNetworkSync>(), "archerCombat", combat);
                SetBool(root.GetComponent<EnemyAttack>(), "attackEnabled", false);
                SetFloat(root.GetComponent<EnemyVision>(), "viewDistance", 20f);
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // Keep the authoring preview complete; the runtime component hides ammo until windup.
                arrow.SetActive(true);
            }
            ConfigurePreview(model.GetComponent<SceneAnimationPosePreview>(), animator, archer);
            SetVisualLayers(root, SourceVisualLayer(), false);
            // Accessory sampling may leave the body at release; save the common reference pose.
            Clips["1_Pose"].SampleAnimation(model.gameObject, 0f);
            PrefabUtility.SaveAsPrefabAsset(root, archer ? ArcherPath : SwordsmanPath);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static Transform AttachGear(Transform model, string asset, string socketName, Scene scene)
    {
        Attachment attachment = data.attachments.Single(a => a.asset == asset);
        Transform bone = FindBone(model, attachment.bone);
        Transform socket = bone.Find(socketName);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GearPath(asset)), scene);
        instance.name = asset;
        instance.transform.SetParent(socket, false);
        Matrix4x4 desiredWorld = model.localToWorldMatrix * bodyCoordinates * Matrix(attachment.sourceMatrix) * GearCoordinates[asset].inverse;
        ApplyMatrix(instance.transform, socket.worldToLocalMatrix * desiredWorld);
        return instance.transform;
    }

    private static void AddAccessoryCurves(Transform model, Transform arrow)
    {
        AnimationClip clip = Clips["Attack_Bow_01"];
        string arrowPath = AnimationUtility.CalculateTransformPath(arrow, model);
        Transform socket = arrow.parent;
        var positions = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
        var rotations = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
        var scales = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
        Quaternion previous = Quaternion.identity;
        bool hasPrevious = false;
        foreach (Sample sample in data.samples.Where(s => s.arrowMatrix.Length == 16))
        {
            float time = sample.frame / data.frameRate;
            clip.SampleAnimation(model.gameObject, time);
            Matrix4x4 desired = model.localToWorldMatrix * bodyCoordinates * Matrix(sample.arrowMatrix) * GearCoordinates["Arrow"].inverse;
            Matrix4x4 local = socket.worldToLocalMatrix * desired;
            Vector3 position = local.GetColumn(3);
            Quaternion rotation = local.rotation;
            if (hasPrevious && Quaternion.Dot(previous, rotation) < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            previous = rotation; hasPrevious = true;
            Vector3 scale = local.lossyScale;
            for (int i = 0; i < 3; i++) { positions[i].Add(new Keyframe(time, position[i])); scales[i].Add(new Keyframe(time, scale[i])); }
            for (int i = 0; i < 4; i++) rotations[i].Add(new Keyframe(time, rotation[i]));
        }
        string[] xyz = { "x", "y", "z", "w" };
        for (int i = 0; i < 3; i++)
        {
            scales[i].Add(new Keyframe(data.releaseFrame / data.frameRate + 0.0001f, 0f));
            scales[i].Add(new Keyframe(data.frameEnd / data.frameRate, 0f));
        }
        for (int i = 0; i < 3; i++)
        {
            SetLinearCurve(clip, arrowPath, typeof(Transform), "m_LocalPosition." + xyz[i], positions[i]);
            SetLinearCurve(clip, arrowPath, typeof(Transform), "m_LocalScale." + xyz[i], scales[i]);
        }
        for (int i = 0; i < 4; i++) SetLinearCurve(clip, arrowPath, typeof(Transform), "m_LocalRotation." + xyz[i], rotations[i]);
        SkinnedMeshRenderer bow = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Single(r => r.sharedMesh != null && r.sharedMesh.GetBlendShapeIndex("Bow_Draw_IA") >= 0);
        SetLinearCurve(clip, AnimationUtility.CalculateTransformPath(bow.transform, model), typeof(SkinnedMeshRenderer),
            "blendShape.Bow_Draw_IA", data.samples.Select(s => new Keyframe(s.frame / data.frameRate, s.bowDraw * 100f)).ToList());
        clip.EnsureQuaternionContinuity();
        EditorUtility.SetDirty(clip);
        clip.SampleAnimation(model.gameObject, 0f);
    }

    private static void SetLinearCurve(AnimationClip clip, string path, Type type, string property, List<Keyframe> keys)
    {
        var curve = new AnimationCurve(keys.ToArray());
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
    }

    private static void WireEnemy(GameObject root, Animator animator)
    {
        EnemySetup setup = root.GetComponent<EnemySetup>();
        SetObject(setup, "animator", animator);
        SetObject(setup, "modelRoot", animator.transform);
        SetObject(root.GetComponent<EnemyAnimationController>(), "animator", animator);
        setup.RefreshReferences();
    }

    private static void ConfigurePreview(SceneAnimationPosePreview preview, Animator animator, bool archer)
    {
        SetObject(preview, "animator", animator);
        SetObject(preview, "previewRoot", animator.gameObject);
        SetObject(preview, "clipSourceAsset", AssetDatabase.LoadMainAssetAtPath(BodyPath));
        preview.SetPreviewClips(Clips.Where(pair => archer ? pair.Key != "Attack_01" : pair.Key != "Attack_Bow_01")
            .Select(pair => pair.Value).ToArray());
        preview.SetSelectedClipIndex(-1);
    }

    private static Transform Socket(Transform bone, string name)
    {
        var result = new GameObject(name).transform;
        result.SetParent(bone, false);
        return result;
    }

    private static int SourceVisualLayer()
    {
        GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy.prefab");
        Animator animator = original.GetComponentInChildren<Animator>(true);
        return animator.gameObject.layer;
    }

    private static int SetVisualLayers(GameObject root, int layer, bool recordSceneChanges)
    {
        Animator animator = root.GetComponentInChildren<Animator>(true);
        if (animator == null) return 0;
        int count = 0;
        foreach (Renderer renderer in animator.GetComponentsInChildren<Renderer>(true))
        {
            GameObject visual = renderer.gameObject;
            if (visual.layer == layer) continue;
            if (recordSceneChanges && !EditorApplication.isPlaying)
                Undo.RecordObject(visual, "Repair Skeleton Visual Layer");
            visual.layer = layer;
            if (recordSceneChanges && !EditorApplication.isPlaying)
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
            count++;
        }
        if (count > 0 && recordSceneChanges && !EditorApplication.isPlaying && root.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(root.scene);
        return count;
    }

    [MenuItem("Tools/Enemies/Skeleton/Repair Visual Layers")]
    public static void RepairVisualLayers()
    {
        int layer = SourceVisualLayer();
        foreach (string path in new[] { BasePath, SwordsmanPath, ArcherPath })
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                SetVisualLayers(contents, layer, false);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        foreach (EnemySetup enemy in Object.FindObjectsByType<EnemySetup>(FindObjectsInactive.Include))
        {
            Animator animator = enemy.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.name == "SkeletonTesteIA")
                SetVisualLayers(enemy.gameObject, layer, true);
        }
    }

    private static void RetargetRootReferences(GameObject root, Transform oldModel, Transform model,
        Animator oldAnimator, Animator animator)
    {
        foreach (Component component in root.GetComponents<Component>())
        {
            if (component == null || component is Transform) continue;
            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object current = property.objectReferenceValue;
                if (current == oldAnimator) { property.objectReferenceValue = animator; continue; }
                Transform oldTransform = current is Transform t ? t : current is GameObject go ? go.transform : null;
                if (oldTransform == null || !oldTransform.IsChildOf(oldModel)) continue;
                Transform replacement = oldTransform == oldModel ? model : model.Find(AnimationUtility.CalculateTransformPath(oldTransform, oldModel));
                if (replacement == null)
                {
                    Transform[] candidates = model.GetComponentsInChildren<Transform>(true).Where(n => n.name == oldTransform.name).ToArray();
                    if (candidates.Length == 1) replacement = candidates[0];
                }
                property.objectReferenceValue = current is GameObject ? replacement != null ? replacement.gameObject : null : replacement;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
