Shader "TIEACH/SelectionRimGlow"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.25, 0.95, 1.45, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimIntensity ("Rim Intensity", Range(0.5, 8)) = 2.8
        _Fill ("Fill", Range(0, 0.4)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SelectionRim"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend One One
            ColorMask RGB

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _RimPower;
                float _RimIntensity;
                float _Fill;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 normalOS = normalize(input.normalOS);
                // Push slightly outward to avoid z-fighting with the source mesh.
                float3 posOS = input.positionOS.xyz + normalOS * 0.012;
                float3 posWS = TransformObjectToWorld(posOS);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                // Extra world-space push for large/scaled props.
                posWS += normalize(normalWS) * 0.01;

                output.positionCS = TransformWorldToHClip(posWS);
                output.normalWS = normalWS;
                output.viewWS = GetCameraPositionWS() - posWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                float ndotv = saturate(dot(n, v));
                float rim = pow(saturate(1.0 - ndotv), _RimPower);
                rim = saturate(rim * _RimIntensity + _Fill);
                // Harder edge falloff reduces noisy bloom shimmer.
                rim = smoothstep(0.15, 0.95, rim);

                float3 col = _OutlineColor.rgb * rim;
                return half4(col, rim * _OutlineColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
