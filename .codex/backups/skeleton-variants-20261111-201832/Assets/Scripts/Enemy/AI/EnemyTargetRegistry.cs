using System.Collections.Generic;
using UnityEngine;

public static class EnemyTargetRegistry
{
    private static readonly List<PlayerHealth> Targets = new List<PlayerHealth>();

    public static int Count => Targets.Count;

    public static void Register(PlayerHealth target)
    {
        if (target == null || Targets.Contains(target))
            return;

        Targets.Add(target);
    }

    public static void Unregister(PlayerHealth target)
    {
        Targets.Remove(target);
    }

    public static PlayerHealth GetTarget(int index)
    {
        return index >= 0 && index < Targets.Count ? Targets[index] : null;
    }

    public static void PruneInvalidTargets()
    {
        for (int i = Targets.Count - 1; i >= 0; i--)
        {
            if (Targets[i] == null)
                Targets.RemoveAt(i);
        }
    }

    public static void RefreshFromScene()
    {
        Targets.Clear();
        PlayerHealth[] sceneTargets = Object.FindObjectsByType<PlayerHealth>(FindObjectsInactive.Exclude);
        for (int i = 0; i < sceneTargets.Length; i++)
            Register(sceneTargets[i]);
    }
}
