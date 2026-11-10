using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
[AddComponentMenu("World/Mechanisms/Interaction Trigger Mechanism")]
public sealed class InteractionTriggerMechanism : MonoBehaviour
{
    [SerializeField] private InteractionTriggerType mechanismType;
    [Tooltip("The transform that rotates or translates during activation.")]
    [SerializeField] private Transform movingPart;
    [Tooltip("Local rotation axis used by lever mechanisms.")]
    [SerializeField] private Vector3 rotationAxis = Vector3.right;
    [Tooltip("Press direction in the moving part's parent space. Pressure plates always move down in world space.")]
    [SerializeField] private Vector3 localPressDirection = Vector3.down;
    [Tooltip("Solid collider used as the pressure plate's contact surface.")]
    [SerializeField] private BoxCollider pressureSurface;
    [Tooltip("World-space travel distance in meters for buttons and pressure plates.")]
    [SerializeField, Min(0f)] private float pressDistance = 0.04f;
    [Tooltip("Rotation from the lever's resting pose, in degrees.")]
    [SerializeField] private float leverAngle = -50f;
    [Tooltip("Linear movement speed in meters per second.")]
    [SerializeField, Min(0.01f)] private float movementSpeed = 0.25f;
    [Tooltip("Lever rotation speed in degrees per second.")]
    [SerializeField, Min(1f)] private float rotationSpeed = 180f;
    [SerializeField] private AudioCue activationAudioCue;
    [SerializeField] private AudioCue deactivationAudioCue;
    [SerializeField] private AudioSource audioSource;

    private Vector3 releasedPosition, targetPosition;
    private Quaternion releasedRotation, targetRotation;
    private Rigidbody movingBody;
    private bool captured;
    private AudioCue preparedActivationCue, preparedDeactivationCue;
    private bool activeTarget, audioPrepared;
    private Quaternion presentedLocalRotation;
    private float presentedPressDistance;
    private Matrix4x4 presentationParentMatrix;
    public InteractionTriggerType Type => mechanismType;
    public BoxCollider PressureSurface => pressureSurface;
    public Transform MovingPart => movingPart;
    public float PressDistance => pressDistance;
    public float ActivationAngle => leverAngle;

    private void Awake() { CapturePose(); PrepareAudio(); }
    private void OnValidate()
    {
        pressDistance = Mathf.Max(0f, pressDistance);
        movementSpeed = Mathf.Max(0.01f, movementSpeed);
        rotationSpeed = Mathf.Max(1f, rotationSpeed);
    }
    public void CapturePose()
    {
        if (captured) return;
        if (movingPart == null) movingPart = transform;
        releasedPosition = movingPart.localPosition;
        releasedRotation = movingPart.localRotation;
        presentedLocalRotation = releasedRotation;
        presentedPressDistance = 0f;
        presentationParentMatrix = ParentMatrix;
        movingBody = movingPart.GetComponent<Rigidbody>();
        targetPosition = movingPart.position;
        targetRotation = movingPart.rotation;
        captured = true;
    }
    private Vector3 RestWorldPosition => movingPart.parent != null ? movingPart.parent.TransformPoint(releasedPosition) : releasedPosition;
    private Quaternion RestWorldRotation => movingPart.parent != null ? movingPart.parent.rotation * releasedRotation : releasedRotation;
    private Matrix4x4 ParentMatrix => movingPart.parent != null ? movingPart.parent.localToWorldMatrix : Matrix4x4.identity;
    private float DesiredPressDistance => activeTarget && mechanismType != InteractionTriggerType.Lever ? pressDistance : 0f;
    private Quaternion DesiredLocalRotation => releasedRotation * (activeTarget && mechanismType == InteractionTriggerType.Lever
        ? Quaternion.AngleAxis(leverAngle, rotationAxis.normalized) : Quaternion.identity);

    public void SetState(bool active, bool playAudio, bool immediate)
    {
        CapturePose();
        activeTarget = active;
        if (immediate)
        {
            presentedPressDistance = DesiredPressDistance;
            presentedLocalRotation = DesiredLocalRotation;
            presentationParentMatrix = ParentMatrix;
        }
        UpdateTargets();
        if (immediate) ApplyPose(targetPosition, targetRotation, true);
        if (playAudio) PlayAudio(active ? activationAudioCue : deactivationAudioCue);
    }
    private void UpdateTargets()
    {
        Vector3 direction = mechanismType == InteractionTriggerType.PressureButton ? Vector3.down
            : movingPart.parent != null ? movingPart.parent.TransformDirection(localPressDirection).normalized : localPressDirection.normalized;
        targetPosition = RestWorldPosition + direction * presentedPressDistance;
        targetRotation = movingPart.parent != null ? movingPart.parent.rotation * presentedLocalRotation : presentedLocalRotation;
    }
    public void MovePresentation(float deltaTime)
    {
        CapturePose(); PrepareAudio();
        var parentMatrix = ParentMatrix;
        bool parentMoved = parentMatrix != presentationParentMatrix;
        presentationParentMatrix = parentMatrix;
        float step = Mathf.Max(0f, deltaTime);
        // Animation progress belongs to the mechanism, not the interpolated world pose of its child body.
        presentedPressDistance = Mathf.MoveTowards(presentedPressDistance, DesiredPressDistance, step * movementSpeed);
        presentedLocalRotation = Quaternion.RotateTowards(presentedLocalRotation, DesiredLocalRotation, step * rotationSpeed);
        UpdateTargets();
        bool useBody = Application.IsPlaying(gameObject) && movingBody != null;
        Vector3 current = useBody ? movingBody.position : movingPart.position;
        Quaternion rotation = useBody ? movingBody.rotation : movingPart.rotation;
        if (!parentMoved && (targetPosition - current).sqrMagnitude < 0.00000001f && Quaternion.Angle(rotation, targetRotation) < 0.001f) return;
        // Moving a parent Transform invalidates the child's interpolation sample. Reconcile both poses
        // for this step so carrier transport cannot cancel the arm rotation or consume button travel.
        ApplyPose(targetPosition, targetRotation, parentMoved && useBody && movingBody.isKinematic);
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
    public void GetDetectionBox(float height, float tolerance, out Vector3 center, out Vector3 halfSize, out float surfaceHeight)
    {
        Transform part = movingPart != null ? movingPart : transform;
        Vector3 restPosition = captured ? RestWorldPosition : part.position;
        Quaternion restRotation = captured ? RestWorldRotation : part.rotation;
        Matrix4x4 rest = Matrix4x4.TRS(restPosition, restRotation, part.lossyScale) * part.worldToLocalMatrix * pressureSurface.transform.localToWorldMatrix;
        Vector3 surface = rest.MultiplyPoint3x4(pressureSurface.center + Vector3.up * pressureSurface.size.y * 0.5f);
        Vector3 size = Vector3.Scale(pressureSurface.size, pressureSurface.transform.lossyScale);
        center = surface + Vector3.up * (height - pressDistance - tolerance) * 0.5f;
        halfSize = new Vector3(Mathf.Abs(size.x) * 0.5f, (height + pressDistance + tolerance) * 0.5f, Mathf.Abs(size.z) * 0.5f);
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
        if (captured && movingPart != null)
        {
            presentedPressDistance = 0f;
            presentedLocalRotation = releasedRotation;
            presentationParentMatrix = ParentMatrix;
            ApplyPose(RestWorldPosition, RestWorldRotation, true);
        }
        if (audioSource != null) audioSource.Stop();
    }
    private void OnDisable() => RestorePose();
}
