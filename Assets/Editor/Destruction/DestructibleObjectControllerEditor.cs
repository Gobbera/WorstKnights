using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DestructibleObjectController))]
public class DestructibleObjectControllerEditor : Editor
{
    private SerializedProperty destructibleNameProperty;
    private SerializedProperty maxHealthProperty;
    private SerializedProperty damageImmunityDurationProperty;
    private SerializedProperty destructionModeProperty;
    private SerializedProperty destructionTargetProperty;
    private SerializedProperty disableCollidersOnDestroyedProperty;
    private SerializedProperty photonViewProperty;
    private SerializedProperty prototypeLocalOnlyProperty;

    private void OnEnable()
    {
        destructibleNameProperty = serializedObject.FindProperty("destructibleName");
        maxHealthProperty = serializedObject.FindProperty("maxHealth");
        damageImmunityDurationProperty = serializedObject.FindProperty("damageImmunityDuration");
        destructionModeProperty = serializedObject.FindProperty("destructionMode");
        destructionTargetProperty = serializedObject.FindProperty("destructionTarget");
        disableCollidersOnDestroyedProperty = serializedObject.FindProperty("disableCollidersOnDestroyed");
        photonViewProperty = serializedObject.FindProperty("photonView");
        prototypeLocalOnlyProperty = serializedObject.FindProperty("prototypeLocalOnly");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSetupSection();
        EditorGUILayout.Space();
        DrawHealthSection();
        EditorGUILayout.Space();
        DrawDestructionSection();
        EditorGUILayout.Space();
        DrawNetworkingSection();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSetupSection()
    {
        EditorGUILayout.LabelField("1. Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(destructibleNameProperty, new GUIContent("Destructible Name"));
        EditorGUILayout.HelpBox(
            "Adicione este componente em qualquer objeto que precise receber dano e quebrar. O ataque melee do jogador ja vai reconhecer esse alvo automaticamente por IDamageable.",
            MessageType.None);
    }

    private void DrawHealthSection()
    {
        EditorGUILayout.LabelField("2. Health", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(maxHealthProperty, new GUIContent("Max Health"));
        EditorGUILayout.PropertyField(damageImmunityDurationProperty, new GUIContent("Damage Immunity Duration"));
        EditorGUILayout.HelpBox(
            "Max Health define quantos golpes o objeto aguenta. Damage Immunity Duration e opcional e serve para evitar dano duplicado no mesmo instante.",
            MessageType.None);
    }

    private void DrawDestructionSection()
    {
        EditorGUILayout.LabelField("3. Destruction", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(destructionModeProperty, new GUIContent("Destruction Mode"));

        DestructibleObjectController.DestructionMode destructionMode = GetDestructionMode();
        if (destructionMode == DestructibleObjectController.DestructionMode.DestroyTarget
            || destructionMode == DestructibleObjectController.DestructionMode.DisableTarget)
        {
            EditorGUILayout.PropertyField(destructionTargetProperty, new GUIContent("Destruction Target"));
        }

        EditorGUILayout.PropertyField(disableCollidersOnDestroyedProperty, new GUIContent("Disable Colliders On Destroyed"));
        EditorGUILayout.HelpBox(
            "Use Destroy/Disable GameObject para quebrar o objeto inteiro. Use Target quando quiser destruir ou desligar apenas uma parte especifica do prefab.",
            MessageType.Info);
    }

    private void DrawNetworkingSection()
    {
        EditorGUILayout.LabelField("4. Networking", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(photonViewProperty, new GUIContent("Photon View"));
        EditorGUILayout.PropertyField(prototypeLocalOnlyProperty, new GUIContent("Prototype Local Only"));

        if (photonViewProperty.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox(
                "Sem Photon View, este destrutivel funciona apenas localmente. Para multiplayer sincronizado, adicione um PhotonView no mesmo objeto.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Se Prototype Local Only estiver desligado, o dano e a destruicao sao replicados via Photon para todos os clientes.",
                MessageType.None);
        }

        EditorGUILayout.HelpBox(
            "Para som, particula e outras reacoes, adicione separadamente os componentes Reaction Signal Receiver e Destructible Reaction Signal Bridge apenas nos objetos que precisarem disso.",
            MessageType.Info);
    }

    private DestructibleObjectController.DestructionMode GetDestructionMode()
    {
        return (DestructibleObjectController.DestructionMode)destructionModeProperty.enumValueIndex;
    }
}
