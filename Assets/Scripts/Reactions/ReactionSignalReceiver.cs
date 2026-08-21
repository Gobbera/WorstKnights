using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.VFX;

public readonly struct ReactionSignalContext
{
    public static ReactionSignalContext Empty => default;

    public ReactionSignalContext(float impactVfxAttackAngle, bool hasImpactVfxAttackAngle)
    {
        ImpactVfxAttackAngle = Mathf.Abs(impactVfxAttackAngle);
        HasImpactVfxAttackAngle = hasImpactVfxAttackAngle;
    }

    public float ImpactVfxAttackAngle { get; }
    public bool HasImpactVfxAttackAngle { get; }

    public static ReactionSignalContext FromDamageInfo(DamageInfo damageInfo)
    {
        return new ReactionSignalContext(
            damageInfo.ImpactVfxAttackAngle,
            damageInfo.HasImpactVfxAttackAngle);
    }
}

[DisallowMultipleComponent]
[AddComponentMenu("World/Reactions/Reaction Signal Receiver")]
public class ReactionSignalReceiver : MonoBehaviour
{
    private static readonly string[] AttackAngleParameterNames =
    {
        "Attack Angule",
        "Attack Angle",
        "AttackAngule",
        "AttackAngle"
    };

    private static readonly string[] TriangleAAngleParameterNames =
    {
        "Triangule A Attack Angule",
        "Triangule A Attack Angle",
        "Triangle A Attack Angule",
        "Triangle A Attack Angle",
        "TrianguleAAttackAngule",
        "TrianguleAAttackAngle",
        "TriangleAAttackAngule",
        "TriangleAAttackAngle"
    };

    private static readonly string[] TriangleBAngleParameterNames =
    {
        "Triangule B Attack Angule",
        "Triangule B Attack Angle",
        "Triangle B Attack Angule",
        "Triangle B Attack Angle",
        "TrianguleBAttackAngule",
        "TrianguleBAttackAngle",
        "TriangleBAttackAngule",
        "TriangleBAttackAngle"
    };

    [Serializable]
    public sealed class ReactionSignalUnityEvent : UnityEvent
    {
    }

    [Serializable]
    public sealed class ReactionSignalEntry
    {
        public string signalId = "Hit";
        public Transform feedbackOrigin;
        public AudioCue audioCue;
        public GameObject effectPrefab;
        [Min(0f)] public float effectLifetime = 5f;
        public ReactionSignalUnityEvent onSignalReceived = new ReactionSignalUnityEvent();
    }

    [Header("Identity")]
    [SerializeField] private string receiverName = "Reactions";
    [Header("Signals")]
    [SerializeField] private List<ReactionSignalEntry> signalEntries = new List<ReactionSignalEntry>();

    public string DisplayName => string.IsNullOrWhiteSpace(receiverName) ? gameObject.name : receiverName;

    private void OnValidate()
    {
        if (signalEntries == null)
            signalEntries = new List<ReactionSignalEntry>();

        for (int i = 0; i < signalEntries.Count; i++)
        {
            ReactionSignalEntry signalEntry = signalEntries[i];
            if (signalEntry == null)
            {
                signalEntries[i] = new ReactionSignalEntry();
                continue;
            }

            signalEntry.effectLifetime = Mathf.Max(0f, signalEntry.effectLifetime);
        }
    }

    public void ReceiveSignal(string signalId)
    {
        ReceiveSignal(signalId, transform.position, Vector3.zero);
    }

    public void ReceiveSignal(string signalId, Vector3 worldPosition, Vector3 worldDirection)
    {
        ReceiveSignal(signalId, worldPosition, worldDirection, ReactionSignalContext.Empty);
    }

    public void ReceiveSignal(string signalId, Vector3 worldPosition, Vector3 worldDirection, ReactionSignalContext context)
    {
        if (string.IsNullOrWhiteSpace(signalId))
            return;

        for (int i = 0; i < signalEntries.Count; i++)
        {
            ReactionSignalEntry signalEntry = signalEntries[i];
            if (!MatchesSignal(signalEntry, signalId))
                continue;

            TriggerEntry(signalEntry, worldPosition, worldDirection, context);
        }
    }

    public void ReceiveSignalFromHere(string signalId)
    {
        ReceiveSignal(signalId, transform.position, transform.forward);
    }

