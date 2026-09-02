using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Rigidbody))]
public class PlayerRagdollController : MonoBehaviour
{
    private const string ModelRootName = "Model";
    private const string ThirdPersonModelRootName = "TP_Model";
    private const string FirstPersonCameraName = "FP_Camera";
    private const string FirstPersonHandsCameraName = "Hands Camera";
    private const string ThirdPersonCameraName = "TP_Camera";
    private static readonly string[] FirstPersonVisualRootNames =
    {
        "FPS_Model",
        "Separated_UpperBody",
        "Separeted_UpperBody"
    };

    [Header("References")]
    [SerializeField] private Transform ragdollRoot;
    [SerializeField] private Animator ragdollAnimator;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerMeleeAttack meleeAttack;
    [SerializeField] private PlayerKickAttack kickAttack;
    [SerializeField] private HandEquipmentController handEquipmentController;
    [SerializeField] private PlayerPerspectiveVisibility perspectiveVisibility;
    [SerializeField] private PhotonView photonView;

    [Header("Ragdoll")]
    [SerializeField] private bool disableAnimatorWhileRagdoll = true;
    [SerializeField] private bool dropActiveItemsOnRagdoll = true;
    [SerializeField] private bool forceThirdPersonAnimatorAlwaysAnimate = true;
    [SerializeField] private bool disableNestedAnimatorsWithoutMovementController = true;
    [SerializeField] private bool disableRootCollidersWhileRagdoll = true;
    [SerializeField] private bool ignoreRagdollSelfCollision = true;
    [SerializeField] [Min(0f)] private float activationImpulse = 1.25f;
    [SerializeField] [Min(0f)] private float upwardImpulse = 0.25f;

    [Header("Ragdoll Collision")]
    [SerializeField] private bool forceRagdollCollidersSolid = true;
    [SerializeField] private bool normalizeSmallRagdollColliders = true;
    [SerializeField] [Min(0.001f)] private float minRagdollColliderWorldRadius = 0.08f;
    [SerializeField] [Min(0.001f)] private float minRagdollColliderWorldLength = 0.25f;
    [SerializeField] [Min(0.001f)] private float minUsableRagdollColliderWorldExtent = 0.08f;
    [SerializeField] [Min(1)] private int minUsableRagdollColliderCount = 3;
    [SerializeField] private bool excludeEndIkAndTargetBodiesFromRagdoll;
    [SerializeField] private bool relaxFootEndJoints = true;
    [SerializeField] [Range(0f, 90f)] private float footEndJointSwingLimit = 25f;
    [SerializeField] [Range(0f, 180f)] private float footEndJointTwistLimit = 45f;

    [Header("Debug")]
    [SerializeField] private bool enableDebugToggle = true;
    [SerializeField] private KeyCode debugToggleKey = KeyCode.F9;
    [SerializeField] private bool enableDebugThirdPersonCameraToggle = true;
    [SerializeField] private KeyCode debugThirdPersonCameraKey = KeyCode.F8;
    [SerializeField] [Min(0f)] private float debugActivationImpulse;
    [SerializeField] [Min(0f)] private float debugUpwardImpulse;
    [SerializeField] private bool logDebugToggle = true;

    [Header("Local Ragdoll Camera")]
    [SerializeField] private bool switchLocalViewToThirdPerson = true;
    [SerializeField] private Camera firstPersonCamera;
    [SerializeField] private Camera firstPersonHandsCamera;
    [SerializeField] private Camera thirdPersonCamera;
    [SerializeField] private Transform ragdollCameraTarget;
    [SerializeField] private bool detachThirdPersonCameraDuringRagdoll = true;
    [SerializeField] private Vector3 thirdPersonRagdollOffset = new Vector3(0.02f, 1.8f, -5.15f);
    [SerializeField] [Min(0f)] private float thirdPersonRagdollFollowSharpness = 14f;
    [SerializeField] [Min(0f)] private float thirdPersonRagdollLookHeight = 0.65f;
    [SerializeField] private bool allowThirdPersonRagdollCameraInput = true;
    [SerializeField] [Min(0f)] private float thirdPersonRagdollMouseSensitivity = 0.17f;
    [SerializeField] private bool invertThirdPersonRagdollMouseY;
    [SerializeField] [Range(-85f, 85f)] private float thirdPersonRagdollMinPitch = -20f;
    [SerializeField] [Range(-85f, 85f)] private float thirdPersonRagdollMaxPitch = 65f;
    [SerializeField] [Min(0.1f)] private float thirdPersonRagdollMinDistance = 1.75f;
    [SerializeField] [Min(0.1f)] private float thirdPersonRagdollMaxDistance = 7.5f;
    [SerializeField] [Min(0f)] private float thirdPersonRagdollZoomSpeed = 2.5f;
    [SerializeField] [Min(0f)] private float thirdPersonRagdollZoomSharpness = 16f;

    [Header("Ragdoll Stability")]
    [SerializeField] [Min(0f)] private float maxInheritedVelocity = 8f;
    [SerializeField] private bool stabilizeCharacterJoints = true;
    [SerializeField] [Min(0f)] private float jointProjectionDistance = 0.08f;
    [SerializeField] [Range(1f, 180f)] private float jointProjectionAngle = 20f;
    [SerializeField] private bool enableJointPreprocessing = true;
    [SerializeField] [Min(0f)] private float maxDepenetrationVelocity = 2.5f;
    [SerializeField] [Min(0f)] private float maxRagdollAngularVelocity = 8f;
    [SerializeField] [Min(1)] private int ragdollSolverIterations = 12;
    [SerializeField] [Min(1)] private int ragdollSolverVelocityIterations = 4;

    private readonly List<Rigidbody> ragdollBodies = new List<Rigidbody>();
    private readonly List<Collider> ragdollColliders = new List<Collider>();
    private readonly List<Rigidbody> excludedRagdollBodies = new List<Rigidbody>();
    private readonly List<Collider> excludedRagdollColliders = new List<Collider>();
    private readonly List<Joint> excludedRagdollJoints = new List<Joint>();
    private readonly List<Renderer> ragdollRenderers = new List<Renderer>();
    private readonly List<Animator> ragdollAnimators = new List<Animator>();
    private readonly List<bool> ragdollAnimatorInitialStates = new List<bool>();
    private readonly List<MovementAnimationController> movementAnimationControllers = new List<MovementAnimationController>();
    private readonly List<bool> movementAnimationControllerInitialStates = new List<bool>();
    private readonly List<Joint> ragdollJoints = new List<Joint>();
    private readonly List<CharacterJoint> ragdollCharacterJoints = new List<CharacterJoint>();
    private readonly List<Collider> rootColliders = new List<Collider>();
    private readonly List<bool> rootColliderInitialStates = new List<bool>();
    private readonly Dictionary<Renderer, bool> preRagdollRendererStates = new Dictionary<Renderer, bool>();
    private readonly Dictionary<Renderer, bool> preRagdollFirstPersonRendererStates = new Dictionary<Renderer, bool>();
    private readonly Dictionary<Collider, bool> preRagdollColliderTriggerStates = new Dictionary<Collider, bool>();
    private readonly Dictionary<CapsuleCollider, CapsuleColliderShape> preRagdollCapsuleColliderShapes = new Dictionary<CapsuleCollider, CapsuleColliderShape>();
    private readonly Dictionary<SphereCollider, SphereColliderShape> preRagdollSphereColliderShapes = new Dictionary<SphereCollider, SphereColliderShape>();
    private readonly Dictionary<BoxCollider, BoxColliderShape> preRagdollBoxColliderShapes = new Dictionary<BoxCollider, BoxColliderShape>();
    private readonly Dictionary<Joint, Rigidbody> preRagdollExcludedJointConnections = new Dictionary<Joint, Rigidbody>();
    private Rigidbody rootBody;
    private Rigidbody ragdollCameraTargetBody;
    private CameraActivationState firstPersonCameraState;
    private CameraActivationState firstPersonHandsCameraState;
    private CameraActivationState thirdPersonCameraState;
    private Transform thirdPersonCameraOriginalParent;
    private Vector3 thirdPersonCameraOriginalLocalPosition;
    private Quaternion thirdPersonCameraOriginalLocalRotation;
    private Vector3 thirdPersonCameraOriginalLocalScale;
    private float ragdollCameraYaw;
    private float ragdollCameraPitch;
    private float ragdollCameraDistance;
    private float ragdollCameraTargetDistance;
    private bool ragdollActive;
    private bool manualThirdPersonViewActive;
    private bool localThirdPersonViewActive;
    private bool thirdPersonCameraWasDetached;
    private bool previousPlayerMovementEnabled = true;
    private bool previousPlayerControllerEnabled = true;
    private bool previousMeleeAttackEnabled = true;
    private bool previousKickAttackEnabled = true;
    private bool hasCachedParts;
    private Coroutine animationRecoveryCoroutine;

    private struct CameraActivationState
    {
        public Camera Camera;
        public AudioListener Listener;
        public MouseLook MouseLook;
        public bool HasState;
        public bool GameObjectActiveSelf;
        public bool CameraEnabled;
        public bool ListenerEnabled;
        public bool MouseLookEnabled;
    }

    private struct CapsuleColliderShape
    {
        public float Radius;
        public float Height;
        public int Direction;
        public Vector3 Center;
    }

    private struct SphereColliderShape
    {
        public float Radius;
        public Vector3 Center;
    }

    private struct BoxColliderShape
    {
        public Vector3 Size;
        public Vector3 Center;
    }

    public bool IsRagdollActive => ragdollActive;

