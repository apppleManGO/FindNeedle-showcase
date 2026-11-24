Shader "FindNeedle/SandTracks"
{
    Properties { _MainTex ("Search tracks", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        struct Attributes { float3 pos : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
        struct Varyings
        {
            float4 position : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 world : TEXCOORD1;
            half damp : TEXCOORD2;
        };
        Varyings vert(Attributes v)
        {
            Varyings o;
            o.world = TransformObjectToWorld(v.pos);
            o.position = TransformWorldToHClip(o.world);
            o.uv = v.uv;
            o.damp = v.color.r;
            return o;
        }
        // 1 on sand nobody has touched, fading to 0 on worked or damp sand and at the patch border.
        half Untouched(Varyings i, half mark)
        {
            float2 border = min(i.uv, 1 - i.uv);
            half inside = smoothstep(0, .05, min(border.x, border.y));
            return inside * (1 - saturate(i.damp * 3)) * (1 - saturate(mark * 2.5));
        }
        ENDHLSL

        // Darkening: search marks, damp sand, wind ripples.
        Pass
        {
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                half mark = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                half3 shade = 1 - mark * half3(.56, .62, .68);
                shade *= lerp(half3(1, 1, 1), half3(.60, .53, .46), saturate(i.damp));
                float range = distance(_WorldSpaceCameraPos, i.world);
                float wave = sin(i.world.z * 38 + sin(i.world.x * 5.3) * 1.7 + sin(i.world.x * 1.9 + i.world.z * .7) * 2.1);
                shade *= 1 - .11 * Untouched(i, mark) * smoothstep(.1, 1, wave) * saturate(1.3 - range / 8);
                return half4(shade, 1);
            }
            ENDHLSL
        }

        // Glints: a few dry grains catch the sun and flicker as the view moves.
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                half mark = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                float2 cell = floor(i.world.xz * 240);
                float grain = frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float flicker = sin((grain * 40 + dot(view, float3(9, 7, 11))) * 6.2831853);
                float range = distance(_WorldSpaceCameraPos, i.world);
                half glint = step(.988, grain) * pow(saturate(flicker), 4) * saturate(1.5 - range / 3.5);
                return half4(half3(.85, .78, .62) * glint * Untouched(i, mark), 1);
            }
            ENDHLSL
        }
    }
}
