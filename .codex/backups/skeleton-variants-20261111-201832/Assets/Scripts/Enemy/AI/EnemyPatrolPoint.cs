using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyPatrolPoint : MonoBehaviour
{
    [SerializeField] private bool overrideWaitTime;
    [SerializeField] [Min(0f)] private float waitTime = 1f;

    public float ResolveWaitTime(float fallbackWaitTime)
    {
        return overrideWaitTime ? Mathf.Max(0f, waitTime) : Mathf.Max(0f, fallbackWaitTime);
    }
}