    private void Awake()
    {
        CacheReferences();
        CacheRagdollParts();
        SetAnimatedState(restoreControlledComponents: false);
    }

    private void Update()
    {
        HandleDebugThirdPersonCameraToggleInput();
        HandleDebugRagdollToggleInput();
    }

    private void HandleDebugRagdollToggleInput()
    {
        if (!enableDebugToggle || debugToggleKey == KeyCode.None || !Input.GetKeyDown(debugToggleKey))
            return;

        if (!HasAuthority())
            return;

        Vector3 hitPoint = transform.position + Vector3.up;
        Vector3 hitDirection = -transform.forward;
        if (playerHealth != null)
        {
            playerHealth.RequestDebugRagdollToggle(hitPoint, hitDirection, debugActivationImpulse, debugUpwardImpulse);
            return;
        }

        if (ragdollActive)
            SetAnimatedState();
        else
            ActivateRagdoll(hitPoint, hitDirection, debugActivationImpulse, debugUpwardImpulse);
    }

    private void HandleDebugThirdPersonCameraToggleInput()
    {
        if (!enableDebugThirdPersonCameraToggle
            || debugThirdPersonCameraKey == KeyCode.None
            || !Input.GetKeyDown(debugThirdPersonCameraKey))
        {
            return;
        }

        if (!HasAuthority())
            return;

        manualThirdPersonViewActive = !manualThirdPersonViewActive;
        RefreshLocalThirdPersonView();
    }

    private void LateUpdate()
    {
        if (!localThirdPersonViewActive)
            return;

        UpdateLocalRagdollCameraInput();
        UpdateLocalRagdollCamera(immediate: false);
    }

    public void ActivateRagdoll(DamageInfo damageInfo)
    {
        ActivateRagdoll(
            damageInfo.HitPoint,
            damageInfo.HitDirection,
            activationImpulse,
            upwardImpulse);
    }

    public void ActivateRagdoll(Vector3 hitPoint, Vector3 hitDirection, float impulse, float upward)
    {
        CacheReferences();
        bool wasRagdollActive = ragdollActive;
        CacheRagdollParts(forceRefresh: !wasRagdollActive);

        if (ragdollBodies.Count == 0)
        {
            Debug.LogWarning("PlayerRagdollController: nenhum Rigidbody de ragdoll foi encontrado nos filhos do Player.", gameObject);
            return;
        }

        Vector3 inheritedVelocity = ResolveInheritedVelocity();

        if (!wasRagdollActive)
        {
            StoreControlledComponentStates();
            DropActiveItemsForRagdoll();
        }

        if (animationRecoveryCoroutine != null)
        {
            StopCoroutine(animationRecoveryCoroutine);
            animationRecoveryCoroutine = null;
        }

        ragdollActive = true;
        SetControlledComponentsEnabled(false);
        SetMovementAnimationControllersEnabled(false);

        if (rootBody != null)
        {
            ClearVelocityIfDynamic(rootBody);
            rootBody.isKinematic = true;
            rootBody.useGravity = false;
            rootBody.detectCollisions = false;
        }

        PrepareRagdollCollidersForSimulation();
        WarnIfRagdollCollisionLooksUnusable();
        SetRootCollidersEnabled(false);
        Physics.SyncTransforms();
        ForceRagdollRenderersVisible();
        RefreshLocalThirdPersonView();

        SetRagdollAnimatorsEnabled(false);
        ApplyRagdollJointStabilityDefaults();

        for (int i = 0; i < ragdollColliders.Count; i++)
        {
            Collider ragdollCollider = ragdollColliders[i];
            if (ragdollCollider != null)
                ragdollCollider.enabled = true;
        }

        for (int i = 0; i < ragdollBodies.Count; i++)
        {
            Rigidbody body = ragdollBodies[i];
            if (body == null)
                continue;

            body.position = body.transform.position;
            body.rotation = body.transform.rotation;
            ConfigureRagdollBodyForSimulation(body);
            body.isKinematic = false;
            body.useGravity = true;
            body.detectCollisions = true;
            body.linearVelocity = inheritedVelocity;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }

        if (logDebugToggle)
            LogRagdollSetup("activated");

        ApplyActivationImpulse(hitPoint, hitDirection, impulse, upward);
    }

    public void SetAnimatedState()
    {
        SetAnimatedState(restoreControlledComponents: true);
    }

    public void SetAnimatedState(bool restoreControlledComponents)
    {
        CacheReferences();
        CacheRagdollParts();

        bool wasRagdollActive = ragdollActive;
        ragdollActive = false;

        for (int i = 0; i < ragdollBodies.Count; i++)
        {
            Rigidbody body = ragdollBodies[i];
            if (body == null)
                continue;

            ClearVelocityIfDynamic(body);
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = false;
            body.Sleep();
        }

        for (int i = 0; i < ragdollColliders.Count; i++)
        {
            Collider ragdollCollider = ragdollColliders[i];
            if (ragdollCollider != null)
                ragdollCollider.enabled = false;
        }

        RestoreRagdollColliderSimulationState();
        RestoreRagdollAnimators();

        SetRootCollidersEnabled(true);
        RestoreRootBodyForOwnership();

        if (wasRagdollActive)
        {
            RefreshLocalThirdPersonView();
            if (!localThirdPersonViewActive)
                RestoreRagdollRendererVisibility();
        }

        if (restoreControlledComponents)
            RestoreControlledComponentStates();

        if (wasRagdollActive)
            RestoreAnimationControlAfterRagdoll();
    }

    private void CacheReferences()
    {
        if (rootBody == null)
            rootBody = GetComponent<Rigidbody>();

        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (playerHealth == null)
            playerHealth = GetComponent<PlayerHealth>();

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (playerController == null)
            playerController = GetComponent<PlayerController>();

        if (meleeAttack == null)
            meleeAttack = GetComponent<PlayerMeleeAttack>();

        if (kickAttack == null)
            kickAttack = GetComponent<PlayerKickAttack>();

        if (handEquipmentController == null)
            handEquipmentController = GetComponent<HandEquipmentController>();

        if (perspectiveVisibility == null)
            perspectiveVisibility = GetComponent<PlayerPerspectiveVisibility>();

        if (ragdollAnimator == null)
            ragdollAnimator = ResolveRagdollAnimator();

        if (ragdollRoot == null)
            ragdollRoot = ResolveRagdollSearchRoot();

        if (firstPersonCamera == null)
            firstPersonCamera = FindCameraByName(FirstPersonCameraName);

        if (firstPersonHandsCamera == null)
            firstPersonHandsCamera = FindCameraByName(FirstPersonHandsCameraName);

        if (thirdPersonCamera == null)
            thirdPersonCamera = FindCameraByName(ThirdPersonCameraName);
    }

    private void DropActiveItemsForRagdoll()
    {
        if (!dropActiveItemsOnRagdoll || !HasAuthority())
            return;

        if (handEquipmentController == null)
            handEquipmentController = GetComponent<HandEquipmentController>();

        if (handEquipmentController != null)
            handEquipmentController.DropActiveEquippedItemsOnRagdoll();
    }

    private void CacheRagdollParts(bool forceRefresh = false)
    {
        if (hasCachedParts && !forceRefresh)
            return;

        Dictionary<Collider, bool> preservedRootColliderStates = null;
        Dictionary<Animator, bool> preservedAnimatorStates = null;
        Dictionary<MovementAnimationController, bool> preservedMovementAnimationControllerStates = null;
        if (ragdollActive)
        {
            preservedRootColliderStates = new Dictionary<Collider, bool>();
            for (int i = 0; i < rootColliders.Count; i++)
            {
                Collider rootCollider = rootColliders[i];
                if (rootCollider != null && i < rootColliderInitialStates.Count)
                    preservedRootColliderStates[rootCollider] = rootColliderInitialStates[i];
            }

            preservedAnimatorStates = new Dictionary<Animator, bool>();
            for (int i = 0; i < ragdollAnimators.Count; i++)
            {
                Animator animator = ragdollAnimators[i];
                if (animator != null && i < ragdollAnimatorInitialStates.Count)
                    preservedAnimatorStates[animator] = ragdollAnimatorInitialStates[i];
            }

            preservedMovementAnimationControllerStates = new Dictionary<MovementAnimationController, bool>();
            for (int i = 0; i < movementAnimationControllers.Count; i++)
            {
                MovementAnimationController movementAnimationController = movementAnimationControllers[i];
                if (movementAnimationController != null && i < movementAnimationControllerInitialStates.Count)
                    preservedMovementAnimationControllerStates[movementAnimationController] = movementAnimationControllerInitialStates[i];
            }
        }

        hasCachedParts = true;
        ragdollBodies.Clear();
        ragdollColliders.Clear();
        excludedRagdollBodies.Clear();
        excludedRagdollColliders.Clear();
        excludedRagdollJoints.Clear();
        ragdollRenderers.Clear();
        ragdollAnimators.Clear();
        ragdollAnimatorInitialStates.Clear();
        movementAnimationControllers.Clear();
        movementAnimationControllerInitialStates.Clear();
        ragdollJoints.Clear();
        ragdollCharacterJoints.Clear();
        rootColliders.Clear();
        rootColliderInitialStates.Clear();

        if (rootBody == null)
            rootBody = GetComponent<Rigidbody>();

        Collider[] directRootColliders = GetComponents<Collider>();
        for (int i = 0; i < directRootColliders.Length; i++)
        {
            Collider rootCollider = directRootColliders[i];
            if (rootCollider == null)
                continue;

            rootColliders.Add(rootCollider);
            rootColliderInitialStates.Add(preservedRootColliderStates != null
                && preservedRootColliderStates.TryGetValue(rootCollider, out bool preservedRootColliderState)
                    ? preservedRootColliderState
                    : rootCollider.enabled);
        }

        Transform searchRoot = ResolveRagdollSearchRoot();
        if (searchRoot == null)
            searchRoot = transform;

        ragdollRoot = searchRoot;

        Joint[] joints = searchRoot.GetComponentsInChildren<Joint>(true);
        for (int i = 0; i < joints.Length; i++)
        {
            Joint joint = joints[i];
            if (joint == null)
                continue;

            ragdollJoints.Add(joint);

            if (joint is CharacterJoint characterJoint)
                ragdollCharacterJoints.Add(characterJoint);
        }

        Rigidbody[] childBodies = searchRoot.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < childBodies.Length; i++)
        {
            Rigidbody childBody = childBodies[i];
            if (childBody == null || childBody == rootBody)
                continue;

            AddRagdollBody(childBody);
        }

