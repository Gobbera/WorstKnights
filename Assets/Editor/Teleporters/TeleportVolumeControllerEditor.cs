using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TeleportVolumeController))]
public class TeleportVolumeControllerEditor : Editor
{
    private SerializedProperty teleporterNameProperty;
    private SerializedProperty routeModeProperty;
    private SerializedProperty destinationPointProperty;
    private SerializedProperty linkedReturnTeleporterProperty;
    private SerializedProperty activationModeProperty;
    private SerializedProperty requiredStayDurationProperty;
    private SerializedProperty allowPlayersProperty;
    private SerializedProperty allowEnemiesProperty;
    private SerializedProperty allowItemsProperty;
    private SerializedProperty allowOtherObjectsProperty;
    private SerializedProperty detectionMaskProperty;

    private void OnEnable()
    {
        teleporterNameProperty = serializedObject.FindProperty("teleporterName");
        routeModeProperty = serializedObject.FindProperty("routeMode");
        destinationPointProperty = serializedObject.FindProperty("destinationPoint");
        linkedReturnTeleporterProperty = serializedObject.FindProperty("linkedReturnTeleporter");
        activationModeProperty = serializedObject.FindProperty("activationMode");
        requiredStayDurationProperty = serializedObject.FindProperty("requiredStayDuration");
        allowPlayersProperty = serializedObject.FindProperty("allowPlayers");
        allowEnemiesProperty = serializedObject.FindProperty("allowEnemies");
        allowItemsProperty = serializedObject.FindProperty("allowItems");
        allowOtherObjectsProperty = serializedObject.FindProperty("allowOtherObjects");
        detectionMaskProperty = serializedObject.FindProperty("detectionMask");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSetupSection();
        EditorGUILayout.Space();
        DrawRouteSection();
        EditorGUILayout.Space();
        DrawActivationSection();
        EditorGUILayout.Space();
        DrawTargetSection();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSetupSection()
    {
        EditorGUILayout.LabelField("1. Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(teleporterNameProperty, new GUIContent("Teleporter Name"));
        EditorGUILayout.PropertyField(destinationPointProperty, new GUIContent("Destination Point"));
        EditorGUILayout.HelpBox(
            "O collider no mesmo objeto define a area do teleporte e sera mantido como Trigger automaticamente.",
            MessageType.None);

        if (destinationPointProperty.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox(
                "Defina um Destination Point para indicar exatamente onde o alvo vai reaparecer.",
                MessageType.Warning);
        }
    }

    private void DrawRouteSection()
    {
        EditorGUILayout.LabelField("2. Route", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(routeModeProperty, new GUIContent("Route Mode"));

        TeleportVolumeController.TeleportRouteMode routeMode = GetRouteMode();
        if (routeMode == TeleportVolumeController.TeleportRouteMode.LinkedTwoWay)
        {
            EditorGUILayout.PropertyField(linkedReturnTeleporterProperty, new GUIContent("Return Teleporter"));
            EditorGUILayout.HelpBox(
                "Para ida e volta, crie um segundo Teleport Volume no destino, configure nele o Destination Point de retorno e vincule os dois entre si neste campo.",
                MessageType.Info);

            if (linkedReturnTeleporterProperty.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "Sem o Return Teleporter preenchido, este volume continuara funcionando como ida simples.",
                    MessageType.Warning);
            }

            return;
        }

        EditorGUILayout.HelpBox(
            "One Way envia o alvo para o Destination Point e encerra o fluxo nesse volume.",
            MessageType.None);
    }

    private void DrawActivationSection()
    {
        EditorGUILayout.LabelField("3. Activation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(activationModeProperty, new GUIContent("Activation Mode"));

        switch (GetActivationMode())
        {
            case TeleportVolumeController.TeleportActivationMode.TimedStay:
                EditorGUILayout.PropertyField(requiredStayDurationProperty, new GUIContent("Stay Duration"));
                EditorGUILayout.HelpBox(
                    "Timed Stay exige que o alvo permaneça dentro do colisor pelo tempo configurado antes de teleportar.",
                    MessageType.Info);
                break;

            default:
                EditorGUILayout.HelpBox(
                    "Instant teleporta assim que o alvo entra na area.",
                    MessageType.None);
                break;
        }
    }

    private void DrawTargetSection()
    {
        EditorGUILayout.LabelField("4. Targets", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(allowPlayersProperty, new GUIContent("Allow Players"));
        EditorGUILayout.PropertyField(allowEnemiesProperty, new GUIContent("Allow Enemies"));
        EditorGUILayout.PropertyField(allowItemsProperty, new GUIContent("Allow Items"));
        EditorGUILayout.PropertyField(allowOtherObjectsProperty, new GUIContent("Allow Other Objects"));
        EditorGUILayout.PropertyField(detectionMaskProperty, new GUIContent("Detection Mask"));

        if (!allowPlayersProperty.boolValue
            && !allowEnemiesProperty.boolValue
            && !allowItemsProperty.boolValue
            && !allowOtherObjectsProperty.boolValue)
        {
            EditorGUILayout.HelpBox(
                "Nenhum alvo esta habilitado. Assim o teleporter nao afetara nada ate voce marcar pelo menos uma categoria.",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            "Items equipados sao ignorados para nao separar o item do jogador durante o teleporte.",
            MessageType.None);
    }

    private TeleportVolumeController.TeleportRouteMode GetRouteMode()
    {
        return (TeleportVolumeController.TeleportRouteMode)routeModeProperty.enumValueIndex;
    }

    private TeleportVolumeController.TeleportActivationMode GetActivationMode()
    {
        return (TeleportVolumeController.TeleportActivationMode)activationModeProperty.enumValueIndex;
    }
}
