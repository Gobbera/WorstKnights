using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MovementVolumeController))]
public class MovementVolumeControllerEditor : Editor
{
    private SerializedProperty volumeNameProperty;
    private SerializedProperty effectModeProperty;
    private SerializedProperty accelerateSpeedMultiplierProperty;
    private SerializedProperty accelerateAccelerationMultiplierProperty;
    private SerializedProperty brakeSpeedMultiplierProperty;
    private SerializedProperty brakeAccelerationMultiplierProperty;
    private SerializedProperty brakeGroundDragMultiplierProperty;
    private SerializedProperty slipperySpeedMultiplierProperty;
    private SerializedProperty slipperySteeringMultiplierProperty;
    private SerializedProperty slipperyGroundDragMultiplierProperty;
    private SerializedProperty trapDurationProperty;
    private SerializedProperty zeroPlanarVelocityOnTrapProperty;
    private SerializedProperty bounceDirectionModeProperty;
    private SerializedProperty customBounceDirectionProperty;
    private SerializedProperty minIncomingBounceSpeedProperty;
    private SerializedProperty minBounceLaunchSpeedProperty;
    private SerializedProperty bounceRestitutionProperty;
    private SerializedProperty bounceSpeedBonusProperty;
    private SerializedProperty maxBounceLaunchSpeedProperty;
    private SerializedProperty lateralVelocityMultiplierProperty;
    private SerializedProperty conveyorDirectionModeProperty;
    private SerializedProperty conveyorAxisProperty;
    private SerializedProperty conveyorDirectionProperty;
    private SerializedProperty conveyorDiagonalDirectionProperty;
    private SerializedProperty conveyorUseLocalDirectionProperty;
    private SerializedProperty conveyorSpeedProperty;
    private SerializedProperty conveyorAffectsRigidbodiesProperty;
    private SerializedProperty conveyorRigidbodyDetectionMaskProperty;
    private SerializedProperty playerDetectionMaskProperty;

