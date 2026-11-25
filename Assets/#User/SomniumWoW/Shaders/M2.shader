// SomniumWoW/M2 - WoW 3.3.5a M2 models (characters, creatures, items, doodads).
//
// One texture per batch (the first texture of the M2 texture unit), WoW's blend modes as material state (set by
// Models/Unity/M2Materials.cs from M2Material.blending_mode; factors as noggit3, the 3.3.5 editor, sets them):
//   0 Opaque      One/Zero, no alpha test           4 Add        SrcAlpha/One, fog to black
//   1 AlphaKey    One/Zero, alpha test 224/255 * a  5 Mod        DstColor/Zero, fog to white
//   2 Alpha       SrcAlpha/OneMinusSrcAlpha         6 Mod2x      DstColor/SrcColor, fog to grey 0.5
//   3 NoAlphaAdd  One/One, fog to black
// Render flags: 0x01 unlit (_Unlit), 0x02 unfogged (_FogMode 4), 0x04 two-sided (_Cull 0), 0x10 no depth write (_ZWrite 0,
// blended modes never write depth). No shader keywords of our own: every switch is a float, so the upload's variant
// stripping cannot drop a variant only runtime materials ask for. Alpha test is clip(a - _Cutoff) with _Cutoff -1 = off.
//
// Lighting (PLAN rule 14): URP main directional light, Lambert + received shadows, plus SampleSH ambient, plus URP fog.
// XR / client (PLAN rule 5): UniversalForward, ShadowCaster, DepthOnly and DepthNormals, single-pass stereo instancing in
// every pass but the shadow caster (shadow maps are not per eye, as URP's ShadowCasterPass.hlsl). The client forces
// depth priming, so opaque colour is drawn ZTest Equal against DepthOnly/DepthNormals: all passes use the same M2Vertex
// position and the same M2Clip.
Shader "SomniumWoW/M2"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour (rgb tint, a = transparency)", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha test (-1 off, AlphaKey 224/255 x alpha)", Float) = -1
        _Unlit ("Unlit (render flag 0x01)", Float) = 0
        _FogMode ("Fog (0 normal, 1 to black, 2 to white, 3 to grey, 4 none)", Float) = 0
        _UvSet ("UV set (0 or 1)", Float) = 0
        _UvScroll ("UV scroll per second (xy)", Vector) = (0, 0, 0, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("Depth write", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Cutoff;
            half _Unlit;
            half _FogMode;
            float _UvSet;
            float4 _UvScroll;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
            float _Cull;
        CBUFFER_END

        struct M2Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv0 : TEXCOORD0;
            float2 uv1 : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // The one vertex position every pass uses.
        float3 M2PositionWS(M2Attributes input)
        {
            return TransformObjectToWorld(input.positionOS.xyz);
        }

        // WoW UVs are used unchanged (PLAN rule 15); UV animation = linear scroll.
        float2 M2Uv(M2Attributes input)
        {
            float2 uv = _UvSet > 0.5 ? input.uv1 : input.uv0;
            return uv * _MainTex_ST.xy + _MainTex_ST.zw + frac(_UvScroll.xy * _Time.y);
        }

        // Texel alpha x material alpha, discarded under the alpha test (the same test in every pass).
        half M2Clip(float2 uv)
        {
            half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a * _Color.a;
            clip(a - _Cutoff);
            return a;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(M2Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = M2PositionWS(input);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = M2Uv(input);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half alpha = texel.a * _Color.a;
                clip(alpha - _Cutoff);
                half3 albedo = texel.rgb * _Color.rgb;

                half3 colour = albedo;
                if (_Unlit < 0.5)
                {
                    // Two-sided batches light their back faces with the normal turned round.
                    float3 normal = SafeNormalize(input.normalWS) * IS_FRONT_VFACE(frontFace, 1.0, -1.0);
                    Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                    half diffuse = (half)saturate(dot(normal, light.direction));
                    colour = albedo * (light.color * diffuse * light.shadowAttenuation * light.distanceAttenuation + SampleSH(normal));
                }

                // Fog: normal colour; additive modes fade to black, Mod to white, Mod2x to grey, unfogged not at all.
                half3 fogColour = (half3)unity_FogColor.rgb;
                if (_FogMode > 0.5 && _FogMode < 1.5) fogColour = half3(0.0h, 0.0h, 0.0h);
                else if (_FogMode > 1.5 && _FogMode < 2.5) fogColour = half3(1.0h, 1.0h, 1.0h);
                else if (_FogMode > 2.5 && _FogMode < 3.5) fogColour = half3(0.5h, 0.5h, 0.5h);
                if (_FogMode < 3.5) colour = MixFogColor(colour, fogColour, input.fog);
                return half4(colour, alpha);
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(M2Attributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = M2PositionWS(input);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                output.positionCS = positionCS;
                output.uv = M2Uv(input);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                M2Clip(input.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma target 4.5

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(M2Attributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformWorldToHClip(M2PositionWS(input));
                output.uv = M2Uv(input);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                M2Clip(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // The depth prepass URP runs instead of DepthOnly when something asks for camera normals (the client's HBAO /
        // SSAO): with depth priming forced it is the pass colour is Equal-tested against. Pattern: URP 17.3
        // DepthNormalsPass.hlsl, ETS EtsLeaves.shader.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthNormalsVaryings DepthNormalsVert(M2Attributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformWorldToHClip(M2PositionWS(input));
                output.uv = M2Uv(input);
                output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            void DepthNormalsFrag(DepthNormalsVaryings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
                )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                M2Clip(input.uv);
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS * IS_FRONT_VFACE(frontFace, 1.0, -1.0)), 0.0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
