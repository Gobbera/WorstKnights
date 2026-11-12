using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Combat/Enemy Kick Reaction")]
public sealed class EnemyKickReaction : MonoBehaviour
{
    [SerializeField] private bool canBePushedByKick = true;
    [SerializeField] [Min(0f)] private float knockbackMultiplier = 1f;
    [SerializeField] [Min(0f)] private float durationMultiplier = 1f;

    public bool CanBePushedByKick => canBePushedByKick;
    public float KnockbackMultiplier => canBePushedByKick ? Mathf.Max(0f, knockbackMultiplier) : 0f;
    public float DurationMultiplier => canBePushedByKick ? Mathf.Max(0f, durationMultiplier) : 0f;

    private void OnValidate()
    {
        knockbackMultiplier = Mathf.Max(0f, knockbackMultiplier);
        durationMultiplier = Mathf.Max(0f, durationMultiplier);
    }
}
