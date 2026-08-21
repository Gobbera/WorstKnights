using UnityEngine;

public static class DestructibleDebrisCollision
{
    public const string LayerName = "DestructibleDebris";

    public static int ExcludeDebrisLayer(int mask)
    {
        if (!TryGetDebrisLayer(out int debrisLayer))
            return mask;

        return mask & ~(1 << debrisLayer);
    }

    public static bool TryApplyDebrisLayer(GameObject root)
    {
        if (root == null || !TryGetDebrisLayer(out int debrisLayer))
            return false;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform child = transforms[i];
            if (child != null)
                child.gameObject.layer = debrisLayer;
        }

        return true;
    }

    public static bool TryGetDebrisLayer(out int debrisLayer)
    {
        debrisLayer = LayerMask.NameToLayer(LayerName);
        return debrisLayer >= 0;
    }
}
