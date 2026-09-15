using System;
using UnityEngine;

[CreateAssetMenu(fileName = "FootstepSurfaceResolver", menuName = "Audio/Footstep Surface Resolver")]
public class FootstepSurfaceResolver : ScriptableObject
{
    [Serializable]
    public sealed class PhysicsMaterialSurface
    {
        [SerializeField] private PhysicsMaterial material;
        [SerializeField] private FootstepSurfaceDefinition surface;

        public PhysicsMaterial Material => material;
        public FootstepSurfaceDefinition Surface => surface;
    }

    [Serializable]
    public sealed class RendererMaterialSurface
    {
        [SerializeField] private Material material;
        [SerializeField] private FootstepSurfaceDefinition surface;

        public Material Material => material;
        public FootstepSurfaceDefinition Surface => surface;
    }

    [Serializable]
    public sealed class TerrainLayerSurface
    {
        [SerializeField] private TerrainLayer terrainLayer;
        [SerializeField] private FootstepSurfaceDefinition surface;

        public TerrainLayer TerrainLayer => terrainLayer;
        public FootstepSurfaceDefinition Surface => surface;
    }

    [Header("Fallback")]
    [SerializeField] private FootstepSurfaceDefinition defaultSurface;

    [Header("Surface Lookup")]
    [SerializeField] private bool searchParentSurface = true;
    [SerializeField] private RendererMaterialSurface[] rendererMaterialSurfaces = Array.Empty<RendererMaterialSurface>();
    [SerializeField] private PhysicsMaterialSurface[] physicsMaterialSurfaces = Array.Empty<PhysicsMaterialSurface>();
    [SerializeField] private TerrainLayerSurface[] terrainLayerSurfaces = Array.Empty<TerrainLayerSurface>();

    public FootstepSurfaceDefinition DefaultSurface => defaultSurface;

    public AudioCue ResolveCue(RaycastHit hit, FootstepGait gait, AudioCue fallbackCue = null)
    {
        FootstepSurfaceDefinition surface = ResolveSurface(hit);
        if (surface == null)
            return fallbackCue;

        AudioCue cue = surface.GetCue(gait);
        return cue != null ? cue : fallbackCue;
    }

    public AudioCue ResolveDefaultCue(FootstepGait gait, AudioCue fallbackCue = null)
    {
        if (defaultSurface == null)
            return fallbackCue;

        AudioCue cue = defaultSurface.GetCue(gait);
        return cue != null ? cue : fallbackCue;
    }

    public FootstepSurfaceDefinition ResolveSurface(RaycastHit hit)
    {
        Collider hitCollider = hit.collider;
        if (hitCollider == null)
            return defaultSurface;

        FootstepSurface markedSurface = ResolveMarkedSurface(hitCollider);
        if (markedSurface != null && markedSurface.Surface != null)
            return markedSurface.Surface;

        if (TryResolveTerrainLayerSurface(hit, out FootstepSurfaceDefinition terrainSurface))
            return terrainSurface;

        if (TryResolveRendererMaterialSurface(hitCollider, out FootstepSurfaceDefinition rendererMaterialSurface))
            return rendererMaterialSurface;

        if (TryResolvePhysicsMaterialSurface(hitCollider.sharedMaterial, out FootstepSurfaceDefinition materialSurface))
            return materialSurface;

        return defaultSurface;
    }

    private FootstepSurface ResolveMarkedSurface(Collider hitCollider)
    {
        if (hitCollider == null)
            return null;

        return searchParentSurface
            ? hitCollider.GetComponentInParent<FootstepSurface>()
            : hitCollider.GetComponent<FootstepSurface>();
    }

    private bool TryResolvePhysicsMaterialSurface(PhysicsMaterial material, out FootstepSurfaceDefinition surface)
    {
        surface = null;
        if (material == null || physicsMaterialSurfaces == null)
            return false;

        for (int i = 0; i < physicsMaterialSurfaces.Length; i++)
        {
            PhysicsMaterialSurface entry = physicsMaterialSurfaces[i];
            if (entry == null || entry.Material == null || entry.Surface == null)
                continue;

            if (entry.Material == material)
            {
                surface = entry.Surface;
                return true;
            }
        }

        return false;
    }

