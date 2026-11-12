using System;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using Unity.Services.Core;
using Unity.Services.Vivox;

public sealed class VivoxMicrophoneLifecycleTests
{
    [TestCase(VivoxInitializationState.Initialized, null, false)]
    [TestCase(VivoxInitializationState.Initialized, null, true)]
    [TestCase(VivoxInitializationState.Initialized, "LoggedOut", false)]
    [TestCase(VivoxInitializationState.Initialized, "LoggedOut", true)]
    [TestCase(VivoxInitializationState.Initialized, "LoggingIn", false)]
    [TestCase(VivoxInitializationState.Initialized, "LoggingIn", true)]
    [TestCase(VivoxInitializationState.Initialized, "LoggingOut", false)]
    [TestCase(VivoxInitializationState.Initialized, "LoggingOut", true)]
    [TestCase(VivoxInitializationState.Uninitialized, "LoggedIn", false)]
    [TestCase(VivoxInitializationState.Uninitialized, "LoggedIn", true)]
    public void MissingOrTransitioningNativeAccountsCannotReceiveMicrophoneRequests(
        VivoxInitializationState initialization, string loginState, bool allowTransmission)
    {
        var servicesBefore = UnityServices.State;
        Assembly sdk = typeof(IVivoxService).Assembly;
        Type serviceType = sdk.GetType("Unity.Services.Vivox.VivoxServiceInternal", true);

        // Use the SDK's real readiness getters without constructors, native handles or a microphone.
        var service = (IVivoxService)FormatterServices.GetUninitializedObject(serviceType);
        serviceType.GetProperty("InitializationState").SetValue(service, initialization);
        if (loginState != null)
        {
            Type sessionType = sdk.GetType("Unity.Services.Vivox.LoginSession", true);
            object session = FormatterServices.GetUninitializedObject(sessionType);
            FieldInfo state = sessionType.GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
            state.SetValue(session, Enum.Parse(state.FieldType, loginState));
            serviceType.GetField("m_LoginSession", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(service, session);
        }

        // Client is deliberately absent. Any mute read/write would touch it and fail this test.
        Assert.That(service.InitializationState, Is.EqualTo(initialization));
        Assert.That(service.IsLoggedIn, Is.EqualTo(loginState == "LoggedIn"));
        MethodInfo apply = typeof(VivoxVoiceManager).GetMethod("ApplyMicrophoneGate", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.DoesNotThrow(() => apply.Invoke(null, new object[] { service, allowTransmission }));
        Assert.That(UnityServices.State, Is.EqualTo(servicesBefore));
    }
}
