using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(InteractionTriggerRouter))]
public sealed class InteractionTriggerRouterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Assign this router as the trigger's Linked System. SelectRoute(int) changes the next action's destination. SelectRouteAndActivate(int) releases the old target and activates the new route immediately in B. Routes can override the trigger's activation mode, such as Momentary before a puzzle and Alternate AB afterward. Use one router per trigger. Movement and audio remain on their own components.", MessageType.Info);
        var routes = serializedObject.FindProperty("routes");
        for (int i = 0; i < routes.arraySize; i++)
        {
            var entry = routes.GetArrayElementAtIndex(i);
            var receiver = entry.FindPropertyRelative("target").objectReferenceValue as MonoBehaviour;
            EditorGUILayout.LabelField("Route " + i, entry.FindPropertyRelative("name").stringValue);
            if (receiver == null || receiver is not IInteractionTriggerTarget || receiver is InteractionTriggerRouter)
                EditorGUILayout.HelpBox("Route " + i + " needs a gameplay target or signal adapter. Empty targets and nested routers block interaction.", MessageType.Warning);
        }
        var initial = serializedObject.FindProperty("initialRoute").intValue;
        if (initial < 0 || initial >= routes.arraySize)
            EditorGUILayout.HelpBox("Initial Route must identify an entry in Routes.", MessageType.Error);
        if (Application.isPlaying)
        {
            var router = (InteractionTriggerRouter)target;
            EditorGUILayout.LabelField("Selected Route", router.SelectedRoute.ToString());
            EditorGUILayout.LabelField("Held Route", router.HeldRoute < 0 ? "None" : router.HeldRoute.ToString());
        }
    }
}
