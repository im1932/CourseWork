Shader "UI/TransitionBlur"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _LeftTex("Left Texture", 2D) = "white" {}
        _RightTex("Right Texture", 2D) = "white" {}
        _NoiseTex("Noise Texture", 2D) = "white" {}
        _BlendWidth("Blend Width", Range(0.01, 0.5)) = 0.4
        _NoiseStrength("Noise Strength", Range(0, 0.1)) = 0.05
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _LeftTex;
            sampler2D _RightTex;
            sampler2D _NoiseTex;
            float _BlendWidth;
            float _NoiseStrength;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Плавный переход слева → справа с увеличенной шириной блюра
                float t = smoothstep(0.2 - _BlendWidth, 0.2 + _BlendWidth, i.uv.x);

                // Добавляем шум, чтобы края не были прямыми
                float noise = tex2D(_NoiseTex, i.uv * 4.0).r; 
                t += (noise - 0.5) * _NoiseStrength;
                t = saturate(t);

                fixed4 leftCol = tex2D(_LeftTex, i.uv);
                fixed4 rightCol = tex2D(_RightTex, i.uv);

                return lerp(leftCol, rightCol, t);
            }
            ENDCG
        }
    }
}
