// Battlefield terrain: grass on flat ground, dirt where vertex colour red is
// painted (roads, trenches, pits), and rock on steep slopes (cliffs). Ground
// layers use a top-down projection; the rock layer is projected from the sides
// so cliffs never stretch.
Shader "CsAutobattler/TerrainLit"
{
    Properties
    {
        _GrassMap("Grass Albedo", 2D) = "white" {}
        [NoScaleOffset] _GrassNormal("Grass Normal", 2D) = "bump" {}
        [NoScaleOffset] _GrassARM("Grass ARM", 2D) = "white" {}
        _GrassTint("Grass Tint", Color) = (1, 1, 1, 1)
        _GrassTile("Grass Tile (m)", Float) = 6

        _DirtMap("Dirt Albedo", 2D) = "white" {}
        [NoScaleOffset] _DirtNormal("Dirt Normal", 2D) = "bump" {}
        [NoScaleOffset] _DirtARM("Dirt ARM", 2D) = "white" {}
        _DirtTint("Dirt Tint", Color) = (1, 1, 1, 1)
        _DirtTile("Dirt Tile (m)", Float) = 3

        _RockMap("Rock Albedo", 2D) = "white" {}
        [NoScaleOffset] _RockNormal("Rock Normal", 2D) = "bump" {}
        _RockTint("Rock Tint", Color) = (1, 1, 1, 1)
        _RockTile("Rock Tile (m)", Float) = 4

        _SlopeStart("Rock Slope Start (normal.y)", Range(0, 1)) = 0.82
        _SlopeEnd("Rock Slope End (normal.y)", Range(0, 1)) = 0.62
        _BumpScale("Normal Strength", Range(0, 2)) = 1
        _Smoothness("Smoothness Scale", Range(0, 1)) = 0.5
        _MacroVariation("Macro Variation", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _GrassMap_ST;
            float4 _DirtMap_ST;
            float4 _RockMap_ST;
            half4 _GrassTint;
            half4 _DirtTint;
            half4 _RockTint;
            float _GrassTile;
            float _DirtTile;
            float _RockTile;
            half _SlopeStart;
            half _SlopeEnd;
            half _BumpScale;
            half _Smoothness;
            half _MacroVariation;
        CBUFFER_END

        TEXTURE2D(_GrassMap);    SAMPLER(sampler_GrassMap);
        TEXTURE2D(_GrassNormal); SAMPLER(sampler_GrassNormal);
        TEXTURE2D(_GrassARM);    SAMPLER(sampler_GrassARM);
        TEXTURE2D(_DirtMap);     SAMPLER(sampler_DirtMap);
        TEXTURE2D(_DirtNormal);  SAMPLER(sampler_DirtNormal);
        TEXTURE2D(_DirtARM);     SAMPLER(sampler_DirtARM);
        TEXTURE2D(_RockMap);     SAMPLER(sampler_RockMap);
        TEXTURE2D(_RockNormal);  SAMPLER(sampler_RockNormal);
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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // Top-projected normal (texture XY maps to world XZ) into world space.
            float3 TopNormal(half3 tn, float3 n)
            {
                return normalize(float3(tn.x + n.x, abs(tn.z) * n.y, tn.y + n.z));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 p = input.positionWS;

                // --- Ground: grass blended with painted dirt (top projection).
                float2 grassUV = p.xz / _GrassTile;
                float2 dirtUV = p.xz / _DirtTile;
                half dirt = saturate(input.color.r);
                half3 grassAlbedo = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, grassUV).rgb * _GrassTint.rgb;
                half3 dirtAlbedo = SAMPLE_TEXTURE2D(_DirtMap, sampler_DirtMap, dirtUV).rgb * _DirtTint.rgb;
                // Height-aware blend: dirt shows first in the grass texture's dark gaps.
                half grassLum = dot(grassAlbedo, half3(0.33, 0.33, 0.34));
                half dirtWeight = saturate((dirt * 1.6 - 0.3) + (0.5 - grassLum) * 0.6 * dirt);
                half3 groundAlbedo = lerp(grassAlbedo, dirtAlbedo, dirtWeight);
                half3 groundArm = lerp(
                    SAMPLE_TEXTURE2D(_GrassARM, sampler_GrassARM, grassUV).rgb,
                    SAMPLE_TEXTURE2D(_DirtARM, sampler_DirtARM, dirtUV).rgb,
                    dirtWeight);
                half3 groundTN = lerp(
                    UnpackNormalScale(SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassNormal, grassUV), _BumpScale),
                    UnpackNormalScale(SAMPLE_TEXTURE2D(_DirtNormal, sampler_DirtNormal, dirtUV), _BumpScale),
                    dirtWeight);
                float3 groundNormal = TopNormal(groundTN, n);

                // --- Cliffs: rock projected from X and Z.
                float3 w = pow(abs(n), 4.0);
                w.y = 0;
                w /= max(w.x + w.z, 1e-4);
                float2 uvX = p.zy / _RockTile;
                float2 uvZ = p.xy / _RockTile;
                half3 rockAlbedo = (SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvX).rgb * w.x +
                                    SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvZ).rgb * w.z) * _RockTint.rgb;
                half3 tnX = UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvX), _BumpScale);
                half3 tnZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvZ), _BumpScale);
                tnX = half3(tnX.xy + n.zy, abs(tnX.z) * n.x);
                tnZ = half3(tnZ.xy + n.xy, abs(tnZ.z) * n.z);
                float3 rockNormal = normalize(tnX.zyx * w.x + tnZ.xyz * w.z + n * 0.001);

                half rock = smoothstep(_SlopeStart, _SlopeEnd, n.y);
                half3 albedo = lerp(groundAlbedo, rockAlbedo, rock);
                float3 normalWS = normalize(lerp(groundNormal, rockNormal, rock));
                half3 arm = lerp(groundArm, half3(1, 0.8, 0), rock);

                half macro = dot(SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, p.xz * 0.017).rgb, half3(0.33, 0.33, 0.34));
                albedo *= lerp(1.0, 0.6 + macro * 0.8, _MacroVariation);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.metallic = 0;
                surface.smoothness = (1.0 - arm.g) * _Smoothness;
                surface.occlusion = arm.r;
                surface.normalTS = half3(0, 0, 1);

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
                inputData.shadowCoord = TransformWorldToShadowCoord(p);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 ShadowFrag() : SV_Target { return 0; }
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
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings DepthNormalsVert(Attributes input)
            {
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
                return half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
