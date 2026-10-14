using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WKFakeVolumeController))]
public sealed class WKFakeVolumeControllerEditor : Editor
{
    private const string ProjectPrefabRoot = "Assets/Prefabs/VFX/FakeVolume";

    private static bool showReferences;
    private static bool showAuthoring;

    private SerializedProperty settingsProperty;
    private SerializedProperty volumeRenderersProperty;
    private SerializedProperty controlledLightsProperty;
    private SerializedProperty motionRootProperty;
    private SerializedProperty shapeBindingsProperty;
    private SerializedProperty includeInactiveChildrenProperty;
    private SerializedProperty applyInEditModeProperty;
    private SerializedProperty autoCreateVisualsProperty;
    private SerializedProperty createGeneratedLightProperty;
    private SerializedProperty disableMonoLimboAnimatorsProperty;

    private void OnEnable()
    {
        settingsProperty = serializedObject.FindProperty("settings");
        volumeRenderersProperty = serializedObject.FindProperty("volumeRenderers");
        controlledLightsProperty = serializedObject.FindProperty("controlledLights");
        motionRootProperty = serializedObject.FindProperty("motionRoot");
        shapeBindingsProperty = serializedObject.FindProperty("shapeBindings");
        includeInactiveChildrenProperty = serializedObject.FindProperty("includeInactiveChildren");
        applyInEditModeProperty = serializedObject.FindProperty("applyInEditMode");
        autoCreateVisualsProperty = serializedObject.FindProperty("autoCreateVisuals");
        createGeneratedLightProperty = serializedObject.FindProperty("createGeneratedLight");
        disableMonoLimboAnimatorsProperty = serializedObject.FindProperty("disableMonoLimboAnimators");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        DrawTuningInspector();
        bool inspectorChanged = EditorGUI.EndChangeCheck();

        serializedObject.ApplyModifiedProperties();

        if (inspectorChanged && !Application.isPlaying)
            ForEachTarget(EnsureVisualsAfterInspectorChange);
    }

    private void DrawTuningInspector()
    {
        DrawShapeSection();
        DrawVisualSection();
        DrawNoiseSection();
        DrawWindSection();
        DrawLightSection();
        DrawAnimationSection();
        DrawColorCycleSection();
        DrawMotionSection();
        DrawAdvancedSections();
    }

    private void DrawShapeSection()
    {
        DrawSectionLabel("Shape");
        EditorGUILayout.PropertyField(Setting("shape"));
    }

    private void DrawVisualSection()
    {
        DrawSectionLabel("Visual");
        EditorGUILayout.PropertyField(Setting("color"));
        EditorGUILayout.PropertyField(Setting("colorIntensity"), new GUIContent("Color Intensity"));
        EditorGUILayout.PropertyField(Setting("opacity"));
        EditorGUILayout.PropertyField(Setting("power"));
        EditorGUILayout.PropertyField(Setting("edge"));
        EditorGUILayout.PropertyField(Setting("fadeDistance"), new GUIContent("Fade Distance"));
        EditorGUILayout.PropertyField(Setting("extraSoftness"), new GUIContent("Extra Softness"));
    }

    private void DrawNoiseSection()
    {
        SerializedProperty driveNoise = Setting("driveNoise");

        DrawSectionLabel("Noise");
        EditorGUILayout.PropertyField(driveNoise, new GUIContent("Use Noise"));
        if (!driveNoise.boolValue)
            return;

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(Setting("noiseAScale"), new GUIContent("Noise A Scale"));
        EditorGUILayout.PropertyField(Setting("noiseBScale"), new GUIContent("Noise B Scale"));
        EditorGUILayout.PropertyField(Setting("noiseASize"), new GUIContent("Noise A Size"));
        EditorGUILayout.PropertyField(Setting("noiseBSize"), new GUIContent("Noise B Size"));
        EditorGUILayout.PropertyField(Setting("noiseASpeed"), new GUIContent("Noise A Speed"));
        EditorGUILayout.PropertyField(Setting("noiseBSpeed"), new GUIContent("Noise B Speed"));

        SerializedProperty extraNoise = Setting("extraNoise");
        EditorGUILayout.PropertyField(extraNoise, new GUIContent("Extra Noise"));
        if (extraNoise.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(Setting("extraNoiseDensity"), new GUIContent("Extra Noise Density"));
            EditorGUILayout.PropertyField(Setting("extraNoiseSize"), new GUIContent("Extra Noise Size"));
            EditorGUI.indentLevel--;
        }

        EditorGUI.indentLevel--;
    }

