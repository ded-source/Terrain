Shader "Unlit/TerrainShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _HeightMap ("Texture", 2D) = "black" {}
        _TerrainSize("Terrain Size Meters", Float) = 1.0
        _HeightScale("Terrain Height Scale", Float) = 1.0
        _TileSize("Terrain Tile Size Meters", Float) = 1.0

        [Toggle] _ENABLE_DEBUG_VIEW ("Enable Debug View", Float) = 0
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
            #pragma multi_compile_instancing

            #pragma multi_compile _ _ENABLE_DEBUG_VIEW_ON

            #include "UnityCG.cginc"

            #ifdef _ENABLE_DEBUG_VIEW_ON
            fixed4 randColor(float2 co)
            {
                float rand = frac(sin(dot(co.xy ,float2(12.9898,78.233))) * 43758.5453);
                fixed4 col;
                col.r = rand;
                col.g = frac(rand + 0.33); // Offset for different color channels
                col.b = frac(rand + 0.66);
                col.a = 1.0;
                return col;
            }
            #endif

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                #ifdef _ENABLE_DEBUG_VIEW_ON
                nointerpolation float2 tilePos : TEXCOORD1;
                #endif
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _HeightMap;
            float4 _HeightMap_ST;
            float _TerrainSize;
            float _HeightScale;
            float _TileSize;

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);

                // Sample heightmap, assumes terrain origin is 0, 0, 0
                float2 worldPos = mul(unity_ObjectToWorld, v.vertex).xz;
                float2 uv = worldPos / _TerrainSize;
                float vertexDistance = -UnityObjectToViewPos(v.vertex).z;
                float heightMapLod = log2(vertexDistance / _TileSize);
                float height = tex2Dlod(_HeightMap, float4(uv, 0, heightMapLod)).r;

                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex + float4(0.0, height * _HeightScale, 0.0, 0.0));
                o.uv = uv;
                #ifdef _ENABLE_DEBUG_VIEW_ON
                o.tilePos = mul(unity_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).xz;
                #endif
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                #ifdef _ENABLE_DEBUG_VIEW_ON
                fixed4 col = randColor(i.tilePos);
                #else
                fixed4 col = tex2D(_MainTex, i.uv);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
