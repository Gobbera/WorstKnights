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

        results.Clear();
        if (mesh != null)
            owner.CollectRenderersFromTarget(mesh, includeChildren, results);

        if (results.Count == 0 && !string.IsNullOrWhiteSpace(fallbackName))
        {
            Transform fallbackTransform = owner.ResolveTransformByName(fallbackName);
            if (fallbackTransform != null)
                owner.CollectRenderersFromTransform(fallbackTransform, includeChildren, results);
        }

        bool shouldShow = ShouldShow(isOwner);
        for (int i = 0; i < results.Count; i++)
            owner.ApplyRendererVisibility(results[i], shouldShow);
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
}

[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView))]
public sealed class PlayerPerspectiveVisibility : MonoBehaviour
{
    [SerializeField] private PhotonView photonView;
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private PlayerPerspectiveVisibilityElement[] elements = CreateDefaultElements();
    [SerializeField, HideInInspector] private PlayerPerspectiveVisibilityRule[] visibilityRules = CreateDefaultVisibilityRules();

    private readonly Dictionary<Renderer, bool> originalRendererStates = new Dictionary<Renderer, bool>();
    private readonly List<Renderer> ruleRenderers = new List<Renderer>();
    private Transform[] cachedTransforms = Array.Empty<Transform>();

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

        if (elements != null && elements.Length > 0)
        {
            for (int i = 0; i < elements.Length; i++)
            {
                PlayerPerspectiveVisibilityElement element = elements[i];
                if (element != null)
                    element.Apply(this, isOwner, ruleRenderers);
            }

            return;
        }

        for (int i = 0; i < visibilityRules.Length; i++)
        {
            PlayerPerspectiveVisibilityRule rule = visibilityRules[i];
            if (rule == null)
                continue;

            ruleRenderers.Clear();
            rule.CollectRenderers(this, ruleRenderers);

            bool shouldShow = rule.ShouldShow(isOwner);
            for (int rendererIndex = 0; rendererIndex < ruleRenderers.Count; rendererIndex++)
                ApplyRendererVisibility(ruleRenderers[rendererIndex], shouldShow);
        }
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
