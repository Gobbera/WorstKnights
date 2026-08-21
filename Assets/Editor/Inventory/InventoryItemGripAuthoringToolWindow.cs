using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class InventoryItemGripAuthoringToolWindow : EditorWindow
{
    private const string DefaultTargetPath = "Assets/Resources/Player.prefab";

    private Vector2 scrollPosition;
    private WorldPickupItem gripPreviewItem;
    private GameObject gripTargetObject;
    private HandType gripPreviewHand = HandType.Right;
    private EquippedItemPerspective gripPreviewPerspective = EquippedItemPerspective.ThirdPerson;
    private Transform gripPreviewSocket;
    private string gripPreviewSocketPath = string.Empty;
    private GripPreviewState gripPreviewState;

    private struct GripPreviewState
    {
        public Transform OriginalParent;
        public int OriginalSiblingIndex;
        public Vector3 OriginalLocalPosition;
        public Quaternion OriginalLocalRotation;
        public Vector3 OriginalLocalScale;
        public bool IsPreviewing;
    }

    private struct PreviewSocketOption
    {
        public string Label;
        public string Path;
        public HandType Hand;
        public EquippedItemPerspective Perspective;
        public Transform SocketTransform;
    }

    [MenuItem("Tools/Inventory/Item Grip Authoring Tool")]
    public static void OpenWindow()
    {
        GetWindow<InventoryItemGripAuthoringToolWindow>("Item Grip Authoring");
    }

    public static void OpenForTarget(WorldPickupItem pickupItem)
    {
        InventoryItemGripAuthoringToolWindow window = GetWindow<InventoryItemGripAuthoringToolWindow>("Item Grip Authoring");
        window.SetItem(pickupItem);
        window.Show();
    }

    private void OnEnable()
    {
        TryUseSelectedItem();
        EnsureDefaultTarget();
    }

    private void OnSelectionChange()
    {
        if (!gripPreviewState.IsPreviewing)
            TryUseSelectedItem();

        if (gripTargetObject == null)
            TryUseSelectedTarget();

        Repaint();
    }

    private void OnDisable()
    {
        if (gripPreviewState.IsPreviewing)
            RestoreGripPreview();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        DrawGripAuthoringSection();
        EditorGUILayout.EndScrollView();
    }

    private void DrawGripAuthoringSection()
    {
        EditorGUILayout.LabelField("Grip Authoring", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Selecione o item na Hierarchy ou abra esta janela pelo Inspector do item. Use Target para apontar o objeto que contem os sockets.",
            MessageType.None);

        using (new EditorGUI.DisabledScope(gripPreviewState.IsPreviewing))
        {
            DrawCurrentItemStatus();

            EditorGUI.BeginChangeCheck();
            gripTargetObject = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Target", "Objeto que contem os sockets usados para posicionar o item."),
                gripTargetObject,
                typeof(GameObject),
                true);
            if (EditorGUI.EndChangeCheck())
                ClearPreviewSocketSelection();

            DrawPreviewSocketPicker();
        }

        using (new EditorGUI.DisabledScope(gripPreviewState.IsPreviewing))
        {
            if (GUILayout.Button("Posicionar Para Ajuste"))
                StartGripPreview();
        }

        if (!gripPreviewState.IsPreviewing)
        {
            string readinessMessage = GetGripPreviewReadinessMessage();
            if (!string.IsNullOrWhiteSpace(readinessMessage))
                EditorGUILayout.HelpBox(readinessMessage, MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox("Preview ativo. Ajuste o Transform do item na Scene e clique em Salvar.", MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Salvar Grip"))
            SaveGripPreview();
        if (GUILayout.Button("Cancelar Preview"))
            RestoreGripPreview();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawCurrentItemStatus()
    {
        string itemLabel = gripPreviewItem != null ? gripPreviewItem.gameObject.name : "Nenhum item selecionado";
        EditorGUILayout.LabelField("Item", itemLabel);
        EditorGUILayout.LabelField("Grip Destino", $"{GetGripLabel(gripPreviewHand, gripPreviewPerspective)}");
        EditorGUILayout.LabelField("Socket Preview", $"{GetPerspectiveLabel(gripPreviewPerspective)} {GetHandLabel(gripPreviewHand)}");
        EditorGUILayout.HelpBox(
            "FPS e TP usam grips separados. Salvar pelo socket FPS atualiza o GripPoints_FPS da mao; salvar pelo socket TP atualiza o GripPoints_TP da mao.",
            MessageType.Info);
    }

    private void SetItem(WorldPickupItem pickupItem)
    {
        if (pickupItem == null)
            return;

        gripPreviewItem = pickupItem;
    }

    private void TryUseSelectedItem()
    {
        GameObject selectedObject = ResolveObjectGameObject(Selection.activeObject);
        if (selectedObject == null)
            return;

        WorldPickupItem pickupItem = selectedObject.GetComponentInParent<WorldPickupItem>();
        if (pickupItem != null)
            SetItem(pickupItem);
    }

    private void TryUseSelectedTarget()
    {
        GameObject selectedObject = ResolveObjectGameObject(Selection.activeObject);
        if (selectedObject == null || selectedObject.GetComponentInParent<WorldPickupItem>() != null)
            return;

        if (!ContainsSocket(selectedObject.transform))
            return;

        gripTargetObject = selectedObject;
        ClearPreviewSocketSelection();
    }

    private void EnsureDefaultTarget()
    {
        if (gripTargetObject != null)
            return;

        HandEquipmentController[] controllers = FindObjectsByType<HandEquipmentController>(FindObjectsInactive.Include);
        if (controllers != null && controllers.Length > 0 && controllers[0] != null)
        {
            gripTargetObject = controllers[0].gameObject;
            return;
        }

        gripTargetObject = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultTargetPath);
    }

    private void DrawPreviewSocketPicker()
    {
        List<PreviewSocketOption> options = BuildCurrentPreviewSocketOptions();
        if (gripTargetObject == null)
        {
            EditorGUILayout.HelpBox("Selecione um Target que contenha sockets.", MessageType.Info);
            return;
        }

        if (options.Count == 0)
        {
            EditorGUILayout.HelpBox("Nenhum socket compativel encontrado no Target. A ferramenta procura objetos com PlayerItemSocketMarker ou nome contendo 'Socket'.", MessageType.Warning);
            return;
        }

        string[] popupLabels = new string[options.Count + 1];
        popupLabels[0] = "Nenhum";
        int selectedPopupIndex = 0;
        for (int i = 0; i < options.Count; i++)
        {
            PreviewSocketOption option = options[i];
            string targetStatus = option.SocketTransform != null && option.SocketTransform.gameObject.scene.IsValid()
                ? "cena"
                : "asset";
            popupLabels[i + 1] = $"{option.Label}  [{targetStatus}]";

            if (selectedPopupIndex == 0
                && !string.IsNullOrWhiteSpace(gripPreviewSocketPath)
                && string.Equals(gripPreviewSocketPath, option.Path, StringComparison.Ordinal))
            {
                selectedPopupIndex = i + 1;
            }

            if (selectedPopupIndex == 0
                && gripPreviewSocket != null
                && gripPreviewSocket == option.SocketTransform)
            {
                selectedPopupIndex = i + 1;
            }
        }

        int nextPopupIndex = EditorGUILayout.Popup(
            new GUIContent("Preview Socket", "Socket encontrado dentro do Target. Este socket sera usado para posicionar o item."),
            selectedPopupIndex,
            popupLabels);
        if (nextPopupIndex != selectedPopupIndex)
        {
            if (nextPopupIndex == 0)
                ClearPreviewSocketSelection();
            else
                ApplyPreviewSocketSelection(options[nextPopupIndex - 1], pingSocket: true);

            selectedPopupIndex = nextPopupIndex;
        }
        else if (selectedPopupIndex > 0)
        {
            ApplyPreviewSocketSelection(options[selectedPopupIndex - 1], pingSocket: false);
        }

        if (selectedPopupIndex > 0)
            EditorGUILayout.LabelField(options[selectedPopupIndex - 1].Path, EditorStyles.miniLabel);
    }

    private void ApplyPreviewSocketSelection(PreviewSocketOption option, bool pingSocket)
    {
        gripPreviewSocketPath = option.Path;
        gripPreviewHand = option.Hand;
        gripPreviewPerspective = option.Perspective;
        gripPreviewSocket = option.SocketTransform;

        if (pingSocket && option.SocketTransform != null)
            EditorGUIUtility.PingObject(option.SocketTransform);
    }

    private void ClearPreviewSocketSelection()
    {
        gripPreviewSocketPath = string.Empty;
        gripPreviewSocket = null;
        gripPreviewPerspective = EquippedItemPerspective.ThirdPerson;
    }

    private List<PreviewSocketOption> BuildCurrentPreviewSocketOptions()
    {
        return BuildPreviewSocketOptions(gripTargetObject, ResolveGripPreviewHandRequirement());
    }

    private HandRequirement ResolveGripPreviewHandRequirement()
    {
        return gripPreviewItem != null && gripPreviewItem.ItemDefinition != null
            ? gripPreviewItem.ItemDefinition.HandRequirement
            : HandRequirement.Any;
    }

    private static List<PreviewSocketOption> BuildPreviewSocketOptions(GameObject targetObject)
    {
        return BuildPreviewSocketOptions(targetObject, HandRequirement.Any);
    }

    private static List<PreviewSocketOption> BuildPreviewSocketOptions(GameObject targetObject, HandRequirement handRequirement)
    {
        List<PreviewSocketOption> options = new List<PreviewSocketOption>();
        if (targetObject == null)
            return options;

        Transform root = targetObject.transform;
        Transform[] targetTransforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < targetTransforms.Length; i++)
        {
            Transform targetTransform = targetTransforms[i];
            if (!TryBuildSocketOption(root, targetTransform, out PreviewSocketOption option))
                continue;

            if (!InventoryItemAuthoringUtility.ShouldUseGripPoint(handRequirement, option.Hand))
                continue;

            options.Add(option);
        }

        options.Sort(ComparePreviewSocketOptions);
        return options;
    }

    private static bool TryBuildSocketOption(
        Transform targetRoot,
        Transform socketTransform,
        out PreviewSocketOption option)
    {
        option = default;
        if (targetRoot == null || socketTransform == null)
            return false;

        PlayerItemSocketMarker marker = socketTransform.GetComponent<PlayerItemSocketMarker>();
        bool hasMarker = marker != null;
        if (!hasMarker && socketTransform.name.IndexOf("Socket", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        string relativePath = GetRelativeTransformPath(socketTransform, targetRoot);
        string path = string.IsNullOrWhiteSpace(relativePath) ? socketTransform.name : relativePath;
        string classificationPath = $"{targetRoot.name}/{path}";
        PlayerItemSocketEnvironment environment = hasMarker
            ? marker.Environment
            : InferSocketEnvironment(classificationPath);
        HandType hand = hasMarker
            ? marker.Hand
            : InferSocketHand(socketTransform.name, classificationPath);
        string displayName = hasMarker ? marker.DisplayName : socketTransform.name;

        option = new PreviewSocketOption
        {
            Label = $"{GetEnvironmentLabel(environment)} {GetHandLabel(hand)} - {displayName}",
            Path = path,
            Hand = hand,
            Perspective = ToEquippedPerspective(environment),
            SocketTransform = socketTransform
        };
        return true;
    }

    private static bool ContainsSocket(Transform targetRoot)
    {
        if (targetRoot == null)
            return false;

        Transform[] targetTransforms = targetRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < targetTransforms.Length; i++)
        {
            Transform targetTransform = targetTransforms[i];
            if (targetTransform == null)
                continue;

            if (targetTransform.GetComponent<PlayerItemSocketMarker>() != null)
                return true;

            if (targetTransform.name.IndexOf("Socket", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static PlayerItemSocketEnvironment InferSocketEnvironment(string path)
    {
        if (path.IndexOf("FPS", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("FirstPerson", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return PlayerItemSocketEnvironment.FirstPerson;
        }

        return PlayerItemSocketEnvironment.ThirdPerson;
    }

    private static HandType InferSocketHand(string socketName, string path)
    {
        string source = $"{socketName}/{path}";
        if (source.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0
            || source.IndexOf(".L", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return HandType.Left;
        }

        return HandType.Right;
    }

    private static int ComparePreviewSocketOptions(PreviewSocketOption left, PreviewSocketOption right)
    {
        int handComparison = left.Hand.CompareTo(right.Hand);
        if (handComparison != 0)
            return handComparison;

        return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanStartGripPreview()
    {
        return gripPreviewItem != null
            && gripPreviewItem.gameObject.scene.IsValid()
            && gripPreviewSocket != null
            && gripPreviewSocket.gameObject.scene.IsValid()
            && InventoryItemAuthoringUtility.ShouldUseGripPoint(ResolveGripPreviewHandRequirement(), gripPreviewHand);
    }

    private void StartGripPreview()
    {
        if (!TryPrepareGripPreview(out string failureMessage))
        {
            EditorUtility.DisplayDialog("Item Grip Authoring", failureMessage, "OK");
            return;
        }

        if (gripPreviewState.IsPreviewing)
            RestoreGripPreview();

        Transform itemTransform = gripPreviewItem.transform;
        gripPreviewState.OriginalParent = itemTransform.parent;
        gripPreviewState.OriginalSiblingIndex = itemTransform.GetSiblingIndex();
        gripPreviewState.OriginalLocalPosition = itemTransform.localPosition;
        gripPreviewState.OriginalLocalRotation = itemTransform.localRotation;
        gripPreviewState.OriginalLocalScale = itemTransform.localScale;
        gripPreviewState.IsPreviewing = true;

        Undo.RecordObject(itemTransform, "Position Item For Grip Authoring");
        InventoryItemAuthoringUtility.TryApplyGripPreview(gripPreviewItem, gripPreviewSocket, gripPreviewHand, gripPreviewPerspective);
        SceneView.RepaintAll();
    }

    private bool TryPrepareGripPreview(out string failureMessage)
    {
        failureMessage = string.Empty;

        if (gripPreviewItem == null)
            TryUseSelectedItem();

        if (gripPreviewItem == null)
        {
            failureMessage = "Selecione um WorldPickupItem na Hierarchy ou abra esta janela pelo Inspector do item.";
            return false;
        }

        if (!gripPreviewItem.gameObject.scene.IsValid())
        {
            if (!TryInstantiateSceneItemFromPrefab(gripPreviewItem, out WorldPickupItem sceneItem))
            {
                failureMessage = "O item em ajuste precisa ser uma instancia na cena ou um prefab de item que possa ser instanciado.";
                return false;
            }

            gripPreviewItem = sceneItem;
        }

        if (gripTargetObject == null)
        {
            failureMessage = "Selecione um Target que contenha os sockets.";
            return false;
        }

        if (gripPreviewSocket == null || !gripPreviewSocket.gameObject.scene.IsValid())
            TryRefreshSelectedSocketFromTarget();

        if (gripPreviewSocket == null || !gripPreviewSocket.gameObject.scene.IsValid())
            TrySelectFirstAvailableSocket();

        if (gripPreviewSocket == null)
        {
            failureMessage = "Selecione um Preview Socket a partir do Target.";
            return false;
        }

        if (!gripPreviewSocket.gameObject.scene.IsValid())
        {
            failureMessage = "O Preview Socket selecionado pertence a um asset. Para posicionar o item, use como Target uma instancia da cena.";
            return false;
        }

        if (!InventoryItemAuthoringUtility.ShouldUseGripPoint(ResolveGripPreviewHandRequirement(), gripPreviewHand))
        {
            failureMessage = "A mao selecionada nao e compativel com o Hand Requirement deste item.";
            return false;
        }

        return CanStartGripPreview();
    }

    private string GetGripPreviewReadinessMessage()
    {
        if (gripPreviewItem == null)
            return "Selecione um WorldPickupItem na Hierarchy ou abra esta janela pelo Inspector do item.";

        if (!gripPreviewItem.gameObject.scene.IsValid())
            return "O item em ajuste aponta para um prefab asset. Ao clicar em Posicionar Para Ajuste, a ferramenta tentara criar uma instancia temporaria na cena.";

        if (gripTargetObject == null)
            return "Selecione um Target que contenha sockets.";

        if (!gripTargetObject.scene.IsValid())
            return "Target esta apontando para um asset. Para posicionar o item, use uma instancia da cena.";

        if (gripPreviewSocket == null)
            return string.IsNullOrWhiteSpace(gripPreviewSocketPath)
                ? "Selecione um socket no campo Preview Socket."
                : "O Preview Socket selecionado nao foi resolvido no Target atual.";

        if (!gripPreviewSocket.gameObject.scene.IsValid())
            return "Preview Socket precisa resolver para um socket de cena.";

        if (!InventoryItemAuthoringUtility.ShouldUseGripPoint(ResolveGripPreviewHandRequirement(), gripPreviewHand))
            return "A mao selecionada nao e compativel com o Hand Requirement deste item.";

        return string.Empty;
    }

    private bool TryRefreshSelectedSocketFromTarget()
    {
        if (string.IsNullOrWhiteSpace(gripPreviewSocketPath))
            return false;

        List<PreviewSocketOption> options = BuildCurrentPreviewSocketOptions();
        for (int i = 0; i < options.Count; i++)
        {
            PreviewSocketOption option = options[i];
            if (!string.Equals(option.Path, gripPreviewSocketPath, StringComparison.Ordinal))
                continue;

            ApplyPreviewSocketSelection(option, pingSocket: false);
            return option.SocketTransform != null;
        }

        ClearPreviewSocketSelection();
        return false;
    }

    private void TrySelectFirstAvailableSocket()
    {
        List<PreviewSocketOption> options = BuildCurrentPreviewSocketOptions();
        for (int i = 0; i < options.Count; i++)
        {
            PreviewSocketOption option = options[i];
            if (option.SocketTransform == null || !option.SocketTransform.gameObject.scene.IsValid() || option.Hand != gripPreviewHand)
                continue;

            ApplyPreviewSocketSelection(option, pingSocket: false);
            return;
        }

        for (int i = 0; i < options.Count; i++)
        {
            PreviewSocketOption option = options[i];
            if (option.SocketTransform == null || !option.SocketTransform.gameObject.scene.IsValid())
                continue;

            ApplyPreviewSocketSelection(option, pingSocket: false);
            return;
        }
    }

    private static bool TryInstantiateSceneItemFromPrefab(WorldPickupItem prefabItem, out WorldPickupItem sceneItem)
    {
        sceneItem = null;
        if (prefabItem == null || !PrefabUtility.IsPartOfPrefabAsset(prefabItem))
            return false;

        GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(prefabItem.gameObject);
        if (prefabRoot == null)
            prefabRoot = prefabItem.gameObject;

        GameObject instance = PrefabUtility.InstantiatePrefab(prefabRoot) as GameObject;
        if (instance == null)
            return false;

        Undo.RegisterCreatedObjectUndo(instance, "Create Grip Preview Item");
        sceneItem = instance.GetComponentInChildren<WorldPickupItem>(true);
        Selection.activeObject = sceneItem != null ? sceneItem.gameObject : instance;
        EditorGUIUtility.PingObject(Selection.activeObject);
        return sceneItem != null;
    }

    private void SaveGripPreview()
    {
        if (gripPreviewItem == null || !gripPreviewState.IsPreviewing)
            return;

        if (!TryResolveAuthoringTarget(gripPreviewItem, out WorldPickupItem authoringTarget, out string warningMessage))
        {
            Debug.LogWarning($"[InventoryItemGripAuthoringTool] {warningMessage}", gripPreviewItem);
            return;
        }

        if (!InventoryItemAuthoringUtility.TryComputeGripLocalPose(gripPreviewItem, gripPreviewSocket, out Vector3 gripLocalPosition, out Quaternion gripLocalRotation))
        {
            Debug.LogWarning("[InventoryItemGripAuthoringTool] Nao foi possivel calcular a pose local do grip.", gripPreviewItem);
            return;
        }

        Transform runtimeGrip = gripPreviewItem.GetGripPoint(gripPreviewHand, gripPreviewPerspective);
        Transform authoringGrip = InventoryItemAuthoringUtility.ResolveWritableAuthoringGrip(
            runtimeGrip,
            authoringTarget,
            gripPreviewHand,
            gripPreviewPerspective);
        if (authoringGrip == null)
        {
            Debug.LogWarning("[InventoryItemGripAuthoringTool] Nao foi possivel localizar ou criar o grip no prefab fonte.", gripPreviewItem);
            return;
        }

        Undo.RecordObject(authoringGrip, "Save Item Grip");
        authoringGrip.localPosition = gripLocalPosition;
        authoringGrip.localRotation = gripLocalRotation;

        Undo.RecordObject(authoringTarget, "Assign Item Grip");
        authoringTarget.SetGripPoint(gripPreviewHand, gripPreviewPerspective, authoringGrip);

        if (runtimeGrip != null && runtimeGrip != authoringGrip)
        {
            Undo.RecordObject(runtimeGrip, "Sync Runtime Grip Preview");
            runtimeGrip.localPosition = gripLocalPosition;
            runtimeGrip.localRotation = gripLocalRotation;
        }

        EditorUtility.SetDirty(authoringGrip);
        EditorUtility.SetDirty(authoringTarget);
        AssetDatabase.SaveAssets();
        PrefabUtility.SavePrefabAsset(authoringTarget.gameObject);

        Debug.Log($"[InventoryItemGripAuthoringTool] Grip {GetPerspectiveLabel(gripPreviewPerspective)} da mao {GetHandLabel(gripPreviewHand)} salvo em '{authoringTarget.ItemName}'.", authoringTarget);
        RestoreGripPreview();
        SceneView.RepaintAll();
    }

    private void RestoreGripPreview()
    {
        if (gripPreviewItem == null || !gripPreviewState.IsPreviewing)
        {
            gripPreviewState = default;
            return;
        }

        Undo.RecordObject(gripPreviewItem.transform, "Restore Item After Grip Authoring");
        Transform itemTransform = gripPreviewItem.transform;
        itemTransform.SetParent(gripPreviewState.OriginalParent, false);
        if (gripPreviewState.OriginalParent != null)
            itemTransform.SetSiblingIndex(Mathf.Clamp(gripPreviewState.OriginalSiblingIndex, 0, gripPreviewState.OriginalParent.childCount - 1));
        itemTransform.localPosition = gripPreviewState.OriginalLocalPosition;
        itemTransform.localRotation = gripPreviewState.OriginalLocalRotation;
        itemTransform.localScale = gripPreviewState.OriginalLocalScale;

        gripPreviewState = default;
        SceneView.RepaintAll();
    }

    private static bool TryResolveAuthoringTarget(WorldPickupItem pickupItem, out WorldPickupItem authoringTarget, out string warningMessage)
    {
        authoringTarget = null;
        warningMessage = null;

        if (pickupItem == null)
        {
            warningMessage = "Item invalido.";
            return false;
        }

        authoringTarget = PrefabUtility.GetCorrespondingObjectFromSource(pickupItem);
        if (authoringTarget != null)
            return true;

        if (PrefabUtility.IsPartOfPrefabAsset(pickupItem) || PrefabStageUtility.GetPrefabStage(pickupItem.gameObject) != null)
        {
            authoringTarget = pickupItem;
            return true;
        }

        warningMessage = "Nao foi encontrado um prefab fonte. Use uma instancia de prefab na cena ou abra o prefab em Prefab Mode.";
        return false;
    }

    private static string GetRelativeTransformPath(Transform target, Transform root)
    {
        if (target == null || root == null)
            return string.Empty;

        if (target == root)
            return string.Empty;

        string path = target.name;
        Transform current = target.parent;
        while (current != null && current != root)
        {
            path = $"{current.name}/{path}";
            current = current.parent;
        }

        return current == root ? path : string.Empty;
    }

    private static GameObject ResolveObjectGameObject(UnityEngine.Object source)
    {
        if (source is GameObject gameObject)
            return gameObject;

        if (source is Component component)
            return component.gameObject;

        return null;
    }

    private static string GetEnvironmentLabel(PlayerItemSocketEnvironment environment)
    {
        return environment == PlayerItemSocketEnvironment.FirstPerson ? "FPS" : "TP";
    }

    private static EquippedItemPerspective ToEquippedPerspective(PlayerItemSocketEnvironment environment)
    {
        return environment == PlayerItemSocketEnvironment.FirstPerson
            ? EquippedItemPerspective.FirstPerson
            : EquippedItemPerspective.ThirdPerson;
    }

    private static string GetPerspectiveLabel(EquippedItemPerspective perspective)
    {
        return perspective == EquippedItemPerspective.FirstPerson ? "FPS" : "TP";
    }

    private static string GetGripLabel(HandType hand, EquippedItemPerspective perspective)
    {
        if (perspective == EquippedItemPerspective.FirstPerson)
            return hand == HandType.Right ? "GripPoints_FPS_Right" : "GripPoints_FPS_Left";

        return hand == HandType.Right ? "GripPoints_TP_Right" : "GripPoints_TP_Left";
    }

    private static string GetHandLabel(HandType hand)
    {
        return hand == HandType.Right ? "direita" : "esquerda";
    }
}
