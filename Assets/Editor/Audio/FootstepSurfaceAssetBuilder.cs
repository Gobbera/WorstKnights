using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class FootstepSurfaceAssetBuilder
{
    private const string SourceRoot = "Assets/Footsteps - Essentials";
    private const string OutputRoot = "Assets/Resources/Audio/Footsteps/Generated";
    private const string ResolverAssetPath = "Assets/Resources/Audio/FootstepSurfaceResolver_Default.asset";

    [MenuItem("Tools/Audio/Footsteps/Rebuild Default Surface Resolver")]
    public static void RebuildDefaultSurfaceResolver()
    {
        if (!AssetDatabase.IsValidFolder(SourceRoot))
        {
            Debug.LogWarning($"[FootstepSurfaceAssetBuilder] Source folder not found: {SourceRoot}");
            return;
        }

        EnsureFolderPath(OutputRoot);

        Dictionary<string, FootstepSurfaceDefinition> generatedSurfaces = new Dictionary<string, FootstepSurfaceDefinition>(StringComparer.OrdinalIgnoreCase);
        string[] surfaceFolders = AssetDatabase.GetSubFolders(SourceRoot);
        Array.Sort(surfaceFolders, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < surfaceFolders.Length; i++)
        {
            string surfaceFolder = surfaceFolders[i];
            string surfaceName = GetSurfaceName(surfaceFolder);
            FootstepSurfaceDefinition surface = CreateOrUpdateSurface(surfaceFolder, surfaceName);
            if (surface != null)
                generatedSurfaces[surfaceName] = surface;
        }

        FootstepSurfaceResolver resolver = CreateOrLoadAsset<FootstepSurfaceResolver>(ResolverAssetPath);
        SerializedObject resolverObject = new SerializedObject(resolver);
        SetObjectReference(resolverObject, "defaultSurface", PickDefaultSurface(generatedSurfaces));
        SetBool(resolverObject, "searchParentSurface", true);
        ClearArray(resolverObject, "rendererMaterialSurfaces");
        ClearArray(resolverObject, "physicsMaterialSurfaces");
        ClearArray(resolverObject, "terrainLayerSurfaces");
        resolverObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(resolver);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[FootstepSurfaceAssetBuilder] Generated {generatedSurfaces.Count} footstep surfaces and updated {ResolverAssetPath}.");
    }

    private static FootstepSurfaceDefinition CreateOrUpdateSurface(string surfaceFolder, string surfaceName)
    {
        string walkFolder = FindChildFolder(surfaceFolder, "Walk");
        string runFolder = FindChildFolder(surfaceFolder, "Run");
        string jumpFolder = FindChildFolder(surfaceFolder, "Jump");

        List<AudioClip> walkClips = LoadAudioClips(walkFolder);
        List<AudioClip> runClips = LoadAudioClips(runFolder);
        List<AudioClip> jumpClips = LoadAudioClips(jumpFolder);
        List<AudioClip> jumpStartClips = FilterClipsByName(jumpClips, "Start");
        List<AudioClip> landClips = FilterClipsByName(jumpClips, "Land");

        if (jumpClips.Count > 0 && jumpStartClips.Count == 0 && landClips.Count == 0)
        {
            jumpStartClips.AddRange(jumpClips);
            landClips.AddRange(jumpClips);
        }

        if (walkClips.Count == 0 && runClips.Count == 0 && jumpClips.Count == 0)
            return null;

        string safeName = SanitizeAssetName(surfaceName);
        string cueFolder = $"{OutputRoot}/Cues/{safeName}";
        EnsureFolderPath(cueFolder);

        AudioCue walkCue = CreateOrUpdateCue($"{cueFolder}/Footstep_{safeName}_Walk.asset", walkClips, 0.25f, new Vector2(0.95f, 1.08f));
        AudioCue sprintCue = CreateOrUpdateCue($"{cueFolder}/Footstep_{safeName}_Sprint.asset", runClips.Count > 0 ? runClips : walkClips, 0.38f, new Vector2(0.95f, 1.12f));
        AudioCue crouchCue = CreateOrUpdateCue($"{cueFolder}/Footstep_{safeName}_Crouch.asset", walkClips, 0.09f, new Vector2(0.9f, 1.02f));
        AudioCue enemyWalkCue = CreateOrUpdateCue($"{cueFolder}/Footstep_{safeName}_EnemyWalk.asset", walkClips.Count > 0 ? walkClips : runClips, 0.25f, new Vector2(0.88f, 1.02f));
        AudioCue jumpCue = CreateOrUpdateCue($"{cueFolder}/Footstep_{safeName}_Jump.asset", jumpStartClips, 0.38f, new Vector2(0.95f, 1.1f));
        AudioCue landCue = CreateOrUpdateCue($"{cueFolder}/Footstep_{safeName}_Land.asset", landClips, 0.216f, new Vector2(0.9f, 1.05f));

        AudioCue defaultCue = walkCue != null ? walkCue : sprintCue != null ? sprintCue : enemyWalkCue != null ? enemyWalkCue : landCue != null ? landCue : jumpCue;
        string surfacePath = $"{OutputRoot}/FootstepSurface_{safeName}.asset";
        FootstepSurfaceDefinition surface = CreateOrLoadAsset<FootstepSurfaceDefinition>(surfacePath);
        SerializedObject surfaceObject = new SerializedObject(surface);
        SetObjectReference(surfaceObject, "defaultCue", defaultCue);
        SetObjectReference(surfaceObject, "walk", walkCue);
        SetObjectReference(surfaceObject, "sprint", sprintCue);
        SetObjectReference(surfaceObject, "crouch", crouchCue);
        SetObjectReference(surfaceObject, "enemyWalk", enemyWalkCue);
        SetObjectReference(surfaceObject, "jump", jumpCue);
        SetObjectReference(surfaceObject, "land", landCue);
        surfaceObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(surface);

        return surface;
    }

    private static AudioCue CreateOrUpdateCue(string assetPath, List<AudioClip> clips, float baseVolume, Vector2 pitchRange)
    {
        if (clips == null || clips.Count == 0)
            return null;

        AudioCue cue = CreateOrLoadAsset<AudioCue>(assetPath);
        SerializedObject cueObject = new SerializedObject(cue);
        SetAudioClipArray(cueObject, "clips", clips);
        SetInt(cueObject, "space", (int)AudioSpace.ThreeD);
        SetInt(cueObject, "replication", (int)AudioReplicationMode.LocalOnly);
        SetInt(cueObject, "anchor", (int)AudioPlaybackAnchor.PositionSnapshot);
        SetBool(cueObject, "loop", false);
        SetFloat(cueObject, "baseVolume", baseVolume);
        SetVector2(cueObject, "randomVolumeRange", new Vector2(0.95f, 1.05f));
        SetFloat(cueObject, "basePitch", 1f);
        SetVector2(cueObject, "randomPitchRange", pitchRange);
        SetFloat(cueObject, "spatialBlend", 1f);
        SetFloat(cueObject, "spread", 0f);
        SetFloat(cueObject, "minDistance", 1f);
        SetFloat(cueObject, "maxDistance", 15f);
        SetFloat(cueObject, "cooldown", 0f);
        cueObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(cue);
        return cue;
    }

    private static List<AudioClip> LoadAudioClips(string folder)
    {
        List<AudioClip> clips = new List<AudioClip>();
        if (string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder))
            return clips;

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
        List<string> paths = new List<string>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
            paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));

        paths.Sort(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < paths.Count; i++)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
            if (clip != null)
                clips.Add(clip);
        }

        return clips;
    }

    private static List<AudioClip> FilterClipsByName(List<AudioClip> clips, string namePart)
    {
        List<AudioClip> matches = new List<AudioClip>();
        if (clips == null || string.IsNullOrWhiteSpace(namePart))
            return matches;

        for (int i = 0; i < clips.Count; i++)
        {
            AudioClip clip = clips[i];
            if (clip != null && clip.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                matches.Add(clip);
        }

        return matches;
    }

    private static string FindChildFolder(string parentFolder, string namePart)
    {
        if (string.IsNullOrWhiteSpace(parentFolder) || !AssetDatabase.IsValidFolder(parentFolder))
            return null;

        string[] folders = AssetDatabase.GetSubFolders(parentFolder);
        for (int i = 0; i < folders.Length; i++)
        {
            string folderName = Path.GetFileName(folders[i]);
            if (!string.IsNullOrEmpty(folderName) && folderName.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                return folders[i];
        }

        return null;
    }

    private static FootstepSurfaceDefinition PickDefaultSurface(Dictionary<string, FootstepSurfaceDefinition> surfaces)
    {
        string[] preferredNames = { "Tile", "Rock", "DirtyGround", "Grass", "Wood" };
        for (int i = 0; i < preferredNames.Length; i++)
        {
            if (surfaces.TryGetValue(preferredNames[i], out FootstepSurfaceDefinition surface) && surface != null)
                return surface;
        }

        foreach (FootstepSurfaceDefinition surface in surfaces.Values)
        {
            if (surface != null)
                return surface;
        }

        return null;
    }

    private static string GetSurfaceName(string surfaceFolder)
    {
        string name = Path.GetFileName(surfaceFolder);
        const string prefix = "Footsteps_";
        return !string.IsNullOrEmpty(name) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name.Substring(prefix.Length)
            : name;
    }

    private static string SanitizeAssetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Surface";

        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] chars = name.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
                chars[i] = '_';
        }

        return new string(chars);
    }

    private static T CreateOrLoadAsset<T>(string assetPath) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset != null)
            return asset;

        EnsureFolderPath(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, assetPath);
        return asset;
    }

    private static void EnsureFolderPath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
            return;

        string normalizedPath = folderPath.Replace('\\', '/');
        string[] parts = normalizedPath.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
            return;

        string currentPath = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = $"{currentPath}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
                AssetDatabase.CreateFolder(currentPath, parts[i]);

            currentPath = nextPath;
        }
    }

    private static void SetAudioClipArray(SerializedObject serializedObject, string propertyName, List<AudioClip> clips)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            return;

        property.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    }

    private static void ClearArray(SerializedObject serializedObject, string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.arraySize = 0;
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }

    private static void SetInt(SerializedObject serializedObject, string propertyName, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.intValue = value;
    }

    private static void SetFloat(SerializedObject serializedObject, string propertyName, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.floatValue = value;
    }

    private static void SetVector2(SerializedObject serializedObject, string propertyName, Vector2 value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.vector2Value = value;
    }
}
