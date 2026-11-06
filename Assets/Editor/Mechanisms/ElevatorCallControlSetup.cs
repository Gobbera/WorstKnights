using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ElevatorCallControlSetup
{
    [MenuItem("Tools/Mechanisms/Connect Lower Elevator Call Lever")]
    public static void SetupActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        var triggers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<InteractionTriggerSystem>(true)).ToArray();
        var main = triggers.Single(s => s.name.EndsWith("Elevator_01", StringComparison.Ordinal));
        var call = triggers.Single(s => s.name.EndsWith("Elevator_02", StringComparison.Ordinal));
        Configure(call, main);
        Selection.activeGameObject = call.gameObject;
    }

    // The shortcut resolves this scene's named controls; the runtime target accepts explicit generic references.
    public static InteractionTriggerStateTarget Configure(InteractionTriggerSystem call, InteractionTriggerSystem main)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Configure controls outside Play Mode.");
        if (call == null || main == null || call == main || call.gameObject.scene != main.gameObject.scene)
            throw new ArgumentException("Assign two different triggers in the same scene.");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Configure lower elevator call control");
        var target = call.GetComponent<InteractionTriggerStateTarget>();
        if (target == null) target = Undo.AddComponent<InteractionTriggerStateTarget>(call.gameObject);
        Undo.RecordObjects(new UnityEngine.Object[] { call, target }, "Connect elevator call control");
        InteractionTriggerAuthoringUtility.Set(target, "controlledTrigger", main);
        InteractionTriggerAuthoringUtility.Set(target, "command", (int)InteractionTriggerStateCommand.SetB);
        var data = new SerializedObject(call);
        data.FindProperty("linkedSystem").objectReferenceValue = target;
        data.FindProperty("activationMode").enumValueIndex = (int)InteractionTriggerMode.Momentary;
        data.FindProperty("startsActive").boolValue = false;
        data.FindProperty("interactionPrompt").stringValue = "[{key}] Call Elevator";
        data.ApplyModifiedProperties();
        var copiedRouter = call.GetComponent<InteractionTriggerRouter>();
        if (copiedRouter != null) Undo.DestroyObjectImmediate(copiedRouter);
        EditorUtility.SetDirty(call); EditorUtility.SetDirty(target);
        PrefabUtility.RecordPrefabInstancePropertyModifications(call);
        EditorSceneManager.MarkSceneDirty(call.gameObject.scene);
        Undo.CollapseUndoOperations(group);
        return target;
    }
}