    private void DrawWindSection()
    {
        DrawSectionLabel("Wind");
        SerializedProperty useWind = Setting("useWind");
        EditorGUILayout.PropertyField(useWind, new GUIContent("Use Wind"));
        if (!useWind.boolValue)
            return;

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(Setting("windSpeed"), new GUIContent("Wind Speed"));
        EditorGUILayout.PropertyField(Setting("windVisibility"), new GUIContent("Wind Visibility"));
        EditorGUI.indentLevel--;
    }

    private void DrawLightSection()
    {
        bool hasLightConfiguration = HasControlledLight() || createGeneratedLightProperty.boolValue;

        DrawSectionLabel("Light");
        EditorGUILayout.PropertyField(createGeneratedLightProperty, new GUIContent("Create Generated Light"));
        if (!hasLightConfiguration)
            return;

        SerializedProperty driveLights = Setting("driveLights");
        EditorGUILayout.PropertyField(driveLights, new GUIContent("Control Lights"));
        if (!driveLights.boolValue)
            return;

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(Setting("lightsEnabled"), new GUIContent("Lights Enabled"));

        SerializedProperty syncLightColor = Setting("syncLightColor");
        EditorGUILayout.PropertyField(syncLightColor, new GUIContent("Use Volume Color"));
        if (!syncLightColor.boolValue)
            EditorGUILayout.PropertyField(Setting("lightColor"), new GUIContent("Light Color"));

        EditorGUILayout.PropertyField(Setting("lightIntensity"), new GUIContent("Light Intensity"));
        EditorGUILayout.PropertyField(Setting("lightRange"), new GUIContent("Light Range"));

        if (CurrentLightUsesSpotAngles())
        {
            EditorGUILayout.PropertyField(Setting("spotAngle"), new GUIContent("Spot Angle"));
            EditorGUILayout.PropertyField(Setting("innerSpotAngle"), new GUIContent("Inner Spot Angle"));
        }

        EditorGUI.indentLevel--;
    }

    private void DrawAnimationSection()
    {
        DrawSectionLabel("Animation");
        EditorGUILayout.PropertyField(Setting("updateMode"), new GUIContent("Update Mode"));

        SerializedProperty randomizeSeed = Setting("randomizeSeed");
        EditorGUILayout.PropertyField(randomizeSeed, new GUIContent("Randomize Seed"));
        if (!randomizeSeed.boolValue)
            EditorGUILayout.PropertyField(Setting("seed"));

        SerializedProperty enableFlicker = Setting("enableFlicker");
        EditorGUILayout.PropertyField(enableFlicker, new GUIContent("Enable Flicker"));
        if (!enableFlicker.boolValue)
            return;

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(Setting("flickerSpeed"), new GUIContent("Flicker Speed"));
        EditorGUILayout.PropertyField(Setting("flickerResponse"), new GUIContent("Flicker Response"));
        EditorGUILayout.PropertyField(Setting("flickerOpacityAmount"), new GUIContent("Opacity Amount"));
        EditorGUILayout.PropertyField(Setting("flickerPowerAmount"), new GUIContent("Power Amount"));

        if ((HasControlledLight() || createGeneratedLightProperty.boolValue) && Setting("driveLights").boolValue)
            EditorGUILayout.PropertyField(Setting("flickerLightIntensityAmount"), new GUIContent("Light Intensity Amount"));

        EditorGUI.indentLevel--;
    }

    private void DrawColorCycleSection()
    {
        DrawSectionLabel("Color Cycle");
        SerializedProperty colorMode = Setting("colorMode");
        EditorGUILayout.PropertyField(colorMode, new GUIContent("Color Mode"));
        if ((WKFakeVolumeColorMode)colorMode.enumValueIndex != WKFakeVolumeColorMode.HueCycle)
            return;

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(Setting("colorCycleSpeed"), new GUIContent("Cycle Speed"));
        EditorGUILayout.PropertyField(Setting("colorSaturation"), new GUIContent("Saturation"));
        EditorGUILayout.PropertyField(Setting("colorValue"), new GUIContent("Value"));
        EditorGUI.indentLevel--;
    }

