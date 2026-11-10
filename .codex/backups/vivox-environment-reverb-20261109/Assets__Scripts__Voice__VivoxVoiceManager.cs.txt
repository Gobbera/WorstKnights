using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class VivoxVoiceManager : MonoBehaviourPunCallbacks
{
    private const string ChannelPrefix = "kwk-pun-room-";
    private const string PhotonVivoxPlayerIdProperty = "kwkVivoxPlayerId";
    private const string SettingsResourcePath = "Voice/VivoxVoiceSettings";
    private const string AuthenticationProfileArgumentPrefix = "--ugs-profile=";
    private const string EditorAuthenticationProfile = "kwk-editor";
    private const string PlayerAuthenticationProfilePrefix = "kwk-player";
    private const int ChannelHashCharacterCount = 32;
    private const int IdentityFingerprintCharacterCount = 8;
    private const int MaximumDisplayNameLength = 30;
    private const int MaximumVivoxLoginsPerUser = 4;
    private const int MinimumVivoxLocalVolume = -50;
    private const int MaximumVivoxLocalVolume = 0;
    private const float ProximityMuteThreshold = 0.01f;
    private const float InitialPositionUpdateDelaySeconds = 1f;
    private const float InitialChannelReconnectDelaySeconds = 2f;
    private const float MaximumChannelReconnectDelaySeconds = 30f;
    private const float StableChannelDurationSeconds = 10f;
    private const string MissingVivoxConfigurationMessage =
        "Vivox credentials are missing. Complete Vivox onboarding in the Unity Dashboard, "
        + "then open Edit > Project Settings > Services > Vivox and wait for Server, Domain, "
        + "and Issuer to populate. Rebuild standalone players after the credentials are saved.";

    public static VivoxVoiceManager Instance { get; private set; }
    private static string resolvedAuthenticationProfile;

    public string ActiveChannelName => activeChannelName;
    public VivoxVoiceMode CurrentMode { get; private set; } = VivoxVoiceMode.Positional3D;
    public string LastError { get; private set; } = string.Empty;

    public bool IsVoiceConnected
    {
        get
        {
            return TryGetVivoxService(out IVivoxService service)
                && service.IsLoggedIn
                && !positionUpdatesSuspended
                && activeChannelConfirmed
                && !string.IsNullOrEmpty(activeChannelName)
                && service.ActiveChannels.ContainsKey(activeChannelName);
        }
    }

    private string desiredChannelName;
    private string activeChannelName;
    private VivoxVoiceMode desiredVoiceMode;
    private VivoxVoiceMode activeVoiceMode = VivoxVoiceMode.Disabled;
    private VivoxVoiceSettings settings;
    private PlayerSetup localPlayerSetup;
    private GameObject localPositionAnchor;
    private float nextPositionUpdateTime;
    private bool synchronizationRequested;
    private bool synchronizationRunning;
    private bool vivoxEventsSubscribed;
    private bool applicationIsQuitting;
    private bool positionUpdateErrorLogged;
    private bool positionUpdatesSuspended = true;
    private bool activeChannelConfirmed;
    private bool channelReconnectPending;
    private int consecutiveChannelDisconnects;
    private float nextChannelReconnectTime;
    private float channelConnectedAtTime = -1f;
    private readonly Dictionary<string, int> participantVolumeByPlayerId = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly HashSet<string> proximityMutedPlayerIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> unresolvedParticipantWarnings = new HashSet<string>(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        resolvedAuthenticationProfile = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        GameObject voiceManagerObject = new GameObject(nameof(VivoxVoiceManager));
        voiceManagerObject.AddComponent<VivoxVoiceManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        settings = Resources.Load<VivoxVoiceSettings>(SettingsResourcePath);
        if (settings == null)
        {
            settings = VivoxVoiceSettings.CreateRuntimeDefaults();
            Debug.LogWarning(
                $"VivoxVoiceManager: no settings asset found at Resources/{SettingsResourcePath}; using runtime defaults.",
                this);
        }

        CurrentMode = settings.InitialMode;
        desiredVoiceMode = CurrentMode;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        RequestSynchronizationForCurrentRoom();
    }

    private void Update()
    {
        UpdateChannelRecovery();
        UpdateVoiceSpatialization();
    }

    private void OnApplicationQuit()
    {
        applicationIsQuitting = true;
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        UnsubscribeFromVivoxEvents();
        Instance = null;
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        PublishVivoxIdentityToPhotonPlayer();
        RequestSynchronizationForCurrentRoom();
    }

    public override void OnLeftRoom()
    {
        base.OnLeftRoom();
        ClearLocalPlayerReference();
        RequestSynchronization(null, CurrentMode);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        base.OnDisconnected(cause);
        ClearLocalPlayerReference();
        RequestSynchronization(null, CurrentMode);
    }

    public void SetVoiceMode(VivoxVoiceMode mode)
    {
        if (!Enum.IsDefined(typeof(VivoxVoiceMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported Vivox voice mode.");

        if (CurrentMode == mode)
            return;

        CurrentMode = mode;
        RequestSynchronizationForCurrentRoom();
    }

    private void RequestSynchronizationForCurrentRoom()
    {
        string channelName = CurrentMode == VivoxVoiceMode.Disabled
            ? null
            : ResolveCurrentPhotonChannel(CurrentMode);
        RequestSynchronization(channelName, CurrentMode);
    }

    private void RequestSynchronization(string channelName, VivoxVoiceMode mode)
    {
        bool targetChanged = !string.Equals(desiredChannelName, channelName, StringComparison.Ordinal)
            || desiredVoiceMode != mode;

        desiredChannelName = channelName;
        desiredVoiceMode = mode;

        if (targetChanged)
            ResetChannelRecoveryState();

        synchronizationRequested = true;

        if (!synchronizationRunning && !applicationIsQuitting)
            _ = SynchronizeChannelAsync();
    }

    private async Task SynchronizeChannelAsync()
    {
        if (synchronizationRunning)
            return;

        synchronizationRunning = true;

        try
        {
            while (synchronizationRequested && !applicationIsQuitting)
            {
                synchronizationRequested = false;
                string targetChannelName = desiredChannelName;
                VivoxVoiceMode targetVoiceMode = desiredVoiceMode;

                try
                {
                    await ApplyDesiredChannelAsync(targetChannelName, targetVoiceMode);
                    LastError = string.Empty;
                }
                catch (Exception exception)
                {
                    LastError = exception.Message;
                    Debug.LogError($"VivoxVoiceManager: voice synchronization failed. {exception}", this);

                    if (!string.IsNullOrEmpty(targetChannelName)
                        && targetVoiceMode != VivoxVoiceMode.Disabled
                        && IsStillDesired(targetChannelName, targetVoiceMode))
                    {
                        ScheduleChannelReconnect("voice synchronization failed");
                    }
                }

                if (!string.Equals(targetChannelName, desiredChannelName, StringComparison.Ordinal)
                    || targetVoiceMode != desiredVoiceMode)
                    synchronizationRequested = true;
            }
        }
        finally
        {
            synchronizationRunning = false;

            if (synchronizationRequested && !applicationIsQuitting)
                _ = SynchronizeChannelAsync();
        }
    }

    private async Task ApplyDesiredChannelAsync(string targetChannelName, VivoxVoiceMode targetVoiceMode)
    {
        if (string.IsNullOrEmpty(targetChannelName) || targetVoiceMode == VivoxVoiceMode.Disabled)
        {
            await LeaveOwnedChannelsAsync();
            return;
        }

        await EnsureVoiceReadyAsync();

        if (!IsStillDesired(targetChannelName, targetVoiceMode))
            return;

        IVivoxService service = VivoxService.Instance;
        if (service.ActiveChannels.ContainsKey(targetChannelName))
        {
            MarkChannelConnected(targetChannelName, targetVoiceMode);
            return;
        }

        if (service.ActiveChannels.Count > 0)
            await service.LeaveAllChannelsAsync();

        activeChannelName = null;
        activeVoiceMode = VivoxVoiceMode.Disabled;
        activeChannelConfirmed = false;
        positionUpdatesSuspended = true;

        if (!IsStillDesired(targetChannelName, targetVoiceMode))
            return;

        bool useNativePositionalAudio = ShouldUseNativeVivoxPositionalAudio(targetVoiceMode);
        if (useNativePositionalAudio)
        {
            await service.JoinPositionalChannelAsync(
                targetChannelName,
                ChatCapability.AudioOnly,
                settings.CreateChannel3DProperties());
        }
        else
        {
            await service.JoinGroupChannelAsync(targetChannelName, ChatCapability.AudioOnly);
        }

        MarkChannelConnected(targetChannelName, targetVoiceMode);

        if (targetVoiceMode == VivoxVoiceMode.Positional3D)
        {
            string spatializationMode = useNativePositionalAudio
                ? "native Vivox positional"
                : "simulated local proximity";
            Debug.Log(
                $"VivoxVoiceManager: joined Positional3D channel '{targetChannelName}' using {spatializationMode} "
                + $"(audible={settings.AudibleDistance}, conversational={settings.ConversationalDistance}, "
                + $"fade={settings.FadeModel}/{settings.AudioFadeIntensity:0.###}, "
                + $"{ResolvePhotonVoiceContext()}).",
                this);
        }
        else
        {
            Debug.Log(
                $"VivoxVoiceManager: joined {targetVoiceMode} voice channel '{targetChannelName}' "
                + $"({ResolvePhotonVoiceContext()}).",
                this);
        }
    }

    private async Task EnsureVoiceReadyAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            InitializationOptions initializationOptions = new InitializationOptions()
                .SetProfile(ResolveAuthenticationProfile());
            await UnityServices.InitializeAsync(initializationOptions);
        }

        string authenticationProfile = ResolveAuthenticationProfile();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            if (!string.Equals(AuthenticationService.Instance.Profile, authenticationProfile, StringComparison.Ordinal))
                AuthenticationService.Instance.SwitchProfile(authenticationProfile);

            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        PublishVivoxIdentityToPhotonPlayer();

        IVivoxService service = VivoxService.Instance;
        if (service == null)
            throw new InvalidOperationException("Vivox was not registered after Unity Services initialization.");

        if (service.InitializationState != VivoxInitializationState.Initialized)
        {
            try
            {
                await service.InitializeAsync(CreateVivoxConfigurationOptions());
            }
            catch (NullReferenceException exception)
            {
                throw new InvalidOperationException(MissingVivoxConfigurationMessage, exception);
            }
        }

        SubscribeToVivoxEvents(service);

        if (!service.IsLoggedIn)
        {
            LoginOptions loginOptions = new LoginOptions
            {
                DisplayName = ResolveDisplayName()
            };

            await service.LoginAsync(loginOptions);
            Debug.Log(
                $"VivoxVoiceManager: Unity Authentication profile '{authenticationProfile}', "
                + $"identity '{ResolveAuthenticationIdentityFingerprint()}', and Vivox login completed.",
                this);
        }
    }

    private void PublishVivoxIdentityToPhotonPlayer()
    {
        if (!PhotonNetwork.InRoom
            || PhotonNetwork.LocalPlayer == null
            || UnityServices.State != ServicesInitializationState.Initialized
            || !AuthenticationService.Instance.IsSignedIn
            || string.IsNullOrWhiteSpace(AuthenticationService.Instance.PlayerId))
        {
            return;
        }

        string playerId = AuthenticationService.Instance.PlayerId;
        Hashtable customProperties = PhotonNetwork.LocalPlayer.CustomProperties;
        if (customProperties != null
            && customProperties.TryGetValue(PhotonVivoxPlayerIdProperty, out object existingPlayerId)
            && existingPlayerId is string existingPlayerIdString
            && string.Equals(existingPlayerIdString, playerId, StringComparison.Ordinal))
        {
            return;
        }

        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
        {
            { PhotonVivoxPlayerIdProperty, playerId }
        });

        Debug.Log(
            $"VivoxVoiceManager: published Vivox identity '{ResolveIdentityFingerprint(playerId)}' to Photon player properties.",
            this);
    }

    private async Task LeaveOwnedChannelsAsync()
    {
        if (!TryGetVivoxService(out IVivoxService service)
            || !service.IsLoggedIn
            || service.ActiveChannels.Count == 0)
        {
            activeChannelName = null;
            activeVoiceMode = VivoxVoiceMode.Disabled;
            activeChannelConfirmed = false;
            positionUpdatesSuspended = true;
            ClearProximityMixState();
            return;
        }

        await service.LeaveAllChannelsAsync();
        activeChannelName = null;
        activeVoiceMode = VivoxVoiceMode.Disabled;
        activeChannelConfirmed = false;
        positionUpdatesSuspended = true;
        ClearProximityMixState();
        Debug.Log("VivoxVoiceManager: left the active voice channel.", this);
    }

    private void SubscribeToVivoxEvents(IVivoxService service)
    {
        if (vivoxEventsSubscribed)
            return;

        service.ConnectionRecovering += OnVivoxConnectionRecovering;
        service.ConnectionRecovered += OnVivoxConnectionRecovered;
        service.ConnectionFailedToRecover += OnVivoxConnectionFailedToRecover;
        service.ChannelJoined += OnVivoxChannelJoined;
        service.ChannelLeft += OnVivoxChannelLeft;
        service.ParticipantAddedToChannel += OnVivoxParticipantAddedToChannel;
        service.ParticipantRemovedFromChannel += OnVivoxParticipantRemovedFromChannel;
        vivoxEventsSubscribed = true;
    }

    private void UnsubscribeFromVivoxEvents()
    {
        if (!vivoxEventsSubscribed || !TryGetVivoxService(out IVivoxService service))
            return;

        service.ConnectionRecovering -= OnVivoxConnectionRecovering;
        service.ConnectionRecovered -= OnVivoxConnectionRecovered;
        service.ConnectionFailedToRecover -= OnVivoxConnectionFailedToRecover;
        service.ChannelJoined -= OnVivoxChannelJoined;
        service.ChannelLeft -= OnVivoxChannelLeft;
        service.ParticipantAddedToChannel -= OnVivoxParticipantAddedToChannel;
        service.ParticipantRemovedFromChannel -= OnVivoxParticipantRemovedFromChannel;
        vivoxEventsSubscribed = false;
    }

    private void OnVivoxConnectionRecovering()
    {
        activeChannelConfirmed = false;
        positionUpdatesSuspended = true;
        Debug.LogWarning("VivoxVoiceManager: voice connection interrupted; Vivox is attempting to recover.", this);
    }

    private void OnVivoxConnectionRecovered()
    {
        Debug.Log("VivoxVoiceManager: voice connection recovered.", this);
        RequestSynchronization(desiredChannelName, desiredVoiceMode);
    }

    private void OnVivoxConnectionFailedToRecover()
    {
        activeChannelConfirmed = false;
        positionUpdatesSuspended = true;
        LastError = "Vivox could not recover the voice connection.";
        Debug.LogError($"VivoxVoiceManager: {LastError}", this);
        ScheduleChannelReconnect("Vivox connection recovery failed");
    }

    private void OnVivoxChannelJoined(string channelName)
    {
        if (!IsStillDesired(channelName, desiredVoiceMode))
            return;

        MarkChannelConnected(channelName, desiredVoiceMode);
        Debug.Log(
            $"VivoxVoiceManager: Vivox confirmed channel '{channelName}' is ready "
            + $"({ResolvePhotonVoiceContext()}).",
            this);
    }

    private void OnVivoxChannelLeft(string channelName)
    {
        activeChannelConfirmed = false;
        positionUpdatesSuspended = true;
        channelConnectedAtTime = -1f;

        if (string.Equals(activeChannelName, channelName, StringComparison.Ordinal))
        {
            activeChannelName = null;
            activeVoiceMode = VivoxVoiceMode.Disabled;
            ClearProximityMixState();
        }

        if (string.Equals(desiredChannelName, channelName, StringComparison.Ordinal))
        {
            Debug.LogWarning(
                $"VivoxVoiceManager: Vivox left desired channel '{channelName}' "
                + $"({ResolvePhotonVoiceContext()}).",
                this);
            ScheduleChannelReconnect("channel was disconnected by Vivox");
        }
    }

    private void OnVivoxParticipantAddedToChannel(VivoxParticipant participant)
    {
        if (!TryResolveParticipantChannelName(participant, out string channelName)
            || !string.Equals(channelName, activeChannelName, StringComparison.Ordinal))
        {
            return;
        }

        Debug.Log(
            $"VivoxVoiceManager: voice participant joined '{channelName}' "
            + $"({ResolveParticipantLabel(participant)}).",
            this);

        if (activeVoiceMode != VivoxVoiceMode.Positional3D || ShouldUseNativeVivoxPositionalAudio(activeVoiceMode))
            ResetParticipantMix(participant);
    }

    private void OnVivoxParticipantRemovedFromChannel(VivoxParticipant participant)
    {
        TryResolveParticipantPlayerId(participant, out string participantPlayerId);
        if (!TryResolveParticipantChannelName(participant, out string channelName))
        {
            ClearParticipantMixState(participantPlayerId);
            return;
        }

        Debug.Log(
            $"VivoxVoiceManager: voice participant left '{channelName}' "
            + $"({ResolveParticipantLabel(participant)}).",
            this);

        ClearParticipantMixState(participantPlayerId);
    }

    private void MarkChannelConnected(string channelName, VivoxVoiceMode voiceMode)
    {
        activeChannelName = channelName;
        activeVoiceMode = voiceMode;
        activeChannelConfirmed = true;
        channelReconnectPending = false;
        channelConnectedAtTime = Time.unscaledTime;
        nextPositionUpdateTime = Time.unscaledTime + InitialPositionUpdateDelaySeconds;
        positionUpdateErrorLogged = false;
        positionUpdatesSuspended = false;
    }

    private void ResetChannelRecoveryState()
    {
        channelReconnectPending = false;
        consecutiveChannelDisconnects = 0;
        nextChannelReconnectTime = 0f;
        channelConnectedAtTime = -1f;
        positionUpdatesSuspended = true;
        activeChannelConfirmed = false;
        ClearProximityMixState();
    }

    private void ScheduleChannelReconnect(string reason)
    {
        if (applicationIsQuitting
            || channelReconnectPending
            || string.IsNullOrEmpty(desiredChannelName)
            || desiredVoiceMode == VivoxVoiceMode.Disabled)
        {
            return;
        }

        consecutiveChannelDisconnects++;
        float delay = Mathf.Min(
            InitialChannelReconnectDelaySeconds * Mathf.Pow(2f, consecutiveChannelDisconnects - 1),
            MaximumChannelReconnectDelaySeconds);

        channelReconnectPending = true;
        nextChannelReconnectTime = Time.unscaledTime + delay;
        Debug.LogWarning(
            $"VivoxVoiceManager: {reason}; retrying channel in {delay:0.#}s "
            + $"(attempt {consecutiveChannelDisconnects}).",
            this);
    }

    private void UpdateChannelRecovery()
    {
        if (channelConnectedAtTime >= 0f
            && Time.unscaledTime - channelConnectedAtTime >= StableChannelDurationSeconds)
        {
            consecutiveChannelDisconnects = 0;
            channelConnectedAtTime = -1f;
        }

        if (!channelReconnectPending || Time.unscaledTime < nextChannelReconnectTime)
            return;

        channelReconnectPending = false;
        RequestSynchronization(desiredChannelName, desiredVoiceMode);
    }

    private string ResolveCurrentPhotonChannel(VivoxVoiceMode mode)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            return null;

        string channelIdentity = string.Join(
            "|",
            PhotonNetwork.AppVersion ?? string.Empty,
            PhotonNetwork.CloudRegion ?? string.Empty,
            PhotonNetwork.CurrentRoom.Name ?? string.Empty);

        string modeSuffix = ResolveChannelModeSuffix(mode);
        return ChannelPrefix + ComputeStableHash(channelIdentity) + modeSuffix;
    }

    private string ResolveChannelModeSuffix(VivoxVoiceMode mode)
    {
        if (mode == VivoxVoiceMode.Positional3D)
            return settings != null && settings.UseNativeVivoxPositionalAudio ? "-3d" : "-sim3d";

        return "-2d";
    }

    private static string ResolvePhotonVoiceContext()
    {
        string region = string.IsNullOrWhiteSpace(PhotonNetwork.CloudRegion)
            ? "none"
            : PhotonNetwork.CloudRegion;
        string appVersion = string.IsNullOrWhiteSpace(PhotonNetwork.AppVersion)
            ? "none"
            : PhotonNetwork.AppVersion;
        string roomName = PhotonNetwork.CurrentRoom == null || string.IsNullOrWhiteSpace(PhotonNetwork.CurrentRoom.Name)
            ? "none"
            : PhotonNetwork.CurrentRoom.Name;
        int playerCount = PhotonNetwork.CurrentRoom?.PlayerCount ?? 0;

        return $"region='{region}', appVersion='{appVersion}', room='{roomName}', players={playerCount}";
    }

    private bool IsStillDesired(string channelName, VivoxVoiceMode mode)
    {
        return string.Equals(channelName, desiredChannelName, StringComparison.Ordinal)
            && mode == desiredVoiceMode;
    }

    private void UpdateVoiceSpatialization()
    {
        if (activeVoiceMode != VivoxVoiceMode.Positional3D
            || !IsVoiceConnected
            || Time.unscaledTime < nextPositionUpdateTime)
        {
            return;
        }

        nextPositionUpdateTime = Time.unscaledTime + settings.PositionUpdateInterval;

        if (ShouldUseNativeVivoxPositionalAudio(activeVoiceMode))
        {
            UpdateNativePositionalVoice();
            return;
        }

        UpdateSimulatedProximityVoice();
    }

    private void UpdateNativePositionalVoice()
    {
        GameObject positionAnchor = ResolveLocalPositionAnchor();
        if (positionAnchor == null)
            return;

        try
        {
            VivoxService.Instance.Set3DPosition(
                positionAnchor,
                activeChannelName,
                settings.AllowDirectionalPanning);
            positionUpdateErrorLogged = false;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            nextPositionUpdateTime = Time.unscaledTime + 1f;
            positionUpdatesSuspended = true;

            if (!positionUpdateErrorLogged)
            {
                Debug.LogWarning($"VivoxVoiceManager: failed to update 3D position. {exception.Message}", this);
                positionUpdateErrorLogged = true;
            }
        }
    }

    private void UpdateSimulatedProximityVoice()
    {
        GameObject listenerAnchor = ResolveLocalPositionAnchor();
        if (listenerAnchor == null)
            return;

        if (!TryGetVivoxService(out IVivoxService service)
            || !service.ActiveChannels.TryGetValue(activeChannelName, out var participants))
            return;

        for (int i = 0; i < participants.Count; i++)
        {
            VivoxParticipant participant = participants[i];
            if (!TryResolveRemoteParticipantPlayerId(participant, out string participantPlayerId))
                continue;

            if (!TryResolveRemoteVoiceAnchor(participantPlayerId, out Transform speakerAnchor))
            {
                LogUnresolvedParticipantOnce(participantPlayerId);
                continue;
            }

            unresolvedParticipantWarnings.Remove(participantPlayerId);
            float distance = Vector3.Distance(listenerAnchor.transform.position, speakerAnchor.position);
            ApplySimulatedProximityMix(participant, participantPlayerId, distance);
        }
    }

    private bool ShouldUseNativeVivoxPositionalAudio(VivoxVoiceMode mode)
    {
        return mode == VivoxVoiceMode.Positional3D
            && settings != null
            && settings.UseNativeVivoxPositionalAudio;
    }

    private static bool TryGetVivoxService(out IVivoxService service)
    {
        service = null;
        if (UnityServices.State != ServicesInitializationState.Initialized)
            return false;

        try
        {
            service = VivoxService.Instance;
            return service != null;
        }
        catch (ServicesInitializationException)
        {
            return false;
        }
    }

    private void ApplySimulatedProximityMix(VivoxParticipant participant, string participantPlayerId, float distance)
    {
        float volume = CalculateSimulatedProximityVolume(distance);
        bool shouldMute = volume <= ProximityMuteThreshold || distance >= settings.AudibleDistance;
        int localVolume = shouldMute
            ? MinimumVivoxLocalVolume
            : Mathf.RoundToInt(Mathf.Lerp(MinimumVivoxLocalVolume, MaximumVivoxLocalVolume, volume));

        ApplyParticipantMix(participant, participantPlayerId, localVolume, shouldMute);
    }

    private float CalculateSimulatedProximityVolume(float distance)
    {
        int conversationalDistance = settings.ConversationalDistance;
        int audibleDistance = settings.AudibleDistance;
        if (distance <= conversationalDistance)
            return 1f;

        if (distance >= audibleDistance)
            return 0f;

        float fadeRange = Mathf.Max(0.001f, audibleDistance - conversationalDistance);
        float normalizedDistance = Mathf.Clamp01((distance - conversationalDistance) / fadeRange);
        float intensity = settings.AudioFadeIntensity;
        if (intensity <= Mathf.Epsilon)
            return 1f;

        switch (settings.FadeModel)
        {
            case AudioFadeModel.LinearByDistance:
                return Mathf.Clamp01(1f - normalizedDistance * intensity);

            case AudioFadeModel.ExponentialByDistance:
                return Mathf.Clamp01(Mathf.Pow(1f - normalizedDistance, Mathf.Max(0.01f, intensity * 2f)));

            case AudioFadeModel.InverseByDistance:
            default:
                float scaledIntensity = Mathf.Max(0.01f, intensity) * 6f;
                float rawVolume = 1f / (1f + normalizedDistance * scaledIntensity);
                float farVolume = 1f / (1f + scaledIntensity);
                return Mathf.Clamp01(Mathf.InverseLerp(farVolume, 1f, rawVolume));
        }
    }

    private void ApplyParticipantMix(VivoxParticipant participant, string participantPlayerId, int localVolume, bool shouldMute)
    {
        try
        {
            if (shouldMute)
            {
                if (proximityMutedPlayerIds.Add(participantPlayerId))
                    participant.MutePlayerLocally();
            }
            else if (proximityMutedPlayerIds.Remove(participantPlayerId))
            {
                participant.UnmutePlayerLocally();
            }

            if (!participantVolumeByPlayerId.TryGetValue(participantPlayerId, out int previousVolume)
                || previousVolume != localVolume)
            {
                participant.SetLocalVolume(localVolume);
                participantVolumeByPlayerId[participantPlayerId] = localVolume;
            }
        }
        catch (InvalidOperationException)
        {
            ClearParticipantMixState(participantPlayerId);
        }
        catch (NullReferenceException)
        {
            ClearParticipantMixState(participantPlayerId);
        }
    }

    private void ResetParticipantMix(VivoxParticipant participant)
    {
        if (!TryResolveRemoteParticipantPlayerId(participant, out string participantPlayerId))
            return;

        try
        {
            if (proximityMutedPlayerIds.Remove(participantPlayerId))
                participant.UnmutePlayerLocally();

            if (!participantVolumeByPlayerId.TryGetValue(participantPlayerId, out int previousVolume)
                || previousVolume != MaximumVivoxLocalVolume)
            {
                participant.SetLocalVolume(MaximumVivoxLocalVolume);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NullReferenceException)
        {
        }
        finally
        {
            ClearParticipantMixState(participantPlayerId);
        }
    }

    private void ClearParticipantMixState(string participantPlayerId)
    {
        if (string.IsNullOrEmpty(participantPlayerId))
            return;

        participantVolumeByPlayerId.Remove(participantPlayerId);
        proximityMutedPlayerIds.Remove(participantPlayerId);
        unresolvedParticipantWarnings.Remove(participantPlayerId);
    }

    private void ClearProximityMixState()
    {
        participantVolumeByPlayerId.Clear();
        proximityMutedPlayerIds.Clear();
        unresolvedParticipantWarnings.Clear();
    }

    private bool TryResolveRemoteParticipantPlayerId(VivoxParticipant participant, out string participantPlayerId)
    {
        participantPlayerId = null;
        if (participant == null)
            return false;

        try
        {
            if (participant.IsSelf)
                return false;

            participantPlayerId = participant.PlayerId;
            return !string.IsNullOrWhiteSpace(participantPlayerId);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NullReferenceException)
        {
            return false;
        }
    }

    private static bool TryResolveParticipantPlayerId(VivoxParticipant participant, out string participantPlayerId)
    {
        participantPlayerId = null;
        if (participant == null)
            return false;

        try
        {
            participantPlayerId = participant.PlayerId;
            return !string.IsNullOrWhiteSpace(participantPlayerId);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NullReferenceException)
        {
            return false;
        }
    }

    private bool TryResolveRemoteVoiceAnchor(string vivoxPlayerId, out Transform anchor)
    {
        anchor = null;
        Player photonPlayer = ResolvePhotonPlayerByVivoxPlayerId(vivoxPlayerId);
        if (photonPlayer == null)
            return false;

        PlayerSetup[] players = FindObjectsByType<PlayerSetup>(FindObjectsInactive.Exclude);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerSetup player = players[i];
            if (player == null)
                continue;

            PhotonView playerView = player.GetComponent<PhotonView>();
            if (playerView == null || playerView.OwnerActorNr != photonPlayer.ActorNumber)
                continue;

            FP_Camera cameraMarker = player.GetComponentInChildren<FP_Camera>(true);
            anchor = cameraMarker != null ? cameraMarker.transform : player.transform;
            return anchor != null;
        }

        return false;
    }

    private static Player ResolvePhotonPlayerByVivoxPlayerId(string vivoxPlayerId)
    {
        if (string.IsNullOrWhiteSpace(vivoxPlayerId) || PhotonNetwork.PlayerList == null)
            return null;

        Player[] photonPlayers = PhotonNetwork.PlayerList;
        for (int i = 0; i < photonPlayers.Length; i++)
        {
            Player photonPlayer = photonPlayers[i];
            if (photonPlayer?.CustomProperties == null)
                continue;

            if (photonPlayer.CustomProperties.TryGetValue(PhotonVivoxPlayerIdProperty, out object propertyValue)
                && propertyValue is string playerId
                && string.Equals(playerId, vivoxPlayerId, StringComparison.Ordinal))
            {
                return photonPlayer;
            }
        }

        return null;
    }

    private void LogUnresolvedParticipantOnce(string participantPlayerId)
    {
        if (string.IsNullOrWhiteSpace(participantPlayerId)
            || !unresolvedParticipantWarnings.Add(participantPlayerId))
        {
            return;
        }

        Debug.LogWarning(
            $"VivoxVoiceManager: waiting for Photon player mapping for voice participant "
            + $"'{ResolveIdentityFingerprint(participantPlayerId)}'. Proximity volume will be applied when the mapping arrives.",
            this);
    }

    private GameObject ResolveLocalPositionAnchor()
    {
        if (localPlayerSetup != null && localPositionAnchor != null)
        {
            PhotonView cachedView = localPlayerSetup.GetComponent<PhotonView>();
            if (cachedView != null && cachedView.IsMine && localPositionAnchor.activeInHierarchy)
                return localPositionAnchor;
        }

        ClearLocalPlayerReference();
        PlayerSetup[] players = FindObjectsByType<PlayerSetup>(FindObjectsInactive.Exclude);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerSetup player = players[i];
            if (player == null)
                continue;

            PhotonView playerView = player.GetComponent<PhotonView>();
            if (playerView == null || !playerView.IsMine || playerView.InstantiationId == 0)
                continue;

            localPlayerSetup = player;
            FP_Camera cameraMarker = player.GetComponentInChildren<FP_Camera>(true);
            localPositionAnchor = cameraMarker != null ? cameraMarker.gameObject : player.gameObject;
            return localPositionAnchor.activeInHierarchy ? localPositionAnchor : null;
        }

        return null;
    }

    private void ClearLocalPlayerReference()
    {
        localPlayerSetup = null;
        localPositionAnchor = null;
        nextPositionUpdateTime = 0f;
        positionUpdateErrorLogged = false;
    }

    private static string ComputeStableHash(string value)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            StringBuilder builder = new StringBuilder(ChannelHashCharacterCount);

            for (int i = 0; i < hash.Length && builder.Length < ChannelHashCharacterCount; i++)
                builder.Append(hash[i].ToString("x2"));

            return builder.ToString();
        }
    }

    private static string ResolveDisplayName()
    {
        string photonNickname = PhotonNetwork.NickName;
        if (string.IsNullOrWhiteSpace(photonNickname))
            return "Player";

        StringBuilder displayName = new StringBuilder(MaximumDisplayNameLength);
        string trimmedNickname = photonNickname.Trim();

        for (int i = 0; i < trimmedNickname.Length && displayName.Length < MaximumDisplayNameLength; i++)
        {
            char character = trimmedNickname[i];
            if (char.IsLetterOrDigit(character) || character == ' ' || character == '_' || character == '-')
                displayName.Append(character);
        }

        return displayName.Length > 0 ? displayName.ToString() : "Player";
    }

    private static string ResolveParticipantLabel(VivoxParticipant participant)
    {
        try
        {
            string displayName = string.IsNullOrWhiteSpace(participant.DisplayName)
                ? "Player"
                : participant.DisplayName;
            string role = participant.IsSelf ? "self" : "remote";
            string identity = ResolveIdentityFingerprint(participant.PlayerId);

            return $"{role}, displayName='{displayName}', identity='{identity}'";
        }
        catch (InvalidOperationException)
        {
            return "participant already removed";
        }
        catch (NullReferenceException)
        {
            return "participant already removed";
        }
    }

    private static bool TryResolveParticipantChannelName(VivoxParticipant participant, out string channelName)
    {
        channelName = null;
        if (participant == null)
            return false;

        try
        {
            channelName = participant.ChannelName;
            return !string.IsNullOrEmpty(channelName);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NullReferenceException)
        {
            return false;
        }
    }

    private static string ResolveAuthenticationIdentityFingerprint()
    {
        return ResolveIdentityFingerprint(AuthenticationService.Instance.PlayerId);
    }

    private static VivoxConfigurationOptions CreateVivoxConfigurationOptions()
    {
        return new VivoxConfigurationOptions
        {
            MaxLoginsPerUser = MaximumVivoxLoginsPerUser
        };
    }

    private static string ResolveIdentityFingerprint(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            return "unavailable";

        string fingerprint = ComputeStableHash(playerId);
        return fingerprint.Substring(0, Mathf.Min(IdentityFingerprintCharacterCount, fingerprint.Length));
    }

    private static string ResolveAuthenticationProfile()
    {
        if (!string.IsNullOrEmpty(resolvedAuthenticationProfile))
            return resolvedAuthenticationProfile;

        string[] commandLineArguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < commandLineArguments.Length; i++)
        {
            string argument = commandLineArguments[i];
            if (!argument.StartsWith(AuthenticationProfileArgumentPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string requestedProfile = argument.Substring(AuthenticationProfileArgumentPrefix.Length);
            if (IsValidAuthenticationProfile(requestedProfile))
            {
                resolvedAuthenticationProfile = requestedProfile;
                return resolvedAuthenticationProfile;
            }
        }

#if UNITY_EDITOR
        resolvedAuthenticationProfile = EditorAuthenticationProfile;
#else
        resolvedAuthenticationProfile = CreateEphemeralStandaloneAuthenticationProfile();
#endif
        return resolvedAuthenticationProfile;
    }

    private static string CreateEphemeralStandaloneAuthenticationProfile()
    {
        string profileSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        return $"{PlayerAuthenticationProfilePrefix}-{profileSuffix}";
    }

    private static bool IsValidAuthenticationProfile(string profile)
    {
        if (string.IsNullOrEmpty(profile) || profile.Length > 30)
            return false;

        for (int i = 0; i < profile.Length; i++)
        {
            char character = profile[i];
            if (!char.IsLetterOrDigit(character) && character != '-' && character != '_')
                return false;
        }

        return true;
    }
}
