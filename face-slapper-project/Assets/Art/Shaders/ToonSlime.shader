// FaceSlapper 史莱姆着色器（URP，不透明）：
// - 继承 ToonLit 的赛璐璐分阶光照 + 反向壳描边 + 阴影投射
// - 硬边高光（湿亮果冻质感，非半透明）
// - Fresnel 硬边边缘光（胶感轮廓）
// - 顶点果冻抖动（沿法线多频正弦位移，常开待机动画）
Shader "FaceSlapper/ToonSlime"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.35, 0.9, 0.45, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _RampMap("Toon Ramp", 2D) = "white" {}
        _RampOffset("Ramp Offset", Range(-0.5, 0.5)) = 0
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 1
        _OutlineColor("Outline Color", Color) = (0.02, 0.02, 0.05, 1)
        _OutlineWidth("Outline Width (mm)", Range(0, 10)) = 1.2

        [Header(Slime Specular)]
        _SpecColor("Spec Color", Color) = (1, 1, 1, 1)
        _SpecSize("Spec Size", Range(0.001, 0.5)) = 0.06
        _SpecStrength("Spec Strength", Range(0, 2)) = 1.2

        [Header(Slime Rim)]
        _RimColor("Rim Color", Color) = (0.6, 1, 0.7, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _RimStrength("Rim Strength", Range(0, 2)) = 0.8

        [Header(Slime Wobble)]
        _WobbleAmp("Wobble Amplitude", Range(0, 0.1)) = 0.02
        _WobbleFreq("Wobble Frequency", Range(0, 20)) = 6
        _WobbleSpeed("Wobble Speed", Range(0, 20)) = 5

        [Header(Slime Squash)]
        _SquashMax("Squash Max", Range(0, 1)) = 0.45
        // _SquashDir / _SquashAmount 由 SlimeSquashDriver 经 MaterialPropertyBlock 写入，
        // 不作为序列化属性暴露。
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // ---------------- 卡通光照 + 高光 + 边缘光 + 果冻抖动 ----------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex ToonVert
            #pragma fragment ToonFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_RampMap);
            SAMPLER(sampler_RampMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _RampOffset;
                float _AmbientStrength;
                half4 _SpecColor;
                float _SpecSize;
                float _SpecStrength;
                half4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _WobbleAmp;
                float _WobbleFreq;
                float _WobbleSpeed;
                float _SquashMax;
                float _SquashAmount;
                float3 _SquashDir;
            CBUFFER_END

            /// 果冻抖动：双频正弦叠加，沿法线位移（主频 + 快而弱的扰动频）。
            float3 ApplyWobble(float3 positionOS, float3 normalOS)
            {
                float phase = (positionOS.x + positionOS.y + positionOS.z) * _WobbleFreq;
                float w = sin(_Time.y * _WobbleSpeed + phase) * 0.7
                        + sin(_Time.y * _WobbleSpeed * 1.7 + positionOS.z * _WobbleFreq * 2.3) * 0.3;
                return positionOS + normalOS * (w * _WobbleAmp);
            }

            /// 速度联动的挤压拉伸：沿运动轴拉伸（SquashAmount>0）或压扁（<0），
            /// 垂直轴反向缩放保持体积。
            float3 ApplySquash(float3 positionOS)
            {
                float stretch = 1.0 + _SquashAmount * _SquashMax;
                float perpScale = rsqrt(max(stretch, 0.05));
                float along = dot(positionOS, _SquashDir);
                float3 parallel = _SquashDir * along;
                return parallel * stretch + (positionOS - parallel) * perpScale;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings ToonVert(Attributes input)
            {
                Varyings output;
                float3 positionOS = ApplySquash(ApplyWobble(input.positionOS.xyz, input.normalOS));
                output.positionWS = TransformObjectToWorld(positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 ToonFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light light = GetMainLight(shadowCoord);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;

                // 半兰伯特 × 阴影衰减 → Ramp 采样，形成硬边分阶。
                half halfLambert = dot(normalWS, light.direction) * 0.5 + 0.5;
                half rampU = saturate(halfLambert * light.shadowAttenuation * light.distanceAttenuation + _RampOffset);
                half3 ramp = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, float2(rampU, 0.5)).rgb;

                half3 ambient = SampleSH(normalWS) * _AmbientStrength;
                half3 color = albedo * (light.color * ramp + ambient);

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // 硬边高光（Blinn-Phong 阶梯化）：湿亮果冻质感。
                float3 halfDir = normalize(light.direction + viewDirWS);
                float ndh = saturate(dot(normalWS, halfDir));
                float spec = smoothstep(1.0 - _SpecSize - 0.03, 1.0 - _SpecSize, ndh);
                color += _SpecColor.rgb * (_SpecStrength * spec) * light.color.rgb;

                // Fresnel 硬边边缘光：胶感轮廓。
                float rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower);
                rim = smoothstep(0.35, 0.75, rim);
                color += _RimColor.rgb * (rim * _RimStrength);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // ---------------- 反向壳描边（同步果冻抖动） ----------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _RampOffset;
                float _AmbientStrength;
                half4 _SpecColor;
                float _SpecSize;
                float _SpecStrength;
                half4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _WobbleAmp;
                float _WobbleFreq;
                float _WobbleSpeed;
                float _SquashMax;
                float _SquashAmount;
                float3 _SquashDir;
            CBUFFER_END

            float3 ApplyWobble(float3 positionOS, float3 normalOS)
            {
                float phase = (positionOS.x + positionOS.y + positionOS.z) * _WobbleFreq;
                float w = sin(_Time.y * _WobbleSpeed + phase) * 0.7
                        + sin(_Time.y * _WobbleSpeed * 1.7 + positionOS.z * _WobbleFreq * 2.3) * 0.3;
                return positionOS + normalOS * (w * _WobbleAmp);
            }

            float3 ApplySquash(float3 positionOS)
            {
                float stretch = 1.0 + _SquashAmount * _SquashMax;
                float perpScale = rsqrt(max(stretch, 0.05));
                float along = dot(positionOS, _SquashDir);
                float3 parallel = _SquashDir * along;
                return parallel * stretch + (positionOS - parallel) * perpScale;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings OutlineVert(Attributes input)
            {
                Varyings output;
                float3 normalOS = input.normalOS;
                float3 positionOS = ApplySquash(ApplyWobble(input.positionOS.xyz, normalOS));
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                float3 positionWS = TransformObjectToWorld(positionOS);
                positionWS += normalize(normalWS) * (_OutlineWidth * 0.001);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ---------------- 阴影投射 ----------------
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Shadows.hlsl 内部用到 LerpWhiteTo（定义于核心库 CommonMaterial.hlsl），
            // 官方 Lit 经 SurfaceInput 链间接包含，这里显式补上。
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