    private void DrawMotionSection()
    {
        DrawSectionLabel("Motion");
        SerializedProperty motionMode = Setting("motionMode");
        EditorGUILayout.PropertyField(motionMode, new GUIContent("Motion Mode"));

        WKFakeVolumeMotionMode mode = (WKFakeVolumeMotionMode)motionMode.enumValueIndex;
        if (mode == WKFakeVolumeMotionMode.None)
            return;

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(Setting("motionSpeed"), new GUIContent("Motion Speed"));

        switch (mode)
        {
            case WKFakeVolumeMotionMode.Swing:
                EditorGUILayout.PropertyField(Setting("swingAngle"), new GUIContent("Swing Angle"));
                EditorGUILayout.PropertyField(Setting("swingAxes"), new GUIContent("Swing Axes"));
                break;
            case WKFakeVolumeMotionMode.Rotate:
                EditorGUILayout.PropertyField(Setting("rotateAxis"), new GUIContent("Rotate Axis"));
                EditorGUILayout.PropertyField(Setting("rotateDegreesPerSecond"), new GUIContent("Degrees Per Second"));
                break;
            case WKFakeVolumeMotionMode.Drift:
                EditorGUILayout.PropertyField(Setting("driftAmplitude"), new GUIContent("Drift Amplitude"));
                break;
        }

        EditorGUI.indentLevel--;
    }

    private void DrawAdvancedSections()
    {
        EditorGUILayout.Space();
        showReferences = EditorGUILayout.Foldout(showReferences, "Advanced References", true);
        if (showReferences)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(volumeRenderersProperty, true);
            EditorGUILayout.PropertyField(controlledLightsProperty, true);
            EditorGUILayout.PropertyField(motionRootProperty);
            EditorGUILayout.PropertyField(shapeBindingsProperty, true);
            EditorGUI.indentLevel--;
        }

