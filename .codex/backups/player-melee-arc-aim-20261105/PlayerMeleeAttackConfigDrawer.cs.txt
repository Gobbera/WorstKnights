using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(PlayerMeleeAttack.AttackConfig))]
public sealed class PlayerMeleeAttackConfigDrawer : PropertyDrawer
{
    private const float VerticalSpacing = 2f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Rect lineRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(lineRect, property.isExpanded, BuildFoldoutLabel(property, label), true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            lineRect.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            DrawSection(ref lineRect, "Combo");
            DrawProperty(ref lineRect, property, "comboStep", "Combo Step");
            DrawProperty(ref lineRect, property, "hitShape", "Hit Shape");

            DrawSection(ref lineRect, "Hit Timing");
            DrawProperty(ref lineRect, property, "damageStartFrame", "Damage Start Frame");
            DrawProperty(ref lineRect, property, "framesPerSecond", "Frames Per Second");
            DrawProperty(ref lineRect, property, "activeDuration", "Active Duration");

            DrawShapeProperties(ref lineRect, property);

            DrawSection(ref lineRect, "Audio");
            DrawProperty(ref lineRect, property, "swingAudioCue", "Swing Audio Cue");
            DrawProperty(ref lineRect, property, "swingAudioDelay", "Swing Audio Delay");

            DrawSection(ref lineRect, "Impact VFX");
            DrawProperty(ref lineRect, property, "impactVfxAttackAngle", "Impact VFX Attack Angle");

            DrawCameraProperties(ref lineRect, property);

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded)
            return height;

        height += VerticalSpacing;
        height += GetSectionHeight();
        height += GetPropertyHeight(property, "comboStep");
        height += GetPropertyHeight(property, "hitShape");

        height += GetSectionHeight();
        height += GetPropertyHeight(property, "damageStartFrame");
        height += GetPropertyHeight(property, "framesPerSecond");
        height += GetPropertyHeight(property, "activeDuration");

        height += GetShapePropertiesHeight(property);

        height += GetSectionHeight();
        height += GetPropertyHeight(property, "swingAudioCue");
        height += GetPropertyHeight(property, "swingAudioDelay");

        height += GetSectionHeight();
        height += GetPropertyHeight(property, "impactVfxAttackAngle");

