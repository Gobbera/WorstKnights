using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Doors/Door Enemy Obstruction Sensor")]
public sealed class DoorEnemyObstructionSensor : MonoBehaviour
{
    private enum RecoveryState
    {
        None,
        WaitingForClose,
        WaitingForOpen
    }

    private sealed class ContactRecord
    {
        public EnemyTraversalController TraversalController;
        public EnemySetup EnemySetup;
        public readonly List<Collider> Colliders = new List<Collider>(4);
        public float OpenDoorContactStartTime;
        public float ClosedDoorContactStartTime;
        public float ClosedDoorStuckStartTime;
        public Vector3 ClosedDoorStuckAnchorPosition;
        public bool HasClosedDoorStuckAnchorPosition;
    }

    [Header("References")]
    [SerializeField] private DoorController doorController;
    [SerializeField] private Collider triggerCollider;

    [Header("Detection")]
    [SerializeField] private LayerMask enemyLayers = ~0;
    [SerializeField] [Min(0f)] private float requiredContactDuration = 0.45f;
    [SerializeField] private bool requireEnemyDoorPermission = true;
    [SerializeField] private bool requireEnemyDestination = true;
    [SerializeField] private bool ignoreEnemyDuringTraversal = true;
    [SerializeField] private bool requireSolidDoorContact = true;
    [SerializeField] [Min(0f)] private float solidDoorContactDistance = 0.08f;
    [SerializeField] [Min(0f)] private float closedDoorContactDuration = 0.25f;
    [SerializeField] private bool allowClosedDoorStuckContact = true;
    [SerializeField] [Min(0f)] private float closedDoorStuckDuration = 0.6f;
    [SerializeField] [Min(0f)] private float closedDoorStuckMoveDistance = 0.12f;

    [Header("Recovery")]
    [SerializeField] private bool allowAutomaticRecovery;
    [SerializeField] private bool allowClosedDoorStuckAutoOpen = true;
    [SerializeField] private bool closeBeforeReopening = true;
    [SerializeField] [Min(0f)] private float doorActionTimeout = 4f;
    [SerializeField] [Min(0f)] private float actionCooldown = 2f;

    private readonly List<ContactRecord> contacts = new List<ContactRecord>(4);
    private RecoveryState recoveryState;
    private EnemyTraversalController activeEnemy;
    private Vector3 activeInteractorPosition;
    private float recoveryDeadline = float.PositiveInfinity;
    private float nextAllowedActionTime = float.NegativeInfinity;

    public bool HasPersistentObstructingContact(EnemyTraversalController traversalController)
    {
        if (!CanDoorReactToContact() || traversalController == null)
            return false;

        for (int i = 0; i < contacts.Count; i++)
        {
            ContactRecord contact = contacts[i];
            if (contact == null || contact.TraversalController != traversalController)
                continue;

            return CanUseContact(contact)
                && IsContactCurrentlyObstructing(contact)
                && HasOpenDoorContactMatured(contact);
        }

        return false;
    }

    public bool HasPersistentClosedDoorContact(EnemyTraversalController traversalController)
    {
        if (!CanDoorReportClosedContact() || traversalController == null)
            return false;

        for (int i = 0; i < contacts.Count; i++)
        {
            ContactRecord contact = contacts[i];
            if (contact == null || contact.TraversalController != traversalController)
                continue;

            return CanUseContact(contact) && HasClosedDoorBlockingEvidence(contact);
        }

        return false;
    }

    private void Reset()
    {
        ResolveReferences();

        if (triggerCollider != null)
            triggerCollider.isTrigger = true;
    }

