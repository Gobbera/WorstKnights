using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class StaminaController : MonoBehaviour
{
    private const string StaminaRootName = "Stamina";
    private const string StaminaFillImageName = "StaminaSliderBar";

    [SerializeField] private PlayerMovement targetMovement;
    [SerializeField] private Image staminaProgressUI;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] [Min(0f)] private float alphaLerpSpeed = 8f;

    public static void BindLocalPlayer(PlayerMovement playerMovement)
    {
        if (playerMovement == null)
            return;

        StaminaController controller = FindOrCreateSceneController();
        if (controller == null)
        {
            Debug.LogWarning("StaminaController: Could not find the scene UI root named 'Stamina'.", playerMovement);
            return;
        }

        controller.SetTarget(playerMovement);
    }

    private void Awake()
    {
        ResolveUiReferences();
        RefreshFillAmount(1f);
        SetCanvasAlpha(0f);
    }

    private void Update()
    {
        ResolveUiReferences();

        if (!ResolveTargetMovement())
        {
            SetCanvasAlpha(0f);
            return;
        }

        RefreshFillAmount(targetMovement.StaminaNormalized);

        float targetAlpha = targetMovement.ShouldShowStaminaBar ? 1f : 0f;
        float nextAlpha = alphaLerpSpeed <= 0f
            ? targetAlpha
            : Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, alphaLerpSpeed * Time.deltaTime);

        SetCanvasAlpha(nextAlpha);
    }

    private void SetTarget(PlayerMovement playerMovement)
    {
        targetMovement = playerMovement;
        ResolveUiReferences();

        if (targetMovement == null)
        {
            RefreshFillAmount(1f);
            SetCanvasAlpha(0f);
            return;
        }

        RefreshFillAmount(targetMovement.StaminaNormalized);
        SetCanvasAlpha(targetMovement.ShouldShowStaminaBar ? 1f : 0f);
    }

    private bool ResolveTargetMovement()
    {
        if (targetMovement != null)
            return true;

        PlayerMovement[] playerMovements = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include);
        for (int i = 0; i < playerMovements.Length; i++)
        {
            PlayerMovement playerMovement = playerMovements[i];
            if (playerMovement == null)
                continue;

            PhotonView playerView = playerMovement.GetComponent<PhotonView>();
            if (playerView != null && playerView.IsMine)
            {
                targetMovement = playerMovement;
                return true;
            }
        }

        return false;
    }

    private void ResolveUiReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        if (staminaProgressUI != null)
        {
            ConfigureFillImage();
            return;
        }

        Transform fillTransform = FindChildByName(transform, StaminaFillImageName);
        if (fillTransform != null)
        {
            staminaProgressUI = fillTransform.GetComponent<Image>();
            if (staminaProgressUI != null)
            {
                ConfigureFillImage();
                return;
            }
        }

        staminaProgressUI = GetComponentInChildren<Image>(true);
        ConfigureFillImage();
    }

    private void RefreshFillAmount(float normalizedStamina)
    {
        if (staminaProgressUI == null)
            return;

        staminaProgressUI.fillAmount = Mathf.Clamp01(normalizedStamina);
    }

    private void SetCanvasAlpha(float alpha)
    {
        if (canvasGroup == null)
            return;

        canvasGroup.alpha = Mathf.Clamp01(alpha);
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void ConfigureFillImage()
    {
        if (staminaProgressUI == null)
            return;

        staminaProgressUI.type = Image.Type.Filled;
        staminaProgressUI.fillMethod = Image.FillMethod.Horizontal;
        staminaProgressUI.fillOrigin = (int)Image.OriginHorizontal.Left;
    }

    private static StaminaController FindOrCreateSceneController()
    {
        Transform staminaRoot = FindSceneTransformByName(StaminaRootName);
        if (staminaRoot == null)
            return null;

        StaminaController controller = staminaRoot.GetComponent<StaminaController>();
        if (controller == null)
            controller = staminaRoot.gameObject.AddComponent<StaminaController>();

        return controller;
    }

    private static Transform FindSceneTransformByName(string objectName)
    {
        Transform[] sceneTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform sceneTransform = sceneTransforms[i];
            if (sceneTransform != null && string.Equals(sceneTransform.name, objectName, System.StringComparison.Ordinal))
                return sceneTransform;
        }

        return null;
    }

    private static Transform FindChildByName(Transform root, string objectName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, objectName, System.StringComparison.Ordinal))
                return child;
        }

        return null;
    }
}

