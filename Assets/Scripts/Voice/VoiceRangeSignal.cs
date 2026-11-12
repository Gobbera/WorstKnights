using System.Collections.Generic;
using UnityEngine;

/// <summary>Transient sender metadata. No audio samples, position or listener preferences.</summary>
public readonly struct VoiceRangePacket
{
    public const int MaximumAgeMilliseconds = 2000;
    public bool Calibrated { get; }
    public byte Level { get; }
    public ushort Sequence { get; }
    public int Timestamp { get; }
    public float Intensity => Level / 255f;

    public VoiceRangePacket(bool calibrated, byte level, ushort sequence, int timestamp)
    { Calibrated = calibrated; Level = calibrated ? level : (byte)0; Sequence = sequence; Timestamp = timestamp; }

    public byte[] Encode() => new[] { (byte)1, Calibrated ? (byte)1 : (byte)0, Level,
        (byte)Sequence, (byte)(Sequence >> 8), (byte)Timestamp, (byte)(Timestamp >> 8),
        (byte)(Timestamp >> 16), (byte)(Timestamp >> 24) };

    public static bool TryDecode(object data, int serverNow, out VoiceRangePacket packet)
    {
        packet = default;
        if (!(data is byte[] bytes) || bytes.Length != 9 || bytes[0] != 1 || bytes[1] > 1) return false;
        int timestamp = bytes[5] | bytes[6] << 8 | bytes[7] << 16 | bytes[8] << 24;
        int age = unchecked(serverNow - timestamp);
        if (age < -1000 || age > MaximumAgeMilliseconds) return false;
        packet = new VoiceRangePacket(bytes[1] == 1, bytes[2], (ushort)(bytes[3] | bytes[4] << 8), timestamp);
        return true;
    }

    public static byte Quantize(float intensity)
        => (byte)Mathf.RoundToInt((float.IsNaN(intensity) || float.IsInfinity(intensity) ? 0f : Mathf.Clamp01(intensity)) * 255f);
}

public sealed class VoiceRangePublisher
{
    private ushort sequence;
    private bool sent, calibrated;
    private byte level;
    private float lastSentAt, nextSendAt;
    public bool HasAudibleSignal => sent && level > 0;

    public bool TryCreate(float now, int serverTimestamp, bool hasCalibration, float intensity,
        out VoiceRangePacket packet, bool force = false)
    {
        packet = default;
        byte quantized = hasCalibration ? VoiceRangePacket.Quantize(intensity) : (byte)0;
        if (!force && sent && (now < nextSendAt ||
            (calibrated == hasCalibration && Mathf.Abs(level - quantized) < 3 && now - lastSentAt < 0.5f))) return false;
        sequence = unchecked((ushort)(sequence + 1));
        packet = new VoiceRangePacket(hasCalibration, quantized, sequence, serverTimestamp);
        calibrated = hasCalibration; level = quantized; sent = true;
        lastSentAt = now; nextSendAt = now + 0.2f;
        return true;
    }

    public void Reset() { sent = false; level = 0; lastSentAt = nextSendAt = 0f; }
}

public sealed class VoiceRangeReceiver
{
    private sealed class Signal
    {
        public ushort Sequence;
        public int Timestamp;
        public bool Calibrated;
        public float ReceivedAt, Target, Smoothed;
    }
    private readonly Dictionary<int, Signal> signals = new Dictionary<int, Signal>();
    public int Count => signals.Count;

    public bool Receive(int actor, VoiceRangePacket packet, float now, float ageSeconds = 0f)
    {
        if (actor <= 0) return false;
        bool known = signals.TryGetValue(actor, out var signal);
        if (known)
        {
            ushort advance = unchecked((ushort)(packet.Sequence - signal.Sequence));
            if (advance == 0 || advance >= 32768 || unchecked(packet.Timestamp - signal.Timestamp) < 0) return false;
        }
        else { signal = new Signal(); signals.Add(actor, signal); }
        signal.Sequence = packet.Sequence; signal.Timestamp = packet.Timestamp;
        signal.Calibrated = packet.Calibrated;
        signal.ReceivedAt = now - Mathf.Clamp(ageSeconds, 0f, MaximumAgeSeconds);
        signal.Target = packet.Intensity;
        if (!known || !packet.Calibrated) signal.Smoothed = signal.Target;
        return true;
    }

    public void Update(float now, float deltaTime)
    {
        float dt = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ? 0f : Mathf.Max(0f, deltaTime);
        foreach (var signal in signals.Values)
        {
            float target = now - signal.ReceivedAt <= MaximumAgeSeconds ? signal.Target : 0f;
            float duration = target > signal.Smoothed ? 0.1f : 0.35f;
            signal.Smoothed = Mathf.Lerp(signal.Smoothed, target, 1f - Mathf.Exp(-dt / duration));
        }
    }

    public bool TryGetIntensity(int actor, out float intensity)
    {
        intensity = 0f;
        if (!signals.TryGetValue(actor, out var signal) || !signal.Calibrated) return false;
        intensity = signal.Smoothed; return true;
    }

    public void Remove(int actor) => signals.Remove(actor);
    public void Clear() => signals.Clear();
    private const float MaximumAgeSeconds = VoiceRangePacket.MaximumAgeMilliseconds / 1000f;
}
