using System.Collections.Generic;
using UnityEngine;

public partial class DoorController
{
    private int lastPairedMotionFrame = -1;
    public DoorController PairedDoor => GetPairedDoor();

    private DoorController GetPairedDoor()
    {
        return pairedDoor != null && pairedDoor != this && pairedDoor.pairedDoor == this
            && pairedDoor.gameObject.scene == gameObject.scene ? pairedDoor : null;
    }

    private void CaptureMotionReferencePoint()
    {
        closedMotionReferencePoint = Vector3.zero;
        if (movingPart == null) return;
        var mesh = movingPart.GetComponentInChildren<MeshFilter>(true);
        if (mesh != null && mesh.sharedMesh != null)
        {
            closedMotionReferencePoint = movingPart.InverseTransformPoint(mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center));
            return;
        }
        var box = movingPart.GetComponentInChildren<BoxCollider>(true);
        if (box != null) closedMotionReferencePoint = movingPart.InverseTransformPoint(box.transform.TransformPoint(box.center));
    }

    private Vector3 GetMotionReferenceWorldPosition(float progress, int direction)
    {
        ResolveRotatePose(progress, direction, out var position, out var rotation);
        Vector3 local = position + rotation * Vector3.Scale(closedMotionReferencePoint, movingPart.localScale);
        return movingPart.parent != null ? movingPart.parent.TransformPoint(local) : local;
    }

    private Vector3 GetClosedDoorWorldNormal()
    {
        Vector3 normal = closedLocalRotation * Vector3.forward;
        return movingPart.parent != null ? movingPart.parent.TransformDirection(normal).normalized : normal.normalized;
    }

    private int ResolvePairedRotationDirection(DoorController peer, int direction)
    {
        int normalized = NormalizeOpenRotationDirection(direction);
        if (motionMode != DoorMotionMode.Rotate || peer.motionMode != DoorMotionMode.Rotate || movingPart == null || peer.movingPart == null) return normalized;
        Vector3 normal = GetClosedDoorWorldNormal();
        Vector3 origin = GetMotionReferenceWorldPosition(0f, normalized);
        float side = Vector3.Dot(GetMotionReferenceWorldPosition(1f, normalized) - origin, normal);
        if (Mathf.Abs(side) < 0.0001f) return normalized;
        Vector3 openingSide = normal * Mathf.Sign(side);
        Vector3 peerOrigin = peer.GetMotionReferenceWorldPosition(0f, normalized);
        float forward = Vector3.Dot(peer.GetMotionReferenceWorldPosition(1f, 1) - peerOrigin, openingSide);
        float reverse = Vector3.Dot(peer.GetMotionReferenceWorldPosition(1f, -1) - peerOrigin, openingSide);
        return Mathf.Abs(forward - reverse) > 0.0001f ? (forward > reverse ? 1 : -1) : normalized;
    }

    private void UpdatePairedDoorMotion()
    {
        var peer = GetPairedDoor();
        if (peer == null || !peer.isActiveAndEnabled) { UpdateDoorMotion(); return; }
        // Whichever peer updates first advances both once in this frame. There is no fixed driver.
        if (lastPairedMotionFrame == Time.frameCount && peer.lastPairedMotionFrame == Time.frameCount) return;
        lastPairedMotionFrame = peer.lastPairedMotionFrame = Time.frameCount;
        UpdateDoorMotion();
        if (peer != null) peer.UpdateDoorMotion();
    }

    // Door traversal and obstruction checks consider both equivalent peers, excluding their frame.
    public Collider[] GetMovingPartColliders()
    {
        var own = GetLocalMovingPartColliders();
        var peer = GetPairedDoor();
        if (peer == null) return own;
        var result = new List<Collider>(own);
        foreach (var collider in peer.GetLocalMovingPartColliders())
            if (!result.Contains(collider)) result.Add(collider);
        return result.ToArray();
    }

    private Collider[] GetLocalMovingPartColliders()
    {
        var root = movingPart != null ? movingPart : transform;
        var colliders = root.GetComponentsInChildren<Collider>(true);
        return colliders.Length > 0 || root == transform ? colliders : GetComponentsInChildren<Collider>(true);
    }
}
