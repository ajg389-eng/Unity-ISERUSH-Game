Shader "ISE Rush/Camera Cutaway Triplanar"
{
    Properties
    {
        _BaseMap("Brick Texture", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Tile("World Tile", Float) = 0.2
        _BlendSharpness("Blend Sharpness", Range(1, 8)) = 1
        _CutHeight("Cut Height", Float) = 10000
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "CameraCutaway"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Tile;
                float _BlendSharpness;
                float _CutHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(_CutHeight - input.positionWS.y);

                half3 normalWS = normalize(input.normalWS);
                half3 weights = pow(abs(normalWS), max(1.0, _BlendSharpness));
                weights /= max(weights.x + weights.y + weights.z, 0.0001);

                float tile = max(0.0001, _Tile);
                half4 xSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.positionWS.zy * tile);
                half4 ySample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.positionWS.xz * tile);
                half4 zSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.positionWS.xy * tile);
                half4 surface = (xSample * weights.x + ySample * weights.y + zSample * weights.z) * _BaseColor;

                Light mainLight = GetMainLight();
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 illumination = SampleSH(normalWS)
                    + mainLight.color * diffuse * mainLight.distanceAttenuation;
                surface.rgb *= max(illumination, half3(0.22, 0.22, 0.22));
                surface.rgb = MixFog(surface.rgb, input.fogFactor);
                return half4(surface.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Tile;
                float _BlendSharpness;
                float _CutHeight;
            CBUFFER_END

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                clip(_CutHeight - input.positionWS.y);
                return 0;
            }
            ENDHLSL
        }
    }
}
