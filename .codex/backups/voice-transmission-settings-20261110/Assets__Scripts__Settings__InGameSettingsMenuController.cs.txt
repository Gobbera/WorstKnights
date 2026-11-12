using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-150)]
public sealed class InGameSettingsMenuController : MonoBehaviour
{
    private const string SettingsMenuPrefabResourcePath = "UI/InGameSettingsMenu";
    private const string SettingsMenuInstanceName = "InGameSettingsMenu";
    private const string EventSystemName = "EventSystem";

    private static int openMenuCount;

    [SerializeField] private GameObject settingsMenuRoot;
    [SerializeField] private CanvasGroup settingsCanvasGroup;
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;
    [SerializeField] private bool openOnStart;
    [SerializeField] private bool allowToggleInput = true;
    [SerializeField] private bool pauseLookInputWhileOpen = true;
    [SerializeField] private CursorLockMode openCursorLockMode = CursorLockMode.Confined;
    [SerializeField] private bool showCursorWhileOpen = true;

    private bool isOpen;
    private bool hasCursorOverride;
    private CursorLockMode cursorLockStateBeforeOpen;
    private bool cursorVisibleBeforeOpen;
    private MouseLook blockedMouseLook;

    public static bool IsAnyMenuOpen => openMenuCount > 0;
    public bool IsOpen => isOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        openMenuCount = 0;
    }

    public static InGameSettingsMenuController EnsureForGameplayScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || GameplaySceneRoot.TryGetActiveSceneRoot() == null)
            return null;

        InGameSettingsMenuController existing = FindInScene(activeScene);
        if (existing != null)
        {
            existing.EnsureConfigured();
            EnsureEventSystem();
            return existing;
        }

        GameObject prefab = Resources.Load<GameObject>(SettingsMenuPrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"InGameSettingsMenuController: prefab 'Resources/{SettingsMenuPrefabResourcePath}' not found.");
            return null;
        }

        GameObject instance = Instantiate(prefab);
        instance.name = SettingsMenuInstanceName;
        SceneManager.MoveGameObjectToScene(instance, activeScene);

        InGameSettingsMenuController controller = instance.GetComponent<InGameSettingsMenuController>();
        if (controller == null)
            controller = instance.AddComponent<InGameSettingsMenuController>();

        controller.EnsureConfigured();
        EnsureEventSystem();
        return controller;
    }

    private void Awake()
    {
        EnsureConfigured();
        SetMenuVisible(false);
    }

    private void Start()
    {
        if (openOnStart)
            OpenMenu();
    }

    private void OnDisable()
    {
        if (isOpen)
            CloseMenu();
    }

    private void OnDestroy()
    {
        if (!isOpen)
            return;

        isOpen = false;
        openMenuCount = Mathf.Max(0, openMenuCount - 1);
        RestoreGameplayCursorState();
    }

    private void Update()
    {
        if (!allowToggleInput)
            return;

        if (!WasTogglePressedThisFrame() || IsAnyEmoteWheelOpen())
            return;

        ToggleMenu();
    }

    public void ToggleMenu()
    {
        if (isOpen)
            CloseMenu();
        else
            OpenMenu();
    }

    public void OpenMenu()
    {
        if (isOpen || IsAnyEmoteWheelOpen())
            return;

        EnsureConfigured();
        EnsureEventSystem();
        isOpen = true;
        openMenuCount++;
        StoreAndApplyMenuCursorState();
        SetMenuVisible(true);
    }

    public void CloseMenu()
    {
        if (!isOpen)
        {
            SetMenuVisible(false);
            return;
        }

        SetMenuVisible(false);
        RestoreGameplayCursorState();

        isOpen = false;
        openMenuCount = Mathf.Max(0, openMenuCount - 1);
    }

    public void SetToggleInputAllowed(bool allowed)
    {
        allowToggleInput = allowed;
    }

    private void EnsureConfigured()
    {
        if (settingsMenuRoot == null)
            settingsMenuRoot = gameObject;

        if (settingsCanvasGroup == null && settingsMenuRoot != null)
            settingsCanvasGroup = settingsMenuRoot.GetComponent<CanvasGroup>();

        if (settingsCanvasGroup == null && settingsMenuRoot != null)
            settingsCanvasGroup = settingsMenuRoot.AddComponent<CanvasGroup>();
    }

    private void SetMenuVisible(bool visible)
    {
        EnsureConfigured();

        if (settingsCanvasGroup != null)
        {
            settingsCanvasGroup.alpha = visible ? 1f : 0f;
            settingsCanvasGroup.interactable = visible;
            settingsCanvasGroup.blocksRaycasts = visible;
        }

        if (visible && settingsMenuRoot != null)
            settingsMenuRoot.transform.SetAsLastSibling();
    }

    private void StoreAndApplyMenuCursorState()
    {
        if (!hasCursorOverride)
        {
            cursorLockStateBeforeOpen = Cursor.lockState;
            cursorVisibleBeforeOpen = Cursor.visible;
            hasCursorOverride = true;
        }

        blockedMouseLook = MouseLook.instance;
        if (pauseLookInputWhileOpen && blockedMouseLook != null)
            blockedMouseLook.SetLookInputBlocked(true);

        Cursor.lockState = openCursorLockMode;
        Cursor.visible = showCursorWhileOpen;
    }

    private void RestoreGameplayCursorState()
    {
        if (pauseLookInputWhileOpen && blockedMouseLook != null)
            blockedMouseLook.SetLookInputBlocked(false);

        blockedMouseLook = null;

        if (!hasCursorOverride)
            return;

        Cursor.lockState = cursorLockStateBeforeOpen;
        Cursor.visible = cursorVisibleBeforeOpen;
        hasCursorOverride = false;
    }

    private bool WasTogglePressedThisFrame()
    {
        if (Keyboard.current != null)
            return Keyboard.current.escapeKey.wasPressedThisFrame;

        return Input.GetKeyDown(toggleKey);
    }

    private static InGameSettingsMenuController FindInScene(Scene scene)
    {
        InGameSettingsMenuController[] controllers = FindObjectsByType<InGameSettingsMenuController>(FindObjectsInactive.Include);
        for (int i = 0; i < controllers.Length; i++)
        {
            InGameSettingsMenuController controller = controllers[i];
            if (controller != null && controller.gameObject.scene == scene)
                return controller;
        }

        return null;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        EventSystem[] eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
        for (int i = 0; i < eventSystems.Length; i++)
        {
            EventSystem eventSystem = eventSystems[i];
            if (eventSystem == null)
                continue;

            eventSystem.gameObject.SetActive(true);
            EnsureInputSystemModule(eventSystem.gameObject);
            return;
        }

        GameObject eventSystemObject = new GameObject(EventSystemName, typeof(EventSystem), typeof(InputSystemUIInputModule));
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid())
            SceneManager.MoveGameObjectToScene(eventSystemObject, activeScene);

        EnsureInputSystemModule(eventSystemObject);
    }

    private static void EnsureInputSystemModule(GameObject eventSystemObject)
    {
        if (eventSystemObject == null)
            return;

        InputSystemUIInputModule inputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
            inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();

        if (inputModule.actionsAsset == null)
            inputModule.AssignDefaultActions();
    }

    private static bool IsAnyEmoteWheelOpen()
    {
        PlayerEmoteWheelController[] controllers = FindObjectsByType<PlayerEmoteWheelController>(FindObjectsInactive.Include);
        for (int i = 0; i < controllers.Length; i++)
        {
            PlayerEmoteWheelController controller = controllers[i];
            if (controller == null || !controller.IsWheelOpen)
                continue;

            PhotonView view = controller.GetComponent<PhotonView>();
            if (view == null || view.IsMine)
                return true;
        }

        return false;
    }
}
