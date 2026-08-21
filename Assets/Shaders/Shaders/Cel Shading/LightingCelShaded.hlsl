#ifndef LIGHTING_CEL_SHADED_INCLUDED
#define LIGHTING_CEL_SHADED_INCLUDED

#ifndef SHADERGRAPH_PREVIEW

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct EdgeConstants
{
    float diffuse;
    float specular;
    float specularOffset;

    float distanceAttenuation;
    float shadowAttenuation;

    float rim;
    float rimOffset;
};

struct SurfaceVariables
{
    float smoothness;
    float shininess;
    float rimThreshold;

    float3 normal;
    float3 view;

    EdgeConstants ec;
};


// ============================================================
// CALCULATE CEL SHADING
// ============================================================

float3 CalculateCelShading(Light l, SurfaceVariables s)
{
    // Evita smoothstep com edge == 0
    float shadowEdge = max(s.ec.shadowAttenuation, 0.0001f);
    float distanceEdge = max(s.ec.distanceAttenuation, 0.0001f);
    float diffuseEdge = max(s.ec.diffuse, 0.0001f);


    // --------------------------------------------------------
    // ATTENUATION
    // --------------------------------------------------------

    float shadowAttenuationSmoothstepped =
        smoothstep(
            0.0f,
            shadowEdge,
            l.shadowAttenuation
        );

    float distanceAttenuationSmoothstepped =
        smoothstep(
            0.0f,
            distanceEdge,
            l.distanceAttenuation
        );

    float attenuation =
        shadowAttenuationSmoothstepped *
        distanceAttenuationSmoothstepped;


    // --------------------------------------------------------
    // DIFFUSE
    // --------------------------------------------------------

    float diffuse =
        saturate(dot(s.normal, l.direction));

    diffuse *= attenuation;

    diffuse =
        smoothstep(
            0.0f,
            diffuseEdge,
            diffuse
        );


    // --------------------------------------------------------
    // SPECULAR
    // --------------------------------------------------------

    float3 h =
        SafeNormalize(l.direction + s.view);

    float specular =
        saturate(dot(s.normal, h));

    specular =
        pow(specular, s.shininess);

    specular *= diffuse;

    float specularMin =
        (1.0f - s.smoothness) *
        s.ec.specular +
        s.ec.specularOffset;

    float specularMax =
        s.ec.specular +
        s.ec.specularOffset;

    specular =
        s.smoothness *
        smoothstep(
            specularMin,
            specularMax,
            specular
        );


    // --------------------------------------------------------
    // RIM
    // --------------------------------------------------------

    float rim =
        1.0f -
        saturate(dot(s.view, s.normal));

    rim *=
        pow(
            max(diffuse, 0.0001f),
            s.rimThreshold
        );

    float rimMin =
        s.ec.rim -
        0.5f * s.ec.rimOffset;

    float rimMax =
        s.ec.rim +
        0.5f * s.ec.rimOffset;

    rim =
        smoothstep(
            rimMin,
            rimMax,
            rim
        );


    // --------------------------------------------------------
    // FINAL
    // --------------------------------------------------------

    return
        l.color *
        (diffuse + max(specular, rim));
}

#endif


// ============================================================
// SHADER GRAPH CUSTOM FUNCTION
// ============================================================

void LightingCelShaded_float(
    float Smoothness,
    float RimThreshold,

    float3 Position,
    float3 Normal,
    float3 View,

    float EdgeDiffuse,
    float EdgeSpecular,
    float EdgeSpecularOffset,

    float EdgeDistanceAttenuation,
    float EdgeShadowAttenuation,

    float EdgeRim,
    float EdgeRimOffset,

    out float3 Color)
{

#if defined(SHADERGRAPH_PREVIEW)

    Color = float3(0.5f, 0.5f, 0.5f);

#else

    // ========================================================
    // SURFACE DATA
    // ========================================================

    SurfaceVariables s;

    s.normal =
        normalize(Normal);

    s.view =
        SafeNormalize(View);

    s.smoothness =
        saturate(Smoothness);

    s.shininess =
        exp2(
            10.0f *
            s.smoothness +
            1.0f
        );

    s.rimThreshold =
        RimThreshold;


    // ========================================================
    // EDGE CONSTANTS
    // ========================================================

    s.ec.diffuse =
        EdgeDiffuse;

    s.ec.specular =
        EdgeSpecular;

    s.ec.specularOffset =
        EdgeSpecularOffset;

    s.ec.distanceAttenuation =
        EdgeDistanceAttenuation;

    s.ec.shadowAttenuation =
        EdgeShadowAttenuation;

    s.ec.rim =
        EdgeRim;

    s.ec.rimOffset =
        EdgeRimOffset;


    // ========================================================
    // MAIN LIGHT SHADOW COORDINATES
    // ========================================================

#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)

    float4 positionCS =
        TransformWorldToHClip(Position);

    float4 shadowCoord =
        ComputeScreenPos(positionCS);

#else

    float4 shadowCoord =
        TransformWorldToShadowCoord(Position);

#endif


    // ========================================================
    // MAIN LIGHT
    // ========================================================

    Light mainLight =
        GetMainLight(shadowCoord);

    Color =
        CalculateCelShading(
            mainLight,
            s
        );


    // ========================================================
    // INPUT DATA FOR FORWARD / FORWARD+
    // ========================================================

    InputData inputData =
        (InputData)0;

    inputData.positionWS =
        Position;

    inputData.normalWS =
        s.normal;

    inputData.viewDirectionWS =
        s.view;

    float4 positionCSForLights =
        TransformWorldToHClip(Position);

    inputData.normalizedScreenSpaceUV =
        GetNormalizedScreenSpaceUV(
            positionCSForLights
        );


    // ========================================================
    // ADDITIONAL LIGHTS
    // Point Lights / Spot Lights / extra Directional Lights
    // ========================================================

#if defined(_ADDITIONAL_LIGHTS)

    // --------------------------------------------------------
    // FORWARD+ / CLUSTER
    // Extra directional lights
    // --------------------------------------------------------

#if USE_CLUSTER_LIGHT_LOOP

    UNITY_LOOP
    for (
        uint lightIndex = 0;
        lightIndex <
            min(
                URP_FP_DIRECTIONAL_LIGHTS_COUNT,
                MAX_VISIBLE_LIGHTS
            );
        lightIndex++
    )
    {
        Light additionalLight =
            GetAdditionalLight(
                lightIndex,
                inputData.positionWS,
                half4(1, 1, 1, 1)
            );

        Color +=
            CalculateCelShading(
                additionalLight,
                s
            );
    }

#endif


    // --------------------------------------------------------
    // LOCAL ADDITIONAL LIGHTS
    // Point / Spot
    // Works with Forward and Forward+
    // --------------------------------------------------------

    uint pixelLightCount =
        GetAdditionalLightsCount();

    LIGHT_LOOP_BEGIN(pixelLightCount)

        Light additionalLight =
            GetAdditionalLight(
                lightIndex,
                inputData.positionWS,
                half4(1, 1, 1, 1)
            );

        Color +=
            CalculateCelShading(
                additionalLight,
                s
            );

    LIGHT_LOOP_END

#endif

#endif
}

#endif