// 그림 표면 물결: 그림 위에 겹쳐 그리는 투명 판.
// 중심(_Center)에서 원형 파동이 퍼지며(_Progress 0→1) 그 고리 부분만 그림을 일렁이게 비틀어 다시 그리고, 은은하게 빛낸다.
Shader "Gallery/PaintingRipple"
{
    Properties
    {
        [MainTexture] _BaseMap ("Painting", 2D) = "white" {}
        _Center ("Center (UV)", Vector) = (0.5, 0.4, 0, 0)
        _Aspect ("Width / Height", Float) = 1
        _Progress ("Progress", Range(0, 1)) = 0
        _MaxRadius ("Max Radius", Float) = 0.9
        _Width ("Ring Width", Float) = 0.07
        _Amplitude ("Distortion", Float) = 0.015
        _Frequency ("Wave Frequency", Float) = 60
        _GlowColor ("Glow Color", Color) = (1, 0.95, 0.8, 1)
        _Glow ("Glow", Range(0, 1)) = 0.15
        _Brightness ("Painting Brightness", Range(0, 1.5)) = 0.8
        _Intensity ("Intensity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Center;
                float _Aspect, _Progress, _MaxRadius, _Width, _Amplitude, _Frequency, _Intensity, _Glow, _Brightness;
                half4 _GlowColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);   // VR 싱글 패스
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 d = i.uv - _Center.xy;
                d.x *= _Aspect;                       // 세로로 긴 그림에서도 동그란 파동
                float dist = length(d);
                float radius = _Progress * _MaxRadius;
                float ring = exp(-pow((dist - radius) / _Width, 2));
                float fade = (1 - _Progress) * _Intensity;

                float2 dir = dist > 1e-4 ? d / dist : 0;
                dir.x /= _Aspect;
                float2 uv = i.uv + dir * sin((dist - radius) * _Frequency) * _Amplitude * ring;

                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv * _BaseMap_ST.xy + _BaseMap_ST.zw);
                col.rgb *= _Brightness;                         // 조명 받은 원래 그림 밝기에 맞춤
                col.rgb += _GlowColor.rgb * ring * _Glow;       // 고리 부분만 은은하게
                col.a = saturate(ring * fade);
                return col;
            }
            ENDHLSL
        }
    }
}
