using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerPerspectiveVisibility))]
public class PlayerPerspectiveVisibilityEditor : Editor
{
    private static readonly string[] RuleLabels =
    {
        "Sempre aparece",
        "So para o dono",
        "So para remotos",
        "Sempre escondido"
    };

    private SerializedProperty elementsProperty;

    private void OnEnable()
    {
        elementsProperty = serializedObject.FindProperty("elements");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawElements();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawElements()
    {
        if (elementsProperty == null)
            return;

        int removeIndex = -1;

        for (int i = 0; i < elementsProperty.arraySize; i++)
        {
            SerializedProperty elementProperty = elementsProperty.GetArrayElementAtIndex(i);
            SerializedProperty labelProperty = elementProperty.FindPropertyRelative("label");
            SerializedProperty meshProperty = elementProperty.FindPropertyRelative("mesh");
            SerializedProperty visibilityProperty = elementProperty.FindPropertyRelative("visibility");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Elemento {i + 1}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("-", GUILayout.Width(28f)))
                    removeIndex = i;
            }

            EditorGUI.BeginChangeCheck();
            Object selectedMesh = EditorGUILayout.ObjectField(
                "Mesh",
                meshProperty.objectReferenceValue,
                typeof(Object),
                allowSceneObjects: true);
            if (EditorGUI.EndChangeCheck())
            {
                meshProperty.objectReferenceValue = IsSupportedTarget(selectedMesh)
                    ? selectedMesh
                    : null;

                if (selectedMesh != null && !IsSupportedTarget(selectedMesh))
                    Debug.LogWarning("[PlayerPerspectiveVisibility] Arraste um GameObject, Transform, Renderer, MeshFilter ou Mesh.", target);
            }

            visibilityProperty.enumValueIndex = EditorGUILayout.Popup(
                "Regra",
                Mathf.Clamp(visibilityProperty.enumValueIndex, 0, RuleLabels.Length - 1),
                RuleLabels);

            labelProperty.stringValue = BuildLabel(meshProperty.objectReferenceValue, i);

            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
            elementsProperty.DeleteArrayElementAtIndex(removeIndex);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ Adicionar Mesh"))
                AddElement(null);

            using (new EditorGUI.DisabledScope(!HasSupportedSelection()))
            {
                if (GUILayout.Button("+ Selecionado"))
                    AddSelection();
            }
        }

        if (elementsProperty.arraySize == 0)
        {
            EditorGUILayout.HelpBox(
                "Adicione uma linha, arraste a mesh/objeto no campo Mesh e escolha a regra.",
                MessageType.None);
        }
    }

    private void AddSelection()
    {
        Object[] selectedObjects = Selection.objects;
        if (selectedObjects == null)
            return;

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            Object selectedObject = selectedObjects[i];
            if (IsSupportedTarget(selectedObject))
                AddElement(selectedObject);
        }
    }

    private void AddElement(Object mesh)
    {
        int newIndex = elementsProperty.arraySize;
        elementsProperty.InsertArrayElementAtIndex(newIndex);

        SerializedProperty elementProperty = elementsProperty.GetArrayElementAtIndex(newIndex);
        elementProperty.FindPropertyRelative("label").stringValue = BuildLabel(mesh, newIndex);
        elementProperty.FindPropertyRelative("mesh").objectReferenceValue = mesh;
        elementProperty.FindPropertyRelative("visibility").enumValueIndex = (int)PlayerPerspectiveVisibilityMode.RemoteOnly;
        elementProperty.FindPropertyRelative("includeChildren").boolValue = true;
        elementProperty.FindPropertyRelative("fallbackName").stringValue = string.Empty;
    }

    private static string BuildLabel(Object mesh, int index)
    {
        return mesh != null ? mesh.name : $"Elemento {index + 1}";
    }

    private static bool HasSupportedSelection()
    {
        Object[] selectedObjects = Selection.objects;
        if (selectedObjects == null || selectedObjects.Length == 0)
            return false;

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            if (IsSupportedTarget(selectedObjects[i]))
                return true;
        }

        return false;
    }

    private static bool IsSupportedTarget(Object selectedObject)
    {
        if (selectedObject == null)
            return true;

        return selectedObject is GameObject
            || selectedObject is Component
            || selectedObject is Mesh;
    }
}
