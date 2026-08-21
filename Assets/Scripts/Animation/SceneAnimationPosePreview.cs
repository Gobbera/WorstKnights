using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SceneAnimationPosePreview : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private GameObject previewRoot;
    [SerializeField] private UnityEngine.Object clipSourceAsset;
    [SerializeField] private AnimationClip[] previewClips = Array.Empty<AnimationClip>();
    [SerializeField] private int selectedClipIndex = -1;
    [SerializeField] [Range(0f, 1f)] private float normalizedTime = 1f;

    public Animator Animator => animator;
    public GameObject PreviewRoot => previewRoot != null ? previewRoot : (animator != null ? animator.gameObject : gameObject);
    public UnityEngine.Object ClipSourceAsset => clipSourceAsset;
    public IReadOnlyList<AnimationClip> PreviewClips => previewClips;
    public int SelectedClipIndex => selectedClipIndex;
    public float NormalizedTime => normalizedTime;

    private void Reset()
    {
        ResolveReferences();
        NormalizeSelection();
    }

    private void OnValidate()
    {
        ResolveReferences();
        NormalizeSelection();
    }

    public void ResolveReferences()
    {
        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (previewRoot == null && animator != null)
            previewRoot = animator.gameObject;
    }

    public AnimationClip GetSelectedClip()
    {
        if (previewClips == null || previewClips.Length == 0)
            return null;

        if (selectedClipIndex < 0 || selectedClipIndex >= previewClips.Length)
            return null;

        return previewClips[selectedClipIndex];
    }

    public float GetSampleTime()
    {
        AnimationClip selectedClip = GetSelectedClip();
        if (selectedClip == null)
            return 0f;

        return Mathf.Clamp01(normalizedTime) * Mathf.Max(0f, selectedClip.length);
    }

    public void SetSelectedClipIndex(int clipIndex)
    {
        selectedClipIndex = clipIndex;
        NormalizeSelection();
    }

    public void SetNormalizedTime(float value)
    {
        normalizedTime = Mathf.Clamp01(value);
    }

    public void SetPreviewClips(AnimationClip[] clips)
    {
        previewClips = DeduplicateClips(clips);
        NormalizeSelection();
    }

    public int PopulateClipsFromAnimatorController()
    {
        ResolveReferences();

        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller == null)
        {
            previewClips = Array.Empty<AnimationClip>();
            selectedClipIndex = -1;
            return 0;
        }

        SetPreviewClips(controller.animationClips);
        return previewClips.Length;
    }

    private void NormalizeSelection()
    {
        normalizedTime = Mathf.Clamp01(normalizedTime);

        if (previewClips == null)
            previewClips = Array.Empty<AnimationClip>();
        else
            previewClips = DeduplicateClips(previewClips);

        if (previewClips.Length == 0)
        {
            selectedClipIndex = -1;
            return;
        }

        selectedClipIndex = Mathf.Clamp(selectedClipIndex, 0, previewClips.Length - 1);
    }

    private static AnimationClip[] DeduplicateClips(AnimationClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return Array.Empty<AnimationClip>();

        List<AnimationClip> uniqueClips = new List<AnimationClip>(clips.Length);
        HashSet<AnimationClip> seenClips = new HashSet<AnimationClip>();
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null || !seenClips.Add(clip))
                continue;

            uniqueClips.Add(clip);
        }

        return uniqueClips.ToArray();
    }
}
