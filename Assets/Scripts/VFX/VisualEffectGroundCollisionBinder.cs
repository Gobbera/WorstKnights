using UnityEngine;
using UnityEngine.VFX;

[DisallowMultipleComponent]
[AddComponentMenu("VFX/Ground Collision Binder")]
public sealed class VisualEffectGroundCollisionBinder : MonoBehaviour
{
    private static readonly string[] CenterParameterNames =
    {
        "Ground Collision Center",
        "GroundCollisionCenter",
        "Ground_Collision_Center",
        "Collision Center",
        "CollisionCenter",
        "Collision_Center"
    };

    private static readonly string[] NormalParameterNames =
    {
        "Ground Collision Normal",
        "GroundCollisionNormal",
        "Ground_Collision_Normal",
        "Collision Normal",
        "CollisionNormal",
        "Collision_Normal"
    };

    private static readonly string[] SizeParameterNames =
    {
        "Ground Collision Size",
        "GroundCollisionSize",
        "Ground_Collision_Size",
        "Collision Size",
        "CollisionSize",
        "Collision_Size"
    };

    private static readonly string[] AnglesParameterNames =
    {
        "Ground Collision Angles",
        "GroundCollisionAngles",
        "Ground_Collision_Angles",
        "Collision Angles",
        "CollisionAngles",
        "Collision_Angles"
    };

    private static readonly string[] HeightParameterNames =
    {
        "Ground Collision Height",
        "GroundCollisionHeight",
        "Ground_Collision_Height",
        "Collision Height",
        "CollisionHeight",
        "Collision_Height",
        "Ground Height",
        "GroundHeight",
        "Ground_Height"
    };

    [Header("Target")]
    [SerializeField] private VisualEffect visualEffect;
    [SerializeField] private bool includeChildVisualEffects = true;

    [Header("Ground Probe")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
    [SerializeField] [Min(0f)] private float raycastStartHeight = 2f;
    [SerializeField] [Min(0.01f)] private float raycastDistance = 20f;

    [Header("Collision Shape")]
    [SerializeField] private Vector2 collisionSize = new Vector2(50f, 50f);
    [SerializeField] [Min(0.01f)] private float collisionThickness = 0.1f;
    [SerializeField] private float surfaceOffset = 0f;

    [Header("Playback")]
    [SerializeField] private bool bindOnEnable = true;
    [SerializeField] private bool reinitAfterBinding = true;
    [SerializeField] private bool playAfterBinding = true;

    [Header("Debug")]
    [SerializeField] private bool drawDebugRay;
    [SerializeField] private bool warnWhenNoParameterWasApplied;

    private void Reset()
    {
        visualEffect = GetComponent<VisualEffect>();
        groundMask = ResolveDefaultGroundMask();
    }

    private void OnValidate()
    {
        if (visualEffect == null)
            visualEffect = GetComponent<VisualEffect>();

        raycastStartHeight = Mathf.Max(0f, raycastStartHeight);
        raycastDistance = Mathf.Max(0.01f, raycastDistance);
        collisionSize.x = Mathf.Max(0.01f, collisionSize.x);
        collisionSize.y = Mathf.Max(0.01f, collisionSize.y);
        collisionThickness = Mathf.Max(0.01f, collisionThickness);

        if (groundMask.value == 0)
            groundMask = ResolveDefaultGroundMask();
    }

    private void Awake()
    {
        if (visualEffect == null)
            visualEffect = GetComponent<VisualEffect>();
    }

    private void OnEnable()
    {
        if (bindOnEnable)
            BindToGround();
    }

    public bool BindToGround()
    {
        return BindToGroundFrom(transform.position);
    }

    public bool BindToGroundFrom(Vector3 worldPosition)
    {
        if (!IsFiniteVector(worldPosition))
            return false;

        if (!TryFindGround(worldPosition, out RaycastHit hit))
            return false;

        return ApplyGround(hit.point, hit.normal);
    }

    public bool ApplyGround(Vector3 groundPoint, Vector3 groundNormal)
    {
        VisualEffect[] visualEffects = ResolveVisualEffects();
        if (visualEffects == null || visualEffects.Length == 0)
            return false;

        Vector3 safeNormal = groundNormal.sqrMagnitude > 0.0001f
            ? groundNormal.normalized
            : Vector3.up;
        Vector3 center = groundPoint + safeNormal * surfaceOffset;
        Vector3 size = new Vector3(collisionSize.x, collisionThickness, collisionSize.y);
        Vector3 angles = Quaternion.FromToRotation(Vector3.up, safeNormal).eulerAngles;

        bool anyApplied = false;
        for (int i = 0; i < visualEffects.Length; i++)
        {
            VisualEffect effect = visualEffects[i];
            if (effect == null)
                continue;

            bool applied = TrySetAnyVector3(effect, CenterParameterNames, center);
            applied |= TrySetAnyVector3(effect, NormalParameterNames, safeNormal);
            applied |= TrySetAnyVector3(effect, SizeParameterNames, size);
            applied |= TrySetAnyVector3(effect, AnglesParameterNames, angles);
            applied |= TrySetAnyFloat(effect, HeightParameterNames, center.y);

            if (!applied)
                continue;

            anyApplied = true;
            if (reinitAfterBinding)
                effect.Reinit();

            if (playAfterBinding)
                effect.Play();
        }

        if (!anyApplied && warnWhenNoParameterWasApplied)
            Debug.LogWarning($"{nameof(VisualEffectGroundCollisionBinder)} on {name} found ground but no matching exposed VFX parameter.", this);

        return anyApplied;
    }

    private bool TryFindGround(Vector3 worldPosition, out RaycastHit hit)
    {
        Vector3 origin = worldPosition + Vector3.up * raycastStartHeight;
        float maxDistance = raycastStartHeight + raycastDistance;

        if (drawDebugRay)
            Debug.DrawRay(origin, Vector3.down * maxDistance, Color.red, 2f);

        return Physics.Raycast(origin, Vector3.down, out hit, maxDistance, groundMask, triggerInteraction);
    }

    private VisualEffect[] ResolveVisualEffects()
    {
        if (includeChildVisualEffects)
            return GetComponentsInChildren<VisualEffect>(true);

        if (visualEffect == null)
            visualEffect = GetComponent<VisualEffect>();

        return visualEffect != null
            ? new[] { visualEffect }
            : System.Array.Empty<VisualEffect>();
    }

    private static bool TrySetAnyVector3(VisualEffect effect, string[] parameterNames, Vector3 value)
    {
        bool applied = false;
        for (int i = 0; i < parameterNames.Length; i++)
        {
            string parameterName = parameterNames[i];
            if (!effect.HasVector3(parameterName))
                continue;

            effect.SetVector3(parameterName, value);
            applied = true;
        }

        return applied;
    }

    private static bool TrySetAnyFloat(VisualEffect effect, string[] parameterNames, float value)
    {
        bool applied = false;
        for (int i = 0; i < parameterNames.Length; i++)
        {
            string parameterName = parameterNames[i];
            if (!effect.HasFloat(parameterName))
                continue;

            effect.SetFloat(parameterName, value);
            applied = true;
        }

        return applied;
    }

    private static LayerMask ResolveDefaultGroundMask()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        return groundLayer >= 0
            ? 1 << groundLayer
            : Physics.DefaultRaycastLayers;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return !(float.IsNaN(value.x)
            || float.IsNaN(value.y)
            || float.IsNaN(value.z)
            || float.IsInfinity(value.x)
            || float.IsInfinity(value.y)
            || float.IsInfinity(value.z));
    }
}
