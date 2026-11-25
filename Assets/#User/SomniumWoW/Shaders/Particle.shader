// SomniumWoW/Particle - WoW 3.3.5a M2 particles and ribbons (Models/Unity/M2Effects.cs), drawn by Unity ParticleSystems
// (billboards, stretched billboards, horizontal billboards) and by the ribbon strip meshes.
//
// Colour = texture x vertex colour (the particle system's colour/alpha over life = WoW's colour/alpha part tracks) x _Color.
// Blend modes as material state, the same table WoW uses for particles (WebWowViewerCpp ParticleBlendingModeToEGxBlendEnum =
// the M2 material table): 0 Opaque One/Zero, 1 AlphaKey One/Zero + alpha test 0.502, 2 Alpha SrcAlpha/OneMinusSrcAlpha,
// 3 NoAlphaAdd One/One, 4 Add SrcAlpha/One, 5 Mod DstColor/Zero, 6 Mod2x DstColor/SrcColor, 7 BlendAdd One/OneMinusSrcAlpha.
// _ModMode 1 (Mod) / 2 (Mod2x) fade the colour toward the neutral value (white / grey) by alpha, so transparent texels
// leave the scene unchanged; _ModMode 3 (NoAlphaAdd, BlendAdd) multiplies the colour by alpha. Fog: 0 normal, 1 to
// black (additive), 2 to white (Mod), 3 to grey (Mod2x), 4 none (particle flag 0x8 unfogged).
//
// Transparent only (every material sits in the transparent queue, 3000 + priority plane, even alpha-keyed ones), so the
// client's forced depth priming never needs a DepthOnly / DepthNormals pass for it (PLAN rule 5: those matter for opaque
// shaders). Single-pass stereo instancing (UNITY_VERTEX_INPUT_INSTANCE_ID / UNITY_VERTEX_OUTPUT_STEREO / eye index) so it
// draws in both eyes. No shader keywords of our own (every switch is a float), so the upload's variant stripping cannot
// drop a variant only runtime materials ask for.
Shader "SomniumWoW/Particle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha test (-1 off, AlphaKey 0.502)", Float) = -1
        _FogMode ("Fog (0 normal, 1 to black, 2 to white, 3 to grey, 4 none)", Float) = 1
        _ModMode ("Colour mode (0 normal, 1 Mod, 2 Mod2x, 3 premultiply by alpha)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
        [Enum(Off, 0, On, 1)] _ZWrite ("Depth write", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Cutoff;
                half _FogMode;
                half _ModMode;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fog : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 c = texel * input.color * _Color;
                clip(c.a - _Cutoff);

                half3 colour = c.rgb;
                if (_ModMode > 0.5 && _ModMode < 1.5) colour = lerp(half3(1.0h, 1.0h, 1.0h), colour, c.a);
                else if (_ModMode > 1.5 && _ModMode < 2.5) colour = lerp(half3(0.5h, 0.5h, 0.5h), colour, c.a);
                else if (_ModMode > 2.5) colour *= c.a;

                half3 fogColour = (half3)unity_FogColor.rgb;
                if (_FogMode > 0.5 && _FogMode < 1.5) fogColour = half3(0.0h, 0.0h, 0.0h);
                else if (_FogMode > 1.5 && _FogMode < 2.5) fogColour = half3(1.0h, 1.0h, 1.0h);
                else if (_FogMode > 2.5 && _FogMode < 3.5) fogColour = half3(0.5h, 0.5h, 0.5h);
                if (_FogMode < 3.5) colour = MixFogColor(colour, fogColour, input.fog);
                return half4(colour, c.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
