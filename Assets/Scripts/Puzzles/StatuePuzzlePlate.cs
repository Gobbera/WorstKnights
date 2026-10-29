using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WorldPickupItem))]
[AddComponentMenu("World/Puzzles/Statue Puzzle Plate")]
public sealed class StatuePuzzlePlate : MonoBehaviour
{
    [SerializeField] private StatueIdentity identity;
    [Tooltip("Rotacao da placa em relacao ao Plate_Pivot. Os modelos atuais sao planos no eixo Y.")]
    [SerializeField] private Vector3 placementEulerAngles = new Vector3(270f, 0f, 270f);

    public StatueIdentity Identity => identity;
    public Quaternion PlacementRotation => Quaternion.Euler(placementEulerAngles);
    public WorldPickupItem Pickup => GetComponent<WorldPickupItem>();
}
