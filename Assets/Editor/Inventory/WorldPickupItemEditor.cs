using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WorldPickupItem))]
public class WorldPickupItemEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("itemDefinition"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("networkSceneId"), new GUIContent("Network Scene Id"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Grip Points", EditorStyles.boldLabel);

        WorldPickupItem pickupItem = (WorldPickupItem)target;
        HandRequirement handRequirement = pickupItem.ItemDefinition != null
            ? pickupItem.ItemDefinition.HandRequirement
            : HandRequirement.Any;

        EditorGUILayout.LabelField("Third Person", EditorStyles.miniBoldLabel);
        if (InventoryItemAuthoringUtility.ShouldUseGripPoint(handRequirement, HandType.Right))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rightHandPose"), new GUIContent("TP Right Hand"), includeChildren: true);
        if (InventoryItemAuthoringUtility.ShouldUseGripPoint(handRequirement, HandType.Left))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leftHandPose"), new GUIContent("TP Left Hand"), includeChildren: true);

        EditorGUILayout.LabelField("First Person", EditorStyles.miniBoldLabel);
        if (InventoryItemAuthoringUtility.ShouldUseGripPoint(handRequirement, HandType.Right))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("firstPersonRightHandPose"), new GUIContent("FPS Right Hand"), includeChildren: true);
        if (InventoryItemAuthoringUtility.ShouldUseGripPoint(handRequirement, HandType.Left))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("firstPersonLeftHandPose"), new GUIContent("FPS Left Hand"), includeChildren: true);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Pickup Trigger", pickupItem.PickupColliderHost, typeof(Transform), true);
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Criacao, dados, collider, outline e prefab setup vivem em Tools/Inventory/Item Authoring Tool. Posicionamento de mao vive em Tools/Inventory/Item Grip Authoring Tool.",
            MessageType.None);

        if (GUILayout.Button("Abrir Item Authoring Tool"))
            InventoryItemAuthoringToolWindow.OpenForTarget(pickupItem);

        if (GUILayout.Button("Abrir Item Grip Authoring Tool"))
            InventoryItemGripAuthoringToolWindow.OpenForTarget(pickupItem);
    }
}
