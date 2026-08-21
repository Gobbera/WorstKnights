using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class PlayerMovement
{
    private void RefreshSurfaceState()
    {
        surfaceState = ProbeSurface(desiredMoveDirection);

        if (IsJumpGroundSuppressed())
            SuppressGrounding(ref surfaceState);

        IsGrounded = surfaceState.HasGround;
        OnSlope = surfaceState.IsWalkableSlope || surfaceState.IsSlidingSlope;
        IsSlidingOnSlope = surfaceState.IsSlidingSlope;
        IsTouchingWall = surfaceState.HasWallBlock;

        if (surfaceState.HasGround && (surfaceState.IsWalkableSlope || surfaceState.HasStep))
            surfaceAdhesionEligibleUntil = Time.time + SurfaceAdhesionGraceTime;

        if (surfaceState.HasGround && rb != null)
            airborneMomentumSpeed = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up).magnitude;
    }

    private SurfaceProbeResult ProbeSurface(Vector3 moveDirection)
    {
        SurfaceProbeResult result = new SurfaceProbeResult();
        if (config == null)
            return result;

        CapsuleShape capsuleShape = CapsuleShape.Create(transform, capsule, config.playerHeight);
        int collisionMask = GetCollisionMask();

        result.GroundProbeRadius = capsuleShape.Radius * Mathf.Clamp(config.groundProbeRadiusScale, 0.5f, 1f);
        result.GroundProbeDistance = Mathf.Max(config.groundProbeDistance, 0.05f);
        result.GroundProbeOrigin = capsuleShape.BottomHemisphereCenter + Vector3.up * SurfaceProbeVerticalOffset;

        if (TrySphereCastIgnoringSelf(result.GroundProbeOrigin, result.GroundProbeRadius, Vector3.down, result.GroundProbeDistance + SurfaceProbeVerticalOffset, collisionMask, out RaycastHit groundHit))
        {
            float groundAngle = Vector3.Angle(Vector3.up, groundHit.normal);
            if (groundAngle <= config.slideSlopeAngle)
            {
                result.HasGround = true;
                result.GroundHit = groundHit;
                result.GroundNormal = groundHit.normal;
                result.GroundAngle = groundAngle >= config.minSlopeAngleToAffect ? groundAngle : 0f;
                result.IsWalkableSlope = groundAngle >= config.minSlopeAngleToAffect && groundAngle <= config.maxSlopeAngle;
                result.IsSlidingSlope = !exitingSlope && groundAngle > config.maxSlopeAngle && groundAngle <= config.slideSlopeAngle;
            }
        }

        result.WallProbeRadius = capsuleShape.Radius * Mathf.Clamp(config.wallCheckRadiusScale, 0.3f, 1f);
        result.WallProbeDistance = Mathf.Max(config.wallCheckDistance, 0.05f);

        float lowerProbeHeight = result.WallProbeRadius + Mathf.Max(0.02f, config.maxStepHeight * 0.5f);
        float upperProbeHeight = Mathf.Clamp(capsuleShape.Height * config.upperWallCheckHeightRatio, result.WallProbeRadius + 0.05f, capsuleShape.Height - result.WallProbeRadius);

        result.LowerWallProbeOrigin = capsuleShape.LowestPoint + Vector3.up * lowerProbeHeight;
        result.UpperWallProbeOrigin = capsuleShape.LowestPoint + Vector3.up * upperProbeHeight;

        Vector3 horizontalMove = Vector3.ProjectOnPlane(moveDirection, Vector3.up);
        if (horizontalMove.sqrMagnitude <= 0.0001f)
            return result;

        result.ProbeDirection = horizontalMove.normalized;

        bool hasLowerHit = TrySphereCastForWallSlide(result.LowerWallProbeOrigin, result.WallProbeRadius, result.ProbeDirection, result.WallProbeDistance, collisionMask, out RaycastHit lowerHit);
        bool hasUpperHit = TrySphereCastForWallSlide(result.UpperWallProbeOrigin, result.WallProbeRadius, result.ProbeDirection, result.WallProbeDistance, collisionMask, out RaycastHit upperHit);

        if (hasLowerHit)
        {
            result.HasLowerHit = true;
            result.LowerHit = lowerHit;
        }

        if (hasUpperHit)
        {
            result.HasUpperHit = true;
            result.UpperHit = upperHit;
        }

        bool lowerIsWall = hasLowerHit && IsWallLike(lowerHit.normal);
        bool upperIsWall = hasUpperHit && IsWallLike(upperHit.normal);

        if (upperIsWall)
        {
            RaycastHit preferredWallHit = lowerIsWall
                ? ChoosePreferredWallHit(lowerHit, upperHit, result.ProbeDirection)
                : upperHit;

            result.HasWallBlock = true;
            result.WallHit = preferredWallHit;
            result.WallNormal = preferredWallHit.normal;
            return result;
        }

        if (lowerIsWall && result.HasGround && !result.IsSlidingSlope && TryFindStep(result.ProbeDirection, capsuleShape, collisionMask, out RaycastHit stepHit, out float stepHeight))
        {
            result.HasStep = true;
            result.StepHit = stepHit;
            result.StepHeight = stepHeight;
            return result;
        }

        if (lowerIsWall)
        {
            result.HasWallBlock = true;
            result.WallHit = lowerHit;
            result.WallNormal = lowerHit.normal;
        }

        return result;
    }

    private bool TryFindStep(Vector3 moveDirection, CapsuleShape capsuleShape, int collisionMask, out RaycastHit stepHit, out float stepHeight)
    {
        stepHit = default;
        stepHeight = 0f;

        Vector3 origin = capsuleShape.LowestPoint
            + Vector3.up * (config.maxStepHeight + config.groundProbeDistance + 0.05f)
            + moveDirection * (capsuleShape.Radius + config.stepSearchDistance);

        float rayDistance = config.maxStepHeight + config.groundProbeDistance + 0.1f;
        if (!TryRaycastIgnoringSelf(origin, Vector3.down, rayDistance, collisionMask, out stepHit))
            return false;

        float stepSurfaceAngle = Vector3.Angle(Vector3.up, stepHit.normal);
        if (stepSurfaceAngle > config.maxSlopeAngle)
            return false;

        stepHeight = stepHit.point.y - capsuleShape.LowestPoint.y;
        return stepHeight > 0.01f && stepHeight <= config.maxStepHeight;
    }

    private bool IsWallLike(Vector3 normal)
    {
        float surfaceAngle = Vector3.Angle(Vector3.up, normal);
        return surfaceAngle > config.slideSlopeAngle;
    }

    private int GetCollisionMask()
    {
        int collisionMask = config.groundLayer.value == 0 ? Physics.DefaultRaycastLayers : config.groundLayer.value;
        return DestructibleDebrisCollision.ExcludeDebrisLayer(collisionMask);
    }

    private bool TrySphereCastIgnoringSelf(Vector3 origin, float radius, Vector3 direction, float distance, int collisionMask, out RaycastHit closestHit)
    {
        closestHit = default;
        int hitCount = Physics.SphereCastNonAlloc(origin, radius, direction, sphereCastHits, distance, collisionMask, QueryTriggerInteraction.Ignore);
        return TryGetClosestValidHit(sphereCastHits, hitCount, out closestHit);
    }

    private bool TrySphereCastForWallSlide(Vector3 origin, float radius, Vector3 direction, float distance, int collisionMask, out RaycastHit bestHit)
    {
        bestHit = default;
        int hitCount = Physics.SphereCastNonAlloc(origin, radius, direction, sphereCastHits, distance, collisionMask, QueryTriggerInteraction.Ignore);
        return TryGetBestWallSlideHit(sphereCastHits, hitCount, direction, out bestHit);
    }

    private bool TryRaycastIgnoringSelf(Vector3 origin, Vector3 direction, float distance, int collisionMask, out RaycastHit closestHit)
    {
        closestHit = default;
        int hitCount = Physics.RaycastNonAlloc(origin, direction, raycastHits, distance, collisionMask, QueryTriggerInteraction.Ignore);
        return TryGetClosestValidHit(raycastHits, hitCount, out closestHit);
    }

    private bool TryGetClosestValidHit(RaycastHit[] hits, int hitCount, out RaycastHit closestHit)
    {
        closestHit = default;
        bool foundHit = false;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || IsSelfCollider(hit.collider))
                continue;

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                foundHit = true;
            }
        }

        return foundHit;
    }

    private bool TryGetBestWallSlideHit(RaycastHit[] hits, int hitCount, Vector3 moveDirection, out RaycastHit bestHit)
    {
        bestHit = default;

        bool foundFallbackHit = false;
        RaycastHit fallbackHit = default;
        float fallbackDistance = float.PositiveInfinity;

        bool foundWallSlideHit = false;
        float bestPreservedMagnitude = -1f;
        float bestDistance = float.PositiveInfinity;
        Vector3 safeMoveDirection = moveDirection.sqrMagnitude > 0.0001f
            ? moveDirection.normalized
            : Vector3.zero;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || IsSelfCollider(hit.collider))
                continue;

            if (hit.distance < fallbackDistance)
            {
                fallbackDistance = hit.distance;
                fallbackHit = hit;
                foundFallbackHit = true;
            }

            if (!IsWallLike(hit.normal))
                continue;

            float preservedMagnitude = GetWallSlidePreservedMagnitude(safeMoveDirection, hit.normal);
            bool shouldReplace = preservedMagnitude > bestPreservedMagnitude + 0.001f;

            if (!shouldReplace
                && Mathf.Abs(preservedMagnitude - bestPreservedMagnitude) <= 0.001f
                && hit.distance < bestDistance)
            {
                shouldReplace = true;
            }

            if (!shouldReplace)
                continue;

            bestPreservedMagnitude = preservedMagnitude;
            bestDistance = hit.distance;
            bestHit = hit;
            foundWallSlideHit = true;
        }

        if (foundWallSlideHit)
            return true;

        if (!foundFallbackHit)
            return false;

        bestHit = fallbackHit;
        return true;
    }

    private RaycastHit ChoosePreferredWallHit(RaycastHit primaryHit, RaycastHit secondaryHit, Vector3 moveDirection)
    {
        float primaryPreservedMagnitude = GetWallSlidePreservedMagnitude(moveDirection, primaryHit.normal);
        float secondaryPreservedMagnitude = GetWallSlidePreservedMagnitude(moveDirection, secondaryHit.normal);

        if (secondaryPreservedMagnitude > primaryPreservedMagnitude + 0.001f)
            return secondaryHit;

        if (primaryPreservedMagnitude > secondaryPreservedMagnitude + 0.001f)
            return primaryHit;

        return secondaryHit.distance < primaryHit.distance ? secondaryHit : primaryHit;
    }

    private float GetWallSlidePreservedMagnitude(Vector3 moveDirection, Vector3 wallNormal)
    {
        if (moveDirection.sqrMagnitude <= 0.0001f)
            return 0f;

        Vector3 slideDirection = Vector3.ProjectOnPlane(moveDirection.normalized, wallNormal);
        return slideDirection.magnitude;
    }

    private bool IsSelfCollider(Collider collider)
    {
        if (collider == null)
            return false;

        if (capsule != null && collider == capsule)
            return true;

        if (rb != null && collider.attachedRigidbody == rb)
            return true;

        return collider.transform.root == transform.root;
    }

    private void OnDrawGizmosSelected()
    {
        if (config == null)
            return;

        SurfaceProbeResult gizmoState = Application.isPlaying
            ? surfaceState
            : ProbeSurface(orientation != null ? orientation.forward : transform.forward);

        DrawGroundGizmos(gizmoState);
        DrawWallAndStepGizmos(gizmoState);
        DrawSurfaceLabels(gizmoState);
    }

    private void DrawGroundGizmos(SurfaceProbeResult gizmoState)
    {
        Color groundColor = Color.red;
        if (gizmoState.IsWalkableSlope)
            groundColor = Color.green;
        else if (gizmoState.IsSlidingSlope)
            groundColor = new Color(1f, 0.55f, 0f);
        else if (gizmoState.HasGround)
            groundColor = Color.yellow;

        Gizmos.color = groundColor;
        Gizmos.DrawWireSphere(gizmoState.GroundProbeOrigin, gizmoState.GroundProbeRadius);
        Gizmos.DrawLine(gizmoState.GroundProbeOrigin, gizmoState.GroundProbeOrigin + Vector3.down * gizmoState.GroundProbeDistance);
        Gizmos.DrawWireSphere(gizmoState.GroundProbeOrigin + Vector3.down * gizmoState.GroundProbeDistance, gizmoState.GroundProbeRadius);

        if (!gizmoState.HasGround)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(gizmoState.GroundHit.point, gizmoState.GroundHit.point + gizmoState.GroundNormal * 0.6f);
        Gizmos.DrawWireSphere(gizmoState.GroundHit.point, 0.05f);
    }

    private void DrawWallAndStepGizmos(SurfaceProbeResult gizmoState)
    {
        if (gizmoState.ProbeDirection.sqrMagnitude <= 0.0001f)
            return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.7f);
        Gizmos.DrawWireSphere(gizmoState.LowerWallProbeOrigin, gizmoState.WallProbeRadius);
        Gizmos.DrawLine(gizmoState.LowerWallProbeOrigin, gizmoState.LowerWallProbeOrigin + gizmoState.ProbeDirection * gizmoState.WallProbeDistance);
        Gizmos.DrawWireSphere(gizmoState.LowerWallProbeOrigin + gizmoState.ProbeDirection * gizmoState.WallProbeDistance, gizmoState.WallProbeRadius);

        Gizmos.color = new Color(1f, 0.65f, 0f, 0.7f);
        Gizmos.DrawWireSphere(gizmoState.UpperWallProbeOrigin, gizmoState.WallProbeRadius);
        Gizmos.DrawLine(gizmoState.UpperWallProbeOrigin, gizmoState.UpperWallProbeOrigin + gizmoState.ProbeDirection * gizmoState.WallProbeDistance);
        Gizmos.DrawWireSphere(gizmoState.UpperWallProbeOrigin + gizmoState.ProbeDirection * gizmoState.WallProbeDistance, gizmoState.WallProbeRadius);

        if (gizmoState.HasWallBlock)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(gizmoState.WallHit.point, 0.1f);
            Gizmos.DrawLine(gizmoState.WallHit.point, gizmoState.WallHit.point + gizmoState.WallNormal * 0.5f);
        }

        if (!gizmoState.HasStep)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(gizmoState.StepHit.point, 0.08f);
        Gizmos.DrawLine(gizmoState.StepHit.point, gizmoState.StepHit.point + Vector3.up * gizmoState.StepHeight);
    }

    private void DrawSurfaceLabels(SurfaceProbeResult gizmoState)
    {
#if UNITY_EDITOR
        if (gizmoState.HasGround)
        {
            string slopeMode = gizmoState.IsSlidingSlope ? "Slide" : gizmoState.IsWalkableSlope ? "Walkable" : "Ground";
            Handles.Label(gizmoState.GroundHit.point + Vector3.up * 0.15f, $"{slopeMode} {gizmoState.GroundAngle:F1} deg");
        }

        Vector3 labelPosition = transform.position + Vector3.up * 1.2f;
        string wallStatus = gizmoState.HasWallBlock ? "Wall block" : "Wall free";
        string stepStatus = gizmoState.HasStep ? $"Step {gizmoState.StepHeight:F2}m" : "No step";
        string jumpStatus = jumpQueued ? "Jump queued" : IsJumpGroundSuppressed() ? "Jump unground" : "Ground active";
        Handles.Label(labelPosition, $"{wallStatus}\n{stepStatus}\n{jumpStatus}");
#endif
    }

    private struct SurfaceProbeResult
    {
        public bool HasGround;
        public bool IsWalkableSlope;
        public bool IsSlidingSlope;
        public bool HasWallBlock;
        public bool HasStep;
        public bool HasLowerHit;
        public bool HasUpperHit;
        public float GroundAngle;
        public float StepHeight;
        public float GroundProbeRadius;
        public float GroundProbeDistance;
        public float WallProbeRadius;
        public float WallProbeDistance;
        public Vector3 GroundNormal;
        public Vector3 WallNormal;
        public Vector3 ProbeDirection;
        public Vector3 GroundProbeOrigin;
        public Vector3 LowerWallProbeOrigin;
        public Vector3 UpperWallProbeOrigin;
        public RaycastHit GroundHit;
        public RaycastHit WallHit;
        public RaycastHit LowerHit;
        public RaycastHit UpperHit;
        public RaycastHit StepHit;
    }

    private readonly struct CapsuleShape
    {
        public readonly float Height;
        public readonly float Radius;
        public readonly Vector3 Center;
        public readonly Vector3 LowestPoint;
        public readonly Vector3 BottomHemisphereCenter;

        private CapsuleShape(float height, float radius, Vector3 center, Vector3 lowestPoint, Vector3 bottomHemisphereCenter)
        {
            Height = height;
            Radius = radius;
            Center = center;
            LowestPoint = lowestPoint;
            BottomHemisphereCenter = bottomHemisphereCenter;
        }

        public static CapsuleShape Create(Transform target, CapsuleCollider capsuleCollider, float fallbackHeight)
        {
            float scaleX = Mathf.Abs(target.lossyScale.x);
            float scaleY = Mathf.Abs(target.lossyScale.y);
            float scaleZ = Mathf.Abs(target.lossyScale.z);

            float radius = capsuleCollider != null
                ? capsuleCollider.radius * Mathf.Max(scaleX, scaleZ)
                : 0.5f * Mathf.Max(scaleX, scaleZ);

            float height = capsuleCollider != null
                ? Mathf.Max(capsuleCollider.height * scaleY, radius * 2f)
                : Mathf.Max(fallbackHeight * scaleY, radius * 2f);

            Vector3 center = capsuleCollider != null
                ? target.TransformPoint(capsuleCollider.center)
                : target.position;

            Vector3 lowestPoint = center - Vector3.up * (height * 0.5f);
            Vector3 bottomHemisphereCenter = lowestPoint + Vector3.up * radius;

            return new CapsuleShape(height, radius, center, lowestPoint, bottomHemisphereCenter);
        }
    }
}
