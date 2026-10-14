using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class SettingsSceneMenuBootstrap : MonoBehaviour
{
    private const string SettingsMenuPrefabResourcePath = "UI/InGameSettingsMenu";
    private const string SettingsMenuInstanceName = "InGameSettingsMenu";

    [SerializeField] private GameObject settingsMenuPrefab;
    [SerializeField] private bool openOnStart = true;
    [SerializeField] private bool allowToggleInput;

    private InGameSettingsMenuController menuController;

    private void Start()
    {
        EnsureMenu();
    }

    private void EnsureMenu()
    {
        if (menuController == null)
            menuController = FindExistingSceneMenu();

        if (menuController == null)
            menuController = InstantiateMenuPrefab();

        if (menuController == null)
            return;

        menuController.SetToggleInputAllowed(allowToggleInput);
        if (openOnStart)
            menuController.OpenMenu();
    }

    private InGameSettingsMenuController InstantiateMenuPrefab()
    {
        GameObject prefab = settingsMenuPrefab != null
            ? settingsMenuPrefab
            : Resources.Load<GameObject>(SettingsMenuPrefabResourcePath);

        if (prefab == null)
        {
            Debug.LogWarning($"SettingsSceneMenuBootstrap: prefab 'Resources/{SettingsMenuPrefabResourcePath}' not found.", gameObject);
            return null;
        }

        GameObject instance = Instantiate(prefab);
        instance.name = SettingsMenuInstanceName;

        Scene scene = gameObject.scene;
        if (scene.IsValid())
            SceneManager.MoveGameObjectToScene(instance, scene);

        return instance.GetComponent<InGameSettingsMenuController>();
    }

    private InGameSettingsMenuController FindExistingSceneMenu()
    {
        Scene scene = gameObject.scene;
        InGameSettingsMenuController[] controllers = FindObjectsByType<InGameSettingsMenuController>(FindObjectsInactive.Include);
        for (int i = 0; i < controllers.Length; i++)
        {
            InGameSettingsMenuController controller = controllers[i];
            if (controller != null && controller.gameObject.scene == scene)
                return controller;
        }

        return null;
    }
}
