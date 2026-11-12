using System.Collections.Generic;
using System;
using UnityEngine;

public sealed class BeholderAttackScheduler
{
    private readonly Dictionary<int, float> availableAt = new Dictionary<int, float>();
    private float pendingIndividualCooldown, pendingGlobalCooldown;
    private bool cooldownsStarted;
    public int ActiveIndex { get; private set; } = -1;
    public float GlobalAvailableAt { get; private set; }

    public bool IsReady(int index, float now)
    {
        return ActiveIndex < 0 && now >= GlobalAvailableAt
            && (!availableAt.TryGetValue(index, out float deadline) || now >= deadline);
    }

    public bool TryStart(int index, float individualCooldown, float globalCooldown, float now)
    {
        if (!IsReady(index, now)) return false;
        ApplyStart(index, individualCooldown, globalCooldown);
        return true;
    }

    public int SelectReady(IReadOnlyList<BeholderAttackController.AttackDefinition> attacks, Predicate<int> eligible, float now)
    {
        int best = -1;
        if (attacks == null) return best;
        for (int i = 0; i < attacks.Count; i++)
        {
            var attack = attacks[i];
            if (attack == null || !attack.enabled || !IsReady(i, now) || !eligible(i)) continue;
            if (best < 0 || attack.priority > attacks[best].priority) best = i;
        }
        return best;
    }

    public void ApplyStart(int index, float individualCooldown, float globalCooldown)
    {
        ActiveIndex = index;
        pendingIndividualCooldown = Mathf.Max(0f, individualCooldown);
        pendingGlobalCooldown = Mathf.Max(0f, globalCooldown);
        cooldownsStarted = false;
    }

    public void StartCooldowns(float executionEndedAt)
    {
        if (ActiveIndex < 0 || cooldownsStarted) return;
        availableAt[ActiveIndex] = executionEndedAt + pendingIndividualCooldown;
        GlobalAvailableAt = executionEndedAt + pendingGlobalCooldown;
        cooldownsStarted = true;
    }

    public void Complete(float endedAt)
    {
        // Interruption also ends an execution; recovery must never restart an existing cooldown.
        StartCooldowns(endedAt);
        ActiveIndex = -1;
    }
}
