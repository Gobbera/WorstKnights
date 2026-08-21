using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DoorController))]
public class DoorControllerEditor : Editor
{
    private const float StartupButtonSpacing = 6f;

    private SerializedProperty doorNameProperty;
    private SerializedProperty movingPartProperty;
    private SerializedProperty photonViewProperty;
    private SerializedProperty prototypeLocalOnlyProperty;
    private SerializedProperty startupStateProperty;
    private SerializedProperty autoOpenOnUnlockProperty;
    private SerializedProperty closeWhenSignalTurnsOffProperty;
    private SerializedProperty relockWhenSignalTurnsOffProperty;
    private SerializedProperty motionModeProperty;
    private SerializedProperty rotatePivotProperty;
    private SerializedProperty openLocalEulerAnglesProperty;
    private SerializedProperty openLocalPositionOffsetProperty;
    private SerializedProperty moveDurationProperty;
    private SerializedProperty destroyDelayProperty;
    private SerializedProperty moveCurveProperty;
    private SerializedProperty lockModeProperty;
    private SerializedProperty requiredKeyItemProperty;
    private SerializedProperty requiredPasscodeProperty;
    private SerializedProperty requiredSignalsProperty;
    private SerializedProperty signalRequirementProperty;
    private SerializedProperty stayOpenAfterFirstSignalOpenProperty;

