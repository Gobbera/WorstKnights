using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public enum PlayerPerspectiveVisibilityMode
{
    Always = 0,
    OwnerOnly = 1,
    RemoteOnly = 2,
    Hidden = 3
}

[Serializable]
public sealed class PlayerPerspectiveVisibilityElement
{
    [SerializeField] private string label;
    [SerializeField] private UnityEngine.Object mesh;
    [SerializeField] private PlayerPerspectiveVisibilityMode visibility = PlayerPerspectiveVisibilityMode.Always;
    [SerializeField] private bool includeChildren = true;
    [SerializeField] private string fallbackName;

    public static PlayerPerspectiveVisibilityElement Create(
        string label,
        PlayerPerspectiveVisibilityMode visibility,
        UnityEngine.Object mesh = null,
        string fallbackName = null,
        bool includeChildren = true)
    {
        PlayerPerspectiveVisibilityElement element = new PlayerPerspectiveVisibilityElement();
        element.label = label;
        element.visibility = visibility;
        element.mesh = mesh;
        element.fallbackName = fallbackName;
        element.includeChildren = includeChildren;
        return element;
    }

    public void Apply(PlayerPerspectiveVisibility owner, bool isOwner, List<Renderer> results)
    {
        if (owner == null || results == null)
            return;

        owner.ClearVisibilityDecisions();
        Queue(owner, isOwner, results);
        owner.ApplyQueuedVisibility();
    }

    public void Queue(PlayerPerspectiveVisibility owner, bool isOwner, List<Renderer> results)
    {
        if (owner == null || results == null)
            return;

        owner.QueueTargetVisibility(mesh, includeChildren, fallbackName, visibility, isOwner, results);
    }

    private bool ShouldShow(bool isOwner)
    {
        switch (visibility)
        {
            case PlayerPerspectiveVisibilityMode.OwnerOnly:
                return isOwner;
            case PlayerPerspectiveVisibilityMode.RemoteOnly:
                return !isOwner;
            case PlayerPerspectiveVisibilityMode.Hidden:
                return false;
            default:
                return true;
        }
    }
}

[Serializable]
public sealed class PlayerPerspectiveVisibilityTarget
{
    [SerializeField] private UnityEngine.Object target;
    [SerializeField] private bool includeChildren = true;
    [SerializeField] private string fallbackName;

    public UnityEngine.Object Target => target;

    public static PlayerPerspectiveVisibilityTarget Create(UnityEngine.Object target, bool includeChildren)
    {
        PlayerPerspectiveVisibilityTarget visibilityTarget = new PlayerPerspectiveVisibilityTarget();
        visibilityTarget.target = target;
        visibilityTarget.includeChildren = includeChildren;
        return visibilityTarget;
    }

    public void CollectRenderers(PlayerPerspectiveVisibility owner, List<Renderer> results)
    {
        if (owner == null || results == null)
            return;

        int previousCount = results.Count;
        if (target != null)
            owner.CollectRenderersFromTarget(target, includeChildren, results);

        if (results.Count > previousCount || string.IsNullOrWhiteSpace(fallbackName))
            return;

        Transform resolvedFallback = owner.ResolveTransformByName(fallbackName);
        if (resolvedFallback != null)
            owner.CollectRenderersFromTransform(resolvedFallback, includeChildren, results);
    }

    public void QueueVisibility(
        PlayerPerspectiveVisibility owner,
        PlayerPerspectiveVisibilityMode visibility,
        bool isOwner,
        List<Renderer> results)
    {
        if (owner == null || results == null)
            return;

        owner.QueueTargetVisibility(target, includeChildren, fallbackName, visibility, isOwner, results);
    }
}

[Serializable]
public sealed class PlayerPerspectiveVisibilityRule
{
    [SerializeField] private string label;
    [SerializeField] private PlayerPerspectiveVisibilityMode visibility = PlayerPerspectiveVisibilityMode.Always;
    [SerializeField] private PlayerPerspectiveVisibilityTarget[] targets = Array.Empty<PlayerPerspectiveVisibilityTarget>();
    [SerializeField] private Transform root;
    [SerializeField] private string fallbackRootName;
    [SerializeField] private bool includeChildren = true;
    [SerializeField] private Renderer[] renderers;

    public string Label => label;