[DisallowMultipleComponent]
public class HealthController : MonoBehaviour
{
    private const string HealthRootName = "Health";
    private const string HealthFillImageName = "HealthSliderBar";

    [SerializeField] private PlayerHealth targetHealth;
    [SerializeField] private Image healthProgressUI;
    [SerializeField] private CanvasGroup canvasGroup;

    public static void BindLocalPlayer(PlayerHealth playerHealth)
    {
        if (playerHealth == null)
            return;

        HealthController controller = FindOrCreateSceneController();
        if (controller == null)
        {
            Debug.LogWarning("HealthController: Could not find the scene UI root named 'Health'.", playerHealth);
            return;
        }

        controller.SetTarget(playerHealth);
    }

    private void Awake()
    {
        ResolveUiReferences();
        RefreshFillAmount(1f);
        SetCanvasAlpha(0f);
    }

    private void Update()
    {
        ResolveUiReferences();

        if (!ResolveTargetHealth())
        {
            SetCanvasAlpha(0f);
            return;
        }

        RefreshFillAmount(GetNormalizedHealth());
        SetCanvasAlpha(1f);
    }

    private void SetTarget(PlayerHealth playerHealth)
    {
        targetHealth = playerHealth;
        ResolveUiReferences();

        if (targetHealth == null)
        {
            RefreshFillAmount(1f);
            SetCanvasAlpha(0f);
            return;
        }

        RefreshFillAmount(GetNormalizedHealth());
        SetCanvasAlpha(1f);
    }

    private bool ResolveTargetHealth()
    {
        if (targetHealth != null)
            return true;

        PlayerHealth[] playerHealths = FindObjectsByType<PlayerHealth>(FindObjectsInactive.Include);
        for (int i = 0; i < playerHealths.Length; i++)
        {
            PlayerHealth playerHealth = playerHealths[i];
            if (playerHealth == null)
                continue;

            PhotonView playerView = playerHealth.GetComponent<PhotonView>();
            if (playerView != null && playerView.IsMine)
            {
                targetHealth = playerHealth;
                return true;
            }
        }

        return false;
    }

    private void ResolveUiReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        if (healthProgressUI != null)
        {
            ConfigureFillImage();
            return;
        }

        Transform fillTransform = FindChildByName(transform, HealthFillImageName);
        if (fillTransform != null)
        {
            healthProgressUI = fillTransform.GetComponent<Image>();
            if (healthProgressUI != null)
            {
                ConfigureFillImage();
                return;
            }
        }

        healthProgressUI = GetComponentInChildren<Image>(true);
        ConfigureFillImage();
    }

    private float GetNormalizedHealth()
    {
        if (targetHealth == null)
            return 0f;

        return targetHealth.MaxHealth <= 0.0001f
            ? 0f
            : Mathf.Clamp01(targetHealth.CurrentHealth / targetHealth.MaxHealth);
    }

    private void RefreshFillAmount(float normalizedHealth)
    {
        if (healthProgressUI == null)
            return;

        healthProgressUI.fillAmount = Mathf.Clamp01(normalizedHealth);
    }

    private void SetCanvasAlpha(float alpha)
    {
        if (canvasGroup == null)
            return;

        canvasGroup.alpha = Mathf.Clamp01(alpha);
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void ConfigureFillImage()
    {
        if (healthProgressUI == null)
            return;

        healthProgressUI.type = Image.Type.Filled;
        healthProgressUI.fillMethod = Image.FillMethod.Horizontal;
        healthProgressUI.fillOrigin = (int)Image.OriginHorizontal.Left;
    }

    private static HealthController FindOrCreateSceneController()
    {
        Transform healthRoot = FindSceneTransformByName(HealthRootName);
        if (healthRoot == null)
            return null;

        HealthController controller = healthRoot.GetComponent<HealthController>();
        if (controller == null)
            controller = healthRoot.gameObject.AddComponent<HealthController>();

        return controller;
    }

    private static Transform FindSceneTransformByName(string objectName)
    {
        Transform[] sceneTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform sceneTransform = sceneTransforms[i];
            if (sceneTransform != null && string.Equals(sceneTransform.name, objectName, System.StringComparison.Ordinal))
                return sceneTransform;
        }

        return null;
    }

    private static Transform FindChildByName(Transform root, string objectName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, objectName, System.StringComparison.Ordinal))
                return child;
        }

        return null;
    }
}
