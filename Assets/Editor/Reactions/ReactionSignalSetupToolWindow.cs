using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ReactionSignalSetupToolWindow : EditorWindow
{
    [MenuItem("Tools/Reactions/Reaction Signal Setup Tool")]
    private static void OpenWindow()
    {
        GetWindow<ReactionSignalSetupToolWindow>("Reaction Setup");
    }

    public static void OpenForTarget(GameObject targetObject)
    {
        if (targetObject != null)
            Selection.activeGameObject = targetObject;

        OpenWindow();
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Reaction Signal Setup Tool", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Selecione um GameObject na Hierarchy e use esta ferramenta para adicionar o ReactionSignalReceiver e aplicar os setups rapidos do sistema de reacoes.",
            MessageType.None);

        GameObject targetObject = Selection.activeGameObject;
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Selected Object", targetObject, typeof(GameObject), true);

        if (targetObject == null)
        {
            EditorGUILayout.HelpBox("Nenhum objeto selecionado.", MessageType.Warning);
            return;
        }

        DrawReceiverSection(targetObject);
        EditorGUILayout.Space();
        DrawSetupButtons(targetObject);
        EditorGUILayout.Space();
        DrawDetectedSignals(targetObject);
    }

    private static void DrawReceiverSection(GameObject targetObject)
    {
        EditorGUILayout.LabelField("Receiver", EditorStyles.boldLabel);
        ReactionSignalReceiver receiver = targetObject.GetComponent<ReactionSignalReceiver>();

        if (receiver != null)
        {
            EditorGUILayout.HelpBox("ReactionSignalReceiver detectado no objeto selecionado.", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox("O objeto selecionado ainda nao possui ReactionSignalReceiver.", MessageType.Warning);
        if (GUILayout.Button("Add ReactionSignalReceiver"))
            ReactionSignalSetupUtility.EnsureReceiver(targetObject);
    }

    private static void DrawSetupButtons(GameObject targetObject)
    {
        EditorGUILayout.LabelField("Quick Setup", EditorStyles.boldLabel);

        DrawSetupButton(
            "Setup Destructible",
            "Adiciona ReactionSignalEmitter e DestructibleReactionSignalBridge, com os sinais Damaged e Destroyed.",
            ReactionSignalSetupUtility.CanSetupDestructible(targetObject),
            () => ReactionSignalSetupUtility.SetupDestructible(targetObject));

        DrawSetupButton(
            "Setup Door",
            "Adiciona ReactionSignalEmitter e DoorReactionSignalBridge, com os sinais Opened, Closed, Locked e Unlocked.",
            ReactionSignalSetupUtility.CanSetupDoor(targetObject),
            () => ReactionSignalSetupUtility.SetupDoor(targetObject));

        DrawSetupButton(
            "Setup Impact",
            "Adiciona ReactionSignalEmitter e ImpactReactionSignalBridge, com o sinal Hit.",
            ReactionSignalSetupUtility.CanSetupImpact(targetObject),
            () => ReactionSignalSetupUtility.SetupImpact(targetObject));

        DrawSetupButton(
            "Setup Trigger Volume",
            "Adiciona ReactionSignalEmitter e TriggerVolumeReactionSignalBridge, com os sinais Entered e Exited.",
            ReactionSignalSetupUtility.CanSetupTriggerVolume(targetObject),
            () => ReactionSignalSetupUtility.SetupTriggerVolume(targetObject));

        DrawSetupButton(
            "Setup Collision",
            "Adiciona ReactionSignalEmitter e CollisionReactionSignalBridge, com o sinal Impact.",
            ReactionSignalSetupUtility.CanSetupCollision(targetObject),
            () => ReactionSignalSetupUtility.SetupCollision(targetObject));
    }

    private static void DrawSetupButton(string label, string description, bool enabled, System.Action onClick)
    {
        using (new EditorGUI.DisabledScope(!enabled))
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button(label))
                onClick?.Invoke();
            EditorGUILayout.EndVertical();
        }
    }

    private static void DrawDetectedSignals(GameObject targetObject)
    {
        EditorGUILayout.LabelField("Detected Signals", EditorStyles.boldLabel);
        List<string> detectedSignals = ReactionSignalSetupUtility.CollectDetectedSignals(targetObject);

        if (detectedSignals.Count == 0)
        {
            EditorGUILayout.HelpBox("Nenhum sinal detectado ou sugerido ainda neste objeto.", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(string.Join(", ", detectedSignals), MessageType.None);
    }
}
