// World-space triplanar PBR for the blockout map geometry. Stretched cubes keep
// a consistent texel density on every face, and a low-frequency macro pass
// breaks up visible tiling on large floors.
Shader "CsAutobattler/TriplanarLit"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _BumpMap("Normal (OpenGL)", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0, 2)) = 1
        [NoScaleOffset] _ARMMap("AO (R) Rough (G) Metal (B)", 2D) = "white" {}
        _TileSize("World Tile Size (m)", Float) = 3
        _Smoothness("Smoothness Scale", Range(0, 1)) = 0.8
        _OcclusionStrength("AO Strength", Range(0, 1)) = 1
        _MacroVariation("Macro Variation", Range(0, 1)) = 0.35
        _GroundFade("Wall Base Darkening", Range(0, 1)) = 0.35
        _CapTint("Top Cap Colour (alpha = strength)", Color) = (1, 1, 1, 0)
        _CapHeight("Top Cap Min Height", Float) = 1
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            float _TileSize;
            half _Smoothness;
            half _OcclusionStrength;
            half _MacroVariation;
            half _GroundFade;
            half4 _CapTint;
            float _CapHeight;
            half4 _EmissionColor;
        CBUFFER_END

        TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);    SAMPLER(sampler_BumpMap);
        TEXTURE2D(_ARMMap);     SAMPLER(sampler_ARMMap);

        float3 TriplanarWeights(float3 normalWS)
        {
            float3 w = pow(abs(normalWS), 6.0);
            return w / max(dot(w, 1.0), 1e-4);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float3 blend = TriplanarWeights(n);
                float3 p = input.positionWS / max(_TileSize, 0.01);
                float2 uvX = p.zy;
                float2 uvY = p.xz;
                float2 uvZ = p.xy;

                half3 albedo =
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * blend.x +
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * blend.y +
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * blend.z;

                // Low-frequency luminance variation hides tiling on big surfaces.
                half macro = dot(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.positionWS.xz * 0.021).rgb, half3(0.33, 0.33, 0.34));
                albedo *= lerp(1.0, 0.55 + macro * 0.9, _MacroVariation);
                half detail = dot(albedo, half3(0.33, 0.33, 0.34));
                albedo *= _BaseColor.rgb;
                // Raised horizontal faces (wall tops) get their own flat plaster
                // colour, keeping only a hint of texture, so the layout reads
                // clearly from the overhead camera.
                half cap = smoothstep(0.6, 0.9, blend.y) * step(_CapHeight, input.positionWS.y) * _CapTint.a;
                half3 capAlbedo = _CapTint.rgb * (0.82 + 0.45 * detail);
                albedo = lerp(albedo, capAlbedo, cap);

                half3 arm =
                    SAMPLE_TEXTURE2D(_ARMMap, sampler_ARMMap, uvX).rgb * blend.x +
                    SAMPLE_TEXTURE2D(_ARMMap, sampler_ARMMap, uvY).rgb * blend.y +
                    SAMPLE_TEXTURE2D(_ARMMap, sampler_ARMMap, uvZ).rgb * blend.z;

                // Whiteout-blended triplanar normal mapping.
                half3 tnX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _BumpScale);
                half3 tnY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), _BumpScale);
                half3 tnZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _BumpScale);
                half3 axisSign = n < 0 ? -1 : 1;
                tnX = half3(tnX.xy + n.zy, abs(tnX.z) * n.x);
                tnY = half3(tnY.xy + n.xz, abs(tnY.z) * n.y);
                tnZ = half3(tnZ.xy + n.xy, abs(tnZ.z) * n.z);
                tnX.z *= axisSign.x;
                tnY.z *= axisSign.y;
                tnZ.z *= axisSign.z;
                float3 normalWS = normalize(tnX.zyx * blend.x + tnY.xzy * blend.y + tnZ.xyz * blend.z);

                // Vertical faces darken toward their base: cheap contact grounding.
                half baseFade = saturate(input.positionWS.y / 1.6);
                half verticalness = 1.0 - blend.y;
                half groundOcclusion = lerp(1.0, lerp(1.0 - _GroundFade, 1.0, baseFade), verticalness);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.metallic = arm.b;
                surface.specular = 0;
                surface.smoothness = (1.0 - arm.g) * _Smoothness;
                surface.occlusion = lerp(1.0, arm.r, _OcclusionStrength) * groundOcclusion;
                surface.emission = _EmissionColor.rgb;
                surface.normalTS = half3(0, 0, 1);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
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

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                return ApplyShadowClamping(positionCS);
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

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
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
