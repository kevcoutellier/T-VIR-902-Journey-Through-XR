Shader "Skybox/SimpleGradient"
{
    // Minimal 3-color vertical-gradient skybox (zenith / horizon / ground).
    // Cheap on Quest (a handful of ALU ops, no texture sample) and pipeline-agnostic —
    // like Unity's own Skybox/Procedural, the skybox pass is drawn directly by the
    // camera regardless of render pipeline, so plain CGPROGRAM is safe under URP.
    Properties
    {
        _SkyColor      ("Sky (Zenith) Color", Color) = (0.45, 0.6, 0.8, 1)
        _HorizonColor  ("Horizon Color", Color) = (0.72, 0.78, 0.85, 1)
        _GroundColor   ("Ground Color", Color) = (0.5, 0.5, 0.52, 1)
        _HorizonHeight ("Horizon Softness", Range(0.01, 2)) = 0.4
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _SkyColor;
            fixed4 _HorizonColor;
            fixed4 _GroundColor;
            float  _HorizonHeight;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz; // skybox cube object-space position == view direction
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float h = normalize(i.dir).y;
                fixed4 col = (h >= 0)
                    ? lerp(_HorizonColor, _SkyColor, saturate(h / _HorizonHeight))
                    : lerp(_HorizonColor, _GroundColor, saturate(-h / _HorizonHeight));
                return col;
            }
            ENDCG
        }
    }
}
