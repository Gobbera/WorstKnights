using System;
using System.Globalization;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/Destruction/Destructible Object Controller")]
public class DestructibleObjectController : MonoBehaviourPunCallbacks, IDamageable, IOnEventCallback
{
    private const string RoomPropertyKeyPrefix = "destructible:";
    private const byte DamageRequestEventCode = 71;

    private readonly struct DestructibleStateSnapshot
    {
        public DestructibleStateSnapshot(int stateSequence, float currentHealth, bool isDestroyed, DamageInfo damageInfo)
        {
            StateSequence = stateSequence;
            CurrentHealth = currentHealth;
            IsDestroyed = isDestroyed;
            DamageInfo = damageInfo;
        }

        public int StateSequence { get; }
        public float CurrentHealth { get; }
        public bool IsDestroyed { get; }
        public DamageInfo DamageInfo { get; }
    }

    public enum DestructionMode
    {
        DestroyGameObject = 0,
        DisableGameObject = 1,
        DestroyTarget = 2,
        DisableTarget = 3
    }

    [Header("Identity")]
    [SerializeField] private string destructibleName = "Destructible";
    [Header("Health")]
    [SerializeField] [Min(1f)] private float maxHealth = 30f;
    [SerializeField] [Min(0f)] private float damageImmunityDuration;
    [Header("Destruction")]
    [SerializeField] private DestructionMode destructionMode = DestructionMode.DestroyGameObject;
    [SerializeField] private GameObject destructionTarget;
    [SerializeField] private bool disableCollidersOnDestroyed = true;
    [Header("Networking")]
    [SerializeField] private new PhotonView photonView;
    [SerializeField] [HideInInspector] private string networkSceneId = string.Empty;
    [SerializeField] private bool prototypeLocalOnly;

    private float currentHealth;
    private float invulnerableUntil;
    private bool isDestroyed;
    private int lastAppliedStateSequence;
    private Collider[] cachedColliders = Array.Empty<Collider>();

    public event Action<DestructibleObjectController, DamageInfo> Damaged;
    public event Action<DestructibleObjectController, DamageInfo> Destroyed;

    public string DisplayName => string.IsNullOrWhiteSpace(destructibleName) ? gameObject.name : destructibleName;
    public bool IsAlive => !isDestroyed;
    public CombatAlignment Alignment => CombatAlignment.Neutral;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => Mathf.Max(1f, maxHealth);

    private string NetworkSceneId
    {
        get
        {
            EnsureNetworkSceneId();
            return networkSceneId;
        }
    }

    private void Reset()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (destructionTarget == null)
            destructionTarget = gameObject;

