// WoW's sky dome (owner: Front). Drawn by WowLighting on a sphere that follows the camera.
// Colours: LightIntBand bands 2..6 (sky top .. horizon) and 7 (fog, below the horizon), keyed by elevation at
// {90, 30, 15, 5, 0, -30, -90} degrees (noggit3 Sky.cpp). Sun/moon disc from band 9, halo from band 10.
// It sits in the first transparent queue (2501) with ZWrite Off and its depth pushed to the far plane, so it is
// drawn after every opaque (the client forces depth priming: an opaque-queue shader without a depth prepass
// would be rejected by ZTest Equal) and only where nothing else was drawn, and before water (3000).
// Single-pass stereo instancing macros so it draws in both eyes.
Shader "SomniumWoW/Sky"
{
    Properties
    {
        _SkyTop ("Sky top (band 2)", Color) = (0.00, 0.12, 0.29, 1)
        _SkyMid ("Sky middle (band 3)", Color) = (0.23, 0.64, 0.81, 1)
        _SkyBand1 ("Sky middle to horizon (band 4)", Color) = (0.60, 0.86, 0.96, 1)
        _SkyBand2 ("Sky above horizon (band 5)", Color) = (0.69, 0.85, 0.88, 1)
        _SkyHorizon ("Sky horizon (band 6)", Color) = (0.71, 0.71, 0.71, 1)
        _SkyFog ("Below horizon (band 7, fog)", Color) = (0.30, 0.47, 0.56, 1)
        _SunColor ("Sun or moon (band 9)", Color) = (1, 0.97, 0.87, 1)
        _SunHalo ("Sun halo (band 10)", Color) = (1, 0.78, 0.54, 1)
        _SunDir ("Direction to the sun or moon (world)", Vector) = (0, 0.7, 0.7, 0)
        _SunSize ("Disc sharpness", Float) = 2000
    }

    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Transparent-499" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyTop;
                half4 _SkyMid;
                half4 _SkyBand1;
                half4 _SkyBand2;
                half4 _SkyHorizon;
                half4 _SkyFog;
                half4 _SunColor;
                half4 _SunHalo;
                float4 _SunDir;
                float _SunSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                // At the far plane whatever the dome's radius or the camera's far clip.
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1.0e-6;
                #else
                    o.positionCS.z = o.positionCS.w * (1.0 - 1.0e-6);
                #endif
                o.dirOS = v.positionOS.xyz;
                return o;
            }

            half3 Band(half3 a, half3 b, float x0, float x1, float x)
            {
                return lerp(a, b, saturate((x - x0) / (x1 - x0)));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 dir = normalize(i.dirOS);
                float elevation = degrees(asin(clamp(dir.y, -1.0, 1.0)));
                half3 c;
                if (elevation >= 30.0) c = Band(_SkyMid.rgb, _SkyTop.rgb, 30.0, 90.0, elevation);
                else if (elevation >= 15.0) c = Band(_SkyBand1.rgb, _SkyMid.rgb, 15.0, 30.0, elevation);
                else if (elevation >= 5.0) c = Band(_SkyBand2.rgb, _SkyBand1.rgb, 5.0, 15.0, elevation);
                else if (elevation >= 0.0) c = Band(_SkyHorizon.rgb, _SkyBand2.rgb, 0.0, 5.0, elevation);
                else c = Band(_SkyHorizon.rgb, _SkyFog.rgb, 0.0, -30.0, elevation);

                float3 sunDir = normalize(_SunDir.xyz + float3(0.0, 1.0e-5, 0.0));
                float d = saturate(dot(dir, sunDir));
                float above = saturate(elevation * 0.5 + 1.0);
                half disc = (half)saturate(pow(d, _SunSize) * 3.0);
                half halo = (half)(pow(d, 24.0) * 0.35);
                c = lerp(c, _SunColor.rgb, disc * above) + _SunHalo.rgb * halo * above;
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
