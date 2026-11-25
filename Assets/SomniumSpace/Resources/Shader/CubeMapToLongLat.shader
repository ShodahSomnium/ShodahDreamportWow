// Made with Amplify Shader Editor v1.9.1.8
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "CubeMapToLongLat"
{
    Properties
    {
        _CubeMap("CubeMap", CUBE) = "white" {}

    }

    SubShader
    {
		LOD 0

        
		CGINCLUDE
		#pragma target 3.0
		ENDCG
		Blend Off
		AlphaToMask Off
		Cull Back
		ColorMask RGBA
		ZWrite On
		ZTest LEqual
		Offset 0 , 0
		
		
        Pass
        {
			Name "Custom RT Init"
            CGPROGRAM
            
            #include "UnityCustomRenderTexture.cginc"

            #pragma vertex ASEInitCustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 3.0
			

			struct ase_appdata_init_customrendertexture
			{
				float4 vertex : POSITION;
				float4 texcoord : TEXCOORD0;
				
			};

			// User facing vertex to fragment structure for initialization materials
			struct ase_v2f_init_customrendertexture
			{
				float4 vertex : SV_POSITION;
				float2 texcoord : TEXCOORD0;
				float3 direction : TEXCOORD1;
				
			};

			uniform samplerCUBE _CubeMap;
			float3 RotateAroundAxis( float3 center, float3 original, float3 u, float angle )
			{
				original -= center;
				float C = cos( angle );
				float S = sin( angle );
				float t = 1 - C;
				float m00 = t * u.x * u.x + C;
				float m01 = t * u.x * u.y - S * u.z;
				float m02 = t * u.x * u.z + S * u.y;
				float m10 = t * u.x * u.y + S * u.z;
				float m11 = t * u.y * u.y + C;
				float m12 = t * u.y * u.z - S * u.x;
				float m20 = t * u.x * u.z - S * u.y;
				float m21 = t * u.y * u.z + S * u.x;
				float m22 = t * u.z * u.z + C;
				float3x3 finalMatrix = float3x3( m00, m01, m02, m10, m11, m12, m20, m21, m22 );
				return mul( finalMatrix, original ) + center;
			}
			


			ase_v2f_init_customrendertexture ASEInitCustomRenderTextureVertexShader (ase_appdata_init_customrendertexture v )
			{
				ase_v2f_init_customrendertexture o;
				
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.texcoord = float3(v.texcoord.xy, CustomRenderTexture3DTexcoordW);
				o.direction = CustomRenderTextureComputeCubeDirection(v.texcoord.xy);
				return o;
			}

            float4 frag(ase_v2f_init_customrendertexture IN ) : COLOR
            {
                float4 finalColor;
				float2 break17 = ( IN.texcoord + float2( 0.25,-0.5 ) );
				float3 rotatedValue19 = RotateAroundAxis( float3( 0,0,0 ), float3( 1,0,0 ), float3( 0,0,1 ), ( break17.y * UNITY_PI ) );
				float3 rotatedValue22 = RotateAroundAxis( float3( 0,0,0 ), rotatedValue19, float3( 0,1,0 ), ( break17.x * ( 2.0 * UNITY_PI ) ) );
				
                finalColor = texCUBE( _CubeMap, rotatedValue22 );
				return finalColor;
            }
            ENDCG
        }
    }
	
	CustomEditor "ASEMaterialInspector"
	Fallback Off
}
/*ASEBEGIN
Version=19108
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;11;-1092.35,16.52072;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;13;-1083.85,185.5207;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PiNode;12;-1092.023,105.1836;Inherit;False;1;0;FLOAT;2;False;1;FLOAT;0
Node;AmplifyShaderEditor.PiNode;14;-1080.85,275.5207;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RotateAboutAxisNode;22;-811.8433,7.654651;Inherit;False;False;4;0;FLOAT3;0,1,0;False;1;FLOAT;0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RotateAboutAxisNode;19;-812.9204,148.037;Inherit;False;False;4;0;FLOAT3;0,0,1;False;1;FLOAT;0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;1,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SamplerNode;1;-458.7109,-2.523697;Inherit;True;Property;_CubeMap;CubeMap;0;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;LockedToCube;False;Object;-1;Auto;Cube;8;0;SAMPLERCUBE;;False;1;FLOAT3;0,0,0;False;2;FLOAT;0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.BreakToComponentsNode;17;-1289.213,17.42079;Inherit;False;FLOAT2;1;0;FLOAT2;0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.TexCoordVertexDataNode;15;-1686.893,44.55352;Inherit;False;0;2;0;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleAddOpNode;16;-1450.506,25.20731;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0.25,-0.5;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;23;-119.0775,8.144219;Float;False;True;-1;2;ASEMaterialInspector;0;1;CubeMapToLongLat;6ce779933eb99f049b78d6163735e06f;True;Custom RT Init;0;0;Custom RT Init;1;False;True;0;1;False;;0;False;;0;1;False;;0;False;;True;0;False;;0;False;;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;0;True;2;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;0;;0;0;Standard;0;0;1;True;False;;False;0
WireConnection;11;0;17;0
WireConnection;11;1;12;0
WireConnection;13;0;17;1
WireConnection;13;1;14;0
WireConnection;22;1;11;0
WireConnection;22;3;19;0
WireConnection;19;1;13;0
WireConnection;1;1;22;0
WireConnection;17;0;16;0
WireConnection;16;0;15;0
WireConnection;23;0;1;0
ASEEND*/
//CHKSM=83DF8DE02EB8BF9C9A815466845B082ED199308C