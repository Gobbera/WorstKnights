using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class GameplaySceneRoot : MonoBehaviour
{
    private const string RootObjectName = "GameplaySceneRoot";
    private const string SpawnPointName = "SpawnPoint";

    private static GameplaySceneRoot cachedActiveRoot;

    [Header("Runtime Discovery")]
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private Camera[] sceneCameras;

    [Header("Scene Services")]
    [SerializeField] private bool disableSceneCamerasForLocalPlayer = true;
    [SerializeField] private bool autoDiscoverSpawnPoints = true;
    [SerializeField] private bool autoDiscoverSceneCameras = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapActiveGameplayScene()
    {
        GameplaySceneRoot root = TryGetActiveSceneRoot(createIfMissing: true);
        root?.AutoPopulateFromScene();
    }

    public static GameplaySceneRoot TryGetActiveSceneRoot(bool createIfMissing = false)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
            return null;

        if (cachedActiveRoot != null)
        {
            if (cachedActiveRoot.gameObject != null && cachedActiveRoot.gameObject.scene == activeScene)
                return cachedActiveRoot;

            cachedActiveRoot = null;
        }

        GameplaySceneRoot[] roots = FindObjectsByType<GameplaySceneRoot>(FindObjectsInactive.Include);
        for (int i = 0; i < roots.Length; i++)
        {
            GameplaySceneRoot root = roots[i];
            if (root == null || root.gameObject.scene != activeScene)
                continue;

            cachedActiveRoot = root;
            return root;
        }

        bool shouldForceBootstrap = GameplaySceneLoadState.ShouldForceGameplayBootstrap(activeScene);
        if (!createIfMissing || (!shouldForceBootstrap && !ShouldTreatAsGameplayScene(activeScene)))
            return null;

        GameObject rootObject = new GameObject(RootObjectName);
        SceneManager.MoveGameObjectToScene(rootObject, activeScene);
        cachedActiveRoot = rootObject.AddComponent<GameplaySceneRoot>();
        cachedActiveRoot.AutoPopulateFromScene();
        if (shouldForceBootstrap)
            Debug.Log($"GameplaySceneRoot: forced runtime bootstrap for scene '{activeScene.name}' using menu handoff state.");
        return cachedActiveRoot;
    }

    public static bool IsActiveGameplayScene()
    {
        return TryGetActiveSceneRoot(createIfMissing: true) != null;
    }

    public static void NotifyLocalPlayerReady(PlayerSetup localPlayer)
    {
        if (localPlayer == null)
            return;

        GameplaySceneRoot root = TryGetActiveSceneRoot(createIfMissing: true);
        root?.HandleLocalPlayerReady(localPlayer);
    }

    public Transform[] GetSpawnPoints()
    {
        AutoPopulateFromScene();
        return CompactTransforms(spawnPoints);
    }

    public GameObject GetPrimarySceneCameraObject()
    {
        AutoPopulateFromScene();
        Camera[] resolvedSceneCameras = CompactCameras(sceneCameras);
        if (resolvedSceneCameras.Length == 0)
            return null;

        return resolvedSceneCameras[0] != null ? resolvedSceneCameras[0].gameObject : null;
    }

    private void Awake()
    {
        if (cachedActiveRoot != null && cachedActiveRoot != this && cachedActiveRoot.gameObject.scene == gameObject.scene)
        {
            Destroy(gameObject);
            return;
        }

        cachedActiveRoot = this;
        AutoPopulateFromScene();
    }

    private void OnDestroy()
    {
        if (cachedActiveRoot == this)
            cachedActiveRoot = null;
    }

    private void HandleLocalPlayerReady(PlayerSetup localPlayer)
    {
        AutoPopulateFromScene();

        if (!disableSceneCamerasForLocalPlayer)
            return;

        DisableSceneCameras(localPlayer);
    }

    private void AutoPopulateFromScene()
    {
        if (autoDiscoverSpawnPoints && (spawnPoints == null || spawnPoints.Length == 0 || HasNullEntries(spawnPoints)))
            spawnPoints = DiscoverSpawnPoints();

        if (autoDiscoverSceneCameras && (sceneCameras == null || sceneCameras.Length == 0 || HasNullEntries(sceneCameras)))
            sceneCameras = DiscoverSceneCameras();
    }

    private void DisableSceneCameras(PlayerSetup localPlayer)
    {
        Camera[] resolvedSceneCameras = CompactCameras(sceneCameras);
        if (resolvedSceneCameras.Length == 0)
            return;

        Transform localPlayerTransform = localPlayer != null ? localPlayer.transform : null;
        for (int i = 0; i < resolvedSceneCameras.Length; i++)
        {
            Camera sceneCamera = resolvedSceneCameras[i];
            if (sceneCamera == null)
                continue;

            if (localPlayerTransform != null && sceneCamera.transform.IsChildOf(localPlayerTransform))
                continue;

            sceneCamera.enabled = false;

            AudioListener listener = sceneCamera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = false;
        }
    }

    private static bool ShouldTreatAsGameplayScene(Scene scene)
    {
        if (!scene.IsValid())
            return false;

        Transform[] sceneTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform sceneTransform = sceneTransforms[i];
            if (sceneTransform == null || sceneTransform.gameObject.scene != scene)
                continue;

            if (IsSpawnPointName(sceneTransform.name))
                return true;
        }

        return false;
    }

    private Transform[] DiscoverSpawnPoints()
    {
        List<Transform> discoveredSpawnPoints = new List<Transform>();
        Transform[] sceneTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform sceneTransform = sceneTransforms[i];
            if (sceneTransform == null || sceneTransform.gameObject.scene != gameObject.scene)
                continue;

            if (IsSpawnPointName(sceneTransform.name))
                discoveredSpawnPoints.Add(sceneTransform);
        }

        return discoveredSpawnPoints.Count > 0
            ? discoveredSpawnPoints.ToArray()
            : Array.Empty<Transform>();
    }

    private Camera[] DiscoverSceneCameras()
    {
        List<Camera> discoveredSceneCameras = new List<Camera>();
        Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
        for (int i = 0; i < allCameras.Length; i++)
        {
            Camera sceneCamera = allCameras[i];
            if (sceneCamera == null || sceneCamera.gameObject.scene != gameObject.scene)
                continue;

            if (HasAncestorOfType<PlayerSetup>(sceneCamera.transform))
                continue;

            discoveredSceneCameras.Add(sceneCamera);
        }

        return discoveredSceneCameras.Count > 0
            ? discoveredSceneCameras.ToArray()
            : Array.Empty<Camera>();
    }

    private static bool IsSpawnPointName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return false;

        return string.Equals(objectName, SpawnPointName, StringComparison.Ordinal)
            || objectName.StartsWith(SpawnPointName + " ", StringComparison.Ordinal)
            || objectName.StartsWith(SpawnPointName + "(", StringComparison.Ordinal);
    }

    private static bool HasNullEntries<T>(T[] values) where T : UnityEngine.Object
    {
        if (values == null || values.Length == 0)
            return true;

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
                return true;
        }

        return false;
    }

    private static Transform[] CompactTransforms(Transform[] values)
    {
        if (values == null || values.Length == 0)
            return Array.Empty<Transform>();

        int validCount = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] != null)
                validCount++;
        }

        if (validCount == 0)
            return Array.Empty<Transform>();

        if (validCount == values.Length)
            return values;

        Transform[] compacted = new Transform[validCount];
        int compactIndex = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
                continue;

            compacted[compactIndex++] = values[i];
        }

        return compacted;
    }

    private static Camera[] CompactCameras(Camera[] values)
    {
        if (values == null || values.Length == 0)
            return Array.Empty<Camera>();

        int validCount = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] != null)
                validCount++;
        }

        if (validCount == 0)
            return Array.Empty<Camera>();

        if (validCount == values.Length)
            return values;

        Camera[] compacted = new Camera[validCount];
        int compactIndex = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null)
                continue;

            compacted[compactIndex++] = values[i];
        }

        return compacted;
    }

    private static bool HasAncestorOfType<T>(Transform candidate) where T : Component
    {
        Transform current = candidate;
        while (current != null)
        {
            if (current.GetComponent<T>() != null)
                return true;

            current = current.parent;
        }

        return false;
    }
}

