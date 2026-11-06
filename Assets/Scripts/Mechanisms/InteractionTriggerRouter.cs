using System;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

[DisallowMultipleComponent]
[AddComponentMenu("World/Mechanisms/Targets/Interaction Trigger Router")]
public sealed class InteractionTriggerRouter : MonoBehaviourPunCallbacks, IInteractionTriggerTarget
{
    [Serializable]
    public sealed class Route
    {
        public string name = "Route";
        [Tooltip("A gameplay target or signal adapter implementing IInteractionTriggerTarget. Routers cannot be nested.")]
        public MonoBehaviour target;
        [Tooltip("Use a different activation policy while this route is controlling the mechanism.")]
        public bool overrideActivationMode;
        public InteractionTriggerMode activationMode = InteractionTriggerMode.Momentary;
    }

    [Tooltip("Ordered destinations. SelectRoute uses the zero-based index of an entry.")]
    [SerializeField] private Route[] routes = Array.Empty<Route>();
    [SerializeField, Min(0)] private int initialRoute;
    [Tooltip("Use only for local prototypes. Normally route changes are decided by the Master Client.")]
    [SerializeField] private bool localOnly;

    private bool initialized;
    private string networkKey;
    private int selectedRoute;
    private int heldRoute = -1;
    private int sequence;
    private GameObject lastActivator;
    private bool selecting;

    public int SelectedRoute { get { Initialize(); return selectedRoute; } }
    public int HeldRoute => heldRoute;
    public bool CanTrigger => isActiveAndEnabled && CurrentTarget != null && CurrentTarget.CanTrigger;
    public bool IsTriggerBusy => isActiveAndEnabled && CurrentTarget != null && CurrentTarget.IsTriggerBusy;
    private IInteractionTriggerTarget CurrentTarget => GetTarget(heldRoute >= 0 ? heldRoute : SelectedRoute);
    private bool Online => Application.IsPlaying(gameObject) && !localOnly && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;

    private void Awake() => Initialize();
    public override void OnEnable()
    {
        base.OnEnable();
        Initialize();
        RestoreNetworkState();
    }

    private void Initialize()
    {
        if (initialized) return;
        selectedRoute = initialRoute;
        networkKey = "interaction-trigger-router:" + SceneNetworkStateIdUtility.BuildSceneObjectId(transform);
        initialized = true;
    }

    private bool IsValidRoute(int index) => routes != null && index >= 0 && index < routes.Length
        && routes[index] != null && routes[index].target != null
        && routes[index].target is IInteractionTriggerTarget && routes[index].target is not InteractionTriggerRouter;

    private IInteractionTriggerTarget GetTarget(int index) => IsValidRoute(index) && routes[index].target.isActiveAndEnabled
        ? (IInteractionTriggerTarget)routes[index].target : null;

    public InteractionTriggerMode ResolveActivationMode(InteractionTriggerMode fallback)
    {
        Initialize();
        int index = heldRoute >= 0 ? heldRoute : selectedRoute;
        return IsValidRoute(index) && routes[index].overrideActivationMode ? routes[index].activationMode : fallback;
    }

    // Event entry point. Selection affects the next action, never the press already in progress.
    public void SelectRoute(int index)
    {
        Initialize();
        if ((Online && !PhotonNetwork.IsMasterClient) || !IsValidRoute(index) || index == selectedRoute) return;
        selectedRoute = index;
        PublishNetworkState();
    }

    // Opt-in handoff: release the old destination and immediately activate the selected one in B.
    public void SelectRouteAndActivate(int index)
    {
        Initialize();
        if (selecting || (Online && !PhotonNetwork.IsMasterClient) || !IsValidRoute(index) || index == selectedRoute) return;
        var source = GetComponent<InteractionTriggerSystem>();
        var next = GetTarget(index);
        if (source == null || source.LinkedSystem != this || !source.isActiveAndEnabled || next == null || !next.CanTrigger) return;
        selecting = true;
        try
        {
            var old = CurrentTarget;
            var oldMode = source.Mode;
            if (source.IsActive && old != null)
                old.ReceiveTrigger(new InteractionTriggerContext(source, lastActivator, false, oldMode));
            selectedRoute = index;
            heldRoute = -1;
            if (source.Mode != InteractionTriggerMode.AlternateAB) heldRoute = index;
            PublishNetworkState();
            if (source.ApplyRoutingState(true)) next.ReceiveTrigger(new InteractionTriggerContext(source, lastActivator));
        }
        finally { selecting = false; }
    }

    public void ReceiveTrigger(InteractionTriggerContext context)
    {
        Initialize();
        if (!isActiveAndEnabled || context.Source == null || (Online && !PhotonNetwork.IsMasterClient)) return;
        lastActivator = context.Activator;
        bool alternate = context.Mode == InteractionTriggerMode.AlternateAB;
        int route = context.IsActive || alternate ? selectedRoute : heldRoute;
        var target = GetTarget(route);
        if ((context.IsActive || alternate) && (target == null || !target.CanTrigger)) return;

        // Remember the old target before dispatch: its synchronous outputs may select a new route.
        heldRoute = context.IsActive && !alternate ? route : -1;
        PublishNetworkState();
        target?.ReceiveTrigger(context);
    }

    private void PublishNetworkState()
    {
        sequence++;
        if (!Online || !PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { networkKey, new object[] { sequence, selectedRoute, heldRoute } }
        });
    }

    private void RestoreNetworkState()
    {
        if (Online && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(networkKey, out object value)) ApplyNetworkState(value);
    }

    private void ApplyNetworkState(object value)
    {
        if (value is not object[] state || state.Length != 3 || state[0] is not int next || next <= sequence
            || state[1] is not int selected || !IsValidRoute(selected)
            || state[2] is not int held || (held != -1 && !IsValidRoute(held))) return;
        sequence = next;
        selectedRoute = selected;
        heldRoute = held;
    }

    public override void OnJoinedRoom() { Initialize(); sequence = 0; RestoreNetworkState(); }
    public override void OnMasterClientSwitched(Player newMasterClient) => RestoreNetworkState();
    public override void OnRoomPropertiesUpdate(Hashtable changes)
    {
        if (Online && changes != null && changes.TryGetValue(networkKey, out object value)) ApplyNetworkState(value);
    }
}