        HashSet<Collider> rootColliderSet = new HashSet<Collider>(rootColliders);
        HashSet<Collider> colliderSet = new HashSet<Collider>();
        Collider[] childColliders = searchRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < childColliders.Length; i++)
        {
            Collider childCollider = childColliders[i];
            if (childCollider == null || rootColliderSet.Contains(childCollider))
                continue;

            if (colliderSet.Add(childCollider))
                ragdollColliders.Add(childCollider);
        }

        Transform renderRoot = ragdollRoot != null ? ragdollRoot : searchRoot;
        Renderer[] renderers = renderRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer ragdollRenderer = renderers[i];
            if (ragdollRenderer != null && !ragdollRenderers.Contains(ragdollRenderer))
                ragdollRenderers.Add(ragdollRenderer);
        }

        Animator[] animators = renderRoot.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null || ragdollAnimators.Contains(animator))
                continue;

            ragdollAnimators.Add(animator);
            ragdollAnimatorInitialStates.Add(preservedAnimatorStates != null
                && preservedAnimatorStates.TryGetValue(animator, out bool preservedAnimatorState)
                    ? preservedAnimatorState
                    : animator.enabled);
        }

        if (ragdollAnimator != null && !ragdollAnimators.Contains(ragdollAnimator))
        {
            ragdollAnimators.Add(ragdollAnimator);
            ragdollAnimatorInitialStates.Add(preservedAnimatorStates != null
                && preservedAnimatorStates.TryGetValue(ragdollAnimator, out bool preservedAnimatorState)
                    ? preservedAnimatorState
                    : ragdollAnimator.enabled);
        }

        MovementAnimationController[] movementControllers = renderRoot.GetComponentsInChildren<MovementAnimationController>(true);
        for (int i = 0; i < movementControllers.Length; i++)
        {
            MovementAnimationController movementAnimationController = movementControllers[i];
            if (movementAnimationController == null || movementAnimationControllers.Contains(movementAnimationController))
                continue;

            movementAnimationControllers.Add(movementAnimationController);
            movementAnimationControllerInitialStates.Add(preservedMovementAnimationControllerStates != null
                && preservedMovementAnimationControllerStates.TryGetValue(movementAnimationController, out bool preservedControllerState)
                    ? preservedControllerState
                    : movementAnimationController.enabled);
        }

        ApplyRagdollCollisionIgnores();
    }

    private void AddRagdollBody(Rigidbody body)
    {
        if (body == null || ragdollBodies.Contains(body))
            return;

        ragdollBodies.Add(body);
    }

    private bool IsRagdollBodyCandidate(Rigidbody body, Transform searchRoot)
    {
        return IsRagdollDescendantBody(body, searchRoot)
            && !ShouldExcludeRagdollBody(body);
    }

    private bool IsExcludedRagdollBodyCandidate(Rigidbody body, Transform searchRoot)
    {
        return IsRagdollDescendantBody(body, searchRoot)
            && ShouldExcludeRagdollBody(body);
    }

    private bool IsRagdollDescendantBody(Rigidbody body, Transform searchRoot)
    {
        return body != null
            && body != rootBody
            && searchRoot != null
            && body.transform.IsChildOf(searchRoot);
    }

    private void AddExcludedRagdollBody(Rigidbody body)
    {
        if (body == null || excludedRagdollBodies.Contains(body))
            return;

        excludedRagdollBodies.Add(body);
    }

    private void AddExcludedRagdollJoint(Joint joint)
    {
        if (joint == null || excludedRagdollJoints.Contains(joint))
            return;

        excludedRagdollJoints.Add(joint);
    }

    private void CollectAttachedColliders(Rigidbody body, List<Collider> destination, HashSet<Collider> colliderSet)
    {
        if (body == null || destination == null || colliderSet == null)
            return;

        Collider[] bodyColliders = body.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < bodyColliders.Length; i++)
        {
            Collider bodyCollider = bodyColliders[i];
            if (bodyCollider == null || bodyCollider.attachedRigidbody != body)
                continue;

            if (colliderSet.Add(bodyCollider))
                destination.Add(bodyCollider);
        }
    }

    private bool ShouldExcludeRagdollBody(Rigidbody body)
    {
        return false;
    }

    private bool IsExcludedRagdollTransform(Transform candidate)
    {
        Transform cursor = candidate;
        while (cursor != null)
        {
            string objectName = cursor.name;
            if (objectName.EndsWith("_end", StringComparison.OrdinalIgnoreCase)
                || ContainsIgnoreCase(objectName, "leg_ik")
                || ContainsIgnoreCase(objectName, "leg target")
                || ContainsIgnoreCase(objectName, "leg_target")
                || ContainsIgnoreCase(objectName, "target"))
            {
                return true;
            }

            if (cursor == ragdollRoot || cursor == transform)
                break;

            cursor = cursor.parent;
        }

        return false;
    }

    private Animator ResolveRagdollAnimator()
    {
        ThirdPersonModel thirdPersonModel = GetComponentInChildren<ThirdPersonModel>(true);
        if (thirdPersonModel != null)
        {
            Animator thirdPersonAnimator = thirdPersonModel.GetComponentInChildren<Animator>(true);
            if (thirdPersonAnimator != null)
                return thirdPersonAnimator;
        }

        Transform thirdPersonModelRoot = FindDescendantByName(transform, ThirdPersonModelRootName);
        if (thirdPersonModelRoot != null)
        {
            Animator thirdPersonAnimator = FindAnimatorWithRagdollParts(thirdPersonModelRoot)
                ?? thirdPersonModelRoot.GetComponentInChildren<Animator>(true);
            if (thirdPersonAnimator != null)
                return thirdPersonAnimator;
        }

        Transform modelRoot = FindDescendantByName(transform, ModelRootName);
        if (modelRoot != null)
        {
            Animator modelAnimator = FindAnimatorWithRagdollParts(modelRoot);
            if (modelAnimator != null)
                return modelAnimator;
        }

        return FindAnimatorWithRagdollParts(transform) ?? GetComponentInChildren<Animator>(true);
    }

    private Transform ResolveRagdollSearchRoot()
    {
        ThirdPersonModel thirdPersonModel = GetComponentInChildren<ThirdPersonModel>(true);
        if (thirdPersonModel != null)
            return thirdPersonModel.transform;

        Transform thirdPersonModelRoot = FindDescendantByName(transform, ThirdPersonModelRootName);
        if (thirdPersonModelRoot != null)
            return thirdPersonModelRoot;

        if (ragdollAnimator != null)
            return ragdollAnimator.transform;

        Transform modelRoot = FindDescendantByName(transform, ModelRootName);
        if (modelRoot != null)
            return modelRoot;

        return transform;
    }

    private Animator FindAnimatorWithRagdollParts(Transform root)
    {
        if (root == null)
            return null;

        Animator[] animators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator candidate = animators[i];
            if (candidate != null && candidate.GetComponentInChildren<Joint>(true) != null)
                return candidate;
        }

        return null;
    }

    private Camera FindCameraByName(string cameraName)
    {
        if (string.IsNullOrWhiteSpace(cameraName))
            return null;

        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate != null && string.Equals(candidate.gameObject.name, cameraName, System.StringComparison.Ordinal))
                return candidate;
        }

        return null;
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < descendants.Length; i++)
        {
            Transform descendant = descendants[i];
            if (descendant != null && string.Equals(descendant.name, targetName, System.StringComparison.Ordinal))
                return descendant;
        }

        return null;
    }

    private void StoreControlledComponentStates()
    {
        previousPlayerMovementEnabled = playerMovement == null || playerMovement.enabled;
        previousPlayerControllerEnabled = playerController == null || playerController.enabled;
        previousMeleeAttackEnabled = meleeAttack == null || meleeAttack.enabled;
        previousKickAttackEnabled = kickAttack == null || kickAttack.enabled;
    }

    private void SetControlledComponentsEnabled(bool enabled)
    {
        if (playerController != null)
            playerController.enabled = enabled;

        if (playerMovement != null)
            playerMovement.enabled = enabled;

        if (meleeAttack != null)
            meleeAttack.enabled = enabled;

        if (kickAttack != null)
            kickAttack.enabled = enabled;
    }

    private void RestoreControlledComponentStates()
    {
        if (playerMovement != null)
            playerMovement.enabled = previousPlayerMovementEnabled;

        if (playerController != null)
            playerController.enabled = previousPlayerControllerEnabled;

        if (meleeAttack != null)
            meleeAttack.enabled = previousMeleeAttackEnabled;

        if (kickAttack != null)
            kickAttack.enabled = previousKickAttackEnabled;
    }

    private void SetMovementAnimationControllersEnabled(bool enabled)
    {
        for (int i = 0; i < movementAnimationControllers.Count; i++)
        {
            MovementAnimationController movementAnimationController = movementAnimationControllers[i];
            if (movementAnimationController != null)
                movementAnimationController.enabled = enabled;
        }
    }

    private void RestoreMovementAnimationControllers()
    {
        for (int i = 0; i < movementAnimationControllers.Count; i++)
        {
            MovementAnimationController movementAnimationController = movementAnimationControllers[i];
            if (movementAnimationController == null)
                continue;

            movementAnimationController.enabled = i < movementAnimationControllerInitialStates.Count
                ? movementAnimationControllerInitialStates[i]
                : true;
        }
    }

    private void SetRootCollidersEnabled(bool enabled)
    {
        if (!disableRootCollidersWhileRagdoll)
            return;

        for (int i = 0; i < rootColliders.Count; i++)
        {
            Collider rootCollider = rootColliders[i];
            if (rootCollider == null)
                continue;

            rootCollider.enabled = enabled
                ? i < rootColliderInitialStates.Count && rootColliderInitialStates[i]
                : false;
        }
    }

    private void ForceRagdollRenderersVisible()
    {
        for (int i = 0; i < ragdollRenderers.Count; i++)
        {
            Renderer ragdollRenderer = ragdollRenderers[i];
            if (ragdollRenderer == null)
                continue;

            if (!preRagdollRendererStates.ContainsKey(ragdollRenderer))
                preRagdollRendererStates[ragdollRenderer] = ragdollRenderer.enabled;

            ragdollRenderer.enabled = true;
        }
    }

    private void RestoreRagdollRendererVisibility()
    {
        if (perspectiveVisibility != null)
        {
            perspectiveVisibility.ApplyForCurrentOwner();
            preRagdollRendererStates.Clear();
            return;
        }

        foreach (KeyValuePair<Renderer, bool> entry in preRagdollRendererStates)
        {
            if (entry.Key != null)
                entry.Key.enabled = entry.Value;
        }

        preRagdollRendererStates.Clear();
    }

    private void RefreshLocalThirdPersonView()
    {
        bool shouldUseThirdPersonView = switchLocalViewToThirdPerson
            && HasAuthority()
            && (ragdollActive || manualThirdPersonViewActive);

        if (shouldUseThirdPersonView)
        {
            EnableLocalThirdPersonView();
            return;
        }

        DisableLocalThirdPersonView();
    }

    private void EnableLocalThirdPersonView()
    {
        if (localThirdPersonViewActive || !switchLocalViewToThirdPerson || !HasAuthority())
            return;

        CacheReferences();
        CacheRagdollParts();
        if (thirdPersonCamera == null)
            return;

        firstPersonCameraState = CaptureCameraState(firstPersonCamera);
        firstPersonHandsCameraState = CaptureCameraState(firstPersonHandsCamera);
        thirdPersonCameraState = CaptureCameraState(thirdPersonCamera);
        CaptureThirdPersonCameraTransform();

        ragdollCameraTargetBody = ResolveRagdollCameraTargetBody();
        InitializeRagdollCameraOrbit();
        localThirdPersonViewActive = true;

        if (detachThirdPersonCameraDuringRagdoll && thirdPersonCamera.transform.parent != null)
        {
            thirdPersonCamera.transform.SetParent(null, true);
            thirdPersonCameraWasDetached = true;
        }

        SetFirstPersonCameraActive(false);
        SetCameraActive(thirdPersonCameraState, true, listenerEnabled: true, mouseLookEnabled: false);
        ForceRagdollRenderersVisible();
        HideFirstPersonRagdollVisuals();
        UpdateLocalRagdollCamera(immediate: true);
    }

    private void DisableLocalThirdPersonView()
    {
        if (!localThirdPersonViewActive)
            return;

        localThirdPersonViewActive = false;
        RestoreFirstPersonRagdollVisuals();
        RestoreThirdPersonCameraTransform();
        RestoreCameraState(thirdPersonCameraState);
        RestoreCameraState(firstPersonCameraState);
        RestoreCameraState(firstPersonHandsCameraState);
        ragdollCameraTargetBody = null;
        RestoreRagdollRendererVisibility();
    }

    private CameraActivationState CaptureCameraState(Camera targetCamera)
    {
        CameraActivationState state = default;
        state.Camera = targetCamera;
        state.HasState = targetCamera != null;

        if (targetCamera == null)
            return state;

        GameObject cameraObject = targetCamera.gameObject;
        state.GameObjectActiveSelf = cameraObject.activeSelf;
        state.CameraEnabled = targetCamera.enabled;
        state.Listener = targetCamera.GetComponent<AudioListener>();
        state.ListenerEnabled = state.Listener != null && state.Listener.enabled;
        state.MouseLook = targetCamera.GetComponent<MouseLook>();
        state.MouseLookEnabled = state.MouseLook != null && state.MouseLook.enabled;
        return state;
    }

    private void SetFirstPersonCameraActive(bool active)
    {
        SetCameraActive(firstPersonHandsCameraState, active, listenerEnabled: false, mouseLookEnabled: active);
        SetCameraActive(firstPersonCameraState, active, listenerEnabled: false, mouseLookEnabled: active);
    }

    private void SetCameraActive(CameraActivationState state, bool active, bool listenerEnabled, bool mouseLookEnabled)
    {
        if (!state.HasState || state.Camera == null)
            return;

        GameObject cameraObject = state.Camera.gameObject;
        cameraObject.SetActive(active);
        state.Camera.enabled = active;

        if (state.Listener != null)
            state.Listener.enabled = active && listenerEnabled;

        if (state.MouseLook != null)
            state.MouseLook.enabled = active && mouseLookEnabled;
    }

    private void RestoreCameraState(CameraActivationState state)
    {
        if (!state.HasState || state.Camera == null)
            return;

        GameObject cameraObject = state.Camera.gameObject;
        cameraObject.SetActive(state.GameObjectActiveSelf);
        state.Camera.enabled = state.CameraEnabled;

        if (state.Listener != null)
            state.Listener.enabled = state.ListenerEnabled;

        if (state.MouseLook != null)
            state.MouseLook.enabled = state.MouseLookEnabled;
    }

    private void CaptureThirdPersonCameraTransform()
    {
        if (thirdPersonCamera == null)
            return;

        Transform cameraTransform = thirdPersonCamera.transform;
        thirdPersonCameraOriginalParent = cameraTransform.parent;
        thirdPersonCameraOriginalLocalPosition = cameraTransform.localPosition;
        thirdPersonCameraOriginalLocalRotation = cameraTransform.localRotation;
        thirdPersonCameraOriginalLocalScale = cameraTransform.localScale;
        thirdPersonCameraWasDetached = false;
    }

    private void RestoreThirdPersonCameraTransform()
    {
        if (thirdPersonCamera == null)
            return;

        Transform cameraTransform = thirdPersonCamera.transform;
        if (thirdPersonCameraWasDetached)
            cameraTransform.SetParent(thirdPersonCameraOriginalParent, false);

        cameraTransform.localPosition = thirdPersonCameraOriginalLocalPosition;
        cameraTransform.localRotation = thirdPersonCameraOriginalLocalRotation;
        cameraTransform.localScale = thirdPersonCameraOriginalLocalScale;
        thirdPersonCameraWasDetached = false;
    }

    private void UpdateLocalRagdollCamera(bool immediate)
    {
        if (thirdPersonCamera == null)
            return;

        Vector3 focus = ResolveRagdollCameraFocus();
        Vector3 lookTarget = focus + Vector3.up * Mathf.Max(0f, thirdPersonRagdollLookHeight);
        float currentDistance = ResolveCurrentRagdollCameraDistance(immediate);
        Vector3 desiredPosition = lookTarget + ResolveRagdollCameraOrbitDirection() * currentDistance;
        Transform cameraTransform = thirdPersonCamera.transform;

        if (immediate || thirdPersonRagdollFollowSharpness <= 0f)
        {
            cameraTransform.position = desiredPosition;
        }
        else
        {
            float followT = 1f - Mathf.Exp(-thirdPersonRagdollFollowSharpness * Time.deltaTime);
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, desiredPosition, followT);
        }

        Vector3 lookDirection = lookTarget - cameraTransform.position;
        if (lookDirection.sqrMagnitude <= 0.0001f)
            lookDirection = transform.forward;

        Quaternion desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        if (immediate || thirdPersonRagdollFollowSharpness <= 0f)
            cameraTransform.rotation = desiredRotation;
        else
        {
            float rotationT = 1f - Mathf.Exp(-thirdPersonRagdollFollowSharpness * Time.deltaTime);
            cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, desiredRotation, rotationT);
        }
    }

    private void UpdateLocalRagdollCameraInput()
    {
        if (!allowThirdPersonRagdollCameraInput || !HasAuthority() || Mouse.current == null)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        if (mouseDelta.sqrMagnitude > 0.0001f && thirdPersonRagdollMouseSensitivity > 0f)
        {
            ragdollCameraYaw += mouseDelta.x * thirdPersonRagdollMouseSensitivity;
            float pitchDirection = invertThirdPersonRagdollMouseY ? -1f : 1f;
            ragdollCameraPitch = ClampRagdollCameraPitch(
                ragdollCameraPitch + mouseDelta.y * thirdPersonRagdollMouseSensitivity * pitchDirection);
        }

        float scrollDelta = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scrollDelta) > 0.001f && thirdPersonRagdollZoomSpeed > 0f)
        {
            float scrollSteps = scrollDelta / 120f;
            ragdollCameraTargetDistance = ClampRagdollCameraDistance(
                ragdollCameraTargetDistance - scrollSteps * thirdPersonRagdollZoomSpeed);
        }
    }

    private void InitializeRagdollCameraOrbit()
    {
        Vector3 initialOffset = ResolveThirdPersonRagdollOffset();
        float horizontalDistance = new Vector2(initialOffset.x, initialOffset.z).magnitude;
        float verticalOffset = initialOffset.y - Mathf.Max(0f, thirdPersonRagdollLookHeight);
        float derivedDistance = Mathf.Max(horizontalDistance, initialOffset.magnitude);

        ragdollCameraYaw = transform.eulerAngles.y;
        ragdollCameraPitch = horizontalDistance > 0.0001f
            ? Mathf.Atan2(verticalOffset, horizontalDistance) * Mathf.Rad2Deg
            : 12f;
        ragdollCameraPitch = ClampRagdollCameraPitch(ragdollCameraPitch);
        ragdollCameraDistance = ClampRagdollCameraDistance(derivedDistance);
        ragdollCameraTargetDistance = ragdollCameraDistance;
    }

    private float ResolveCurrentRagdollCameraDistance(bool immediate)
    {
        ragdollCameraTargetDistance = ClampRagdollCameraDistance(ragdollCameraTargetDistance);

        if (immediate || thirdPersonRagdollZoomSharpness <= 0f)
        {
            ragdollCameraDistance = ragdollCameraTargetDistance;
            return ragdollCameraDistance;
        }

        float zoomT = 1f - Mathf.Exp(-thirdPersonRagdollZoomSharpness * Time.deltaTime);
        ragdollCameraDistance = Mathf.Lerp(ragdollCameraDistance, ragdollCameraTargetDistance, zoomT);
        return ragdollCameraDistance;
    }

    private Vector3 ResolveRagdollCameraOrbitDirection()
    {
        float yawRadians = ragdollCameraYaw * Mathf.Deg2Rad;
        float pitchRadians = ragdollCameraPitch * Mathf.Deg2Rad;
        float pitchCos = Mathf.Cos(pitchRadians);

        Vector3 direction = new Vector3(
            -Mathf.Sin(yawRadians) * pitchCos,
            Mathf.Sin(pitchRadians),
            -Mathf.Cos(yawRadians) * pitchCos);

        if (direction.sqrMagnitude <= 0.0001f)
            return -transform.forward;

        return direction.normalized;
    }

    private float ClampRagdollCameraPitch(float pitch)
    {
        float minPitch = Mathf.Min(thirdPersonRagdollMinPitch, thirdPersonRagdollMaxPitch);
        float maxPitch = Mathf.Max(thirdPersonRagdollMinPitch, thirdPersonRagdollMaxPitch);
        return Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private float ClampRagdollCameraDistance(float distance)
    {
        float minDistance = Mathf.Max(0.1f, Mathf.Min(thirdPersonRagdollMinDistance, thirdPersonRagdollMaxDistance));
        float maxDistance = Mathf.Max(minDistance, Mathf.Max(thirdPersonRagdollMinDistance, thirdPersonRagdollMaxDistance));
        return Mathf.Clamp(distance, minDistance, maxDistance);
    }

    private Vector3 ResolveRagdollCameraFocus()
    {
        if (ragdollCameraTarget != null)
            return ragdollCameraTarget.position;

        if (ragdollCameraTargetBody != null)
            return ragdollCameraTargetBody.worldCenterOfMass;

        if (ragdollBodies.Count > 0)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < ragdollBodies.Count; i++)
            {
                Rigidbody body = ragdollBodies[i];
                if (body == null)
                    continue;

                sum += body.worldCenterOfMass;
                count++;
            }

            if (count > 0)
                return sum / count;
        }

        return transform.position + Vector3.up;
    }

    private Rigidbody ResolveRagdollCameraTargetBody()
    {
        if (ragdollCameraTarget != null)
        {
            Rigidbody targetBody = ragdollCameraTarget.GetComponentInParent<Rigidbody>();
            if (IsRagdollBodyCandidate(targetBody, ragdollRoot != null ? ragdollRoot : transform))
                return targetBody;
        }

        Rigidbody preferredBody = FindRagdollBodyByName("hip")
            ?? FindRagdollBodyByName("pelvis")
            ?? FindRagdollBodyByName("spine")
            ?? FindRagdollBodyByName("chest");

        if (preferredBody != null)
            return preferredBody;

        return ragdollBodies.Count > 0 ? ragdollBodies[0] : null;
    }

    private Rigidbody FindRagdollBodyByName(string bodyNamePart)
    {
        for (int i = 0; i < ragdollBodies.Count; i++)
        {
            Rigidbody body = ragdollBodies[i];
            if (body == null)
                continue;

            if (body.gameObject.name.IndexOf(bodyNamePart, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return body;
        }

        return null;
    }

    private Vector3 ResolveThirdPersonRagdollOffset()
    {
        if (thirdPersonCamera != null && thirdPersonCamera.transform.localPosition.sqrMagnitude > 0.0001f)
            return thirdPersonCamera.transform.localPosition;

        return thirdPersonRagdollOffset;
    }

    private void HideFirstPersonRagdollVisuals()
    {
        preRagdollFirstPersonRendererStates.Clear();
        for (int i = 0; i < FirstPersonVisualRootNames.Length; i++)
        {
            Transform visualRoot = FindDescendantByName(transform, FirstPersonVisualRootNames[i]);
            if (visualRoot == null)
                continue;

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer targetRenderer = renderers[rendererIndex];
                if (targetRenderer == null)
                    continue;

                if (!preRagdollFirstPersonRendererStates.ContainsKey(targetRenderer))
                    preRagdollFirstPersonRendererStates.Add(targetRenderer, targetRenderer.enabled);

                targetRenderer.enabled = false;
            }
        }
    }

    private void RestoreFirstPersonRagdollVisuals()
    {
        foreach (KeyValuePair<Renderer, bool> entry in preRagdollFirstPersonRendererStates)
        {
            if (entry.Key != null)
                entry.Key.enabled = entry.Value;
        }

        preRagdollFirstPersonRendererStates.Clear();
    }

    private void SetRagdollAnimatorsEnabled(bool enabled)
    {
        if (!disableAnimatorWhileRagdoll)
            return;

        for (int i = 0; i < ragdollAnimators.Count; i++)
        {
            Animator animator = ragdollAnimators[i];
            if (animator != null)
                animator.enabled = enabled;
        }
    }

    private void RestoreRagdollAnimators()
    {
        if (!disableAnimatorWhileRagdoll)
            return;

        for (int i = 0; i < ragdollAnimators.Count; i++)
        {
            Animator animator = ragdollAnimators[i];
            if (animator == null)
                continue;

            bool shouldRestore = i < ragdollAnimatorInitialStates.Count
                ? ragdollAnimatorInitialStates[i]
                : true;

            if (disableNestedAnimatorsWithoutMovementController && !IsAnimatorDrivenByMovementController(animator))
                shouldRestore = false;

            animator.enabled = shouldRestore;
            if (shouldRestore && forceThirdPersonAnimatorAlwaysAnimate)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    private void RestoreAnimationControlAfterRagdoll()
    {
        if (animationRecoveryCoroutine != null)
            StopCoroutine(animationRecoveryCoroutine);

        RestoreAnimationControlAfterRagdollPass(rebind: true);
        animationRecoveryCoroutine = StartCoroutine(CompleteAnimationControlAfterRagdoll());
    }

    private IEnumerator CompleteAnimationControlAfterRagdoll()
    {
        yield return new WaitForFixedUpdate();
        RestoreAnimationControlAfterRagdollPass(rebind: false);
        yield return null;
        RestoreAnimationControlAfterRagdollPass(rebind: false);
        animationRecoveryCoroutine = null;
    }

    private void RestoreAnimationControlAfterRagdollPass(bool rebind)
    {
        for (int i = 0; i < ragdollAnimators.Count; i++)
        {
            Animator animator = ragdollAnimators[i];
            if (disableNestedAnimatorsWithoutMovementController && !IsAnimatorDrivenByMovementController(animator))
            {
                if (animator != null)
                    animator.enabled = false;

                continue;
            }

            RestoreAnimatorControlAfterRagdoll(animator, rebind);
        }

        if (ragdollAnimator != null && !ragdollAnimators.Contains(ragdollAnimator))
            RestoreAnimatorControlAfterRagdoll(ragdollAnimator, rebind);

        RestoreMovementAnimationControllers();
        for (int i = 0; i < movementAnimationControllers.Count; i++)
        {
            MovementAnimationController movementAnimationController = movementAnimationControllers[i];
            if (movementAnimationController != null && movementAnimationController.enabled)
                movementAnimationController.ResetAfterRagdoll();
        }

        for (int i = 0; i < ragdollAnimators.Count; i++)
        {
            Animator animator = ragdollAnimators[i];
            if (animator != null && animator.enabled)
                animator.Update(0f);
        }

        Physics.SyncTransforms();
    }

    private void RestoreAnimatorControlAfterRagdoll(Animator animator, bool rebind)
    {
        if (animator == null || !animator.gameObject.activeInHierarchy)
            return;

        animator.enabled = true;
        if (forceThirdPersonAnimatorAlwaysAnimate)
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (rebind)
            animator.Rebind();

        animator.Update(0f);
    }

    private bool IsAnimatorDrivenByMovementController(Animator animator)
    {
        if (animator == null)
            return false;

        for (int i = 0; i < movementAnimationControllers.Count; i++)
        {
            MovementAnimationController movementAnimationController = movementAnimationControllers[i];
            if (movementAnimationController == null)
                continue;

            if (movementAnimationController.animator == animator)
                return true;

            if (movementAnimationController.animator == null
                && movementAnimationController.GetComponent<Animator>() == animator)
            {
                return true;
            }
        }

        return movementAnimationControllers.Count == 0 && animator == ragdollAnimator;
    }

    private void RestoreRootBodyForOwnership()
    {
        if (rootBody == null)
            return;

        bool hasAuthority = HasAuthority();
        rootBody.detectCollisions = true;
        rootBody.isKinematic = !hasAuthority;
        rootBody.useGravity = hasAuthority;
        ClearVelocityIfDynamic(rootBody);
    }

    private void DisableExcludedRagdollBodies()
    {
        for (int i = 0; i < excludedRagdollBodies.Count; i++)
        {
            Rigidbody body = excludedRagdollBodies[i];
            if (body == null)
                continue;

            ClearVelocityIfDynamic(body);
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = false;
            body.Sleep();
        }

        for (int i = 0; i < excludedRagdollColliders.Count; i++)
        {
            Collider bodyCollider = excludedRagdollColliders[i];
            if (bodyCollider != null)
                bodyCollider.enabled = false;
        }
    }

    private void DisconnectExcludedRagdollJoints()
    {
        for (int i = 0; i < excludedRagdollJoints.Count; i++)
        {
            Joint joint = excludedRagdollJoints[i];
            if (joint == null)
                continue;

            if (!preRagdollExcludedJointConnections.ContainsKey(joint))
                preRagdollExcludedJointConnections.Add(joint, joint.connectedBody);

            joint.connectedBody = null;
        }
    }

    private void RestoreExcludedRagdollJoints()
    {
        foreach (KeyValuePair<Joint, Rigidbody> entry in preRagdollExcludedJointConnections)
        {
            if (entry.Key != null)
                entry.Key.connectedBody = entry.Value;
        }

        preRagdollExcludedJointConnections.Clear();
    }

    private Vector3 ResolveInheritedVelocity()
    {
        if (rootBody == null)
            return Vector3.zero;

        Vector3 inheritedVelocity = rootBody.linearVelocity;
        if (maxInheritedVelocity > 0f)
            inheritedVelocity = Vector3.ClampMagnitude(inheritedVelocity, maxInheritedVelocity);

        return inheritedVelocity;
    }

    private void ConfigureRagdollBodyForSimulation(Rigidbody body)
    {
        if (body == null)
            return;

        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        if (maxDepenetrationVelocity > 0f)
            body.maxDepenetrationVelocity = maxDepenetrationVelocity;

        if (maxRagdollAngularVelocity > 0f)
            body.maxAngularVelocity = maxRagdollAngularVelocity;

        body.solverIterations = Mathf.Max(body.solverIterations, ragdollSolverIterations);
        body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, ragdollSolverVelocityIterations);
    }

    private void PrepareRagdollCollidersForSimulation()
    {
        for (int i = 0; i < ragdollColliders.Count; i++)
        {
            Collider ragdollCollider = ragdollColliders[i];
            if (ragdollCollider == null)
                continue;

            if (forceRagdollCollidersSolid)
            {
                if (!preRagdollColliderTriggerStates.ContainsKey(ragdollCollider))
                    preRagdollColliderTriggerStates.Add(ragdollCollider, ragdollCollider.isTrigger);

                ragdollCollider.isTrigger = false;
            }

            if (normalizeSmallRagdollColliders)
                NormalizeSmallRagdollCollider(ragdollCollider);
        }
    }

    private void WarnIfRagdollCollisionLooksUnusable()
    {
        int usableColliderCount = CountUsableRagdollColliders(
            out int colliderCount,
            out float largestWorldExtent);

        int requiredColliderCount = Mathf.Max(1, minUsableRagdollColliderCount);
        if (colliderCount > 0 && usableColliderCount >= requiredColliderCount)
            return;

        float minUsableWorldExtent = Mathf.Max(0.001f, minUsableRagdollColliderWorldExtent);
        Debug.LogWarning(
            $"PlayerRagdollController: ragdoll collision may be too small or missing for ground collision. usableColliders={usableColliderCount}/{colliderCount}, required={requiredColliderCount}, largestWorldExtent={largestWorldExtent:0.###}, minUsableWorldExtent={minUsableWorldExtent:0.###}. The Player root colliders are disabled during ragdoll, so floor contact must come from colliders under the TP_Model ragdoll hierarchy.",
            gameObject);
    }

    private int CountUsableRagdollColliders(out int colliderCount, out float largestWorldExtent)
    {
        colliderCount = 0;
        largestWorldExtent = 0f;
        int usableColliderCount = 0;
        float minUsableWorldExtent = Mathf.Max(0.001f, minUsableRagdollColliderWorldExtent);

        for (int i = 0; i < ragdollColliders.Count; i++)
        {
            Collider ragdollCollider = ragdollColliders[i];
            if (ragdollCollider == null)
                continue;

            colliderCount++;
            float worldExtent = EstimateColliderLargestWorldExtent(ragdollCollider);
            largestWorldExtent = Mathf.Max(largestWorldExtent, worldExtent);
            if (worldExtent >= minUsableWorldExtent)
                usableColliderCount++;
        }

        return usableColliderCount;
    }

    private void RestoreRagdollColliderSimulationState()
    {
        foreach (KeyValuePair<Collider, bool> entry in preRagdollColliderTriggerStates)
        {
            if (entry.Key != null)
                entry.Key.isTrigger = entry.Value;
        }

        foreach (KeyValuePair<CapsuleCollider, CapsuleColliderShape> entry in preRagdollCapsuleColliderShapes)
        {
            CapsuleCollider capsule = entry.Key;
            if (capsule == null)
                continue;

            capsule.radius = entry.Value.Radius;
            capsule.height = entry.Value.Height;
            capsule.direction = entry.Value.Direction;
            capsule.center = entry.Value.Center;
        }

        foreach (KeyValuePair<SphereCollider, SphereColliderShape> entry in preRagdollSphereColliderShapes)
        {
            SphereCollider sphere = entry.Key;
            if (sphere == null)
                continue;

            sphere.radius = entry.Value.Radius;
            sphere.center = entry.Value.Center;
        }

        foreach (KeyValuePair<BoxCollider, BoxColliderShape> entry in preRagdollBoxColliderShapes)
        {
            BoxCollider box = entry.Key;
            if (box == null)
                continue;

            box.size = entry.Value.Size;
            box.center = entry.Value.Center;
        }

        preRagdollColliderTriggerStates.Clear();
        preRagdollCapsuleColliderShapes.Clear();
        preRagdollSphereColliderShapes.Clear();
        preRagdollBoxColliderShapes.Clear();
    }

    private void NormalizeSmallRagdollCollider(Collider ragdollCollider)
    {
        if (ragdollCollider is CapsuleCollider capsule)
        {
            NormalizeSmallCapsuleCollider(capsule);
            return;
        }

        if (ragdollCollider is SphereCollider sphere)
        {
            NormalizeSmallSphereCollider(sphere);
            return;
        }

        if (ragdollCollider is BoxCollider box)
            NormalizeSmallBoxCollider(box);
    }

    private void NormalizeSmallCapsuleCollider(CapsuleCollider capsule)
    {
        Vector3 scale = AbsVector(capsule.transform.lossyScale);
        int direction = Mathf.Clamp(capsule.direction, 0, 2);
        float heightScale = GetAxis(scale, direction);
        float radiusScale = Mathf.Max(GetAxis(scale, (direction + 1) % 3), GetAxis(scale, (direction + 2) % 3));
        if (heightScale <= 0.0001f || radiusScale <= 0.0001f)
            return;

        float minWorldRadius = Mathf.Max(0.001f, minRagdollColliderWorldRadius);
        float minWorldLength = Mathf.Max(minWorldRadius * 2f, minRagdollColliderWorldLength);
        float resolvedRadius = capsule.radius;
        float resolvedHeight = capsule.height;

        if (resolvedRadius * radiusScale < minWorldRadius)
            resolvedRadius = minWorldRadius / radiusScale;

        if (resolvedHeight * heightScale < minWorldLength)
            resolvedHeight = minWorldLength / heightScale;

        resolvedHeight = Mathf.Max(resolvedHeight, resolvedRadius * 2f);
        if (Mathf.Approximately(resolvedRadius, capsule.radius)
            && Mathf.Approximately(resolvedHeight, capsule.height))
            return;

        if (!preRagdollCapsuleColliderShapes.ContainsKey(capsule))
        {
            preRagdollCapsuleColliderShapes.Add(capsule, new CapsuleColliderShape
            {
                Radius = capsule.radius,
                Height = capsule.height,
                Direction = capsule.direction,
                Center = capsule.center
            });
        }

        capsule.radius = resolvedRadius;
        capsule.height = resolvedHeight;
    }

    private void NormalizeSmallSphereCollider(SphereCollider sphere)
    {
        Vector3 scale = AbsVector(sphere.transform.lossyScale);
        float radiusScale = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        if (radiusScale <= 0.0001f)
            return;

        float minWorldRadius = Mathf.Max(0.001f, minRagdollColliderWorldRadius);
        if (sphere.radius * radiusScale >= minWorldRadius)
            return;

        if (!preRagdollSphereColliderShapes.ContainsKey(sphere))
        {
            preRagdollSphereColliderShapes.Add(sphere, new SphereColliderShape
            {
                Radius = sphere.radius,
                Center = sphere.center
            });
        }

        sphere.radius = minWorldRadius / radiusScale;
    }

    private void NormalizeSmallBoxCollider(BoxCollider box)
    {
        Vector3 scale = AbsVector(box.transform.lossyScale);
        Vector3 resolvedSize = box.size;
        float minWorldSize = Mathf.Max(0.001f, minRagdollColliderWorldRadius * 2f);
        bool changed = false;

        for (int axis = 0; axis < 3; axis++)
        {
            float axisScale = GetAxis(scale, axis);
            if (axisScale <= 0.0001f || GetAxis(resolvedSize, axis) * axisScale >= minWorldSize)
                continue;

            SetAxis(ref resolvedSize, axis, minWorldSize / axisScale);
            changed = true;
        }

        if (!changed)
            return;

        if (!preRagdollBoxColliderShapes.ContainsKey(box))
        {
            preRagdollBoxColliderShapes.Add(box, new BoxColliderShape
            {
                Size = box.size,
                Center = box.center
            });
        }

        box.size = resolvedSize;
    }

    private void ApplyRagdollJointStabilityDefaults()
    {
        if (!stabilizeCharacterJoints)
            return;

        for (int i = 0; i < ragdollCharacterJoints.Count; i++)
        {
            CharacterJoint characterJoint = ragdollCharacterJoints[i];
            if (characterJoint == null)
                continue;

            characterJoint.enableProjection = true;
            characterJoint.projectionDistance = jointProjectionDistance;
            characterJoint.projectionAngle = jointProjectionAngle;
            characterJoint.enablePreprocessing = enableJointPreprocessing;

            if (relaxFootEndJoints && IsFootEndJoint(characterJoint))
                RelaxFootEndJoint(characterJoint);
        }
    }

    private void ApplyRagdollCollisionIgnores()
    {
        if (!ignoreRagdollSelfCollision)
            return;

        for (int i = 0; i < ragdollColliders.Count; i++)
        {
            Collider first = ragdollColliders[i];
            if (first == null)
                continue;

            for (int j = i + 1; j < ragdollColliders.Count; j++)
            {
                Collider second = ragdollColliders[j];
                if (second != null)
                    Physics.IgnoreCollision(first, second, true);
            }

            for (int j = 0; j < rootColliders.Count; j++)
            {
                Collider rootCollider = rootColliders[j];
                if (rootCollider != null)
                    Physics.IgnoreCollision(first, rootCollider, true);
            }
        }
    }

    private bool IsFootEndJoint(CharacterJoint characterJoint)
    {
        if (characterJoint == null)
            return false;

        Transform jointTransform = characterJoint.transform;
        string bodyName = jointTransform != null ? jointTransform.name : string.Empty;
        string connectedName = characterJoint.connectedBody != null ? characterJoint.connectedBody.name : string.Empty;

        if (IsFootBoneName(bodyName))
            return true;

        return ContainsIgnoreCase(bodyName, "leg_lower")
            && ContainsIgnoreCase(bodyName, "end")
            && ContainsIgnoreCase(connectedName, "leg_lower");
    }

    private void RelaxFootEndJoint(CharacterJoint characterJoint)
    {
        float swingLimit = Mathf.Max(0f, footEndJointSwingLimit);
        float twistLimit = Mathf.Max(0f, footEndJointTwistLimit);

        SoftJointLimit swing1 = characterJoint.swing1Limit;
        if (swing1.limit < swingLimit)
        {
            swing1.limit = swingLimit;
            characterJoint.swing1Limit = swing1;
        }

        SoftJointLimit swing2 = characterJoint.swing2Limit;
        if (swing2.limit < swingLimit)
        {
            swing2.limit = swingLimit;
            characterJoint.swing2Limit = swing2;
        }

        SoftJointLimit lowTwist = characterJoint.lowTwistLimit;
        if (lowTwist.limit > -twistLimit)
        {
            lowTwist.limit = -twistLimit;
            characterJoint.lowTwistLimit = lowTwist;
        }

        SoftJointLimit highTwist = characterJoint.highTwistLimit;
        if (highTwist.limit < twistLimit)
        {
            highTwist.limit = twistLimit;
            characterJoint.highTwistLimit = highTwist;
        }
    }

    private static void ClearVelocityIfDynamic(Rigidbody body)
    {
        if (body == null || body.isKinematic)
            return;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    [ContextMenu("Log Ragdoll Setup")]
    private void LogRagdollSetup()
    {
        CacheReferences();
        CacheRagdollParts(forceRefresh: true);
        LogRagdollSetup("inspected");
    }

    [ContextMenu("Log Ragdoll Foot Diagnostics")]
    private void LogRagdollFootDiagnostics()
    {
        CacheReferences();
        CacheRagdollParts(forceRefresh: true);

        Transform root = ragdollRoot != null ? ragdollRoot : transform;
        StringBuilder builder = new StringBuilder(4096);
        builder.AppendLine($"[PlayerRagdollController] Foot diagnostics for {name}");
        builder.AppendLine($"Root: {GetTransformPath(root, transform)} | bodies={ragdollBodies.Count} | colliders={ragdollColliders.Count} | excludedBodies={excludedRagdollBodies.Count} | excludedColliders={excludedRagdollColliders.Count} | excludedJoints={excludedRagdollJoints.Count} | joints={ragdollJoints.Count} | characterJoints={ragdollCharacterJoints.Count}");
        builder.AppendLine($"Stability target on activation: stabilize={stabilizeCharacterJoints} | projection=true({jointProjectionDistance:0.###}/{jointProjectionAngle:0.#}) | preprocessing={enableJointPreprocessing}");
        builder.AppendLine("Ragdoll bodies:");

        for (int i = 0; i < ragdollBodies.Count; i++)
        {
            Rigidbody body = ragdollBodies[i];
            if (body == null)
                continue;

            Joint joint = body.GetComponent<Joint>();
            builder.Append(" - ")
                .Append(GetTransformPath(body.transform, root))
                .Append(" | scale=").Append(FormatVector(body.transform.lossyScale))
                .Append(" | mass=").Append(body.mass.ToString("0.###"));

            if (joint != null)
            {
                builder.Append(" | joint=").Append(joint.GetType().Name)
                    .Append(" | connected=")
                    .Append(joint.connectedBody != null ? GetTransformPath(joint.connectedBody.transform, root) : "<none>");

                if (joint is CharacterJoint characterJoint)
                {
                    builder.Append(" | projection=").Append(characterJoint.enableProjection)
                        .Append('(').Append(characterJoint.projectionDistance.ToString("0.###"))
                        .Append('/').Append(characterJoint.projectionAngle.ToString("0.#")).Append(')')
                        .Append(" | preprocessing=").Append(characterJoint.enablePreprocessing);
                }
            }
            else
            {
                builder.Append(" | rootBody");
            }

            builder.Append(" | colliders=");
            Collider[] bodyColliders = body.GetComponentsInChildren<Collider>(true);
            int matchingColliderCount = 0;
            if (bodyColliders.Length == 0)
            {
                builder.Append("<none>");
            }
            else
            {
                for (int j = 0; j < bodyColliders.Length; j++)
                {
                    Collider bodyCollider = bodyColliders[j];
                    if (bodyCollider == null || bodyCollider.attachedRigidbody != body)
                        continue;

                    if (matchingColliderCount > 0)
                        builder.Append(", ");

                    matchingColliderCount++;
                    builder.Append(DescribeCollider(bodyCollider));
                }

                if (matchingColliderCount == 0)
                    builder.Append("<none attached>");
            }

            builder.AppendLine();
        }

        AppendExcludedRagdollBodyDiagnostics(builder, root);
        AppendLegAndFootRendererDiagnostics(builder, root);
        Debug.Log(builder.ToString(), gameObject);
    }

    private void LogRagdollSetup(string action)
    {
        string rootName = ragdollRoot != null ? ragdollRoot.name : "<none>";
        string animatorName = ragdollAnimator != null ? ragdollAnimator.name : "<none>";
        int usableColliderCount = CountUsableRagdollColliders(
            out int colliderCount,
            out float largestWorldExtent);

        Debug.Log(
            $"[PlayerRagdollController] {action}: root={rootName}, animator={animatorName}, animators={ragdollAnimators.Count}, bodies={ragdollBodies.Count}, excludedBodies={excludedRagdollBodies.Count}, excludedJoints={excludedRagdollJoints.Count}, joints={ragdollJoints.Count}, characterJoints={ragdollCharacterJoints.Count}, colliders={ragdollColliders.Count}, usableColliders={usableColliderCount}/{colliderCount}, largestColliderExtent={largestWorldExtent:0.###}, rootColliders={rootColliders.Count}.",
            gameObject);
    }

    private void AppendExcludedRagdollBodyDiagnostics(StringBuilder builder, Transform root)
    {
        if (excludedRagdollBodies.Count == 0)
            return;

        builder.AppendLine("Excluded ragdoll bodies:");
        for (int i = 0; i < excludedRagdollBodies.Count; i++)
        {
            Rigidbody body = excludedRagdollBodies[i];
            if (body == null)
                continue;

            builder.Append(" - ")
                .Append(GetTransformPath(body.transform, root))
                .Append(" | reason=end/IK/target")
                .Append(" | colliders=");

            Collider[] bodyColliders = body.GetComponentsInChildren<Collider>(true);
            int matchingColliderCount = 0;
            for (int j = 0; j < bodyColliders.Length; j++)
            {
                Collider bodyCollider = bodyColliders[j];
                if (bodyCollider == null || bodyCollider.attachedRigidbody != body)
                    continue;

                if (matchingColliderCount > 0)
                    builder.Append(", ");

                matchingColliderCount++;
                builder.Append(DescribeCollider(bodyCollider));
            }

            if (matchingColliderCount == 0)
                builder.Append("<none attached>");

            builder.AppendLine();
        }
    }

    private void AppendLegAndFootRendererDiagnostics(StringBuilder builder, Transform root)
    {
        Transform renderRoot = ragdollRoot != null ? ragdollRoot : transform;
        SkinnedMeshRenderer[] skinnedRenderers = renderRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int relevantRendererCount = 0;
        bool foundNamedFootBone = false;

        builder.AppendLine("Leg/foot skinned renderers:");

        for (int i = 0; i < skinnedRenderers.Length; i++)
        {
            SkinnedMeshRenderer skinnedRenderer = skinnedRenderers[i];
            if (skinnedRenderer == null)
                continue;

            string rendererPath = GetTransformPath(skinnedRenderer.transform, renderRoot);
            string meshName = skinnedRenderer.sharedMesh != null ? skinnedRenderer.sharedMesh.name : "<none>";
            if (!IsLegOrFootName(rendererPath) && !IsLegOrFootName(meshName))
                continue;

            relevantRendererCount++;
            Transform rootBone = skinnedRenderer.rootBone;
            builder.Append(" - ")
                .Append(rendererPath)
                .Append(" | mesh=").Append(meshName)
                .Append(" | enabled=").Append(skinnedRenderer.enabled)
                .Append(" | rootBone=").Append(rootBone != null ? GetTransformPath(rootBone, root) : "<none>")
                .Append(" | bones=").Append(skinnedRenderer.bones != null ? skinnedRenderer.bones.Length : 0)
                .AppendLine();

            int relevantBoneCount = 0;
            Transform[] bones = skinnedRenderer.bones;
            if (bones == null)
                continue;

            for (int j = 0; j < bones.Length; j++)
            {
                Transform bone = bones[j];
                if (bone == null || !IsLegOrFootBoneName(bone.name))
                    continue;

                relevantBoneCount++;
                if (IsFootBoneName(bone.name))
                    foundNamedFootBone = true;

                Rigidbody boneBody = bone.GetComponent<Rigidbody>();
                CharacterJoint boneJoint = bone.GetComponent<CharacterJoint>();

                builder.Append("    bone ")
                    .Append(GetTransformPath(bone, root))
                    .Append(" | localScale=").Append(FormatVector(bone.localScale))
                    .Append(" | body=").Append(boneBody != null ? "yes" : "no");

                if (boneBody != null)
                {
                    builder.Append(" | ragdollState=");
                    if (ragdollBodies.Contains(boneBody))
                        builder.Append("simulated");
                    else if (excludedRagdollBodies.Contains(boneBody))
                        builder.Append("excluded");
                    else
                        builder.Append("not-collected");
                }

                if (boneJoint != null)
                {
                    builder.Append(" | joint connected=")
                        .Append(boneJoint.connectedBody != null ? GetTransformPath(boneJoint.connectedBody.transform, root) : "<none>");
                }

                builder.AppendLine();
            }

            if (relevantBoneCount == 0)
                builder.AppendLine("    no leg/foot-related bones listed by this renderer");
        }

        if (relevantRendererCount == 0)
            builder.AppendLine(" - no renderer with leg/foot/lower-body naming was found under the ragdoll root");

        if (!foundNamedFootBone)
            builder.AppendLine("Observation: no bone named Foot/Feet/Toe was found in those renderers; visible feet are probably skinned to lower-leg/end bones.");
    }

    private static string DescribeCollider(Collider collider)
    {
        if (collider == null)
            return "<null>";

        if (collider is CapsuleCollider capsule)
            return $"Capsule(r={capsule.radius:0.####}, h={capsule.height:0.####}, world={FormatVector(EstimateCapsuleWorldSize(capsule))}, center={FormatVector(capsule.center)})";

        if (collider is BoxCollider box)
            return $"Box(size={FormatVector(box.size)}, world={FormatVector(Vector3.Scale(box.size, AbsVector(box.transform.lossyScale)))}, center={FormatVector(box.center)})";

        if (collider is SphereCollider sphere)
            return $"Sphere(r={sphere.radius:0.####}, worldRadius={EstimateSphereWorldRadius(sphere):0.###}, center={FormatVector(sphere.center)})";

        return collider.GetType().Name;
    }

    private static Vector3 EstimateCapsuleWorldSize(CapsuleCollider capsule)
    {
        if (capsule == null)
            return Vector3.zero;

        Vector3 scale = AbsVector(capsule.transform.lossyScale);
        int direction = Mathf.Clamp(capsule.direction, 0, 2);
        float heightScale = GetAxis(scale, direction);
        float radiusScale = Mathf.Max(GetAxis(scale, (direction + 1) % 3), GetAxis(scale, (direction + 2) % 3));
        float diameter = capsule.radius * 2f * radiusScale;
        float height = capsule.height * heightScale;
        Vector3 worldSize = new Vector3(diameter, diameter, diameter);
        SetAxis(ref worldSize, direction, height);
        return worldSize;
    }

    private static float EstimateSphereWorldRadius(SphereCollider sphere)
    {
        if (sphere == null)
            return 0f;

        Vector3 scale = AbsVector(sphere.transform.lossyScale);
        return sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
    }

    private static float EstimateColliderLargestWorldExtent(Collider collider)
    {
        if (collider == null)
            return 0f;

        if (collider is CapsuleCollider capsule)
            return LargestAxis(EstimateCapsuleWorldSize(capsule));

        if (collider is BoxCollider box)
            return LargestAxis(Vector3.Scale(box.size, AbsVector(box.transform.lossyScale)));

        if (collider is SphereCollider sphere)
            return EstimateSphereWorldRadius(sphere) * 2f;

        return LargestAxis(collider.bounds.size);
    }

    private static float LargestAxis(Vector3 value)
    {
        return Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
    }

    private static bool IsLegOrFootBoneName(string value)
    {
        return IsLegOrFootName(value)
            || ContainsIgnoreCase(value, "hip")
            || ContainsIgnoreCase(value, "end")
            || ContainsIgnoreCase(value, "ik")
            || ContainsIgnoreCase(value, "target");
    }

    private static bool IsLegOrFootName(string value)
    {
        return ContainsIgnoreCase(value, "leg")
            || ContainsIgnoreCase(value, "foot")
            || ContainsIgnoreCase(value, "feet")
            || ContainsIgnoreCase(value, "toe")
            || ContainsIgnoreCase(value, "lower body");
    }

    private static bool IsFootBoneName(string value)
    {
        return ContainsIgnoreCase(value, "foot")
            || ContainsIgnoreCase(value, "feet")
            || ContainsIgnoreCase(value, "toe");
    }

    private static bool ContainsIgnoreCase(string value, string match)
    {
        return !string.IsNullOrEmpty(value)
            && value.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Vector3 AbsVector(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        switch (axis)
        {
            case 0:
                return value.x;
            case 1:
                return value.y;
            default:
                return value.z;
        }
    }

    private static void SetAxis(ref Vector3 value, int axis, float axisValue)
    {
        switch (axis)
        {
            case 0:
                value.x = axisValue;
                break;
            case 1:
                value.y = axisValue;
                break;
            default:
                value.z = axisValue;
                break;
        }
    }

    private static string GetTransformPath(Transform target, Transform relativeRoot = null)
    {
        if (target == null)
            return "<none>";

        Stack<string> names = new Stack<string>();
        Transform cursor = target;
        while (cursor != null)
        {
            names.Push(cursor.name);
            if (relativeRoot != null && cursor == relativeRoot)
                break;

            cursor = cursor.parent;
        }

        return string.Join("/", names.ToArray());
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
    }

    private void ApplyActivationImpulse(Vector3 hitPoint, Vector3 hitDirection, float impulse, float upward)
    {
        Vector3 resolvedImpulse = ResolveImpulseDirection(hitDirection) * Mathf.Max(0f, impulse)
            + Vector3.up * Mathf.Max(0f, upward);

        if (resolvedImpulse.sqrMagnitude <= 0.0001f)
            return;

        Rigidbody targetBody = ResolveImpulseBody(hitPoint);
        if (targetBody != null)
        {
            Vector3 forcePoint = hitPoint.sqrMagnitude > 0.0001f ? hitPoint : targetBody.worldCenterOfMass;
            targetBody.AddForceAtPosition(resolvedImpulse, forcePoint, ForceMode.Impulse);
            return;
        }

        Vector3 distributedImpulse = resolvedImpulse / Mathf.Max(1, ragdollBodies.Count);
        for (int i = 0; i < ragdollBodies.Count; i++)
        {
            Rigidbody body = ragdollBodies[i];
            if (body != null)
                body.AddForce(distributedImpulse, ForceMode.Impulse);
        }
    }

    private Rigidbody ResolveImpulseBody(Vector3 hitPoint)
    {
        if (ragdollBodies.Count == 0)
            return null;

        if (hitPoint.sqrMagnitude <= 0.0001f)
            return ragdollBodies[0];

        Rigidbody nearestBody = null;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < ragdollBodies.Count; i++)
        {
            Rigidbody body = ragdollBodies[i];
            if (body == null)
                continue;

            float distance = (body.worldCenterOfMass - hitPoint).sqrMagnitude;
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearestBody = body;
        }

        return nearestBody;
    }

    private Vector3 ResolveImpulseDirection(Vector3 hitDirection)
    {
        Vector3 planarDirection = Vector3.ProjectOnPlane(hitDirection, Vector3.up);
        if (planarDirection.sqrMagnitude > 0.0001f)
            return planarDirection.normalized;

        Vector3 fallback = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
        return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.back;
    }

    private bool HasAuthority()
    {
        return photonView == null || photonView.IsMine;
    }
}