    private void OnEnable()
    {
        volumeNameProperty = serializedObject.FindProperty("volumeName");
        effectModeProperty = serializedObject.FindProperty("effectMode");
        accelerateSpeedMultiplierProperty = serializedObject.FindProperty("accelerateSpeedMultiplier");
        accelerateAccelerationMultiplierProperty = serializedObject.FindProperty("accelerateAccelerationMultiplier");
        brakeSpeedMultiplierProperty = serializedObject.FindProperty("brakeSpeedMultiplier");
        brakeAccelerationMultiplierProperty = serializedObject.FindProperty("brakeAccelerationMultiplier");
        brakeGroundDragMultiplierProperty = serializedObject.FindProperty("brakeGroundDragMultiplier");
        slipperySpeedMultiplierProperty = serializedObject.FindProperty("slipperySpeedMultiplier");
        slipperySteeringMultiplierProperty = serializedObject.FindProperty("slipperySteeringMultiplier");
        slipperyGroundDragMultiplierProperty = serializedObject.FindProperty("slipperyGroundDragMultiplier");
        trapDurationProperty = serializedObject.FindProperty("trapDuration");
        zeroPlanarVelocityOnTrapProperty = serializedObject.FindProperty("zeroPlanarVelocityOnTrap");
        bounceDirectionModeProperty = serializedObject.FindProperty("bounceDirectionMode");
        customBounceDirectionProperty = serializedObject.FindProperty("customBounceDirection");
        minIncomingBounceSpeedProperty = serializedObject.FindProperty("minIncomingBounceSpeed");
        minBounceLaunchSpeedProperty = serializedObject.FindProperty("minBounceLaunchSpeed");
        bounceRestitutionProperty = serializedObject.FindProperty("bounceRestitution");
        bounceSpeedBonusProperty = serializedObject.FindProperty("bounceSpeedBonus");
        maxBounceLaunchSpeedProperty = serializedObject.FindProperty("maxBounceLaunchSpeed");
        lateralVelocityMultiplierProperty = serializedObject.FindProperty("lateralVelocityMultiplier");
        conveyorDirectionModeProperty = serializedObject.FindProperty("conveyorDirectionMode");
        conveyorAxisProperty = serializedObject.FindProperty("conveyorAxis");
        conveyorDirectionProperty = serializedObject.FindProperty("conveyorDirection");
        conveyorDiagonalDirectionProperty = serializedObject.FindProperty("conveyorDiagonalDirection");
        conveyorUseLocalDirectionProperty = serializedObject.FindProperty("conveyorUseLocalDirection");
        conveyorSpeedProperty = serializedObject.FindProperty("conveyorSpeed");
        conveyorAffectsRigidbodiesProperty = serializedObject.FindProperty("conveyorAffectsRigidbodies");
        conveyorRigidbodyDetectionMaskProperty = serializedObject.FindProperty("conveyorRigidbodyDetectionMask");
        playerDetectionMaskProperty = serializedObject.FindProperty("playerDetectionMask");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSetupSection();
        EditorGUILayout.Space();
        DrawEffectSection();
        EditorGUILayout.Space();
        DrawAreaSection();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSetupSection()
    {
        EditorGUILayout.LabelField("1. Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(volumeNameProperty, new GUIContent("Volume Name"));
        EditorGUILayout.HelpBox(
            "Use o collider do proprio objeto para desenhar a area. O sistema mantem esse collider como Trigger automaticamente.",
            MessageType.None);
    }

    private void DrawEffectSection()
    {
        EditorGUILayout.LabelField("2. Effect", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(effectModeProperty, new GUIContent("Effect Mode"));

        switch (GetEffectMode())
        {
            case MovementVolumeController.MovementVolumeEffectMode.Accelerate:
                EditorGUILayout.PropertyField(accelerateSpeedMultiplierProperty, new GUIContent("Speed Multiplier"));
                EditorGUILayout.PropertyField(accelerateAccelerationMultiplierProperty, new GUIContent("Acceleration Multiplier"));
                EditorGUILayout.HelpBox(
                    "Accelerate aumenta a velocidade maxima e a capacidade de ganhar velocidade enquanto o jogador estiver dentro da area.",
                    MessageType.Info);
                break;

            case MovementVolumeController.MovementVolumeEffectMode.Brake:
                EditorGUILayout.PropertyField(brakeSpeedMultiplierProperty, new GUIContent("Speed Multiplier"));
                EditorGUILayout.PropertyField(brakeAccelerationMultiplierProperty, new GUIContent("Acceleration Multiplier"));
                EditorGUILayout.PropertyField(brakeGroundDragMultiplierProperty, new GUIContent("Ground Drag Multiplier"));
                EditorGUILayout.HelpBox(
                    "Brake segura o deslocamento e ajuda a parar mais rapido. Bom para lama, areia pesada ou vento contrario.",
                    MessageType.Info);
                break;

            case MovementVolumeController.MovementVolumeEffectMode.Slippery:
                EditorGUILayout.PropertyField(slipperySpeedMultiplierProperty, new GUIContent("Speed Multiplier"));
                EditorGUILayout.PropertyField(slipperySteeringMultiplierProperty, new GUIContent("Steering Multiplier"));
                EditorGUILayout.PropertyField(slipperyGroundDragMultiplierProperty, new GUIContent("Ground Drag Multiplier"));
                EditorGUILayout.HelpBox(
                    "Slippery reduz o controle e o atrito no chao, fazendo o jogador escorregar mais e responder menos ao comando.",
                    MessageType.Info);
                break;

            case MovementVolumeController.MovementVolumeEffectMode.Trap:
                EditorGUILayout.PropertyField(trapDurationProperty, new GUIContent("Trap Duration"));
                EditorGUILayout.PropertyField(zeroPlanarVelocityOnTrapProperty, new GUIContent("Zero Planar Velocity On Trap"));
                EditorGUILayout.HelpBox(
                    "Trap prende o jogador uma vez por entrada na area. O efeito so arma de novo depois que o jogador sair e entrar novamente.",
                    MessageType.Warning);
                break;

            case MovementVolumeController.MovementVolumeEffectMode.Bounce:
                EditorGUILayout.PropertyField(bounceDirectionModeProperty, new GUIContent("Bounce Direction Mode"));
                if ((MovementVolumeController.BounceDirectionMode)bounceDirectionModeProperty.enumValueIndex
                    == MovementVolumeController.BounceDirectionMode.CustomDirection)
                {
                    EditorGUILayout.PropertyField(customBounceDirectionProperty, new GUIContent("Custom Bounce Direction"));
                }

                EditorGUILayout.PropertyField(minIncomingBounceSpeedProperty, new GUIContent("Min Incoming Speed"));
                EditorGUILayout.PropertyField(minBounceLaunchSpeedProperty, new GUIContent("Min Bounce Launch Speed"));
                EditorGUILayout.PropertyField(bounceRestitutionProperty, new GUIContent("Bounce Restitution"));
                EditorGUILayout.PropertyField(bounceSpeedBonusProperty, new GUIContent("Bounce Speed Bonus"));
                EditorGUILayout.PropertyField(maxBounceLaunchSpeedProperty, new GUIContent("Max Bounce Launch Speed"));
                EditorGUILayout.PropertyField(lateralVelocityMultiplierProperty, new GUIContent("Lateral Velocity Multiplier"));
                EditorGUILayout.HelpBox(
                    "Bounce usa a velocidade com que o jogador chega para calcular o impulso de saida. Queda maior gera bounce maior, com bonus e limites configuraveis.",
                    MessageType.Info);
                break;

            case MovementVolumeController.MovementVolumeEffectMode.Conveyor:
                EditorGUILayout.LabelField("Conveyor Direction", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(conveyorDirectionModeProperty, new GUIContent("Direction Mode"));
                if ((MovementVolumeController.ConveyorDirectionMode)conveyorDirectionModeProperty.enumValueIndex
                    == MovementVolumeController.ConveyorDirectionMode.Diagonal)
                {
                    EditorGUILayout.PropertyField(conveyorDiagonalDirectionProperty, new GUIContent("Diagonal Direction"));
                    EditorGUILayout.HelpBox(
                        "Use um vetor como (1, 0, 1) para empurrar em diagonal. O sistema normaliza esse vetor automaticamente.",
                        MessageType.None);
                }
                else
                {
                    EditorGUILayout.PropertyField(conveyorAxisProperty, new GUIContent("Axis"));
                    EditorGUILayout.PropertyField(conveyorDirectionProperty, new GUIContent("Direction"));
                }

                EditorGUILayout.PropertyField(conveyorUseLocalDirectionProperty, new GUIContent("Use Local Direction"));
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Conveyor Movement", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(conveyorSpeedProperty, new GUIContent("Speed"));
                EditorGUILayout.HelpBox(
                    "Conveyor empurra continuamente o jogador dentro do trigger, mesmo sem input. Speed e a velocidade da esteira na direcao configurada.",
                    MessageType.Info);
                break;
        }
    }

    private void DrawAreaSection()
    {
        EditorGUILayout.LabelField("3. Area", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(playerDetectionMaskProperty, new GUIContent("Player Detection Mask"));
        if (GetEffectMode() == MovementVolumeController.MovementVolumeEffectMode.Conveyor)
        {
            EditorGUILayout.PropertyField(conveyorAffectsRigidbodiesProperty, new GUIContent("Affects Rigidbodies"));
            if (conveyorAffectsRigidbodiesProperty.boolValue)
                EditorGUILayout.PropertyField(conveyorRigidbodyDetectionMaskProperty, new GUIContent("Rigidbody Detection Mask"));
        }

        EditorGUILayout.HelpBox(
            "Esses volumes podem ser usados em prefabs ou direto no level design. O gizmo selecionado mostra a area afetada e, nos modos Bounce e Conveyor, a direcao do impulso.",
            MessageType.None);
    }

    private MovementVolumeController.MovementVolumeEffectMode GetEffectMode()
    {
        return (MovementVolumeController.MovementVolumeEffectMode)effectModeProperty.enumValueIndex;
    }
}
