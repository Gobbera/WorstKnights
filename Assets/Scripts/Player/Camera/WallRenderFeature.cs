using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class WallRenderFeature : ScriptableRendererFeature
{
    private static readonly string[] ShaderPassNames =
    {
        "UniversalForward",
        "UniversalForwardOnly",
        "SRPDefaultUnlit",
        "LightweightForward"
    };

    [Serializable]
    public sealed class Settings
    {
        public string baseCameraName = "FP_Camera";
        public string handsCameraName = "Hands Camera";
        public string firstPersonLayerName = "FirstPersonView";
        public string wallBypassLayerName = "Wall";
        public LayerMask firstPersonLayerMask = 1 << 8;
        public LayerMask wallBypassLayerMask = 1 << 9;
        [Range(1, 255)] public int stencilReference = 77;
        public RenderPassEvent wallStencilEvent = RenderPassEvent.AfterRenderingOpaques;
        public RenderPassEvent firstPersonOverlayEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new Settings();

    private RenderObjectsPass wallStencilPass;
    private RenderObjectsPass firstPersonWallOverlayPass;
    private int cachedWallMask;
    private int cachedFirstPersonMask;
    private int cachedStencilReference;

    public override void Create()
    {
        RebuildPasses();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings == null)
            return;

        Camera camera = renderingData.cameraData.camera;
        if (camera == null || camera.cameraType != CameraType.Game)
            return;

        EnsurePassesCurrent();

        if (wallStencilPass != null
            && renderingData.cameraData.renderType == CameraRenderType.Base
            && string.Equals(camera.name, settings.baseCameraName, StringComparison.Ordinal))
        {
            if (HasClearDepthHandsOverlay(camera))
                return;

            renderer.EnqueuePass(wallStencilPass);
            return;
        }

        if (firstPersonWallOverlayPass != null
            && renderingData.cameraData.renderType == CameraRenderType.Overlay
            && string.Equals(camera.name, settings.handsCameraName, StringComparison.Ordinal))
        {
            if (IsClearDepthHandsOverlay(camera))
                return;

            renderer.EnqueuePass(firstPersonWallOverlayPass);
        }
    }

    private bool HasClearDepthHandsOverlay(Camera baseCamera)
    {
        UniversalAdditionalCameraData baseCameraData = baseCamera != null
            ? baseCamera.GetComponent<UniversalAdditionalCameraData>()
            : null;
        if (baseCameraData == null)
            return false;

        List<Camera> cameraStack = baseCameraData.cameraStack;
        if (cameraStack == null)
            return false;

        for (int i = 0; i < cameraStack.Count; i++)
        {
            if (IsClearDepthHandsOverlay(cameraStack[i]))
                return true;
        }

        return false;
    }

    private bool IsClearDepthHandsOverlay(Camera camera)
    {
        if (camera == null || !string.Equals(camera.name, settings.handsCameraName, StringComparison.Ordinal))
            return false;

        UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        return cameraData != null
            && cameraData.renderType == CameraRenderType.Overlay
            && cameraData.clearDepth;
    }

    private void EnsurePassesCurrent()
    {
        int wallMask = ResolveLayerMask(settings.wallBypassLayerMask, settings.wallBypassLayerName);
        int firstPersonMask = ResolveLayerMask(settings.firstPersonLayerMask, settings.firstPersonLayerName);
        int stencilReference = Mathf.Clamp(settings.stencilReference, 1, 255);

        if (wallMask == cachedWallMask
            && firstPersonMask == cachedFirstPersonMask
            && stencilReference == cachedStencilReference
            && wallStencilPass != null
            && firstPersonWallOverlayPass != null)
        {
            return;
        }

        RebuildPasses(wallMask, firstPersonMask, stencilReference);
    }

    private void RebuildPasses()
    {
        if (settings == null)
            settings = new Settings();

        RebuildPasses(
            ResolveLayerMask(settings.wallBypassLayerMask, settings.wallBypassLayerName),
            ResolveLayerMask(settings.firstPersonLayerMask, settings.firstPersonLayerName),
            Mathf.Clamp(settings.stencilReference, 1, 255));
    }

    private void RebuildPasses(int wallMask, int firstPersonMask, int stencilReference)
    {
        cachedWallMask = wallMask;
        cachedFirstPersonMask = firstPersonMask;
        cachedStencilReference = stencilReference;

        wallStencilPass = null;
        firstPersonWallOverlayPass = null;

        if (wallMask == 0 || firstPersonMask == 0)
            return;

        wallStencilPass = new RenderObjectsPass(
            "First Person Wall Stencil",
            settings.wallStencilEvent,
            ShaderPassNames,
            RenderQueueType.Opaque,
            wallMask,
            new RenderObjects.CustomCameraSettings());
        wallStencilPass.SetDepthState(false, CompareFunction.LessEqual);
        wallStencilPass.SetStencilState(
            stencilReference,
            CompareFunction.Always,
            StencilOp.Replace,
            StencilOp.Keep,
            StencilOp.Keep);

        firstPersonWallOverlayPass = new RenderObjectsPass(
            "First Person Wall Overlay",
            settings.firstPersonOverlayEvent,
            ShaderPassNames,
            RenderQueueType.Opaque,
            firstPersonMask,
            new RenderObjects.CustomCameraSettings());
        firstPersonWallOverlayPass.SetDepthState(false, CompareFunction.Always);
        firstPersonWallOverlayPass.SetStencilState(
            stencilReference,
            CompareFunction.Equal,
            StencilOp.Keep,
            StencilOp.Keep,
            StencilOp.Keep);
    }

    private static int ResolveLayerMask(LayerMask layerMask, string fallbackLayerName)
    {
        if (layerMask.value != 0)
            return layerMask.value;

        int layer = LayerMask.NameToLayer(fallbackLayerName);
        return layer >= 0 ? 1 << layer : 0;
    }
}
