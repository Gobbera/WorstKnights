using System;
using UnityEngine;

[Serializable]
public sealed class EmoteWheelSlotConfig
{
    [SerializeField] private bool enabled = true;
    [SerializeField] private PlayerEmoteType emoteType = PlayerEmoteType.None;
    [SerializeField] private Sprite icon;
    [SerializeField] private string label;

    public bool Enabled => enabled;
    public PlayerEmoteType EmoteType => enabled ? emoteType : PlayerEmoteType.None;
    public Sprite Icon => icon;
    public bool HasEmote => enabled && emoteType != PlayerEmoteType.None;
    public string Label => string.IsNullOrWhiteSpace(label) ? GetDefaultLabel(emoteType) : label;

    public static EmoteWheelSlotConfig Create(PlayerEmoteType emoteType, string label = null, Sprite icon = null, bool enabled = true)
    {
        EmoteWheelSlotConfig slot = new EmoteWheelSlotConfig();
        slot.enabled = enabled;
        slot.emoteType = emoteType;
        slot.icon = icon;
        slot.label = label;
        return slot;
    }

    public static EmoteWheelSlotConfig CreateEmpty()
    {
        return Create(PlayerEmoteType.None, string.Empty, null, false);
    }

    private static string GetDefaultLabel(PlayerEmoteType emoteType)
    {
        switch (emoteType)
        {
            case PlayerEmoteType.ThumbsUp:
                return "Thumbs Up";
            case PlayerEmoteType.Point:
                return "Point";
            default:
                return string.Empty;
        }
    }
}
