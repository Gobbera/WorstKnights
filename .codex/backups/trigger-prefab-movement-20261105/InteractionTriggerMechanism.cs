using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
[AddComponentMenu("World/Mechanisms/Interaction Trigger Mechanism")]
public sealed class InteractionTriggerMechanism : MonoBehaviour
{
    [SerializeField] private InteractionTriggerType mechanismType;
    [SerializeField] private Transform movingPart;
    [SerializeField] private Vector3 rotationAxis = Vector3.right;
    [SerializeField] private Vector3 localPressDirection = Vector3.down;
    [SerializeField] private BoxCollider pressureSurface;
    [Header("Audio")]
    [SerializeField] private AudioCue activationAudioCue;
    [SerializeField] private AudioCue deactivationAudioCue;
    [SerializeField] private AudioSource audioSource;

    private Vector3 releasedPosition, targetPosition;
    private Quaternion releasedRotation, targetRotation;
    private Rigidbody movingBody;
    private bool captured;
    private AudioCue preparedActivationCue, preparedDeactivationCue;
    private bool activeTarget, audioPrepared;
    private float configuredDistance, configuredAngle;
    public InteractionTriggerType Type => mechanismType;
    public BoxCollider PressureSurface => pressureSurface;
    public Transform MovingPart => movingPart;

    private void Awake() { CapturePose(); PrepareAudio(); }
    public void CapturePose()
    {
        if (captured) return;
        if (movingPart == null) movingPart = transform;
        releasedPosition = movingPart.localPosition;
        releasedRotation = movingPart.localRotation;
        movingBody = movingPart.GetComponent<Rigidbody>();
        targetPosition = movingPart.position;
        targetRotation = movingPart.rotation;
        captured = true;
    }
    private Vector3 RestWorldPosition => movingPart.parent != null ? movingPart.parent.TransformPoint(releasedPosition) : releasedPosition;
    private Quaternion RestWorldRotation => movingPart.parent != null ? movingPart.parent.rotation * releasedRotation : releasedRotation;

    public void SetState(bool active, bool playAudio, bool immediate, float distance, float angle)
    {
        CapturePose();
        activeTarget = active; configuredDistance = distance; configuredAngle = angle;
        UpdateTargets();
        if (immediate) ApplyPose(targetPosition, targetRotation, true);
        if (playAudio) PlayAudio(active ? activationAudioCue : deactivationAudioCue);
    }
    private void UpdateTargets()
    {
        Vector3 direction = mechanismType == InteractionTriggerType.PressureButton ? Vector3.down
            : movingPart.parent != null ? movingPart.parent.TransformDirection(localPressDirection).normalized : localPressDirection.normalized;
        targetPosition = RestWorldPosition + (activeTarget && mechanismType != InteractionTriggerType.Lever ? direction * configuredDistance : Vector3.zero);
        targetRotation = RestWorldRotation * (activeTarget && mechanismType == InteractionTriggerType.Lever ? Quaternion.AngleAxis(configuredAngle, rotationAxis.normalized) : Quaternion.identity);
    }
    public void MovePresentation(float deltaTime, float speed, float angularSpeed)
    {
        CapturePose(); PrepareAudio(); UpdateTargets();
        bool useBody = Application.IsPlaying(gameObject) && movingBody != null;
        Vector3 current = useBody ? movingBody.position : movingPart.position;
        Quaternion rotation = useBody ? movingBody.rotation : movingPart.rotation;
        Vector3 next = Vector3.MoveTowards(current, targetPosition, Mathf.Max(0f, deltaTime) * speed);
        Quaternion nextRotation = Quaternion.RotateTowards(rotation, targetRotation, Mathf.Max(0f, deltaTime) * angularSpeed);
        if ((next - current).sqrMagnitude < 0.00000001f && Quaternion.Angle(rotation, nextRotation) < 0.001f) return;
        ApplyPose(next, nextRotation, false);
    }
    private void ApplyPose(Vector3 position, Quaternion rotation, bool immediate)
    {
        if (Application.IsPlaying(gameObject) && movingBody != null)
        {
            if (immediate)
            {
                // Keep the rendered pose current even when interpolation has a pending physics sample.
                movingPart.SetPositionAndRotation(position, rotation);
                movingBody.position = position; movingBody.rotation = rotation;
            }
            else { movingBody.MovePosition(position); movingBody.MoveRotation(rotation); }
        }
        else movingPart.SetPositionAndRotation(position, rotation);
    }
    public void GetDetectionBox(float distance, float height, float tolerance, out Vector3 center, out Vector3 halfSize, out float surfaceHeight)
    {
        Transform part = movingPart != null ? movingPart : transform;
        Vector3 restPosition = captured ? RestWorldPosition : part.position;
        Quaternion restRotation = captured ? RestWorldRotation : part.rotation;
        Matrix4x4 rest = Matrix4x4.TRS(restPosition, restRotation, part.lossyScale) * part.worldToLocalMatrix * pressureSurface.transform.localToWorldMatrix;
        Vector3 surface = rest.MultiplyPoint3x4(pressureSurface.center + Vector3.up * pressureSurface.size.y * 0.5f);
        Vector3 size = Vector3.Scale(pressureSurface.size, pressureSurface.transform.lossyScale);
        center = surface + Vector3.up * (height - distance - tolerance) * 0.5f;
        halfSize = new Vector3(Mathf.Abs(size.x) * 0.5f, (height + distance + tolerance) * 0.5f, Mathf.Abs(size.z) * 0.5f);
        surfaceHeight = surface.y;
    }
    private void PrepareAudio()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        if (!audioPrepared && audioSource != null) { audioSource.playOnAwake = false; audioSource.loop = false; audioSource.dopplerLevel = 0f; audioPrepared = true; }
        if (!Application.IsPlaying(gameObject)) return;
        if (preparedActivationCue != activationAudioCue) { PrepareCue(activationAudioCue); preparedActivationCue = activationAudioCue; }
        if (preparedDeactivationCue != deactivationAudioCue) { PrepareCue(deactivationAudioCue); preparedDeactivationCue = deactivationAudioCue; }
    }
    private static void PrepareCue(AudioCue cue)
    {
        if (cue == null || cue.Clips == null) return;
        foreach (var clip in cue.Clips)
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
    }
    private void PlayAudio(AudioCue cue)
    {
        if (!Application.IsPlaying(gameObject) || cue == null || audioSource == null) return;
        var clip = cue.GetRandomClip();
        if (clip == null) return;
        cue.ConfigureAudioSource(audioSource, clip);
        audioSource.loop = false;
        audioSource.Play();
    }
    public void RestorePose()
    {
        if (captured && movingPart != null) ApplyPose(RestWorldPosition, RestWorldRotation, true);
        if (audioSource != null) audioSource.Stop();
    }
    private void OnDisable() => RestorePose();
}
