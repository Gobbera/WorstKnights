using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ProBuilderCollisionTools
{
    private const string ProBuilderShapeTypeName = "UnityEngine.ProBuilder.Shapes.ProBuilderShape";
    private const string StairShapeTypeName = "UnityEngine.ProBuilder.Shapes.Stairs";
    private const string StairRampHelperName = "CollisionRamp";
    private const string BoundsBoxHelperName = "CollisionBox";
    private const string AutoColliderHelperName = "CollisionAuto";
    private const string AutoColliderPartPrefix = "AutoBox_";
    private const float MinimumColliderThickness = 0.05f;
    private const float MaximumColliderThickness = 0.35f;
    private const float MinimumAutoColliderSize = 0.05f;
    private const float AutoColliderTargetSliceLength = 0.75f;
    private const float AutoColliderMergeTolerance = 0.05f;
    private const int AutoColliderMinimumSliceCount = 1;
    private const int AutoColliderMaximumSliceCount = 12;

    [MenuItem("Tools/Level/Collision/Generate Stair Ramp Helpers")]
    private static void GenerateStairRampHelpers()
    {
        int updatedCount = 0;

        foreach (GameObject candidate in EnumerateSelectionHierarchy())
        {
            if (!TryGetProBuilderShape(candidate, out Component shapeComponent) || !IsStraightStairShape(shapeComponent))
                continue;

            if (TryCreateOrUpdateStairRamp(candidate))
                updatedCount++;
        }

        if (updatedCount == 0)
        {
            Debug.LogWarning("[ProBuilderCollisionTools] Nenhuma escada ProBuilder reta foi encontrada na selecao.");
            return;
        }

        Debug.Log($"[ProBuilderCollisionTools] {updatedCount} helper(s) de rampa criados/atualizados.");
    }

    [MenuItem("Tools/Level/Collision/Generate Stair Ramp Helpers", true)]
    private static bool ValidateGenerateStairRampHelpers()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    [MenuItem("Tools/Level/Collision/Generate Bounds Box Helpers")]
    private static void GenerateBoundsBoxHelpers()
    {
        int updatedCount = 0;

        foreach (GameObject candidate in EnumerateSelectionHierarchy())
        {
            if (!TryGetProBuilderShape(candidate, out _))
                continue;

            if (TryCreateOrUpdateBoundsBox(candidate))
                updatedCount++;
        }

        if (updatedCount == 0)
        {
            Debug.LogWarning("[ProBuilderCollisionTools] Nenhum objeto ProBuilder com MeshCollider foi encontrado na selecao.");
            return;
        }

        Debug.Log($"[ProBuilderCollisionTools] {updatedCount} helper(s) de BoxCollider criados/atualizados.");
    }

    [MenuItem("Tools/Level/Collision/Generate Bounds Box Helpers", true)]
    private static bool ValidateGenerateBoundsBoxHelpers()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    [MenuItem("Tools/Level/Collision/Generate Auto Collider Helpers")]
    private static void GenerateAutoColliderHelpers()
    {
        int updatedCount = 0;
        int colliderCount = 0;

        foreach (GameObject candidate in EnumerateSelectionHierarchy())
        {
            if (!TryCreateOrUpdateAutoCollider(candidate, out int createdColliderCount))
                continue;

            updatedCount++;
            colliderCount += createdColliderCount;
        }

        if (updatedCount == 0)
        {
            Debug.LogWarning("[ProBuilderCollisionTools] Nenhuma malha elegivel foi encontrada para gerar auto collider na selecao.");
            return;
        }

        Debug.Log($"[ProBuilderCollisionTools] {updatedCount} helper(s) de auto collider criados/atualizados com {colliderCount} BoxCollider(s).");
    }

    [MenuItem("Tools/Level/Collision/Generate Auto Collider Helpers", true)]
    private static bool ValidateGenerateAutoColliderHelpers()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    [MenuItem("Tools/Level/Collision/Restore Original Mesh Colliders")]
    private static void RestoreOriginalMeshColliders()
    {
        int restoredCount = 0;

        foreach (GameObject candidate in EnumerateSelectionHierarchy())
        {
            MeshCollider meshCollider = candidate.GetComponent<MeshCollider>();
            Transform rampHelper = candidate.transform.Find(StairRampHelperName);
            Transform boxHelper = candidate.transform.Find(BoundsBoxHelperName);
            Transform autoHelper = candidate.transform.Find(AutoColliderHelperName);

            bool changed = false;

            if (meshCollider != null && !meshCollider.enabled)
            {
                Undo.RecordObject(meshCollider, "Restore MeshCollider");
                meshCollider.enabled = true;
                EditorUtility.SetDirty(meshCollider);
                changed = true;
            }

            if (rampHelper != null)
            {
                Undo.DestroyObjectImmediate(rampHelper.gameObject);
                changed = true;
            }

            if (boxHelper != null)
            {
                Undo.DestroyObjectImmediate(boxHelper.gameObject);
                changed = true;
            }

            if (autoHelper != null)
            {
                Undo.DestroyObjectImmediate(autoHelper.gameObject);
                changed = true;
            }

            if (changed)
                restoredCount++;
        }

        if (restoredCount == 0)
        {
            Debug.LogWarning("[ProBuilderCollisionTools] Nenhum helper de colisao foi encontrado para restaurar.");
            return;
        }

        Debug.Log($"[ProBuilderCollisionTools] {restoredCount} objeto(s) restaurados para o MeshCollider original.");
    }

    [MenuItem("Tools/Level/Collision/Restore Original Mesh Colliders", true)]
    private static bool ValidateRestoreOriginalMeshColliders()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    private static IEnumerable<GameObject> EnumerateSelectionHierarchy()
    {
        HashSet<Transform> visited = new HashSet<Transform>();
        Stack<Transform> stack = new Stack<Transform>();

        foreach (GameObject selectedObject in Selection.gameObjects)
        {
            if (selectedObject == null)
                continue;

            stack.Push(selectedObject.transform);
        }

        while (stack.Count > 0)
        {
            Transform current = stack.Pop();
            if (current == null || !visited.Add(current))
                continue;

            yield return current.gameObject;

            for (int i = 0; i < current.childCount; i++)
                stack.Push(current.GetChild(i));
        }
    }

    private static bool TryCreateOrUpdateStairRamp(GameObject source)
    {
        if (!TryGetMeshSetup(source, out MeshCollider meshCollider, out Mesh sharedMesh))
            return false;

        if (!TryResolveStairRamp(source, sharedMesh, out Vector3 localCenter, out Quaternion localRotation, out Vector3 colliderSize))
            return false;

        GameObject helper = GetOrCreateHelperChild(source, StairRampHelperName);
        helper.layer = source.layer;

        Undo.RecordObject(helper.transform, "Update Stair Ramp Helper");
        helper.transform.localPosition = localCenter;
        helper.transform.localRotation = localRotation;
        helper.transform.localScale = Vector3.one;

        BoxCollider boxCollider = EnsureSingleBoxCollider(helper);
        Undo.RecordObject(boxCollider, "Update Stair Ramp Collider");
        boxCollider.isTrigger = false;
        boxCollider.center = Vector3.zero;
        boxCollider.size = colliderSize;

        DisableOriginalMeshCollider(meshCollider);
        EditorUtility.SetDirty(helper);
        return true;
    }

    private static bool TryCreateOrUpdateBoundsBox(GameObject source)
    {
        if (!TryGetMeshSetup(source, out MeshCollider meshCollider, out Mesh sharedMesh))
            return false;

        Bounds localBounds = sharedMesh.bounds;
        if (localBounds.size.sqrMagnitude <= 0.000001f)
            return false;

        GameObject helper = GetOrCreateHelperChild(source, BoundsBoxHelperName);
        helper.layer = source.layer;

        Undo.RecordObject(helper.transform, "Update Bounds Box Helper");
        helper.transform.localPosition = localBounds.center;
        helper.transform.localRotation = Quaternion.identity;
        helper.transform.localScale = Vector3.one;

        BoxCollider boxCollider = EnsureSingleBoxCollider(helper);
        Undo.RecordObject(boxCollider, "Update Bounds Box Collider");
        boxCollider.isTrigger = false;
        boxCollider.center = Vector3.zero;
        boxCollider.size = localBounds.size;

        DisableOriginalMeshCollider(meshCollider);
        EditorUtility.SetDirty(helper);
        return true;
    }

    private static bool TryCreateOrUpdateAutoCollider(GameObject source, out int createdColliderCount)
    {
        createdColliderCount = 0;

        if (!TryGetAutoColliderMeshSetup(source, out MeshCollider meshCollider, out Mesh sharedMesh))
            return false;

        if (!TryBuildAutoColliderDescriptors(sharedMesh, out List<AutoColliderDescriptor> descriptors) || descriptors.Count == 0)
            return false;

        GameObject helperRoot = GetOrCreateHelperChild(source, AutoColliderHelperName);
        helperRoot.layer = source.layer;

        Undo.RecordObject(helperRoot.transform, "Update Auto Collider Helper");
        helperRoot.transform.localPosition = Vector3.zero;
        helperRoot.transform.localRotation = Quaternion.identity;
        helperRoot.transform.localScale = Vector3.one;

        ClearHelperColliders(helperRoot);
        ClearHelperChildren(helperRoot);

        for (int i = 0; i < descriptors.Count; i++)
            CreateAutoColliderPart(helperRoot, source.layer, i, descriptors[i]);

        DisableOriginalMeshCollider(meshCollider);
        EditorUtility.SetDirty(helperRoot);
        createdColliderCount = descriptors.Count;
        return true;
    }

    private static bool TryResolveStairRamp(GameObject source, Mesh sharedMesh, out Vector3 localCenter, out Quaternion localRotation, out Vector3 colliderSize)
    {
        localCenter = Vector3.zero;
        localRotation = Quaternion.identity;
        colliderSize = Vector3.one;

        Bounds bounds = sharedMesh.bounds;
        if (bounds.size.y <= 0.0001f)
            return false;

        if (!TryResolveRunAxis(sharedMesh, bounds, out bool runOnX, out float runSign))
            return false;

        float runMin = runOnX ? bounds.min.x : bounds.min.z;
        float runMax = runOnX ? bounds.max.x : bounds.max.z;
        float startRun = runSign >= 0f ? runMin : runMax;
        float endRun = runSign >= 0f ? runMax : runMin;

        Vector3 start = runOnX
            ? new Vector3(startRun, bounds.min.y, bounds.center.z)
            : new Vector3(bounds.center.x, bounds.min.y, startRun);
        Vector3 end = runOnX
            ? new Vector3(endRun, bounds.max.y, bounds.center.z)
            : new Vector3(bounds.center.x, bounds.max.y, endRun);

        Vector3 slopeDirection = end - start;
        if (slopeDirection.sqrMagnitude <= 0.000001f)
            return false;

        float runLength = Mathf.Abs(endRun - startRun);
        float width = runOnX ? bounds.size.z : bounds.size.x;
        float slopeLength = slopeDirection.magnitude;
        if (runLength <= 0.0001f || width <= 0.0001f)
            return false;

        slopeDirection.Normalize();

        Vector3 widthAxis = runOnX ? Vector3.forward : Vector3.right;
        Vector3 surfaceNormal = Vector3.Cross(slopeDirection, widthAxis);
        if (surfaceNormal.sqrMagnitude <= 0.000001f)
            surfaceNormal = Vector3.up;
        else
            surfaceNormal.Normalize();

        if (Vector3.Dot(surfaceNormal, Vector3.up) < 0f)
            surfaceNormal = -surfaceNormal;

        float thickness = Mathf.Clamp(Mathf.Min(width, slopeLength) * 0.08f, MinimumColliderThickness, MaximumColliderThickness);
        Vector3 rampSurfaceMidpoint = (start + end) * 0.5f;

        localCenter = rampSurfaceMidpoint - surfaceNormal * (thickness * 0.5f);
        localRotation = Quaternion.LookRotation(slopeDirection, surfaceNormal);
        colliderSize = new Vector3(width, thickness, slopeLength);
        return true;
    }

    private static bool TryResolveRunAxis(Mesh sharedMesh, Bounds bounds, out bool runOnX, out float runSign)
    {
        runOnX = false;
        runSign = 1f;

        Vector3[] vertices = sharedMesh.vertices;
        if (vertices == null || vertices.Length == 0)
            return false;

        Vector3 mean = Vector3.zero;
        for (int i = 0; i < vertices.Length; i++)
            mean += vertices[i];

        mean /= vertices.Length;

        float covarianceXY = 0f;
        float covarianceZY = 0f;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 centered = vertices[i] - mean;
            covarianceXY += centered.x * centered.y;
            covarianceZY += centered.z * centered.y;
        }

        runOnX = Mathf.Abs(covarianceXY) > Mathf.Abs(covarianceZY);
        float covariance = runOnX ? covarianceXY : covarianceZY;

        if (Mathf.Abs(covariance) <= 0.0001f)
            runOnX = bounds.size.x > bounds.size.z;

        runSign = Mathf.Sign(covariance);
        if (Mathf.Approximately(runSign, 0f))
            runSign = 1f;

        return true;
    }

    private static bool TryBuildAutoColliderDescriptors(Mesh sharedMesh, out List<AutoColliderDescriptor> descriptors)
    {
        descriptors = new List<AutoColliderDescriptor>();

        if (sharedMesh == null)
            return false;

        Bounds bounds = sharedMesh.bounds;
        if (bounds.size.sqrMagnitude <= 0.000001f)
            return false;

        Vector3[] vertices = sharedMesh.vertices;
        int[] triangles = sharedMesh.triangles;

        if (vertices == null || vertices.Length == 0)
            return false;

        if (triangles == null || triangles.Length < 3)
        {
            descriptors.Add(CreateBoundsDescriptor(bounds));
            return true;
        }

        int primaryAxis = ResolvePrimaryAxis(bounds.size);
        GetSecondaryAxes(primaryAxis, out int secondaryAxisA, out int secondaryAxisB);

        float primarySize = GetAxis(bounds.size, primaryAxis);
        int sliceCount = Mathf.Clamp(
            Mathf.CeilToInt(primarySize / AutoColliderTargetSliceLength),
            AutoColliderMinimumSliceCount,
            AutoColliderMaximumSliceCount);

        if (sliceCount <= 1)
        {
            descriptors.Add(CreateBoundsDescriptor(bounds));
            return true;
        }

        AutoColliderSlice[] slices = CreateSlices(sliceCount);
        float boundsPrimaryMin = GetAxis(bounds.min, primaryAxis);
        float sliceLength = primarySize / sliceCount;

        for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
        {
            int vertexIndexA = triangles[triangleIndex];
            int vertexIndexB = triangles[triangleIndex + 1];
            int vertexIndexC = triangles[triangleIndex + 2];

            if (!IsValidVertexIndex(vertexIndexA, vertices.Length) ||
                !IsValidVertexIndex(vertexIndexB, vertices.Length) ||
                !IsValidVertexIndex(vertexIndexC, vertices.Length))
                continue;

            Bounds triangleBounds = CreateTriangleBounds(vertices[vertexIndexA], vertices[vertexIndexB], vertices[vertexIndexC]);
            float trianglePrimaryMin = GetAxis(triangleBounds.min, primaryAxis);
            float trianglePrimaryMax = GetAxis(triangleBounds.max, primaryAxis);
            int startSlice = GetSliceIndex(trianglePrimaryMin, boundsPrimaryMin, primarySize, sliceCount);
            int endSlice = GetSliceIndex(trianglePrimaryMax, boundsPrimaryMin, primarySize, sliceCount);

            float otherAMin = GetAxis(triangleBounds.min, secondaryAxisA);
            float otherAMax = GetAxis(triangleBounds.max, secondaryAxisA);
            float otherBMin = GetAxis(triangleBounds.min, secondaryAxisB);
            float otherBMax = GetAxis(triangleBounds.max, secondaryAxisB);

            for (int sliceIndex = startSlice; sliceIndex <= endSlice; sliceIndex++)
            {
                float sliceMin = boundsPrimaryMin + (sliceLength * sliceIndex);
                float sliceMax = sliceIndex == sliceCount - 1
                    ? boundsPrimaryMin + primarySize
                    : sliceMin + sliceLength;

                AutoColliderSlice slice = slices[sliceIndex];
                slice.Occupied = true;
                slice.PrimaryMin = Mathf.Min(slice.PrimaryMin, Mathf.Max(trianglePrimaryMin, sliceMin));
                slice.PrimaryMax = Mathf.Max(slice.PrimaryMax, Mathf.Min(trianglePrimaryMax, sliceMax));
                slice.OtherAMin = Mathf.Min(slice.OtherAMin, otherAMin);
                slice.OtherAMax = Mathf.Max(slice.OtherAMax, otherAMax);
                slice.OtherBMin = Mathf.Min(slice.OtherBMin, otherBMin);
                slice.OtherBMax = Mathf.Max(slice.OtherBMax, otherBMax);
                slices[sliceIndex] = slice;
            }
        }

        float secondaryToleranceA = Mathf.Max(AutoColliderMergeTolerance, GetAxis(bounds.size, secondaryAxisA) * 0.08f);
        float secondaryToleranceB = Mathf.Max(AutoColliderMergeTolerance, GetAxis(bounds.size, secondaryAxisB) * 0.08f);

        for (int sliceIndex = 0; sliceIndex < slices.Length; sliceIndex++)
        {
            if (!slices[sliceIndex].Occupied)
                continue;

            AutoColliderSlice mergedSlice = slices[sliceIndex];
            int lastSliceIndex = sliceIndex;

            while (lastSliceIndex + 1 < slices.Length &&
                   slices[lastSliceIndex + 1].Occupied &&
                   CanMergeSlices(mergedSlice, slices[lastSliceIndex + 1], secondaryToleranceA, secondaryToleranceB))
            {
                lastSliceIndex++;
                mergedSlice = MergeSlices(mergedSlice, slices[lastSliceIndex]);
            }

            descriptors.Add(CreateSliceDescriptor(primaryAxis, secondaryAxisA, secondaryAxisB, mergedSlice));
            sliceIndex = lastSliceIndex;
        }

        if (descriptors.Count == 0)
            descriptors.Add(CreateBoundsDescriptor(bounds));

        return true;
    }

    private static bool TryGetMeshSetup(GameObject source, out MeshCollider meshCollider, out Mesh sharedMesh)
    {
        meshCollider = source.GetComponent<MeshCollider>();
        sharedMesh = null;

        MeshFilter meshFilter = source.GetComponent<MeshFilter>();
        if (meshCollider == null || meshFilter == null || meshFilter.sharedMesh == null)
            return false;

        sharedMesh = meshFilter.sharedMesh;
        return true;
    }

    private static bool TryGetAutoColliderMeshSetup(GameObject source, out MeshCollider meshCollider, out Mesh sharedMesh)
    {
        meshCollider = source.GetComponent<MeshCollider>();
        sharedMesh = null;

        if (source == null || HasManualPrimitiveCollider(source))
            return false;

        MeshFilter meshFilter = source.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return false;

        sharedMesh = meshFilter.sharedMesh;
        return true;
    }

    private static void DisableOriginalMeshCollider(MeshCollider meshCollider)
    {
        if (meshCollider == null || !meshCollider.enabled)
            return;

        Undo.RecordObject(meshCollider, "Disable MeshCollider");
        meshCollider.enabled = false;
        EditorUtility.SetDirty(meshCollider);
    }

    private static GameObject GetOrCreateHelperChild(GameObject source, string helperName)
    {
        Transform existing = source.transform.Find(helperName);
        if (existing != null)
            return existing.gameObject;

        GameObject helper = new GameObject(helperName);
        Undo.RegisterCreatedObjectUndo(helper, $"Create {helperName}");
        helper.transform.SetParent(source.transform, false);
        return helper;
    }

    private static BoxCollider EnsureSingleBoxCollider(GameObject helper)
    {
        Collider[] colliders = helper.GetComponents<Collider>();
        BoxCollider boxCollider = null;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] is BoxCollider currentBox && boxCollider == null)
            {
                boxCollider = currentBox;
                continue;
            }

            Undo.DestroyObjectImmediate(colliders[i]);
        }

        if (boxCollider != null)
            return boxCollider;

        return Undo.AddComponent<BoxCollider>(helper);
    }

    private static void ClearHelperColliders(GameObject helper)
    {
        Collider[] colliders = helper.GetComponents<Collider>();

        for (int i = 0; i < colliders.Length; i++)
            Undo.DestroyObjectImmediate(colliders[i]);
    }

    private static void ClearHelperChildren(GameObject helper)
    {
        List<GameObject> childrenToRemove = new List<GameObject>();

        for (int i = 0; i < helper.transform.childCount; i++)
            childrenToRemove.Add(helper.transform.GetChild(i).gameObject);

        for (int i = 0; i < childrenToRemove.Count; i++)
            Undo.DestroyObjectImmediate(childrenToRemove[i]);
    }

    private static void CreateAutoColliderPart(GameObject helperRoot, int layer, int index, AutoColliderDescriptor descriptor)
    {
        GameObject part = new GameObject($"{AutoColliderPartPrefix}{index:00}");
        Undo.RegisterCreatedObjectUndo(part, "Create Auto Collider Part");
        part.layer = layer;
        part.transform.SetParent(helperRoot.transform, false);
        part.transform.localPosition = descriptor.Center;
        part.transform.localRotation = Quaternion.identity;
        part.transform.localScale = Vector3.one;

        BoxCollider boxCollider = Undo.AddComponent<BoxCollider>(part);
        boxCollider.isTrigger = false;
        boxCollider.center = Vector3.zero;
        boxCollider.size = descriptor.Size;
    }

    private static bool HasManualPrimitiveCollider(GameObject source)
    {
        Collider[] colliders = source.GetComponents<Collider>();

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger || collider is MeshCollider)
                continue;

            return true;
        }

        return false;
    }

    private static AutoColliderSlice[] CreateSlices(int count)
    {
        AutoColliderSlice[] slices = new AutoColliderSlice[count];

        for (int i = 0; i < count; i++)
        {
            slices[i] = new AutoColliderSlice
            {
                PrimaryMin = float.PositiveInfinity,
                PrimaryMax = float.NegativeInfinity,
                OtherAMin = float.PositiveInfinity,
                OtherAMax = float.NegativeInfinity,
                OtherBMin = float.PositiveInfinity,
                OtherBMax = float.NegativeInfinity,
            };
        }

        return slices;
    }

    private static bool IsValidVertexIndex(int vertexIndex, int vertexCount)
    {
        return vertexIndex >= 0 && vertexIndex < vertexCount;
    }

    private static Bounds CreateTriangleBounds(Vector3 a, Vector3 b, Vector3 c)
    {
        Bounds bounds = new Bounds(a, Vector3.zero);
        bounds.Encapsulate(b);
        bounds.Encapsulate(c);
        return bounds;
    }

    private static int ResolvePrimaryAxis(Vector3 size)
    {
        if (size.x >= size.y && size.x >= size.z)
            return 0;

        if (size.y >= size.z)
            return 1;

        return 2;
    }

    private static void GetSecondaryAxes(int primaryAxis, out int secondaryAxisA, out int secondaryAxisB)
    {
        switch (primaryAxis)
        {
            case 0:
                secondaryAxisA = 1;
                secondaryAxisB = 2;
                break;
            case 1:
                secondaryAxisA = 0;
                secondaryAxisB = 2;
                break;
            default:
                secondaryAxisA = 0;
                secondaryAxisB = 1;
                break;
        }
    }

    private static int GetSliceIndex(float axisValue, float boundsMin, float boundsSize, int sliceCount)
    {
        if (sliceCount <= 1 || boundsSize <= 0.0001f)
            return 0;

        float normalized = Mathf.InverseLerp(boundsMin, boundsMin + boundsSize, axisValue);
        return Mathf.Clamp(Mathf.FloorToInt(normalized * sliceCount), 0, sliceCount - 1);
    }

    private static bool CanMergeSlices(AutoColliderSlice current, AutoColliderSlice next, float toleranceA, float toleranceB)
    {
        return Mathf.Abs(current.OtherAMin - next.OtherAMin) <= toleranceA &&
               Mathf.Abs(current.OtherAMax - next.OtherAMax) <= toleranceA &&
               Mathf.Abs(current.OtherBMin - next.OtherBMin) <= toleranceB &&
               Mathf.Abs(current.OtherBMax - next.OtherBMax) <= toleranceB;
    }

    private static AutoColliderSlice MergeSlices(AutoColliderSlice current, AutoColliderSlice next)
    {
        current.PrimaryMin = Mathf.Min(current.PrimaryMin, next.PrimaryMin);
        current.PrimaryMax = Mathf.Max(current.PrimaryMax, next.PrimaryMax);
        current.OtherAMin = Mathf.Min(current.OtherAMin, next.OtherAMin);
        current.OtherAMax = Mathf.Max(current.OtherAMax, next.OtherAMax);
        current.OtherBMin = Mathf.Min(current.OtherBMin, next.OtherBMin);
        current.OtherBMax = Mathf.Max(current.OtherBMax, next.OtherBMax);
        return current;
    }

    private static AutoColliderDescriptor CreateBoundsDescriptor(Bounds bounds)
    {
        return new AutoColliderDescriptor(bounds.center, MaxVector(bounds.size, MinimumAutoColliderSize));
    }

    private static AutoColliderDescriptor CreateSliceDescriptor(int primaryAxis, int secondaryAxisA, int secondaryAxisB, AutoColliderSlice slice)
    {
        float primaryCenter = (slice.PrimaryMin + slice.PrimaryMax) * 0.5f;
        float otherACenter = (slice.OtherAMin + slice.OtherAMax) * 0.5f;
        float otherBCenter = (slice.OtherBMin + slice.OtherBMax) * 0.5f;

        float primarySize = Mathf.Max(MinimumAutoColliderSize, slice.PrimaryMax - slice.PrimaryMin);
        float otherASize = Mathf.Max(MinimumAutoColliderSize, slice.OtherAMax - slice.OtherAMin);
        float otherBSize = Mathf.Max(MinimumAutoColliderSize, slice.OtherBMax - slice.OtherBMin);

        Vector3 center = ComposeVector(primaryAxis, primaryCenter, secondaryAxisA, otherACenter, secondaryAxisB, otherBCenter);
        Vector3 size = ComposeVector(primaryAxis, primarySize, secondaryAxisA, otherASize, secondaryAxisB, otherBSize);
        return new AutoColliderDescriptor(center, size);
    }

    private static Vector3 MaxVector(Vector3 value, float minimumComponent)
    {
        return new Vector3(
            Mathf.Max(minimumComponent, value.x),
            Mathf.Max(minimumComponent, value.y),
            Mathf.Max(minimumComponent, value.z));
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        switch (axis)
        {
            case 0:
                return value.x;
            case 1:
                return value.y;
            default:
                return value.z;
        }
    }

    private static Vector3 ComposeVector(int axisA, float valueA, int axisB, float valueB, int axisC, float valueC)
    {
        Vector3 result = Vector3.zero;
        SetAxis(ref result, axisA, valueA);
        SetAxis(ref result, axisB, valueB);
        SetAxis(ref result, axisC, valueC);
        return result;
    }

    private static void SetAxis(ref Vector3 value, int axis, float axisValue)
    {
        switch (axis)
        {
            case 0:
                value.x = axisValue;
                break;
            case 1:
                value.y = axisValue;
                break;
            default:
                value.z = axisValue;
                break;
        }
    }

    private static bool TryGetProBuilderShape(GameObject source, out Component shapeComponent)
    {
        shapeComponent = null;
        Component[] components = source.GetComponents<Component>();

        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
                continue;

            if (component.GetType().FullName == ProBuilderShapeTypeName)
            {
                shapeComponent = component;
                return true;
            }
        }

        return false;
    }

    private static bool IsStraightStairShape(Component shapeComponent)
    {
        if (shapeComponent == null)
            return false;

        SerializedObject serializedObject = new SerializedObject(shapeComponent);
        SerializedProperty shapeProperty = serializedObject.FindProperty("m_Shape");
        if (shapeProperty == null)
            return false;

        string referenceType = shapeProperty.managedReferenceFullTypename;
        if (string.IsNullOrWhiteSpace(referenceType) || !referenceType.Contains(StairShapeTypeName))
            return false;

        SerializedProperty circumferenceProperty = shapeProperty.FindPropertyRelative("m_Circumference");
        return circumferenceProperty == null || Mathf.Abs(circumferenceProperty.floatValue) <= 0.001f;
    }

    private struct AutoColliderSlice
    {
        public bool Occupied;
        public float PrimaryMin;
        public float PrimaryMax;
        public float OtherAMin;
        public float OtherAMax;
        public float OtherBMin;
        public float OtherBMax;
    }

    private readonly struct AutoColliderDescriptor
    {
        public AutoColliderDescriptor(Vector3 center, Vector3 size)
        {
            Center = center;
            Size = size;
        }

        public Vector3 Center { get; }
        public Vector3 Size { get; }
    }
}
