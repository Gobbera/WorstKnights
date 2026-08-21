using UnityEngine;

[CreateAssetMenu(fileName = "ItemDefinition", menuName = "Inventory/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    [Header("Presentation")]
    [SerializeField] private string itemName = "Item";
    [SerializeField] private Sprite uiSprite;

    [Header("Equip Rules")]
    [SerializeField] private HandRequirement handRequirement = HandRequirement.Any;
    [SerializeField] private HandType preferredHand = HandType.Right;

    [Header("Use Rules")]
    [SerializeField] private ItemUseType useType = ItemUseType.None;
    [SerializeField] [Min(0f)] private float healAmount;
    [SerializeField] [Min(0f)] private float baseDamage = 25f;
    [SerializeField] private bool consumeOnUse;

    [Header("Economy")]
    [SerializeField] private bool canBeSold;
    [SerializeField] [Min(0)] private int sellPrice;

    public string ItemName => string.IsNullOrWhiteSpace(itemName) ? name : itemName;
    public Sprite UiSprite => uiSprite;
    public HandRequirement HandRequirement => handRequirement;
    public HandType PreferredHand => preferredHand;
    public ItemUseType UseType => useType;
    public float HealAmount => healAmount;
    public float BaseDamage => baseDamage;
    public bool ConsumeOnUse => consumeOnUse;
    public bool CanBeSold => canBeSold;
    public int SellPrice => Mathf.Max(0, sellPrice);

    public bool CanEquipInHand(HandType hand)
    {
        switch (handRequirement)
        {
            case HandRequirement.RightOnly:
                return hand == HandType.Right;
            case HandRequirement.LeftOnly:
                return hand == HandType.Left;
            case HandRequirement.Any:
                return true;
            case HandRequirement.TwoHanded:
                return hand == HandType.Right || hand == HandType.Left;
            default:
                return false;
        }
    }
}
