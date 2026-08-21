#ifndef GET_MAIN_LIGHT_DIRECTION_INCLUDED
#define GET_MAIN_LIGHT_DIRECTION_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

void GetMainLightDirection_float(out float3 Direction)
{
#if SHADERGRAPH_PREVIEW

    Direction = float3(0.5, 0.5, 0);

#else

    Light light = GetMainLight();
    Direction = light.direction;

#endif
}

#endif