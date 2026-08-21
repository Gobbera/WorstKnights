using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class InvalidSceneValueScanner
{
    private const string Prefix = "[InvalidSceneValueScanner]";
    private const string ScanMenuPath = "Tools/Diagnostics/Scan Invalid Scene Values";
    private const string SerializedScanMenuPath = "Tools/Diagnostics/Scan Serialized Invalid Values";
    private const string MonitorMenuPath = "Tools/Diagnostics/Monitor Invalid Scene Values";
    private const string MonitorSessionKey = "KWK.InvalidSceneValueScanner.MonitorEnabled";
    private const float HugeValueThreshold = 1000000f;
    private const double MonitorIntervalSeconds = 1.0;
    private const double LogTriggeredScanCooldownSeconds = 1.0;
    private const double EditModePostPlayMonitorSeconds = 5.0;
    private const int ManualLogLimit = 200;
    private const int MonitorLogLimit = 40;
    private const string TextureTilingProperty = "_Texture_Tiling";

    private static readonly Regex SerializedNonFiniteValuePattern = new Regex(
        @"(^|[,{]\s*|\s)[A-Za-z_][A-Za-z0-9_]*\s*:\s*[+-]?(?:NaN|Infinity|\.nan|\.inf)(?=\s|[,}#]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SerializedScanExtensions = new HashSet<string>
    {
        ".anim",
        ".asset",
        ".controller",
        ".mat",
        ".overridecontroller",
        ".playable",
        ".prefab",
        ".shadergraph",
        ".shadersubgraph",
        ".unity"
    };

    private static readonly HashSet<string> monitorLoggedSignatures = new HashSet<string>();
    private static readonly List<Vector3> meshVector3Buffer = new List<Vector3>();
    private static readonly List<Vector4> meshVector4Buffer = new List<Vector4>();
    private static ParticleSystem.Particle[] particleBuffer = Array.Empty<ParticleSystem.Particle>();
    private static bool monitorEnabled;
    private static bool logTriggeredScanQueued;
    private static bool postPlayEditModeScanQueued;
    private static double nextMonitorScanTime;
    private static double lastLogTriggeredScanTime;
    private static double editModeMonitorUntil;

    static InvalidSceneValueScanner()
    {
        monitorEnabled = SessionState.GetBool(MonitorSessionKey, false);
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        Application.logMessageReceived += OnUnityLogMessage;
    }

    [MenuItem(ScanMenuPath)]
    private static void ScanOnce()
    {
        ScanOptions options = new ScanOptions
        {
            MaxLoggedIssues = ManualLogLimit,
            KnownSignatures = null
        };

        ScanResult result = ScanLoadedObjects(options);
        LogSummary(result, "Manual scan");

        if (result.FirstContext != null)
            Selection.activeObject = result.FirstContext;

        SerializedScanResult serializedResult = ScanSerializedAssets(ManualLogLimit);
        LogSerializedSummary(serializedResult, "Serialized asset scan");

        if (result.FirstContext == null && serializedResult.FirstAsset != null)
            Selection.activeObject = serializedResult.FirstAsset;
    }

    [MenuItem(SerializedScanMenuPath)]
    private static void ScanSerializedAssetsFromMenu()
    {
        SerializedScanResult result = ScanSerializedAssets(ManualLogLimit);
        LogSerializedSummary(result, "Manual serialized asset scan");

        if (result.FirstAsset != null)
            Selection.activeObject = result.FirstAsset;
    }

    [MenuItem(MonitorMenuPath)]
    private static void ToggleMonitor()
    {
        monitorEnabled = !monitorEnabled;
        SessionState.SetBool(MonitorSessionKey, monitorEnabled);
        monitorLoggedSignatures.Clear();
        nextMonitorScanTime = 0.0;
        Menu.SetChecked(MonitorMenuPath, monitorEnabled);

        string state = monitorEnabled ? "enabled" : "disabled";
        Debug.Log($"{Prefix} Play Mode monitor {state}.");
    }

    [MenuItem(MonitorMenuPath, true)]
    private static bool ValidateMonitor()
    {
        Menu.SetChecked(MonitorMenuPath, monitorEnabled);
        return true;
    }

    private static void OnEditorUpdate()
    {
        if (!monitorEnabled || !ShouldRunPeriodicMonitorScan())
            return;

        double now = EditorApplication.timeSinceStartup;
        if (now < nextMonitorScanTime)
            return;

        nextMonitorScanTime = now + MonitorIntervalSeconds;

        ScanOptions options = new ScanOptions
        {
            MaxLoggedIssues = MonitorLogLimit,
            KnownSignatures = monitorLoggedSignatures
        };

        ScanResult result = ScanLoadedObjects(options);
        if (result.IssueCount == 0)
        {
            monitorLoggedSignatures.Clear();
            return;
        }

        if (result.LoggedIssueCount > 0)
            LogSummary(result, EditorApplication.isPlaying ? "Play Mode monitor" : "Edit Mode monitor");
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
            editModeMonitorUntil = EditorApplication.timeSinceStartup + EditModePostPlayMonitorSeconds;

        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            monitorLoggedSignatures.Clear();
            logTriggeredScanQueued = false;
        }

        if (state == PlayModeStateChange.EnteredEditMode)
        {
            editModeMonitorUntil = EditorApplication.timeSinceStartup + EditModePostPlayMonitorSeconds;

            if (monitorEnabled)
                QueuePostPlayEditModeScan();
        }
    }

    private static void OnUnityLogMessage(string condition, string stackTrace, LogType type)
    {
        if (!monitorEnabled)
            return;

        if (string.IsNullOrEmpty(condition)
            || condition.StartsWith(Prefix, StringComparison.Ordinal)
            || !IsInvalidBoundsUnityLog(condition))
        {
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        if (!EditorApplication.isPlaying)
            editModeMonitorUntil = now + EditModePostPlayMonitorSeconds;

        if (logTriggeredScanQueued || now - lastLogTriggeredScanTime < LogTriggeredScanCooldownSeconds)
            return;

        lastLogTriggeredScanTime = now;
        logTriggeredScanQueued = true;
        EditorApplication.delayCall += RunLogTriggeredScan;
    }

    private static void RunLogTriggeredScan()
    {
        logTriggeredScanQueued = false;
        if (!monitorEnabled || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        ScanOptions options = new ScanOptions
        {
            MaxLoggedIssues = MonitorLogLimit,
            KnownSignatures = monitorLoggedSignatures
        };

        ScanResult result = ScanLoadedObjects(options);
        if (result.IssueCount > 0)
        {
            if (result.LoggedIssueCount > 0)
                LogSummary(result, $"Unity invalid-bounds log trigger{GetActiveEditorContextSuffix()}");

            return;
        }

        Debug.LogWarning(
            $"{Prefix} Unity logged Invalid AABB/IsFinite{GetActiveEditorContextSuffix()}, but immediate scan found 0 persistent invalid values. " +
            "This points to a one-frame or render-thread value; keep the monitor enabled and enable Error Pause to catch the exact frame.");
    }

    private static bool ShouldRunPeriodicMonitorScan()
    {
        if (EditorApplication.isPlaying)
            return true;

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return false;

        if (EditorApplication.timeSinceStartup < editModeMonitorUntil)
            return true;

        return PrefabStageUtility.GetCurrentPrefabStage() != null;
    }

    private static void QueuePostPlayEditModeScan()
    {
        if (postPlayEditModeScanQueued)
            return;

        postPlayEditModeScanQueued = true;
        EditorApplication.delayCall += RunPostPlayEditModeScan;
    }

    private static void RunPostPlayEditModeScan()
    {
        postPlayEditModeScanQueued = false;
        if (!monitorEnabled || EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        ScanOptions options = new ScanOptions
        {
            MaxLoggedIssues = MonitorLogLimit,
            KnownSignatures = monitorLoggedSignatures
        };

        ScanResult result = ScanLoadedObjects(options);
        if (result.IssueCount > 0)
        {
            if (result.LoggedIssueCount > 0)
                LogSummary(result, $"Post-Play Edit Mode scan{GetActiveEditorContextSuffix()}");

            return;
        }

        Debug.Log($"{Prefix} Post-Play Edit Mode scan{GetActiveEditorContextSuffix()}: 0 issue(s).");
    }

    private static bool IsInvalidBoundsUnityLog(string condition)
    {
        return condition.IndexOf("Invalid AABB", StringComparison.OrdinalIgnoreCase) >= 0
            || condition.IndexOf("IsFinite(distanceForSort)", StringComparison.OrdinalIgnoreCase) >= 0
            || condition.IndexOf("IsFinite(distanceAlongView)", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static ScanResult ScanLoadedObjects(ScanOptions options)
    {
        ScanState state = new ScanState(options);

        Transform[] transforms = FindLoadedComponents<Transform>();
        for (int i = 0; i < transforms.Length; i++)
            ScanTransform(transforms[i], state);

        Renderer[] renderers = FindLoadedComponents<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
            ScanRenderer(renderers[i], state);

        Collider[] colliders = FindLoadedComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            ScanCollider(colliders[i], state);

        Rigidbody[] rigidbodies = FindLoadedComponents<Rigidbody>();
        for (int i = 0; i < rigidbodies.Length; i++)
            ScanRigidbody(rigidbodies[i], state);

        LineRenderer[] lineRenderers = FindLoadedComponents<LineRenderer>();
        for (int i = 0; i < lineRenderers.Length; i++)
            ScanLineRenderer(lineRenderers[i], state);

        TrailRenderer[] trailRenderers = FindLoadedComponents<TrailRenderer>();
        for (int i = 0; i < trailRenderers.Length; i++)
            ScanTrailRenderer(trailRenderers[i], state);

        ParticleSystem[] particleSystems = FindLoadedComponents<ParticleSystem>();
        for (int i = 0; i < particleSystems.Length; i++)
            ScanParticleSystem(particleSystems[i], state);

        Cloth[] cloths = FindLoadedComponents<Cloth>();
        for (int i = 0; i < cloths.Length; i++)
            ScanCloth(cloths[i], state);

        return state.ToResult();
    }

    private static T[] FindLoadedComponents<T>() where T : Component
    {
        return Resources.FindObjectsOfTypeAll<T>();
    }

    private static SerializedScanResult ScanSerializedAssets(int maxLoggedIssues)
    {
        SerializedScanResult result = new SerializedScanResult
        {
            MaxLoggedIssues = maxLoggedIssues
        };

        if (!Directory.Exists("Assets"))
            return result;

        foreach (string filePath in Directory.EnumerateFiles("Assets", "*.*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (!SerializedScanExtensions.Contains(extension))
                continue;

            string assetPath = filePath.Replace('\\', '/');
            result.AssetCount++;

            try
            {
                int lineNumber = 0;
                foreach (string line in File.ReadLines(filePath))
                {
                    lineNumber++;
                    if (!SerializedNonFiniteValuePattern.IsMatch(line))
                        continue;

                    result.IssueCount++;
                    Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    if (result.FirstAsset == null && asset != null)
                        result.FirstAsset = asset;

                    if (result.LoggedIssueCount >= result.MaxLoggedIssues)
                        continue;

                    result.LoggedIssueCount++;
                    Debug.LogWarning(
                        $"{Prefix} Serialized invalid numeric value: {assetPath}:{lineNumber} - {line.Trim()}",
                        asset);
                }
            }
            catch (System.Exception exception)
            {
                result.ReadErrorCount++;
                if (result.LoggedIssueCount < result.MaxLoggedIssues)
                {
                    result.LoggedIssueCount++;
                    Debug.LogWarning($"{Prefix} Could not scan serialized asset {assetPath}: {exception.Message}");
                }
            }
        }

        return result;
    }

    private static void ScanTransform(Transform transform, ScanState state)
    {
        if (transform == null || !IsSceneObject(transform.gameObject))
            return;

        state.TransformCount++;
        ValidateVector3(state, transform, "Transform.localPosition", transform.gameObject, transform.localPosition);
        ValidateQuaternion(state, transform, "Transform.localRotation", transform.gameObject, transform.localRotation);
        ValidateVector3(state, transform, "Transform.localScale", transform.gameObject, transform.localScale);
        ValidateVector3(state, transform, "Transform.position", transform.gameObject, transform.position);
        ValidateQuaternion(state, transform, "Transform.rotation", transform.gameObject, transform.rotation);
        ValidateVector3(state, transform, "Transform.lossyScale", transform.gameObject, transform.lossyScale);
    }

    private static void ScanRenderer(Renderer renderer, ScanState state)
    {
        if (renderer == null || !IsSceneObject(renderer.gameObject))
            return;

        state.RendererCount++;
        ValidateBounds(state, renderer, "Renderer.bounds", renderer.gameObject, renderer.bounds);
        ScanRendererTextureTiling(renderer, state);

        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
            ScanMesh(meshFilter.sharedMesh, meshFilter, "MeshFilter.sharedMesh", renderer.gameObject, state);

        SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
        if (skinnedMeshRenderer != null)
        {
            ValidateBounds(state, skinnedMeshRenderer, "SkinnedMeshRenderer.localBounds", renderer.gameObject, skinnedMeshRenderer.localBounds);
            ScanSkinnedMeshRenderer(skinnedMeshRenderer, state);
        }
    }

    private static void ScanSkinnedMeshRenderer(SkinnedMeshRenderer skinnedMeshRenderer, ScanState state)
    {
        if (skinnedMeshRenderer == null)
            return;

        Mesh sharedMesh = skinnedMeshRenderer.sharedMesh;
        if (sharedMesh == null)
            return;

        ScanMesh(sharedMesh, skinnedMeshRenderer, "SkinnedMeshRenderer.sharedMesh", skinnedMeshRenderer.gameObject, state);

        int blendShapeCount = sharedMesh.blendShapeCount;
        for (int i = 0; i < blendShapeCount; i++)
        {
            float weight = skinnedMeshRenderer.GetBlendShapeWeight(i);
            if (!IsFinite(weight) || IsHuge(weight))
            {
                ReportIssue(
                    state,
                    skinnedMeshRenderer,
                    "SkinnedMeshRenderer.blendShapeWeight",
                    skinnedMeshRenderer.gameObject,
                    $"index={i}, value={Format(weight)}");
                return;
            }
        }
    }

    private static void ScanMesh(Mesh mesh, Object context, string label, GameObject gameObject, ScanState state)
    {
        if (mesh == null)
            return;

        if (!state.ScannedMeshes.Add(mesh))
            return;

        state.MeshCount++;
        ValidateBounds(state, context, $"{label}.bounds", gameObject, mesh.bounds);

        if (!mesh.isReadable)
            return;

        try
        {
            mesh.GetVertices(meshVector3Buffer);
            ValidateVector3List(state, context, $"{label}.vertices", gameObject, meshVector3Buffer);

            mesh.GetNormals(meshVector3Buffer);
            ValidateVector3List(state, context, $"{label}.normals", gameObject, meshVector3Buffer);

            mesh.GetTangents(meshVector4Buffer);
            ValidateVector4List(state, context, $"{label}.tangents", gameObject, meshVector4Buffer);

            for (int channel = 0; channel < 8; channel++)
            {
                mesh.GetUVs(channel, meshVector4Buffer);
                ValidateVector4List(state, context, $"{label}.uv{channel}", gameObject, meshVector4Buffer);
            }

            Matrix4x4[] bindposes = mesh.bindposes;
            for (int i = 0; i < bindposes.Length; i++)
            {
                if (!IsFinite(bindposes[i]) || IsHuge(bindposes[i]))
                {
                    ReportIssue(state, context, $"{label}.bindposes", gameObject, $"index={i}, value={Format(bindposes[i])}");
                    return;
                }
            }
        }
        catch (UnityException exception)
        {
            ReportIssue(state, context, $"{label}.readableData", gameObject, $"could not scan mesh data: {exception.Message}");
        }
        finally
        {
            meshVector3Buffer.Clear();
            meshVector4Buffer.Clear();
        }
    }

    private static void ScanRendererTextureTiling(Renderer renderer, ScanState state)
    {
        Material[] materials = renderer.sharedMaterials;
        bool usesTextureTiling = false;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null || !material.HasProperty(TextureTilingProperty))
                continue;

            usesTextureTiling = true;
            ValidateVector4(
                state,
                renderer,
                $"Material[{i}].{TextureTilingProperty}",
                renderer.gameObject,
                material.GetVector(TextureTilingProperty));
        }

        if (!usesTextureTiling)
            return;

        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(propertyBlock);
        if (!propertyBlock.isEmpty)
        {
            ValidateVector4(
                state,
                renderer,
                $"MaterialPropertyBlock.{TextureTilingProperty}",
                renderer.gameObject,
                propertyBlock.GetVector(TextureTilingProperty));
        }
    }

    private static void ScanCollider(Collider collider, ScanState state)
    {
        if (collider == null || !IsSceneObject(collider.gameObject))
            return;

        state.ColliderCount++;
        ValidateBounds(state, collider, "Collider.bounds", collider.gameObject, collider.bounds);

        MeshCollider meshCollider = collider as MeshCollider;
        if (meshCollider != null && meshCollider.sharedMesh != null)
            ScanMesh(meshCollider.sharedMesh, meshCollider, "MeshCollider.sharedMesh", collider.gameObject, state);
    }

    private static void ScanRigidbody(Rigidbody rigidbody, ScanState state)
    {
        if (rigidbody == null || !IsSceneObject(rigidbody.gameObject))
            return;

        state.RigidbodyCount++;
        ValidateVector3(state, rigidbody, "Rigidbody.position", rigidbody.gameObject, rigidbody.position);
        ValidateQuaternion(state, rigidbody, "Rigidbody.rotation", rigidbody.gameObject, rigidbody.rotation);
        ValidateVector3(state, rigidbody, "Rigidbody.linearVelocity", rigidbody.gameObject, rigidbody.linearVelocity);
        ValidateVector3(state, rigidbody, "Rigidbody.angularVelocity", rigidbody.gameObject, rigidbody.angularVelocity);
        ValidateVector3(state, rigidbody, "Rigidbody.centerOfMass", rigidbody.gameObject, rigidbody.centerOfMass);
        ValidateVector3(state, rigidbody, "Rigidbody.inertiaTensor", rigidbody.gameObject, rigidbody.inertiaTensor);
    }

    private static void ScanLineRenderer(LineRenderer lineRenderer, ScanState state)
    {
        if (lineRenderer == null || !IsSceneObject(lineRenderer.gameObject))
            return;

        int positionCount = lineRenderer.positionCount;
        for (int i = 0; i < positionCount; i++)
        {
            Vector3 position = lineRenderer.GetPosition(i);
            if (!IsFinite(position) || IsHuge(position))
            {
                ReportIssue(state, lineRenderer, "LineRenderer.position", lineRenderer.gameObject, $"index={i}, value={Format(position)}");
                return;
            }
        }
    }

    private static void ScanTrailRenderer(TrailRenderer trailRenderer, ScanState state)
    {
        if (trailRenderer == null || !IsSceneObject(trailRenderer.gameObject))
            return;

        int positionCount = trailRenderer.positionCount;
        if (positionCount <= 0)
            return;

        Vector3[] positions = new Vector3[positionCount];
        int copiedCount = trailRenderer.GetPositions(positions);
        for (int i = 0; i < copiedCount; i++)
        {
            if (!IsFinite(positions[i]) || IsHuge(positions[i]))
            {
                ReportIssue(state, trailRenderer, "TrailRenderer.position", trailRenderer.gameObject, $"index={i}, value={Format(positions[i])}");
                return;
            }
        }
    }

    private static void ScanParticleSystem(ParticleSystem particleSystem, ScanState state)
    {
        if (particleSystem == null || !IsSceneObject(particleSystem.gameObject))
            return;

        ParticleSystem.MainModule main = particleSystem.main;
        if (!main.stopAction.Equals(ParticleSystemStopAction.None))
        {
            // Touching the module keeps this method from looking unused in older IDEs and makes it easy to expand later.
        }

        state.ParticleSystemCount++;

        int particleCount = particleSystem.particleCount;
        if (particleCount <= 0)
            return;

        if (particleBuffer.Length < particleCount)
            particleBuffer = new ParticleSystem.Particle[particleCount];

        int copiedCount = particleSystem.GetParticles(particleBuffer, particleCount);
        for (int i = 0; i < copiedCount; i++)
        {
            ParticleSystem.Particle particle = particleBuffer[i];
            if (!IsFinite(particle.position) || IsHuge(particle.position))
            {
                ReportIssue(state, particleSystem, "ParticleSystem.particle.position", particleSystem.gameObject, $"index={i}, value={Format(particle.position)}");
                return;
            }

            if (!IsFinite(particle.velocity) || IsHuge(particle.velocity))
            {
                ReportIssue(state, particleSystem, "ParticleSystem.particle.velocity", particleSystem.gameObject, $"index={i}, value={Format(particle.velocity)}");
                return;
            }

            if (!IsFinite(particle.axisOfRotation) || IsHuge(particle.axisOfRotation))
            {
                ReportIssue(state, particleSystem, "ParticleSystem.particle.axisOfRotation", particleSystem.gameObject, $"index={i}, value={Format(particle.axisOfRotation)}");
                return;
            }

            if (!IsFinite(particle.rotation3D) || IsHuge(particle.rotation3D))
            {
                ReportIssue(state, particleSystem, "ParticleSystem.particle.rotation3D", particleSystem.gameObject, $"index={i}, value={Format(particle.rotation3D)}");
                return;
            }

            if (!IsFinite(particle.angularVelocity3D) || IsHuge(particle.angularVelocity3D))
            {
                ReportIssue(state, particleSystem, "ParticleSystem.particle.angularVelocity3D", particleSystem.gameObject, $"index={i}, value={Format(particle.angularVelocity3D)}");
                return;
            }

            if (!IsFinite(particle.startSize3D) || IsHuge(particle.startSize3D))
            {
                ReportIssue(state, particleSystem, "ParticleSystem.particle.startSize3D", particleSystem.gameObject, $"index={i}, value={Format(particle.startSize3D)}");
                return;
            }

            if (!IsFinite(particle.remainingLifetime) || IsHuge(particle.remainingLifetime))
            {
                ReportIssue(state, particleSystem, "ParticleSystem.particle.remainingLifetime", particleSystem.gameObject, $"index={i}, value={Format(particle.remainingLifetime)}");
                return;
            }

            if (!IsFinite(particle.startLifetime) || IsHuge(particle.startLifetime))
            {
                ReportIssue(state, particleSystem, "ParticleSystem.particle.startLifetime", particleSystem.gameObject, $"index={i}, value={Format(particle.startLifetime)}");
                return;
            }
        }
    }

    private static void ScanCloth(Cloth cloth, ScanState state)
    {
        if (cloth == null || !IsSceneObject(cloth.gameObject))
            return;

        state.ClothCount++;
        ValidateVector3(state, cloth, "Cloth.externalAcceleration", cloth.gameObject, cloth.externalAcceleration);
        ValidateVector3(state, cloth, "Cloth.randomAcceleration", cloth.gameObject, cloth.randomAcceleration);
        ValidateClothFloat(state, cloth, "Cloth.worldVelocityScale", cloth.gameObject, cloth.worldVelocityScale, allowHuge: false);
        ValidateClothFloat(state, cloth, "Cloth.worldAccelerationScale", cloth.gameObject, cloth.worldAccelerationScale, allowHuge: false);
        ValidateClothFloat(state, cloth, "Cloth.friction", cloth.gameObject, cloth.friction, allowHuge: false);
        ValidateClothFloat(state, cloth, "Cloth.collisionMassScale", cloth.gameObject, cloth.collisionMassScale, allowHuge: false);
        ValidateClothFloat(state, cloth, "Cloth.sleepThreshold", cloth.gameObject, cloth.sleepThreshold, allowHuge: false);

        ClothSkinningCoefficient[] coefficients = cloth.coefficients;
        if (coefficients == null)
            return;

        for (int i = 0; i < coefficients.Length; i++)
        {
            ClothSkinningCoefficient coefficient = coefficients[i];
            if (!IsFinite(coefficient.maxDistance) || coefficient.maxDistance < 0f || IsHuge(coefficient.maxDistance))
            {
                ReportIssue(
                    state,
                    cloth,
                    "Cloth.coefficients.maxDistance",
                    cloth.gameObject,
                    $"index={i}, value={Format(coefficient.maxDistance)}");
                return;
            }

            if (!IsFinite(coefficient.collisionSphereDistance) || coefficient.collisionSphereDistance < 0f)
            {
                ReportIssue(
                    state,
                    cloth,
                    "Cloth.coefficients.collisionSphereDistance",
                    cloth.gameObject,
                    $"index={i}, value={Format(coefficient.collisionSphereDistance)}");
                return;
            }
        }
    }

    private static void ValidateBounds(ScanState state, Object context, string label, GameObject gameObject, Bounds bounds)
    {
        if (!IsFinite(bounds.center) || !IsFinite(bounds.extents))
        {
            ReportIssue(state, context, label, gameObject, $"invalid bounds center={Format(bounds.center)}, extents={Format(bounds.extents)}");
            return;
        }

        if (bounds.extents.x < 0f || bounds.extents.y < 0f || bounds.extents.z < 0f)
            ReportIssue(state, context, label, gameObject, $"negative extents={Format(bounds.extents)}");

        if (IsHuge(bounds.center) || IsHuge(bounds.extents))
            ReportIssue(state, context, label, gameObject, $"suspiciously huge bounds center={Format(bounds.center)}, extents={Format(bounds.extents)}");
    }

    private static void ValidateVector3(ScanState state, Object context, string label, GameObject gameObject, Vector3 value)
    {
        if (!IsFinite(value))
        {
            ReportIssue(state, context, label, gameObject, $"invalid value={Format(value)}");
            return;
        }

        if (IsHuge(value))
            ReportIssue(state, context, label, gameObject, $"suspiciously huge value={Format(value)}");
    }

    private static void ValidateVector4(ScanState state, Object context, string label, GameObject gameObject, Vector4 value)
    {
        if (!IsFinite(value))
        {
            ReportIssue(state, context, label, gameObject, $"invalid value={Format(value)}");
            return;
        }

        if (IsHuge(value))
            ReportIssue(state, context, label, gameObject, $"suspiciously huge value={Format(value)}");
    }

    private static void ValidateQuaternion(ScanState state, Object context, string label, GameObject gameObject, Quaternion value)
    {
        if (!IsFinite(value))
        {
            ReportIssue(state, context, label, gameObject, $"invalid value={Format(value)}");
            return;
        }

        if (IsHuge(value))
            ReportIssue(state, context, label, gameObject, $"suspiciously huge value={Format(value)}");
    }

    private static void ValidateClothFloat(
        ScanState state,
        Object context,
        string label,
        GameObject gameObject,
        float value,
        bool allowHuge)
    {
        if (!IsFinite(value))
        {
            ReportIssue(state, context, label, gameObject, $"invalid value={Format(value)}");
            return;
        }

        if (!allowHuge && IsHuge(value))
            ReportIssue(state, context, label, gameObject, $"suspiciously huge value={Format(value)}");
    }

    private static void ValidateVector3List(ScanState state, Object context, string label, GameObject gameObject, List<Vector3> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Vector3 value = values[i];
            if (!IsFinite(value) || IsHuge(value))
            {
                ReportIssue(state, context, label, gameObject, $"index={i}, value={Format(value)}");
                return;
            }
        }
    }

    private static void ValidateVector4List(ScanState state, Object context, string label, GameObject gameObject, List<Vector4> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            Vector4 value = values[i];
            if (!IsFinite(value) || IsHuge(value))
            {
                ReportIssue(state, context, label, gameObject, $"index={i}, value={Format(value)}");
                return;
            }
        }
    }

    private static void ReportIssue(ScanState state, Object context, string label, GameObject gameObject, string detail)
    {
        state.IssueCount++;

        string path = GetHierarchyPath(gameObject);
        string signature = $"{label}|{path}|{detail}";
        if (state.Options.KnownSignatures != null)
        {
            if (state.Options.KnownSignatures.Contains(signature))
                return;

            state.Options.KnownSignatures.Add(signature);
        }

        state.LoggedIssueCount++;
        if (state.FirstContext == null)
            state.FirstContext = context;

        if (state.LoggedIssueCount <= state.Options.MaxLoggedIssues)
            Debug.LogWarning($"{Prefix} {label}: {path} - {detail}", context);
    }

    private static void LogSummary(ScanResult result, string source)
    {
        string summary =
            $"{Prefix} {source}: {result.IssueCount} issue(s), " +
            $"{result.TransformCount} transforms, {result.RendererCount} renderers, " +
            $"{result.ColliderCount} colliders, {result.RigidbodyCount} rigidbodies, " +
            $"{result.MeshCount} meshes, {result.ParticleSystemCount} particle systems, " +
            $"{result.ClothCount} cloth components scanned.";

        if (result.IssueCount == 0)
        {
            Debug.Log(summary);
            return;
        }

        int omittedCount = Mathf.Max(0, result.LoggedIssueCount - result.MaxLoggedIssues);
        if (omittedCount > 0)
            summary += $" {omittedCount} new issue(s) omitted by log limit.";

        Debug.LogWarning(summary, result.FirstContext);
    }

    private static void LogSerializedSummary(SerializedScanResult result, string source)
    {
        string summary =
            $"{Prefix} {source}: {result.IssueCount} serialized non-finite numeric value(s), " +
            $"{result.AssetCount} asset(s) scanned, {result.ReadErrorCount} read error(s).";

        if (result.IssueCount == 0)
        {
            Debug.Log(summary);
            return;
        }

        int omittedCount = Mathf.Max(0, result.IssueCount - result.LoggedIssueCount);
        if (omittedCount > 0)
            summary += $" {omittedCount} issue(s) omitted by log limit.";

        Debug.LogWarning(summary, result.FirstAsset);
    }

    private static bool IsSceneObject(GameObject gameObject)
    {
        return gameObject != null && gameObject.scene.IsValid() && !EditorUtility.IsPersistent(gameObject);
    }

    private static string GetHierarchyPath(GameObject gameObject)
    {
        if (gameObject == null)
            return "<null>";

        List<string> names = new List<string>();
        Transform current = gameObject.transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        string sceneName = GetSceneLabel(gameObject);
        return $"{sceneName}:/{string.Join("/", names)}";
    }

    private static string GetSceneLabel(GameObject gameObject)
    {
        if (gameObject == null || !gameObject.scene.IsValid())
            return "<no scene>";

        PrefabStage prefabStage = PrefabStageUtility.GetPrefabStage(gameObject);
        if (prefabStage != null && !string.IsNullOrWhiteSpace(prefabStage.assetPath))
            return $"PrefabStage({prefabStage.assetPath})";

        if (!string.IsNullOrWhiteSpace(gameObject.scene.name))
            return gameObject.scene.name;

        if (!string.IsNullOrWhiteSpace(gameObject.scene.path))
            return gameObject.scene.path;

        return "<unnamed scene>";
    }

    private static string GetActiveEditorContextSuffix()
    {
        if (EditorApplication.isPlaying)
            return " (Play Mode)";

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null && !string.IsNullOrWhiteSpace(prefabStage.assetPath))
            return $" (Edit Mode, Prefab Stage: {prefabStage.assetPath})";

        return " (Edit Mode)";
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(Vector4 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    }

    private static bool IsFinite(Matrix4x4 value)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if (!IsFinite(value[row, column]))
                    return false;
            }
        }

        return true;
    }

    private static bool IsHuge(float value)
    {
        return Mathf.Abs(value) > HugeValueThreshold;
    }

    private static bool IsHuge(Vector3 value)
    {
        return Mathf.Abs(value.x) > HugeValueThreshold ||
            Mathf.Abs(value.y) > HugeValueThreshold ||
            Mathf.Abs(value.z) > HugeValueThreshold;
    }

    private static bool IsHuge(Vector4 value)
    {
        return Mathf.Abs(value.x) > HugeValueThreshold ||
            Mathf.Abs(value.y) > HugeValueThreshold ||
            Mathf.Abs(value.z) > HugeValueThreshold ||
            Mathf.Abs(value.w) > HugeValueThreshold;
    }

    private static bool IsHuge(Quaternion value)
    {
        return Mathf.Abs(value.x) > HugeValueThreshold ||
            Mathf.Abs(value.y) > HugeValueThreshold ||
            Mathf.Abs(value.z) > HugeValueThreshold ||
            Mathf.Abs(value.w) > HugeValueThreshold;
    }

    private static bool IsHuge(Matrix4x4 value)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if (IsHuge(value[row, column]))
                    return true;
            }
        }

        return false;
    }

    private static string Format(Vector3 value)
    {
        return $"({Format(value.x)}, {Format(value.y)}, {Format(value.z)})";
    }

    private static string Format(Vector4 value)
    {
        return $"({Format(value.x)}, {Format(value.y)}, {Format(value.z)}, {Format(value.w)})";
    }

    private static string Format(Quaternion value)
    {
        return $"({Format(value.x)}, {Format(value.y)}, {Format(value.z)}, {Format(value.w)})";
    }

    private static string Format(Matrix4x4 value)
    {
        return
            $"[{Format(value.m00)}, {Format(value.m01)}, {Format(value.m02)}, {Format(value.m03)}; " +
            $"{Format(value.m10)}, {Format(value.m11)}, {Format(value.m12)}, {Format(value.m13)}; " +
            $"{Format(value.m20)}, {Format(value.m21)}, {Format(value.m22)}, {Format(value.m23)}; " +
            $"{Format(value.m30)}, {Format(value.m31)}, {Format(value.m32)}, {Format(value.m33)}]";
    }

    private static string Format(float value)
    {
        return value.ToString("G9", CultureInfo.InvariantCulture);
    }

    private sealed class ScanOptions
    {
        public int MaxLoggedIssues;
        public HashSet<string> KnownSignatures;
    }

    private sealed class ScanState
    {
        public readonly ScanOptions Options;
        public readonly HashSet<Mesh> ScannedMeshes = new HashSet<Mesh>();
        public int TransformCount;
        public int RendererCount;
        public int ColliderCount;
        public int RigidbodyCount;
        public int MeshCount;
        public int ParticleSystemCount;
        public int ClothCount;
        public int IssueCount;
        public int LoggedIssueCount;
        public Object FirstContext;

        public ScanState(ScanOptions options)
        {
            Options = options;
        }

        public ScanResult ToResult()
        {
            return new ScanResult
            {
                TransformCount = TransformCount,
                RendererCount = RendererCount,
                ColliderCount = ColliderCount,
                RigidbodyCount = RigidbodyCount,
                MeshCount = MeshCount,
                ParticleSystemCount = ParticleSystemCount,
                ClothCount = ClothCount,
                IssueCount = IssueCount,
                LoggedIssueCount = LoggedIssueCount,
                MaxLoggedIssues = Options.MaxLoggedIssues,
                FirstContext = FirstContext
            };
        }
    }

    private struct ScanResult
    {
        public int TransformCount;
        public int RendererCount;
        public int ColliderCount;
        public int RigidbodyCount;
        public int MeshCount;
        public int ParticleSystemCount;
        public int ClothCount;
        public int IssueCount;
        public int LoggedIssueCount;
        public int MaxLoggedIssues;
        public Object FirstContext;
    }

    private struct SerializedScanResult
    {
        public int AssetCount;
        public int IssueCount;
        public int LoggedIssueCount;
        public int MaxLoggedIssues;
        public int ReadErrorCount;
        public Object FirstAsset;
    }
}
