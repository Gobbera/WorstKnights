public interface IEnvironmentReverbSource
{
    string DisplayName { get; }
    EnvironmentReverbProfile Profile { get; }
    int Priority { get; }
    bool HasTransitionTimeOverride { get; }
    float TransitionTime { get; }
    bool IsReverbSourceActive { get; }
}
