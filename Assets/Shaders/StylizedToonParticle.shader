// Transparent toon shader for the spit VFX, in the same family as FlySimulator/StylizedToon:
// _ShadowColor/_MidColor/_ShadowThreshold/_MidThreshold/_BandSoftness/_Realism and the highlight and
// rim properties mean the same thing and take the same values, so a droplet is banded by the sun
// exactly like the kitchen and the fly are.
// Three things make it a particle shader rather than a copy of the lit one:
//   - The blob shape is procedural, from the UV, so the VFX needs no texture at all. A base map is
//     still supported and multiplies on top. _SplatLobes bends the circle into a splat silhouette.
//   - Billboards all share one flat normal, which no toon ramp can read, so the normal is faked as a
//     sphere across the quad: every droplet shades as a little round ball with its own highlight.
//   - Main light only. Particles overdraw heavily and the project's look comes from the sun anyway,
//     so point and spot lights are skipped on purpose; ambient still picks up the room's tint.
// Blending is material state (_SrcBlend/_DstBlend), set by SpitTestSceneBuilder: alpha for the wet
// blobs, and the same shader goes additive by switching the two to One/One.
Shader "FlySimulator/StylizedToonParticle"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap ("Base Map (optional, multiplies the blob)", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (0.72, 0.85, 0.35, 1)
        [Toggle(_PROCEDURALBLOB_ON)] _ProceduralBlob ("Procedural Round Blob", Float) = 1
        _SoftEdge ("Blob Edge Softness", Range(0.001, 1)) = 0.35
        // 1 for a droplet in the air, which is a little ball. Low for a stain lying on a surface,
        // which should be lit by the surface it is stuck to, not like a ball facing the camera.
        _BlobRoundness ("Blob Roundness (1 ball, 0 flat on the surface)", Range(0, 1)) = 1
        _SplatLobes ("Splat Lobes (0 = round)", Range(0, 12)) = 0
        _SplatLobeDepth ("Splat Lobe Depth", Range(0, 0.5)) = 0.18
        _FresnelAlpha ("Edge Alpha (wet, glassy rim)", Range(0, 2)) = 0.4

        [Header(Toon Shading)]
        _ShadowColor ("Shadow Tint", Color) = (0.42, 0.45, 0.62, 1)
        _MidColor ("Mid Tone Tint", Color) = (0.78, 0.74, 0.76, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.45
        _MidThreshold ("Mid Tone Threshold", Range(0, 1)) = 0.68
        _BandSoftness ("Band Softness", Range(0.001, 0.5)) = 0.06
        _Realism ("Realism (0 toon, 1 smooth)", Range(0, 1)) = 0.2
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1

        [Header(Highlight)]
        _HighlightColor ("Highlight Color", Color) = (1, 0.99, 0.92, 1)
        _HighlightStrength ("Highlight Strength", Range(0, 4)) = 1.6
        _HighlightSize ("Highlight Size", Range(0, 1)) = 0.5
        _HighlightSoftness ("Highlight Softness", Range(0.001, 0.5)) = 0.06

        [Header(Rim Light)]
        _RimColor ("Rim Color", Color) = (1, 1, 0.9, 1)
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.7
        _RimSize ("Rim Size", Range(0, 1)) = 0.45
        _RimSoftness ("Rim Softness", Range(0.001, 0.5)) = 0.12

        [Header(Soft Particles)]
        // Off by default: it samples the camera depth texture, which the Mobile pipeline asset may
        // have switched off. Turn it on with Depth Texture enabled and droplets stop cutting hard
        // lines where they cross the floor.
        [Toggle(_SOFTPARTICLES_ON)] _SoftParticles ("Soft Particles (needs Depth Texture)", Float) = 0
        _SoftParticleFadeDistance ("Soft Particle Fade Distance", Range(0.001, 2)) = 0.15

        // Set from code, not the inspector: alpha blend by default, One/One for additive.
        [HideInInspector] _SrcBlend ("__src", Float) = 5
        [HideInInspector] _DstBlend ("__dst", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            // Billboards are single-sided quads and mesh-mode splats are seen from both sides.
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ParticleVertex
            #pragma fragment ParticleFragment

            #pragma shader_feature_local _PROCEDURALBLOB_ON
            #pragma shader_feature_local_fragment _SOFTPARTICLES_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _SoftEdge;
                half _BlobRoundness;
                half _SplatLobes;
                half _SplatLobeDepth;
                half _FresnelAlpha;
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
                half _SoftParticleFadeDistance;
                half _SrcBlend;
                half _DstBlend;
            CBUFFER_END

            TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // xy: the base map's UV, zw: the raw quad UV the blob shape is built from, which
                // tiling and offset must not move.
                float4 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color       : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
                half fogFactor    : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ParticleVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.screenPos = ComputeScreenPos(positionInputs.positionCS);
                output.uv = float4(TRANSFORM_TEX(input.uv, _BaseMap), input.uv);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            // Same two soft steps as StylizedToon's ToonRamp, so a droplet bands like the walls do.
            half3 ToonRamp(half lightAmount)
            {
                half shadowStep = smoothstep(_ShadowThreshold - _BandSoftness, _ShadowThreshold + _BandSoftness, lightAmount);
                half midStep = smoothstep(_MidThreshold - _BandSoftness, _MidThreshold + _BandSoftness, lightAmount);
                half3 banded = lerp(_ShadowColor.rgb, _MidColor.rgb, shadowStep);
                banded = lerp(banded, half3(1, 1, 1), midStep);
                half3 smoothRamp = lerp(_ShadowColor.rgb, half3(1, 1, 1), saturate(lightAmount));
                return lerp(banded, smoothRamp, _Realism);
            }

            // How far out the blob's edge sits in this direction. Lobes push it in and out around the
            // circle, which turns a splat into an uneven blot instead of a disc.
            // Two harmonics, not one: a single cosine gives every lobe the same width and depth, which
            // reads as a cog rather than as something that splattered. Both frequencies are whole
            // numbers so the silhouette still closes cleanly at the atan2 seam.
            half BlobEdge(float2 centred)
            {
                if (_SplatLobes < 0.5)
                    return 1.0;
                half angle = atan2(centred.y, centred.x);
                half second = floor(_SplatLobes * 0.6) + 1.0;
                half wobble = 0.62 * cos(angle * _SplatLobes) + 0.38 * cos(angle * second + 2.3);
                return 1.0 - _SplatLobeDepth * (0.5 - 0.5 * wobble);
            }

            // A billboard's own normal is useless for shading: every pixel of the quad shares it.
            // A sphere's normal across the quad instead, built in view space and rotated to world, so
            // the droplet reads as round and catches its own highlight and rim.
            // _BlobRoundness blends that back towards the object's own +Z, which the splash prefab
            // points straight out of the surface it hit: a stain then takes the light of the floor or
            // wall it is lying on instead of shading like a ball stuck to the camera, which is what
            // otherwise turns a stain on a bright floor into a near-black hole.
            half3 BlobNormalWS(float2 centred, half radius)
            {
                half3 sphereVS = half3(centred, -sqrt(saturate(1.0 - radius * radius)));
                // Column 2 of the inverse view matrix is the camera's forward in world space, so this
                // is the plain view-to-world rotation.
                half3 sphereWS = normalize(mul((float3x3)UNITY_MATRIX_I_V, sphereVS));
                half3 surfaceWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 0, 1)));
                return normalize(lerp(surfaceWS, sphereWS, _BlobRoundness));
            }

            half3 ShadeHighlight(half3 normalWS, half3 viewDirWS, half3 lightDirWS)
            {
                half3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
                half nDotH = saturate(dot(normalWS, halfDir));
                half threshold = 1.0 - _HighlightSize * _HighlightSize * 0.5;
                half highlight = smoothstep(threshold - _HighlightSoftness, threshold + _HighlightSoftness, nDotH);
                return highlight * _HighlightColor.rgb * _HighlightStrength;
            }

            half3 ShadeRim(half3 normalWS, half3 viewDirWS, Light mainLight)
            {
                half fresnel = 1.0 - saturate(dot(normalWS, viewDirWS));
                half rimThreshold = 1.0 - _RimSize;
                half rim = smoothstep(rimThreshold - _RimSoftness, rimThreshold + _RimSoftness, fresnel);
                half litSide = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5) * mainLight.shadowAttenuation;
                return rim * litSide * _RimColor.rgb * _RimStrength * mainLight.color;
            }

            half4 ParticleFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // -1..1 across the quad: 0 at the droplet's middle, 1 at its silhouette.
                float2 centred = input.uv.zw * 2.0 - 1.0;
                half radius = length(centred);

                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy) * _BaseColor * input.color;
                half alpha = baseColor.a;

                #if defined(_PROCEDURALBLOB_ON)
                    half edge = BlobEdge(centred);
                    // Fades in over _SoftEdge of the radius, so 1 is a soft puff and 0.001 a hard disc.
                    alpha *= 1.0 - smoothstep(edge * (1.0 - _SoftEdge), edge, radius);
                    // Outside the lobed silhouette there is nothing to light, and radius > 1 would
                    // make the faked sphere normal imaginary.
                    radius = min(radius, 1.0);
                #else
                    radius = min(radius, 1.0);
                #endif

                half3 normalWS = BlobNormalWS(centred, radius);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));

                // Half-Lambert, like the lit shader: the shadow side stays readable instead of black.
                half nDotL = dot(normalWS, mainLight.direction);
                half lightAmount = (nDotL * 0.5 + 0.5) * mainLight.shadowAttenuation;
                half isLit = step(_ShadowThreshold, lightAmount);

                half3 color = SampleSH(normalWS) * baseColor.rgb * _AmbientStrength;
                color += baseColor.rgb * ToonRamp(lightAmount) * mainLight.color;
                color += ShadeHighlight(normalWS, viewDirWS, mainLight.direction) * mainLight.color * isLit;
                color += ShadeRim(normalWS, viewDirWS, mainLight);

                // A wet blob is densest where you look through the most of it, which is the edge.
                alpha = saturate(alpha * (1.0 + _FresnelAlpha * radius * radius));

                #if defined(_SOFTPARTICLES_ON)
                    float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 1e-5);
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    alpha *= saturate((sceneDepth - input.screenPos.w) / _SoftParticleFadeDistance);
                #endif

                // Fogging towards the fog colour would brighten an additive blob, so fade it out
                // instead: with Blend One One the colour is the only thing that can carry the fog.
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
