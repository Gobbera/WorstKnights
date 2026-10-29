using System.Collections.Generic;
using System;
using UnityEngine;

public sealed class BeholderAttackScheduler
{
    private readonly Dictionary<int, float> availableAt = new Dictionary<int, float>();
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
        ApplyStart(index, individualCooldown, globalCooldown, now);
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

    public void ApplyStart(int index, float individualCooldown, float globalCooldown, float startedAt)
    {
        ActiveIndex = index;
        availableAt[index] = startedAt + Mathf.Max(0f, individualCooldown);
        GlobalAvailableAt = startedAt + Mathf.Max(0f, globalCooldown);
    }

    public void Complete()
    {
        ActiveIndex = -1;
    }
}
