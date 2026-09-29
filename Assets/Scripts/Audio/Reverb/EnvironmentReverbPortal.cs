using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Audio/Reverb/Environment Reverb Portal")]
public sealed class EnvironmentReverbPortal : EnvironmentReverbTriggerVolume
{
    [Header("Door State")]
    [SerializeField] private DoorController door;
    [SerializeField] private bool useDoorStateProfiles = true;
    [SerializeField] private EnvironmentReverbProfile openProfile;
    [SerializeField] private EnvironmentReverbProfile closedProfile;
    [SerializeField] private EnvironmentReverbProfile movingProfile;

    public override EnvironmentReverbProfile Profile
    {
        get
        {
            if (!useDoorStateProfiles || door == null)
                return profile;

            if (door.IsMoving && movingProfile != null)
                return movingProfile;

            if (door.IsOpen)
                return openProfile != null ? openProfile : profile;

            return closedProfile != null ? closedProfile : profile;
        }
    }

    protected override void Reset()
    {
        base.Reset();
        ResolveDoorReference();
    }

    protected override void Awake()
    {
        base.Awake();
        ResolveDoorReference();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        ResolveDoorReference();
    }

    private void ResolveDoorReference()
    {
        if (door == null)
            door = GetComponentInParent<DoorController>();
    }
}
