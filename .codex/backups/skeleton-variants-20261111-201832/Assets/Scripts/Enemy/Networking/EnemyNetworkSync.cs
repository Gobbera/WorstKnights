using System.Collections.Generic;
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
    [SerializeField] private BeholderMoodController beholderMood;
    [SerializeField] private BeholderDialogueController beholderDialogue;

    [Header("Remote Interpolation")]
    [SerializeField] [Min(0.01f)] private float remotePositionLerpSpeed = 12f;
    [SerializeField] [Min(0.01f)] private float remoteRotationLerpSpeed = 16f;
    [SerializeField] [Min(0.1f)] private float remoteTeleportDistance = 4f;

    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private Vector3 networkVelocity;
    private double networkSentAt;
    private EnemyState networkState;
    private bool networkAlertArmed;
    private int networkTargetViewId;
    private bool networkHasLastKnown;
    private Vector3 networkLastKnown;
    private EnemySearchArea networkPrimaryArea;
    private int networkPrimaryPlayerViewId;
    private readonly List<EnemyInvestigationMemory.Snapshot> capturedEvidence
        = new List<EnemyInvestigationMemory.Snapshot>();
    private readonly List<EnemyInvestigationMemory.Snapshot> networkEvidence
        = new List<EnemyInvestigationMemory.Snapshot>();
    private float networkEvidenceReceivedAt;
    private float networkPlanarSpeed;
    private float networkHealth;
    private int networkAttackSequence;
    private int networkDamageSequence;
    private bool networkIsAlive = true;
    private bool hasNetworkState;
    private bool lastAuthorityState;
    private bool networkHasMood;
    private int networkMoodPoints;
    private BeholderMood networkMood;
    private bool networkHasDialogue;
    private int networkDialogueSequence;
    private EnemyState networkDialogueState;
    private BeholderMood networkDialogueMood;
    private int networkDialogueCueIndex;
    private int networkDialogueClipIndex;
    private float networkDialogueVolume;
    private float networkDialoguePitch;
    private double networkDialogueStartedAt;
    private RigidbodyInterpolation initialInterpolation;

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

        if (beholderMood == null)
            beholderMood = GetComponent<BeholderMoodController>();

        if (beholderDialogue == null)
            beholderDialogue = GetComponent<BeholderDialogueController>();

        Rigidbody rb = enemySetup != null ? enemySetup.Rigidbody : GetComponent<Rigidbody>();
        if (rb != null)
            initialInterpolation = rb.interpolation;

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
            Rigidbody rb = enemySetup != null ? enemySetup.Rigidbody : GetComponent<Rigidbody>();
            stream.SendNext(rb != null ? rb.linearVelocity : Vector3.zero);
            stream.SendNext((int)(enemyBrain != null ? enemyBrain.CurrentState : EnemyState.Idle));
            stream.SendNext(enemyBrain == null || enemyBrain.AlertArmed);
            PhotonView targetView = enemyBrain != null && enemyBrain.CurrentTarget != null
                ? enemyBrain.CurrentTarget.GetComponent<PhotonView>()
                : null;
            stream.SendNext(targetView != null ? targetView.ViewID : 0);
            stream.SendNext(enemyBrain != null && enemyBrain.HasLastKnownTargetPosition);
            stream.SendNext(enemyBrain != null ? enemyBrain.LastKnownTargetPosition : Vector3.zero);
            EnemySearchArea primaryArea = enemyBrain != null ? enemyBrain.InvestigationPrimaryArea : null;
            stream.SendNext(primaryArea != null);
            if (primaryArea != null)
                stream.SendNext(primaryArea.transform.position);
            PhotonView primaryPlayerView = enemyBrain != null && enemyBrain.InvestigationPrimaryPlayer != null
                ? enemyBrain.InvestigationPrimaryPlayer.GetComponent<PhotonView>() : null;
            stream.SendNext(primaryPlayerView != null ? primaryPlayerView.ViewID : 0);

            capturedEvidence.Clear();
            enemyBrain?.CaptureInvestigation(capturedEvidence);
            int evidenceCount = 0;
            foreach (EnemyInvestigationMemory.Snapshot evidence in capturedEvidence)
            {
                if (evidence.Player != null && evidence.Player.GetComponent<PhotonView>() != null)
                    evidenceCount++;
            }

            stream.SendNext(evidenceCount);
            foreach (EnemyInvestigationMemory.Snapshot evidence in capturedEvidence)
            {
                PhotonView playerView = evidence.Player != null
                    ? evidence.Player.GetComponent<PhotonView>() : null;
                if (playerView == null)
                    continue;

                stream.SendNext(playerView.ViewID);
                stream.SendNext(evidence.Position);
                stream.SendNext(evidence.Age);
                stream.SendNext(evidence.Visited);
                stream.SendNext(evidence.SignalArea != null);
                if (evidence.SignalArea != null)
                    stream.SendNext(evidence.SignalArea.transform.position);
                stream.SendNext(evidence.NextArea != null);
                if (evidence.NextArea != null)
                    stream.SendNext(evidence.NextArea.transform.position);
            }

            stream.SendNext(enemyMotor != null ? enemyMotor.CurrentPlanarSpeed : 0f);
            stream.SendNext(enemyHealth != null ? enemyHealth.CurrentHealth : 0f);
            stream.SendNext(enemyHealth == null || enemyHealth.IsAlive);
            stream.SendNext(enemyAttack != null ? enemyAttack.AttackSequence : 0);
            stream.SendNext(enemyHealth != null ? enemyHealth.DamageSequence : 0);
            stream.SendNext(beholderMood != null);
            if (beholderMood != null)
            {
                stream.SendNext(beholderMood.Points);
                stream.SendNext((int)beholderMood.CurrentMood);
            }

            stream.SendNext(beholderDialogue != null);
            if (beholderDialogue != null)
            {
                stream.SendNext(beholderDialogue.CueSequence);
                stream.SendNext((int)beholderDialogue.CueState);
                stream.SendNext((int)beholderDialogue.CueMood);
                stream.SendNext(beholderDialogue.CueIndex);
                stream.SendNext(beholderDialogue.CueClipIndex);
                stream.SendNext(beholderDialogue.CueVolume);
                stream.SendNext(beholderDialogue.CuePitch);
                stream.SendNext(beholderDialogue.CueStartedAt);
            }
            return;
        }

        networkPosition = (Vector3)stream.ReceiveNext();
        networkRotation = (Quaternion)stream.ReceiveNext();
        networkVelocity = (Vector3)stream.ReceiveNext();
        networkSentAt = info.SentServerTime;
        networkState = (EnemyState)(int)stream.ReceiveNext();
        networkAlertArmed = (bool)stream.ReceiveNext();
        networkTargetViewId = (int)stream.ReceiveNext();
        networkHasLastKnown = (bool)stream.ReceiveNext();
        networkLastKnown = (Vector3)stream.ReceiveNext();
        networkPrimaryArea = (bool)stream.ReceiveNext()
            ? EnemySearchArea.FindByAnchor((Vector3)stream.ReceiveNext(), gameObject.scene) : null;
        networkPrimaryPlayerViewId = (int)stream.ReceiveNext();
        networkEvidence.Clear();
        networkEvidenceReceivedAt = Time.time;
        int receivedEvidenceCount = (int)stream.ReceiveNext();
        for (int i = 0; i < receivedEvidenceCount; i++)
        {
            int playerViewId = (int)stream.ReceiveNext();
            Vector3 position = (Vector3)stream.ReceiveNext();
            float age = (float)stream.ReceiveNext();
            bool visited = (bool)stream.ReceiveNext();
            EnemySearchArea signalArea = (bool)stream.ReceiveNext()
                ? EnemySearchArea.FindByAnchor((Vector3)stream.ReceiveNext(), gameObject.scene) : null;
            EnemySearchArea nextArea = (bool)stream.ReceiveNext()
                ? EnemySearchArea.FindByAnchor((Vector3)stream.ReceiveNext(), gameObject.scene) : null;
            PlayerHealth player = PhotonView.Find(playerViewId)?.GetComponent<PlayerHealth>();
            if (player != null)
                networkEvidence.Add(new EnemyInvestigationMemory.Snapshot(
                    player, position, age, visited, signalArea, nextArea));
        }

        networkPlanarSpeed = (float)stream.ReceiveNext();
        networkHealth = (float)stream.ReceiveNext();
        networkIsAlive = (bool)stream.ReceiveNext();
        networkAttackSequence = (int)stream.ReceiveNext();
        networkDamageSequence = (int)stream.ReceiveNext();
        networkHasMood = (bool)stream.ReceiveNext();
        if (networkHasMood)
        {
            networkMoodPoints = (int)stream.ReceiveNext();
            networkMood = (BeholderMood)(int)stream.ReceiveNext();
        }

        networkHasDialogue = (bool)stream.ReceiveNext();
        if (networkHasDialogue)
        {
            networkDialogueSequence = (int)stream.ReceiveNext();
            networkDialogueState = (EnemyState)(int)stream.ReceiveNext();
            networkDialogueMood = (BeholderMood)(int)stream.ReceiveNext();
            networkDialogueCueIndex = (int)stream.ReceiveNext();
            networkDialogueClipIndex = (int)stream.ReceiveNext();
            networkDialogueVolume = (float)stream.ReceiveNext();
            networkDialoguePitch = (float)stream.ReceiveNext();
            networkDialogueStartedAt = (double)stream.ReceiveNext();
        }

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
        beholderDialogue?.OnAuthorityChanged(hasAuthority);

        if (enemySetup == null)
            enemySetup = GetComponent<EnemySetup>();

        Rigidbody rb = enemySetup != null ? enemySetup.Rigidbody : GetComponent<Rigidbody>();
        if (rb != null)
            rb.interpolation = hasAuthority ? initialInterpolation : RigidbodyInterpolation.Interpolate;

        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (enemySetup == null)
            return;

        bool isAlive = enemyHealth == null || enemyHealth.IsAlive;
        enemySetup.ApplyAliveState(isAlive);
        enemySetup.ApplySimulationState(hasAuthority, isAlive);
        if (hasAuthority && hasNetworkState && enemyBrain != null)
        {
            PlayerHealth target = networkTargetViewId != 0
                ? PhotonView.Find(networkTargetViewId)?.GetComponent<PlayerHealth>() : null;
            enemyBrain.RestoreAuthorityAfterHandoff(networkState, target,
                networkLastKnown, networkHasLastKnown, networkAlertArmed);
        }
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
        enemyBrain?.ApplyReplicatedAlertArmed(networkAlertArmed);
        PlayerHealth target = networkTargetViewId != 0
            ? PhotonView.Find(networkTargetViewId)?.GetComponent<PlayerHealth>()
            : null;
        enemyBrain?.ApplyReplicatedTarget(target);
        enemyBrain?.ApplyReplicatedInvestigation(networkEvidence,
            networkPrimaryArea,
            networkPrimaryPlayerViewId != 0
                ? PhotonView.Find(networkPrimaryPlayerViewId)?.GetComponent<PlayerHealth>() : null,
            networkLastKnown, networkHasLastKnown,
            Mathf.Max(0f, Time.time - networkEvidenceReceivedAt));
        if (networkHasMood)
            beholderMood?.ApplyReplicatedState(networkMoodPoints, networkMood);

        if (networkHasDialogue)
            beholderDialogue?.ApplyReplicatedCue(
                networkDialogueSequence,
                networkDialogueState,
                networkDialogueMood,
                networkDialogueCueIndex,
                networkDialogueClipIndex,
                networkDialogueVolume,
                networkDialoguePitch,
                networkDialogueStartedAt);

        enemyMotor?.ApplyReplicatedPlanarSpeed(networkPlanarSpeed);
        enemyAttack?.ApplyReplicatedAttackSequence(networkAttackSequence);
    }

    private void InterpolateRemoteTransform(float deltaTime)
    {
        float snapshotAge = PhotonNetwork.InRoom
            ? Mathf.Clamp((float)(PhotonNetwork.Time - networkSentAt), 0f, 0.15f)
            : 0f;
        Vector3 predictedPosition = networkPosition + networkVelocity * snapshotAge;
        Rigidbody rb = enemySetup != null ? enemySetup.Rigidbody : null;
        if (rb == null)
        {
            float positionT = 1f - Mathf.Exp(-remotePositionLerpSpeed * deltaTime);
            float rotationT = 1f - Mathf.Exp(-remoteRotationLerpSpeed * deltaTime);
            transform.position = Vector3.Lerp(transform.position, predictedPosition, positionT);
            transform.rotation = Quaternion.Slerp(transform.rotation, networkRotation, rotationT);
            return;
        }

        float distanceToTarget = Vector3.Distance(rb.position, predictedPosition);
        if (distanceToTarget > remoteTeleportDistance)
        {
            rb.position = predictedPosition;
            rb.rotation = networkRotation;
            return;
        }

        float lerpPositionT = 1f - Mathf.Exp(-remotePositionLerpSpeed * deltaTime);
        float lerpRotationT = 1f - Mathf.Exp(-remoteRotationLerpSpeed * deltaTime);
        rb.MovePosition(Vector3.Lerp(rb.position, predictedPosition, lerpPositionT));
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, networkRotation, lerpRotationT));
    }
}
