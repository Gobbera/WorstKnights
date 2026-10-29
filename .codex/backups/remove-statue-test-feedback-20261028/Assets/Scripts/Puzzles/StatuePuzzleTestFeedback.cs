using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(StatuePuzzleController))]
[AddComponentMenu("World/Puzzles/Statue Puzzle Test Feedback")]
public sealed class StatuePuzzleTestFeedback : MonoBehaviour
{
    [SerializeField, Min(0f)] private float failureDamage = 50f;

    public void PrintSolved()
    {
        if (isActiveAndEnabled)
            Debug.Log("Acertou!", this);
    }

    public void PrintFailedAndDamage()
    {
        if (!isActiveAndEnabled)
            return;

        Debug.Log("Falhou!", this);
        PlayerHealth player = GetComponent<StatuePuzzleController>().SubmittingPlayer;
        if (player == null || !player.IsAlive || failureDamage <= 0f)
            return;

        player.ReceiveDamage(new DamageInfo(failureDamage, gameObject, CombatAlignment.Neutral,
            player.transform.position, Vector3.zero,
            PlayerDamageAnimationType.ReactionDamage, PlayerCameraImpactType.DefaultHit));
    }
}
