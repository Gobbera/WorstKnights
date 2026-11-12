using System;
using UnityEngine;

public static class NoiseSystem
{
    public static event Action<NoiseEvent> NoiseEmitted;

    public static void EmitNoise(Vector3 position, float radius, float intensity = 1f, NoiseType noiseType = NoiseType.Generic, GameObject source = null)
    {
        NoiseEmitted?.Invoke(new NoiseEvent(position, radius, intensity, noiseType, source));
    }

    public static void EmitNoise(NoiseEvent noiseEvent)
    {
        NoiseEmitted?.Invoke(noiseEvent);
    }
}
