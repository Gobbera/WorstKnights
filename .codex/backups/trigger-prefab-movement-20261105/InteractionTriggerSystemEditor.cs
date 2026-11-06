using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(InteractionTriggerSystem))]
public sealed class InteractionTriggerSystemEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        var system = (InteractionTriggerSystem)target;
        Field("triggerType", "Type");
        var type = (InteractionTriggerType)serializedObject.FindProperty("triggerType").enumValueIndex;
        var prefabProperty = serializedObject.FindProperty("mechanismPrefab");
        var prefabs = InteractionTriggerAuthoringUtility.FindPrefabs(type);
        string[] names = new[] { "Selecione um prefab..." }.Concat(prefabs.Select(p => p.name)).ToArray();
        int selected = Array.IndexOf(prefabs, prefabProperty.objectReferenceValue) + 1;
        int choice = EditorGUILayout.Popup("Prefab", Mathf.Max(0, selected), names);
        if (choice != selected) prefabProperty.objectReferenceValue = choice == 0 ? null : prefabs[choice - 1];
        var selectedPrefab = prefabProperty.objectReferenceValue as GameObject;
        if (selectedPrefab != null && !prefabs.Contains(selectedPrefab))
            EditorGUILayout.HelpBox("Escolha um prefab da categoria selecionada.", MessageType.Warning);
        using (new EditorGUI.DisabledScope(Application.isPlaying || selectedPrefab == null || !prefabs.Contains(selectedPrefab)))
        {
            if (GUILayout.Button("Aplicar prefab ao objeto"))
            {
                serializedObject.ApplyModifiedProperties();
                InteractionTriggerAuthoringUtility.BuildMechanism(system);
                serializedObject.Update();
            }
        }
        using (new EditorGUI.DisabledScope(selectedPrefab == null))
            if (GUILayout.Button("Editar prefab (audio e mecanismo)")) AssetDatabase.OpenAsset(selectedPrefab);
        if (system.Mechanism == null || system.Mechanism.Type != type)
            EditorGUILayout.HelpBox("Aplique um prefab para montar o mecanismo correspondente ao Type.", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sistema acionado", EditorStyles.boldLabel);
        Field("linkedSystem", "Linked System");
        var linked = serializedObject.FindProperty("linkedSystem").objectReferenceValue as MonoBehaviour;
        if (linked != null && linked is not IInteractionTriggerTarget)
            EditorGUILayout.HelpBox("Esse componente nao recebe Interaction Triggers. Use StatuePuzzleController, DoorInteractionTriggerTarget ou deixe vazio e configure Outputs.", MessageType.Error);
        Field("waitForLinkedSystem", "Aguardar sistema / cooldown");
        Field("interactionCooldown", "Intervalo entre usos");

        EditorGUILayout.Space();
        if (type == InteractionTriggerType.PressureButton)
        {
            EditorGUILayout.LabelField("Ocupacao do Pressure Button", EditorStyles.boldLabel);
            Field("pressureFilter", "Aceitar");
            if (serializedObject.FindProperty("pressureFilter").enumValueIndex == (int)PressureTriggerFilter.SpecificObject) Field("specificObject", "Objeto especifico");
            Field("occupancyMask", "Layers aceitas");
            Field("detectionHeight", "Altura da deteccao");
            Field("footTolerance", "Tolerancia de contato");
            EditorGUILayout.HelpBox("Objetos = corpos fisicos dinamicos sem PlayerHealth/EnemyHealth. Anything combina jogadores vivos, inimigos vivos e esses objetos; pisos e paredes nao pressionam. Specific Object aceita os colliders do objeto e de seus filhos.", MessageType.None);
        }
        else
        {
            EditorGUILayout.LabelField("Ativacao", EditorStyles.boldLabel);
            Field("activationMode", "Mode");
            var mode = (InteractionTriggerMode)serializedObject.FindProperty("activationMode").enumValueIndex;
            if (mode == InteractionTriggerMode.Toggle || mode == InteractionTriggerMode.AlternateAB) Field("startsActive", mode == InteractionTriggerMode.AlternateAB ? "Iniciar em B" : "Iniciar ligado");
            if (mode == InteractionTriggerMode.Momentary) Field("momentaryDuration", "Tempo pressionado");
            Field("interactionPrompt", "Prompt");
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Movimento", EditorStyles.boldLabel);
        if (type == InteractionTriggerType.Lever)
        {
            Field("leverAngle", "Angulo ativo");
            Field("rotationSpeed", "Velocidade angular");
        }
        else
        {
            Field("pressDistance", "Deslocamento (metros)");
            Field("movementSpeed", "Velocidade (m/s)");
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Outputs (autoridade)", EditorStyles.boldLabel);
        Field("onUsed"); Field("onActivated"); Field("onDeactivated");
        if (type != InteractionTriggerType.PressureButton && serializedObject.FindProperty("activationMode").enumValueIndex == (int)InteractionTriggerMode.AlternateAB)
        { Field("onStateA"); Field("onStateB"); }
        Field("localOnly");
        serializedObject.ApplyModifiedProperties();
        if (Application.isPlaying)
            EditorGUILayout.HelpBox("Estado: " + (system.IsActive ? "ativo / B" : "inativo / A") + "; ocupado: " + system.IsBusy + "; ocupantes: " + system.OccupantCount, MessageType.Info);
    }
    private void Field(string name, string label = null)
    {
        var property = serializedObject.FindProperty(name);
        if (label == null) EditorGUILayout.PropertyField(property, true);
        else EditorGUILayout.PropertyField(property, new GUIContent(label), true);
    }
}
