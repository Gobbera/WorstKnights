using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

[CustomEditor(typeof(PlatformController))]
public class PlatformControllerEditor : Editor
{
    private SerializedProperty platformNameProperty;
    private SerializedProperty movingPartProperty;
    private SerializedProperty motionModeProperty;
    private SerializedProperty movementPointsProperty;
    private SerializedProperty carryPlayersProperty;
    private SerializedProperty activationModeProperty;
    private SerializedProperty activationSignalsProperty;
    private SerializedProperty signalRequirementProperty;
    private SerializedProperty movementAudioCueProperty;
    private SerializedProperty movementAudioSourceProperty;
    private SerializedProperty startAudioCueProperty, stopAudioCueProperty;
    private SerializedProperty startAudioSourceProperty, stopAudioSourceProperty;
    private bool audioSourcesExpanded;
    private SerializedProperty breakableProperty;
    private SerializedProperty breakDelayProperty;
    private SerializedProperty topTriggerHeightProperty;
    private SerializedProperty respawnsProperty;
    private SerializedProperty respawnDelayProperty;
    private SerializedProperty playerDetectionMaskProperty;

    private void OnEnable()
    {
        platformNameProperty = serializedObject.FindProperty("platformName");
        movingPartProperty = serializedObject.FindProperty("movingPart");
        motionModeProperty = serializedObject.FindProperty("motionMode");
        movementPointsProperty = serializedObject.FindProperty("movementPoints");
        carryPlayersProperty = serializedObject.FindProperty("carryPlayers");
        activationModeProperty = serializedObject.FindProperty("activationMode");
        activationSignalsProperty = serializedObject.FindProperty("activationSignals");
        signalRequirementProperty = serializedObject.FindProperty("signalRequirement");
        movementAudioCueProperty = serializedObject.FindProperty("movementAudioCue");
        movementAudioSourceProperty = serializedObject.FindProperty("movementAudioSource");
        startAudioCueProperty = serializedObject.FindProperty("startAudioCue");
        stopAudioCueProperty = serializedObject.FindProperty("stopAudioCue");
        startAudioSourceProperty = serializedObject.FindProperty("startAudioSource");
        stopAudioSourceProperty = serializedObject.FindProperty("stopAudioSource");
        breakableProperty = serializedObject.FindProperty("breakable");
        breakDelayProperty = serializedObject.FindProperty("breakDelay");
        topTriggerHeightProperty = serializedObject.FindProperty("topTriggerHeight");
        respawnsProperty = serializedObject.FindProperty("respawns");
        respawnDelayProperty = serializedObject.FindProperty("respawnDelay");
        playerDetectionMaskProperty = serializedObject.FindProperty("playerDetectionMask");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSetupSection();
        EditorGUILayout.Space();
        DrawMotionSection();
        EditorGUILayout.Space();
        DrawActivationSection();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("4. Movement Audio", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(startAudioCueProperty, new GUIContent("Start Audio Cue"));
        EditorGUILayout.PropertyField(movementAudioCueProperty, new GUIContent("Moving Audio Cue"));
        EditorGUILayout.PropertyField(stopAudioCueProperty, new GUIContent("Stop Audio Cue"));
        EditorGUILayout.HelpBox("Start plays once on departure. Moving loops throughout travel. Stop plays once on arrival at A or B. Start and Moving begin together; continuous reversal keeps the loop. Empty stages are silent.", MessageType.None);
        audioSourcesExpanded = EditorGUILayout.Foldout(audioSourcesExpanded, "Audio Sources", true);
        if (audioSourcesExpanded)
        {
            EditorGUILayout.PropertyField(startAudioSourceProperty, new GUIContent("Start Audio Source"));
            EditorGUILayout.PropertyField(movementAudioSourceProperty, new GUIContent("Moving Audio Source"));
            EditorGUILayout.PropertyField(stopAudioSourceProperty, new GUIContent("Stop Audio Source"));
            EditorGUILayout.HelpBox("Use three dedicated sources. Prepare them before Play; older controllers prepare missing sources in Awake.", MessageType.None);
        }
        using (new EditorGUI.DisabledScope(Application.isPlaying))
            if (GUILayout.Button("Prepare Audio Sources"))
            {
                serializedObject.ApplyModifiedProperties();
                PrepareAudioSources((PlatformController)target);
                serializedObject.Update();
            }
        EditorGUILayout.Space();
        DrawBreakableSection();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSetupSection()
    {
        EditorGUILayout.LabelField("1. Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(platformNameProperty, new GUIContent("Platform Name"));
        EditorGUILayout.PropertyField(movingPartProperty, new GUIContent("Moving Part"));
    }

    private void DrawMotionSection()
    {
        EditorGUILayout.LabelField("2. Motion", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(motionModeProperty, new GUIContent("Motion Mode"));

        PlatformController.PlatformMotionMode motionMode = GetMotionMode();
        if (motionMode == PlatformController.PlatformMotionMode.Static)
        {
            EditorGUILayout.HelpBox(
                "Static keeps the platform still. Choose another Motion Mode to use the configured path.",
                MessageType.None);
            return;
        }

        DrawMovementPointsSection();
        EditorGUILayout.PropertyField(carryPlayersProperty, new GUIContent("Carry Players"));

        switch (motionMode)
        {
            case PlatformController.PlatformMotionMode.OneWay:
                EditorGUILayout.HelpBox(
                    "OneWay follows the entire path once and remains at its final endpoint.",
                    MessageType.Info);
                break;

            case PlatformController.PlatformMotionMode.TwoPosition:
                EditorGUILayout.HelpBox("TwoPosition moves to the last path point in active/B and returns to the initial position in inactive/A. It stops at each endpoint and supports repeated trips and reversal during travel.", MessageType.Info);
                break;

            default:
                EditorGUILayout.HelpBox(
                    "PingPong repeatedly traverses the path forward and backward while activation is satisfied.",
                    MessageType.Info);
                break;
        }
    }

    private void DrawMovementPointsSection()
    {
        EditorGUILayout.LabelField("Movement Points", EditorStyles.boldLabel);

        if (movementPointsProperty.arraySize == 0)
        {
            EditorGUILayout.HelpBox(
                "Add at least one movement point to create the platform path.",
                MessageType.Warning);
        }

        int removeIndex = -1;
        for (int i = 0; i < movementPointsProperty.arraySize; i++)
        {
            SerializedProperty pointProperty = movementPointsProperty.GetArrayElementAtIndex(i);
            SerializedProperty directionModeProperty = pointProperty.FindPropertyRelative("directionMode");
            SerializedProperty axisProperty = pointProperty.FindPropertyRelative("axis");
            SerializedProperty directionProperty = pointProperty.FindPropertyRelative("direction");
            SerializedProperty diagonalDirectionProperty = pointProperty.FindPropertyRelative("diagonalDirection");
            SerializedProperty distanceProperty = pointProperty.FindPropertyRelative("distance");
            SerializedProperty speedProperty = pointProperty.FindPropertyRelative("speed");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Point {i + 1}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                removeIndex = i;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(directionModeProperty, new GUIContent("Direction Mode"));

            PlatformController.PlatformMovementDirectionMode directionMode =
                (PlatformController.PlatformMovementDirectionMode)directionModeProperty.enumValueIndex;
            if (directionMode == PlatformController.PlatformMovementDirectionMode.Axis)
            {
                EditorGUILayout.PropertyField(axisProperty, new GUIContent("Axis"));
                EditorGUILayout.PropertyField(directionProperty, new GUIContent("Direction"));
            }
            else
            {
                EditorGUILayout.PropertyField(diagonalDirectionProperty, new GUIContent("Diagonal Direction"));
                EditorGUILayout.HelpBox(
                    "Use a vector such as (1, 0, 1) or (-1, 1, 0). The direction is normalized automatically.",
                    MessageType.None);
            }

            EditorGUILayout.PropertyField(distanceProperty, new GUIContent("Distance"));
            EditorGUILayout.PropertyField(speedProperty, new GUIContent("Speed"));
            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
            movementPointsProperty.DeleteArrayElementAtIndex(removeIndex);

        if (GUILayout.Button("Add Movement Point"))
            AddMovementPoint();
    }

    private void AddMovementPoint()
    {
        int newIndex = movementPointsProperty.arraySize;
        movementPointsProperty.InsertArrayElementAtIndex(newIndex);

        SerializedProperty pointProperty = movementPointsProperty.GetArrayElementAtIndex(newIndex);
        pointProperty.FindPropertyRelative("directionMode").enumValueIndex = (int)PlatformController.PlatformMovementDirectionMode.Axis;
        pointProperty.FindPropertyRelative("axis").enumValueIndex = (int)PlatformController.PlatformAxis.X;
        pointProperty.FindPropertyRelative("direction").enumValueIndex = (int)PlatformController.PlatformDirection.Positive;
        pointProperty.FindPropertyRelative("diagonalDirection").vector3Value = new Vector3(1f, 0f, 1f);
        pointProperty.FindPropertyRelative("distance").floatValue = 2f;
        pointProperty.FindPropertyRelative("speed").floatValue = 1f;
    }

    private void DrawActivationSection()
    {
        EditorGUILayout.LabelField("3. Activation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(activationModeProperty, new GUIContent("Activation Mode"));

        PlatformController.PlatformActivationMode activationMode = GetActivationMode();
        switch (activationMode)
        {
            case PlatformController.PlatformActivationMode.PlayerOnTop:
                EditorGUILayout.HelpBox(
                    "Player On Top uses the platform's top detection volume as its activation input.",
                    MessageType.Info);
                break;

            case PlatformController.PlatformActivationMode.SignalSource:
                EditorGUILayout.PropertyField(activationSignalsProperty, new GUIContent("Activation Signals"), includeChildren: true);
                EditorGUILayout.PropertyField(signalRequirementProperty, new GUIContent("Signal Requirement"));
                EditorGUILayout.HelpBox(
                    "Assign Door Signal Sources driven by modular triggers, levers or trigger zones. Any/All defines the combined activation state.",
                    MessageType.Info);
                break;

            default:
                EditorGUILayout.HelpBox(
                    "Always Active continuously enables movement. TwoPosition stays at its B endpoint with this input.",
                    MessageType.None);
                break;
        }

        if (GetMotionMode() == PlatformController.PlatformMotionMode.OneWay)
        {
            EditorGUILayout.HelpBox(
                "OneWay needs one activation to complete the full route and remain at its destination. Use TwoPosition for a repeatable A/B elevator.",
                MessageType.Warning);
        }
    }

    private void DrawBreakableSection()
    {
        EditorGUILayout.LabelField("5. Breakable", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(breakableProperty, new GUIContent("Breakable"));

        if (!breakableProperty.boolValue)
        {
            EditorGUILayout.HelpBox(
                "When Breakable is disabled, standing on the platform does not break it.",
                MessageType.None);
            return;
        }

        EditorGUILayout.PropertyField(breakDelayProperty, new GUIContent("Break Delay"));
        EditorGUILayout.PropertyField(topTriggerHeightProperty, new GUIContent("Top Trigger Height"));
        EditorGUILayout.PropertyField(respawnsProperty, new GUIContent("Respawns"));

        if (respawnsProperty.boolValue)
            EditorGUILayout.PropertyField(respawnDelayProperty, new GUIContent("Respawn Delay"));

        EditorGUILayout.PropertyField(playerDetectionMaskProperty, new GUIContent("Player Detection Mask"));
        EditorGUILayout.HelpBox(
            "A player entering the top detection volume schedules the break. Respawn restores the initial pose and restarts traversal.",
            MessageType.Info);
    }

    private PlatformController.PlatformMotionMode GetMotionMode()
    {
        return (PlatformController.PlatformMotionMode)motionModeProperty.enumValueIndex;
    }

    public static void PrepareAudioSources(PlatformController platform)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || platform == null) return;
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Prepare Platform Audio Sources");
        var data = new SerializedObject(platform);
        var moving = PrepareSource(platform, data.FindProperty("movementAudioSource"), PlatformController.MovementAudioObjectName, true, null, null);
        var start = PrepareSource(platform, data.FindProperty("startAudioSource"), PlatformController.StartAudioObjectName, false, moving, null);
        PrepareSource(platform, data.FindProperty("stopAudioSource"), PlatformController.StopAudioObjectName, false, moving, start);
        data.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(platform);
        if (platform.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(platform.gameObject.scene);
        Undo.CollapseUndoOperations(group);
    }

    private static AudioSource PrepareSource(PlatformController platform, SerializedProperty property, string name, bool loop, AudioSource other, AudioSource another)
    {
        var source = property.objectReferenceValue as AudioSource;
        if (source == null || source == other || source == another)
        {
            var anchor = platform.transform.Find(name);
            if (anchor == null)
            {
                var root = new GameObject(name); Undo.RegisterCreatedObjectUndo(root, "Create Platform Audio Source");
                Undo.SetTransformParent(root.transform, platform.transform, "Parent Platform Audio Source");
                root.transform.localPosition = Vector3.zero; anchor = root.transform;
            }
            source = anchor.GetComponent<AudioSource>();
            if (source == null || source == other || source == another) source = Undo.AddComponent<AudioSource>(anchor.gameObject);
            property.objectReferenceValue = source;
        }
        Undo.RecordObject(source, "Prepare Platform Audio Source");
        source.playOnAwake = false; source.loop = loop; source.dopplerLevel = 0f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(source);
        return source;
    }

    private PlatformController.PlatformActivationMode GetActivationMode()
    {
        return (PlatformController.PlatformActivationMode)activationModeProperty.enumValueIndex;
    }
}
