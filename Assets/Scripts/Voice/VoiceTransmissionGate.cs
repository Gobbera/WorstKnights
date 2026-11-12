/// <summary>Requires a fresh push-to-talk press after a channel or input interruption.</summary>
public sealed class VoiceTransmissionGate
{
    private bool requiresRelease = true;
    private VoiceTransmissionMode previousMode;

    public void Reset() { requiresRelease = true; }

    public bool Evaluate(VoiceTransmissionMode mode, bool ready, bool focused, bool keyHeld, bool capturingBinding)
    {
        if (mode != previousMode)
        {
            requiresRelease = true;
            previousMode = mode;
        }

        if (!ready || !focused || capturingBinding)
        {
            requiresRelease = true;
            return false;
        }

        if (mode == VoiceTransmissionMode.OpenMicrophone) return true;
        if (!keyHeld) requiresRelease = false;
        return keyHeld && !requiresRelease;
    }
}
