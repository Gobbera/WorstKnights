using UnityEngine;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemySetup))]
[AddComponentMenu("Enemy/Enemy Ground Anchor")]
public sealed class EnemyGroundAnchor : MonoBehaviour
{
    private const int GroundHitBufferSize = 8;

    private static PhysicsMaterial runtimeLowFrictionColliderMaterial;

    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CapsuleCollider capsule;

    [Header("Collider Contact")]
    [SerializeField] private bool applyLowFrictionColliderMaterial = true;

    [Header("Ground Anchor")]
    [SerializeField] private bool enableGroundAnchor = true;
    [SerializeField] private LayerMask groundMask = Physics.DefaultRaycastLayers;
    [SerializeField] [Range(0f, 1f)] private float minimumGroundNormalY = 0.35f;
    [SerializeField] private bool preserveInitialGroundOffset = true;
    [SerializeField] [Min(0f)] private float groundOffset;
    [SerializeField] [Min(0f)] private float probeStartHeight = 1.5f;
    [SerializeField] [Min(0.05f)] private float probeDistance = 5f;
    [SerializeField] [Min(0f)] private float maxSnapUpDistance = 1.5f;
    [SerializeField] [Min(0f)] private float maxSnapDownDistance = 5f;
    [SerializeField] [Min(0f)] private float movementProbeRadiusScale = 1f;
    [SerializeField] [Min(0f)] private float minimumMovementProbeSpeed = 0.05f;
    [SerializeField] [Min(0f)] private float maxMovementProbeUpDistance = 1.5f;
    [SerializeField] [Min(0f)] private float maxMovementProbeDownDistance = 1.5f;
    [SerializeField] [Min(0f)] private float correctionSpeed;
    [SerializeField] private bool followGroundSlopeVelocity = true;
    [SerializeField] [Min(0f)] private float maxSlopeVerticalSpeed = 5f;
    [SerializeField] private bool zeroVerticalVelocity = true;

    [Header("Debug")]
    [SerializeField] private bool drawGroundAnchorGizmos = true;
    [SerializeField] private Color groundAnchorGizmoColor = new Color(0.35f, 0.85f, 1f, 0.9f);

