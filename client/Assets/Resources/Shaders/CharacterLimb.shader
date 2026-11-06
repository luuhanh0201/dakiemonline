Shader "DaKiem/CharacterLimb"
{
    // Used for every character part. Shading runs from the back and bottom of each part to its
    // front and top, which gives the flat drawings some volume.
    Properties
    {
        _MainTex ("Part", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Dark ("Shaded side", Float) = 1
        _Light ("Lit side", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            struct Input
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct Output
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Dark;
            float _Light;

            Output Vertex(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            fixed4 Fragment(Output input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv) * input.color * _Color;
                float light = saturate(input.uv.x * 0.6 + input.uv.y * 0.4);

                color.rgb *= lerp(_Dark, _Light, light);
                return color;
            }
            ENDCG
        }
    }
}
