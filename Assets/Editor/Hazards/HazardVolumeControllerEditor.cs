using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HazardVolumeController))]
public class HazardVolumeControllerEditor : Editor
{
    private SerializedProperty hazardNameProperty;
    private SerializedProperty effectModeProperty;
    private SerializedProperty damageAmountProperty;
    private SerializedProperty damagePerSecondProperty;
    private SerializedProperty damageTickIntervalProperty;
    private SerializedProperty ignoreDamageImmunityProperty;
    private SerializedProperty suppressDamageKnockbackProperty;
    private SerializedProperty playerDetectionMaskProperty;

    private void OnEnable()
    {
        hazardNameProperty = serializedObject.FindProperty("hazardName");
        effectModeProperty = serializedObject.FindProperty("effectMode");
        damageAmountProperty = serializedObject.FindProperty("damageAmount");
        damagePerSecondProperty = serializedObject.FindProperty("damagePerSecond");
        damageTickIntervalProperty = serializedObject.FindProperty("damageTickInterval");
        ignoreDamageImmunityProperty = serializedObject.FindProperty("ignoreDamageImmunity");
        suppressDamageKnockbackProperty = serializedObject.FindProperty("suppressDamageKnockback");
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
        EditorGUILayout.PropertyField(hazardNameProperty, new GUIContent("Hazard Name"));
        EditorGUILayout.HelpBox(
            "O collider no mesmo objeto define a area de efeito e sera mantido como Trigger automaticamente.",
            MessageType.None);
    }

    private void DrawEffectSection()
    {
        EditorGUILayout.LabelField("2. Effect", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(effectModeProperty, new GUIContent("Effect Mode"));

        switch (GetEffectMode())
        {
            case HazardVolumeController.HazardEffectMode.InstantKill:
                EditorGUILayout.PropertyField(ignoreDamageImmunityProperty, new GUIContent("Ignore Damage Immunity"));
                EditorGUILayout.PropertyField(suppressDamageKnockbackProperty, new GUIContent("Suppress Damage Knockback"));
                EditorGUILayout.HelpBox(
                    "Instant Kill mata o jogador assim que ele entra ou permanece dentro da area. Ideal para pocos fatais, quedas letais e armadilhas de morte.",
                    MessageType.Warning);
                break;

            case HazardVolumeController.HazardEffectMode.InstantDamage:
                EditorGUILayout.PropertyField(damageAmountProperty, new GUIContent("Damage Amount"));
                EditorGUILayout.PropertyField(ignoreDamageImmunityProperty, new GUIContent("Ignore Damage Immunity"));
                EditorGUILayout.PropertyField(suppressDamageKnockbackProperty, new GUIContent("Suppress Damage Knockback"));
                EditorGUILayout.HelpBox(
                    "Instant Damage aplica um unico golpe por entrada na area. Saiu e entrou de novo, toma dano novamente.",
                    MessageType.Info);
                break;

            case HazardVolumeController.HazardEffectMode.DamageOverTime:
                EditorGUILayout.PropertyField(damagePerSecondProperty, new GUIContent("Damage Per Second"));
                EditorGUILayout.PropertyField(damageTickIntervalProperty, new GUIContent("Damage Tick Interval"));
                EditorGUILayout.PropertyField(ignoreDamageImmunityProperty, new GUIContent("Ignore Damage Immunity"));
                EditorGUILayout.PropertyField(suppressDamageKnockbackProperty, new GUIContent("Suppress Damage Knockback"));
                EditorGUILayout.HelpBox(
                    "Damage Over Time aplica dano continuo em ticks enquanto o jogador estiver na area. Para DPS fiel, deixe Ignore Damage Immunity ligado.",
                    MessageType.Info);
                break;
        }
    }

    private void DrawAreaSection()
    {
        EditorGUILayout.LabelField("3. Area", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(playerDetectionMaskProperty, new GUIContent("Player Detection Mask"));
        EditorGUILayout.HelpBox(
            "Use o collider do proprio objeto para desenhar a area afetada. O gizmo selecionado mostra a zona de dano ou morte.",
            MessageType.None);
    }

    private HazardVolumeController.HazardEffectMode GetEffectMode()
    {
        return (HazardVolumeController.HazardEffectMode)effectModeProperty.enumValueIndex;
    }
}
