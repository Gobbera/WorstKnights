using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InteractionTriggerRoutingSetup
{
    [MenuItem("Tools/Mechanisms/Connect Statue Puzzle To Platform")]
    public static void SetupActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        var components = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
        var puzzle = components.OfType<StatuePuzzleController>().Single();
        var platform = components.OfType<PlatformController>().Single();
        var system = components.OfType<InteractionTriggerSystem>().Single(s => ReferencesPuzzle(s, puzzle));
        Configure(system, puzzle, platform);
        Selection.activeGameObject = system.gameObject;
    }

    public static bool ReferencesPuzzle(InteractionTriggerSystem system, StatuePuzzleController puzzle)
    {
        if (system.LinkedSystem == puzzle) return true;
        if (system.LinkedSystem is not InteractionTriggerRouter router) return false;
        var entries = new SerializedObject(router).FindProperty("routes");
        for (int i = 0; i < entries.arraySize; i++)
            if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("target").objectReferenceValue == puzzle) return true;
        return false;
    }

    // Scene convenience only. Runtime routing has no dependency on puzzles or platforms.
    public static InteractionTriggerRouter Configure(InteractionTriggerSystem system, StatuePuzzleController puzzle, PlatformController platform)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Configure routing outside Play Mode.");
        if (system == null || puzzle == null || platform == null) throw new ArgumentException("Assign a trigger, puzzle and platform.");
        if (system.gameObject.scene != puzzle.gameObject.scene || system.gameObject.scene != platform.gameObject.scene)
            throw new ArgumentException("The trigger, puzzle and platform must belong to the same scene.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Connect puzzle and platform trigger routes");
        var router = system.GetComponent<InteractionTriggerRouter>();
        if (router == null) router = Undo.AddComponent<InteractionTriggerRouter>(system.gameObject);
        var signal = platform.GetComponent<DoorSignalSource>();
        if (signal == null) signal = Undo.AddComponent<DoorSignalSource>(platform.gameObject);
        var adapter = platform.GetComponent<SignalInteractionTriggerTarget>();
        if (adapter == null) adapter = Undo.AddComponent<SignalInteractionTriggerTarget>(platform.gameObject);
        Undo.RecordObjects(new UnityEngine.Object[] { system, router, signal, adapter, platform, puzzle }, "Configure trigger routing");
        platform.enabled = true;

        var sourceData = new SerializedObject(system);
        var routerData = new SerializedObject(router);
        routerData.FindProperty("localOnly").boolValue = sourceData.FindProperty("localOnly").boolValue;
        routerData.ApplyModifiedProperties();
        InteractionTriggerAuthoringUtility.Set(adapter, "signalSource", signal);
        InteractionTriggerAuthoringUtility.Set(adapter, "action", (int)SignalTriggerAction.FollowState);
        var platformData = new SerializedObject(platform);
        var signalData = new SerializedObject(signal);
        signalData.FindProperty("prototypeLocalOnly").boolValue = platformData.FindProperty("prototypeLocalOnly").boolValue;
        signalData.ApplyModifiedProperties();
        platformData.FindProperty("activationMode").enumValueIndex = (int)PlatformController.PlatformActivationMode.SignalSource;
        platformData.FindProperty("motionMode").enumValueIndex = (int)PlatformController.PlatformMotionMode.TwoPosition;
        var signals = platformData.FindProperty("activationSignals");
        bool alreadyLinked = false;
        for (int i = 0; i < signals.arraySize; i++) alreadyLinked |= signals.GetArrayElementAtIndex(i).objectReferenceValue == signal;
        if (!alreadyLinked) { signals.arraySize++; signals.GetArrayElementAtIndex(signals.arraySize - 1).objectReferenceValue = signal; }
        platformData.ApplyModifiedProperties();

        routerData.Update();
        var routes = routerData.FindProperty("routes");
        int puzzleRoute = EnsureRoute(routes, "Check Statues", puzzle);
        int platformRoute = EnsureRoute(routes, "Control Platform", adapter);
        var checkRoute = routes.GetArrayElementAtIndex(puzzleRoute);
        checkRoute.FindPropertyRelative("overrideActivationMode").boolValue = true;
        checkRoute.FindPropertyRelative("activationMode").enumValueIndex = (int)InteractionTriggerMode.Momentary;
        var controlRoute = routes.GetArrayElementAtIndex(platformRoute);
        controlRoute.FindPropertyRelative("overrideActivationMode").boolValue = true;
        controlRoute.FindPropertyRelative("activationMode").enumValueIndex = (int)InteractionTriggerMode.AlternateAB;
        routerData.FindProperty("initialRoute").intValue = puzzleRoute;
        routerData.ApplyModifiedProperties();
        InteractionTriggerAuthoringUtility.Set(system, "linkedSystem", router);

        // The signal now gates motion. A legacy enable/disable output conflicts with that connection.
        for (int i = puzzle.OnPuzzleSolved.GetPersistentEventCount() - 1; i >= 0; i--)
            if (puzzle.OnPuzzleSolved.GetPersistentTarget(i) == platform && puzzle.OnPuzzleSolved.GetPersistentMethodName(i) == "set_enabled")
                UnityEventTools.RemovePersistentListener(puzzle.OnPuzzleSolved, i);

        int existing = -1;
        for (int i = 0; i < puzzle.OnPuzzleSolved.GetPersistentEventCount(); i++)
            if (puzzle.OnPuzzleSolved.GetPersistentTarget(i) == router
                && (puzzle.OnPuzzleSolved.GetPersistentMethodName(i) == nameof(router.SelectRoute)
                    || puzzle.OnPuzzleSolved.GetPersistentMethodName(i) == nameof(router.SelectRouteAndActivate))) existing = i;
        if (existing < 0) UnityEventTools.AddIntPersistentListener(puzzle.OnPuzzleSolved, router.SelectRouteAndActivate, platformRoute);
        else UnityEventTools.RegisterIntPersistentListener(puzzle.OnPuzzleSolved, existing, router.SelectRouteAndActivate, platformRoute);

        PreparePlatformAudio(platform);

        foreach (var component in new Component[] { system, router, signal, adapter, platform, puzzle })
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
        EditorSceneManager.MarkSceneDirty(system.gameObject.scene);
        Undo.CollapseUndoOperations(group);
        return router;
    }

    private static void PreparePlatformAudio(PlatformController platform)
    {
        var data = new SerializedObject(platform);
        if (data.FindProperty("movementAudioSource").objectReferenceValue != null) return;
        var anchor = platform.transform.Find(PlatformController.MovementAudioObjectName);
        if (anchor == null)
        {
            var root = new GameObject(PlatformController.MovementAudioObjectName);
            Undo.RegisterCreatedObjectUndo(root, "Create platform movement audio");
            Undo.SetTransformParent(root.transform, platform.transform, "Parent platform audio");
            root.transform.localPosition = Vector3.zero;
            anchor = root.transform;
        }
        var source = anchor.GetComponent<AudioSource>();
        if (source == null) source = Undo.AddComponent<AudioSource>(anchor.gameObject);
        Undo.RecordObject(source, "Prepare platform movement audio");
        source.playOnAwake = false; source.loop = true; source.spatialBlend = 1f; source.dopplerLevel = 0f;
        data.FindProperty("movementAudioSource").objectReferenceValue = source;
        data.ApplyModifiedProperties();
    }

    private static int EnsureRoute(SerializedProperty routes, string name, MonoBehaviour target)
    {
        for (int i = 0; i < routes.arraySize; i++)
            if (routes.GetArrayElementAtIndex(i).FindPropertyRelative("target").objectReferenceValue == target) return i;
        int index = routes.arraySize++;
        var entry = routes.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("name").stringValue = name;
        entry.FindPropertyRelative("target").objectReferenceValue = target;
        return index;
    }
}
