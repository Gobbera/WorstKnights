using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(InteractionTriggerInteractable))]
public sealed class InteractionTriggerInteractableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        EditorGUILayout.LabelField("Interaction Range", EditorStyles.boldLabel);
        Field("rangeMode", "Range Mode");
        if (serializedObject.FindProperty("rangeMode").enumValueIndex == (int)InteractionRangeMode.Distance)
            Field("interactionDistance", "Interaction Distance");
        else
        {
            Field("interactionBox", "Interaction Box");
            var box = serializedObject.FindProperty("interactionBox").objectReferenceValue as BoxCollider;
            if (box == null)
            {
                EditorGUILayout.HelpBox("Assign a BoxCollider or create a separate trigger box. Interaction is unavailable until the box exists and is enabled.", MessageType.Info);
                if (EditorUtility.IsPersistent(target))
                {
                    if (GUILayout.Button("Open Prefab to Create Box")) AssetDatabase.OpenAsset(((InteractionTriggerInteractable)target).gameObject);
                }
                else if (GUILayout.Button("Create Interaction Box"))
                {
                    serializedObject.ApplyModifiedProperties();
                    CreateInteractionBox((InteractionTriggerInteractable)target);
                    serializedObject.Update();
                }
            }
            else
            {
                var boxData = new SerializedObject(box); boxData.Update();
                EditorGUILayout.PropertyField(boxData.FindProperty("m_Center"), new GUIContent("Box Center (Local)"));
                var size = boxData.FindProperty("m_Size");
                EditorGUILayout.PropertyField(size, new GUIContent("Box Size (Local)"));
                Vector3 value = size.vector3Value;
                var clamped = new Vector3(Mathf.Max(0.01f, value.x), Mathf.Max(0.01f, value.y), Mathf.Max(0.01f, value.z));
                if (clamped != value) size.vector3Value = clamped;
                boxData.ApplyModifiedProperties();
                if (!box.enabled || !box.gameObject.activeSelf || (!EditorUtility.IsPersistent(box) && !box.gameObject.activeInHierarchy))
                    EditorGUILayout.HelpBox("The linked box is disabled or inactive. Players cannot interact until it is enabled.", MessageType.Warning);
                if (!box.isTrigger)
                    EditorGUILayout.HelpBox("Use a separate trigger collider so the interaction volume does not block player movement.", MessageType.Warning);
                if (GUILayout.Button("Select Interaction Box")) Selection.activeGameObject = box.gameObject;
            }
            EditorGUILayout.HelpBox("The player's root/body position must be inside the box. Aim at the mechanism and use the normal interaction key. Box mode replaces numeric reach; nearby pickups and other objects retain their own reach.", MessageType.None);
        }
        EditorGUILayout.Space(); EditorGUILayout.LabelField("Player Interaction", EditorStyles.boldLabel);
        Field("lockPlayerControls", "Lock Player Controls");
        if (serializedObject.FindProperty("lockPlayerControls").boolValue) Field("controlLockDuration", "Control Lock Duration");
        Field("playerAnimationTrigger", "Player Animation Trigger");
        EditorGUILayout.Space(); EditorGUILayout.LabelField("Local Player Events", EditorStyles.boldLabel);
        Field("onInteractionStarted", "On Interaction Started"); Field("onInteractionFinished", "On Interaction Finished");
        serializedObject.ApplyModifiedProperties();
    }

    public static BoxCollider CreateInteractionBox(InteractionTriggerInteractable interactable)
    {
        if (EditorUtility.IsPersistent(interactable)) throw new System.InvalidOperationException("Open the prefab before creating its interaction box.");
        if (interactable.InteractionBox != null) return interactable.InteractionBox;
        var root = new GameObject("Interaction Area"); root.layer = LayerMask.NameToLayer("Ignore Raycast");
        Undo.RegisterCreatedObjectUndo(root, "Create interaction box");
        Undo.SetTransformParent(root.transform, interactable.transform, "Parent interaction box");
        root.transform.localPosition = Vector3.zero; root.transform.localRotation = Quaternion.identity; root.transform.localScale = Vector3.one;
        var box = Undo.AddComponent<BoxCollider>(root); box.isTrigger = true; box.size = Vector3.one * 2f; box.center = Vector3.up * 0.5f;
        Undo.RecordObject(interactable, "Link interaction box");
        var data = new SerializedObject(interactable); data.FindProperty("interactionBox").objectReferenceValue = box; data.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(interactable);
        return box;
    }
    private void Field(string name, string label) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label), true);
}