    public static PlayerPerspectiveVisibilityRule Create(string label, PlayerPerspectiveVisibilityMode visibility, string fallbackRootName)
    {
        PlayerPerspectiveVisibilityRule rule = new PlayerPerspectiveVisibilityRule();
        rule.label = label;
        rule.visibility = visibility;
        rule.fallbackRootName = fallbackRootName;
        rule.includeChildren = true;
        rule.targets = Array.Empty<PlayerPerspectiveVisibilityTarget>();
        rule.renderers = Array.Empty<Renderer>();
        return rule;
    }

    public bool ShouldShow(bool isOwner)
    {
        switch (visibility)
        {
            case PlayerPerspectiveVisibilityMode.OwnerOnly:
                return isOwner;
            case PlayerPerspectiveVisibilityMode.RemoteOnly:
                return !isOwner;
            case PlayerPerspectiveVisibilityMode.Hidden:
                return false;
            default:
                return true;
        }
    }

    public void CollectRenderers(PlayerPerspectiveVisibility owner, List<Renderer> results)
    {
        if (owner == null || results == null)
            return;

        if (targets != null)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                PlayerPerspectiveVisibilityTarget targetRule = targets[i];
                if (targetRule != null)
                    targetRule.CollectRenderers(owner, results);
            }
        }

        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                owner.AddUniqueRenderer(renderers[i], results);
            }
        }

        Transform resolvedRoot = root != null ? root : owner.ResolveTransformByName(fallbackRootName);
        if (resolvedRoot == null)
            return;

        owner.CollectRenderersFromTransform(resolvedRoot, includeChildren, results);
    }

    public void QueueVisibility(PlayerPerspectiveVisibility owner, bool isOwner, List<Renderer> results)
    {
        if (owner == null || results == null)
            return;

        if (targets != null)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                PlayerPerspectiveVisibilityTarget targetRule = targets[i];
                if (targetRule != null)
                    targetRule.QueueVisibility(owner, visibility, isOwner, results);
            }
        }

        bool shouldShow = ShouldShow(isOwner);
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer targetRenderer = renderers[i];
                owner.QueueRendererVisibility(
                    targetRenderer,
                    shouldShow,
                    owner.GetDirectRendererVisibilityPriority(targetRenderer),
                    visibility);
            }
        }

        Transform resolvedRoot = root != null ? root : owner.ResolveTransformByName(fallbackRootName);
        if (resolvedRoot != null)
            owner.QueueTransformVisibility(resolvedRoot, includeChildren, visibility, isOwner, results);
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView))]
public sealed class PlayerPerspectiveVisibility : MonoBehaviour
{
    private const int TransformDepthPriorityStep = 10;
    private const int DirectTargetPriorityBonus = 5;
    private const int ForceHiddenVisibilityPriority = int.MaxValue;
    private const string FirstPersonViewLayerName = "FirstPersonView";

    private static readonly string[] RemoteHiddenFirstPersonRootNames =
    {
        "FPS_Model",
        "Separated_UpperBody",
        "Separeted_UpperBody"
    };

    [SerializeField] private PhotonView photonView;
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool forceHideFirstPersonRootsForRemotePlayers = true;
    [SerializeField] private bool forceHideFirstPersonLayerForRemotePlayers = true;
    [SerializeField] private PlayerPerspectiveVisibilityElement[] elements = CreateDefaultElements();
    [SerializeField, HideInInspector] private PlayerPerspectiveVisibilityRule[] visibilityRules = CreateDefaultVisibilityRules();

    private readonly Dictionary<Renderer, bool> originalRendererStates = new Dictionary<Renderer, bool>();
    private readonly Dictionary<Renderer, VisibilityDecision> visibilityDecisions = new Dictionary<Renderer, VisibilityDecision>();
    private readonly List<Renderer> ruleRenderers = new List<Renderer>();
    private Transform[] cachedTransforms = Array.Empty<Transform>();

    private struct VisibilityDecision
    {
        public bool ShouldShow;
        public int Priority;
        public PlayerPerspectiveVisibilityMode Mode;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        if (applyOnStart)
            ApplyForCurrentOwner();
    }

    private void Reset()
    {
        ResolveReferences();
        elements = CreateDefaultElements();
    }

    private void OnValidate()
    {
        cachedTransforms = Array.Empty<Transform>();
        EnsureElementsOrRules();
    }

    [ContextMenu("Apply For Current Owner")]
    public void ApplyForCurrentOwner()
    {
        ResolveReferences();
        Apply(photonView == null || photonView.IsMine);
    }

