using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Audio/Audio Cue Radius Gizmo")]
public sealed class AudioCueRadiusGizmo : MonoBehaviour
{
    [Serializable]
    private sealed class CueRadiusEntry
    {
        [SerializeField] private string label;
        [SerializeField] private AudioCue cue;
        [SerializeField] private Transform anchorOverride;
        [SerializeField] private bool draw = true;

        public bool Draw => draw;

        public bool TryResolve(
            Transform defaultAnchor,
            out AudioCue resolvedCue,
            out Transform resolvedAnchor,
            out string resolvedLabel)
        {
            resolvedCue = cue;
            resolvedAnchor = anchorOverride != null ? anchorOverride : defaultAnchor;
            resolvedLabel = !string.IsNullOrWhiteSpace(label)
                ? label
                : (cue != null ? cue.name : "Audio Cue");

            return draw
                && resolvedCue != null
                && resolvedCue.DrawDistanceGizmos
                && resolvedAnchor != null;
        }
    }

    [Header("References")]
    [SerializeField] private Transform defaultAnchor;
    [SerializeField] private CueRadiusEntry[] cues = Array.Empty<CueRadiusEntry>();

    [Header("Drawing")]
    [SerializeField] private bool drawWhenNotSelected;
    [SerializeField] private bool drawMinDistance = true;
    [SerializeField] private bool drawMaxDistance = true;
    [SerializeField] private bool drawLabels = true;
    [SerializeField] private bool drawTwoDimensionalCueLabels;
    [SerializeField] [Min(0f)] private float labelHeightOffset = 0.25f;

    private void OnDrawGizmos()
    {
        if (drawWhenNotSelected)
            DrawCueRadiusGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        DrawCueRadiusGizmos();
    }

    private void DrawCueRadiusGizmos()
    {
        if (!enabled || cues == null || cues.Length == 0)
            return;

        Transform anchor = defaultAnchor != null ? defaultAnchor : transform;
        int labelIndex = 0;
        for (int i = 0; i < cues.Length; i++)
        {
            CueRadiusEntry entry = cues[i];
            if (entry == null || !entry.Draw)
                continue;

            if (!entry.TryResolve(anchor, out AudioCue cue, out Transform resolvedAnchor, out string label))
                continue;

            DrawCueRadius(resolvedAnchor.position, cue, label, labelIndex);
            labelIndex++;
        }
    }

    private void DrawCueRadius(
        Vector3 origin,
        AudioCue cue,
        string label,
        int labelIndex)
    {
        if (cue == null)
            return;

        bool isSpatial = cue.Is3D && cue.SpatialBlend > 0.001f;
        if (!isSpatial)
        {
#if UNITY_EDITOR
            if (drawLabels && drawTwoDimensionalCueLabels)
                Handles.Label(origin + Vector3.up * ResolveLabelHeight(labelIndex), $"{label}\n2D / sem raio espacial");
#endif
            return;
        }

        float minDistance = Mathf.Max(0.01f, cue.MinDistance);
        float maxDistance = Mathf.Max(minDistance, cue.MaxDistance);

        if (drawMinDistance)
        {
            Gizmos.color = cue.MinDistanceGizmoColor;
            Gizmos.DrawWireSphere(origin, minDistance);
        }

        if (drawMaxDistance)
        {
            Gizmos.color = cue.MaxDistanceGizmoColor;
            Gizmos.DrawWireSphere(origin, maxDistance);
        }

#if UNITY_EDITOR
        if (drawLabels)
        {
            string text = $"{label}\nMin {minDistance:0.##}m | Max {maxDistance:0.##}m | Blend {cue.SpatialBlend:0.##}";
            Handles.Label(origin + Vector3.up * ResolveLabelHeight(labelIndex), text);
        }
#endif
    }

    private float ResolveLabelHeight(int labelIndex)
    {
        return labelHeightOffset + labelIndex * 0.35f;
    }

    private void OnValidate()
    {
        if (cues == null)
            cues = Array.Empty<CueRadiusEntry>();

        labelHeightOffset = Mathf.Max(0f, labelHeightOffset);
    }
}