        showAuthoring = EditorGUILayout.Foldout(showAuthoring, "Advanced Authoring", true);
        if (showAuthoring)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(includeInactiveChildrenProperty, new GUIContent("Include Inactive Children"));
            EditorGUILayout.PropertyField(applyInEditModeProperty, new GUIContent("Preview In Edit Mode"));
            EditorGUILayout.PropertyField(autoCreateVisualsProperty, new GUIContent("Auto Create Visuals"));
            EditorGUILayout.PropertyField(disableMonoLimboAnimatorsProperty, new GUIContent("Disable Legacy Animators"));
            EditorGUI.indentLevel--;
        }
    }

    private SerializedProperty Setting(string propertyName)
    {
        return settingsProperty.FindPropertyRelative(propertyName);
    }

    private WKFakeVolumeShape CurrentShape()
    {
        return (WKFakeVolumeShape)Setting("shape").enumValueIndex;
    }

    private bool HasControlledLight()
    {
        return ArrayHasObjectReference(controlledLightsProperty);
    }

    private bool CurrentLightUsesSpotAngles()
    {
        WKFakeVolumeShape shape = CurrentShape();
        if (shape == WKFakeVolumeShape.SpotCone)
            return true;

        return shape == WKFakeVolumeShape.Existing && AnyControlledLightIsSpot();
    }

    private bool AnyControlledLightIsSpot()
    {
        if (controlledLightsProperty == null || !controlledLightsProperty.isArray)
            return false;

        for (int i = 0; i < controlledLightsProperty.arraySize; i++)
        {
            SerializedProperty element = controlledLightsProperty.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue is Light light && light.type == LightType.Spot)
                return true;
        }

        return false;
    }

    private static bool ArrayHasObjectReference(SerializedProperty property)
    {
        if (property == null || !property.isArray)
            return false;

        for (int i = 0; i < property.arraySize; i++)
        {
            if (property.GetArrayElementAtIndex(i).objectReferenceValue != null)
                return true;
        }

        return false;
    }

    private static void DrawSectionLabel(string label)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
    }

    [MenuItem("GameObject/VFX/WK Fake Volume", false, 10)]
    private static void CreateFakeVolumeGameObject(MenuCommand command)
    {
        GameObject fakeVolume = new GameObject("WK Fake Volume");
        Undo.RegisterCreatedObjectUndo(fakeVolume, "Create WK Fake Volume");
        GameObjectUtility.SetParentAndAlign(fakeVolume, command.context as GameObject);

        WKFakeVolumeController controller = Undo.AddComponent<WKFakeVolumeController>(fakeVolume);
        controller.EnsureAuthoringVisuals(forceRebuild: true);
        controller.ApplyNow();
        MarkDirty(controller);

        Selection.activeGameObject = fakeVolume;
    }

    [MenuItem("Tools/VFX/Fake Volume/Create WK Fake Volume")]
    private static void CreateFakeVolumeFromToolsMenu()
    {
        CreateFakeVolumeGameObject(new MenuCommand(null));
    }

    [MenuItem("Tools/VFX/Fake Volume/Add Controller To Selection")]
    private static void AddControllerToSelection()
    {
        GameObject[] gameObjects = Selection.gameObjects;
        for (int i = 0; i < gameObjects.Length; i++)
        {
            GameObject selected = gameObjects[i];
            if (selected == null)
                continue;

            WKFakeVolumeController controller = selected.GetComponent<WKFakeVolumeController>();
            if (controller == null)
                controller = Undo.AddComponent<WKFakeVolumeController>(selected);

            Undo.RecordObject(controller, "Auto Bind WK Fake Volume");
            bool createdVisuals = controller.EnsureAuthoringVisuals();
            if (!createdVisuals)
            {
                controller.AutoBind();
                controller.PullCurrentValuesFromTargets();
            }

            controller.ApplyNow();
            MarkDirty(controller);
        }
    }

    [MenuItem("Tools/VFX/Fake Volume/Add Controller To Selection", true)]
    private static bool ValidateAddControllerToSelection()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    [MenuItem("Tools/VFX/Fake Volume/Auto Bind Selected Controllers")]
    private static void AutoBindSelectedControllers()
    {
        WKFakeVolumeController[] controllers = Selection.GetFiltered<WKFakeVolumeController>(SelectionMode.Editable | SelectionMode.Deep);
        for (int i = 0; i < controllers.Length; i++)
            ApplyAutoBind(controllers[i]);
    }

    [MenuItem("Tools/VFX/Fake Volume/Auto Bind Selected Controllers", true)]
    private static bool ValidateAutoBindSelectedControllers()
    {
        return Selection.GetFiltered<WKFakeVolumeController>(SelectionMode.Editable | SelectionMode.Deep).Length > 0;
    }

    [MenuItem("Tools/VFX/Fake Volume/Rebind WK Prefab Library")]
    private static void RebindPrefabLibrary()
    {
        EnsureProjectPrefabRoot();

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { ProjectPrefabRoot });
        int reboundCount = 0;
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            if (string.IsNullOrEmpty(prefabPath))
                continue;

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                RemovePackageDemoScripts(contents);
                WKFakeVolumeController controller = contents.GetComponent<WKFakeVolumeController>();
                bool addedController = controller == null;
                if (controller == null)
                    controller = contents.AddComponent<WKFakeVolumeController>();

                controller.AutoBind();
                if (addedController)
                    controller.PullCurrentValuesFromTargets();

                controller.ApplyNow();

                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                reboundCount++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[WKFakeVolumeController] Rebound {reboundCount} WK fake volume prefabs in {ProjectPrefabRoot}.");
    }

    private void ForEachTarget(System.Action<WKFakeVolumeController> action)
    {
        Object[] targetObjects = targets;
        for (int i = 0; i < targetObjects.Length; i++)
        {
            if (targetObjects[i] is WKFakeVolumeController controller)
                action(controller);
        }
    }

    private static void ApplyAutoBind(WKFakeVolumeController controller)
    {
        Undo.RecordObject(controller, "Auto Bind WK Fake Volume");
        controller.AutoBind();
        controller.ApplyNow();
        MarkDirty(controller);
    }

    private static void EnsureVisualsAfterInspectorChange(WKFakeVolumeController controller)
    {
        Undo.RegisterFullObjectHierarchyUndo(controller.gameObject, "Update WK Fake Volume");
        controller.EnsureAuthoringVisuals();
        controller.ApplyNow();
        MarkDirty(controller);
    }

    private static void MarkDirty(WKFakeVolumeController controller)
    {
        EditorUtility.SetDirty(controller);
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
    }

    private static void EnsureProjectPrefabRoot()
    {
        EnsureFolder("Assets", "Prefabs");
        EnsureFolder("Assets/Prefabs", "VFX");
        EnsureFolder("Assets/Prefabs/VFX", "FakeVolume");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static void RemovePackageDemoScripts(GameObject root)
    {
        Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
        for (int i = behaviours.Length - 1; i >= 0; i--)
        {
            Behaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            string fullName = behaviour.GetType().FullName;
            if (fullName == "MonoLimboStudio.LightFlicker"
                || fullName == "MonoLimboStudio.PartyLightController"
                || fullName == "MonoLimboStudio.PartyLightSwing")
            {
                DestroyImmediate(behaviour, true);
            }
        }
    }
}
