using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class ChandelierPrefabTests
{
    [Test]
    public void Chandelier_UsesOneInteractionForAllCandles()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WK_Pack/Snaps/Objects/Chandelier.prefab");
        Assert.That(prefab, Is.Not.Null);

        Transform vfx = prefab.transform.Find("VFX");
        Assert.That(vfx, Is.Not.Null);
        Assert.That(vfx.gameObject.activeSelf, Is.False);

        int flameCount = 0;
        int smokeCount = 0;
        foreach (Transform child in vfx)
        {
            if (child.name.StartsWith("Flame_"))
                flameCount++;
            else if (child.name.StartsWith("Smoke_"))
                smokeCount++;
        }

        Assert.That(flameCount, Is.EqualTo(8));
        Assert.That(smokeCount, Is.EqualTo(8));

        TorchFlameController flame = prefab.GetComponent<TorchFlameController>();
        WallTorchStateController state = prefab.GetComponent<WallTorchStateController>();
        WallTorchInteractable interactable = prefab.GetComponent<WallTorchInteractable>();
        ReactionSignalReceiver receiver = prefab.GetComponent<ReactionSignalReceiver>();
        ReactionSignalEmitter emitter = prefab.GetComponent<ReactionSignalEmitter>();
        PlayerInteractionReactionSignalBridge bridge = prefab.GetComponent<PlayerInteractionReactionSignalBridge>();
        Assert.That(flame, Is.Not.Null);
        Assert.That(state, Is.Not.Null);
        Assert.That(interactable, Is.Not.Null);
        Assert.That(receiver, Is.Not.Null);
        Assert.That(emitter, Is.Not.Null);
        Assert.That(bridge, Is.Not.Null);

        Light light = vfx.GetComponentInChildren<Light>(true);
        AudioSource audio = prefab.transform.Find("Audio Source")?.GetComponent<AudioSource>();
        Transform prompt = prefab.transform.Find("Prompt_Pivot");
        BoxCollider trigger = prefab.transform.Find("InteractionTrigger")?.GetComponent<BoxCollider>();
        Assert.That(light, Is.Not.Null);
        Assert.That(audio, Is.Not.Null);
        Assert.That(audio.playOnAwake, Is.False);
        Assert.That(prompt, Is.Not.Null);
        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger.isTrigger, Is.True);
        Assert.That(prefab.GetComponent<BoxCollider>().isTrigger, Is.False);

        SerializedObject flameData = new SerializedObject(flame);
        Assert.That(flameData.FindProperty("flameRoot").objectReferenceValue, Is.SameAs(vfx.gameObject));
        Assert.That(flameData.FindProperty("flameLight").objectReferenceValue, Is.SameAs(light));
        Assert.That(flameData.FindProperty("litLoopAudioSource").objectReferenceValue, Is.SameAs(audio));
        Assert.That(flameData.FindProperty("litLoopAudioCue").objectReferenceValue, Is.Not.Null);
        Assert.That(new SerializedObject(state).FindProperty("flameController").objectReferenceValue, Is.SameAs(flame));

        SerializedObject interactionData = new SerializedObject(interactable);
        Assert.That(interactionData.FindProperty("stateController").objectReferenceValue, Is.SameAs(state));
        Assert.That(interactionData.FindProperty("interactionPromptPivot").objectReferenceValue, Is.SameAs(prompt));
        Assert.That(interactionData.FindProperty("requireLitTorchInHand").boolValue, Is.True);

        SerializedObject bridgeData = new SerializedObject(bridge);
        Assert.That(bridgeData.FindProperty("targetInteractable").objectReferenceValue, Is.SameAs(interactable));
        Assert.That(bridgeData.FindProperty("signalEmitter").objectReferenceValue, Is.SameAs(emitter));
        Assert.That(bridgeData.FindProperty("signalReceiver").objectReferenceValue, Is.SameAs(receiver));

        SerializedProperty signals = new SerializedObject(receiver).FindProperty("signalEntries");
        Assert.That(signals.arraySize, Is.EqualTo(1));
        SerializedProperty litSignal = signals.GetArrayElementAtIndex(0);
        Assert.That(litSignal.FindPropertyRelative("signalId").stringValue, Is.EqualTo("Lit"));
        Assert.That(litSignal.FindPropertyRelative("audioCue").objectReferenceValue, Is.Not.Null);
    }
}
