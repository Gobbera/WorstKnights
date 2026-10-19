using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class PlayerPickupInteractorFocusTests
{
    private readonly List<GameObject> createdObjects = new List<GameObject>();
    private PlayerPickupInteractor interactor;

    [SetUp]
    public void SetUp()
    {
        GameObject player = Create("Player");
        interactor = player.AddComponent<PlayerPickupInteractor>();
        Camera camera = Create("FP_Camera").AddComponent<Camera>();
        camera.transform.SetParent(player.transform);
        typeof(PlayerPickupInteractor)
            .GetField("interactionCamera", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(interactor, camera);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
                Object.DestroyImmediate(createdObjects[i]);
        }

        createdObjects.Clear();
    }

    [Test]
    public void VisiblePickupInDrawer_TakesFocusBeforeDrawer()
    {
        PickupFocusTestInteractable drawer = CreateDrawer(new Vector3(0.2f, 0f, 1.4f));
        WorldPickupItem item = CreatePickup(drawer.transform, 2f);

        Assert.That(ResolvePickup(), Is.SameAs(item));
        Assert.That(ResolveInteractable(), Is.Null);
    }

    [Test]
    public void UnavailablePickup_LeavesFocusOnDrawer()
    {
        PickupFocusTestInteractable drawer = CreateDrawer(new Vector3(0.2f, 0f, 1.4f));
        WorldPickupItem item = CreatePickup(drawer.transform, 2f);
        item.SetPickupAvailable(false);

        Assert.That(ResolvePickup(), Is.Null);
        Assert.That(ResolveInteractable(), Is.SameAs(drawer));
    }

    [Test]
    public void SolidDrawerFront_BlocksPickupFocus()
    {
        PickupFocusTestInteractable drawer = CreateDrawer(new Vector3(0f, 0f, 1.4f));
        CreatePickup(drawer.transform, 2f);

        Assert.That(ResolvePickup(), Is.Null);
        Assert.That(ResolveInteractable(), Is.SameAs(drawer));
    }

    [Test]
    public void SolidObstacle_BlocksSpherePickupFallback()
    {
        GameObject obstacle = Create("Obstacle");
        obstacle.transform.position = new Vector3(0f, 0f, 1.4f);
        obstacle.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.1f);
        CreatePickup(null, 2f);

        Assert.That(ResolvePickup(), Is.Null);
        Assert.That(ResolveInteractable(), Is.Null);
    }

    [Test]
    public void PickupCollider_DoesNotInheritDrawerInteraction()
    {
        PickupFocusTestInteractable drawer = CreateDrawer(new Vector3(0.2f, 0f, 1.4f));
        WorldPickupItem item = CreatePickup(drawer.transform, 2f);
        object[] args = { item.GetComponent<Collider>(), null };
        bool found = (bool)typeof(PlayerPickupInteractor)
            .GetMethod("TryGetInteractableFromCollider", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, args);

        Assert.That(found, Is.False);
        Assert.That(args[1], Is.Null);
    }

    private PickupFocusTestInteractable CreateDrawer(Vector3 colliderPosition)
    {
        GameObject drawer = Create("Drawer");
        BoxCollider collider = drawer.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.1f, 0.5f, 0.1f);
        drawer.transform.position = colliderPosition;
        return drawer.AddComponent<PickupFocusTestInteractable>();
    }

    private WorldPickupItem CreatePickup(Transform parent, float worldZ)
    {
        GameObject pickup = Create("Pickup");
        if (parent != null)
            pickup.transform.SetParent(parent);

        pickup.transform.position = new Vector3(0f, 0f, worldZ);
        pickup.AddComponent<BoxCollider>().size = new Vector3(0.25f, 0.25f, 0.25f);
        WorldPickupItem item = pickup.AddComponent<WorldPickupItem>();
        Physics.SyncTransforms();
        return item;
    }

    private WorldPickupItem ResolvePickup()
    {
        object focus = ResolveFocus();
        return (WorldPickupItem)focus.GetType().GetProperty("Pickup").GetValue(focus);
    }

    private IPlayerInteractable ResolveInteractable()
    {
        object focus = ResolveFocus();
        return (IPlayerInteractable)focus.GetType().GetProperty("Interactable").GetValue(focus);
    }

    private object ResolveFocus()
    {
        Physics.SyncTransforms();
        return typeof(PlayerPickupInteractor)
            .GetMethod("ResolveInteractionFocus", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(interactor, null);
    }

    private GameObject Create(string name)
    {
        GameObject gameObject = new GameObject(name);
        createdObjects.Add(gameObject);
        return gameObject;
    }
}

public sealed class PickupFocusTestInteractable : MonoBehaviour, IPlayerInteractable
{
    public int InteractionPriority => 100;

    public bool TryInteract(PlayerPickupInteractor interactor) => true;
}
