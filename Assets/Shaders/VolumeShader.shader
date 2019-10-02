Shader "AbstractMath/VolumeShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
			#pragma target 3.0

            #include "UnityCG.cginc"

			sampler2D _MainTex;
			uniform float4x4 _CamFrustrum, _CamToWorld;
			uniform float _maxDistance;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
				float3 ray : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
				half index = v.vertex.z;
				v.vertex.z = 0;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

				o.ray = _CamFrustrum[(int)index].xyz;
				o.ray /= abs(o.ray.z);
				o.ray = mul(_CamToWorld, o.ray);
                return o;
            }

			float sdSphere(float3 p, float s) 
			{
				return length(p) - s;
			}

			float densityField(float3 p) 
			{
				float sphere1 = sdSphere(p, 1);
				float result = 0;

				if (sphere1 <= 1) {
					result = 0.5f;
				}

				return result;
			}

			fixed4 raymarching(float3 ro, float3 rd) 
			{
				const int max_iteration = 64;
				float t = 0;//Distance travelled.
				float stepSize = 1.0f / max_iteration;
				float accumDist = 0;

				for (int i = 0; i < 4 * max_iteration; i++) 
				{

					float3 p = ro + rd * t;
					float curSample = densityField(p);
					accumDist += curSample * stepSize;
					t += stepSize;

				}

				return accumDist;
			}

            fixed4 frag (v2f i) : SV_Target
            {
				fixed3 col = tex2D(_MainTex, i.uv);
				float3 rayDirection = normalize(i.ray.xyz);
				float3 rayOrigin = _WorldSpaceCameraPos;
				fixed4 result = raymarching(rayOrigin, rayDirection);
				float mult = min(1, result.w);

				return fixed4(col * (1 - mult) + fixed4(0.5f, 0.5f, 0.5f, 1) * mult , 1);
            }
            ENDCG
        }
    }
}
