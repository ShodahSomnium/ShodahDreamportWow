// Made with Amplify Shader Editor v1.9.9.12
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Community/VFog Cone March Noise"
{
	Properties
	{
		[HDR][Header(Coloring)] _ColorA( "ColorA", Color ) = ( 0.4735848, 1, 1 )
		[HDR] _ColorB( "ColorB", Color ) = ( 1, 1, 1 )
		_GradientCurve( "GradientCurve", Range( 0.1, 1 ) ) = 0.4
		_Brighness( "Brighness", Float ) = 0
		[Header(Cone Falloff)] _InnerAngle( "InnerAngle", Range( 0, 1 ) ) = 0.5
		_ApexSoftness( "ApexSoftness", Range( 0, 0.25 ) ) = 0
		[Header(Distance Falloff)] _CoreBoost( "CoreBoost", Range( 0, 32 ) ) = 20
		_CoreRadius( "CoreRadius", Range( 0.02, 1 ) ) = 0.025
		_DistanceCurve( "DistanceCurve", Range( 0.01, 8 ) ) = 6.5
		[Header(3D Noise)] _NoiseSpeed( "NoiseSpeed", Vector ) = ( 0, -0.05, 0, 0 )
		_NoiseScale( "NoiseScale", Vector ) = ( 15, 15, 15, 0 )
		[Header(Ray Marching)][IntRange] _Steps( "Steps", Range( 1, 128 ) ) = 12
		_DitherStrength( "DitherStrength", Range( 0, 1 ) ) = 1


		//_TessPhongStrength( "Tess Phong Strength", Range( 0, 1 ) ) = 0.5
		//_TessValue( "Tess Max Tessellation", Range( 1, 32 ) ) = 16
		//_TessMin( "Tess Min Distance", Float ) = 10
		//_TessMax( "Tess Max Distance", Float ) = 25
		//_TessEdgeLength ( "Tess Edge length", Range( 2, 50 ) ) = 16
		//_TessMaxDisp( "Tess Max Displacement", Float ) = 25

		[HideInInspector] _QueueOffset("_QueueOffset", Float) = 0
        [HideInInspector] _QueueControl("_QueueControl", Float) = -1

        [HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}

		//[HideInInspector][ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0

		[HideInInspector][ToggleUI] _AddPrecomputedVelocity("Add Precomputed Velocity", Float) = 1
		[HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1

		//[HideInInspector] _AlphaClip("__clip", Float) = 0.0
	}

	SubShader
	{
		PackageRequirements
		{
			"com.unity.render-pipelines.universal": "[17.0,18.0]"
		}

		

		

		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Unlit" }

	LOD 0

		ZWrite Off
		Cull Front
		AlphaToMask Off
		ColorMask RGBA
		Blend One Zero, One Zero
		BlendOp Add, Add

		

		HLSLINCLUDE
		#pragma target 4.5
		#pragma prefer_hlslcc gles
		// ensure rendering platforms toggle list is visible

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

		#define ASE_ADJUST_CLIP_POSITION( x ) x

		#ifndef ASE_TESS_FUNCS
		#define ASE_TESS_FUNCS
		float4 FixedTess( float tessValue )
		{
			return tessValue;
		}

		float CalcDistanceTessFactor (float4 vertex, float minDist, float maxDist, float tess, float4x4 o2w, float3 cameraPos )
		{
			float3 wpos = mul(o2w,vertex).xyz;
			float dist = distance (wpos, cameraPos);
			float f = clamp(1.0 - (dist - minDist) / (maxDist - minDist), 0.01, 1.0) * tess;
			return f;
		}

		float4 CalcTriEdgeTessFactors (float3 triVertexFactors)
		{
			float4 tess;
			tess.x = 0.5 * (triVertexFactors.y + triVertexFactors.z);
			tess.y = 0.5 * (triVertexFactors.x + triVertexFactors.z);
			tess.z = 0.5 * (triVertexFactors.x + triVertexFactors.y);
			tess.w = (triVertexFactors.x + triVertexFactors.y + triVertexFactors.z) / 3.0f;
			return tess;
		}

		float CalcEdgeTessFactor (float3 wpos0, float3 wpos1, float edgeLen, float3 cameraPos, float4 scParams )
		{
			float dist = distance (0.5 * (wpos0+wpos1), cameraPos);
			float len = distance(wpos0, wpos1);
			float f = max(len * scParams.y / (edgeLen * dist), 1.0);
			return f;
		}

		float DistanceFromPlane (float3 pos, float4 plane)
		{
			float d = dot (float4(pos,1.0f), plane);
			return d;
		}

		bool WorldViewFrustumCull (float3 wpos0, float3 wpos1, float3 wpos2, float cullEps, float4 planes[6] )
		{
			float4 planeTest;
			planeTest.x = (( DistanceFromPlane(wpos0, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[0]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.y = (( DistanceFromPlane(wpos0, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[1]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.z = (( DistanceFromPlane(wpos0, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[2]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.w = (( DistanceFromPlane(wpos0, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[3]) > -cullEps) ? 1.0f : 0.0f );
			return !all (planeTest);
		}

		float4 DistanceBasedTess( float4 v0, float4 v1, float4 v2, float tess, float minDist, float maxDist, float4x4 o2w, float3 cameraPos )
		{
			float3 f;
			f.x = CalcDistanceTessFactor (v0,minDist,maxDist,tess,o2w,cameraPos);
			f.y = CalcDistanceTessFactor (v1,minDist,maxDist,tess,o2w,cameraPos);
			f.z = CalcDistanceTessFactor (v2,minDist,maxDist,tess,o2w,cameraPos);

			return CalcTriEdgeTessFactors (f);
		}

		float4 EdgeLengthBasedTess( float4 v0, float4 v1, float4 v2, float edgeLength, float4x4 o2w, float3 cameraPos, float4 scParams )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;
			tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
			tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
			tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
			tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			return tess;
		}

		float4 EdgeLengthBasedTessCull( float4 v0, float4 v1, float4 v2, float edgeLength, float maxDisplacement, float4x4 o2w, float3 cameraPos, float4 scParams, float4 planes[6] )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;

			if (WorldViewFrustumCull(pos0, pos1, pos2, maxDisplacement, planes))
			{
				tess = 0.0f;
			}
			else
			{
				tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
				tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
				tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
				tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			}
			return tess;
		}
		#endif //ASE_TESS_FUNCS
		ENDHLSL

		
		Pass
		{
			
			Name "Forward"
			Tags { "LightMode"="UniversalForwardOnly" }

			ZWrite Off
			ZTest Always
			Offset 0,0
			ColorMask RGBA
			Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
			BlendOp Add, Add

			

			HLSLPROGRAM

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170300
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3

			#pragma multi_compile_fragment _ DEBUG_DISPLAY

			#pragma vertex vert
			#pragma fragment frag

			// Option "Keep Lighting Variants" @david please keep this note for future changes in Unlit  lighting
			//#define UNLIT_REALTIME_LIGHTING 1
			//#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			//#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			//#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			//#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
			//#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
			//#pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
			//#pragma multi_compile _ REFLECTION_PROBE_ROTATION
			//#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			//#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			//#pragma multi_compile _ SHADOWS_SHADOWMASK
			//#pragma multi_compile_fragment _ _LIGHT_LAYERS
			//#pragma multi_compile_fragment _ _LIGHT_COOKIES
			//#pragma multi_compile _ _CLUSTER_LIGHT_LOOP

			#define SHADERPASS SHADERPASS_UNLIT

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#if ( UNITY_VERSION >= 60010000 )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
			#else
			#pragma multi_compile_fog
			#endif
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#define ASE_NEEDS_FRAG_WORLD_VIEW_DIR
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS


			#if defined(ASE_WRITE_DEPTH_CONSERVATIVE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float4 positionWSAndFogFactor : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				half4 tangentWS : TEXCOORD2;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float3 _ColorA;
			float3 _ColorB;
			float3 _NoiseSpeed;
			float3 _NoiseScale;
			float _Steps;
			float _DitherStrength;
			float _InnerAngle;
			float _DistanceCurve;
			float _ApexSoftness;
			float _CoreBoost;
			float _CoreRadius;
			float _GradientCurve;
			float _Brighness;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			

			float4 hash41( float4 n )
			{
				// Fold n onto the hash's 4096-cell ring: n is integer-valued and far below
				// 2^24, so frac( n / 4096 ) is exact and every product below stays within
				// fp32 precision no matter how far the noise has drifted. This also makes the
				// noise periodic every 4096 cells, letting the time offset wrap seamlessly.
				float4 m = frac( n * 0.000244140625 );
				return frac( m * 69632.0 * frac( m * 1303.7973 ) );
			}
			
			float ValueNoise3d( float3 p )
			{
				float3 i = floor( p );
				float3 f = frac( p );
				f = f * f * ( 3.0 - 2.0 * f );
				// Encode the lattice cell as one scalar; the 8 corners become constant offsets.
				// The +113 bias keeps the cell at the origin off the hash's zero.
				float n = i.x + 317.0 * i.y + 157.0 * i.z + 113.0;
				// Corner order per float4: (0,0) (1,0) (0,1) (1,1) in xy, one float4 per z slice.
				float4 xy = n + float4( 0.0, 1.0, 317.0, 318.0 );
				float4 h  = lerp( hash41( xy ), hash41( xy + 157.0 ), f.z );
				float2 hx = lerp( h.xz, h.yw, f.x );
				return lerp( hx.x, hx.y, f.y );
			}
			
			float ConeVolume( float3 ViewDirWS, float SceneDepth, float Steps, float4 ScreenPos, float DitherStrength, float DitherSpeed, float InnerAngle, float DistanceCurve, float ApexSoftness, float3 NoiseSpeed, float3 NoiseScale, float CoreBoost, float CoreRadius )
			{
				// A cone volume, ray marched. The cone is fixed in object space exactly as the
				// analytic version had it: apex at the origin, axis along +Z, 1 unit deep and
				// 1 unit across at the base. Scale the proxy mesh to place and size it. The mesh
				// only has to contain that cone, so building in a margin is the right call: the
				// silhouette comes from this maths, never from the triangles.
				const float coneK   = 0.5;
				const float coneLen = 1.0;
				const float k2      = coneK * coneK;
				float3 ro    = mul( GetWorldToObjectMatrix(), float4( _WorldSpaceCameraPos, 1.0 ) ).xyz;
				float3 rdOS  = mul( ( float3x3 )GetWorldToObjectMatrix(), -ViewDirWS );
				float  lenOS = length( rdOS );
				float3 rd    = rdOS / lenOS;
				float tScene = SceneDepth / dot( -ViewDirWS, GetViewForwardDir() ) * lenOS;
				// Clip the visible span down to the cone. Cap planes first.
				float tEnter = 0.0;
				float tExit  = tScene;
				if ( abs( rd.z ) > 1e-6 )
				{
					float ta = ( 0.0 - ro.z ) / rd.z;
					float tb = ( coneLen - ro.z ) / rd.z;
					tEnter = max( tEnter, min( ta, tb ) );
					tExit  = min( tExit,  max( ta, tb ) );
				}
				else if ( ro.z < 0.0 || ro.z > coneLen )
					return 0.0;
				if ( tExit <= tEnter )
					return 0.0;
				// Then the cone surface itself: x*x + y*y = k*k * z*z.
				float A = dot( rd.xy, rd.xy ) - k2 * rd.z * rd.z;
				float B = 2.0 * ( dot( ro.xy, rd.xy ) - k2 * ro.z * rd.z );
				float C = dot( ro.xy, ro.xy ) - k2 * ro.z * ro.z;
				if ( abs( A ) > 1e-6 )
				{
					float disc = B * B - 4.0 * A * C;
					if ( disc > 0.0 )
					{
						float sq  = sqrt( disc );
						float inv = 0.5 / A;
						float r0  = ( -B - sq ) * inv;
						float r1  = ( -B + sq ) * inv;
						if ( r0 > r1 ) { float sw = r0; r0 = r1; r1 = sw; }
						if ( A > 0.0 )
						{
							tEnter = max( tEnter, r0 );
							tExit  = min( tExit,  r1 );
						}
						else
						{
							// Looking along the beam: the valid span is outside the roots, so it
							// splits in two. The cap clip already picked a nappe, so keep the
							// piece that still has length.
							if ( tEnter >= r1 || tExit <= r0 )
							{
							}
							else if ( tEnter < r0 )
								tExit = min( tExit, r0 );
							else
								tEnter = max( tEnter, r1 );
						}
					}
					else if ( C > 0.0 )
						return 0.0;
				}
				else if ( abs( B ) > 1e-6 )
				{
					float r = -C / B;
					if ( B > 0.0 ) tExit  = min( tExit, r );
					else           tEnter = max( tEnter, r );
				}
				else if ( C > 0.0 )
					return 0.0;
				if ( tExit <= tEnter )
					return 0.0;
				// Guard the sign gymnastics above: the midpoint must really be inside.
				float3 pm = ro + rd * ( 0.5 * ( tEnter + tExit ) );
				if ( pm.z < 0.0 || pm.z > coneLen || dot( pm.xy, pm.xy ) > k2 * pm.z * pm.z )
					return 0.0;
				float rayLen  = tExit - tEnter;
				int   steps   = (int)Steps;
				float stepLen = rayLen / steps;
				// Animated dither, as before: jitter each pixel first sample inside its own
				// step so the step boundaries stop lining up and banding becomes grain.
				float ign = frac( 52.9829189 * frac( dot( ScreenPos.xy, float2( 0.06711056, 0.00583715 ) ) ) );
				ign = frac( ign + _TimeParameters.x * DitherSpeed );
				float jitter = 0.5 + ( ign - 0.5 ) * DitherStrength;
				// The cone shape fixes the outer angle: tan( theta ) = coneK, so cos( theta )
				// falls straight out of it. InnerAngle is the inner/outer ratio, matching how
				// Unity pairs a spot inner angle with its outer angle. 0 ramps all the way from
				// the axis, 1 collapses the ramp into a hard rim. All of this is per pixel, not
				// per sample, so the loop only pays the rsqrt and a multiply add.
				float cosOuter      = rsqrt( 1.0 + k2 );
				float cosInner      = cos( saturate( InnerAngle ) * atan( coneK ) );
				float invAngleRange = 1.0 / max( cosInner - cosOuter, 1e-4 );
				// Squared once here so the loop pays an add instead of the max it replaces.
				// This bounds how fast the angle can swing near the apex, where |p| tends to 0
				// and a sub-millimetre step would otherwise change the angle enormously. That
				// runaway, not the sample count, is what makes the tip look noisy. It is also
				// the physically honest form: a real emitter has size, so its angular falloff
				// is not infinitely sharp at the source.
				float apexSoft2 = ApexSoftness * ApexSoftness + 1e-8;
				// Radius of the bright core, squared once here. This is a separate scale from
				// ApexSoftness on purpose: that one bounds how fast the angle may swing, this
				// one says how far the extra brightness near the source reaches.
				float coreR2 = CoreRadius * CoreRadius + 1e-8;
				float3 dp = rd * stepLen;
				float3 p  = ro + rd * ( tEnter + stepLen * jitter );
				// Noise coordinates ride along with the march, advanced by the same fixed step
				// so no extra multiply is needed per sample. Kept in object space, so the
				// pattern scales with the proxy mesh exactly as it does in the sphere shaders.
				// The drift wraps at the noise period, which makes the wrap seamless and keeps
				// the coordinate small enough to stay precise no matter how long the world runs.
				float3 noiseOffset = fmod( _TimeParameters.x * ( NoiseSpeed * NoiseScale ), 4096.0 );
				float3 dnp = dp * NoiseScale;
				float3 np  = p * NoiseScale + noiseOffset;
				float acc = 0.0;
				[loop]
				for ( int i = 0; i < steps; i++ )
				{
					// Normalised cone coordinates. Because the cone is a fixed 1 by 1, both of
					// these land in 0..1 with no extra parameters: zn runs apex to base, and rn2
					// is the squared radial position, 0 on the axis and 1 on the cone surface.
					// One rsqrt serves both falloffs. len comes back for a single multiply,
					// because d2 * rsqrt( d2 ) is just sqrt( d2 ).
					float d2     = dot( p, p ) + apexSoft2;
					float invLen = rsqrt( d2 );
					float len    = d2 * invLen;
					// Angular falloff, the same construction URP uses for a spot light:
					//     atten = saturate( SdotL * invRange - cosOuter * invRange ), squared
					// SdotL is the cosine of the angle off the beam axis, so measuring it from
					// the apex reproduces Unity distribution exactly, including the flat fully
					// lit core inside the inner angle. A pow curve cannot do that plateau, which
					// is why the older RadialCurve version fell off from the axis immediately.
					float cosA   = p.z * invLen;
					float ang    = saturate( ( cosA - cosOuter ) * invAngleRange );
					float radial = ang * ang;
					// Falloff with distance from the apex, not along the axis. Light falls off on
					// spherical shells, so the rim of any cross section is genuinely further from
					// the source than its centre and has to be dimmer. Measuring on flat planes,
					// which this did before, is what made the beam read as a shaped tube instead
					// of a light. Still a normalised curve rather than an inverse square, so
					// there is no blowup at the apex and no range to keep in sync.
					float axial = pow( saturate( 1.0 - len / coneLen ), DistanceCurve );
					// A real spot is not merely brighter at the source, it is brighter by a large
					// factor: inverse square makes the apex hundreds of times the mid cone value,
					// whereas the curve above tops out at 1 and so peaks only about four times the
					// middle. That missing headroom is what stops this reading as a lamp.
					//
					// Rather than swap the whole falloff and disturb the part that already looks
					// right, this multiplies in a bump that decays as 1/(1 + (len/CoreRadius)^2).
					// Beyond a couple of core radii the factor is essentially 1, so everything
					// past the core is left exactly as it was. CoreBoost of 0 is a no-op.
					axial *= 1.0 + CoreBoost * ( coreR2 / ( d2 + coreR2 ) );
					// Density is the cone shaping multiplied by the noise, so the beam keeps its
					// spot light distribution while the noise only breaks it up.
					acc += radial * axial * ValueNoise3d( np );
					p   += dp;
					np  += dnp;
				}
				return acc * stepLen;
			}
			
			float4 ASEScreenPositionNormalizedToPixel( float4 screenPosNorm, float4 screenParams )
			{
				float4 screenPosPixel = screenPosNorm * float4( screenParams.xy, 1, 1 );
				#if UNITY_UV_STARTS_AT_TOP
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x < 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#else
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x > 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#endif
				return screenPosPixel;
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				#ifdef ASE_CUSTOM_MOTION_VECTOR
					// Declared so the Motion Vector output port surfaces on the master node; only consumed by the motion vector passes.
					float3 aseCustomMotionVector = float3(0, 0, 0);
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS, input.tangentOS );

				float fogFactor = 0;
				#if defined(ASE_FOG) && !defined(_FOG_FRAGMENT)
					fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
				#endif

				output.positionCS = ASE_ADJUST_CLIP_POSITION( vertexInput.positionCS );
				output.positionWSAndFogFactor = float4( vertexInput.positionWS, fogFactor );
				output.normalWS = normalInput.normalWS;
				output.tangentWS = half4( normalInput.tangentWS, ( input.tangentOS.w > 0.0 ? 1.0 : -1.0 ) * GetOddNegativeScale() );;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag ( PackedVaryings input
						#if defined( ASE_WRITE_DEPTH )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						#ifdef _WRITE_RENDERING_LAYERS
						#if ( UNITY_VERSION >= 60020000 )
						, out uint outRenderingLayers : SV_Target1
						#else
						, out float4 outRenderingLayers : SV_Target1
						#endif
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined( _SURFACE_TYPE_TRANSPARENT )
					const bool isTransparent = true;
				#else
					const bool isTransparent = false;
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					float4 shadowCoord = TransformWorldToShadowCoord( input.positionWSAndFogFactor.xyz );
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				// @diogo: mikktspace compliant
				float renormFactor = 1.0 / max( FLT_MIN, length( input.normalWS ) );

				float3 PositionWS = input.positionWSAndFogFactor.xyz;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				half3 ViewDirWS = GetWorldSpaceNormalizeViewDir( PositionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );
				float3 TangentWS = input.tangentWS.xyz * renormFactor;
				float3 BitangentWS = cross( input.normalWS, input.tangentWS.xyz ) * input.tangentWS.w * renormFactor;
				float3 NormalWS = input.normalWS * renormFactor;

				float3 ViewDirWS12 = ViewDirWS;
				float depthLinearEye201 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ), _ZBufferParams );
				float SceneDepth12 = depthLinearEye201;
				float Steps12 = _Steps;
				float4 ase_positionSS_Pixel = ASEScreenPositionNormalizedToPixel( ScreenPosNorm, _ScaledScreenParams );
				float4 ScreenPos12 = ase_positionSS_Pixel;
				float DitherStrength12 = _DitherStrength;
				float DitherSpeed12 = ( 17.7 * PI );
				float InnerAngle12 = _InnerAngle;
				float DistanceCurve12 = _DistanceCurve;
				float ApexSoftness12 = _ApexSoftness;
				float3 NoiseSpeed12 = _NoiseSpeed;
				float3 NoiseScale12 = _NoiseScale;
				float CoreBoost12 = _CoreBoost;
				float CoreRadius12 = _CoreRadius;
				float localConeVolume12 = ConeVolume( ViewDirWS12 , SceneDepth12 , Steps12 , ScreenPos12 , DitherStrength12 , DitherSpeed12 , InnerAngle12 , DistanceCurve12 , ApexSoftness12 , NoiseSpeed12 , NoiseScale12 , CoreBoost12 , CoreRadius12 );
				float3 lerpResult85 = lerp( _ColorA , _ColorB , pow( localConeVolume12 , _GradientCurve ));
				
				float3 BakedAlbedo = 0;
				float3 BakedEmission = 0;
				float3 Color = ( lerpResult85 * localConeVolume12 );
				float3 Normal = float3(0, 0, 1);
				float Alpha = ( localConeVolume12 * -_Brighness );
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
					float AlphaClipThresholdShadow = 0.5;
				#endif


				#if defined( ASE_WRITE_DEPTH )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_CHANGES_WORLD_POS)
					ShadowCoord = TransformWorldToShadowCoord( PositionWS );
				#endif

				InputData inputData = (InputData)0;
				inputData.positionWS = PositionWS;
				inputData.positionCS = input.positionCS;
				inputData.normalizedScreenSpaceUV = ScreenPosNorm.xy;
				inputData.normalWS = NormalWS;
				inputData.viewDirectionWS = ViewDirWS;

				#if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT) && defined(UNLIT_DEFAULT_SSAO)
					float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
					AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(normalizedScreenSpaceUV);
					Color.rgb *= aoFactor.directAmbientOcclusion;
				#endif

				#ifdef ASE_FOG
					inputData.fogCoord = InitializeInputDataFog(float4(inputData.positionWS, 1.0), input.positionWSAndFogFactor.w);
				#endif

				#if defined(_DBUFFER) && defined(UNLIT_DEFAULT_DECAL_BLENDING)
					ApplyDecalToBaseColor(input.positionCS, Color);
				#endif

				#ifdef ASE_FOG
					#ifdef TERRAIN_SPLAT_ADDPASS
						Color.rgb = MixFogColor(Color.rgb, half3(0,0,0), inputData.fogCoord);
					#else
						Color.rgb = MixFog(Color.rgb, inputData.fogCoord);
					#endif
				#endif

				#if defined( ASE_WRITE_DEPTH )
					outputDepth = input.positionCS.z;
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					#if ( UNITY_VERSION >= 60020000 )
					outRenderingLayers = EncodeMeshRenderingLayer();
					#else
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4( EncodeMeshRenderingLayer( renderingLayers ), 0, 0, 0 );
					#endif
				#endif

				#if defined( ASE_OPAQUE_KEEP_ALPHA )
					return half4( Color, Alpha );
				#else
					return half4( Color, OutputAlpha( Alpha, isTransparent ) );
				#endif
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "SceneSelectionPass"
			Tags { "LightMode"="SceneSelectionPass" }

			Cull Off
			AlphaToMask Off

			HLSLPROGRAM

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170300
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS


			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float3 _ColorA;
			float3 _ColorB;
			float3 _NoiseSpeed;
			float3 _NoiseScale;
			float _Steps;
			float _DitherStrength;
			float _InnerAngle;
			float _DistanceCurve;
			float _ApexSoftness;
			float _CoreBoost;
			float _CoreRadius;
			float _GradientCurve;
			float _Brighness;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			

			float4 hash41( float4 n )
			{
				// Fold n onto the hash's 4096-cell ring: n is integer-valued and far below
				// 2^24, so frac( n / 4096 ) is exact and every product below stays within
				// fp32 precision no matter how far the noise has drifted. This also makes the
				// noise periodic every 4096 cells, letting the time offset wrap seamlessly.
				float4 m = frac( n * 0.000244140625 );
				return frac( m * 69632.0 * frac( m * 1303.7973 ) );
			}
			
			float ValueNoise3d( float3 p )
			{
				float3 i = floor( p );
				float3 f = frac( p );
				f = f * f * ( 3.0 - 2.0 * f );
				// Encode the lattice cell as one scalar; the 8 corners become constant offsets.
				// The +113 bias keeps the cell at the origin off the hash's zero.
				float n = i.x + 317.0 * i.y + 157.0 * i.z + 113.0;
				// Corner order per float4: (0,0) (1,0) (0,1) (1,1) in xy, one float4 per z slice.
				float4 xy = n + float4( 0.0, 1.0, 317.0, 318.0 );
				float4 h  = lerp( hash41( xy ), hash41( xy + 157.0 ), f.z );
				float2 hx = lerp( h.xz, h.yw, f.x );
				return lerp( hx.x, hx.y, f.y );
			}
			
			float ConeVolume( float3 ViewDirWS, float SceneDepth, float Steps, float4 ScreenPos, float DitherStrength, float DitherSpeed, float InnerAngle, float DistanceCurve, float ApexSoftness, float3 NoiseSpeed, float3 NoiseScale, float CoreBoost, float CoreRadius )
			{
				// A cone volume, ray marched. The cone is fixed in object space exactly as the
				// analytic version had it: apex at the origin, axis along +Z, 1 unit deep and
				// 1 unit across at the base. Scale the proxy mesh to place and size it. The mesh
				// only has to contain that cone, so building in a margin is the right call: the
				// silhouette comes from this maths, never from the triangles.
				const float coneK   = 0.5;
				const float coneLen = 1.0;
				const float k2      = coneK * coneK;
				float3 ro    = mul( GetWorldToObjectMatrix(), float4( _WorldSpaceCameraPos, 1.0 ) ).xyz;
				float3 rdOS  = mul( ( float3x3 )GetWorldToObjectMatrix(), -ViewDirWS );
				float  lenOS = length( rdOS );
				float3 rd    = rdOS / lenOS;
				float tScene = SceneDepth / dot( -ViewDirWS, GetViewForwardDir() ) * lenOS;
				// Clip the visible span down to the cone. Cap planes first.
				float tEnter = 0.0;
				float tExit  = tScene;
				if ( abs( rd.z ) > 1e-6 )
				{
					float ta = ( 0.0 - ro.z ) / rd.z;
					float tb = ( coneLen - ro.z ) / rd.z;
					tEnter = max( tEnter, min( ta, tb ) );
					tExit  = min( tExit,  max( ta, tb ) );
				}
				else if ( ro.z < 0.0 || ro.z > coneLen )
					return 0.0;
				if ( tExit <= tEnter )
					return 0.0;
				// Then the cone surface itself: x*x + y*y = k*k * z*z.
				float A = dot( rd.xy, rd.xy ) - k2 * rd.z * rd.z;
				float B = 2.0 * ( dot( ro.xy, rd.xy ) - k2 * ro.z * rd.z );
				float C = dot( ro.xy, ro.xy ) - k2 * ro.z * ro.z;
				if ( abs( A ) > 1e-6 )
				{
					float disc = B * B - 4.0 * A * C;
					if ( disc > 0.0 )
					{
						float sq  = sqrt( disc );
						float inv = 0.5 / A;
						float r0  = ( -B - sq ) * inv;
						float r1  = ( -B + sq ) * inv;
						if ( r0 > r1 ) { float sw = r0; r0 = r1; r1 = sw; }
						if ( A > 0.0 )
						{
							tEnter = max( tEnter, r0 );
							tExit  = min( tExit,  r1 );
						}
						else
						{
							// Looking along the beam: the valid span is outside the roots, so it
							// splits in two. The cap clip already picked a nappe, so keep the
							// piece that still has length.
							if ( tEnter >= r1 || tExit <= r0 )
							{
							}
							else if ( tEnter < r0 )
								tExit = min( tExit, r0 );
							else
								tEnter = max( tEnter, r1 );
						}
					}
					else if ( C > 0.0 )
						return 0.0;
				}
				else if ( abs( B ) > 1e-6 )
				{
					float r = -C / B;
					if ( B > 0.0 ) tExit  = min( tExit, r );
					else           tEnter = max( tEnter, r );
				}
				else if ( C > 0.0 )
					return 0.0;
				if ( tExit <= tEnter )
					return 0.0;
				// Guard the sign gymnastics above: the midpoint must really be inside.
				float3 pm = ro + rd * ( 0.5 * ( tEnter + tExit ) );
				if ( pm.z < 0.0 || pm.z > coneLen || dot( pm.xy, pm.xy ) > k2 * pm.z * pm.z )
					return 0.0;
				float rayLen  = tExit - tEnter;
				int   steps   = (int)Steps;
				float stepLen = rayLen / steps;
				// Animated dither, as before: jitter each pixel first sample inside its own
				// step so the step boundaries stop lining up and banding becomes grain.
				float ign = frac( 52.9829189 * frac( dot( ScreenPos.xy, float2( 0.06711056, 0.00583715 ) ) ) );
				ign = frac( ign + _TimeParameters.x * DitherSpeed );
				float jitter = 0.5 + ( ign - 0.5 ) * DitherStrength;
				// The cone shape fixes the outer angle: tan( theta ) = coneK, so cos( theta )
				// falls straight out of it. InnerAngle is the inner/outer ratio, matching how
				// Unity pairs a spot inner angle with its outer angle. 0 ramps all the way from
				// the axis, 1 collapses the ramp into a hard rim. All of this is per pixel, not
				// per sample, so the loop only pays the rsqrt and a multiply add.
				float cosOuter      = rsqrt( 1.0 + k2 );
				float cosInner      = cos( saturate( InnerAngle ) * atan( coneK ) );
				float invAngleRange = 1.0 / max( cosInner - cosOuter, 1e-4 );
				// Squared once here so the loop pays an add instead of the max it replaces.
				// This bounds how fast the angle can swing near the apex, where |p| tends to 0
				// and a sub-millimetre step would otherwise change the angle enormously. That
				// runaway, not the sample count, is what makes the tip look noisy. It is also
				// the physically honest form: a real emitter has size, so its angular falloff
				// is not infinitely sharp at the source.
				float apexSoft2 = ApexSoftness * ApexSoftness + 1e-8;
				// Radius of the bright core, squared once here. This is a separate scale from
				// ApexSoftness on purpose: that one bounds how fast the angle may swing, this
				// one says how far the extra brightness near the source reaches.
				float coreR2 = CoreRadius * CoreRadius + 1e-8;
				float3 dp = rd * stepLen;
				float3 p  = ro + rd * ( tEnter + stepLen * jitter );
				// Noise coordinates ride along with the march, advanced by the same fixed step
				// so no extra multiply is needed per sample. Kept in object space, so the
				// pattern scales with the proxy mesh exactly as it does in the sphere shaders.
				// The drift wraps at the noise period, which makes the wrap seamless and keeps
				// the coordinate small enough to stay precise no matter how long the world runs.
				float3 noiseOffset = fmod( _TimeParameters.x * ( NoiseSpeed * NoiseScale ), 4096.0 );
				float3 dnp = dp * NoiseScale;
				float3 np  = p * NoiseScale + noiseOffset;
				float acc = 0.0;
				[loop]
				for ( int i = 0; i < steps; i++ )
				{
					// Normalised cone coordinates. Because the cone is a fixed 1 by 1, both of
					// these land in 0..1 with no extra parameters: zn runs apex to base, and rn2
					// is the squared radial position, 0 on the axis and 1 on the cone surface.
					// One rsqrt serves both falloffs. len comes back for a single multiply,
					// because d2 * rsqrt( d2 ) is just sqrt( d2 ).
					float d2     = dot( p, p ) + apexSoft2;
					float invLen = rsqrt( d2 );
					float len    = d2 * invLen;
					// Angular falloff, the same construction URP uses for a spot light:
					//     atten = saturate( SdotL * invRange - cosOuter * invRange ), squared
					// SdotL is the cosine of the angle off the beam axis, so measuring it from
					// the apex reproduces Unity distribution exactly, including the flat fully
					// lit core inside the inner angle. A pow curve cannot do that plateau, which
					// is why the older RadialCurve version fell off from the axis immediately.
					float cosA   = p.z * invLen;
					float ang    = saturate( ( cosA - cosOuter ) * invAngleRange );
					float radial = ang * ang;
					// Falloff with distance from the apex, not along the axis. Light falls off on
					// spherical shells, so the rim of any cross section is genuinely further from
					// the source than its centre and has to be dimmer. Measuring on flat planes,
					// which this did before, is what made the beam read as a shaped tube instead
					// of a light. Still a normalised curve rather than an inverse square, so
					// there is no blowup at the apex and no range to keep in sync.
					float axial = pow( saturate( 1.0 - len / coneLen ), DistanceCurve );
					// A real spot is not merely brighter at the source, it is brighter by a large
					// factor: inverse square makes the apex hundreds of times the mid cone value,
					// whereas the curve above tops out at 1 and so peaks only about four times the
					// middle. That missing headroom is what stops this reading as a lamp.
					//
					// Rather than swap the whole falloff and disturb the part that already looks
					// right, this multiplies in a bump that decays as 1/(1 + (len/CoreRadius)^2).
					// Beyond a couple of core radii the factor is essentially 1, so everything
					// past the core is left exactly as it was. CoreBoost of 0 is a no-op.
					axial *= 1.0 + CoreBoost * ( coreR2 / ( d2 + coreR2 ) );
					// Density is the cone shaping multiplied by the noise, so the beam keeps its
					// spot light distribution while the noise only breaks it up.
					acc += radial * axial * ValueNoise3d( np );
					p   += dp;
					np  += dnp;
				}
				return acc * stepLen;
			}
			
			float4 ASEScreenPositionNormalizedToPixel( float4 screenPosNorm, float4 screenParams )
			{
				float4 screenPosPixel = screenPosNorm * float4( screenParams.xy, 1, 1 );
				#if UNITY_UV_STARTS_AT_TOP
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x < 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#else
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x > 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#endif
				return screenPosPixel;
			}
			

			int _ObjectId;
			int _PassValue;

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction(Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 ase_positionWS = TransformObjectToWorld( ( input.positionOS ).xyz );
				output.ase_texcoord.xyz = ase_positionWS;
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				output.ase_texcoord1 = screenPos;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord.w = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = ASE_ADJUST_CLIP_POSITION( vertexInput.positionCS );
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input ) : SV_Target
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float3 ase_positionWS = input.ase_texcoord.xyz;
				float3 ase_viewVectorWS = ( ( unity_OrthoParams.w == 0 ) ? _WorldSpaceCameraPos - ase_positionWS : UNITY_MATRIX_V[ 2 ].xyz );
				float3 ase_viewDirWS = normalize( ase_viewVectorWS );
				float3 ViewDirWS12 = ase_viewDirWS;
				float4 screenPos = input.ase_texcoord1;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float depthLinearEye201 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ), _ZBufferParams );
				float SceneDepth12 = depthLinearEye201;
				float Steps12 = _Steps;
				float4 ase_positionSS_Pixel = ASEScreenPositionNormalizedToPixel( ase_positionSSNorm, _ScaledScreenParams );
				float4 ScreenPos12 = ase_positionSS_Pixel;
				float DitherStrength12 = _DitherStrength;
				float DitherSpeed12 = ( 17.7 * PI );
				float InnerAngle12 = _InnerAngle;
				float DistanceCurve12 = _DistanceCurve;
				float ApexSoftness12 = _ApexSoftness;
				float3 NoiseSpeed12 = _NoiseSpeed;
				float3 NoiseScale12 = _NoiseScale;
				float CoreBoost12 = _CoreBoost;
				float CoreRadius12 = _CoreRadius;
				float localConeVolume12 = ConeVolume( ViewDirWS12 , SceneDepth12 , Steps12 , ScreenPos12 , DitherStrength12 , DitherSpeed12 , InnerAngle12 , DistanceCurve12 , ApexSoftness12 , NoiseSpeed12 , NoiseScale12 , CoreBoost12 , CoreRadius12 );
				

				surfaceDescription.Alpha = ( localConeVolume12 * -_Brighness );
				#if defined( _ALPHATEST_ON )
					surfaceDescription.AlphaClipThreshold = _Cutoff;
				#endif

				#ifdef _ALPHATEST_ON
					clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
				#endif

				half4 outColor = half4(_ObjectId, _PassValue, 1.0, 1.0);
				return outColor;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "ScenePickingPass"
			Tags { "LightMode"="Picking" }

			AlphaToMask Off

			HLSLPROGRAM

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170300
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT

			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS


			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float3 _ColorA;
			float3 _ColorB;
			float3 _NoiseSpeed;
			float3 _NoiseScale;
			float _Steps;
			float _DitherStrength;
			float _InnerAngle;
			float _DistanceCurve;
			float _ApexSoftness;
			float _CoreBoost;
			float _CoreRadius;
			float _GradientCurve;
			float _Brighness;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			

			float4 hash41( float4 n )
			{
				// Fold n onto the hash's 4096-cell ring: n is integer-valued and far below
				// 2^24, so frac( n / 4096 ) is exact and every product below stays within
				// fp32 precision no matter how far the noise has drifted. This also makes the
				// noise periodic every 4096 cells, letting the time offset wrap seamlessly.
				float4 m = frac( n * 0.000244140625 );
				return frac( m * 69632.0 * frac( m * 1303.7973 ) );
			}
			
			float ValueNoise3d( float3 p )
			{
				float3 i = floor( p );
				float3 f = frac( p );
				f = f * f * ( 3.0 - 2.0 * f );
				// Encode the lattice cell as one scalar; the 8 corners become constant offsets.
				// The +113 bias keeps the cell at the origin off the hash's zero.
				float n = i.x + 317.0 * i.y + 157.0 * i.z + 113.0;
				// Corner order per float4: (0,0) (1,0) (0,1) (1,1) in xy, one float4 per z slice.
				float4 xy = n + float4( 0.0, 1.0, 317.0, 318.0 );
				float4 h  = lerp( hash41( xy ), hash41( xy + 157.0 ), f.z );
				float2 hx = lerp( h.xz, h.yw, f.x );
				return lerp( hx.x, hx.y, f.y );
			}
			
			float ConeVolume( float3 ViewDirWS, float SceneDepth, float Steps, float4 ScreenPos, float DitherStrength, float DitherSpeed, float InnerAngle, float DistanceCurve, float ApexSoftness, float3 NoiseSpeed, float3 NoiseScale, float CoreBoost, float CoreRadius )
			{
				// A cone volume, ray marched. The cone is fixed in object space exactly as the
				// analytic version had it: apex at the origin, axis along +Z, 1 unit deep and
				// 1 unit across at the base. Scale the proxy mesh to place and size it. The mesh
				// only has to contain that cone, so building in a margin is the right call: the
				// silhouette comes from this maths, never from the triangles.
				const float coneK   = 0.5;
				const float coneLen = 1.0;
				const float k2      = coneK * coneK;
				float3 ro    = mul( GetWorldToObjectMatrix(), float4( _WorldSpaceCameraPos, 1.0 ) ).xyz;
				float3 rdOS  = mul( ( float3x3 )GetWorldToObjectMatrix(), -ViewDirWS );
				float  lenOS = length( rdOS );
				float3 rd    = rdOS / lenOS;
				float tScene = SceneDepth / dot( -ViewDirWS, GetViewForwardDir() ) * lenOS;
				// Clip the visible span down to the cone. Cap planes first.
				float tEnter = 0.0;
				float tExit  = tScene;
				if ( abs( rd.z ) > 1e-6 )
				{
					float ta = ( 0.0 - ro.z ) / rd.z;
					float tb = ( coneLen - ro.z ) / rd.z;
					tEnter = max( tEnter, min( ta, tb ) );
					tExit  = min( tExit,  max( ta, tb ) );
				}
				else if ( ro.z < 0.0 || ro.z > coneLen )
					return 0.0;
				if ( tExit <= tEnter )
					return 0.0;
				// Then the cone surface itself: x*x + y*y = k*k * z*z.
				float A = dot( rd.xy, rd.xy ) - k2 * rd.z * rd.z;
				float B = 2.0 * ( dot( ro.xy, rd.xy ) - k2 * ro.z * rd.z );
				float C = dot( ro.xy, ro.xy ) - k2 * ro.z * ro.z;
				if ( abs( A ) > 1e-6 )
				{
					float disc = B * B - 4.0 * A * C;
					if ( disc > 0.0 )
					{
						float sq  = sqrt( disc );
						float inv = 0.5 / A;
						float r0  = ( -B - sq ) * inv;
						float r1  = ( -B + sq ) * inv;
						if ( r0 > r1 ) { float sw = r0; r0 = r1; r1 = sw; }
						if ( A > 0.0 )
						{
							tEnter = max( tEnter, r0 );
							tExit  = min( tExit,  r1 );
						}
						else
						{
							// Looking along the beam: the valid span is outside the roots, so it
							// splits in two. The cap clip already picked a nappe, so keep the
							// piece that still has length.
							if ( tEnter >= r1 || tExit <= r0 )
							{
							}
							else if ( tEnter < r0 )
								tExit = min( tExit, r0 );
							else
								tEnter = max( tEnter, r1 );
						}
					}
					else if ( C > 0.0 )
						return 0.0;
				}
				else if ( abs( B ) > 1e-6 )
				{
					float r = -C / B;
					if ( B > 0.0 ) tExit  = min( tExit, r );
					else           tEnter = max( tEnter, r );
				}
				else if ( C > 0.0 )
					return 0.0;
				if ( tExit <= tEnter )
					return 0.0;
				// Guard the sign gymnastics above: the midpoint must really be inside.
				float3 pm = ro + rd * ( 0.5 * ( tEnter + tExit ) );
				if ( pm.z < 0.0 || pm.z > coneLen || dot( pm.xy, pm.xy ) > k2 * pm.z * pm.z )
					return 0.0;
				float rayLen  = tExit - tEnter;
				int   steps   = (int)Steps;
				float stepLen = rayLen / steps;
				// Animated dither, as before: jitter each pixel first sample inside its own
				// step so the step boundaries stop lining up and banding becomes grain.
				float ign = frac( 52.9829189 * frac( dot( ScreenPos.xy, float2( 0.06711056, 0.00583715 ) ) ) );
				ign = frac( ign + _TimeParameters.x * DitherSpeed );
				float jitter = 0.5 + ( ign - 0.5 ) * DitherStrength;
				// The cone shape fixes the outer angle: tan( theta ) = coneK, so cos( theta )
				// falls straight out of it. InnerAngle is the inner/outer ratio, matching how
				// Unity pairs a spot inner angle with its outer angle. 0 ramps all the way from
				// the axis, 1 collapses the ramp into a hard rim. All of this is per pixel, not
				// per sample, so the loop only pays the rsqrt and a multiply add.
				float cosOuter      = rsqrt( 1.0 + k2 );
				float cosInner      = cos( saturate( InnerAngle ) * atan( coneK ) );
				float invAngleRange = 1.0 / max( cosInner - cosOuter, 1e-4 );
				// Squared once here so the loop pays an add instead of the max it replaces.
				// This bounds how fast the angle can swing near the apex, where |p| tends to 0
				// and a sub-millimetre step would otherwise change the angle enormously. That
				// runaway, not the sample count, is what makes the tip look noisy. It is also
				// the physically honest form: a real emitter has size, so its angular falloff
				// is not infinitely sharp at the source.
				float apexSoft2 = ApexSoftness * ApexSoftness + 1e-8;
				// Radius of the bright core, squared once here. This is a separate scale from
				// ApexSoftness on purpose: that one bounds how fast the angle may swing, this
				// one says how far the extra brightness near the source reaches.
				float coreR2 = CoreRadius * CoreRadius + 1e-8;
				float3 dp = rd * stepLen;
				float3 p  = ro + rd * ( tEnter + stepLen * jitter );
				// Noise coordinates ride along with the march, advanced by the same fixed step
				// so no extra multiply is needed per sample. Kept in object space, so the
				// pattern scales with the proxy mesh exactly as it does in the sphere shaders.
				// The drift wraps at the noise period, which makes the wrap seamless and keeps
				// the coordinate small enough to stay precise no matter how long the world runs.
				float3 noiseOffset = fmod( _TimeParameters.x * ( NoiseSpeed * NoiseScale ), 4096.0 );
				float3 dnp = dp * NoiseScale;
				float3 np  = p * NoiseScale + noiseOffset;
				float acc = 0.0;
				[loop]
				for ( int i = 0; i < steps; i++ )
				{
					// Normalised cone coordinates. Because the cone is a fixed 1 by 1, both of
					// these land in 0..1 with no extra parameters: zn runs apex to base, and rn2
					// is the squared radial position, 0 on the axis and 1 on the cone surface.
					// One rsqrt serves both falloffs. len comes back for a single multiply,
					// because d2 * rsqrt( d2 ) is just sqrt( d2 ).
					float d2     = dot( p, p ) + apexSoft2;
					float invLen = rsqrt( d2 );
					float len    = d2 * invLen;
					// Angular falloff, the same construction URP uses for a spot light:
					//     atten = saturate( SdotL * invRange - cosOuter * invRange ), squared
					// SdotL is the cosine of the angle off the beam axis, so measuring it from
					// the apex reproduces Unity distribution exactly, including the flat fully
					// lit core inside the inner angle. A pow curve cannot do that plateau, which
					// is why the older RadialCurve version fell off from the axis immediately.
					float cosA   = p.z * invLen;
					float ang    = saturate( ( cosA - cosOuter ) * invAngleRange );
					float radial = ang * ang;
					// Falloff with distance from the apex, not along the axis. Light falls off on
					// spherical shells, so the rim of any cross section is genuinely further from
					// the source than its centre and has to be dimmer. Measuring on flat planes,
					// which this did before, is what made the beam read as a shaped tube instead
					// of a light. Still a normalised curve rather than an inverse square, so
					// there is no blowup at the apex and no range to keep in sync.
					float axial = pow( saturate( 1.0 - len / coneLen ), DistanceCurve );
					// A real spot is not merely brighter at the source, it is brighter by a large
					// factor: inverse square makes the apex hundreds of times the mid cone value,
					// whereas the curve above tops out at 1 and so peaks only about four times the
					// middle. That missing headroom is what stops this reading as a lamp.
					//
					// Rather than swap the whole falloff and disturb the part that already looks
					// right, this multiplies in a bump that decays as 1/(1 + (len/CoreRadius)^2).
					// Beyond a couple of core radii the factor is essentially 1, so everything
					// past the core is left exactly as it was. CoreBoost of 0 is a no-op.
					axial *= 1.0 + CoreBoost * ( coreR2 / ( d2 + coreR2 ) );
					// Density is the cone shaping multiplied by the noise, so the beam keeps its
					// spot light distribution while the noise only breaks it up.
					acc += radial * axial * ValueNoise3d( np );
					p   += dp;
					np  += dnp;
				}
				return acc * stepLen;
			}
			
			float4 ASEScreenPositionNormalizedToPixel( float4 screenPosNorm, float4 screenParams )
			{
				float4 screenPosPixel = screenPosNorm * float4( screenParams.xy, 1, 1 );
				#if UNITY_UV_STARTS_AT_TOP
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x < 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#else
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x > 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#endif
				return screenPosPixel;
			}
			

			float4 _SelectionID;

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction(Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 ase_positionWS = TransformObjectToWorld( ( input.positionOS ).xyz );
				output.ase_texcoord.xyz = ase_positionWS;
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				output.ase_texcoord1 = screenPos;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord.w = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = ASE_ADJUST_CLIP_POSITION( vertexInput.positionCS );
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input ) : SV_Target
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float3 ase_positionWS = input.ase_texcoord.xyz;
				float3 ase_viewVectorWS = ( ( unity_OrthoParams.w == 0 ) ? _WorldSpaceCameraPos - ase_positionWS : UNITY_MATRIX_V[ 2 ].xyz );
				float3 ase_viewDirWS = normalize( ase_viewVectorWS );
				float3 ViewDirWS12 = ase_viewDirWS;
				float4 screenPos = input.ase_texcoord1;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float depthLinearEye201 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ), _ZBufferParams );
				float SceneDepth12 = depthLinearEye201;
				float Steps12 = _Steps;
				float4 ase_positionSS_Pixel = ASEScreenPositionNormalizedToPixel( ase_positionSSNorm, _ScaledScreenParams );
				float4 ScreenPos12 = ase_positionSS_Pixel;
				float DitherStrength12 = _DitherStrength;
				float DitherSpeed12 = ( 17.7 * PI );
				float InnerAngle12 = _InnerAngle;
				float DistanceCurve12 = _DistanceCurve;
				float ApexSoftness12 = _ApexSoftness;
				float3 NoiseSpeed12 = _NoiseSpeed;
				float3 NoiseScale12 = _NoiseScale;
				float CoreBoost12 = _CoreBoost;
				float CoreRadius12 = _CoreRadius;
				float localConeVolume12 = ConeVolume( ViewDirWS12 , SceneDepth12 , Steps12 , ScreenPos12 , DitherStrength12 , DitherSpeed12 , InnerAngle12 , DistanceCurve12 , ApexSoftness12 , NoiseSpeed12 , NoiseScale12 , CoreBoost12 , CoreRadius12 );
				

				surfaceDescription.Alpha = ( localConeVolume12 * -_Brighness );
				#if defined( _ALPHATEST_ON )
					surfaceDescription.AlphaClipThreshold = _Cutoff;
				#endif

				#ifdef _ALPHATEST_ON
					clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
				#endif

				half4 outColor = 0;
				outColor = unity_SelectionID;

				return outColor;
			}

			ENDHLSL
		}

	
	}
	

	

	CustomEditor "UnityEditor.ShaderGraphUnlitGUI"
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Fallback Off
}
/*ASEBEGIN
Version=19912
{"type":"AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor","id":170,"pos":[-344,-880],"params":["Inherit","False","642.0834","649.2089","Ray Marching","7","212","13","217","12","218","200","201","","1,1,1,1","0","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":207,"pos":[1224,-352],"params":["Inherit","False","Property","_Brighness","Brighness","3","0","Create","True","0","0","0","False","0","False","Object","-1","","0","0","0","0","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.ScreenDepthNode, AmplifyShaderEditor","id":201,"pos":[-304,-680],"params":["Inherit","False","0","1","0","FLOAT4","0,0,0,0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.ViewDirInputsCoordNode, AmplifyShaderEditor","id":200,"pos":[-304,-832],"params":["Inherit","False","World","False","0","4","FLOAT3","0","FLOAT","1","FLOAT","2","FLOAT","3"]}
{"type":"AmplifyShaderEditor.ScreenPosInputsNode, AmplifyShaderEditor","id":218,"pos":[-296,-520],"params":["Float","False","4","False","0","5","FLOAT4","0","FLOAT","1","FLOAT","2","FLOAT","3","FLOAT","4"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":250,"pos":[-608,200],"params":["Inherit","False","Property","_InnerAngle","InnerAngle","4","1","[Header]","Create","True","1","Cone Falloff","0","0","False","0","False","Object","-1","","0.5","0","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.Vector3Node, AmplifyShaderEditor","id":253,"pos":[-608,440],"params":["Inherit","False","Property","_NoiseSpeed","NoiseSpeed","9","1","[Header]","Create","True","1","3D Noise","0","0","False","0","False","Object","-1","","0,-0.05,0","0,0,0","0","4","FLOAT3","0","FLOAT","1","FLOAT","2","FLOAT","3"]}
{"type":"AmplifyShaderEditor.Vector3Node, AmplifyShaderEditor","id":254,"pos":[-608,520],"params":["Inherit","False","Property","_NoiseScale","NoiseScale","10","0","Create","True","0","0","0","False","0","False","Object","-1","","15,15,15","0,0,0","0","4","FLOAT3","0","FLOAT","1","FLOAT","2","FLOAT","3"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":261,"pos":[-608,680],"params":["Inherit","False","Property","_CoreRadius","CoreRadius","7","0","Create","True","0","0","0","False","0","False","Object","-1","","0.025","0","0.02","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":251,"pos":[-40,288],"params":["Inherit","False","Property","_DistanceCurve","DistanceCurve","8","0","Create","True","0","0","0","False","0","False","Object","-1","","6.5","0","0.01","8","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":252,"pos":[-72,88],"params":["Inherit","False","Property","_ApexSoftness","ApexSoftness","5","0","Create","True","0","0","0","False","0","False","Object","-1","","0","0","0","0.25","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.PiNode, AmplifyShaderEditor","id":217,"pos":[16,-472],"params":["Inherit","False","1","0","FLOAT","17.7","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":13,"pos":[16,-392],"params":["Inherit","False","Property","_Steps","Steps","11","2","[Header]","[IntRange]","Create","True","1","Ray Marching","0","0","False","0","False","Object","-1","","12","0","1","128","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":212,"pos":[16,-320],"params":["Inherit","False","Property","_DitherStrength","DitherStrength","12","0","Create","True","1","Dither","0","0","False","0","False","Object","-1","","1","0","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":260,"pos":[-608,600],"params":["Inherit","False","Property","_CoreBoost","CoreBoost","6","1","[Header]","Create","True","1","Distance Falloff","0","0","False","0","False","Object","-1","","20","0","0","32","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor","id":140,"pos":[304,-880],"params":["Inherit","False","484","604.3251","Color","6","168","85","38","90","88","87","","1,1,1,1","0","0"]}
{"type":"AmplifyShaderEditor.NegateNode, AmplifyShaderEditor","id":209,"pos":[1224,-424],"params":["Inherit","False","1","0","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.CustomExpressionNode, AmplifyShaderEditor","id":12,"pos":[16,-832],"params":["Inherit","False","// A cone volume, ray marched. The cone is fixed in object space exactly as the\n// analytic version had it: apex at the origin, axis along +Z, 1 unit deep and\n// 1 unit across at the base. Scale the proxy mesh to place and size it. The mesh\n// only has to contain that cone, so building in a margin is the right call: the\n// silhouette comes from this maths, never from the triangles.\nconst float coneK   = 0.5;\nconst float coneLen = 1.0;\nconst float k2      = coneK * coneK;\n\nfloat3 ro    = mul( GetWorldToObjectMatrix(), float4( _WorldSpaceCameraPos, 1.0 ) ).xyz;\nfloat3 rdOS  = mul( ( float3x3 )GetWorldToObjectMatrix(), -ViewDirWS );\nfloat  lenOS = length( rdOS );\nfloat3 rd    = rdOS / lenOS;\n\nfloat tScene = SceneDepth / dot( -ViewDirWS, GetViewForwardDir() ) * lenOS;\n\n// Clip the visible span down to the cone. Cap planes first.\nfloat tEnter = 0.0;\nfloat tExit  = tScene;\n\nif ( abs( rd.z ) > 1e-6 )\n{\n\tfloat ta = ( 0.0 - ro.z ) / rd.z;\n\tfloat tb = ( coneLen - ro.z ) / rd.z;\n\ttEnter = max( tEnter, min( ta, tb ) );\n\ttExit  = min( tExit,  max( ta, tb ) );\n}\nelse if ( ro.z < 0.0 || ro.z > coneLen )\n\treturn 0.0;\n\nif ( tExit <= tEnter )\n\treturn 0.0;\n\n// Then the cone surface itself: x*x + y*y = k*k * z*z.\nfloat A = dot( rd.xy, rd.xy ) - k2 * rd.z * rd.z;\nfloat B = 2.0 * ( dot( ro.xy, rd.xy ) - k2 * ro.z * rd.z );\nfloat C = dot( ro.xy, ro.xy ) - k2 * ro.z * ro.z;\n\nif ( abs( A ) > 1e-6 )\n{\n\tfloat disc = B * B - 4.0 * A * C;\n\tif ( disc > 0.0 )\n\t{\n\t\tfloat sq  = sqrt( disc );\n\t\tfloat inv = 0.5 / A;\n\t\tfloat r0  = ( -B - sq ) * inv;\n\t\tfloat r1  = ( -B + sq ) * inv;\n\t\tif ( r0 > r1 ) { float sw = r0; r0 = r1; r1 = sw; }\n\n\t\tif ( A > 0.0 )\n\t\t{\n\t\t\ttEnter = max( tEnter, r0 );\n\t\t\ttExit  = min( tExit,  r1 );\n\t\t}\n\t\telse\n\t\t{\n\t\t\t// Looking along the beam: the valid span is outside the roots, so it\n\t\t\t// splits in two. The cap clip already picked a nappe, so keep the\n\t\t\t// piece that still has length.\n\t\t\tif ( tEnter >= r1 || tExit <= r0 )\n\t\t\t{\n\t\t\t}\n\t\t\telse if ( tEnter < r0 )\n\t\t\t\ttExit = min( tExit, r0 );\n\t\t\telse\n\t\t\t\ttEnter = max( tEnter, r1 );\n\t\t}\n\t}\n\telse if ( C > 0.0 )\n\t\treturn 0.0;\n}\nelse if ( abs( B ) > 1e-6 )\n{\n\tfloat r = -C / B;\n\tif ( B > 0.0 ) tExit  = min( tExit, r );\n\telse           tEnter = max( tEnter, r );\n}\nelse if ( C > 0.0 )\n\treturn 0.0;\n\nif ( tExit <= tEnter )\n\treturn 0.0;\n\n// Guard the sign gymnastics above: the midpoint must really be inside.\nfloat3 pm = ro + rd * ( 0.5 * ( tEnter + tExit ) );\nif ( pm.z < 0.0 || pm.z > coneLen || dot( pm.xy, pm.xy ) > k2 * pm.z * pm.z )\n\treturn 0.0;\n\nfloat rayLen  = tExit - tEnter;\nint   steps   = (int)Steps;\nfloat stepLen = rayLen / steps;\n\n// Animated dither, as before: jitter each pixel first sample inside its own\n// step so the step boundaries stop lining up and banding becomes grain.\nfloat ign = frac( 52.9829189 * frac( dot( ScreenPos.xy, float2( 0.06711056, 0.00583715 ) ) ) );\nign = frac( ign + _TimeParameters.x * DitherSpeed );\nfloat jitter = 0.5 + ( ign - 0.5 ) * DitherStrength;\n\n// The cone shape fixes the outer angle: tan( theta ) = coneK, so cos( theta )\n// falls straight out of it. InnerAngle is the inner/outer ratio, matching how\n// Unity pairs a spot inner angle with its outer angle. 0 ramps all the way from\n// the axis, 1 collapses the ramp into a hard rim. All of this is per pixel, not\n// per sample, so the loop only pays the rsqrt and a multiply add.\nfloat cosOuter      = rsqrt( 1.0 + k2 );\nfloat cosInner      = cos( saturate( InnerAngle ) * atan( coneK ) );\nfloat invAngleRange = 1.0 / max( cosInner - cosOuter, 1e-4 );\n\n// Squared once here so the loop pays an add instead of the max it replaces.\n// This bounds how fast the angle can swing near the apex, where |p| tends to 0\n// and a sub-millimetre step would otherwise change the angle enormously. That\n// runaway, not the sample count, is what makes the tip look noisy. It is also\n// the physically honest form: a real emitter has size, so its angular falloff\n// is not infinitely sharp at the source.\nfloat apexSoft2 = ApexSoftness * ApexSoftness + 1e-8;\n\n// Radius of the bright core, squared once here. This is a separate scale from\n// ApexSoftness on purpose: that one bounds how fast the angle may swing, this\n// one says how far the extra brightness near the source reaches.\nfloat coreR2 = CoreRadius * CoreRadius + 1e-8;\n\nfloat3 dp = rd * stepLen;\nfloat3 p  = ro + rd * ( tEnter + stepLen * jitter );\n\n// Noise coordinates ride along with the march, advanced by the same fixed step\n// so no extra multiply is needed per sample. Kept in object space, so the\n// pattern scales with the proxy mesh exactly as it does in the sphere shaders.\n// The drift wraps at the noise period, which makes the wrap seamless and keeps\n// the coordinate small enough to stay precise no matter how long the world runs.\nfloat3 noiseOffset = fmod( _TimeParameters.x * ( NoiseSpeed * NoiseScale ), 4096.0 );\nfloat3 dnp = dp * NoiseScale;\nfloat3 np  = p * NoiseScale + noiseOffset;\n\nfloat acc = 0.0;\n\n[loop]\nfor ( int i = 0; i < steps; i++ )\n{\n\t// Normalised cone coordinates. Because the cone is a fixed 1 by 1, both of\n\t// these land in 0..1 with no extra parameters: zn runs apex to base, and rn2\n\t// is the squared radial position, 0 on the axis and 1 on the cone surface.\n\t// One rsqrt serves both falloffs. len comes back for a single multiply,\n\t// because d2 * rsqrt( d2 ) is just sqrt( d2 ).\n\tfloat d2     = dot( p, p ) + apexSoft2;\n\tfloat invLen = rsqrt( d2 );\n\tfloat len    = d2 * invLen;\n\n\t// Angular falloff, the same construction URP uses for a spot light:\n\t//     atten = saturate( SdotL * invRange - cosOuter * invRange ), squared\n\t// SdotL is the cosine of the angle off the beam axis, so measuring it from\n\t// the apex reproduces Unity distribution exactly, including the flat fully\n\t// lit core inside the inner angle. A pow curve cannot do that plateau, which\n\t// is why the older RadialCurve version fell off from the axis immediately.\n\tfloat cosA   = p.z * invLen;\n\tfloat ang    = saturate( ( cosA - cosOuter ) * invAngleRange );\n\tfloat radial = ang * ang;\n\n\t// Falloff with distance from the apex, not along the axis. Light falls off on\n\t// spherical shells, so the rim of any cross section is genuinely further from\n\t// the source than its centre and has to be dimmer. Measuring on flat planes,\n\t// which this did before, is what made the beam read as a shaped tube instead\n\t// of a light. Still a normalised curve rather than an inverse square, so\n\t// there is no blowup at the apex and no range to keep in sync.\n\tfloat axial = pow( saturate( 1.0 - len / coneLen ), DistanceCurve );\n\n\t// A real spot is not merely brighter at the source, it is brighter by a large\n\t// factor: inverse square makes the apex hundreds of times the mid cone value,\n\t// whereas the curve above tops out at 1 and so peaks only about four times the\n\t// middle. That missing headroom is what stops this reading as a lamp.\n\t//\n\t// Rather than swap the whole falloff and disturb the part that already looks\n\t// right, this multiplies in a bump that decays as 1/(1 + (len/CoreRadius)^2).\n\t// Beyond a couple of core radii the factor is essentially 1, so everything\n\t// past the core is left exactly as it was. CoreBoost of 0 is a no-op.\n\taxial *= 1.0 + CoreBoost * ( coreR2 / ( d2 + coreR2 ) );\n\n\t// Density is the cone shaping multiplied by the noise, so the beam keeps its\n\t// spot light distribution while the noise only breaks it up.\n\tacc += radial * axial * ValueNoise3d( np );\n\tp   += dp;\n\tnp  += dnp;\n}\n\nreturn acc * stepLen;","1","Create","13","False","ViewDirWS","FLOAT3","0,0,0","In","","Inherit","False","False","SceneDepth","FLOAT","0","In","","Inherit","False","False","Steps","FLOAT","0","In","","Inherit","False","False","ScreenPos","FLOAT4","0,0,0,0","In","","Inherit","False","False","DitherStrength","FLOAT","0","In","","Inherit","False","False","DitherSpeed","FLOAT","0","In","","Inherit","False","False","InnerAngle","FLOAT","0","In","","Inherit","False","False","DistanceCurve","FLOAT","0","In","","Inherit","False","False","ApexSoftness","FLOAT","0","In","","Inherit","False","False","NoiseSpeed","FLOAT3","0,0,0","In","","Inherit","False","False","NoiseScale","FLOAT3","0,0,0","In","","Inherit","False","False","CoreBoost","FLOAT","0","In","","Inherit","False","True","CoreRadius","FLOAT","0","In","","Inherit","False","Cone Volume","False","True","1","73","","False","13","0","FLOAT3","0,0,0","False","1","FLOAT","0","False","2","FLOAT","0","False","3","FLOAT4","0,0,0,0","False","4","FLOAT","0","False","5","FLOAT","0","False","6","FLOAT","0","False","7","FLOAT","0","False","8","FLOAT","0","False","9","FLOAT3","0,0,0","False","10","FLOAT3","0,0,0","False","11","FLOAT","0","False","12","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.PowerNode, AmplifyShaderEditor","id":88,"pos":[352,-544],"params":["Inherit","False","False","2","0","FLOAT","0","False","1","FLOAT","2","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.LerpOp, AmplifyShaderEditor","id":85,"pos":[608,-712],"params":["Inherit","False","3","0","FLOAT3","0,0,0","False","1","FLOAT3","0,0,0","False","2","FLOAT","0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RelayNode, AmplifyShaderEditor","id":168,"pos":[352,-360],"params":["Inherit","False","1","0","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":86,"pos":[856,-712],"params":["Inherit","False","2","2","0","FLOAT3","0,0,0","False","1","FLOAT","0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.ColorNode, AmplifyShaderEditor","id":87,"pos":[352,-688],"params":["Inherit","False","Property","_ColorB","ColorB","1","1","[HDR]","Create","True","0","0","0","False","0","False","Object","-1","","1,1,1,0","0,0,0,0","True","False","0","6","FLOAT3","0","FLOAT","1","FLOAT","2","FLOAT","3","FLOAT","4","FLOAT3","5"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":90,"pos":[352,-448],"params":["Inherit","False","Property","_GradientCurve","GradientCurve","2","0","Create","True","0","0","0","False","0","False","Object","-1","","0.4","0","0.1","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.ColorNode, AmplifyShaderEditor","id":38,"pos":[352,-832],"params":["Inherit","False","Property","_ColorA","ColorA","0","2","[HDR]","[Header]","Create","True","1","Coloring","0","0","False","0","False","Object","-1","","0.4735848,1,1,0","0,0,0,0","True","False","0","6","FLOAT3","0","FLOAT","1","FLOAT","2","FLOAT","3","FLOAT","4","FLOAT3","5"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":208,"pos":[1224,-520],"params":["Inherit","False","2","2","0","FLOAT","0","False","1","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.CustomExpressionNode, AmplifyShaderEditor","id":72,"pos":[856,-608],"params":["Inherit","False","// Fold n onto the hash's 4096-cell ring: n is integer-valued and far below\n// 2^24, so frac( n / 4096 ) is exact and every product below stays within\n// fp32 precision no matter how far the noise has drifted. This also makes the\n// noise periodic every 4096 cells, letting the time offset wrap seamlessly.\nfloat4 m = frac( n * 0.000244140625 );\nreturn frac( m * 69632.0 * frac( m * 1303.7973 ) );","4","Create","1","True","n","FLOAT4","0,0,0,0","In","","Inherit","False","hash41","False","True","0","","False","1","0","FLOAT4","0,0,0,0","False","1","FLOAT4","0"]}
{"type":"AmplifyShaderEditor.CustomExpressionNode, AmplifyShaderEditor","id":73,"pos":[856,-528],"params":["Inherit","False","float3 i = floor( p );\nfloat3 f = frac( p );\nf = f * f * ( 3.0 - 2.0 * f );\n\n// Encode the lattice cell as one scalar; the 8 corners become constant offsets.\n// The +113 bias keeps the cell at the origin off the hash's zero.\nfloat n = i.x + 317.0 * i.y + 157.0 * i.z + 113.0;\n\n// Corner order per float4: (0,0) (1,0) (0,1) (1,1) in xy, one float4 per z slice.\nfloat4 xy = n + float4( 0.0, 1.0, 317.0, 318.0 );\nfloat4 h  = lerp( hash41( xy ), hash41( xy + 157.0 ), f.z );\nfloat2 hx = lerp( h.xz, h.yw, f.x );\nreturn lerp( hx.x, hx.y, f.y );","1","Create","1","True","p","FLOAT3","0,0,0","In","","Inherit","False","ValueNoise3d","False","True","1","72","","False","1","0","FLOAT3","0,0,0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":232,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","ExtraPrePass","0","0","ExtraPrePass","6","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","0","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":234,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","ShadowCaster","0","2","ShadowCaster","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","False","False","True","False","False","False","False","0","False","","False","False","False","False","False","False","False","False","False","True","1","False","","True","3","False","","False","False","True","1","LightMode=ShadowCaster","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":235,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","DepthOnly","0","3","DepthOnly","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","False","False","True","False","False","False","False","0","False","","False","False","False","False","False","False","False","False","False","True","1","False","","False","False","False","True","1","LightMode=DepthOnly","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":236,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","Meta","0","4","Meta","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","2","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","LightMode=Meta","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":237,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","Universal2D","0","5","Universal2D","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","1","LightMode=Universal2D","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":238,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","SceneSelectionPass","0","6","SceneSelectionPass","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","2","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","LightMode=SceneSelectionPass","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":239,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","ScenePickingPass","0","7","ScenePickingPass","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","LightMode=Picking","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":240,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","DepthNormals","0","8","DepthNormals","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","False","","True","3","False","","False","False","True","1","LightMode=DepthNormalsOnly","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":241,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","DepthNormalsOnly","0","9","DepthNormalsOnly","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","False","","True","3","False","","False","False","True","1","LightMode=DepthNormalsOnly","False","True","11","d3d11","metal","vulkan","xboxone","xboxseries","playstation","ps4","ps5","switch","switch2","webgpu","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":242,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","MotionVectors","0","10","MotionVectors","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","True","True","False","False","0","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","LightMode=MotionVectors","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":243,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","XRMotionVectors","0","11","XRMotionVectors","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","True","1","False","","255","False","","1","False","","7","False","","3","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","False","False","False","False","True","1","LightMode=XRMotionVectors","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":244,"pos":[1224,-808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","1","New Amplify Shader","2992e84f91cbeb14eab234972e07ea9d","True","GBuffer","0","12","GBuffer","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","1","LightMode=UniversalGBuffer","False","True","12","d3d11","gles","metal","vulkan","xboxone","xboxseries","playstation","ps4","ps5","switch","switch2","webgpu","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":233,"pos":[1224,-808],"params":["Float","False","True","-1","3","UnityEditor.ShaderGraphUnlitGUI","0","24","Community/VFog Cone March Noise","2992e84f91cbeb14eab234972e07ea9d","True","Forward","0","1","Forward","12","False","True","1","1","False","","0","False","","1","1","False","","0","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","1","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","True","True","2","False","","False","False","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Transparent=RenderType","Queue=Transparent=Queue=0","UniversalMaterialType=Unlit","True","5","True","14","all","0","False","True","1","1","False","","10","False","","1","1","False","","10","False","","True","1","False","","1","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","True","True","2","False","","True","7","False","","True","False","0","False","","0","False","","False","True","1","LightMode=UniversalForwardOnly","False","False","4","Include","","False","","Native","False","0","0","","Pragma","multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN","False","","Custom","False","0","0","","Pragma","multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH","False","","Custom","False","0","0","","Pragma","multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS","False","","Custom","False","0","0","","","0","0","Standard","33","Surface","1","639225154279776056","  Keep Alpha","0","0","  Blend","1","639225154296421842","Two Sided","2","639225154692044986","Alpha Clipping","0","0","  Use Shadow Threshold","0","0","Fragment Normal Space","0","0","Forward Only","1","639225154561059774","Cast Shadows","0","639225154322240760","Receive Shadows","1","639225154332691513","Receive SSAO","0","639225154358691139","Default Decal Blending","0","639225154373436240","Motion Vectors","0","639225154383733052","  Additional Motion Vectors","1","0","  Alembic Motion Vectors","0","0","  XR Motion Vectors","0","0","GPU Instancing","1","0","LOD CrossFade","0","639225154401990998","Built-in Fog","0","639225154406194123","Meta Pass","0","0","Extra Pre Pass","0","0","Tessellation","0","0","  Phong","0","0","  Strength","0.5,False,","0","  Type","0","0","  Tess","16,False,","0","  Min","10,False,","0","  Max","25,False,","0","  Edge Length","16,False,","0","  Max Displacement","25,False,","0","Write Depth","0","0","  Conservative","0","0","Vertex Position","1","0","0","13","False","True","False","False","False","False","True","True","False","False","False","False","False","False","","False","0"]}
{"wire":[209,0,207,0]}
{"wire":[12,0,200,0]}
{"wire":[12,1,201,0]}
{"wire":[12,2,13,0]}
{"wire":[12,3,218,0]}
{"wire":[12,4,212,0]}
{"wire":[12,5,217,0]}
{"wire":[12,6,250,0]}
{"wire":[12,7,251,0]}
{"wire":[12,8,252,0]}
{"wire":[12,9,253,0]}
{"wire":[12,10,254,0]}
{"wire":[12,11,260,0]}
{"wire":[12,12,261,0]}
{"wire":[88,0,12,0]}
{"wire":[88,1,90,0]}
{"wire":[85,0,38,0]}
{"wire":[85,1,87,0]}
{"wire":[85,2,88,0]}
{"wire":[168,0,12,0]}
{"wire":[86,0,85,0]}
{"wire":[86,1,168,0]}
{"wire":[208,0,12,0]}
{"wire":[208,1,209,0]}
{"wire":[233,2,86,0]}
{"wire":[233,3,208,0]}
ASEEND*/
//CHKSM=666AD2C4E6FE335993C2518F393855A43A10A481