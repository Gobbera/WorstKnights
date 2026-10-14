Shader "Hidden/WorstKnights/Outline Selection Composite"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 1)
        _Thickness ("Thickness (Pixels)", Range(0.5, 8)) = 2
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
            Name "Outline Selection Composite"
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

            TEXTURE2D_X(_WKOutlineSelectionMask);
            SAMPLER(sampler_WKOutlineSelectionMask);

            float4 _Color;
            float _Thickness;
            float _FadeStart;
            float _FadeEnd;
            float _NearOpacity;
            float _FarOpacity;
            float _ForceNearOpacity;

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

            float SelectionAt(float2 uv)
            {
                return 1.0 - SAMPLE_TEXTURE2D_X(_WKOutlineSelectionMask, sampler_WKOutlineSelectionMask, saturate(uv)).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 offset = _Thickness / _ScaledScreenParams.xy;
                float neighbor = 0.0;
                neighbor = max(neighbor, SelectionAt(input.uv + float2(offset.x, 0)));
                neighbor = max(neighbor, SelectionAt(input.uv - float2(offset.x, 0)));
                neighbor = max(neighbor, SelectionAt(input.uv + float2(0, offset.y)));
                neighbor = max(neighbor, SelectionAt(input.uv - float2(0, offset.y)));
                neighbor = max(neighbor, SelectionAt(input.uv + offset));
                neighbor = max(neighbor, SelectionAt(input.uv - offset));
                neighbor = max(neighbor, SelectionAt(input.uv + float2(offset.x, -offset.y)));
                neighbor = max(neighbor, SelectionAt(input.uv + float2(-offset.x, offset.y)));

                float edge = (1.0 - SelectionAt(input.uv)) * neighbor;
                float opacity = _NearOpacity;
                if (_ForceNearOpacity < 0.5)
                {
                    float depth = LinearEyeDepth(SampleSceneDepth(input.uv), _ZBufferParams);
                    float distanceLerp = saturate((depth - _FadeStart) / max(_FadeEnd - _FadeStart, 0.001));
                    opacity = lerp(_NearOpacity, _FarOpacity, distanceLerp);
                }
                return half4(_Color.rgb, _Color.a * edge * opacity);
            }
            ENDHLSL
        }
    }
}
