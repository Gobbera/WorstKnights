using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ReactionSignalSetupUtility
{
    public static bool CanSetupDestructible(GameObject targetObject)
    {
        return targetObject != null && targetObject.GetComponent<DestructibleObjectController>() != null;
    }

    public static bool CanSetupDoor(GameObject targetObject)
    {
        return targetObject != null && targetObject.GetComponent<DoorController>() != null;
    }

    public static bool CanSetupImpact(GameObject targetObject)
    {
        return GetPrimaryCollider(targetObject) != null;
    }

    public static bool CanSetupTriggerVolume(GameObject targetObject)
    {
        return GetPrimaryCollider(targetObject) != null;
    }

    public static bool CanSetupCollision(GameObject targetObject)
    {
        return GetPrimaryCollider(targetObject) != null;
    }

    public static void SetupDestructible(GameObject targetObject)
    {
        ExecuteSetup(targetObject, "Setup Reaction Destructible", receiver =>
        {
            EnsureComponent<ReactionSignalEmitter>(targetObject);
            EnsureComponent<DestructibleReactionSignalBridge>(targetObject);
            AddSignalEntryIfMissing(receiver, "Damaged");
            AddSignalEntryIfMissing(receiver, "Destroyed");
        });
    }

    public static void SetupDoor(GameObject targetObject)
    {
        ExecuteSetup(targetObject, "Setup Reaction Door", receiver =>
        {
            EnsureComponent<ReactionSignalEmitter>(targetObject);
            EnsureComponent<DoorReactionSignalBridge>(targetObject);
            AddSignalEntryIfMissing(receiver, "Opened");
            AddSignalEntryIfMissing(receiver, "Closed");
            AddSignalEntryIfMissing(receiver, "Locked");
            AddSignalEntryIfMissing(receiver, "Unlocked");
        });
    }

    public static void SetupImpact(GameObject targetObject)
    {
        ExecuteSetup(targetObject, "Setup Reaction Impact", receiver =>
        {
            EnsureComponent<ReactionSignalEmitter>(targetObject);

            bool hasImpactSource = targetObject.GetComponent<ImpactReactionSignalBridge>() != null
                || targetObject.GetComponent<MeleeImpactReactionReceiver>() != null;
            if (!hasImpactSource)
                Undo.AddComponent<ImpactReactionSignalBridge>(targetObject);

            AddSignalEntryIfMissing(receiver, "Hit");
        });
    }

    public static void SetupTriggerVolume(GameObject targetObject)
    {
        ExecuteSetup(targetObject, "Setup Reaction Trigger Volume", receiver =>
        {
            EnsureComponent<ReactionSignalEmitter>(targetObject);
            TriggerVolumeReactionSignalBridge triggerEmitter = EnsureComponent<TriggerVolumeReactionSignalBridge>(targetObject);

            Collider collider = GetPrimaryCollider(targetObject);
            if (collider != null && !collider.isTrigger)
            {
                Undo.RecordObject(collider, "Mark Collider As Trigger");
                collider.isTrigger = true;
                EditorUtility.SetDirty(collider);
            }

            AddSignalEntryIfMissing(receiver, triggerEmitter.EnteredSignalId);
            AddSignalEntryIfMissing(receiver, triggerEmitter.ExitedSignalId);
        });
    }

    public static void SetupCollision(GameObject targetObject)
    {
        ExecuteSetup(targetObject, "Setup Reaction Collision", receiver =>
        {
            EnsureComponent<ReactionSignalEmitter>(targetObject);
            CollisionReactionSignalBridge collisionEmitter = EnsureComponent<CollisionReactionSignalBridge>(targetObject);
            AddSignalEntryIfMissing(receiver, collisionEmitter.CollisionEnterSignalId);
        });
    }

    public static List<string> CollectDetectedSignals(GameObject targetObject)
    {
        HashSet<string> signalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (targetObject == null)
            return new List<string>();

        ImpactReactionSignalBridge impactRelay = targetObject.GetComponent<ImpactReactionSignalBridge>();
        if (impactRelay != null && !string.IsNullOrWhiteSpace(impactRelay.SignalId))
            signalIds.Add(impactRelay.SignalId.Trim());

        MeleeImpactReactionReceiver legacyImpactRelay = targetObject.GetComponent<MeleeImpactReactionReceiver>();
        if (legacyImpactRelay != null && !string.IsNullOrWhiteSpace(legacyImpactRelay.SignalId))
            signalIds.Add(legacyImpactRelay.SignalId.Trim());

        DestructibleReactionSignalBridge destructibleBridge = targetObject.GetComponent<DestructibleReactionSignalBridge>();
        if (destructibleBridge != null)
        {
            TryAddSignal(signalIds, destructibleBridge.DamagedSignalId);
            TryAddSignal(signalIds, destructibleBridge.DestroyedSignalId);
        }

        DoorReactionSignalBridge doorBridge = targetObject.GetComponent<DoorReactionSignalBridge>();
        if (doorBridge != null)
        {
            TryAddSignal(signalIds, doorBridge.OpenedSignalId);
            TryAddSignal(signalIds, doorBridge.ClosedSignalId);
            TryAddSignal(signalIds, doorBridge.LockedSignalId);
            TryAddSignal(signalIds, doorBridge.UnlockedSignalId);
        }

        TriggerVolumeReactionSignalBridge triggerEmitter = targetObject.GetComponent<TriggerVolumeReactionSignalBridge>();
        if (triggerEmitter != null && triggerEmitter.TargetMode == ReactionSignalTargetMode.SelfReceiver)
        {
            TryAddSignal(signalIds, triggerEmitter.EnteredSignalId);
            TryAddSignal(signalIds, triggerEmitter.StayedSignalId);
            TryAddSignal(signalIds, triggerEmitter.ExitedSignalId);
        }

        CollisionReactionSignalBridge collisionEmitter = targetObject.GetComponent<CollisionReactionSignalBridge>();
        if (collisionEmitter != null && collisionEmitter.TargetMode == ReactionSignalTargetMode.SelfReceiver)
        {
            TryAddSignal(signalIds, collisionEmitter.CollisionEnterSignalId);
            TryAddSignal(signalIds, collisionEmitter.CollisionStaySignalId);
            TryAddSignal(signalIds, collisionEmitter.CollisionExitSignalId);
        }

        return new List<string>(signalIds);
    }

    public static ReactionSignalReceiver EnsureReceiver(GameObject targetObject)
    {
        if (targetObject == null)
            return null;

        ReactionSignalReceiver existingReceiver = targetObject.GetComponent<ReactionSignalReceiver>();
        return existingReceiver != null
            ? existingReceiver
            : Undo.AddComponent<ReactionSignalReceiver>(targetObject);
    }

    private static void ExecuteSetup(GameObject targetObject, string undoName, Action<ReactionSignalReceiver> setupAction)
    {
        if (targetObject == null || setupAction == null)
            return;

        Undo.SetCurrentGroupName(undoName);
        int undoGroup = Undo.GetCurrentGroup();

        ReactionSignalReceiver receiver = EnsureReceiver(targetObject);
        if (receiver != null)
            setupAction(receiver);

        EditorUtility.SetDirty(targetObject);
        if (receiver != null)
            EditorUtility.SetDirty(receiver);

        Undo.CollapseUndoOperations(undoGroup);
    }

    private static void AddSignalEntryIfMissing(ReactionSignalReceiver receiver, string signalId)
    {
        if (receiver == null || string.IsNullOrWhiteSpace(signalId))
            return;

        SerializedObject serializedReceiver = new SerializedObject(receiver);
        SerializedProperty signalEntriesProperty = serializedReceiver.FindProperty("signalEntries");
        if (signalEntriesProperty == null)
            return;

        for (int i = 0; i < signalEntriesProperty.arraySize; i++)
        {
            SerializedProperty entryProperty = signalEntriesProperty.GetArrayElementAtIndex(i);
            string currentSignalId = entryProperty.FindPropertyRelative("signalId").stringValue;
            if (string.Equals(currentSignalId?.Trim(), signalId.Trim(), StringComparison.OrdinalIgnoreCase))
                return;
        }

        int newIndex = signalEntriesProperty.arraySize;
        signalEntriesProperty.InsertArrayElementAtIndex(newIndex);

        SerializedProperty newEntryProperty = signalEntriesProperty.GetArrayElementAtIndex(newIndex);
        newEntryProperty.FindPropertyRelative("signalId").stringValue = signalId;
        newEntryProperty.FindPropertyRelative("feedbackOrigin").objectReferenceValue = null;
        newEntryProperty.FindPropertyRelative("audioCue").objectReferenceValue = null;
        newEntryProperty.FindPropertyRelative("effectPrefab").objectReferenceValue = null;
        newEntryProperty.FindPropertyRelative("effectLifetime").floatValue = 5f;

        SerializedProperty callsProperty = newEntryProperty
            .FindPropertyRelative("onSignalReceived")
            ?.FindPropertyRelative("m_PersistentCalls.m_Calls");
        if (callsProperty != null)
            callsProperty.ClearArray();

        serializedReceiver.ApplyModifiedProperties();
    }

    private static T EnsureComponent<T>(GameObject targetObject) where T : Component
    {
        T existingComponent = targetObject.GetComponent<T>();
        return existingComponent != null
            ? existingComponent
            : Undo.AddComponent<T>(targetObject);
    }

    private static Collider GetPrimaryCollider(GameObject targetObject)
    {
        return targetObject != null ? targetObject.GetComponent<Collider>() : null;
    }

    private static void TryAddSignal(HashSet<string> signalIds, string signalId)
    {
        if (!string.IsNullOrWhiteSpace(signalId))
            signalIds.Add(signalId.Trim());
    }
}
