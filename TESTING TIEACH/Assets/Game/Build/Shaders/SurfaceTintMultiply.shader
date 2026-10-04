Shader "ISE Rush/Surface Tint Multiply"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _Tint("Tint", Color) = (1,1,1,1)
        _Tiling("World Tiling", Float) = 0.5
        [HideInInspector] _TextureMode("Texture Mode", Float) = 0
        [HideInInspector] _SrcBlend("Source Blend", Float) = 2
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("ZWrite", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" "RenderType"="Opaque" }
        Pass
        {
            Name "SurfaceTint"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull Back
            // This mesh exactly matches its source renderer. A depth offset exposes the
            // hidden caps between stacked wall pieces as bright horizontal bands.
            Offset 0, 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Tiling;
                float _TextureMode;
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
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_TextureMode < 0.5)
                    return half4(_Tint.rgb, 1.0h);

                half3 normalWS = normalize(input.normalWS);
                half3 weights = pow(abs(normalWS), 8.0h);
                weights /= max(weights.x + weights.y + weights.z, 0.0001h);

                float scale = max(_Tiling, 0.001);
                half3 sampleX = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.positionWS.zy * scale).rgb;
                half3 sampleY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.positionWS.xz * scale).rgb;
                half3 sampleZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.positionWS.xy * scale).rgb;
                half3 albedo = (sampleX * weights.x + sampleY * weights.y + sampleZ * weights.z) * _Tint.rgb;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = SampleSH(normalWS) + mainLight.color * diffuse * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                return half4(albedo * max(lighting, 0.18h), 1.0h);
            }
            ENDHLSL
        }
    }
}