    private bool TryResolveRendererMaterialSurface(Collider hitCollider, out FootstepSurfaceDefinition surface)
    {
        surface = null;
        if (hitCollider == null || rendererMaterialSurfaces == null)
            return false;

        Renderer renderer = searchParentSurface
            ? hitCollider.GetComponentInParent<Renderer>()
            : hitCollider.GetComponent<Renderer>();

        if (renderer == null)
            return false;

        Material[] sharedMaterials = renderer.sharedMaterials;
        if (sharedMaterials == null || sharedMaterials.Length == 0)
            return false;

        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            Material sharedMaterial = sharedMaterials[i];
            if (sharedMaterial == null)
                continue;

            if (TryResolveRendererMaterialSurface(sharedMaterial, out surface))
                return true;
        }

        return false;
    }

    private bool TryResolveRendererMaterialSurface(Material material, out FootstepSurfaceDefinition surface)
    {
        surface = null;
        if (material == null || rendererMaterialSurfaces == null)
            return false;

        for (int i = 0; i < rendererMaterialSurfaces.Length; i++)
        {
            RendererMaterialSurface entry = rendererMaterialSurfaces[i];
            if (entry == null || entry.Material == null || entry.Surface == null)
                continue;

            if (entry.Material == material)
            {
                surface = entry.Surface;
                return true;
            }
        }

        return false;
    }

    private bool TryResolveTerrainLayerSurface(RaycastHit hit, out FootstepSurfaceDefinition surface)
    {
        surface = null;

        TerrainCollider terrainCollider = hit.collider as TerrainCollider;
        if (terrainCollider == null || terrainCollider.terrainData == null || terrainLayerSurfaces == null)
            return false;

        TerrainData terrainData = terrainCollider.terrainData;
        TerrainLayer[] terrainLayers = terrainData.terrainLayers;
        if (terrainLayers == null || terrainLayers.Length == 0)
            return false;

        int layerIndex = GetDominantTerrainLayerIndex(terrainData, terrainCollider.transform, hit.point);
        if (layerIndex < 0 || layerIndex >= terrainLayers.Length)
            return false;

        TerrainLayer terrainLayer = terrainLayers[layerIndex];
        if (terrainLayer == null)
            return false;

        for (int i = 0; i < terrainLayerSurfaces.Length; i++)
        {
            TerrainLayerSurface entry = terrainLayerSurfaces[i];
            if (entry == null || entry.TerrainLayer == null || entry.Surface == null)
                continue;

            if (entry.TerrainLayer == terrainLayer)
            {
                surface = entry.Surface;
                return true;
            }
        }

        return false;
    }

    private static int GetDominantTerrainLayerIndex(TerrainData terrainData, Transform terrainTransform, Vector3 worldPosition)
    {
        if (terrainData == null || terrainTransform == null || terrainData.alphamapWidth <= 0 || terrainData.alphamapHeight <= 0)
            return -1;

        Vector3 localPosition = terrainTransform.InverseTransformPoint(worldPosition);
        Vector3 terrainSize = terrainData.size;
        if (terrainSize.x <= 0f || terrainSize.z <= 0f)
            return -1;

        float normalizedX = Mathf.Clamp01(localPosition.x / terrainSize.x);
        float normalizedZ = Mathf.Clamp01(localPosition.z / terrainSize.z);
        int alphamapX = Mathf.Clamp(Mathf.RoundToInt(normalizedX * (terrainData.alphamapWidth - 1)), 0, terrainData.alphamapWidth - 1);
        int alphamapZ = Mathf.Clamp(Mathf.RoundToInt(normalizedZ * (terrainData.alphamapHeight - 1)), 0, terrainData.alphamapHeight - 1);

        float[,,] samples = terrainData.GetAlphamaps(alphamapX, alphamapZ, 1, 1);
        int layerCount = samples.GetLength(2);
        int bestLayerIndex = -1;
        float bestWeight = 0f;

        for (int i = 0; i < layerCount; i++)
        {
            float weight = samples[0, 0, i];
            if (weight > bestWeight)
            {
                bestWeight = weight;
                bestLayerIndex = i;
            }
        }

        return bestLayerIndex;
    }
}
