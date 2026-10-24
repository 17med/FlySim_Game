// Stylized "half-toon" lit shader for URP 17 (Forward and Forward+).
// Property names _BaseMap/_BaseColor/_BumpMap/_BumpScale/_ORMMap/_OcclusionStrength match
// KitchenORM_Lit.shadergraph, so switching a kitchen material to this shader keeps its textures.
// ORM packing follows that graph: R = occlusion, G = roughness, B = metallic.
Shader "FlySimulator/StylizedToon"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        _ORMMap ("ORM Map (R occlusion, G roughness, B metallic)", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip (leaves, cutouts)", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Toon Shading)]
        _ShadowColor ("Shadow Tint", Color) = (0.42, 0.45, 0.62, 1)
        _MidColor ("Mid Tone Tint", Color) = (0.78, 0.74, 0.76, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.45
        _MidThreshold ("Mid Tone Threshold", Range(0, 1)) = 0.68
        _BandSoftness ("Band Softness", Range(0.001, 0.5)) = 0.06
        _Realism ("Realism (0 toon, 1 smooth)", Range(0, 1)) = 0.2
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1

        [Header(Highlight)]
        _HighlightColor ("Highlight Color", Color) = (1, 0.97, 0.9, 1)
        _HighlightStrength ("Highlight Strength", Range(0, 2)) = 0.6
        _HighlightSize ("Highlight Size", Range(0, 1)) = 0.35
        _HighlightSoftness ("Highlight Softness", Range(0.001, 0.5)) = 0.05

        [Header(Rim Light)]
        _RimColor ("Rim Color", Color) = (1, 0.95, 0.85, 1)
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.5
        _RimSize ("Rim Size", Range(0, 1)) = 0.3
        _RimSoftness ("Rim Softness", Range(0.001, 0.5)) = 0.08

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.12, 0.08, 0.07, 1)
        _OutlineWidth ("Outline Width (pixels, 0 = off)", Range(0, 8)) = 1.5
        _OutlineTextureTint ("Outline Takes Texture Color", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Every pass declares the same UnityPerMaterial buffer, which the SRP Batcher requires.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _OcclusionStrength;
            half _Cutoff;
            half4 _ShadowColor;
            half4 _MidColor;
            half _ShadowThreshold;
            half _MidThreshold;
            half _BandSoftness;
            half _Realism;
            half _AmbientStrength;
            half4 _HighlightColor;
            half _HighlightStrength;
            half _HighlightSize;
            half _HighlightSoftness;
            half4 _RimColor;
            half _RimStrength;
            half _RimSize;
            half _RimSoftness;
            half4 _OutlineColor;
            half _OutlineWidth;
            half _OutlineTextureTint;
        CBUFFER_END

        TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);    SAMPLER(sampler_BumpMap);
        TEXTURE2D(_ORMMap);     SAMPLER(sampler_ORMMap);

        half4 SampleBase(float2 uv)
        {
            half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            #if defined(_ALPHATEST_ON)
                clip(baseColor.a - _Cutoff);
            #endif
            return baseColor;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS    : TEXCOORD2;
                half4 tangentWS   : TEXCOORD3;
                half fogFactor    : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ToonSurface
            {
                half3 albedo;
                half3 normalWS;
                half3 viewDirWS;
                half occlusion;
                half smoothness;
                half metallic;
            };

            Varyings ToonVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = normalInputs.normalWS;
                half tangentSign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = half4(normalInputs.tangentWS, tangentSign);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            // Two soft steps (shadow -> mid -> lit) instead of a continuous gradient. _Realism
            // blends back towards a smooth falloff, which keeps the look away from flat cel shading.
            half3 ToonRamp(half lightAmount)
            {
                half shadowStep = smoothstep(_ShadowThreshold - _BandSoftness, _ShadowThreshold + _BandSoftness, lightAmount);
                half midStep = smoothstep(_MidThreshold - _BandSoftness, _MidThreshold + _BandSoftness, lightAmount);
                half3 banded = lerp(_ShadowColor.rgb, _MidColor.rgb, shadowStep);
                banded = lerp(banded, half3(1, 1, 1), midStep);
                half3 smoothRamp = lerp(_ShadowColor.rgb, half3(1, 1, 1), saturate(lightAmount));
                return lerp(banded, smoothRamp, _Realism);
            }

            half3 ToonHighlight(ToonSurface surface, half3 lightDirWS)
            {
                half3 halfDir = SafeNormalize(lightDirWS + surface.viewDirWS);
                half nDotH = saturate(dot(surface.normalWS, halfDir));
                // Rough surfaces (high ORM green) get a smaller, dimmer highlight.
                half size = _HighlightSize * surface.smoothness;
                half threshold = 1.0 - size * size * 0.5;
                half highlight = smoothstep(threshold - _HighlightSoftness, threshold + _HighlightSoftness, nDotH);
                half3 tint = lerp(_HighlightColor.rgb, surface.albedo, surface.metallic);
                return highlight * tint * _HighlightStrength * surface.smoothness;
            }

            half3 ShadeMainLight(ToonSurface surface, Light light)
            {
                // Half-Lambert keeps the shadow side readable; shadow attenuation pushes
                // cast shadows into the shadow band.
                half nDotL = dot(surface.normalWS, light.direction);
                half lightAmount = (nDotL * 0.5 + 0.5) * light.shadowAttenuation;
                half3 diffuse = surface.albedo * ToonRamp(lightAmount);
                half isLit = step(_ShadowThreshold, lightAmount);
                half3 highlight = ToonHighlight(surface, light.direction) * isLit;
                return (diffuse + highlight) * light.color;
            }

            // Point and spot lights get a single soft band and no shadow tint, so a lamp
            // never tints the far side of an object blue.
            half3 ShadeAdditionalLight(ToonSurface surface, Light light)
            {
                half nDotL = saturate(dot(surface.normalWS, light.direction));
                half band = smoothstep(_ShadowThreshold - _BandSoftness, _ShadowThreshold + _BandSoftness, nDotL);
                half lightAmount = lerp(band, nDotL, _Realism) * light.shadowAttenuation;
                return surface.albedo * light.color * light.distanceAttenuation * lightAmount;
            }

            half3 ShadeAdditionalLights(ToonSurface surface, float3 positionWS, float4 positionCS)
            {
                half3 color = 0;
                #if defined(_ADDITIONAL_LIGHTS)
                    half4 shadowMask = half4(1, 1, 1, 1);
                    // LIGHT_LOOP_BEGIN reads inputData in Forward+ (cluster) mode.
                    InputData inputData = (InputData)0;
                    inputData.positionWS = positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);

                    #if USE_CLUSTER_LIGHT_LOOP
                    // Forward+ keeps extra directional lights outside the cluster loop.
                    [loop] for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                    {
                        color += ShadeAdditionalLight(surface, GetAdditionalLight(dirIndex, positionWS, shadowMask));
                    }
                    #endif

                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        color += ShadeAdditionalLight(surface, GetAdditionalLight(lightIndex, positionWS, shadowMask));
                    LIGHT_LOOP_END
                #endif
                return color;
            }

            half3 ShadeRim(ToonSurface surface, Light mainLight)
            {
                half fresnel = 1.0 - saturate(dot(surface.normalWS, surface.viewDirWS));
                half rimThreshold = 1.0 - _RimSize;
                half rim = smoothstep(rimThreshold - _RimSoftness, rimThreshold + _RimSoftness, fresnel);
                // Rim only on the side facing the sun, so the shadow side stays calm.
                half litSide = saturate(dot(surface.normalWS, mainLight.direction) * 0.5 + 0.5) * mainLight.shadowAttenuation;
                return rim * litSide * _RimColor.rgb * _RimStrength * mainLight.color;
            }

            ToonSurface BuildSurface(Varyings input, half3 albedo)
            {
                ToonSurface surface;
                surface.albedo = albedo;

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                half3 normalWS = normalize(input.normalWS);
                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS = input.tangentWS.w * cross(normalWS, tangentWS);
                surface.normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(tangentWS, bitangentWS, normalWS)));
                surface.viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                half3 orm = SAMPLE_TEXTURE2D(_ORMMap, sampler_ORMMap, input.uv).rgb;
                surface.occlusion = lerp(1.0, orm.r, _OcclusionStrength);
                surface.smoothness = 1.0 - orm.g;
                surface.metallic = orm.b;
                return surface;
            }

            half4 ToonFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 baseColor = SampleBase(input.uv);
                ToonSurface surface = BuildSurface(input, baseColor.rgb);

                half occlusion = surface.occlusion;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    occlusion *= SampleAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                #endif

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));

                half3 ambient = SampleSH(surface.normalWS) * surface.albedo * occlusion * _AmbientStrength;
                half3 color = ambient;
                color += ShadeMainLight(surface, mainLight);
                color += ShadeAdditionalLights(surface, input.positionWS, input.positionCS);
                color += ShadeRim(surface, mainLight);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // Inverted hull outline: back faces pushed out along the normal, by a fixed number
        // of screen pixels so the line stays the same thickness near and far.
        // Width 0 puts the hull on the surface, where the front faces hide it.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half fogFactor    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = TransformWorldToHClipDir(normalWS).xy;
                // Multiplying by w cancels the perspective divide, giving a constant pixel width.
                float2 offset = SafeNormalize(float3(normalCS, 0)).xy * _OutlineWidth * 2.0 / _ScreenParams.xy;
                positionCS.xy += offset * positionCS.w;

                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionCS.z);
                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 albedo = SampleBase(input.uv).rgb;
                half3 color = _OutlineColor.rgb * lerp(half3(1, 1, 1), albedo, _OutlineTextureTint);
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP's ShadowUtils for the light currently rendering shadows.
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(positionCS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                SampleBase(input.uv);
                return 0;
            }
            ENDHLSL
        }

        // Depth and DepthNormals feed SSAO, depth priming and the camera depth texture.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half DepthFragment(Varyings input) : SV_Target
            {
                SampleBase(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3 normalWS    : TEXCOORD1;
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                SampleBase(input.uv);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
