using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SettingsManagement;
using UnityEngine;

[InitializeOnLoad]
public sealed class VivoxConfigurationValidator : IPreprocessBuildWithReport
{
    private const string PackageName = "com.unity.services.vivox";
    private const string ServerKey = "server";
    private const string DomainKey = "domain";
    private const string IssuerKey = "tokenIssuer";
    private const string VivoxSettingsPath = "Project/Services/Vivox";
    private const string ConfigurationError =
        "Vivox is not configured for this project. Complete onboarding at Unity Dashboard > "
        + "Development > Products > Vivox Voice and Text Chat. Then open Edit > Project Settings > "
        + "Services > Vivox and wait for Server, Domain, and Issuer to populate. Keep Test Mode off "
        + "when using Unity Authentication.";

    static VivoxConfigurationValidator()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!IsConfigured())
            throw new BuildFailedException(ConfigurationError);
    }

    [MenuItem("Tools/Voice/Validate Vivox Configuration")]
    private static void ValidateConfiguration()
    {
        if (IsConfigured())
        {
            Debug.Log("Vivox configuration is present: Server, Domain, and Issuer are populated.");
            return;
        }

        Debug.LogError(ConfigurationError);
        OpenVivoxSettings();
    }

    [MenuItem("Tools/Voice/Open Vivox Settings")]
    private static void OpenVivoxSettings()
    {
        SettingsService.OpenProjectSettings(VivoxSettingsPath);
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode || IsConfigured())
            return;

        Debug.LogError(ConfigurationError);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += OpenVivoxSettings;
    }

    private static bool IsConfigured()
    {
        Settings settings = new Settings(PackageName);
        string server = settings.Get<string>(ServerKey, SettingsScope.Project, string.Empty);
        string domain = settings.Get<string>(DomainKey, SettingsScope.Project, string.Empty);
        string issuer = settings.Get<string>(IssuerKey, SettingsScope.Project, string.Empty);

        return !string.IsNullOrWhiteSpace(server)
            && !string.IsNullOrWhiteSpace(domain)
            && !string.IsNullOrWhiteSpace(issuer);
    }
}
