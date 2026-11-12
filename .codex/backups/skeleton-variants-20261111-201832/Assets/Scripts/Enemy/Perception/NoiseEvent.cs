using UnityEngine;

public readonly struct NoiseEvent
{
    public NoiseEvent(Vector3 position, float radius, float intensity, NoiseType noiseType, GameObject source)
    {
        Position = position;
        Radius = Mathf.Max(0f, radius);
        Intensity = Mathf.Max(0f, intensity);
        NoiseType = noiseType;
        Source = source;
    }

    public Vector3 Position { get; }
    public float Radius { get; }
    public float Intensity { get; }
    public NoiseType NoiseType { get; }
    public GameObject Source { get; }
}
