#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SceneAnimationPosePreview))]
public class SceneAnimationPosePreviewEditor : Editor
{
    private static SceneAnimationPosePreview activePreviewTarget;

    private SerializedProperty animatorProperty;
    private SerializedProperty previewRootProperty;
    private SerializedProperty clipSourceAssetProperty;
    private SerializedProperty previewClipsProperty;

    static SceneAnimationPosePreviewEditor()
    {
        EditorApplication.update -= UpdateActivePreview;
        EditorApplication.update += UpdateActivePreview;

        AssemblyReloadEvents.beforeAssemblyReload -= StopPreviewOnReload;
        AssemblyReloadEvents.beforeAssemblyReload += StopPreviewOnReload;

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void OnEnable()
    {
        animatorProperty = serializedObject.FindProperty("animator");
        previewRootProperty = serializedObject.FindProperty("previewRoot");
        clipSourceAssetProperty = serializedObject.FindProperty("clipSourceAsset");
        previewClipsProperty = serializedObject.FindProperty("previewClips");
    }

    private void OnDisable()
    {
        // Keep the sampled pose alive when the user selects another object to
        // author props/items against that pose in the Scene view.
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(animatorProperty);
        EditorGUILayout.PropertyField(previewRootProperty);
        EditorGUILayout.PropertyField(clipSourceAssetProperty);
        EditorGUILayout.PropertyField(previewClipsProperty, true);

        serializedObject.ApplyModifiedProperties();

        SceneAnimationPosePreview preview = (SceneAnimationPosePreview)target;
        if (preview == null)
            return;

        preview.ResolveReferences();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scene Pose Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Use esta ferramenta em Scene/Edit Mode para visualizar poses e clips no modelo sem entrar em Play. Para poses de mao, normalmente voce vai escolher um clip de grip e ajustar o tempo pelo slider.",
            MessageType.Info);

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Saia do Play para usar o preview de pose na cena.", MessageType.None);
            return;
        }

        DrawClipLoadTools(preview);

        IReadOnlyList<AnimationClip> clips = preview.PreviewClips;
        if (clips == null || clips.Count == 0)
        {
            EditorGUILayout.HelpBox("Nenhum clip disponivel. Carregue clips do Animator, do asset fonte, ou preencha a lista manualmente.", MessageType.Warning);
            return;
        }

        bool previewSelectionChanged = false;

