using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("World/Doors/Door Signal Trigger Zone")]
public class DoorSignalTriggerZone : MonoBehaviour
{
    [SerializeField] private DoorSignalSource signalSource;
    [SerializeField] private bool activateOnEnter = true;
    [SerializeField] private bool deactivateOnExit = true;

    private readonly HashSet<int> localPlayersInside = new HashSet<int>();

    private void Reset()
    {
        if (signalSource == null)
            signalSource = GetComponent<DoorSignalSource>();

        EnsureTriggerCollider();
    }

    private void OnValidate()
    {
        if (signalSource == null)
            signalSource = GetComponent<DoorSignalSource>();

        EnsureTriggerCollider();
    }

    private void OnDisable()
    {
        localPlayersInside.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!TryGetLocalPlayerKey(other, out int playerKey))
            return;

        if (!localPlayersInside.Add(playerKey))
            return;

        if (activateOnEnter && localPlayersInside.Count == 1)
            signalSource?.RequestSetSignalState(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryGetLocalPlayerKey(other, out int playerKey))
            return;

        if (!localPlayersInside.Remove(playerKey))
            return;

        if (deactivateOnExit && localPlayersInside.Count == 0)
            signalSource?.RequestSetSignalState(false);
    }

    private bool TryGetLocalPlayerKey(Collider other, out int playerKey)
    {
        playerKey = 0;
        if (other == null)
            return false;

        PlayerSetup playerSetup = other.GetComponentInParent<PlayerSetup>();
        if (playerSetup == null)
            return false;

        PhotonView playerPhotonView = playerSetup.GetComponent<PhotonView>();
        if (playerPhotonView != null)
        {
            if (!playerPhotonView.IsMine)
                return false;

            playerKey = playerPhotonView.ViewID != 0
                ? playerPhotonView.ViewID
                : playerSetup.GetHashCode();
            return true;
        }

        playerKey = playerSetup.GetHashCode();
        return true;
    }

    private void EnsureTriggerCollider()
    {
        Collider triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null && !triggerCollider.isTrigger)
            triggerCollider.isTrigger = true;
    }
}
