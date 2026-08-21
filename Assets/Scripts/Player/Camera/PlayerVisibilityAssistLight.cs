using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerVisibilityAssistLight : MonoBehaviour
{
    private const string RuntimeLightName = "Visibility Assist Light";
    private const string DefaultAnchorName = "FP_Camera";
    private const float MinimumSourceRefreshInterval = 0.05f;

    [Header("References")]
    [Tooltip("PhotonView do player. Usado para garantir que a luz exista apenas no dono local.")]
    [SerializeField] private PhotonView photonView;
    [Tooltip("Inventario/equipamento do player. Usado para apagar o auxilio quando existe uma fonte de luz equipada.")]
    [SerializeField] private HandEquipmentController handEquipmentController;
    [Tooltip("Anchor da luz. Se vazio, o script procura o marker FP_Camera.")]
    [SerializeField] private Transform lightAnchor;
    [Tooltip("Luz runtime. Normalmente fica vazia no prefab e e criada em Play Mode.")]
    [SerializeField] private Light assistLight;

    [Header("Runtime")]
    [Tooltip("Liga ou desliga o auxilio de visibilidade sem remover o componente.")]
    [SerializeField] private bool assistEnabled = true;
    [Tooltip("Quando ligado, qualquer Light ativa no item equipado apaga este auxilio.")]
    [SerializeField] private bool disableWhenEquippedLightSource = true;
    [Tooltip("Aplica mudancas feitas no Inspector durante Play Mode imediatamente.")]
    [SerializeField] private bool applyInspectorChangesInPlayMode = true;

    [Header("Light Tuning")]
    [Tooltip("Nome usado para achar a camera/anchor se Light Anchor nao estiver preenchido.")]
    [SerializeField] private string lightAnchorName = DefaultAnchorName;
    [Tooltip("Offset local da luz em relacao ao anchor.")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, -0.18f, 0.22f);
    [Tooltip("Intensidade maxima da luz. Valores baixos preservam o escuro.")]
    [SerializeField] [Range(0f, 1f)] private float assistIntensity = 0.18f;
    [Tooltip("Alcance local da luz em metros.")]
    [SerializeField] [Min(0f)] private float assistRange = 3.2f;
    [Tooltip("Cor da luz. Tons frios tendem a parecer percepcao/olhos, nao fogo.")]
    [SerializeField] private Color assistColor = new Color(0.68f, 0.76f, 0.9f, 1f);
    [Tooltip("Velocidade do fade ao ligar/desligar. Zero troca instantaneamente.")]
    [SerializeField] [Range(0f, 30f)] private float fadeSpeed = 8f;
    [Tooltip("Intervalo para checar se o item equipado tem luz ativa.")]
    [SerializeField] [Min(MinimumSourceRefreshInterval)] private float sourceRefreshInterval = 0.25f;

    private float currentIntensity;
    private float targetIntensity;
    private float nextSourceRefreshTime;
    private bool subscribedToEquipment;

    public bool AssistEnabled
    {
        get => assistEnabled;
        set
        {
            assistEnabled = value;
            RefreshTargetIntensity(force: false);
        }
    }

    public float AssistIntensity
    {
        get => assistIntensity;
        set
        {
            assistIntensity = Mathf.Max(0f, value);
            RefreshTargetIntensity(force: false);
        }
    }

    public float AssistRange
    {
        get => assistRange;
        set
        {
            assistRange = Mathf.Max(0f, value);
            ConfigureAssistLight();
        }
    }

    public Color AssistColor
    {
        get => assistColor;
        set
        {
            assistColor = value;
            ConfigureAssistLight();
        }
    }

    public bool DisableWhenEquippedLightSource
    {
        get => disableWhenEquippedLightSource;
        set
        {
            disableWhenEquippedLightSource = value;
            RefreshTargetIntensity(force: false);
        }
    }

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToEquipment();
        RefreshTargetIntensity(force: true);
    }

    private void Start()
    {
        ResolveReferences();
        SubscribeToEquipment();
        RefreshTargetIntensity(force: true);
    }

    private void OnDisable()
    {
        UnsubscribeFromEquipment();
        DisableAssistLightImmediate();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEquipment();
    }

    private void OnValidate()
    {
        assistIntensity = Mathf.Max(0f, assistIntensity);
        assistRange = Mathf.Max(0f, assistRange);
        fadeSpeed = Mathf.Max(0f, fadeSpeed);
        sourceRefreshInterval = Mathf.Max(MinimumSourceRefreshInterval, sourceRefreshInterval);

        if (assistLight != null)
            ConfigureAssistLight();

        if (Application.isPlaying && applyInspectorChangesInPlayMode)
            RefreshTargetIntensity(force: false);
    }

    private void Update()
    {
        if (!HasLocalAuthority())
        {
            DisableAssistLightImmediate();
            return;
        }

        ResolveReferences();
        SubscribeToEquipment();

        if (assistLight == null && !assistEnabled)
            return;

        EnsureAssistLight();

        if (Time.time >= nextSourceRefreshTime)
            RefreshTargetIntensity(force: false);

        UpdateLightIntensity();
    }

    private void RefreshTargetIntensity(bool force)
    {
        nextSourceRefreshTime = Time.time + sourceRefreshInterval;

        bool shouldEnable = assistEnabled && HasLocalAuthority();
        if (shouldEnable && disableWhenEquippedLightSource && handEquipmentController != null)
            shouldEnable = !handEquipmentController.HasActiveEquippedLightSource();

        targetIntensity = shouldEnable ? assistIntensity : 0f;

        if (!force)
            return;

        currentIntensity = targetIntensity;
        ApplyAssistLightIntensity();
    }

    private void UpdateLightIntensity()
    {
        if (assistLight == null)
            return;

        float deltaTime = Mathf.Max(0f, Time.deltaTime);
        if (fadeSpeed <= 0f || deltaTime <= 0f)
            currentIntensity = targetIntensity;
        else
            currentIntensity = Mathf.Lerp(currentIntensity, targetIntensity, 1f - Mathf.Exp(-fadeSpeed * deltaTime));

        ApplyAssistLightIntensity();
    }

    private void ApplyAssistLightIntensity()
    {
        if (assistLight == null)
            return;

        ConfigureAssistLight();

        float appliedIntensity = Mathf.Max(0f, currentIntensity);
        assistLight.intensity = appliedIntensity;
        assistLight.enabled = appliedIntensity > 0.0001f;
    }

    private void DisableAssistLightImmediate()
    {
        currentIntensity = 0f;
        targetIntensity = 0f;

        if (assistLight == null)
            return;

        assistLight.intensity = 0f;
        assistLight.enabled = false;
    }

    private void ResolveReferences()
    {
        if (photonView == null)
            photonView = GetComponent<PhotonView>();

        if (handEquipmentController == null)
            handEquipmentController = GetComponent<HandEquipmentController>();

        if (lightAnchor == null)
            lightAnchor = ResolveLightAnchor();
    }

    private void EnsureAssistLight()
    {
        if (assistLight != null)
        {
            ConfigureAssistLight();
            return;
        }

        Transform anchor = lightAnchor != null ? lightAnchor : transform;
        GameObject lightObject = new GameObject(RuntimeLightName);
        lightObject.transform.SetParent(anchor, false);
        lightObject.transform.localPosition = localOffset;
        lightObject.transform.localRotation = Quaternion.identity;
        lightObject.transform.localScale = Vector3.one;

        assistLight = lightObject.AddComponent<Light>();
        ConfigureAssistLight();
        ApplyAssistLightIntensity();
    }

    private void ConfigureAssistLight()
    {
        if (assistLight == null)
            return;

        if (assistLight.transform.parent != lightAnchor && lightAnchor != null)
            assistLight.transform.SetParent(lightAnchor, false);

        assistLight.transform.localPosition = localOffset;
        assistLight.transform.localRotation = Quaternion.identity;
        assistLight.type = LightType.Point;
        assistLight.color = assistColor;
        assistLight.range = Mathf.Max(0f, assistRange);
        assistLight.shadows = LightShadows.None;
        assistLight.bounceIntensity = 0f;
        assistLight.renderMode = LightRenderMode.Auto;
    }

    private Transform ResolveLightAnchor()
    {
        FP_Camera cameraMarker = GetComponentInChildren<FP_Camera>(true);
        if (cameraMarker != null)
            return cameraMarker.transform;

        return FindChildTransformByName(transform, string.IsNullOrWhiteSpace(lightAnchorName) ? DefaultAnchorName : lightAnchorName);
    }

    private void SubscribeToEquipment()
    {
        if (subscribedToEquipment || handEquipmentController == null)
            return;

        handEquipmentController.StateChanged += HandleEquipmentStateChanged;
        subscribedToEquipment = true;
    }

    private void UnsubscribeFromEquipment()
    {
        if (!subscribedToEquipment || handEquipmentController == null)
            return;

        handEquipmentController.StateChanged -= HandleEquipmentStateChanged;
        subscribedToEquipment = false;
    }

    private void HandleEquipmentStateChanged()
    {
        RefreshTargetIntensity(force: false);
    }

    [ContextMenu("Refresh Assist Light")]
    private void RefreshFromInspector()
    {
        ResolveReferences();
        EnsureAssistLight();
        RefreshTargetIntensity(force: true);
    }

    private bool HasLocalAuthority()
    {
        return photonView == null || photonView.IsMine;
    }

    private static Transform FindChildTransformByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        Transform[] childTransforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform childTransform = childTransforms[i];
            if (childTransform != null && string.Equals(childTransform.name, targetName, System.StringComparison.Ordinal))
                return childTransform;
        }

        return null;
    }
}
