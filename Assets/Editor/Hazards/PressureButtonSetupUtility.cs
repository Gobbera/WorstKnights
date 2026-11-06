using UnityEditor;
using UnityEngine.SceneManagement;

// Retained menu entry for existing authoring workflows; all runtime behavior is modular.
public static class PressureButtonSetupUtility
{
    [MenuItem("Tools/Hazards/Setup Statue Puzzle Pressure Button")]
    public static void SetupActiveScene()
    {
        InteractionTriggerAuthoringUtility.PreparePrefabs();
        InteractionTriggerSceneSetup.Configure(SceneManager.GetActiveScene(), InteractionTriggerType.PressureButton);
    }
}
