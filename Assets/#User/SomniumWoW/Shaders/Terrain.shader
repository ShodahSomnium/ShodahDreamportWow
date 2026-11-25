// WoW 3.3.5a terrain (ADT) for the SomniumWoW world. One mesh + one material per tile (256 chunks, 145 vertices each).
//
// Texturing follows the client's own ARB programs (patch.MPQ shaders\pixel\arbfp1\Terrain1.bls, read as text):
//   c = t0; c = lerp(c, t1, a.x); c = lerp(c, t2, a.y); c = lerp(c, t3, a.z)   (sequential, NOT a weighted sum)
//   diffuse *= 0.7 + 0.3 * a.w                                                  (a.w = 1 lit, 0 in the baked MCSH shadow)
//   primary  = lighting * MCCV (MCCV variant) or * 0.5, then * 2 in the fragment -> MCCV 0x7F = neutral.
// Textures repeat 8 times per chunk (noggit3 detail_size = 8). The alpha atlas holds 64x64 texels per chunk
// (R, G, B = layer 1, 2, 3 alpha, A = 1 - shadow); each chunk samples only its own texels, clamped like the client's
// per-chunk 64x64 texture (texel coordinate clamp(u * 64 - 0.5, 0, 63)).
// Layer textures come from the map-wide Texture2DArray _WowTerrainLayers (global, set by TerrainTextureArray); the slot
// of each of the chunk's 4 layers rides in TEXCOORD1 (UNorm8 x 255, 255 = no layer). Layer slot 0 = 255 marks the far
// (WDL) mesh: albedo = vertex colour (sRGB minimap colour).
// The client works in gamma space; in a linear project its gamma-space factors (shadow 0.7..1, MCCV x2) are raised to
// 2.2 so they darken the picture as much as they do in the game.
//
// Specular (client CVar "specular"): the layer slices are the tileset textures' _s.blp twins (TerrainTexturePrep), whose
// ALPHA is the specular mask. The client's programs (read as text from the MPQ):
//   vertex (arbvp1 Terrain.bls):  H = normalize(normalize(-posView) + L); spec = pow(max(dot(N, H), 0), c[27].w) * c[27].xyz
//   fragment (arbfp1 Terrain1..): lerp RGBA (ADD R0, R0, -R1; MAD R0, R2.x, R0, R1); ... MUL R1.xyz, R0.w, secondary;
//                                 MUL R1.xyz, R2.w, R1; MAD result.color.xyz, R0, 2, R1   -> + tex.a * spec * (1 - MCSH)
// c[27] is set by the client's code (not in a file). Reference for its value: WebWowViewerCpp adtShader_text.slang
//   lSpecular = uExteriorDirectColor * pow(max(0.0, dot(halfVec, normalize(vNormal))), 20.0); finalColor.rgb += specBlend * lSpecular
// i.e. colour = the sun's direct colour (LightIntBand 0 = our main light colour), exponent 20. Added in gamma space like
// the client (per pixel here, per vertex in the client), before fog; also x URP's received shadow like the diffuse.
// Global _WowTerrainSpecularOff (Settings "Terrain shine", WowLighting.Tick): 1 = no specular; unset = 0 = on.
//
// Lighting (PLAN rule 14): URP main light, Lambert x received shadow, + SampleSH ambient, clamped to 1 like the client's
// primary colour (MIN R1.xyz, R0, 1 in the vertex program), + specular, + URP fog.
// Passes (PLAN rule 5): UniversalForward, ShadowCaster, DepthOnly, DepthNormals; single-pass stereo instancing in all
// per-eye passes; identical clip-space positions (TransformObjectToHClip) in every pass (depth priming is forced).
Shader "SomniumWoW/Terrain"
{
    Properties
    {
        _AlphaMap ("Alpha atlas (RGB = layer 1-3 alpha, A = 1 - shadow)", 2D) = "white" {}
        _TexRepeat ("Texture repeats per chunk", Float) = 8
        _ShadowDark ("Baked shadow brightness (client 0.7)", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _AlphaMap_TexelSize;
            float _TexRepeat;
            float _ShadowDark;
        CBUFFER_END

        TEXTURE2D(_AlphaMap);
        SAMPLER(sampler_AlphaMap);
        TEXTURE2D_ARRAY(_WowTerrainLayers);
        SAMPLER(sampler_WowTerrainLayers);
        float _WowTerrainSpecularOff;
        float _WowTerrainSpecularStrength;   // ours: Settings "Shine strength" (unset = 0 = the client's x1)

        struct TerrainAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            float4 uv0 : TEXCOORD0;
            float4 layers : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 WowGammaFactor(float3 f)
        {
        #ifdef UNITY_COLORSPACE_GAMMA
            return f;
        #else
            return pow(max(f, 0.0), 2.2);
        #endif
        }

        // rgb = albedo (MCSH shade and MCCV applied), a = specular mask x (1 - MCSH shadow); the far mesh: vertex colour, 0
        float4 TerrainAlbedo(float4 uv0, float4 layers, float4 color)
        {
            float4 slot = floor(layers * 255.0 + 0.5);
            bool isFar = slot.x > 254.5;
            // missing layers (255) sample slot 0; their alpha is 0 in the atlas, so they add nothing
            float4 s = slot > 254.5 ? 0.0 : slot;
            float2 tuv = uv0.xy * _TexRepeat;
            float2 t = clamp(uv0.xy * 64.0 - 0.5, 0.0, 63.0);
            float2 auv = (uv0.zw * 64.0 + t + 0.5) * _AlphaMap_TexelSize.xy;
            float4 a = SAMPLE_TEXTURE2D_LOD(_AlphaMap, sampler_AlphaMap, auv, 0);
            float4 c = SAMPLE_TEXTURE2D_ARRAY(_WowTerrainLayers, sampler_WowTerrainLayers, tuv, s.x);
            c = lerp(c, SAMPLE_TEXTURE2D_ARRAY(_WowTerrainLayers, sampler_WowTerrainLayers, tuv, s.y), a.r);
            c = lerp(c, SAMPLE_TEXTURE2D_ARRAY(_WowTerrainLayers, sampler_WowTerrainLayers, tuv, s.z), a.g);
            c = lerp(c, SAMPLE_TEXTURE2D_ARRAY(_WowTerrainLayers, sampler_WowTerrainLayers, tuv, s.w), a.b);
            float shade = _ShadowDark + (1.0 - _ShadowDark) * a.a;
            c.rgb *= WowGammaFactor(float3(shade, shade, shade) * color.rgb * 2.0);
            float3 farColour = color.rgb;
        #ifndef UNITY_COLORSPACE_GAMMA
            farColour = SRGBToLinear(farColour);
        #endif
            return isFar ? float4(farColour, 0.0) : float4(c.rgb, c.a * a.a);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 uv0 : TEXCOORD2;
                nointerpolation float4 layers : TEXCOORD3;
                float4 color : TEXCOORD4;
                float fog : TEXCOORD5;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD6;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(TerrainAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs vp = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vp.positionCS;
                output.positionWS = vp.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv0 = input.uv0;
                output.layers = input.layers;
                output.color = input.color;
                output.fog = ComputeFogFactor(vp.positionCS.z);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vp);
            #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 albedoSpec = TerrainAlbedo(input.uv0, input.layers, input.color);
                float3 albedo = albedoSpec.rgb;
                float3 normalWS = SafeNormalize(input.normalWS);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                float4 shadowCoord = float4(0, 0, 0, 0);
            #endif
                Light mainLight = GetMainLight(shadowCoord);
                float nDotL = saturate(dot(normalWS, mainLight.direction));
                float sunLit = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                float3 colour = albedo * min(mainLight.color * (nDotL * sunLit) + SampleSH(normalWS), 1.0);
                // client specular: + tex.a x (1 - MCSH) x sun colour x pow(N.H, 20), added in gamma space. The client masks it with MCSH only
                // (already in albedoSpec.a), not with a real-time shadow: sunLit here took the shine off all ground inside Unity's shadow distance.
                float3 halfWS = SafeNormalize(mainLight.direction + GetWorldSpaceNormalizeViewDir(input.positionWS));
                float shineScale = _WowTerrainSpecularStrength > 0.0 ? _WowTerrainSpecularStrength : 1.0;
                float specAmount = albedoSpec.a * mainLight.distanceAttenuation * (1.0 - _WowTerrainSpecularOff) * shineScale * pow(saturate(dot(normalWS, halfWS)), 20.0);
            #ifdef UNITY_COLORSPACE_GAMMA
                colour += specAmount * mainLight.color;
            #else
                colour = SRGBToLinear(LinearToSRGB(colour) + specAmount * LinearToSRGB(mainLight.color));
            #endif
                colour = MixFog(colour, input.fog);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // Shadow maps are not per eye (URP ShadowCasterPass.hlsl carries only the instance id).
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(TerrainAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
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
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(TerrainAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Somnium's client forces depth priming: without this pass an opaque shader draws nothing there (PLAN rule 5).
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthNormalsVaryings DepthNormalsVert(TerrainAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));
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