        string[] clipNames = BuildClipNames(clips);
        int popupIndex = Mathf.Clamp(preview.SelectedClipIndex, 0, clipNames.Length - 1);
        EditorGUI.BeginChangeCheck();
        int selectedIndex = EditorGUILayout.Popup("Animation", popupIndex, clipNames);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(preview, "Change Preview Animation");
            preview.SetSelectedClipIndex(selectedIndex);
            EditorUtility.SetDirty(preview);
            previewSelectionChanged = true;
        }

        EditorGUI.BeginChangeCheck();
        float normalizedTime = EditorGUILayout.Slider("Normalized Time", preview.NormalizedTime, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(preview, "Change Preview Animation Time");
            preview.SetNormalizedTime(normalizedTime);
            EditorUtility.SetDirty(preview);
            previewSelectionChanged = true;
        }

        AnimationClip selectedClip = preview.GetSelectedClip();
        if (selectedClip != null)
        {
            EditorGUILayout.HelpBox(
                $"Clip atual: {selectedClip.name} | Tempo: {preview.GetSampleTime():0.###}s / {selectedClip.length:0.###}s",
                MessageType.None);
        }

        bool canPreview = CanPreview(preview, out string previewWarning);
        using (new EditorGUI.DisabledScope(!canPreview))
        {
            if (GUILayout.Button("Aplicar Preview"))
                StartPreview(preview);
        }

        using (new EditorGUI.DisabledScope(activePreviewTarget != preview))
        {
            if (GUILayout.Button("Restaurar Pose"))
                StopPreview();
        }

        if (!string.IsNullOrEmpty(previewWarning))
            EditorGUILayout.HelpBox(previewWarning, MessageType.Warning);

        if (previewSelectionChanged && activePreviewTarget == preview && canPreview)
            SampleActivePreview(forceStart: true);
    }

    private static void DrawClipLoadTools(SceneAnimationPosePreview preview)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Clip Setup", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Carregar Clips do Animator"))
                LoadClipsFromAnimator(preview);

            using (new EditorGUI.DisabledScope(preview.ClipSourceAsset == null))
            {
                if (GUILayout.Button("Carregar Clips do Asset"))
                    LoadClipsFromAsset(preview);
            }
        }

        if (GUILayout.Button("Limpar Lista de Clips"))
            ClearClipList(preview);
    }

    private static void LoadClipsFromAnimator(SceneAnimationPosePreview preview)
    {
        if (preview == null)
            return;

        Undo.RecordObject(preview, "Load Clips From Animator");
        int clipCount = preview.PopulateClipsFromAnimatorController();
        EditorUtility.SetDirty(preview);

        Debug.Log($"[SceneAnimationPosePreview] {clipCount} clip(s) carregado(s) do Animator em '{preview.name}'.", preview);

        if (activePreviewTarget == preview)
        {
            if (CanPreview(preview, out _))
                SampleActivePreview(forceStart: true);
            else
                StopPreview();
        }
    }

    private static void LoadClipsFromAsset(SceneAnimationPosePreview preview)
    {
        if (preview == null || preview.ClipSourceAsset == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(preview.ClipSourceAsset);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning("[SceneAnimationPosePreview] Nao foi possivel resolver o caminho do asset fonte.", preview);
            return;
        }

        UnityEngine.Object[] loadedAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        List<AnimationClip> clips = new List<AnimationClip>(loadedAssets.Length);
        for (int i = 0; i < loadedAssets.Length; i++)
        {
            if (!(loadedAssets[i] is AnimationClip clip))
                continue;

            if (ShouldIgnoreClip(clip, preview.ClipSourceAsset))
                continue;

            clips.Add(clip);
        }

        Undo.RecordObject(preview, "Load Clips From Asset");
        preview.SetPreviewClips(clips.ToArray());
        EditorUtility.SetDirty(preview);

        Debug.Log($"[SceneAnimationPosePreview] {clips.Count} clip(s) carregado(s) do asset '{preview.ClipSourceAsset.name}' em '{preview.name}'.", preview);

        if (activePreviewTarget == preview)
        {
            if (CanPreview(preview, out _))
                SampleActivePreview(forceStart: true);
            else
                StopPreview();
        }
    }

    private static void ClearClipList(SceneAnimationPosePreview preview)
    {
        if (preview == null)
            return;

        Undo.RecordObject(preview, "Clear Preview Clips");
        preview.SetPreviewClips(Array.Empty<AnimationClip>());
        EditorUtility.SetDirty(preview);

        if (activePreviewTarget == preview)
            StopPreview();
    }

    private static bool CanPreview(SceneAnimationPosePreview preview, out string warningMessage)
    {
        warningMessage = null;

        if (preview == null)
        {
            warningMessage = "Preview invalido.";
            return false;
        }

        preview.ResolveReferences();

        if (preview.Animator == null)
        {
            warningMessage = "Nenhum Animator foi encontrado.";
            return false;
        }

        if (preview.PreviewRoot == null)
        {
            warningMessage = "Nenhum Preview Root foi encontrado.";
            return false;
        }

        if (preview.GetSelectedClip() == null)
        {
            warningMessage = "Selecione um clip valido.";
            return false;
        }

        return true;
    }

    private static string[] BuildClipNames(IReadOnlyList<AnimationClip> clips)
    {
        string[] names = new string[clips.Count];
        for (int i = 0; i < clips.Count; i++)
            names[i] = clips[i] != null ? clips[i].name : "<null>";

        return names;
    }

    private static void StartPreview(SceneAnimationPosePreview preview)
    {
        activePreviewTarget = preview;
        SampleActivePreview(forceStart: true);
    }

    private static void StopPreview()
    {
        activePreviewTarget = null;

        if (AnimationMode.InAnimationMode())
            AnimationMode.StopAnimationMode();

        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
    }

    [MenuItem("Tools/Animation/Restaurar Scene Pose Preview")]
    private static void StopPreviewFromMenu()
    {
        StopPreview();
    }

    [MenuItem("Tools/Animation/Restaurar Scene Pose Preview", true)]
    private static bool CanStopPreviewFromMenu()
    {
        return activePreviewTarget != null || AnimationMode.InAnimationMode();
    }

    private static void SampleActivePreview(bool forceStart)
    {
        if (Application.isPlaying)
        {
            StopPreview();
            return;
        }

        if (!CanPreview(activePreviewTarget, out _))
        {
            StopPreview();
            return;
        }

        if (forceStart && !AnimationMode.InAnimationMode())
            AnimationMode.StartAnimationMode();
        else if (!AnimationMode.InAnimationMode())
            return;

        AnimationClip clip = activePreviewTarget.GetSelectedClip();
        if (clip == null)
            return;

        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(activePreviewTarget.PreviewRoot, clip, activePreviewTarget.GetSampleTime());
        AnimationMode.EndSampling();

        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
    }

    private static void UpdateActivePreview()
    {
        if (activePreviewTarget == null)
            return;

        SampleActivePreview(forceStart: false);
    }

    private static void StopPreviewOnReload()
    {
        StopPreview();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            StopPreview();
    }

    private static bool ShouldIgnoreClip(AnimationClip clip, UnityEngine.Object assetSource)
    {
        if (clip == null)
            return true;

        if (clip == assetSource)
            return true;

        return clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase);
    }
}
#endif
