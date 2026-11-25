// WoW liquids (terrain MH2O / legacy MCLQ, and WMO MLIQ through WowWater.ForLiquidType) for the SomniumWoW world.
//
// Water / ocean follow the 3.3.5a editor noggit3's liquid shader (liquid_frag.glsl), which reproduces the client:
//   colour = saturate(frame + lerp(light, dark, depth)), alpha = lerp(light.a, dark.a, depth)
// where frame = the liquid's animated texture (LiquidType.dbc Texture "...%d.blp", frames 1..N, a Texture2DArray),
// depth = the MH2O depth byte / 255 (or surface - terrain, 10 yards = 1, when the file has none) in vertex colour alpha,
// light/dark = river or ocean colours (WowWater.SetColors; defaults = noggit3's). The texture repeats every 4 liquid
// cells (noggit3 default_uv). Magma and slime are drawn opaque and unlit from their frames (they glow in the game).
// Water is unlit like in the client (colours from Light.dbc via WowWater.SetColors) but scaled by the brightness (not the
// colour) of URP main light + SampleSH ambient so it darkens at night; URP fog.
// Transparent: no depth passes needed (URP's depth prepass only takes opaque queues); drawn from both sides.
// Single-pass stereo instancing supported.
// Lead 2026-10-26 (user: "fix the water shader as it is barely visible"): the client's water is not a flat tint - it reflects the sky at
// grazing angles and glints in the sun (its "Liquid Detail"). Added on water / ocean: a normal from the animated frame's brightness
// gradient (the lake/ocean frames are height-like noise), the zone-sky reflection (unity_SpecCube0 = WowLighting.PushReflection's
// sky cubemap) by Schlick Fresnel (F0 0.02), a Blinn sun glint, and the opacity raised to the Fresnel term (glancing water is opaque).
// Global _WowWaterStrength (Settings > Effects "Water visibility", 0 when unset = x1) scales the reflection and the opacity.
Shader "SomniumWoW/Water"
{
    Properties
    {
        _Frames ("Animation frames", 2DArray) = "" {}
        _FrameCount ("Frame count", Float) = 1
        _FramesPerSecond ("Frames per second", Float) = 15
        _Kind ("Kind (0 water, 1 ocean, 2 magma, 3 slime)", Float) = 0
        _ColorLight ("Shallow colour (a = shallow opacity)", Color) = (0.3, 0.3, 0.4, 0.5)
        _ColorDark ("Deep colour (a = deep opacity)", Color) = (0.2, 0.2, 0.3, 0.9)
        _Scroll ("Magma/slime scroll (uv per second)", Vector) = (0.01, 0.02, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _FrameCount;
                float _FramesPerSecond;
                float _Kind;
                float4 _ColorLight;
                float4 _ColorDark;
                float4 _Scroll;
            CBUFFER_END

            TEXTURE2D_ARRAY(_Frames);
            SAMPLER(sampler_Frames);
            float _WowWaterStrength;   // global (WowLighting); 0 = unset = 1

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float depth : TEXCOORD3;
                float fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs vp = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vp.positionCS;
                output.positionWS = vp.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.depth = input.color.a;
                output.fog = ComputeFogFactor(vp.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float count = max(1.0, _FrameCount);
                float frame = fmod(floor(_Time.y * _FramesPerSecond), count);
                bool molten = _Kind > 1.5;
                float2 uv = molten ? input.uv + _Scroll.xy * _Time.y : input.uv;
                float3 tex = SAMPLE_TEXTURE2D_ARRAY(_Frames, sampler_Frames, uv, frame).rgb;
                float3 colour;
                float alpha;
                if (molten)
                {
                    colour = tex;
                    alpha = 1.0;
                }
                else
                {
                    float d = saturate(input.depth);
                    float4 tint = lerp(_ColorLight, _ColorDark, d);
                    colour = saturate(tex + tint.rgb);
                    alpha = tint.a;
                    // The client draws water unlit (its colours come from Light.dbc for the time of day, see WowWater.SetColors).
                    // To still darken it at night without tinting it with the sun's colour, scale by the brightness only.
                    float3 up = float3(0.0, 1.0, 0.0);
                    Light mainLight = GetMainLight();
                    float3 lit = mainLight.color * saturate(dot(up, mainLight.direction)) + SampleSH(up);
                    float bright = clamp(Luminance(lit), 0.2, 1.0);
                    colour *= bright;

                    // sky reflection + sun glint (lead 2026-10-26)
                    float strength = _WowWaterStrength > 0.0 ? _WowWaterStrength : 1.0;
                    float2 du = float2(1.0 / 48.0, 0.0), dv = float2(0.0, 1.0 / 48.0);
                    float h0 = dot(tex, float3(0.3333, 0.3333, 0.3333));
                    float hu = dot(SAMPLE_TEXTURE2D_ARRAY(_Frames, sampler_Frames, uv + du, frame).rgb, float3(0.3333, 0.3333, 0.3333));
                    float hv = dot(SAMPLE_TEXTURE2D_ARRAY(_Frames, sampler_Frames, uv + dv, frame).rgb, float3(0.3333, 0.3333, 0.3333));
                    float3 n = normalize(float3((h0 - hu) * 1.5, 1.0, (h0 - hv) * 1.5));
                    float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    if (v.y < 0.0) n.y = -n.y;                      // seen from under the surface
                    float nv = saturate(dot(n, v));
                    float fresnel = 0.02 + 0.98 * pow(1.0 - nv, 5.0);
                    float3 sky = GlossyEnvironmentReflection(reflect(-v, n), 0.08, 1.0);
                    colour = lerp(colour, sky, saturate(fresnel * strength));
                    float3 hdir = normalize(mainLight.direction + v);
                    float glint = pow(saturate(dot(n, hdir)), 180.0) * 3.0 * saturate(mainLight.direction.y * 3.0);
                    colour += mainLight.color * glint * strength;
                    alpha = saturate(max(alpha * strength, saturate(fresnel * 1.5 * strength)) + glint);
                }
                colour = MixFog(colour, input.fog);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