public static class GameplaySceneLoadState
{
    private const string PendingScenePathPlayerPrefsKey = "PendingGameplayScenePath";

    public static void MarkPendingSceneLoad(string scenePath)
    {
        string normalizedScenePath = NormalizeScenePath(scenePath);
        if (string.IsNullOrWhiteSpace(normalizedScenePath))
        {
            ClearPendingSceneLoad();
            return;
        }

        PlayerPrefs.SetString(PendingScenePathPlayerPrefsKey, normalizedScenePath);
        PlayerPrefs.Save();
    }

    public static void ClearPendingSceneLoad()
    {
        if (!PlayerPrefs.HasKey(PendingScenePathPlayerPrefsKey))
            return;

        PlayerPrefs.DeleteKey(PendingScenePathPlayerPrefsKey);
        PlayerPrefs.Save();
    }

    public static bool ShouldForceGameplayBootstrap(Scene scene)
    {
        if (!scene.IsValid())
            return false;

        string pendingScenePath = NormalizeScenePath(PlayerPrefs.GetString(PendingScenePathPlayerPrefsKey, string.Empty));
        if (string.IsNullOrWhiteSpace(pendingScenePath))
            return false;

        string activeScenePath = NormalizeScenePath(scene.path);
        if (!string.IsNullOrWhiteSpace(activeScenePath))
            return string.Equals(activeScenePath, pendingScenePath, StringComparison.OrdinalIgnoreCase);

        string pendingSceneName = Path.GetFileNameWithoutExtension(pendingScenePath);
        return !string.IsNullOrWhiteSpace(pendingSceneName)
            && string.Equals(scene.name, pendingSceneName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeScenePath(string scenePath)
    {
        return string.IsNullOrWhiteSpace(scenePath)
            ? string.Empty
            : scenePath.Trim().Replace('\\', '/');
    }
}
