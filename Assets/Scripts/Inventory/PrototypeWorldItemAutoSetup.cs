using UnityEngine;

public static class PrototypeWorldItemAutoSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ValidateConfiguredPrototypeItems()
    {
        WorldPickupItem[] pickupItems = Object.FindObjectsByType<WorldPickupItem>(FindObjectsInactive.Include);
        for (int i = 0; i < pickupItems.Length; i++)
        {
            WorldPickupItem pickupItem = pickupItems[i];
            if (pickupItem != null)
                pickupItem.ValidateAuthoringState();
        }
    }
}
