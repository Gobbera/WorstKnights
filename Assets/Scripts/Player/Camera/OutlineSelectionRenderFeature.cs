using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class OutlineSelectionRenderFeature : ScriptableRendererFeature
{
    private static readonly int OutlineMaskId = Shader.PropertyToID("_WKOutlineSelectionMask");
    private static readonly int OutlineColorId = Shader.PropertyToID("_Color");
    private static readonly int OutlineThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int NearOpacityId = Shader.PropertyToID("_NearOpacity");
    private static readonly int FarOpacityId = Shader.PropertyToID("_FarOpacity");
    private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
    private static readonly int FadeEndId = Shader.PropertyToID("_FadeEnd");
    private static readonly int ForceNearOpacityId = Shader.PropertyToID("_ForceNearOpacity");

    [SerializeField] private bool useOutlineMask = true;
    [SerializeField] private RenderingLayerMask outlineRenderingLayers = (RenderingLayerMask)(1u << 8);
    [SerializeField] private FullScreenPassRendererFeature outlinePass;
    [SerializeField] private Material maskCompositeMaterial;

    private OutlineSelectionPass selectionPass;

    public override void Create()
    {
        selectionPass = new OutlineSelectionPass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Material outlineMaterial = outlinePass != null ? outlinePass.passMaterial : null;
        Camera camera = renderingData.cameraData.camera;
        if (selectionPass == null
            || outlineMaterial == null
            || camera == null
            || camera.cameraType == CameraType.Preview
            || camera.cameraType == CameraType.Reflection)
            return;

        bool isHandsCamera = renderingData.cameraData.renderType == CameraRenderType.Overlay
            && camera.name == "Hands Camera"
            && (camera.cullingMask & (1 << 8)) != 0;
        if (renderingData.cameraData.renderType != CameraRenderType.Base && !isHandsCamera)
            return;

        bool renderMask = isHandsCamera || useOutlineMask;
        if (renderMask && (maskCompositeMaterial == null || (!isHandsCamera && outlineRenderingLayers.value == 0)))
            return;

        selectionPass.ConfigureInput(isHandsCamera ? ScriptableRenderPassInput.None : ScriptableRenderPassInput.Depth);
        selectionPass.SetSettings(outlineRenderingLayers.value, outlineMaterial, maskCompositeMaterial, renderMask, isHandsCamera);
        renderer.EnqueuePass(selectionPass);
    }

    private sealed class OutlineSelectionPass : ScriptableRenderPass
    {
        private static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("LightweightForward")
        };

        private static readonly RenderStateBlock SelectionState = CreateSelectionState();

        private uint renderingLayers;
        private Material outlineMaterial;
        private Material compositeMaterial;
        private bool renderMask;
        private bool isHandsCamera;

        private sealed class GlobalPassData
        {
            public Material material;
        }

        private sealed class MaskPassData
        {
            public RendererListHandle rendererList;
        }

        private sealed class CompositePassData
        {
            public TextureHandle maskTexture;
            public Material material;
            public MaterialPropertyBlock properties;
        }

        public OutlineSelectionPass()
        {
            profilingSampler = new ProfilingSampler("Outline Selection Mask");
        }

        public void SetSettings(uint layers, Material material, Material maskMaterial, bool useMask, bool handsCamera)
        {
            renderingLayers = layers;
            outlineMaterial = material;
            compositeMaterial = maskMaterial;
            renderMask = useMask;
            isHandsCamera = handsCamera;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (!resources.activeColorTexture.IsValid() || !resources.activeDepthTexture.IsValid())
                return;

            if (!renderMask)
            {
                if (!resources.cameraDepthTexture.IsValid())
                    return;

                using (var builder = renderGraph.AddRasterRenderPass<GlobalPassData>("World Outline", out GlobalPassData passData, profilingSampler))
                {
                    passData.material = outlineMaterial;
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (GlobalPassData data, RasterGraphContext context) =>
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3));
                }
                return;
            }

            TextureDesc maskDesc = renderGraph.GetTextureDesc(resources.activeDepthTexture);
            maskDesc.name = "Outline Selection Mask";
            maskDesc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            maskDesc.clearBuffer = true;
            maskDesc.clearColor = Color.white;
            maskDesc.filterMode = FilterMode.Point;
            TextureHandle maskTexture = renderGraph.CreateTexture(maskDesc);

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                ShaderTags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
            FilteringSettings filteringSettings = new FilteringSettings(
                RenderQueueRange.opaque, isHandsCamera ? 1 << 8 : -1,
                isHandsCamera ? uint.MaxValue : renderingLayers);

            NativeArray<ShaderTagId> tagValues = new NativeArray<ShaderTagId>(1, Allocator.Temp);
            tagValues[0] = ShaderTagId.none;
            NativeArray<RenderStateBlock> stateBlocks = new NativeArray<RenderStateBlock>(1, Allocator.Temp);
            stateBlocks[0] = SelectionState;

            RendererListParams parameters = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings)
            {
                tagValues = tagValues,
                stateBlocks = stateBlocks,
                isPassTagName = false
            };
            RendererListHandle rendererList = renderGraph.CreateRendererList(parameters);

            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Outline Selection Mask", out MaskPassData passData, profilingSampler))
            {
                passData.rendererList = rendererList;
                builder.UseRendererList(rendererList);
                builder.SetRenderAttachment(maskTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                    context.cmd.DrawRendererList(data.rendererList));
            }

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Outline Selection Composite", out CompositePassData passData, profilingSampler))
            {
                passData.maskTexture = maskTexture;
                passData.material = compositeMaterial;
                passData.properties = new MaterialPropertyBlock();
                passData.properties.SetColor(OutlineColorId, outlineMaterial.GetColor(OutlineColorId));
                passData.properties.SetFloat(OutlineThicknessId, outlineMaterial.GetFloat(OutlineThicknessId));
                passData.properties.SetFloat(NearOpacityId, outlineMaterial.GetFloat(NearOpacityId));
                passData.properties.SetFloat(FarOpacityId, outlineMaterial.GetFloat(FarOpacityId));
                passData.properties.SetFloat(FadeStartId, outlineMaterial.GetFloat(FadeStartId));
                passData.properties.SetFloat(FadeEndId, outlineMaterial.GetFloat(FadeEndId));
                passData.properties.SetFloat(ForceNearOpacityId, isHandsCamera ? 1f : 0f);
                builder.UseTexture(maskTexture, AccessFlags.Read);
                if (!isHandsCamera && resources.cameraDepthTexture.IsValid())
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(OutlineMaskId, data.maskTexture);
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, data.properties);
                });
            }
        }

        private static RenderStateBlock CreateSelectionState()
        {
            // Keep each material's alpha clipping while writing a binary mask.
            BlendState blendState = BlendState.defaultValue;
            blendState.blendState0 = new RenderTargetBlendState(
                sourceColorBlendMode: BlendMode.Zero,
                destinationColorBlendMode: BlendMode.Zero);

            return new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth)
            {
                blendState = blendState,
                depthState = new DepthState(false, CompareFunction.LessEqual)
            };
        }
    }
}
