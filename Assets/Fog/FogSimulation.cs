using System.Collections.Generic;
using System.Runtime.InteropServices;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class FogSimulation : MonoBehaviour
{
    private const int ThreadGroupSize = 8;
    private const string DefaultComputePath = "Assets/Fog/FogSim.compute";
    private const string DefaultMaterialPath = "Assets/Fog/FogVolume.mat";
    private static readonly int DensityTextureProperty = Shader.PropertyToID("_DensityTex");

    [Header("References")]
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Material fogMaterial;
    [SerializeField] private Renderer fogRenderer;
    [SerializeField] private Transform player;

    [Header("Simulation")]
    [SerializeField, Min(16)] private int resolution = 256;
    [SerializeField, Min(0.1f)] private float areaSize = 30f;
    [SerializeField] private bool useTransformSize = true;
    [SerializeField] private bool followPlayerHorizontally = true;

    [Header("Player Interaction")]
    [SerializeField, Min(0f)] private float playerRadius = 1.8f;
    [SerializeField, Range(0f, 10f)] private float pushStrength = 4.5f;
    [SerializeField, Range(0f, 10f)] private float swirlStrength = 1.5f;
    [SerializeField, Min(0.05f)] private float targetSearchInterval = 0.25f;

    [Header("Fog Motion")]
    [SerializeField] private Vector2 wind = new Vector2(0.02f, 0.008f);
    [SerializeField, Range(0f, 2f)] private float regrowRate = 0.25f;
    [SerializeField, Range(0f, 5f)] private float warmupSeconds = 4f;

    [Header("Obstacles")]
    [SerializeField] private bool useFogObstacleComponents = true;
    [SerializeField] private bool autoCollectSceneColliders = true;
    [SerializeField] private LayerMask obstacleLayerMask = ~0;
    [SerializeField, Min(0)] private int maxObstacles = 96;
    [SerializeField, Min(0f)] private float obstaclePadding = 0.2f;
    [SerializeField, Min(0f)] private float minAutoObstacleHeight = 0.5f;
    [SerializeField, Min(0f)] private float maxAutoObstacleRadius = 3.5f;
    [SerializeField, Min(0.05f)] private float obstacleSourceRefreshInterval = 0.75f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    [StructLayout(LayoutKind.Sequential)]
    public struct ObstacleData
    {
        public Vector2 position;
        public float radius;
    }

    private readonly List<FogObstacle> manualObstacleSources = new List<FogObstacle>();
    private readonly List<Collider> autoObstacleSources = new List<Collider>();
    private readonly List<ObstacleData> obstacleData = new List<ObstacleData>();

    private RenderTexture[] density;
    private ComputeBuffer obstacleBuffer;
    private MaterialPropertyBlock propertyBlock;
    private int current;
    private int kernel;
    private int groups;
    private bool initialized;
    private bool warnedMissingPlayer;
    private float nextPlayerSearchTime;
    private float nextObstacleSourceRefreshTime;
    private Vector3 previousPlayerPosition;

    private void Reset()
    {
        ResolveSerializedReferences();
        ConfigureRendererForVolume();
    }

    private void OnValidate()
    {
        resolution = Mathf.Max(16, resolution);
        areaSize = Mathf.Max(0.1f, areaSize);
        playerRadius = Mathf.Max(0f, playerRadius);
        targetSearchInterval = Mathf.Max(0.05f, targetSearchInterval);
        warmupSeconds = Mathf.Max(0f, warmupSeconds);
        maxObstacles = Mathf.Max(0, maxObstacles);
        obstaclePadding = Mathf.Max(0f, obstaclePadding);
        minAutoObstacleHeight = Mathf.Max(0f, minAutoObstacleHeight);
        maxAutoObstacleRadius = Mathf.Max(0f, maxAutoObstacleRadius);
        obstacleSourceRefreshInterval = Mathf.Max(0.05f, obstacleSourceRefreshInterval);

        ResolveSerializedReferences();
        ConfigureRendererForVolume();
    }

    private void Start()
    {
        InitializeRuntime();
    }

    private void Update()
    {
        if (!initialized)
            InitializeRuntime();

        if (!initialized)
            return;

        Step(Time.deltaTime);
    }

    private void OnDisable()
    {
        ReleaseRuntimeResources();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeResources();
    }

    public void RefreshObstacleSources()
    {
        manualObstacleSources.Clear();
        autoObstacleSources.Clear();

        if (useFogObstacleComponents)
        {
            FogObstacle[] obstacles = FindObjectsByType<FogObstacle>(FindObjectsInactive.Exclude);
            for (int i = 0; i < obstacles.Length; i++)
            {
                FogObstacle obstacle = obstacles[i];
                if (obstacle == null || obstacle.transform.IsChildOf(transform))
                    continue;

                manualObstacleSources.Add(obstacle);
            }
        }

        if (!autoCollectSceneColliders)
            return;

        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Exclude);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider candidate = colliders[i];
            if (IsUsableAutoObstacle(candidate))
                autoObstacleSources.Add(candidate);
        }
    }

    private void InitializeRuntime()
    {
        ResolveSerializedReferences();

        if (compute == null)
        {
            Debug.LogError("FogSimulation: Compute shader nao configurado. Use GameObject > VFX > Interactive Fog Volume ou atribua Assets/Fog/FogSim.compute.", this);
            enabled = false;
            return;
        }

        if (fogRenderer == null)
        {
            Debug.LogError("FogSimulation: Renderer nao encontrado no volume de fog.", this);
            enabled = false;
            return;
        }

        if (fogMaterial == null)
            fogMaterial = fogRenderer.sharedMaterial;

        try
        {
            kernel = compute.FindKernel("CSMain");
        }
        catch
        {
            Debug.LogError("FogSimulation: kernel CSMain nao existe em Assets/Fog/FogSim.compute.", compute);
            enabled = false;
            return;
        }

        groups = Mathf.CeilToInt(resolution / (float)ThreadGroupSize);
        CreateDensityTextures();
        RefreshObstacleSources();

        ResolvePlayer(forceSearch: true);
        previousPlayerPosition = player != null ? player.position : transform.position;

        int warmupSteps = Mathf.Clamp(Mathf.CeilToInt(warmupSeconds / 0.1f), 0, 120);
        for (int i = 0; i < warmupSteps; i++)
            StepInternal(0.1f, allowTargetSearch: false);

        initialized = true;
    }

    private void CreateDensityTextures()
    {
        ReleaseDensityTextures();

        density = new RenderTexture[2];
        for (int i = 0; i < density.Length; i++)
        {
            density[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = $"{name}_FogDensity_{i}"
            };
            density[i].Create();
        }

        current = 0;
    }

    private void Step(float deltaTime)
    {
        StepInternal(deltaTime, allowTargetSearch: true);
    }

    private void StepInternal(float deltaTime, bool allowTargetSearch)
    {
        if (density == null || density.Length < 2 || compute == null)
            return;

        if (allowTargetSearch)
            ResolvePlayer(forceSearch: false);

        if (player != null && followPlayerHorizontally)
            FollowPlayerHorizontally();

        if (allowTargetSearch && Time.time >= nextObstacleSourceRefreshTime)
        {
            RefreshObstacleSources();
            nextObstacleSourceRefreshTime = Time.time + obstacleSourceRefreshInterval;
        }

        UpdateObstacleBuffer();

        int next = 1 - current;
        bool hasPlayer = player != null;
        Vector2 playerUv = hasPlayer ? WorldToUv(player.position) : new Vector2(-10f, -10f);
        Vector2 previousPlayerUv = hasPlayer ? WorldToUv(previousPlayerPosition) : playerUv;
        Vector2 playerVelocityUv = hasPlayer && deltaTime > 0f ? (playerUv - previousPlayerUv) / deltaTime : Vector2.zero;

        compute.SetInt("_Resolution", resolution);
        compute.SetFloat("_PlayerActive", hasPlayer ? 1f : 0f);
        compute.SetVector("_PlayerPos", playerUv);
        compute.SetVector("_PlayerVel", playerVelocityUv);
        compute.SetFloat("_PlayerRadius", WorldRadiusToUv(playerRadius));
        compute.SetVector("_Wind", wind);
        compute.SetFloat("_Push", pushStrength);
        compute.SetFloat("_Swirl", swirlStrength);
        compute.SetFloat("_Regrow", regrowRate);
        compute.SetFloat("t", Time.time);
        compute.SetFloat("dt", Mathf.Max(0f, deltaTime));
        compute.SetTexture(kernel, "Source", density[current]);
        compute.SetTexture(kernel, "Result", density[next]);
        compute.Dispatch(kernel, groups, groups, 1);

        ApplyDensityTexture(density[next]);
        current = next;

        if (hasPlayer)
            previousPlayerPosition = player.position;
    }

    private void ResolvePlayer(bool forceSearch)
    {
        if (!forceSearch && player != null && player.gameObject.activeInHierarchy)
            return;

        if (!forceSearch && Time.time < nextPlayerSearchTime)
            return;

        nextPlayerSearchTime = Time.time + targetSearchInterval;
        Transform resolvedPlayer = FindLocalPlayer();
        if (resolvedPlayer != null)
        {
            player = resolvedPlayer;
            previousPlayerPosition = player.position;
            warnedMissingPlayer = false;
            return;
        }

        if (!warnedMissingPlayer)
        {
            Debug.LogWarning("FogSimulation: aguardando o player local ser instanciado para a fog interativa.", this);
            warnedMissingPlayer = true;
        }
    }

    private Transform FindLocalPlayer()
    {
        PlayerSetup[] playerSetups = FindObjectsByType<PlayerSetup>(FindObjectsInactive.Exclude);
        for (int i = 0; i < playerSetups.Length; i++)
        {
            PlayerSetup setup = playerSetups[i];
            if (setup == null)
                continue;

            PhotonView view = setup.GetComponent<PhotonView>();
            if (view == null || view.IsMine)
                return setup.transform;
        }

        if (PhotonNetwork.InRoom)
            return null;

        GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (taggedPlayer != null)
            return taggedPlayer.transform;

        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>(FindObjectsInactive.Exclude);
        return movement != null ? movement.transform : null;
    }

    private void FollowPlayerHorizontally()
    {
        Vector3 position = transform.position;
        transform.position = new Vector3(player.position.x, position.y, player.position.z);
    }

    private void UpdateObstacleBuffer()
    {
        obstacleData.Clear();

        if (maxObstacles > 0)
        {
            for (int i = 0; i < manualObstacleSources.Count && obstacleData.Count < maxObstacles; i++)
            {
                FogObstacle obstacle = manualObstacleSources[i];
                if (obstacle == null || !obstacle.gameObject.activeInHierarchy)
                    continue;

                AddObstacle(obstacle.transform.position, obstacle.Radius);
            }

            for (int i = 0; i < autoObstacleSources.Count && obstacleData.Count < maxObstacles; i++)
            {
                Collider obstacleCollider = autoObstacleSources[i];
                if (!IsUsableAutoObstacle(obstacleCollider))
                    continue;

                Bounds bounds = obstacleCollider.bounds;
                float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + obstaclePadding;
                if (maxAutoObstacleRadius > 0f)
                    radius = Mathf.Min(radius, maxAutoObstacleRadius);

                AddObstacle(bounds.center, radius);
            }
        }

        int uploadCount = Mathf.Max(1, obstacleData.Count);
        int stride = Marshal.SizeOf(typeof(ObstacleData));
        if (obstacleBuffer == null || obstacleBuffer.count < uploadCount)
        {
            obstacleBuffer?.Release();
            obstacleBuffer = new ComputeBuffer(uploadCount, stride);
        }

        ObstacleData[] upload = new ObstacleData[uploadCount];
        for (int i = 0; i < obstacleData.Count; i++)
            upload[i] = obstacleData[i];

        obstacleBuffer.SetData(upload);
        compute.SetInt("_ObstacleCount", obstacleData.Count);
        compute.SetBuffer(kernel, "_Obstacles", obstacleBuffer);
    }

    private void AddObstacle(Vector3 worldPosition, float worldRadius)
    {
        if (worldRadius <= 0f)
            return;

        float radiusUv = WorldRadiusToUv(worldRadius);
        Vector2 uv = WorldToUv(worldPosition);
        if (uv.x < -radiusUv || uv.x > 1f + radiusUv || uv.y < -radiusUv || uv.y > 1f + radiusUv)
            return;

        obstacleData.Add(new ObstacleData
        {
            position = uv,
            radius = Mathf.Max(0.001f, radiusUv)
        });
    }

    private bool IsUsableAutoObstacle(Collider candidate)
    {
        if (candidate == null || !candidate.enabled || candidate.isTrigger)
            return false;

        if (candidate.transform.IsChildOf(transform))
            return false;

        if (!LayerIsInMask(candidate.gameObject.layer, obstacleLayerMask))
            return false;

        if (IsIgnoredRuntimeActor(candidate.transform))
            return false;

        Rigidbody attachedBody = candidate.attachedRigidbody;
        if (attachedBody != null && !attachedBody.isKinematic)
            return false;

        if (candidate.GetComponentInParent<FogObstacle>() != null)
            return false;

        Bounds bounds = candidate.bounds;
        if (!IsFinite(bounds.center) || !IsFinite(bounds.size))
            return false;

        if (bounds.size.y < minAutoObstacleHeight)
            return false;

        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        if (radius <= 0.05f)
            return false;

        if (maxAutoObstacleRadius > 0f && radius > maxAutoObstacleRadius * 2f)
            return false;

        float radiusUv = WorldRadiusToUv(radius + obstaclePadding);
        Vector2 uv = WorldToUv(bounds.center);
        return uv.x >= -radiusUv && uv.x <= 1f + radiusUv && uv.y >= -radiusUv && uv.y <= 1f + radiusUv;
    }

    private static bool IsIgnoredRuntimeActor(Transform candidate)
    {
        if (candidate == null)
            return true;

        string layerName = LayerMask.LayerToName(candidate.gameObject.layer);
        if (layerName == "Player" || layerName == "Enemy" || layerName == "FirstPersonView" || layerName == "Ignore Raycast")
            return true;

        if (candidate.CompareTag("Player"))
            return true;

        return candidate.GetComponentInParent<PlayerSetup>() != null
            || candidate.GetComponentInParent<PlayerMovement>() != null;
    }

    private Vector2 WorldToUv(Vector3 worldPosition)
    {
        if (useTransformSize)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return new Vector2(local.x + 0.5f, local.z + 0.5f);
        }

        Vector3 localOffset = worldPosition - transform.position;
        return new Vector2(localOffset.x / areaSize + 0.5f, localOffset.z / areaSize + 0.5f);
    }

    private float WorldRadiusToUv(float worldRadius)
    {
        float planarSize = useTransformSize
            ? Mathf.Max(0.001f, (Mathf.Abs(transform.lossyScale.x) + Mathf.Abs(transform.lossyScale.z)) * 0.5f)
            : Mathf.Max(0.001f, areaSize);

        return worldRadius / planarSize;
    }

    private void ApplyDensityTexture(RenderTexture densityTexture)
    {
        if (densityTexture == null)
            return;

        if (fogMaterial != null)
            fogMaterial.SetTexture(DensityTextureProperty, densityTexture);

        if (fogRenderer == null)
            return;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        fogRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetTexture(DensityTextureProperty, densityTexture);
        fogRenderer.SetPropertyBlock(propertyBlock);
    }

    private void ResolveSerializedReferences()
    {
        if (fogRenderer == null)
            TryGetComponent(out fogRenderer);

        if (fogMaterial == null && fogRenderer != null)
            fogMaterial = fogRenderer.sharedMaterial;

#if UNITY_EDITOR
        if (compute == null)
            compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(DefaultComputePath);

        if (fogMaterial == null)
            fogMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
#endif
    }

    private void ConfigureRendererForVolume()
    {
        if (fogRenderer == null)
            return;

        fogRenderer.shadowCastingMode = ShadowCastingMode.Off;
        fogRenderer.receiveShadows = false;
    }

    private void ReleaseRuntimeResources()
    {
        initialized = false;
        obstacleBuffer?.Release();
        obstacleBuffer = null;
        ReleaseDensityTextures();
    }

    private void ReleaseDensityTextures()
    {
        if (density == null)
            return;

        for (int i = 0; i < density.Length; i++)
        {
            if (density[i] != null)
                density[i].Release();
        }

        density = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = new Color(0.4f, 0.75f, 1f, 0.45f);
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = previousMatrix;
    }

    private static bool LayerIsInMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
