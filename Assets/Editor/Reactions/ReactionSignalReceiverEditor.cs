using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ReactionSignalReceiver))]
public class ReactionSignalReceiverEditor : Editor
{
    private SerializedProperty receiverNameProperty;
    private SerializedProperty signalEntriesProperty;
    private ReactionSignalReceiver receiverTarget;

    private void OnEnable()
    {
        receiverNameProperty = serializedObject.FindProperty("receiverName");
        signalEntriesProperty = serializedObject.FindProperty("signalEntries");
        receiverTarget = target as ReactionSignalReceiver;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSetupSection();
        EditorGUILayout.Space();
        DrawEntriesSection();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSetupSection()
    {
        EditorGUILayout.LabelField("1. Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(receiverNameProperty, new GUIContent("Receiver Name"));
        EditorGUILayout.HelpBox(
            "Este componente guarda as reacoes por Signal Id. Os setups rapidos ficam na ferramenta de editor Reaction Signal Setup Tool.",
            MessageType.None);

        if (receiverTarget != null && GUILayout.Button("Open Reaction Signal Setup Tool"))
            ReactionSignalSetupToolWindow.OpenForTarget(receiverTarget.gameObject);
    }

    private void DrawEntriesSection()
    {
        EditorGUILayout.LabelField("2. Signal Entries", EditorStyles.boldLabel);

        if (signalEntriesProperty.arraySize == 0)
        {
            EditorGUILayout.HelpBox(
                "Adicione pelo menos uma entrada para definir qual sinal dispara qual feedback.",
                MessageType.Warning);
        }

        int removeIndex = -1;
        for (int i = 0; i < signalEntriesProperty.arraySize; i++)
        {
            SerializedProperty entryProperty = signalEntriesProperty.GetArrayElementAtIndex(i);
            SerializedProperty signalIdProperty = entryProperty.FindPropertyRelative("signalId");
            SerializedProperty feedbackOriginProperty = entryProperty.FindPropertyRelative("feedbackOrigin");
            SerializedProperty audioCueProperty = entryProperty.FindPropertyRelative("audioCue");
            SerializedProperty effectPrefabProperty = entryProperty.FindPropertyRelative("effectPrefab");
            SerializedProperty effectLifetimeProperty = entryProperty.FindPropertyRelative("effectLifetime");
            SerializedProperty onSignalReceivedProperty = entryProperty.FindPropertyRelative("onSignalReceived");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Entry {i + 1}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                removeIndex = i;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(signalIdProperty, new GUIContent("Signal Id"));
            EditorGUILayout.PropertyField(feedbackOriginProperty, new GUIContent("Feedback Origin"));
            EditorGUILayout.PropertyField(audioCueProperty, new GUIContent("Audio Cue"));
            EditorGUILayout.PropertyField(effectPrefabProperty, new GUIContent("Effect Prefab"));
            if (effectPrefabProperty.objectReferenceValue != null)
                EditorGUILayout.PropertyField(effectLifetimeProperty, new GUIContent("Effect Lifetime"));
            EditorGUILayout.PropertyField(onSignalReceivedProperty, new GUIContent("On Signal Received"));

            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
            signalEntriesProperty.DeleteArrayElementAtIndex(removeIndex);

        if (GUILayout.Button("Add Signal Entry"))
            AddSignalEntry("Hit");
    }

    private void AddSignalEntry(string signalId)
    {
        int newIndex = signalEntriesProperty.arraySize;
        signalEntriesProperty.InsertArrayElementAtIndex(newIndex);

        SerializedProperty entryProperty = signalEntriesProperty.GetArrayElementAtIndex(newIndex);
        entryProperty.FindPropertyRelative("signalId").stringValue = signalId;
        entryProperty.FindPropertyRelative("feedbackOrigin").objectReferenceValue = null;
        entryProperty.FindPropertyRelative("audioCue").objectReferenceValue = null;
        entryProperty.FindPropertyRelative("effectPrefab").objectReferenceValue = null;
        entryProperty.FindPropertyRelative("effectLifetime").floatValue = 5f;

        SerializedProperty callsProperty = entryProperty
            .FindPropertyRelative("onSignalReceived")
            ?.FindPropertyRelative("m_PersistentCalls.m_Calls");
        if (callsProperty != null)
            callsProperty.ClearArray();
    }
}
