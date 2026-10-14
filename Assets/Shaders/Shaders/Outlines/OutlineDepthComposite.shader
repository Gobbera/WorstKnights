Shader "Hidden/WorstKnights/Outline Depth Composite"
{
    Properties
    {
        _Color ("Outline Color", Color) = (0, 0, 0, 1)
        _Thickness ("Thickness at 1 m (Pixels)", Range(0.5, 8)) = 2
        _DepthThreshold ("Depth Threshold (Meters)", Range(0.001, 1)) = 0.05
        _FadeStart ("Distance Fade Start (Meters)", Float) = 20
        _FadeEnd ("Distance Fade End (Meters)", Float) = 200
        _NearOpacity ("Near Opacity", Range(0, 1)) = 1
        _FarOpacity ("Far Opacity", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "World Outline"
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask RGB
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _Color;
            float _Thickness;
            float _DepthThreshold;
            float _FadeStart;
            float _FadeEnd;
            float _NearOpacity;
            float _FarOpacity;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.uv = GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }

            float EyeDepthAt(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(saturate(uv)), _ZBufferParams);
            }

            float OutlineOpacity(float eyeDepth)
            {
                float distanceLerp = saturate((eyeDepth - _FadeStart) / max(_FadeEnd - _FadeStart, 0.001));
                return lerp(_NearOpacity, _FarOpacity, distanceLerp);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float depth = EyeDepthAt(input.uv);
                if (depth >= _ProjectionParams.z - 0.01)
                    return half4(0, 0, 0, 0);

                float radiusPixels = clamp(_Thickness / max(depth, 0.5), min(_Thickness, 1.0), 8.0);
                float2 offset = radiusPixels / _ScaledScreenParams.xy;
                float edgeDepth = 0;
                edgeDepth = max(edgeDepth, EyeDepthAt(input.uv + float2(offset.x, 0)) - depth);
                edgeDepth = max(edgeDepth, EyeDepthAt(input.uv - float2(offset.x, 0)) - depth);
                edgeDepth = max(edgeDepth, EyeDepthAt(input.uv + float2(0, offset.y)) - depth);
                edgeDepth = max(edgeDepth, EyeDepthAt(input.uv - float2(0, offset.y)) - depth);
                edgeDepth = max(edgeDepth, EyeDepthAt(input.uv + offset) - depth);
                edgeDepth = max(edgeDepth, EyeDepthAt(input.uv - offset) - depth);
                edgeDepth = max(edgeDepth, EyeDepthAt(input.uv + float2(offset.x, -offset.y)) - depth);
                edgeDepth = max(edgeDepth, EyeDepthAt(input.uv + float2(-offset.x, offset.y)) - depth);

                float threshold = _DepthThreshold * (1 + depth * 0.01);
                float edge = smoothstep(threshold, threshold * 1.5, edgeDepth);
                return half4(_Color.rgb, _Color.a * edge * OutlineOpacity(depth));
            }
            ENDHLSL
        }
    }
}
