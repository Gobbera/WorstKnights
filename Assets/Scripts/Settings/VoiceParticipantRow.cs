using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class VoiceParticipantRow : MonoBehaviour
{
    [SerializeField] private TMP_Text playerName;
    [SerializeField] private TMP_Text voiceStatus;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TMP_Text volumeLabel;
    [SerializeField] private Button muteButton;
    [SerializeField] private TMP_Text muteLabel;

    private int actorNumber;
    private bool muted;
    private bool eventsBound;
    private Action<int, float> volumeChanged;
    private Action<int, bool> muteChanged;

    public void Bind(RoomVoiceParticipant participant, Action<int, float> onVolume, Action<int, bool> onMute)
    {
        actorNumber = participant.ActorNumber;
        muted = participant.Listening.Muted;
        volumeChanged = onVolume; muteChanged = onMute;
        if (!eventsBound)
        {
            volumeSlider.onValueChanged.AddListener(SetVolume);
            muteButton.onClick.AddListener(ToggleMute);
            eventsBound = true;
        }
        playerName.richText = false;
        playerName.text = participant.DisplayName;
        voiceStatus.text = muted ? "Muted for you" : participant.Reconnecting ? "Reconnecting..."
            : participant.VoiceConnected ? "Voice connected" : "Voice unavailable";
        voiceStatus.color = muted ? new Color(1f, 0.55f, 0.5f) : new Color(0.65f, 0.68f, 0.72f);
        volumeSlider.SetValueWithoutNotify(participant.Listening.Volume);
        volumeLabel.text = $"{Mathf.RoundToInt(participant.Listening.Volume * 100f)}%";
        muteLabel.text = muted ? "Unmute" : "Mute";
    }

    private void SetVolume(float value)
    {
        volumeLabel.text = $"{Mathf.RoundToInt(value * 100f)}%";
        volumeChanged?.Invoke(actorNumber, value);
    }

    private void ToggleMute()
    {
        muted = !muted;
        muteLabel.text = muted ? "Unmute" : "Mute";
        muteChanged?.Invoke(actorNumber, muted);
    }

    private void OnDestroy()
    {
        if (eventsBound)
        {
            if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(SetVolume);
            if (muteButton != null) muteButton.onClick.RemoveListener(ToggleMute);
        }
    }
}
