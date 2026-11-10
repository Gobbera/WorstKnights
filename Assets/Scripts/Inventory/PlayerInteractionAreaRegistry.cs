using System.Collections.Generic;
using UnityEngine;

// Area providers register themselves; aim resolution does not scan scene objects every frame.
public static class PlayerInteractionAreaRegistry
{
    private static readonly HashSet<MonoBehaviour> providers = new HashSet<MonoBehaviour>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => providers.Clear();
    public static void Register(MonoBehaviour provider) { if (provider is IPlayerInteractionAreaProvider) providers.Add(provider); }
    public static void Unregister(MonoBehaviour provider) => providers.Remove(provider);
    public static float GetSearchDistance(PlayerPickupInteractor interactor, float defaultDistance)
    {
        float distance = defaultDistance;
        foreach (var provider in providers)
            if (provider != null && provider.isActiveAndEnabled && provider is IPlayerInteractionAreaProvider area)
                distance = Mathf.Max(distance, area.GetAreaInteractionReach(interactor));
        return distance;
    }
}
