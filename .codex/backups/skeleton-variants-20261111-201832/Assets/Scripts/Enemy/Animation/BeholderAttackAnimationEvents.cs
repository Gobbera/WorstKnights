using UnityEngine;

[DisallowMultipleComponent]
public sealed class BeholderAttackAnimationEvents : MonoBehaviour
{
    [SerializeField] private BeholderAttackController attackController;

    public void OnBeholderAttackEffect(string maskName)
    {
        if (attackController == null) attackController = GetComponentInParent<BeholderAttackController>();
        attackController?.QueueAnimationEffect(maskName);
    }
}