        height += GetCameraPropertiesHeight(property);
        return height;
    }

    private static GUIContent BuildFoldoutLabel(SerializedProperty property, GUIContent fallbackLabel)
    {
        SerializedProperty comboStepProperty = property.FindPropertyRelative("comboStep");
        SerializedProperty hitShapeProperty = property.FindPropertyRelative("hitShape");

        string comboText = comboStepProperty != null ? $"Combo {comboStepProperty.intValue}" : fallbackLabel.text;
        string shapeText = ResolveEnumDisplayName(hitShapeProperty);
        return new GUIContent(string.IsNullOrWhiteSpace(shapeText) ? comboText : $"{comboText} - {shapeText}");
    }

    private static string ResolveEnumDisplayName(SerializedProperty enumProperty)
    {
        if (enumProperty == null || enumProperty.propertyType != SerializedPropertyType.Enum)
            return string.Empty;

        if (enumProperty.enumDisplayNames.Length == 0)
            return string.Empty;

        int index = Mathf.Clamp(enumProperty.enumValueIndex, 0, enumProperty.enumDisplayNames.Length - 1);
        return enumProperty.enumDisplayNames[index];
    }

    private static void DrawShapeProperties(ref Rect lineRect, SerializedProperty property)
    {
        PlayerMeleeAttackShape hitShape = ResolveHitShape(property);
        switch (hitShape)
        {
            case PlayerMeleeAttackShape.Arc:
                DrawSection(ref lineRect, "Arc Shape");
                DrawProperty(ref lineRect, property, "arcRange", "Range");
                DrawProperty(ref lineRect, property, "arcAngle", "Angle");
                DrawProperty(ref lineRect, property, "arcHalfHeight", "Half Height");
                break;

            case PlayerMeleeAttackShape.Box:
                DrawSection(ref lineRect, "Box Shape");
                DrawProperty(ref lineRect, property, "boxCenterDistance", "Center Distance");
                DrawProperty(ref lineRect, property, "boxSize", "Size");
                break;

            case PlayerMeleeAttackShape.Ball:
            default:
                DrawSection(ref lineRect, "Ball Shape");
                DrawProperty(ref lineRect, property, "targetDistance", "Center Distance");
                DrawProperty(ref lineRect, property, "hitRadius", "Radius");
                break;
        }
    }

    private static float GetShapePropertiesHeight(SerializedProperty property)
    {
        switch (ResolveHitShape(property))
        {
            case PlayerMeleeAttackShape.Arc:
                return GetSectionHeight()
                    + GetPropertyHeight(property, "arcRange")
                    + GetPropertyHeight(property, "arcAngle")
                    + GetPropertyHeight(property, "arcHalfHeight");
            case PlayerMeleeAttackShape.Box:
                return GetSectionHeight()
                    + GetPropertyHeight(property, "boxCenterDistance")
                    + GetPropertyHeight(property, "boxSize");
            case PlayerMeleeAttackShape.Ball:
            default:
                return GetSectionHeight()
                    + GetPropertyHeight(property, "targetDistance")
                    + GetPropertyHeight(property, "hitRadius");
        }
    }

    private static void DrawCameraProperties(ref Rect lineRect, SerializedProperty property)
    {
        DrawSection(ref lineRect, "Camera");
        DrawProperty(ref lineRect, property, "playerCameraImpactType", "Player Camera Impact Type");

        SerializedProperty applyCameraFeedbackProperty = property.FindPropertyRelative("applyCameraFeedback");
        DrawProperty(ref lineRect, applyCameraFeedbackProperty, "Apply Camera Feedback");
        if (applyCameraFeedbackProperty == null || !applyCameraFeedbackProperty.boolValue)
            return;

        DrawProperty(ref lineRect, property, "applyFovFeedbackToHandsCamera", "Apply FOV To Hands Camera");
        DrawProperty(ref lineRect, property, "attackFovPercentChange", "Attack FOV Percent Change");
        DrawProperty(ref lineRect, property, "attackFovReachTime", "Attack FOV Reach Time");
        DrawProperty(ref lineRect, property, "attackFovHoldTime", "Attack FOV Hold Time");
        DrawProperty(ref lineRect, property, "attackFovRelaxTime", "Attack FOV Relax Time");
        DrawProperty(ref lineRect, property, "attackCameraRotation", "Attack Camera Rotation");
    }

    private static float GetCameraPropertiesHeight(SerializedProperty property)
    {
        float height = GetSectionHeight()
            + GetPropertyHeight(property, "playerCameraImpactType")
            + GetPropertyHeight(property, "applyCameraFeedback");

        SerializedProperty applyCameraFeedbackProperty = property.FindPropertyRelative("applyCameraFeedback");
        if (applyCameraFeedbackProperty == null || !applyCameraFeedbackProperty.boolValue)
            return height;

        height += GetPropertyHeight(property, "applyFovFeedbackToHandsCamera");
        height += GetPropertyHeight(property, "attackFovPercentChange");
        height += GetPropertyHeight(property, "attackFovReachTime");
        height += GetPropertyHeight(property, "attackFovHoldTime");
        height += GetPropertyHeight(property, "attackFovRelaxTime");
        height += GetPropertyHeight(property, "attackCameraRotation");
        return height;
    }

    private static PlayerMeleeAttackShape ResolveHitShape(SerializedProperty property)
    {
        SerializedProperty hitShapeProperty = property.FindPropertyRelative("hitShape");
        if (hitShapeProperty == null || hitShapeProperty.propertyType != SerializedPropertyType.Enum)
            return PlayerMeleeAttackShape.Ball;

        return (PlayerMeleeAttackShape)Mathf.Clamp(
            hitShapeProperty.enumValueIndex,
            0,
            System.Enum.GetValues(typeof(PlayerMeleeAttackShape)).Length - 1);
    }

    private static void DrawSection(ref Rect lineRect, string label)
    {
        lineRect.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.LabelField(lineRect, label, EditorStyles.boldLabel);
        lineRect.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
    }

    private static void DrawProperty(ref Rect lineRect, SerializedProperty parent, string relativePropertyName, string label)
    {
        DrawProperty(ref lineRect, parent.FindPropertyRelative(relativePropertyName), label);
    }

    private static void DrawProperty(ref Rect lineRect, SerializedProperty property, string label)
    {
        if (property == null)
            return;

        lineRect.height = EditorGUI.GetPropertyHeight(property, true);
        EditorGUI.PropertyField(lineRect, property, new GUIContent(label), true);
        lineRect.y += lineRect.height + VerticalSpacing;
    }

    private static float GetSectionHeight()
    {
        return EditorGUIUtility.singleLineHeight + VerticalSpacing;
    }

    private static float GetPropertyHeight(SerializedProperty parent, string relativePropertyName)
    {
        return GetPropertyHeight(parent.FindPropertyRelative(relativePropertyName));
    }

    private static float GetPropertyHeight(SerializedProperty property)
    {
        return property != null ? EditorGUI.GetPropertyHeight(property, true) + VerticalSpacing : 0f;
    }
}
