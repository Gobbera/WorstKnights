using System;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public class EnemySetup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PhotonView photonView;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CapsuleCollider capsule;
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyMotor enemyMotor;
    [SerializeField] private EnemyAttack enemyAttack;
    [SerializeField] private EnemyAnimationController enemyAnimationController;
    [SerializeField] private EnemyNetworkSync enemyNetworkSync;
    [SerializeField] private Transform attackOrigin;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform modelRoot;

    private Collider[] colliders;

    public PhotonView PhotonView => photonView;
    public Rigidbody Rigidbody => rb;
    public CapsuleCollider Capsule => capsule;
    public EnemyHealth EnemyHealth => enemyHealth;
    public EnemyBrain EnemyBrain => enemyBrain;
    public EnemyMotor EnemyMotor => enemyMotor;
    public EnemyAttack EnemyAttack => enemyAttack;
    public EnemyAnimationController EnemyAnimationController => enemyAnimationController;
    public EnemyNetworkSync EnemyNetworkSync => enemyNetworkSync;
    public Transform AttackOrigin => attackOrigin != null ? attackOrigin : transform;
    public Animator Animator => animator;
    public Transform ModelRoot => modelRoot != null ? modelRoot : transform;
    public bool HasAuthority => photonView == null || PhotonNetwork.OfflineMode || !PhotonNetwork.InRoom || photonView.IsMine;

    private void Awake()
    {
        RefreshReferences();
        ApplyAliveState(enemyHealth == null || enemyHealth.IsAlive);
        ApplySimulationState(HasAuthority, enemyHealth == null || enemyHealth.IsAlive);
    }

    public void RefreshReferences()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (capsule == null)
            capsule = GetComponent<CapsuleCollider>();

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (enemyBrain == null)
            enemyBrain = GetComponent<EnemyBrain>();

        if (enemyMotor == null)
            enemyMotor = GetComponent<EnemyMotor>();

        if (enemyAttack == null)
            enemyAttack = GetComponent<EnemyAttack>();

        if (enemyAnimationController == null)
            enemyAnimationController = GetComponent<EnemyAnimationController>();

        if (enemyNetworkSync == null)
            enemyNetworkSync = GetComponent<EnemyNetworkSync>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (modelRoot == null)
            modelRoot = animator != null ? animator.transform : transform;

        if (attackOrigin == null)
        {
            Transform namedAttackOrigin = FindNamedChild("AttackOrigin");
            attackOrigin = namedAttackOrigin != null ? namedAttackOrigin : transform;
        }

        colliders = GetComponentsInChildren<Collider>(true);
        EnsurePhysicsDefaults(HasAuthority, enemyHealth == null || enemyHealth.IsAlive);
    }

    public void ApplyAliveState(bool isAlive)
    {
        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider currentCollider = colliders[i];
            if (currentCollider == null)
                continue;

            currentCollider.enabled = isAlive;
        }
    }

    public void ApplySimulationState(bool hasAuthority, bool isAlive)
    {
        enemyBrain?.SetSimulationEnabled(hasAuthority && isAlive);
        enemyMotor?.SetAliveState(isAlive);
        enemyAttack?.SetAliveState(isAlive);
        EnsurePhysicsDefaults(hasAuthority, isAlive);
    }

    private void EnsurePhysicsDefaults(bool hasAuthority, bool isAlive)
    {
        if (rb == null)
            return;

        bool simulatePhysics = hasAuthority && isAlive;
        if (!simulatePhysics)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        rb.isKinematic = !simulatePhysics;
        rb.useGravity = simulatePhysics;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private Transform FindNamedChild(string childName)
    {
        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform childTransform = childTransforms[i];
            if (childTransform != null && string.Equals(childTransform.name, childName, StringComparison.Ordinal))
                return childTransform;
        }

        return null;
    }
}