    private bool MatchesSignal(ReactionSignalEntry signalEntry, string signalId)
    {
        if (signalEntry == null || string.IsNullOrWhiteSpace(signalEntry.signalId))
            return false;

        return string.Equals(
            signalEntry.signalId.Trim(),
            signalId?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private void TriggerEntry(ReactionSignalEntry signalEntry, Vector3 worldPosition, Vector3 worldDirection, ReactionSignalContext context)
    {
        if (signalEntry == null)
            return;

        Vector3 resolvedPosition = ResolveWorldPosition(signalEntry, worldPosition);
        PlayAudioCue(signalEntry, resolvedPosition);
        SpawnEffect(signalEntry, resolvedPosition, worldDirection, context);
        signalEntry.onSignalReceived?.Invoke();
    }

    private Vector3 ResolveWorldPosition(ReactionSignalEntry signalEntry, Vector3 requestedWorldPosition)
    {
        if (signalEntry != null && signalEntry.feedbackOrigin != null)
            return signalEntry.feedbackOrigin.position;

        if (IsFiniteVector(requestedWorldPosition))
            return requestedWorldPosition;

        return transform.position;
    }

    private void PlayAudioCue(ReactionSignalEntry signalEntry, Vector3 worldPosition)
    {
        AudioCue audioCue = signalEntry != null ? signalEntry.audioCue : null;
        if (audioCue == null || !audioCue.HasPlayableClip())
            return;

        Transform anchor = signalEntry != null && signalEntry.feedbackOrigin != null
            ? signalEntry.feedbackOrigin
            : transform;
        if (audioCue.Is3D && audioCue.Anchor == AudioPlaybackAnchor.FollowTransform && anchor != null)
        {
            GameAudioService.Play(audioCue, anchor);
            return;
        }

        if (audioCue.Is3D)
        {
            GameAudioService.Play(audioCue, worldPosition);
            return;
        }

        GameAudioService.Play(audioCue);
    }

    private void SpawnEffect(ReactionSignalEntry signalEntry, Vector3 worldPosition, Vector3 worldDirection, ReactionSignalContext context)
    {
        if (signalEntry == null || signalEntry.effectPrefab == null)
            return;

        Quaternion effectRotation = ResolveEffectRotation(worldDirection);
        GameObject spawnedEffect = Instantiate(signalEntry.effectPrefab, worldPosition, effectRotation);
        ApplyVisualEffectContext(spawnedEffect, context);
        if (signalEntry.effectLifetime > 0f)
            Destroy(spawnedEffect, signalEntry.effectLifetime);
    }

    private static void ApplyVisualEffectContext(GameObject spawnedEffect, ReactionSignalContext context)
    {
        if (spawnedEffect == null || !context.HasImpactVfxAttackAngle)
            return;

        VisualEffect[] visualEffects = spawnedEffect.GetComponentsInChildren<VisualEffect>(true);
        if (visualEffects == null || visualEffects.Length == 0)
            return;

        float attackAngle = Mathf.Abs(context.ImpactVfxAttackAngle);
        for (int i = 0; i < visualEffects.Length; i++)
        {
            VisualEffect visualEffect = visualEffects[i];
            if (visualEffect == null)
                continue;

            bool applied = TrySetAnyVisualEffectFloat(visualEffect, AttackAngleParameterNames, attackAngle);
            applied |= TrySetAnyVisualEffectFloat(visualEffect, TriangleAAngleParameterNames, attackAngle);
            applied |= TrySetAnyVisualEffectFloat(visualEffect, TriangleBAngleParameterNames, -attackAngle);
            if (!applied)
                continue;

            visualEffect.Reinit();
            visualEffect.Play();
        }
    }

    private static bool TrySetAnyVisualEffectFloat(VisualEffect visualEffect, string[] parameterNames, float value)
    {
        if (visualEffect == null || parameterNames == null)
            return false;

        bool applied = false;
        for (int i = 0; i < parameterNames.Length; i++)
        {
            string parameterName = parameterNames[i];
            if (string.IsNullOrWhiteSpace(parameterName) || !visualEffect.HasFloat(parameterName))
                continue;

            visualEffect.SetFloat(parameterName, value);
            applied = true;
        }

        return applied;
    }

    private static Quaternion ResolveEffectRotation(Vector3 worldDirection)
    {
        Vector3 safeForward = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
        if (safeForward.sqrMagnitude <= 0.0001f)
            safeForward = Vector3.forward;

        return Quaternion.LookRotation(safeForward.normalized, Vector3.up);
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z));
    }
}
