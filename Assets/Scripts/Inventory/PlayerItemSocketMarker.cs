using UnityEngine;

public enum PlayerItemSocketEnvironment
{
    ThirdPerson = 0,
    FirstPerson = 1
}

[DisallowMultipleComponent]
public sealed class PlayerItemSocketMarker : MonoBehaviour
{
    [SerializeField] private PlayerItemSocketEnvironment environment = PlayerItemSocketEnvironment.ThirdPerson;
    [SerializeField] private HandType hand = HandType.Right;
    [SerializeField] private string displayName = string.Empty;

    public PlayerItemSocketEnvironment Environment => environment;
    public HandType Hand => hand;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
}
