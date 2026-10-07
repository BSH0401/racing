// Countryside ground: photographed grass and forest-floor soil (ambientCG), blended by the vertex
// colour's red channel and mapped from world XZ. Each texture is sampled at two scales to hide
// tiling over kilometres of open ground; green carries a broad brightness variation, blue dry grass.
Shader "Racing/TerrainBlend"
{
    Properties
    {
        _GrassMap("Grass", 2D) = "white" {}
        [Normal] _GrassNormal("Grass Normal", 2D) = "bump" {}
        _DirtMap("Soil", 2D) = "white" {}
        [Normal] _DirtNormal("Soil Normal", 2D) = "bump" {}
        _GrassTint("Grass Tint", Color) = (1, 1, 1, 1)
        _DirtTint("Soil Tint", Color) = (1, 1, 1, 1)
        _Tiling("Metres per repeat", Float) = 3.5
        _NormalStrength("Normal Strength", Range(0, 2)) = 0.8
        _Smoothness("Smoothness", Range(0, 1)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
            TEXTURE2D(_GrassNormal); SAMPLER(sampler_GrassNormal);
            TEXTURE2D(_DirtMap); SAMPLER(sampler_DirtMap);
            TEXTURE2D(_DirtNormal); SAMPLER(sampler_DirtNormal);

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassMap_ST, _GrassNormal_ST, _DirtMap_ST, _DirtNormal_ST;
                half4 _GrassTint, _DirtTint;
                float _Tiling;
                half _NormalStrength, _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            // Two scales, the coarse one rotated, so neither repeat pattern shows.
            half4 Sample2(TEXTURE2D_PARAM(tex, samp), float2 uv)
            {
                float2 uv2 = float2(uv.x * 0.8 - uv.y * 0.6, uv.x * 0.6 + uv.y * 0.8) * 0.29 + 0.37;
                return lerp(SAMPLE_TEXTURE2D(tex, samp, uv), SAMPLE_TEXTURE2D(tex, samp, uv2), 0.42);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.positionWS.xz / _Tiling;
                half dirt = saturate(i.color.r);
                half3 grass = Sample2(TEXTURE2D_ARGS(_GrassMap, sampler_GrassMap), uv).rgb * _GrassTint.rgb;
                half3 soil = Sample2(TEXTURE2D_ARGS(_DirtMap, sampler_DirtMap), uv * 0.8).rgb * _DirtTint.rgb;
                grass = lerp(grass, grass * half3(1.12, 1.0, 0.6), saturate(i.color.b)); // sun-dried patches
                half3 albedo = lerp(grass, soil, dirt) * (0.62 + 0.7 * i.color.g);

                half3 ng = UnpackNormalScale(SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassNormal, uv), _NormalStrength);
                half3 nd = UnpackNormalScale(SAMPLE_TEXTURE2D(_DirtNormal, sampler_DirtNormal, uv * 0.8), _NormalStrength);
                half3 nt = normalize(lerp(ng, nd, dirt));
                float3 n = normalize(i.normalWS);
                float3 t = normalize(float3(1, 0, 0) - n * n.x);
                float3 b = cross(t, n);
                float3 normalWS = normalize(t * nt.x + b * nt.y + n * nt.z);

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = normalWS;
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fogFactor;
                input.bakedGI = SampleSH(normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(input, surface);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
}