    private readonly RaycastHit[] groundHits = new RaycastHit[GroundHitBufferSize];
    private bool hasGroundOffset;
    private bool hasLastGroundHit;
    private Vector3 lastGroundPoint;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        hasGroundOffset = !preserveInitialGroundOffset;
        TryInitializeGroundOffset();
    }

    private void FixedUpdate()
    {
        ResolveReferences();

        if (!ShouldAnchor())
            return;

        if (!TryFindGround(out RaycastHit groundHit))
            return;

        if (!hasGroundOffset)
            CaptureGroundOffset(groundHit);

        AnchorToGround(groundHit, Time.fixedDeltaTime);
    }

    private void ResolveReferences()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (rb == null)
            rb = enemySetup != null ? enemySetup.Rigidbody : GetComponent<Rigidbody>();

        if (capsule == null)
            capsule = enemySetup != null ? enemySetup.Capsule : GetComponent<CapsuleCollider>();

        ApplyColliderContactTuning();
    }

    private bool ShouldAnchor()
    {
        if (!enableGroundAnchor || rb == null || rb.isKinematic)
            return false;

        if (enemySetup != null)
        {
            if (!enemySetup.HasAuthority)
                return false;

            EnemyHealth enemyHealth = enemySetup.EnemyHealth;
            if (enemyHealth != null && !enemyHealth.IsAlive)
                return false;
        }

        return true;
    }

    private void TryInitializeGroundOffset()
    {
        if (!ShouldAnchor() || hasGroundOffset)
            return;

        if (TryFindGround(out RaycastHit groundHit))
            CaptureGroundOffset(groundHit);
    }

    private void CaptureGroundOffset(RaycastHit groundHit)
    {
        groundOffset = Mathf.Max(0f, ResolveBodyPosition().y - groundHit.point.y);
        hasGroundOffset = true;
    }

    private void AnchorToGround(RaycastHit groundHit, float deltaTime)
    {
        Vector3 bodyPosition = ResolveBodyPosition();
        float targetY = groundHit.point.y + Mathf.Max(0f, groundOffset);
        float deltaY = targetY - bodyPosition.y;

        if (deltaY > maxSnapUpDistance || -deltaY > maxSnapDownDistance)
            return;

        if (Mathf.Abs(deltaY) > 0.0001f)
        {
            bodyPosition.y = correctionSpeed <= 0f
                ? targetY
                : Mathf.MoveTowards(bodyPosition.y, targetY, correctionSpeed * deltaTime);
            rb.MovePosition(bodyPosition);
        }

        ApplyAnchoredVelocity(groundHit);

        hasLastGroundHit = true;
        lastGroundPoint = groundHit.point;
    }

    private Vector3 ResolveBodyPosition()
    {
        return rb != null ? rb.position : transform.position;
    }

    private bool TryFindGround(out RaycastHit bestHit)
    {
        bestHit = default;

        Vector3 bodyPosition = ResolveBodyPosition();
        float distance = Mathf.Max(0.05f, probeStartHeight + probeDistance);
        int mask = groundMask.value == 0 ? Physics.DefaultRaycastLayers : groundMask.value;

        bool hasCenterHit = TryFindGroundAt(bodyPosition, distance, mask, out RaycastHit centerHit);

        if (hasGroundOffset
            && TryResolveMovementProbeOffset(out Vector3 movementProbeOffset)
            && TryFindGroundAt(bodyPosition + movementProbeOffset, distance, mask, out RaycastHit movementHit)
            && CanUseMovementHit(movementHit, hasCenterHit, centerHit, bodyPosition))
        {
            bestHit = movementHit;
            return true;
        }

        if (hasCenterHit)
        {
            bestHit = centerHit;
            return true;
        }

        return false;
    }

    private bool TryFindGroundAt(Vector3 bodyPosition, float distance, int mask, out RaycastHit bestHit)
    {
        bestHit = default;

        Vector3 origin = bodyPosition + Vector3.up * Mathf.Max(0f, probeStartHeight);
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            groundHits,
            distance,
            mask,
            QueryTriggerInteraction.Ignore);

        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null || hitCollider == capsule || hitCollider.transform.IsChildOf(transform))
                continue;

            if (hit.normal.y < minimumGroundNormalY)
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            bestHit = hit;
        }

        return closestDistance < float.PositiveInfinity;
    }

    private bool TryResolveMovementProbeOffset(out Vector3 offset)
    {
        offset = Vector3.zero;

        Vector3 planarVelocity = rb != null
            ? Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up)
            : Vector3.zero;

        if (planarVelocity.sqrMagnitude < minimumMovementProbeSpeed * minimumMovementProbeSpeed)
            return false;

        float probeDistanceFromCenter = ResolveHorizontalCapsuleRadius() * movementProbeRadiusScale;
        if (probeDistanceFromCenter <= 0.001f)
            return false;

        offset = planarVelocity.normalized * probeDistanceFromCenter;
        return true;
    }

    private float ResolveHorizontalCapsuleRadius()
    {
        if (capsule == null)
            return 0f;

        Vector3 scale = capsule.transform.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        return Mathf.Max(0f, capsule.radius * horizontalScale);
    }

    private bool IsWithinSnapRange(RaycastHit groundHit, Vector3 bodyPosition)
    {
        float targetY = groundHit.point.y + Mathf.Max(0f, groundOffset);
        float deltaY = targetY - bodyPosition.y;
        return deltaY <= maxSnapUpDistance && -deltaY <= maxSnapDownDistance;
    }

    private bool CanUseMovementHit(
        RaycastHit movementHit,
        bool hasCenterHit,
        RaycastHit centerHit,
        Vector3 bodyPosition)
    {
        // The movement probe is for ramp continuity, not for large ledge snaps.
        if (!IsWithinSnapRange(movementHit, bodyPosition))
            return false;

        if (hasCenterHit)
        {
            float surfaceDeltaY = movementHit.point.y - centerHit.point.y;
            return surfaceDeltaY <= maxMovementProbeUpDistance
                && -surfaceDeltaY <= maxMovementProbeDownDistance;
        }

        float targetY = movementHit.point.y + Mathf.Max(0f, groundOffset);
        float bodyDeltaY = targetY - bodyPosition.y;
        return bodyDeltaY <= maxMovementProbeUpDistance
            && -bodyDeltaY <= maxMovementProbeDownDistance;
    }

    private void ApplyAnchoredVelocity(RaycastHit groundHit)
    {
        if (rb == null)
            return;

        Vector3 currentVelocity = rb.linearVelocity;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(currentVelocity, Vector3.up);
        if (!followGroundSlopeVelocity || planarVelocity.sqrMagnitude <= minimumMovementProbeSpeed * minimumMovementProbeSpeed)
        {
            if (zeroVerticalVelocity)
                rb.linearVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
            return;
        }

        Vector3 slopeDirection = Vector3.ProjectOnPlane(planarVelocity.normalized, groundHit.normal);
        Vector3 slopeDirectionPlanar = Vector3.ProjectOnPlane(slopeDirection, Vector3.up);
        if (slopeDirectionPlanar.sqrMagnitude <= 0.0001f)
        {
            if (zeroVerticalVelocity)
                rb.linearVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
            return;
        }

        float verticalVelocity = slopeDirection.y * (planarVelocity.magnitude / slopeDirectionPlanar.magnitude);
        verticalVelocity = Mathf.Clamp(verticalVelocity, -maxSlopeVerticalSpeed, maxSlopeVerticalSpeed);
        rb.linearVelocity = new Vector3(planarVelocity.x, verticalVelocity, planarVelocity.z);
    }

    private void ApplyColliderContactTuning()
    {
        if (!applyLowFrictionColliderMaterial || capsule == null)
            return;

        PhysicsMaterial lowFrictionMaterial = GetOrCreateLowFrictionColliderMaterial();
        if (capsule.sharedMaterial != lowFrictionMaterial)
            capsule.sharedMaterial = lowFrictionMaterial;
    }

    private static PhysicsMaterial GetOrCreateLowFrictionColliderMaterial()
    {
        if (runtimeLowFrictionColliderMaterial != null)
            return runtimeLowFrictionColliderMaterial;

        runtimeLowFrictionColliderMaterial = new PhysicsMaterial("EnemyHoverLowFriction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        runtimeLowFrictionColliderMaterial.hideFlags = HideFlags.HideAndDontSave;
        return runtimeLowFrictionColliderMaterial;
    }

    private void OnValidate()
    {
        groundOffset = Mathf.Max(0f, groundOffset);
        minimumGroundNormalY = Mathf.Clamp01(minimumGroundNormalY);
        probeStartHeight = Mathf.Max(0f, probeStartHeight);
        probeDistance = Mathf.Max(0.05f, probeDistance);
        maxSnapUpDistance = Mathf.Max(0f, maxSnapUpDistance);
        maxSnapDownDistance = Mathf.Max(0f, maxSnapDownDistance);
        movementProbeRadiusScale = Mathf.Max(0f, movementProbeRadiusScale);
        minimumMovementProbeSpeed = Mathf.Max(0f, minimumMovementProbeSpeed);
        maxMovementProbeUpDistance = Mathf.Max(0f, maxMovementProbeUpDistance);
        maxMovementProbeDownDistance = Mathf.Max(0f, maxMovementProbeDownDistance);
        correctionSpeed = Mathf.Max(0f, correctionSpeed);
        maxSlopeVerticalSpeed = Mathf.Max(0f, maxSlopeVerticalSpeed);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGroundAnchorGizmos)
            return;

        Vector3 bodyPosition = rb != null ? rb.position : transform.position;
        Vector3 origin = bodyPosition + Vector3.up * Mathf.Max(0f, probeStartHeight);
        float distance = Mathf.Max(0.05f, probeStartHeight + probeDistance);

        Gizmos.color = groundAnchorGizmoColor;
        Gizmos.DrawLine(origin, origin + Vector3.down * distance);

        if (hasLastGroundHit)
            Gizmos.DrawWireSphere(lastGroundPoint, 0.08f);
    }
}