    private void OnValidate()
    {
        requiredContactDuration = Mathf.Max(0f, requiredContactDuration);
        solidDoorContactDistance = Mathf.Max(0f, solidDoorContactDistance);
        closedDoorContactDuration = Mathf.Max(0f, closedDoorContactDuration);
        closedDoorStuckDuration = Mathf.Max(0f, closedDoorStuckDuration);
        closedDoorStuckMoveDistance = Mathf.Max(0f, closedDoorStuckMoveDistance);
        doorActionTimeout = Mathf.Max(0f, doorActionTimeout);
        actionCooldown = Mathf.Max(0f, actionCooldown);

        ResolveReferences();
        if (triggerCollider != null)
            triggerCollider.isTrigger = true;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnDisable()
    {
        contacts.Clear();
        ClearRecovery();
    }

    private void Update()
    {
        CleanupContacts();
        RefreshContactTimers();
        UpdateRecovery();
        TryOpenClosedDoorForStuckContact();

        if (!allowAutomaticRecovery)
            return;

        TryStartAutomaticRecovery();
    }

    private void OnTriggerEnter(Collider other)
    {
        RegisterContact(other);
    }

    private void OnTriggerStay(Collider other)
    {
        RegisterContact(other);
    }

    private void OnTriggerExit(Collider other)
    {
        UnregisterContact(other);
    }

    private void TryStartAutomaticRecovery()
    {
        if (recoveryState != RecoveryState.None
            || Time.time < nextAllowedActionTime
            || !CanDoorReactToContact())
        {
            return;
        }

        for (int i = 0; i < contacts.Count; i++)
        {
            ContactRecord contact = contacts[i];
            if (!CanUseContact(contact) || !IsContactCurrentlyObstructing(contact))
            {
                ResetContactTimer(contact);
                continue;
            }

            if (!HasOpenDoorContactMatured(contact))
                continue;

            if (BeginRecovery(contact))
                return;
        }
    }

    private void TryOpenClosedDoorForStuckContact()
    {
        if (!allowClosedDoorStuckAutoOpen
            || recoveryState != RecoveryState.None
            || Time.time < nextAllowedActionTime
            || !CanDoorReportClosedContact())
        {
            return;
        }

        for (int i = 0; i < contacts.Count; i++)
        {
            ContactRecord contact = contacts[i];
            if (!CanUseContact(contact) || !HasClosedDoorStuckContactMatured(contact))
                continue;

            activeEnemy = contact.TraversalController;
            activeInteractorPosition = ResolveContactPosition(contact);
            nextAllowedActionTime = Time.time + Mathf.Max(0f, actionCooldown);
            ResetContactTimer(contact);

            if (!doorController.RequestOpenByEnemy(activeInteractorPosition))
            {
                ClearRecovery();
                return;
            }

            recoveryState = DoorIsReadyOpen() ? RecoveryState.None : RecoveryState.WaitingForOpen;
            recoveryDeadline = Time.time + Mathf.Max(0f, doorActionTimeout);
            return;
        }
    }

    private void RefreshContactTimers()
    {
        bool canReportOpenContact = CanDoorReactToContact();
        bool canReportClosedContact = CanDoorReportClosedContact();
        for (int i = 0; i < contacts.Count; i++)
        {
            ContactRecord contact = contacts[i];
            bool canUseContact = CanUseContact(contact);
            bool hasSolidDoorContact = IsContactCurrentlyObstructing(contact);
            bool isInsideTrigger = IsContactInsideTrigger(contact);

            if (!canReportOpenContact || !canUseContact || !hasSolidDoorContact)
                ResetOpenDoorContactTimer(contact);

            if (!canReportClosedContact || !canUseContact || !hasSolidDoorContact)
                ResetClosedDoorContactTimer(contact);

            if (canReportClosedContact && canUseContact && isInsideTrigger)
                UpdateClosedDoorStuckTimer(contact);
            else
                ResetClosedDoorStuckTimer(contact);
        }
    }

    private void UpdateRecovery()
    {
        if (recoveryState == RecoveryState.None)
            return;

        if (doorController == null || doorController.IsLocked || Time.time > recoveryDeadline)
        {
            ClearRecovery();
            return;
        }

        activeInteractorPosition = ResolveActiveInteractorPosition();

        switch (recoveryState)
        {
            case RecoveryState.WaitingForClose:
                if (doorController.IsMoving || doorController.IsOpen)
                    return;

                if (!doorController.RequestOpenByEnemy(activeInteractorPosition) && !doorController.IsOpen)
                {
                    ClearRecovery();
                    return;
                }

                recoveryState = RecoveryState.WaitingForOpen;
                recoveryDeadline = Time.time + Mathf.Max(0f, doorActionTimeout);
                return;

            case RecoveryState.WaitingForOpen:
                if (doorController.IsMoving || !doorController.IsOpen)
                    return;

                ClearRecovery();
                return;
        }
    }

    private bool BeginRecovery(ContactRecord contact)
    {
        if (contact == null || doorController == null)
            return false;

        activeEnemy = contact.TraversalController;
        activeInteractorPosition = ResolveContactPosition(contact);
        nextAllowedActionTime = Time.time + Mathf.Max(0f, actionCooldown);
        ResetContactTimer(contact);

        if (closeBeforeReopening && doorController.RequestClose())
        {
            recoveryState = RecoveryState.WaitingForClose;
            recoveryDeadline = Time.time + Mathf.Max(0f, doorActionTimeout);
            return true;
        }

        if (doorController.RequestOpenByEnemy(activeInteractorPosition))
        {
            recoveryState = RecoveryState.WaitingForOpen;
            recoveryDeadline = Time.time + Mathf.Max(0f, doorActionTimeout);
            return true;
        }

        ClearRecovery();
        return false;
    }

    private bool CanDoorReactToContact()
    {
        return doorController != null
            && doorController.IsOpen
            && !doorController.IsMoving
            && !doorController.IsLocked;
    }

    private bool DoorIsReadyOpen()
    {
        return doorController != null
            && doorController.IsOpen
            && !doorController.IsMoving;
    }

    private bool CanDoorReportClosedContact()
    {
        return doorController != null
            && !doorController.IsOpen
            && !doorController.IsMoving
            && !doorController.IsLocked;
    }

    private bool CanUseContact(ContactRecord contact)
    {
        if (contact == null || contact.TraversalController == null)
            return false;

        EnemyTraversalController traversalController = contact.TraversalController;
        if (requireEnemyDoorPermission && !traversalController.CanOpenDoors)
            return false;

        if (ignoreEnemyDuringTraversal && traversalController.IsTraversing)
            return false;

        EnemySetup enemySetup = ResolveEnemySetup(contact);
        if (enemySetup != null && !enemySetup.HasAuthority)
            return false;

        EnemyHealth enemyHealth = enemySetup != null
            ? enemySetup.EnemyHealth
            : traversalController.GetComponent<EnemyHealth>();
        if (enemyHealth != null && !enemyHealth.IsAlive)
            return false;

        if (requireEnemyDestination)
        {
            EnemyNavigation enemyNavigation = enemySetup != null
                ? enemySetup.EnemyNavigation
                : traversalController.GetComponent<EnemyNavigation>();
            if (enemyNavigation == null || !enemyNavigation.HasDestination)
                return false;
        }

        return true;
    }

    private EnemySetup ResolveEnemySetup(ContactRecord contact)
    {
        if (contact == null)
            return null;

        if (contact.EnemySetup == null && contact.TraversalController != null)
            contact.EnemySetup = contact.TraversalController.GetComponent<EnemySetup>();

        return contact.EnemySetup;
    }

    private bool HasOpenDoorContactMatured(ContactRecord contact)
    {
        return contact != null
            && Time.time - contact.OpenDoorContactStartTime >= Mathf.Max(0f, requiredContactDuration);
    }

    private bool HasClosedDoorContactMatured(ContactRecord contact)
    {
        return contact != null
            && Time.time - contact.ClosedDoorContactStartTime >= Mathf.Max(0f, closedDoorContactDuration);
    }

    private bool HasClosedDoorBlockingEvidence(ContactRecord contact)
    {
        return (IsContactCurrentlyObstructing(contact) && HasClosedDoorContactMatured(contact))
            || HasClosedDoorStuckContactMatured(contact);
    }

    private bool HasClosedDoorStuckContactMatured(ContactRecord contact)
    {
        return allowClosedDoorStuckContact
            && contact != null
            && contact.HasClosedDoorStuckAnchorPosition
            && IsContactInsideTrigger(contact)
            && Time.time - contact.ClosedDoorStuckStartTime >= Mathf.Max(0f, closedDoorStuckDuration);
    }

    private bool IsContactCurrentlyObstructing(ContactRecord contact)
    {
        if (contact == null || contact.Colliders.Count == 0)
            return false;

        if (!requireSolidDoorContact)
            return true;

        return HasSolidDoorContact(contact);
    }

    private bool HasSolidDoorContact(ContactRecord contact)
    {
        if (doorController == null || contact == null)
            return false;

        Collider[] doorColliders = ResolveDoorSolidColliders();
        if (doorColliders == null || doorColliders.Length == 0)
            return false;

        float allowedDistance = Mathf.Max(0f, solidDoorContactDistance);
        float allowedDistanceSqr = allowedDistance * allowedDistance;
        for (int i = 0; i < contact.Colliders.Count; i++)
        {
            Collider enemyCollider = contact.Colliders[i];
            if (!CanUseSolidContactCollider(enemyCollider))
                continue;

            for (int doorIndex = 0; doorIndex < doorColliders.Length; doorIndex++)
            {
                Collider doorCollider = doorColliders[doorIndex];
                if (!CanUseDoorSolidCollider(doorCollider))
                    continue;

                if (GetApproximateColliderDistanceSqr(enemyCollider, doorCollider) <= allowedDistanceSqr)
                    return true;
            }
        }

        return false;
    }

    private bool IsContactInsideTrigger(ContactRecord contact)
    {
        if (contact == null || triggerCollider == null)
            return false;

        for (int i = 0; i < contact.Colliders.Count; i++)
        {
            Collider contactCollider = contact.Colliders[i];
            if (!CanUseSolidContactCollider(contactCollider))
                continue;

            if (contactCollider.bounds.Intersects(triggerCollider.bounds))
                return true;
        }

        return false;
    }

    private void UpdateClosedDoorStuckTimer(ContactRecord contact)
    {
        if (contact == null)
            return;

        Vector3 bodyPosition = ResolveContactPosition(contact);
        if (!contact.HasClosedDoorStuckAnchorPosition)
        {
            contact.ClosedDoorStuckAnchorPosition = bodyPosition;
            contact.ClosedDoorStuckStartTime = Time.time;
            contact.HasClosedDoorStuckAnchorPosition = true;
            return;
        }

        Vector3 planarOffset = Vector3.ProjectOnPlane(bodyPosition - contact.ClosedDoorStuckAnchorPosition, Vector3.up);
        float maxMoveDistance = Mathf.Max(0f, closedDoorStuckMoveDistance);
        if (planarOffset.sqrMagnitude <= maxMoveDistance * maxMoveDistance)
            return;

        contact.ClosedDoorStuckAnchorPosition = bodyPosition;
        contact.ClosedDoorStuckStartTime = Time.time;
    }

    private Collider[] ResolveDoorSolidColliders()
    {
        Transform colliderRoot = doorController.MovingPart != null
            ? doorController.MovingPart
            : doorController.transform;
        Collider[] doorColliders = colliderRoot.GetComponentsInChildren<Collider>(true);
        if ((doorColliders == null || doorColliders.Length == 0) && colliderRoot != doorController.transform)
            doorColliders = doorController.GetComponentsInChildren<Collider>(true);

        return doorColliders;
    }

    private bool CanUseSolidContactCollider(Collider contactCollider)
    {
        return contactCollider != null
            && contactCollider.enabled
            && contactCollider.gameObject.activeInHierarchy
            && !contactCollider.isTrigger;
    }

    private bool CanUseDoorSolidCollider(Collider doorCollider)
    {
        return doorCollider != null
            && doorCollider != triggerCollider
            && doorCollider.enabled
            && doorCollider.gameObject.activeInHierarchy
            && !doorCollider.isTrigger;
    }

    private static float GetApproximateColliderDistanceSqr(Collider a, Collider b)
    {
        if (!CanUseClosestPoint(a) || !CanUseClosestPoint(b))
            return GetBoundsDistanceSqr(a.bounds, b.bounds);

        Vector3 aCenter = a.bounds.center;
        Vector3 bCenter = b.bounds.center;

        Vector3 pointOnBFromA = b.ClosestPoint(aCenter);
        Vector3 pointOnAFromBPoint = a.ClosestPoint(pointOnBFromA);
        float distanceSqr = (pointOnAFromBPoint - pointOnBFromA).sqrMagnitude;

        Vector3 pointOnAFromB = a.ClosestPoint(bCenter);
        Vector3 pointOnBFromAPoint = b.ClosestPoint(pointOnAFromB);
        return Mathf.Min(distanceSqr, (pointOnBFromAPoint - pointOnAFromB).sqrMagnitude);
    }

    private static bool CanUseClosestPoint(Collider collider)
    {
        return collider is BoxCollider
            || collider is SphereCollider
            || collider is CapsuleCollider
            || collider is MeshCollider { convex: true };
    }

    private static float GetBoundsDistanceSqr(Bounds a, Bounds b)
    {
        float x = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
        float y = Mathf.Max(0f, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
        float z = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
        return x * x + y * y + z * z;
    }

    private void RegisterContact(Collider other)
    {
        if (!CanUseCollider(other)
            || !TryResolveEnemy(other, out EnemyTraversalController traversalController, out EnemySetup enemySetup))
        {
            return;
        }

        ContactRecord contact = FindContact(traversalController);
        if (contact == null)
        {
            contact = new ContactRecord
            {
                TraversalController = traversalController,
                EnemySetup = enemySetup,
                OpenDoorContactStartTime = Time.time,
                ClosedDoorContactStartTime = Time.time,
                ClosedDoorStuckStartTime = Time.time,
                ClosedDoorStuckAnchorPosition = traversalController.transform.position,
                HasClosedDoorStuckAnchorPosition = false
            };
            contacts.Add(contact);
        }
        else if (contact.EnemySetup == null)
        {
            contact.EnemySetup = enemySetup;
        }

        if (!contact.Colliders.Contains(other))
            contact.Colliders.Add(other);
    }

    private void UnregisterContact(Collider other)
    {
        if (other == null)
            return;

        for (int i = contacts.Count - 1; i >= 0; i--)
        {
            ContactRecord contact = contacts[i];
            if (contact == null)
            {
                contacts.RemoveAt(i);
                continue;
            }

            contact.Colliders.Remove(other);
            if (contact.Colliders.Count == 0)
                contacts.RemoveAt(i);
        }
    }

    private void CleanupContacts()
    {
        for (int i = contacts.Count - 1; i >= 0; i--)
        {
            ContactRecord contact = contacts[i];
            if (contact == null || contact.TraversalController == null)
            {
                contacts.RemoveAt(i);
                continue;
            }

            for (int colliderIndex = contact.Colliders.Count - 1; colliderIndex >= 0; colliderIndex--)
            {
                Collider contactCollider = contact.Colliders[colliderIndex];
                if (contactCollider == null || !contactCollider.enabled || !contactCollider.gameObject.activeInHierarchy)
                    contact.Colliders.RemoveAt(colliderIndex);
            }

            if (contact.Colliders.Count == 0)
                contacts.RemoveAt(i);
        }
    }

    private ContactRecord FindContact(EnemyTraversalController traversalController)
    {
        for (int i = 0; i < contacts.Count; i++)
        {
            ContactRecord contact = contacts[i];
            if (contact != null && contact.TraversalController == traversalController)
                return contact;
        }

        return null;
    }

    private bool TryResolveEnemy(
        Collider other,
        out EnemyTraversalController traversalController,
        out EnemySetup enemySetup)
    {
        traversalController = null;
        enemySetup = null;

        if (other == null)
            return false;

        traversalController = other.GetComponentInParent<EnemyTraversalController>();
        if (traversalController == null)
            return false;

        enemySetup = traversalController.GetComponent<EnemySetup>();
        return true;
    }

    private bool CanUseCollider(Collider other)
    {
        if (other == null || other == triggerCollider)
            return false;

        int layerMask = 1 << other.gameObject.layer;
        return (enemyLayers.value & layerMask) != 0;
    }

    private Vector3 ResolveContactPosition(ContactRecord contact)
    {
        if (contact != null && contact.TraversalController != null)
            return contact.TraversalController.transform.position;

        return transform.position;
    }

    private Vector3 ResolveActiveInteractorPosition()
    {
        if (activeEnemy != null)
            return activeEnemy.transform.position;

        return activeInteractorPosition;
    }

    private static void ResetContactTimer(ContactRecord contact)
    {
        if (contact != null)
        {
            contact.OpenDoorContactStartTime = Time.time;
            contact.ClosedDoorContactStartTime = Time.time;
            contact.ClosedDoorStuckStartTime = Time.time;
            contact.HasClosedDoorStuckAnchorPosition = false;
        }
    }

    private static void ResetOpenDoorContactTimer(ContactRecord contact)
    {
        if (contact != null)
            contact.OpenDoorContactStartTime = Time.time;
    }

    private static void ResetClosedDoorContactTimer(ContactRecord contact)
    {
        if (contact != null)
            contact.ClosedDoorContactStartTime = Time.time;
    }

    private static void ResetClosedDoorStuckTimer(ContactRecord contact)
    {
        if (contact != null)
        {
            contact.ClosedDoorStuckStartTime = Time.time;
            contact.HasClosedDoorStuckAnchorPosition = false;
        }
    }

    private void ClearRecovery()
    {
        recoveryState = RecoveryState.None;
        activeEnemy = null;
        activeInteractorPosition = Vector3.zero;
        recoveryDeadline = float.PositiveInfinity;
    }

    private void ResolveReferences()
    {
        if (doorController == null)
            doorController = GetComponentInParent<DoorController>();

        if (triggerCollider == null)
            triggerCollider = FindTriggerCollider();
    }

    private Collider FindTriggerCollider()
    {
        Collider[] localColliders = GetComponents<Collider>();
        for (int i = 0; i < localColliders.Length; i++)
        {
            Collider candidate = localColliders[i];
            if (candidate != null && candidate.isTrigger)
                return candidate;
        }

        return null;
    }
}