    public void Apply(bool isOwner)
    {
        EnsureElementsOrRules();
        ClearVisibilityDecisions();

        if (elements != null && elements.Length > 0)
        {
            for (int i = 0; i < elements.Length; i++)
            {
                PlayerPerspectiveVisibilityElement element = elements[i];
                if (element != null)
                    element.Queue(this, isOwner, ruleRenderers);
            }

            QueueRemoteFirstPersonVisibility(isOwner);
            ApplyQueuedVisibility();
            return;
        }

        for (int i = 0; i < visibilityRules.Length; i++)
        {
            PlayerPerspectiveVisibilityRule rule = visibilityRules[i];
            if (rule == null)
                continue;

            rule.QueueVisibility(this, isOwner, ruleRenderers);
        }

        QueueRemoteFirstPersonVisibility(isOwner);
        ApplyQueuedVisibility();
    }

    public Transform ResolveTransformByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        if (cachedTransforms == null || cachedTransforms.Length == 0)
            cachedTransforms = GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < cachedTransforms.Length; i++)
        {
            Transform candidate = cachedTransforms[i];
            if (candidate != null && string.Equals(candidate.name, objectName, StringComparison.Ordinal))
                return candidate;
        }

        return null;
    }

    public void CollectRenderersFromTarget(UnityEngine.Object target, bool includeChildren, List<Renderer> results)
    {
        if (target == null || results == null)
            return;

        if (target is Mesh targetMesh)
        {
            CollectRenderersUsingMesh(targetMesh, results);
            return;
        }

        if (target is GameObject targetObject)
        {
            CollectRenderersFromTransform(targetObject.transform, includeChildren, results);
            return;
        }

        if (!(target is Component targetComponent))
            return;

        if (targetComponent is Renderer targetRenderer)
        {
            if (includeChildren)
                CollectRenderersFromTransform(targetRenderer.transform, includeChildren: true, results);
            else
                AddUniqueRenderer(targetRenderer, results);

            return;
        }

        if (targetComponent is MeshFilter meshFilter)
        {
            if (includeChildren)
            {
                CollectRenderersFromTransform(meshFilter.transform, includeChildren: true, results);
            }
            else
            {
                AddUniqueRenderer(meshFilter.GetComponent<Renderer>(), results);
            }

            return;
        }

        CollectRenderersFromTransform(targetComponent.transform, includeChildren, results);
    }

    public void CollectRenderersFromTransform(Transform root, bool includeChildren, List<Renderer> results)
    {
        if (root == null || results == null)
            return;

        if (!includeChildren)
        {
            AddUniqueRenderer(root.GetComponent<Renderer>(), results);
            return;
        }

        Renderer[] childRenderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < childRenderers.Length; i++)
            AddUniqueRenderer(childRenderers[i], results);
    }

    public void AddUniqueRenderer(Renderer targetRenderer, List<Renderer> results)
    {
        if (targetRenderer == null || results == null || results.Contains(targetRenderer))
            return;

        results.Add(targetRenderer);
    }

    public void QueueTargetVisibility(
        UnityEngine.Object target,
        bool includeChildren,
        string fallbackName,
        PlayerPerspectiveVisibilityMode visibility,
        bool isOwner,
        List<Renderer> results)
    {
        if (results == null)
            return;

        results.Clear();
        Transform targetRoot = ResolveTargetTransform(target);
        if (target != null)
            CollectRenderersFromTarget(target, includeChildren, results);

        if (results.Count == 0 && !string.IsNullOrWhiteSpace(fallbackName))
        {
            targetRoot = ResolveTransformByName(fallbackName);
            if (targetRoot != null)
                CollectRenderersFromTransform(targetRoot, includeChildren, results);
        }

        bool shouldShow = ShouldShow(visibility, isOwner);
        for (int i = 0; i < results.Count; i++)
        {
            Renderer targetRenderer = results[i];
            QueueRendererVisibility(
                targetRenderer,
                shouldShow,
                GetTargetVisibilityPriority(targetRenderer, targetRoot, target, includeChildren),
                visibility);
        }
    }

    public void QueueTransformVisibility(
        Transform root,
        bool includeChildren,
        PlayerPerspectiveVisibilityMode visibility,
        bool isOwner,
        List<Renderer> results)
    {
        if (root == null || results == null)
            return;

        results.Clear();
        CollectRenderersFromTransform(root, includeChildren, results);

        bool shouldShow = ShouldShow(visibility, isOwner);
        int priority = GetTransformVisibilityPriority(root, includeChildren);
        for (int i = 0; i < results.Count; i++)
            QueueRendererVisibility(results[i], shouldShow, priority, visibility);
    }

    public void QueueRendererVisibility(
        Renderer targetRenderer,
        bool shouldShow,
        int priority,
        PlayerPerspectiveVisibilityMode visibility)
    {
        if (targetRenderer == null)
            return;

        VisibilityDecision nextDecision = new VisibilityDecision
        {
            ShouldShow = shouldShow,
            Priority = priority,
            Mode = visibility
        };

        if (visibilityDecisions.TryGetValue(targetRenderer, out VisibilityDecision currentDecision)
            && ShouldKeepCurrentDecision(currentDecision, nextDecision))
        {
            return;
        }

        visibilityDecisions[targetRenderer] = nextDecision;
    }

    private void QueueRemoteFirstPersonVisibility(bool isOwner)
    {
        if (isOwner)
            return;

        if (forceHideFirstPersonRootsForRemotePlayers)
        {
            for (int i = 0; i < RemoteHiddenFirstPersonRootNames.Length; i++)
                QueueRemoteHiddenRootVisibility(ResolveTransformByName(RemoteHiddenFirstPersonRootNames[i]));
        }

        if (!forceHideFirstPersonLayerForRemotePlayers)
            return;

        int firstPersonLayer = LayerMask.NameToLayer(FirstPersonViewLayerName);
        if (firstPersonLayer < 0)
            return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null || !IsTransformOnLayerOrUnderLayer(targetRenderer.transform, firstPersonLayer))
                continue;

            QueueRendererVisibility(
                targetRenderer,
                false,
                ForceHiddenVisibilityPriority,
                PlayerPerspectiveVisibilityMode.Hidden);
        }
    }

    private void QueueRemoteHiddenRootVisibility(Transform root)
    {
        if (root == null)
            return;

        ruleRenderers.Clear();
        CollectRenderersFromTransform(root, true, ruleRenderers);
        for (int i = 0; i < ruleRenderers.Count; i++)
        {
            QueueRendererVisibility(
                ruleRenderers[i],
                false,
                ForceHiddenVisibilityPriority,
                PlayerPerspectiveVisibilityMode.Hidden);
        }
    }

    private bool IsTransformOnLayerOrUnderLayer(Transform target, int layer)
    {
        Transform cursor = target;
        while (cursor != null && cursor != transform)
        {
            if (cursor.gameObject.layer == layer)
                return true;

            cursor = cursor.parent;
        }

        return false;
    }

    public int GetDirectRendererVisibilityPriority(Renderer targetRenderer)
    {
        return targetRenderer != null
            ? GetTransformDepth(targetRenderer.transform) * TransformDepthPriorityStep + DirectTargetPriorityBonus
            : 0;
    }

    public void ClearVisibilityDecisions()
    {
        visibilityDecisions.Clear();
    }

    public void ApplyQueuedVisibility()
    {
        foreach (KeyValuePair<Renderer, VisibilityDecision> entry in visibilityDecisions)
            ApplyRendererVisibility(entry.Key, entry.Value.ShouldShow);

        visibilityDecisions.Clear();
    }

    private void ResolveReferences()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        cachedTransforms = GetComponentsInChildren<Transform>(true);
    }

    public void ApplyRendererVisibility(Renderer targetRenderer, bool shouldShow)
    {
        if (targetRenderer == null)
            return;

        if (!originalRendererStates.ContainsKey(targetRenderer))
            originalRendererStates.Add(targetRenderer, targetRenderer.enabled);

        targetRenderer.enabled = shouldShow && originalRendererStates[targetRenderer];
    }

    private void CollectRenderersUsingMesh(Mesh targetMesh, List<Renderer> results)
    {
        if (targetMesh == null || results == null)
            return;

        SkinnedMeshRenderer[] skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedRenderers.Length; i++)
        {
            SkinnedMeshRenderer skinnedRenderer = skinnedRenderers[i];
            if (skinnedRenderer != null && skinnedRenderer.sharedMesh == targetMesh)
                AddUniqueRenderer(skinnedRenderer, results);
        }

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter == null || meshFilter.sharedMesh != targetMesh)
                continue;

            AddUniqueRenderer(meshFilter.GetComponent<Renderer>(), results);
        }
    }

    private int GetTargetVisibilityPriority(
        Renderer targetRenderer,
        Transform targetRoot,
        UnityEngine.Object target,
        bool includeChildren)
    {
        if (targetRoot != null)
            return GetTransformVisibilityPriority(targetRoot, includeChildren);

        if (target is Mesh)
            return GetDirectRendererVisibilityPriority(targetRenderer);

        return 0;
    }

    private int GetTransformVisibilityPriority(Transform targetRoot, bool includeChildren)
    {
        if (targetRoot == null)
            return 0;

        return GetTransformDepth(targetRoot) * TransformDepthPriorityStep
            + (includeChildren ? 0 : DirectTargetPriorityBonus);
    }

    private Transform ResolveTargetTransform(UnityEngine.Object target)
    {
        if (target is GameObject targetObject)
            return targetObject.transform;

        if (target is Component targetComponent)
            return targetComponent.transform;

        return null;
    }

    private int GetTransformDepth(Transform target)
    {
        int depth = 0;
        Transform cursor = target;
        while (cursor != null && cursor != transform)
        {
            depth++;
            cursor = cursor.parent;
        }

        return depth;
    }

    private static bool ShouldShow(PlayerPerspectiveVisibilityMode visibility, bool isOwner)
    {
        switch (visibility)
        {
            case PlayerPerspectiveVisibilityMode.OwnerOnly:
                return isOwner;
            case PlayerPerspectiveVisibilityMode.RemoteOnly:
                return !isOwner;
            case PlayerPerspectiveVisibilityMode.Hidden:
                return false;
            default:
                return true;
        }
    }

    private static bool ShouldKeepCurrentDecision(VisibilityDecision currentDecision, VisibilityDecision nextDecision)
    {
        if (currentDecision.Priority > nextDecision.Priority)
            return true;

        if (currentDecision.Priority < nextDecision.Priority)
            return false;

        if (!currentDecision.ShouldShow && nextDecision.ShouldShow)
            return IsRestrictiveVisibilityMode(currentDecision.Mode)
                || !IsRestrictiveVisibilityMode(nextDecision.Mode);

        return false;
    }

    private static bool IsRestrictiveVisibilityMode(PlayerPerspectiveVisibilityMode visibility)
    {
        return visibility != PlayerPerspectiveVisibilityMode.Always;
    }

    private void EnsureElementsOrRules()
    {
        if (elements != null && elements.Length > 0)
            return;

        if (visibilityRules != null && visibilityRules.Length > 0)
            return;

        elements = CreateDefaultElements();
    }

    private static PlayerPerspectiveVisibilityElement[] CreateDefaultElements()
    {
        return new[]
        {
            PlayerPerspectiveVisibilityElement.Create("Third-person full body", PlayerPerspectiveVisibilityMode.RemoteOnly, fallbackName: "TP_Model"),
            PlayerPerspectiveVisibilityElement.Create("First-person FPS model", PlayerPerspectiveVisibilityMode.OwnerOnly, fallbackName: "FPS_Model"),
            PlayerPerspectiveVisibilityElement.Create("First-person upper body", PlayerPerspectiveVisibilityMode.OwnerOnly, fallbackName: "Separated_UpperBody"),
            PlayerPerspectiveVisibilityElement.Create("First-person upper body typo fallback", PlayerPerspectiveVisibilityMode.OwnerOnly, fallbackName: "Separeted_UpperBody")
        };
    }

    private static PlayerPerspectiveVisibilityRule[] CreateDefaultVisibilityRules()
    {
        return new[]
        {
            PlayerPerspectiveVisibilityRule.Create("Third-person full body", PlayerPerspectiveVisibilityMode.RemoteOnly, "Knight"),
            PlayerPerspectiveVisibilityRule.Create("First-person FPS model", PlayerPerspectiveVisibilityMode.OwnerOnly, "FPS_Model"),
            PlayerPerspectiveVisibilityRule.Create("First-person upper body", PlayerPerspectiveVisibilityMode.OwnerOnly, "Separated_UpperBody"),
            PlayerPerspectiveVisibilityRule.Create("First-person upper body typo fallback", PlayerPerspectiveVisibilityMode.OwnerOnly, "Separeted_UpperBody")
        };
    }
}
