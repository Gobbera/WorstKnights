using UnityEditor;
using UnityEngine;

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
                "Static deixa o objeto parado. Troque o Motion Mode para PingPong ou OneWay para usar a rota configurada.",
                MessageType.None);
            return;
        }

        DrawMovementPointsSection();
        EditorGUILayout.PropertyField(carryPlayersProperty, new GUIContent("Carry Players"));

        switch (motionMode)
        {
            case PlatformController.PlatformMotionMode.OneWay:
                EditorGUILayout.HelpBox(
                    "OneWay percorre a rota do ponto inicial ate o ultimo ponto uma unica vez e para no destino final.",
                    MessageType.Info);
                break;

            default:
                EditorGUILayout.HelpBox(
                    "PingPong percorre a rota ate o ultimo ponto e volta continuamente enquanto a plataforma estiver ativa.",
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
                "Adicione pelo menos um ponto para criar o trajeto da plataforma.",
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
                    "Use um vetor como (1, 0, 1), (-1, 1, 0) ou qualquer outra diagonal desejada. O sistema normaliza esse vetor automaticamente.",
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
                    "Player On Top usa o proprio volume superior da plataforma como gatilho. Quando o jogador pisa nela, o movimento e ativado.",
                    MessageType.Info);
                break;

            case PlatformController.PlatformActivationMode.SignalSource:
                EditorGUILayout.PropertyField(activationSignalsProperty, new GUIContent("Activation Signals"), includeChildren: true);
                EditorGUILayout.PropertyField(signalRequirementProperty, new GUIContent("Signal Requirement"));
                EditorGUILayout.HelpBox(
                    "Signal Source reaproveita o mesmo ecossistema das portas. Vincule aqui os Door Signal Sources que podem ser ativados por alavancas ou trigger zones.",
                    MessageType.Info);
                break;

            default:
                EditorGUILayout.HelpBox(
                    "Always Active mantem a plataforma sempre liberada para se mover conforme o Motion Mode.",
                    MessageType.None);
                break;
        }

        if (GetMotionMode() == PlatformController.PlatformMotionMode.OneWay)
        {
            EditorGUILayout.HelpBox(
                "Com OneWay, basta a ativacao acontecer uma vez para a plataforma completar toda a rota e permanecer no ultimo ponto.",
                MessageType.Warning);
        }
    }

    private void DrawBreakableSection()
    {
        EditorGUILayout.LabelField("4. Breakable", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(breakableProperty, new GUIContent("Breakable"));

        if (!breakableProperty.boolValue)
        {
            EditorGUILayout.HelpBox(
                "Quando Breakable esta desligado, a plataforma nao reage ao jogador em cima dela.",
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
            "A quebra ativa quando um Player entra no volume sobre a superficie da plataforma. Se Respawns estiver ativo, ela reaparece no ponto inicial e reinicia o movimento.",
            MessageType.Info);
    }

    private PlatformController.PlatformMotionMode GetMotionMode()
    {
        return (PlatformController.PlatformMotionMode)motionModeProperty.enumValueIndex;
    }

    private PlatformController.PlatformActivationMode GetActivationMode()
    {
        return (PlatformController.PlatformActivationMode)activationModeProperty.enumValueIndex;
    }
}
