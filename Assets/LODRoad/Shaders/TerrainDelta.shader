Shader "Unlit/TerrainDelta"
{
    Properties
    {
        _HeightTex ("Heightmap", 2D) = "white" {}          //Terrain heightmap testure
        _DeltaTex ("Delta", 2D) = "white" {}               //Difference between terrain heightmap and mesh
        _MeshWeight ("Mesh weight", Float) = 1.0           //Mesh height texture
        _HeightmapWeight ("Heightmap weight", Float) = 0.0 //Weight of terrain heightmap
        _DeltaWeight ("Delta weight", Float) = 0.0         //Weight of difference between heightmap and mesh
        _HeightMult ("Height mult", Float) = 1.0           //Convert world height to texture height
        _Smoothness ("Smoothness", Float) = 2.0            //Smoothness of edges of heightmap modification
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            //#pragma enable_d3d11_debug_symbols

            #include "UnityCG.cginc"

            struct appdata{
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f{
                float4 vertex : SV_POSITION;
                float4 height : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _HeightTex, _DeltaTex;
            float _MeshWeight, _HeightmapWeight, _DeltaWeight, _HeightMult, _Smoothness;

            v2f vert (appdata v){
                v2f o;
                o.uv = v.uv;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.height = v.vertex*_HeightMult;
                o.height.xz=UnityObjectToViewPos(v.vertex).xy * 0.5 + 0.5;
                o.height.w=v.uv.y*_HeightMult;
                //printf("!");
                return o;
            }

            float4 frag (v2f i) : SV_Target{
                float3 delta=float3(0,0,0);
                delta.x = tex2D(_DeltaTex, i.height.xz).r * _DeltaWeight;
                delta.y = tex2D(_HeightTex,i.height.xz).r * _HeightmapWeight;
                //delta.w = tex2D(_PrevDeltaTex,i.height.xz).r * _PrevDeltaWeight;
                delta.z = lerp(-delta.y, max(i.height.y * _MeshWeight, 0), 1);

                if(_MeshWeight>0){
                    delta.z = max(lerp(-delta.y,  max(i.height.y*_MeshWeight,0),  saturate(-abs(i.uv.y*2-1)*_Smoothness+_Smoothness)),0);
                }
                return float4(delta.z + delta.y + delta.x,0,0,0);
            }
            ENDCG
        }
    }
}
