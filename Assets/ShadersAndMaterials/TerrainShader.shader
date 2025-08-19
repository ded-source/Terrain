Shader "Unlit/TerrainClipmapShader"
{
    Properties
    {
        _MainTex ("Albedo Map", 2D) = "white" {}
        _HeightMap ("Height Map", 2D) = "black" {}

        _MainTexClipmapArray ("Albedo Map Clipmap Array", 2DArray) = "" {}
        _MainTexClipmapArrayCount("Albedo Map Clipmap Array Count", Float) = 0.0
        _HeightMapClipmapArray("Height Map Clipmap Array", 2DArray) = "" {}
        _HeightMapClipmapArrayCount("Height Map Clipmap Array Count", Float) = 0.0

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
                float lod : TEXCOORD1;
                #ifdef _ENABLE_DEBUG_VIEW_ON
                nointerpolation float2 tilePos : TEXCOORD2;
                #endif
            };

            sampler2D _MainTex;
            sampler2D _HeightMap;
            float _TerrainSize;
            float _HeightScale;
            float _TileSize;

            UNITY_DECLARE_TEX2DARRAY(_MainTexClipmapArray);
            float _MainTexClipmapArrayCount;
            UNITY_DECLARE_TEX2DARRAY(_HeightMapClipmapArray);
            float _HeightMapClipmapArrayCount;

            float4 sampleClipmap(UNITY_ARGS_TEX2DARRAY(clipmapArray), float arrayCount, sampler2D baseTexture, float lod, float2 uv)
            {
                float4 col0 = float4(1, 0, 0, 1);
                float4 col1 = float4(0, 1, 0, 1);
                float lerpVal = 0.0;

                if(lod >= arrayCount - 1)
                {
                    col1 = tex2Dlod(baseTexture, float4(uv, 0.0, max(0, lod - arrayCount)));
                    lerpVal = 1.0;
                }

                if(lod < arrayCount)
                {
                    float sampleLod = floor(lod);
                    float clipmapUvScale = pow(2, arrayCount - sampleLod);
                    float2 clipmapUv = uv * clipmapUvScale - (clipmapUvScale * 0.5);
                    col0 = UNITY_SAMPLE_TEX2DARRAY_LOD(clipmapArray, float3(clipmapUv, sampleLod), 0);

                    if(lod < arrayCount - 1)
                    {
                        sampleLod = ceil(lod);
                        clipmapUvScale = pow(2, arrayCount - sampleLod);
                        clipmapUv = uv * clipmapUvScale - (clipmapUvScale * 0.5);
                        col1 = UNITY_SAMPLE_TEX2DARRAY_LOD(clipmapArray, float3(clipmapUv, sampleLod), 0);
                    }

                    lerpVal = frac(lod);
                }

                float4 col = lerp(col0, col1, lerpVal);
                return col;
            }

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);

                // Sample heightmap, assumes terrain origin is 0, 0, 0
                float2 worldPos = mul(unity_ObjectToWorld, v.vertex).xz;
                float2 uv = worldPos / _TerrainSize;
                float vertexDistance = -UnityObjectToViewPos(v.vertex).z;
                float lod = max(0, log2(vertexDistance / _TileSize / 4) - 0.5);

                float height = sampleClipmap(UNITY_PASS_TEX2DARRAY(_HeightMapClipmapArray), _HeightMapClipmapArrayCount, _HeightMap, lod, uv).r;
                
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex + float4(0.0, height * _HeightScale, 0.0, 0.0));
                o.uv = uv;
                o.lod = lod;
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

                fixed4 col = sampleClipmap(UNITY_PASS_TEX2DARRAY(_MainTexClipmapArray), _MainTexClipmapArrayCount, _MainTex, i.lod, i.uv);
                
                #endif
                return col;
            }
            ENDCG
        }
    }
}
