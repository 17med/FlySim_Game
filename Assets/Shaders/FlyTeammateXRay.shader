// The glow a fly sees on the other flies where a wall, a cupboard or anything else hides them: bright
// at the edges of the body and faint in the middle, so a teammate behind something reads as a
// light-blue shape exactly where they are. Used only by FlyTeammateXRay, which adds it to the other
// flies' renderers on a fly player's machine.
// It draws the mesh a second time and only where that mesh is behind something already drawn
// (ZTest Greater), writing no depth, so the part of a fly in plain view looks exactly as before and
// nothing else in the scene is changed by it. A fly's legs behind its own body also count as hidden,
// so a close fly shows a faint glow on its far legs; at the distances the glow is for that is not seen.
// Unlit and with no shadow pass: it is a marker, not a surface, and must not light, shade or shadow.
Shader "FlySimulator/FlyTeammateXRay"
{
    Properties
    {
        _GlowColor ("Glow Colour", Color) = (0.55, 0.85, 1, 1)
        _EdgeSharpness ("Edge Sharpness", Range(0.5, 8)) = 2.5
        _EdgeBrightness ("Edge Brightness", Range(0, 4)) = 1.6
        _MiddleBrightness ("Middle Brightness", Range(0, 1)) = 0.12
    }

    SubShader
    {
        // After every opaque surface, so the depth it is tested against already holds the walls.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "XRay"
            Tags { "LightMode" = "UniversalForward" }

            ZTest Greater
            ZWrite Off
            Cull Back
            // Added on top of whatever hides the fly, so the wall stays readable through the glow.
            Blend One One

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex XRayVertex
            #pragma fragment XRayFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _EdgeSharpness;
                half _EdgeBrightness;
                half _MiddleBrightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings XRayVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(position.positionWS);
                return output;
            }

            half4 XRayFragment(Varyings input) : SV_Target
            {
                // abs: some of the fly model's pieces were imported with their normals turned inside
                // out (the model's axes are flipped), and those would otherwise glow solid instead of
                // at their edges.
                half facing = abs(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                half edge = pow(saturate(1.0h - facing), _EdgeSharpness);
                half3 glow = _GlowColor.rgb * (_MiddleBrightness + edge * _EdgeBrightness);
                return half4(glow, 1.0h);
            }
            ENDHLSL
        }
    }
}
