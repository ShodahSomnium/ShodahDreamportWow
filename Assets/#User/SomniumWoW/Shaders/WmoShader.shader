// SomniumWoW/Wmo - WoW 3.3.5a buildings (WMO groups), written by World/Unity/WmoFactory + WmoMaterials.
//
// Lighting follows the 3.3.5a WMO shaders (wowdev WMO/Rendering + the "Unified Rendering" post, see World/WmoGeometry.cs):
//     light  = saturate(dirColor * saturate(n.L) + ambColor) * 0.5 + vertexColor ;  light = saturate(light + emissive)
//     colour = texture * light * 2                                                  (all in gamma space in the client)
// WmoGeometry folds the per-batch lighting mode into the vertex colour: rgb = the vertexColor term (the group's baked MOCV
// after the client's FixColorVertexAlpha, plus 0.5 x MOHD ambient where the unified path adds it), a = how much of the
// exterior term applies (1 exterior / window batches, 0 interior batches, the MOCV alpha on trans batches, 0 unlit).
// The exterior term is PLAN rule 14's: URP main light (Lambert x received shadow) + SampleSH ambient, which Front's
// WowLighting sets from the zone's Light.dbc. The project renders in linear space, so the exterior term is taken to gamma
// (pow 1/2.2), combined with the gamma vertex colour as the client does, and the doubled result brought back (pow 2.2):
// a purely exterior surface therefore gets exactly texture x (sun + ambient), like the terrain and M2 shaders.
//
// Material shaders (MOMT.shader, 3.3.5a uses 0-6): Diffuse/Specular/Metal/Env/EnvMetal are all drawn as Diffuse
// (texture 1 only - no specular or environment map: an approximation); TwoLayerDiffuse (6) blends texture 2 (second UV set)
// over texture 1 by texture 2's alpha, and that back towards texture 1 by the second MOCV alpha (TEXCOORD2.x):
//     layer2 = lerp(t1, t2, t2.a);  diffuse = lerp(layer2, t1, blend)
// Blend modes: WmoMaterials sets _SrcBlend/_DstBlend/_ZWrite/_AlphaClip/_AlphaBlend and the render queue per EGxBlend.
// Two-sided materials (F_UNCULLED) use Cull Off and light the back face with the flipped normal.
// Emissive: _Emissive = MOMT sidnColor (F_SIDN materials) x the global _WowSidn (0 until something sets it: night glow).
//
// Somnium client rules (PLAN rule 5): Forward + ShadowCaster + DepthOnly + DepthNormals (depth priming is forced, an opaque
// shader without DepthNormals draws nothing), single-pass stereo instancing in every pass that draws for the camera,
// identical clip-space position math in all passes, URP fog. The material buffer is identical in every pass (SRP batcher).
Shader "SomniumWoW/Wmo"
{
    Properties
    {
        _BaseMap ("Texture 1 (uv0)", 2D) = "white" {}
        _Layer2Map ("Texture 2 (uv1, two-layer)", 2D) = "black" {}
        _TwoLayer ("Two-layer diffuse (MOMT shader 6)", Float) = 0
        _AlphaClip ("Alpha test (AlphaKey)", Float) = 0
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _AlphaBlend ("Output texture alpha (blended modes)", Float) = 0
        _Unfogged ("No fog (F_UNFOGGED)", Float) = 0
        _Emissive ("SIDN emissive (gamma)", Color) = (0, 0, 0, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_Layer2Map);
        SAMPLER(sampler_Layer2Map);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _Layer2Map_ST;
            half4 _Emissive;
            half _TwoLayer;
            half _AlphaClip;
            half _Cutoff;
            half _AlphaBlend;
            half _Unfogged;
        CBUFFER_END

        // Set globally (Shader.SetGlobalFloat) by whoever runs the day/night cycle; 0 = no window glow.
        half _WowSidn;

        // One clip-space formula for every pass (depth priming compares Forward against the depth passes bit for bit).
        float4 WmoClipPos(float3 positionOS, out float3 positionWS)
        {
            positionWS = TransformObjectToWorld(positionOS);
            return TransformWorldToHClip(positionWS);
        }

        void WmoAlphaTest(float2 uv)
        {
            if (_AlphaClip > 0.5h)
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a - _Cutoff);
            }
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 blend : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;          // xy texture 1, zw texture 2
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float2 extra : TEXCOORD3;       // x two-layer blend, y fog factor
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS;
                output.positionCS = WmoClipPos(input.positionOS.xyz, positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = float4(TRANSFORM_TEX(input.uv0, _BaseMap), TRANSFORM_TEX(input.uv1, _Layer2Map));
                output.extra = float2(input.blend.x, ComputeFogFactor(output.positionCS.z));
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 t1 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy);
                half3 diffuse = t1.rgb;
                if (_TwoLayer > 0.5h)
                {
                    half4 t2 = SAMPLE_TEXTURE2D(_Layer2Map, sampler_Layer2Map, input.uv.zw);
                    half3 layer2 = lerp(t1.rgb, t2.rgb, t2.a);
                    diffuse = lerp(layer2, t1.rgb, (half)saturate(input.extra.x));
                }
                if (_AlphaClip > 0.5h)
                {
                    clip(t1.a - _Cutoff);
                }

                half3 normalWS = SafeNormalize(input.normalWS);
                normalWS = IS_FRONT_VFACE(face, normalWS, -normalWS);

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half3 exterior = saturate(mainLight.color * (nDotL * mainLight.shadowAttenuation * mainLight.distanceAttenuation) + SampleSH(normalWS));

                // gamma-space combination as in the client (see the header), then back to linear
                half3 exteriorGamma = PositivePow(exterior, 1.0h / 2.2h);
                half3 lightGamma = saturate(0.5h * input.color.a * exteriorGamma + input.color.rgb + _Emissive.rgb * _WowSidn);
                half3 colour = diffuse * PositivePow(2.0h * lightGamma, 2.2h);

                if (_Unfogged < 0.5h)
                {
                    colour = MixFog(colour, input.extra.y);
                }
                return half4(colour, _AlphaBlend > 0.5h ? t1.a : 1.0h);
            }
            ENDHLSL
        }

        // Shadow maps are not per eye (URP's own ShadowCasterPass carries only the instance id).
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv0, _BaseMap);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                WmoAlphaTest(input.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS;
                output.positionCS = WmoClipPos(input.positionOS.xyz, positionWS);
                output.uv = TRANSFORM_TEX(input.uv0, _BaseMap);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                WmoAlphaTest(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // The prepass URP runs instead of DepthOnly when something needs camera normals (SSAO/HBAO); with depth priming
        // forced this is the pass the colour pass is Equal-tested against. Pattern: URP 17.3 DepthNormalsPass.hlsl.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS;
                output.positionCS = WmoClipPos(input.positionOS.xyz, positionWS);
                output.uv = TRANSFORM_TEX(input.uv0, _BaseMap);
                output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            void DepthNormalsFrag(DepthNormalsVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
                )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                WmoAlphaTest(input.uv);

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                outNormalWS = half4(IS_FRONT_VFACE(face, normalWS, -normalWS), 0.0);
                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
