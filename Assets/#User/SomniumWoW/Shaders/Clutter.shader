// SomniumWoW/Clutter - WoW 3.3.5a ground clutter (GroundEffectDoodad detail doodads: grass, flowers, pebbles), drawn GPU-instanced
// by Game/Play/Details (Graphics.RenderMeshInstanced, one draw per detail model and texture).
//
// The detail doodads MEASURED (Details report): one batch, blend mode 1 (AlphaKey) and render flag 0x04 (two-sided), 4-9
// triangles, a zone atlas texture (NewElynnGrassFlowerRock128.blp ...). So: alpha test (_Cutoff = 224/255 like M2Materials'
// AlphaKey), Cull Off, opaque queue AlphaTest. Mip levels average the alpha down, so the test uses alpha x (1 + 0.25 x mip
// level) (grass would otherwise thin out a few metres away); the mip level comes from the UV derivatives, the same in every pass.
//
// Vertex motion (the same function in every pass, so the depth prepass and the colour pass meet exactly - the client forces
// depth priming): a gentle wind sway growing with the height above the instance's base, and a distance fade that shrinks the
// instance into its base between _WowClutterFade.x and .y metres (horizontal) from _WowClutterCenter (the player; globals set
// by Details every frame) - no dithering, no blending, so it stays an opaque cutout.
//
// Lighting (PLAN rule 14): URP main directional light, Lambert + received shadows, plus SampleSH ambient, plus URP fog; the
// normal is bent towards up (thin grass cards lit like the ground they stand on, both faces alike).
// XR / client (PLAN rule 5): UniversalForward, ShadowCaster, DepthOnly and DepthNormals, single-pass stereo instancing in every
// pass but the shadow caster (shadow maps are not per eye). No shader keywords of our own.
Shader "SomniumWoW/Clutter"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour (rgb tint, a = alpha scale)", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha test (AlphaKey 224/255)", Float) = 0.878
        _WindStrength ("Wind sway (metres per metre of height)", Float) = 0.08
        _WindSpeed ("Wind speed", Float) = 1.4
        _UpNormal ("Normal bent towards up (0..1)", Float) = 0.65
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            half4 _Color;
            half _Cutoff;
            float _WindStrength;
            float _WindSpeed;
            half _UpNormal;
        CBUFFER_END

        // Set by Game/Play/Details every frame (Shader.SetGlobalVector): the player's Unity position, fade start/end (metres).
        float4 _WowClutterCenter;
        float4 _WowClutterFade;

        struct ClutterAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv0 : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // The one vertex position every pass uses.
        float3 ClutterPositionWS(ClutterAttributes input)
        {
            precise float3 baseWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
            precise float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
            float fadeRange = max(_WowClutterFade.y - _WowClutterFade.x, 0.001);
            float d = distance(baseWS.xz, _WowClutterCenter.xz);
            float s = 1.0 - saturate((d - _WowClutterFade.x) / fadeRange);
            float h = max(posWS.y - baseWS.y, 0.0);
            float phase = _Time.y * _WindSpeed + dot(baseWS.xz, float2(0.37, 0.23));
            float2 sway = float2(sin(phase), cos(phase * 0.83 + 1.3)) * (_WindStrength * h);
            posWS = baseWS + (posWS - baseWS) * s;
            posWS.xz += sway * s;
            return posWS;
        }

        float3 ClutterNormalWS(ClutterAttributes input)
        {
            float3 n = TransformObjectToWorldNormal(input.normalOS);
            return SafeNormalize(lerp(n, float3(0.0, 1.0, 0.0), _UpNormal));
        }

        // WoW UVs are used unchanged (PLAN rule 15).
        float2 ClutterUv(ClutterAttributes input)
        {
            return input.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
        }

        // Texel alpha, sharpened by the mip level, discarded under the alpha test (the same test in every pass).
        half ClutterClip(float2 uv, out half4 texel)
        {
            texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            float2 t = uv * _MainTex_TexelSize.zw;
            float2 dx = ddx(t), dy = ddy(t);
            float lod = 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-6));
            half a = texel.a * _Color.a * (half)(1.0 + max(lod, 0.0) * 0.25);
            clip(a - _Cutoff);
            return a;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Off

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

            Varyings Vert(ClutterAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = ClutterPositionWS(input);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = ClutterNormalWS(input);
                output.uv = ClutterUv(input);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 texel;
                ClutterClip(input.uv, texel);
                half3 albedo = texel.rgb * _Color.rgb;
                float3 normal = SafeNormalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = (half)saturate(dot(normal, light.direction));
                half3 colour = albedo * (light.color * diffuse * light.shadowAttenuation * light.distanceAttenuation + SampleSH(normal));
                colour = MixFog(colour, input.fog);
                return half4(colour, 1.0h);
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
            Cull Off

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

            ShadowVaryings ShadowVert(ClutterAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = ClutterPositionWS(input);
                float3 normalWS = ClutterNormalWS(input);
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
                output.uv = ClutterUv(input);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 texel;
                ClutterClip(input.uv, texel);
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
            Cull Off

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

            DepthVaryings DepthVert(ClutterAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformWorldToHClip(ClutterPositionWS(input));
                output.uv = ClutterUv(input);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 texel;
                ClutterClip(input.uv, texel);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // The depth prepass URP runs instead of DepthOnly when something asks for camera normals (the client's HBAO / SSAO):
        // with depth priming forced it is the pass colour is Equal-tested against. Pattern: URP 17.3 DepthNormalsPass.hlsl, M2.shader.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

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

            DepthNormalsVaryings DepthNormalsVert(ClutterAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformWorldToHClip(ClutterPositionWS(input));
                output.uv = ClutterUv(input);
                output.normalWS = NormalizeNormalPerVertex(ClutterNormalWS(input));
                return output;
            }

            void DepthNormalsFrag(DepthNormalsVaryings input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
                )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 texel;
                ClutterClip(input.uv, texel);
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
