using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class InteractionTriggerSystemTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<Object> objects = new List<Object>();
    readonly Vector3 origin = new Vector3(2400, 2400, 2400);
    InteractionTriggerSystem system;
    InteractionTriggerTestTarget target;

    [SetUp] public void SetUp() { }
    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }
    [Test]
    public void ToggleChangesBothStatesAndReceivesTheActivatingPlayer()
    {
        Build(InteractionTriggerType.Lever, InteractionTriggerMode.Toggle);
        var actor = Player();
        Assert.That(system.TryUse(actor.gameObject), Is.True);
        Assert.That(system.IsActive, Is.True);
        Assert.That(target.LastPlayer, Is.SameAs(actor));
        Assert.That(system.TryUse(actor.gameObject), Is.True);
        Assert.That(system.IsActive, Is.False);
        Assert.That(target.States, Is.EqualTo(new[] { true, false }));
    }
    [Test]
    public void OnceRejectsEveryUseAfterItsFirstAcceptedActivation()
    {
        Build(InteractionTriggerType.Button, InteractionTriggerMode.Once);
        Assert.That(system.TryUse(null), Is.True);
        Assert.That(system.TryUse(null), Is.False);
        Tick();
        Assert.That(system.IsActive && system.HasBeenUsed, Is.True);
        Assert.That(target.States.Count, Is.EqualTo(1));
    }
    [Test]
    public void AlternateABHasDistinctOutputsAndEveryUseIsAnAction()
    {
        Build(InteractionTriggerType.Lever, InteractionTriggerMode.AlternateAB);
        int a = 0, b = 0;
        system.OnStateA.AddListener(() => a++); system.OnStateB.AddListener(() => b++);
        system.TryUse(null); system.TryUse(null); system.TryUse(null);
        Assert.That(a, Is.EqualTo(1)); Assert.That(b, Is.EqualTo(2));
        Assert.That(target.Actions, Is.EqualTo(3));
    }
    [Test]
    public void MomentaryDoesNotRepeatWhileHeldAndWaitsForLinkedSystem()
    {
        Build(InteractionTriggerType.Lever);
        system.TryUse(null); target.Busy = true;
        ExpirePulse(); Tick();
        Assert.That(system.IsActive, Is.True);
        Assert.That(system.TryUse(null), Is.False);
        target.Busy = false; Tick();
        Assert.That(system.IsActive, Is.False);
        Assert.That(target.Actions, Is.EqualTo(1));
        Assert.That(system.TryUse(null), Is.True);
    }
    [Test]
    public void InteractionCooldownRejectsRapidUses()
    {
        Build(InteractionTriggerType.Button, InteractionTriggerMode.Toggle);
        Set(system, "interactionCooldown", 10f);
        Assert.That(system.TryUse(null), Is.True);
        Assert.That(system.TryUse(null), Is.False);
        Set(system, "readyAt", -10000d);
        Assert.That(system.TryUse(null), Is.True);
    }
    [Test]
    public void UnavailableTargetDoesNotMoveOrEmitOutputs()
    {
        Build(InteractionTriggerType.Lever);
        target.Ready = false;
        int used = 0; system.OnUsed.AddListener(() => used++);
        Assert.That(system.TryUse(null), Is.False);
        Assert.That(system.IsActive, Is.False); Assert.That(used, Is.Zero);
    }
    [Test]
    public void PressureDeduplicatesCollidersAndWaitsUntilEveryPlayerLeaves()
    {
        Build(InteractionTriggerType.PressureButton);
        var first = Player(); Stand(first.gameObject);
        var extra = new GameObject("Extra collider"); extra.transform.SetParent(first.transform, false); extra.AddComponent<BoxCollider>().size = new Vector3(0.4f, 2, 0.4f);
        Tick(); Assert.That(system.OccupantCount, Is.EqualTo(1));
        var second = Player(); Stand(second.gameObject);
        Tick(); Assert.That(system.OccupantCount, Is.EqualTo(2));
        first.transform.position += Vector3.right * 5; Tick();
        Assert.That(system.IsActive, Is.True); Assert.That(target.Actions, Is.EqualTo(1));
        second.gameObject.SetActive(false); Tick();
        Assert.That(system.IsActive, Is.False);
    }
    [Test]
    public void PressurePlayerFilterRejectsPropsAndEnemyActors()
    {
        Build(InteractionTriggerType.PressureButton);
        var prop = Prop(); Stand(prop); var enemy = Enemy(); Stand(enemy.gameObject);
        Tick(); Assert.That(system.IsActive, Is.False);
        var player = Player(); Stand(player.gameObject); Tick();
        Assert.That(system.IsActive, Is.True); Assert.That(system.OccupantCount, Is.EqualTo(1));
    }
    [Test]
    public void PressureObjectsFilterAcceptsDynamicPropsButExcludesPlayersEnemiesAndLevelGeometry()
    {
        Build(InteractionTriggerType.PressureButton);
        Set(system, "pressureFilter", PressureTriggerFilter.Objects);
        Stand(Player().gameObject); Stand(Enemy().gameObject);
        var level = Create("Static level collider"); level.AddComponent<BoxCollider>().size = Vector3.one * 2; Stand(level);
        Tick(); Assert.That(system.IsActive, Is.False);
        Stand(Prop()); Tick(); Assert.That(system.IsActive, Is.True); Assert.That(system.OccupantCount, Is.EqualTo(1));
    }
    [Test]
    public void PressureEnemyFilterRejectsPlayersAndObjects()
    {
        Build(InteractionTriggerType.PressureButton); Set(system, "pressureFilter", PressureTriggerFilter.Enemies);
        Stand(Player().gameObject); Stand(Prop()); Tick(); Assert.That(system.IsActive, Is.False);
        Stand(Enemy().gameObject); Tick(); Assert.That(system.OccupantCount, Is.EqualTo(1));
    }
    [Test]
    public void PressureAnythingCombinesActorCategoriesWithoutCountingTheFloor()
    {
        Build(InteractionTriggerType.PressureButton); Set(system, "pressureFilter", PressureTriggerFilter.Anything);
        Stand(Player().gameObject); Stand(Enemy().gameObject); Stand(Prop()); Tick();
        Assert.That(system.OccupantCount, Is.EqualTo(3)); Assert.That(target.Actions, Is.EqualTo(1));
    }
    [Test]
    public void PressureSpecificObjectMatchesItsChildCollidersAndIgnoresOtherProps()
    {
        Build(InteractionTriggerType.PressureButton); Set(system, "pressureFilter", PressureTriggerFilter.SpecificObject);
        var selected = Create("Specific object"); Set(system, "specificObject", selected);
        Stand(Prop()); Tick(); Assert.That(system.IsActive, Is.False);
        var child = new GameObject("Collider child"); child.transform.SetParent(selected.transform, false);
        child.AddComponent<BoxCollider>().size = new Vector3(0.4f, 2, 0.4f); Stand(selected); Tick();
        Assert.That(system.IsActive, Is.True); Assert.That(system.OccupantCount, Is.EqualTo(1));
    }
    [Test]
    public void FeetBelowOrAboveSurfaceAndExcludedLayersDoNotPress()
    {
        Build(InteractionTriggerType.PressureButton);
        var actor = Player(); Stand(actor.gameObject); actor.transform.position += Vector3.up; Tick();
        Assert.That(system.IsActive, Is.False);
        actor.transform.position -= Vector3.up * 2; Tick(); Assert.That(system.IsActive, Is.False);
        Stand(actor.gameObject); Set(system, "occupancyMask", (LayerMask)0); Tick(); Assert.That(system.IsActive, Is.False);
    }
    [Test]
    public void EmptyPressureWaitsForBusyTargetAndUsesWorldDistanceWithScaledParent()
    {
        Build(InteractionTriggerType.PressureButton);
        var parent = Create("Scaled parent"); parent.transform.localScale = new Vector3(2, 3, 2);
        system.transform.SetParent(parent.transform, true);
        float rest = system.Mechanism.transform.position.y;
        var player = Player(); Stand(player.gameObject); Tick();
        Assert.That(system.Mechanism.transform.position.y, Is.EqualTo(rest - 0.04f).Within(0.0005f));
        target.Busy = true; player.transform.position += Vector3.right * 5; Tick(); Assert.That(system.IsActive, Is.True);
        target.Busy = false; Tick(); Assert.That(system.IsActive, Is.False);
        Assert.That(system.Mechanism.transform.position.y, Is.EqualTo(rest).Within(0.0005f));
    }
    [Test]
    public void PuzzleOwnsTrapCooldownAndOnlyFailureStartsFire()
    {
        Build(InteractionTriggerType.Lever);
        var puzzle = Create("Puzzle").AddComponent<StatuePuzzleController>(); Set(system, "linkedSystem", puzzle);
        var trapRoot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FlamethrowerTrapAuthoringUtility.PrefabPath)); objects.Add(trapRoot);
        trapRoot.transform.position = origin + Vector3.forward * 50;
        var trap = trapRoot.GetComponent<FlamethrowerTrapController>();
        Set(trap, "localOnly", true); Set(trap, "damagePerSecond", 0f); Set(trap, "cooldown", 100f);
        Set(puzzle, "activationCooldownSources", new[] { trap }); puzzle.OnPuzzleFailed.AddListener(trap.TriggerTrap);
        var player = Player(); system.TryUse(player.gameObject); ExpirePulse(); Tick();
        Assert.That(puzzle.SubmittingPlayer, Is.SameAs(player)); Assert.That(trap.IsCycleActive && puzzle.IsTriggerBusy && system.IsActive, Is.True);
        Set(trap, "activationTime", -10000d); Tick(); Assert.That(system.IsActive, Is.False);
        Assert.That(puzzle.IsTriggerBusy, Is.False);
    }
    [Test]
    public void DoorTargetCanFollowToggleStateAndUnlock()
    {
        Build(InteractionTriggerType.Lever, InteractionTriggerMode.Toggle);
        var door = Create("Door").AddComponent<DoorController>();
        Set(door, "startupState", DoorController.DoorStartupState.StartsClosed);
        Set(door, "prototypeLocalOnly", true); Set(door, "interactionCooldown", 0f);
        typeof(DoorController).GetMethod("Awake", Private).Invoke(door, null);
        var receiver = door.gameObject.AddComponent<DoorInteractionTriggerTarget>(); Set(receiver, "door", door); Set(system, "linkedSystem", receiver);
        Set(system, "waitForLinkedSystem", false);
        system.TryUse(null); Assert.That(door.IsOpen, Is.True);
        system.TryUse(null); Assert.That(door.IsOpen, Is.False);
        Set(door, "isLocked", true); Set(receiver, "action", DoorTriggerAction.Unlock);
        system.TryUse(null); Assert.That(door.IsLocked, Is.False);
    }
    [Test]
    public void NetworkRestoreRejectsStaleInvalidStateAndDoesNotReplayGameplay()
    {
        Build(InteractionTriggerType.Lever, InteractionTriggerMode.Once);
        var apply = typeof(InteractionTriggerSystem).GetMethod("ApplyNetworkState", Private);
        apply.Invoke(system, new object[] { new object[] { 4, true, true, 1d, 1d, true }, true });
        Assert.That(system.IsActive && system.HasBeenUsed, Is.True); Assert.That(target.Actions, Is.Zero);
        apply.Invoke(system, new object[] { new object[] { 3, false, false, 1d, 1d, true }, false });
        apply.Invoke(system, new object[] { new object[] { 5, false, false, double.NaN, 1d, true }, false });
        Assert.That(system.IsActive, Is.True); Assert.That(system.TryUse(null), Is.False);
    }
    [Test]
    public void PrefabCategoriesAreTypedAndPressureHasNoClickInteractorOrPuzzleReference()
    {
        foreach (InteractionTriggerType type in Enum.GetValues(typeof(InteractionTriggerType)))
        {
            var prefabs = InteractionTriggerAuthoringUtility.FindPrefabs(type); Assert.That(prefabs, Is.Not.Empty);
            foreach (var prefab in prefabs)
            {
                var mechanism = prefab.GetComponent<InteractionTriggerMechanism>(); Assert.That(mechanism.Type, Is.EqualTo(type));
                Assert.That(prefab.GetComponent<AudioSource>().playOnAwake, Is.False);
                Assert.That(prefab.GetComponent<InteractionTriggerSystem>(), Is.Null);
                foreach (var component in prefab.GetComponents<MonoBehaviour>())
                    Assert.That(component.GetType().Name, Is.Not.EqualTo("PressureButtonController"));
                Assert.That(prefab.GetComponent<InteractionTriggerInteractable>() != null, Is.EqualTo(type != InteractionTriggerType.PressureButton));
            }
        }
    }
    [Test]
    public void AuthoringBuildIsIdempotentAndChangingCategoryReplacesOnlyItsMechanism()
    {
        var root = Create("Authoring root"); system = root.AddComponent<InteractionTriggerSystem>();
        var untouched = new GameObject("User child"); untouched.transform.SetParent(root.transform, false);
        InteractionTriggerAuthoringUtility.Set(system, "mechanismPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(InteractionTriggerAuthoringUtility.LeverPath));
        InteractionTriggerAuthoringUtility.BuildMechanism(system); var first = system.Mechanism;
        InteractionTriggerAuthoringUtility.BuildMechanism(system); Assert.That(system.Mechanism, Is.SameAs(first));
        InteractionTriggerAuthoringUtility.Set(system, "triggerType", (int)InteractionTriggerType.Button);
        InteractionTriggerAuthoringUtility.Set(system, "mechanismPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(InteractionTriggerAuthoringUtility.ButtonPath));
        InteractionTriggerAuthoringUtility.BuildMechanism(system);
        Assert.That(first == null, Is.True); Assert.That(system.Mechanism.Type, Is.EqualTo(InteractionTriggerType.Button));
        Assert.That(untouched != null, Is.True); Assert.That(root.GetComponentsInChildren<InteractionTriggerMechanism>().Length, Is.EqualTo(1));
    }
    [Test]
    public void InitialBDoesNotEmitActionsAndMomentaryIgnoresHiddenStartState()
    {
        Build(InteractionTriggerType.Lever, InteractionTriggerMode.AlternateAB);
        Set(system, "startsActive", true); Set(system, "initialized", false);
        typeof(InteractionTriggerSystem).GetMethod("Initialize", Private).Invoke(system, null);
        Assert.That(system.IsActive, Is.True); Assert.That(target.Actions, Is.Zero);
        Set(system, "activationMode", InteractionTriggerMode.Momentary); Set(system, "initialized", false);
        typeof(InteractionTriggerSystem).GetMethod("Initialize", Private).Invoke(system, null);
        Assert.That(system.IsActive, Is.False);
    }
    [Test]
    public void WrongCategoryAndNonTargetComponentsCannotActivate()
    {
        Build(InteractionTriggerType.Lever);
        Set(system, "triggerType", InteractionTriggerType.Button); Assert.That(system.TryUse(null), Is.False);
        Set(system, "triggerType", InteractionTriggerType.Lever); Set(system, "linkedSystem", Create("Wrong target").AddComponent<DoorSignalSource>());
        Assert.That(system.TryUse(null), Is.False);
    }
    private void Build(InteractionTriggerType type, InteractionTriggerMode mode = InteractionTriggerMode.Momentary)
    {
        var root = Create("Interaction Trigger System"); root.SetActive(false);
        system = root.AddComponent<InteractionTriggerSystem>();
        Set(system, "triggerType", type); Set(system, "activationMode", mode); Set(system, "localOnly", true); Set(system, "interactionCooldown", 0f);
        var prefab = InteractionTriggerAuthoringUtility.FindPrefabs(type)[0];
        var mechanism = Object.Instantiate(prefab, root.transform, false).GetComponent<InteractionTriggerMechanism>();
        Set(system, "mechanism", mechanism);
        target = Create("Target").AddComponent<InteractionTriggerTestTarget>(); Set(system, "linkedSystem", target);
        root.SetActive(true); typeof(InteractionTriggerSystem).GetMethod("Initialize", Private).Invoke(system, null);
    }
    private void Tick()
    {
        Physics.SyncTransforms(); typeof(InteractionTriggerSystem).GetMethod("RefreshTrigger", Private).Invoke(system, new object[] { 1f });
    }
    private void ExpirePulse() => Set(system, "changedAt", -10000d);
    private PlayerHealth Player()
    {
        var player = Create("Player").AddComponent<PlayerHealth>(); player.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.4f, 2, 0.4f);
        typeof(PlayerHealth).GetMethod("Awake", Private).Invoke(player, null); return player;
    }
    private EnemyHealth Enemy()
    {
        var enemy = Create("Enemy").AddComponent<EnemyHealth>(); enemy.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.4f, 2, 0.4f);
        typeof(EnemyHealth).GetMethod("Awake", Private).Invoke(enemy, null); return enemy;
    }
    private GameObject Prop()
    {
        var prop = Create("Prop"); prop.AddComponent<BoxCollider>().size = new Vector3(0.4f, 2, 0.4f); prop.AddComponent<Rigidbody>().useGravity = false; return prop;
    }
    private void Stand(GameObject actor)
    {
        Physics.SyncTransforms(); float top = system.Mechanism.PressureSurface.bounds.max.y;
        actor.transform.position = new Vector3(origin.x, top + 1.01f, origin.z);
    }
    private GameObject Create(string name)
    {
        var result = new GameObject(name); result.transform.position = origin; objects.Add(result); return result;
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
}

public sealed class InteractionTriggerTestTarget : MonoBehaviour, IInteractionTriggerTarget
{
    public bool Ready = true, Busy;
    public int Actions;
    public PlayerHealth LastPlayer;
    public readonly List<bool> States = new List<bool>();
    public bool CanTrigger => Ready;
    public bool IsTriggerBusy => Busy;
    public void ReceiveTrigger(InteractionTriggerContext context)
    {
        States.Add(context.IsActive); LastPlayer = context.Player;
        if (context.TriggersAction) Actions++;
    }
}