    private void OnEnable()
    {
        doorNameProperty = serializedObject.FindProperty("doorName");
        movingPartProperty = serializedObject.FindProperty("movingPart");
        photonViewProperty = serializedObject.FindProperty("photonView");
        prototypeLocalOnlyProperty = serializedObject.FindProperty("prototypeLocalOnly");
        startupStateProperty = serializedObject.FindProperty("startupState");
        autoOpenOnUnlockProperty = serializedObject.FindProperty("autoOpenOnUnlock");
        closeWhenSignalTurnsOffProperty = serializedObject.FindProperty("closeWhenSignalTurnsOff");
        relockWhenSignalTurnsOffProperty = serializedObject.FindProperty("relockWhenSignalTurnsOff");
        motionModeProperty = serializedObject.FindProperty("motionMode");
        rotatePivotProperty = serializedObject.FindProperty("rotatePivot");
        openLocalEulerAnglesProperty = serializedObject.FindProperty("openLocalEulerAngles");
        openLocalPositionOffsetProperty = serializedObject.FindProperty("openLocalPositionOffset");
        moveDurationProperty = serializedObject.FindProperty("moveDuration");
        destroyDelayProperty = serializedObject.FindProperty("destroyDelay");
        moveCurveProperty = serializedObject.FindProperty("moveCurve");
        lockModeProperty = serializedObject.FindProperty("lockMode");
        requiredKeyItemProperty = serializedObject.FindProperty("requiredKeyItem");
        requiredPasscodeProperty = serializedObject.FindProperty("requiredPasscode");
        requiredSignalsProperty = serializedObject.FindProperty("requiredSignals");
        signalRequirementProperty = serializedObject.FindProperty("signalRequirement");
        stayOpenAfterFirstSignalOpenProperty = serializedObject.FindProperty("stayOpenAfterFirstSignalOpen");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSetupSection();
        EditorGUILayout.Space();
        DrawStartupStateSection();
        EditorGUILayout.Space();
        DrawMotionSection();

        if (GetStartupState() == DoorController.DoorStartupState.StartsLocked)
        {
            EditorGUILayout.Space();
            DrawLockSection();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSetupSection()
    {
        EditorGUILayout.LabelField("1. Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(doorNameProperty, new GUIContent("Door Name"));
        EditorGUILayout.PropertyField(movingPartProperty, new GUIContent("Moving Part"));
        EditorGUILayout.PropertyField(photonViewProperty, new GUIContent("Photon View"));
        EditorGUILayout.PropertyField(prototypeLocalOnlyProperty, new GUIContent("Prototype Local Only"));
    }

    private void DrawStartupStateSection()
    {
        EditorGUILayout.LabelField("2. Startup State", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Escolha como a porta comeca na cena. Ela pode iniciar aberta, fechada sem trava, ou trancada.",
            MessageType.None);

        Rect buttonRowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        float buttonWidth = (buttonRowRect.width - (StartupButtonSpacing * 2f)) / 3f;
        Rect startsOpenRect = new Rect(buttonRowRect.x, buttonRowRect.y, buttonWidth, buttonRowRect.height);
        Rect startsClosedRect = new Rect(buttonRowRect.x + buttonWidth + StartupButtonSpacing, buttonRowRect.y, buttonWidth, buttonRowRect.height);
        Rect startsLockedRect = new Rect(buttonRowRect.x + ((buttonWidth + StartupButtonSpacing) * 2f), buttonRowRect.y, buttonWidth, buttonRowRect.height);

        DoorController.DoorStartupState startupState = GetStartupState();
        DrawStartupStateButton(startsOpenRect, "Starts Open", startupState == DoorController.DoorStartupState.StartsOpen, DoorController.DoorStartupState.StartsOpen);
        DrawStartupStateButton(startsClosedRect, "Starts Closed", startupState == DoorController.DoorStartupState.StartsClosed, DoorController.DoorStartupState.StartsClosed);
        DrawStartupStateButton(startsLockedRect, "Starts Locked", startupState == DoorController.DoorStartupState.StartsLocked, DoorController.DoorStartupState.StartsLocked);

        switch (startupState)
        {
            case DoorController.DoorStartupState.StartsLocked:
                EditorGUILayout.HelpBox(
                    "Com Starts Locked ativo, a porta entra fechada e as configuracoes de trava ficam disponiveis abaixo.",
                    MessageType.Info);
                break;

            case DoorController.DoorStartupState.StartsClosed:
                EditorGUILayout.HelpBox(
                    "Com Starts Closed ativo, a porta inicia fechada, mas livre para abrir sem chave, senha ou sinal.",
                    MessageType.None);
                break;

            default:
                EditorGUILayout.HelpBox(
                    "Com Starts Open ativo, a porta inicia aberta e as configuracoes de trava ficam ocultas para manter o authoring limpo.",
                    MessageType.None);
                break;
        }
    }

    private void DrawMotionSection()
    {
        EditorGUILayout.LabelField("3. Motion", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(motionModeProperty, new GUIContent("Motion Mode"));

        DoorController.DoorMotionMode motionMode = (DoorController.DoorMotionMode)motionModeProperty.enumValueIndex;
        switch (motionMode)
        {
            case DoorController.DoorMotionMode.Rotate:
                EditorGUILayout.PropertyField(rotatePivotProperty, new GUIContent("Rotate Pivot"));
                EditorGUILayout.PropertyField(openLocalEulerAnglesProperty, new GUIContent("Open Local Euler Angles"));
                EditorGUILayout.PropertyField(openLocalPositionOffsetProperty, new GUIContent("Open Local Position Offset"));
                EditorGUILayout.PropertyField(moveDurationProperty, new GUIContent("Move Duration"));
                EditorGUILayout.PropertyField(moveCurveProperty, new GUIContent("Move Curve"));
                EditorGUILayout.HelpBox(
                    "Se um Rotate Pivot for informado, a porta vai orbitar ao redor desse ponto e usar a orientacao dele como referencia do giro.",
                    MessageType.None);
                break;

            case DoorController.DoorMotionMode.Slide:
                EditorGUILayout.PropertyField(openLocalPositionOffsetProperty, new GUIContent("Open Local Position Offset"));
                EditorGUILayout.PropertyField(moveDurationProperty, new GUIContent("Move Duration"));
                EditorGUILayout.PropertyField(moveCurveProperty, new GUIContent("Move Curve"));
                break;

            case DoorController.DoorMotionMode.Destroy:
                EditorGUILayout.PropertyField(destroyDelayProperty, new GUIContent("Destroy Delay"));
                EditorGUILayout.HelpBox(
                    "No modo Destroy, o objeto apontado em Moving Part sera destruido quando a porta abrir.",
                    MessageType.Warning);
                break;
        }
    }

    private void DrawLockSection()
    {
        EditorGUILayout.LabelField("4. Lock", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(lockModeProperty, new GUIContent("Lock Mode"));
        EditorGUILayout.PropertyField(autoOpenOnUnlockProperty, new GUIContent("Auto Open On Unlock"));

        DoorController.DoorLockMode lockMode = (DoorController.DoorLockMode)lockModeProperty.enumValueIndex;
        switch (lockMode)
        {
            case DoorController.DoorLockMode.KeyItem:
                EditorGUILayout.PropertyField(requiredKeyItemProperty, new GUIContent("Required Key Item"));
                break;

            case DoorController.DoorLockMode.Passcode:
                EditorGUILayout.PropertyField(requiredPasscodeProperty, new GUIContent("Required Passcode"));
                break;

            case DoorController.DoorLockMode.SignalSource:
                EditorGUILayout.PropertyField(requiredSignalsProperty, new GUIContent("Required Signals"), includeChildren: true);
                EditorGUILayout.PropertyField(signalRequirementProperty, new GUIContent("Signal Requirement"));
                EditorGUILayout.PropertyField(stayOpenAfterFirstSignalOpenProperty, new GUIContent("Stay Open After First Open"));
                EditorGUILayout.PropertyField(closeWhenSignalTurnsOffProperty, new GUIContent("Close When Signal Turns Off"));
                EditorGUILayout.PropertyField(relockWhenSignalTurnsOffProperty, new GUIContent("Relock When Signal Turns Off"));

                if (stayOpenAfterFirstSignalOpenProperty.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Quando ativo, assim que a porta por sinal abrir pela primeira vez, ela permanece aberta para sempre e ignora futuros fechamentos.",
                        MessageType.Info);
                }
                break;

            default:
                EditorGUILayout.HelpBox(
                    "No modo None, a porta inicia trancada mas qualquer interacao valida destranca sem exigir chave, senha ou sinal.",
                    MessageType.None);
                break;
        }
    }

    private DoorController.DoorStartupState GetStartupState()
    {
        return (DoorController.DoorStartupState)startupStateProperty.enumValueIndex;
    }

    private void DrawStartupStateButton(Rect rect, string label, bool isActive, DoorController.DoorStartupState targetState)
    {
        Color previousBackgroundColor = GUI.backgroundColor;
        GUI.backgroundColor = isActive ? new Color(0.3f, 0.75f, 0.35f, 1f) : previousBackgroundColor;

        if (GUI.Button(rect, label))
            startupStateProperty.enumValueIndex = (int)targetState;

        GUI.backgroundColor = previousBackgroundColor;
    }
}
