// Makes the stars of the login sky twinkle. Bright, small, whitish pixels above the mountains
// brighten and dim, each patch of sky on its own beat; the moon is left alone. Based on UI/Default.
Shader "DaKiem/UI/Twinkle"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Threshold ("Brightness that counts as a star", Float) = 0.62
        _SkyFrom ("Lowest part of the texture that is sky (0 bottom, 1 top)", Float) = 0.45
        _Amount ("How much a star brightens", Float) = 0.9
        _Speed ("Twinkles per second", Float) = 0.8
        _Cells ("Patches across the sky", Float) = 60
        _Moon ("Moon centre xy and radius z, in texture units", Vector) = (0.87, 0.72, 0.1, 0)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _Threshold, _SkyFrom, _Amount, _Speed, _Cells;
            float4 _Moon;

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                float light = max(c.r, max(c.g, c.b));
                float white = 1 - (max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b)));
                float star = smoothstep(_Threshold, _Threshold + 0.15, light) * smoothstep(0.55, 0.8, white);
                star *= step(_SkyFrom, i.uv.y);
                float2 fromMoon = (i.uv - _Moon.xy) * float2(1.777, 1);
                star *= step(_Moon.z, length(fromMoon));
                float beat = hash(floor(i.uv * _Cells));
                float twinkle = 0.5 + 0.5 * sin(_Time.y * _Speed * 6.2832 + beat * 6.2832);
                c.rgb *= 1 + star * (twinkle - 0.35) * _Amount;
                c.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
