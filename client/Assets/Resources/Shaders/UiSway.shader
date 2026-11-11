// Moves a cut-out layer with the wind, for the login background. Each pixel is pushed sideways by
// a wave; pixels near a free top edge (nothing drawn just above them) move most, so blades of grass
// and the tips of a blossom cluster sway while their base stays put. Based on Unity's UI/Default.
Shader "DaKiem/UI/Sway"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Amp ("Sway, in texture widths", Float) = 0.004
        _Speed ("Waves per second", Float) = 1.5
        _Freq ("Waves across the texture", Float) = 12
        _Probe ("How far above to look for the edge", Float) = 0.04
        _Gust ("Gust strength, set by script", Float) = 1
        _ProbeOn ("1: only tips move (grass); 0: the whole layer moves (blossom)", Float) = 1
        _Along ("Waves run along x (0) or y (1)", Float) = 0
        _EdgeFade ("Near the left and top edges nothing moves, so cut art never shows", Float) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

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
            float _Amp, _Speed, _Freq, _Probe, _Gust, _ProbeOn, _Along, _EdgeFade;

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Free above: this pixel is near a tip and swings; covered above: it is near the base.
                float above = tex2D(_MainTex, i.uv + float2(0, _Probe)).a;
                float weight = lerp(1, saturate(1 - above), _ProbeOn);
                float fade = max(_EdgeFade, 0.0001);
                weight *= lerp(1, saturate(i.uv.x / fade) * saturate((1 - i.uv.y) / fade), step(0.00001, _EdgeFade));
                float along = lerp(i.uv.x, i.uv.y, _Along);
                float wave = sin(_Time.y * _Speed * 6.2832 + along * _Freq * 6.2832) * 0.6
                           + sin(_Time.y * _Speed * 3.1 + along * _Freq * 2.7) * 0.4;
                float2 uv = i.uv + float2(wave * _Amp * _Gust * weight, 0);
                fixed4 c = tex2D(_MainTex, uv) * i.color;
                c.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