        EnsureNetworkSceneId();
    }

    private void Awake()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (destructionTarget == null)
            destructionTarget = gameObject;

        currentHealth = Mathf.Max(1f, maxHealth);
        cachedColliders = GetComponentsInChildren<Collider>(true);
        EnsureNetworkSceneId();
    }

    private void Start()
    {
        TryApplyRoomSyncedState(emitDamagedEvent: false, emitDestroyedEvent: true);
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        damageImmunityDuration = Mathf.Max(0f, damageImmunityDuration);

        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (destructionTarget == null)
            destructionTarget = gameObject;

        EnsureNetworkSceneId();
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        TryApplyRoomSyncedState(emitDamagedEvent: false, emitDestroyedEvent: true);
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);

        if (!ShouldUseRoomPropertySync() || propertiesThatChanged == null)
            return;

        string propertyKey = BuildRoomPropertyKey();
        if (!propertiesThatChanged.TryGetValue(propertyKey, out object propertyValue))
            return;

        if (!TryDecodeStateSnapshot(propertyValue, out DestructibleStateSnapshot snapshot))
            return;

        ApplyRoomSyncedState(snapshot, emitDamagedEvent: true, emitDestroyedEvent: true);
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent == null
            || photonEvent.Code != DamageRequestEventCode
            || !ShouldUseRoomPropertySync()
            || !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        if (!TryDecodeDamageRequest(photonEvent.CustomData, out string targetSceneId, out DamageInfo damageInfo))
            return;

        if (!string.Equals(targetSceneId, NetworkSceneId, StringComparison.Ordinal))
            return;

        ApplyDamageAuthoritative(damageInfo);
    }

    public void ApplyDamage(DamageInfo damageInfo)
    {
        if (!CanReceiveDamage(damageInfo))
            return;

        if (ShouldUsePhotonViewSync())
        {
            photonView.RPC(
                nameof(RpcApplyDamageWithContext),
                RpcTarget.AllBufferedViaServer,
                damageInfo.Amount,
                ResolveInstigatorViewId(damageInfo.Instigator),
                (int)damageInfo.SourceAlignment,
                damageInfo.HitPoint,
                damageInfo.HitDirection,
                damageInfo.ImpactVfxAttackAngle,
                damageInfo.HasImpactVfxAttackAngle);
            return;
        }

        if (ShouldUseRoomPropertySync())
        {
            if (PhotonNetwork.IsMasterClient)
            {
                ApplyDamageAuthoritative(damageInfo);
                return;
            }

            RaiseDamageRequest(damageInfo);
            return;
        }

        ApplyDamageLocal(damageInfo);
    }

    [ContextMenu("Force Destroy")]
    public void ForceDestroy()
    {
        if (!IsAlive)
            return;

        DamageInfo forceDamage = new DamageInfo(
            Mathf.Max(1f, currentHealth),
            null,
            CombatAlignment.Neutral,
            transform.position,
            Vector3.zero);

        ApplyDamage(forceDamage);
    }

    private bool CanReceiveDamage(DamageInfo damageInfo)
    {
        if (!IsAlive || damageInfo.Amount <= 0f)
            return false;

        if (Time.time + 0.0001f < invulnerableUntil)
            return false;

        return true;
    }

    private void ApplyDamageAuthoritative(DamageInfo damageInfo)
    {
        if (!ApplyDamageLocal(damageInfo))
            return;

        lastAppliedStateSequence++;
        PublishRoomSyncedState(new DestructibleStateSnapshot(lastAppliedStateSequence, currentHealth, isDestroyed, damageInfo));
    }

    private bool RaiseDamageRequest(DamageInfo damageInfo)
    {
        object[] eventContent =
        {
            NetworkSceneId,
            damageInfo.Amount,
            ResolveInstigatorViewId(damageInfo.Instigator),
            (int)damageInfo.SourceAlignment,
            damageInfo.HitPoint,
            damageInfo.HitDirection,
            damageInfo.ImpactVfxAttackAngle,
            damageInfo.HasImpactVfxAttackAngle
        };

        RaiseEventOptions raiseEventOptions = new RaiseEventOptions
        {
            Receivers = ReceiverGroup.MasterClient
        };

        if (PhotonNetwork.RaiseEvent(DamageRequestEventCode, eventContent, raiseEventOptions, SendOptions.SendReliable))
            return true;

        Debug.LogWarning($"[DestructibleObjectController] Falha ao solicitar dano sincronizado para '{DisplayName}'.", gameObject);
        return false;
    }

    private bool ApplyDamageLocal(DamageInfo damageInfo)
    {
        if (!CanReceiveDamage(damageInfo))
            return false;

        currentHealth = Mathf.Max(0f, currentHealth - damageInfo.Amount);
        invulnerableUntil = Time.time + Mathf.Max(0f, damageImmunityDuration);

        Damaged?.Invoke(this, damageInfo);

        if (currentHealth > 0f)
            return true;

        isDestroyed = true;
        currentHealth = 0f;
        invulnerableUntil = float.PositiveInfinity;
        HandleDestroyed(damageInfo, emitDestroyedEvent: true);
        return true;
    }

    private void ApplyRoomSyncedState(DestructibleStateSnapshot snapshot, bool emitDamagedEvent, bool emitDestroyedEvent)
    {
        if (snapshot.StateSequence <= lastAppliedStateSequence)
            return;

        lastAppliedStateSequence = snapshot.StateSequence;

        if (isDestroyed)
            return;

        currentHealth = Mathf.Clamp(snapshot.CurrentHealth, 0f, MaxHealth);
        if (snapshot.IsDestroyed)
        {
            isDestroyed = true;
            currentHealth = 0f;
            invulnerableUntil = float.PositiveInfinity;

            if (emitDamagedEvent)
                Damaged?.Invoke(this, snapshot.DamageInfo);

            HandleDestroyed(snapshot.DamageInfo, emitDestroyedEvent);
            return;
        }

        invulnerableUntil = 0f;
        if (emitDamagedEvent)
            Damaged?.Invoke(this, snapshot.DamageInfo);
    }

    private void TryApplyRoomSyncedState(bool emitDamagedEvent, bool emitDestroyedEvent)
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(BuildRoomPropertyKey(), out object propertyValue))
            return;

        if (!TryDecodeStateSnapshot(propertyValue, out DestructibleStateSnapshot snapshot))
            return;

        ApplyRoomSyncedState(snapshot, emitDamagedEvent, emitDestroyedEvent);
    }

    private void PublishRoomSyncedState(DestructibleStateSnapshot snapshot)
    {
        if (!ShouldUseRoomPropertySync() || PhotonNetwork.CurrentRoom == null)
            return;

        Hashtable roomState = new Hashtable
        {
            { BuildRoomPropertyKey(), EncodeStateSnapshot(snapshot) }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomState);
    }

    private void HandleDestroyed(DamageInfo damageInfo, bool emitDestroyedEvent)
    {
        if (disableCollidersOnDestroyed)
            DisableCachedColliders();

        if (emitDestroyedEvent)
            Destroyed?.Invoke(this, damageInfo);

        ExecuteDestructionMode();
    }

    private void DisableCachedColliders()
    {
        if (cachedColliders == null || cachedColliders.Length == 0)
            cachedColliders = GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < cachedColliders.Length; i++)
        {
            Collider cachedCollider = cachedColliders[i];
            if (cachedCollider != null)
                cachedCollider.enabled = false;
        }
    }

    private void ExecuteDestructionMode()
    {
        GameObject target = destructionTarget != null ? destructionTarget : gameObject;

        switch (destructionMode)
        {
            case DestructionMode.DisableGameObject:
                gameObject.SetActive(false);
                break;

            case DestructionMode.DestroyTarget:
                if (target != null)
                    Destroy(target);
                break;

            case DestructionMode.DisableTarget:
                if (target != null)
                    target.SetActive(false);
                break;

            default:
                Destroy(gameObject);
                break;
        }
    }

    private bool ShouldUsePhotonViewSync()
    {
        return !prototypeLocalOnly
            && photonView != null
            && PhotonNetwork.InRoom
            && !PhotonNetwork.OfflineMode;
    }

    private bool ShouldUseRoomPropertySync()
    {
        return !prototypeLocalOnly
            && (photonView == null || photonView.ViewID == 0)
            && PhotonNetwork.InRoom
            && !PhotonNetwork.OfflineMode
            && !string.IsNullOrWhiteSpace(NetworkSceneId);
    }

    private string BuildRoomPropertyKey()
    {
        return RoomPropertyKeyPrefix + NetworkSceneId;
    }

    private void EnsureNetworkSceneId()
    {
        if (!string.IsNullOrWhiteSpace(networkSceneId))
            return;

        networkSceneId = SceneNetworkStateIdUtility.BuildSceneObjectId(transform);
    }

    private static int ResolveInstigatorViewId(GameObject instigator)
    {
        PhotonView instigatorView = instigator != null ? instigator.GetComponentInParent<PhotonView>() : null;
        return instigatorView != null ? instigatorView.ViewID : 0;
    }

    private static GameObject ResolveInstigator(int viewId)
    {
        PhotonView instigatorView = viewId != 0 ? PhotonView.Find(viewId) : null;
        return instigatorView != null ? instigatorView.gameObject : null;
    }

    private static string EncodeStateSnapshot(DestructibleStateSnapshot snapshot)
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        return string.Join("|",
            snapshot.StateSequence.ToString(culture),
            snapshot.CurrentHealth.ToString("R", culture),
            snapshot.IsDestroyed ? "1" : "0",
            snapshot.DamageInfo.Amount.ToString("R", culture),
            ((int)snapshot.DamageInfo.SourceAlignment).ToString(culture),
            EncodeVector3(snapshot.DamageInfo.HitPoint, culture),
            EncodeVector3(snapshot.DamageInfo.HitDirection, culture),
            ResolveInstigatorViewId(snapshot.DamageInfo.Instigator).ToString(culture),
            snapshot.DamageInfo.ImpactVfxAttackAngle.ToString("R", culture),
            snapshot.DamageInfo.HasImpactVfxAttackAngle ? "1" : "0");
    }

    private static bool TryDecodeStateSnapshot(object propertyValue, out DestructibleStateSnapshot snapshot)
    {
        snapshot = default;
        if (propertyValue is not string encodedState || string.IsNullOrWhiteSpace(encodedState))
            return false;

        string[] segments = encodedState.Split('|');
        if (segments.Length != 8 && segments.Length != 10)
            return false;

        CultureInfo culture = CultureInfo.InvariantCulture;
        if (!int.TryParse(segments[0], NumberStyles.Integer, culture, out int stateSequence)
            || !float.TryParse(segments[1], NumberStyles.Float, culture, out float resultingHealth)
            || !float.TryParse(segments[3], NumberStyles.Float, culture, out float damageAmount)
            || !int.TryParse(segments[4], NumberStyles.Integer, culture, out int sourceAlignment)
            || !TryDecodeVector3(segments[5], culture, out Vector3 hitPoint)
            || !TryDecodeVector3(segments[6], culture, out Vector3 hitDirection)
            || !int.TryParse(segments[7], NumberStyles.Integer, culture, out int instigatorViewId))
        {
            return false;
        }

        bool destroyedState = string.Equals(segments[2], "1", StringComparison.Ordinal);
        float impactVfxAttackAngle = 0f;
        bool hasImpactVfxAttackAngle = false;
        if (segments.Length == 10)
        {
            if (!float.TryParse(segments[8], NumberStyles.Float, culture, out impactVfxAttackAngle))
                return false;

            hasImpactVfxAttackAngle = string.Equals(segments[9], "1", StringComparison.Ordinal);
        }

        DamageInfo damageInfo = new DamageInfo(
            damageAmount,
            ResolveInstigator(instigatorViewId),
            (CombatAlignment)sourceAlignment,
            hitPoint,
            hitDirection,
            impactVfxAttackAngle: impactVfxAttackAngle,
            hasImpactVfxAttackAngle: hasImpactVfxAttackAngle);

        snapshot = new DestructibleStateSnapshot(stateSequence, resultingHealth, destroyedState, damageInfo);
        return true;
    }

    private static bool TryDecodeDamageRequest(object eventContent, out string targetSceneId, out DamageInfo damageInfo)
    {
        targetSceneId = string.Empty;
        damageInfo = default;

        if (eventContent is not object[] eventSegments || (eventSegments.Length != 6 && eventSegments.Length != 8))
            return false;

        if (eventSegments[0] is not string sceneId
            || eventSegments[1] is not float damageAmount
            || eventSegments[2] is not int instigatorViewId
            || eventSegments[3] is not int sourceAlignment
            || eventSegments[4] is not Vector3 hitPoint
            || eventSegments[5] is not Vector3 hitDirection)
        {
            return false;
        }

        float impactVfxAttackAngle = 0f;
        bool hasImpactVfxAttackAngle = false;
        if (eventSegments.Length == 8)
        {
            if (eventSegments[6] is not float decodedImpactVfxAttackAngle
                || eventSegments[7] is not bool decodedHasImpactVfxAttackAngle)
            {
                return false;
            }

            impactVfxAttackAngle = decodedImpactVfxAttackAngle;
            hasImpactVfxAttackAngle = decodedHasImpactVfxAttackAngle;
        }

        targetSceneId = sceneId;
        damageInfo = new DamageInfo(
            damageAmount,
            ResolveInstigator(instigatorViewId),
            (CombatAlignment)sourceAlignment,
            hitPoint,
            hitDirection,
            impactVfxAttackAngle: impactVfxAttackAngle,
            hasImpactVfxAttackAngle: hasImpactVfxAttackAngle);
        return true;
    }

    private static string EncodeVector3(Vector3 value, CultureInfo culture)
    {
        return string.Join(",",
            value.x.ToString("R", culture),
            value.y.ToString("R", culture),
            value.z.ToString("R", culture));
    }

    private static bool TryDecodeVector3(string encodedVector, CultureInfo culture, out Vector3 value)
    {
        value = Vector3.zero;
        if (string.IsNullOrWhiteSpace(encodedVector))
            return false;

        string[] components = encodedVector.Split(',');
        if (components.Length != 3)
            return false;

        if (!float.TryParse(components[0], NumberStyles.Float, culture, out float x)
            || !float.TryParse(components[1], NumberStyles.Float, culture, out float y)
            || !float.TryParse(components[2], NumberStyles.Float, culture, out float z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }

    [PunRPC]
    private void RpcApplyDamage(
        float amount,
        int instigatorViewId,
        int sourceAlignment,
        Vector3 hitPoint,
        Vector3 hitDirection)
    {
        DamageInfo damageInfo = new DamageInfo(
            amount,
            ResolveInstigator(instigatorViewId),
            (CombatAlignment)sourceAlignment,
            hitPoint,
            hitDirection);
        ApplyDamageLocal(damageInfo);
    }

    [PunRPC]
    private void RpcApplyDamageWithContext(
        float amount,
        int instigatorViewId,
        int sourceAlignment,
        Vector3 hitPoint,
        Vector3 hitDirection,
        float impactVfxAttackAngle,
        bool hasImpactVfxAttackAngle)
    {
        DamageInfo damageInfo = new DamageInfo(
            amount,
            ResolveInstigator(instigatorViewId),
            (CombatAlignment)sourceAlignment,
            hitPoint,
            hitDirection,
            impactVfxAttackAngle: impactVfxAttackAngle,
            hasImpactVfxAttackAngle: hasImpactVfxAttackAngle);
        ApplyDamageLocal(damageInfo);
    }
}
