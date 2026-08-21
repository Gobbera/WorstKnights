using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyNetworkSync : MonoBehaviour, IPunObservable
{
    [Header("References")]
    [SerializeField] private EnemySetup enemySetup;
    [SerializeField] private EnemyBrain enemyBrain;
    [SerializeField] private EnemyMotor enemyMotor;
    [SerializeField] private EnemyAttack enemyAttack;
    [SerializeField] private EnemyHealth enemyHealth;

    [Header("Remote Interpolation")]
    [SerializeField] [Min(0.01f)] private float remotePositionLerpSpeed = 12f;
    [SerializeField] [Min(0.01f)] private float remoteRotationLerpSpeed = 16f;
    [SerializeField] [Min(0.1f)] private float remoteTeleportDistance = 4f;

    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private EnemyState networkState;
    private float networkPlanarSpeed;
    private float networkHealth;
    private int networkAttackSequence;
    private int networkDamageSequence;
    private bool networkIsAlive = true;
    private bool hasNetworkState;
    private bool lastAuthorityState;

    private void Awake()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyBrain == null)
            enemyBrain = GetComponent<EnemyBrain>();

        if (enemyMotor == null)
            enemyMotor = GetComponent<EnemyMotor>();

        if (enemyAttack == null)
            enemyAttack = GetComponent<EnemyAttack>();

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        networkPosition = transform.position;
        networkRotation = transform.rotation;
        lastAuthorityState = HasAuthority();
        ApplyAuthorityState(lastAuthorityState);
    }

    private void Update()
    {
        bool hasAuthority = HasAuthority();
        if (hasAuthority != lastAuthorityState)
        {
            lastAuthorityState = hasAuthority;
            ApplyAuthorityState(hasAuthority);
        }

        if (hasAuthority || !hasNetworkState)
            return;

        ApplyReplicatedState();
    }

    private void FixedUpdate()
    {
        if (HasAuthority() || !hasNetworkState)
            return;

        InterpolateRemoteTransform(Time.fixedDeltaTime);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext((int)(enemyBrain != null ? enemyBrain.CurrentState : EnemyState.Idle));
            stream.SendNext(enemyMotor != null ? enemyMotor.CurrentPlanarSpeed : 0f);
            stream.SendNext(enemyHealth != null ? enemyHealth.CurrentHealth : 0f);
            stream.SendNext(enemyHealth == null || enemyHealth.IsAlive);
            stream.SendNext(enemyAttack != null ? enemyAttack.AttackSequence : 0);
            stream.SendNext(enemyHealth != null ? enemyHealth.DamageSequence : 0);
            return;
        }

        networkPosition = (Vector3)stream.ReceiveNext();
        networkRotation = (Quaternion)stream.ReceiveNext();
        networkState = (EnemyState)(int)stream.ReceiveNext();
        networkPlanarSpeed = (float)stream.ReceiveNext();
        networkHealth = (float)stream.ReceiveNext();
        networkIsAlive = (bool)stream.ReceiveNext();
        networkAttackSequence = (int)stream.ReceiveNext();
        networkDamageSequence = (int)stream.ReceiveNext();

        if (!hasNetworkState)
        {
            hasNetworkState = true;
            transform.SetPositionAndRotation(networkPosition, networkRotation);
        }

        ApplyReplicatedState();
    }

    private bool HasAuthority()
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        return enemySetup == null || enemySetup.HasAuthority;
    }

    private void ApplyAuthorityState(bool hasAuthority)
    {
        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (enemySetup == null)
            return;

        bool isAlive = enemyHealth == null || enemyHealth.IsAlive;
        enemySetup.ApplyAliveState(isAlive);
        enemySetup.ApplySimulationState(hasAuthority, isAlive);
    }

    private void ApplyReplicatedState()
    {
        // Disable local AI first. EnemyBrain resets to Idle when simulation is
        // disabled, so replicated values must be applied after this handoff.
        if (enemySetup != null)
        {
            enemySetup.ApplyAliveState(networkIsAlive);
            enemySetup.ApplySimulationState(false, networkIsAlive);
        }

        if (enemyHealth != null)
        {
            enemyHealth.ApplyNetworkState(networkHealth, networkIsAlive);
            enemyHealth.ApplyReplicatedDamageSequence(networkDamageSequence);
        }

        enemyBrain?.ApplyReplicatedState(networkState);
        enemyMotor?.ApplyReplicatedPlanarSpeed(networkPlanarSpeed);
        enemyAttack?.ApplyReplicatedAttackSequence(networkAttackSequence);
    }

    private void InterpolateRemoteTransform(float deltaTime)
    {
        Rigidbody rb = enemySetup != null ? enemySetup.Rigidbody : null;
        if (rb == null)
        {
            float positionT = 1f - Mathf.Exp(-remotePositionLerpSpeed * deltaTime);
            float rotationT = 1f - Mathf.Exp(-remoteRotationLerpSpeed * deltaTime);
            transform.position = Vector3.Lerp(transform.position, networkPosition, positionT);
            transform.rotation = Quaternion.Slerp(transform.rotation, networkRotation, rotationT);
            return;
        }

        float distanceToTarget = Vector3.Distance(rb.position, networkPosition);
        if (distanceToTarget > remoteTeleportDistance)
        {
            rb.position = networkPosition;
            rb.rotation = networkRotation;
            return;
        }

        float lerpPositionT = 1f - Mathf.Exp(-remotePositionLerpSpeed * deltaTime);
        float lerpRotationT = 1f - Mathf.Exp(-remoteRotationLerpSpeed * deltaTime);
        rb.MovePosition(Vector3.Lerp(rb.position, networkPosition, lerpPositionT));
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, networkRotation, lerpRotationT));
    }
}
