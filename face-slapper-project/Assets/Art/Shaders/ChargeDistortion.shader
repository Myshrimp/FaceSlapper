// FaceSlapper 蓄力空间扭曲着色器（URP）：
// 采样 _CameraOpaqueTexture，沿球面法线方向偏移屏幕 UV 形成折射扭曲，
// 附加旋涡旋转制造"能量搅动"感；轮廓处偏移归零避免边缘采样跳动。
// 依赖 URP 资产开启 Opaque Texture（m_RequireOpaqueTexture）。
Shader "FaceSlapper/ChargeDistortion"
{
    Properties
    {
        _Distortion("扭曲强度", Range(0, 0.4)) = 0.1
        _Swirl("旋涡角", Range(0, 6.283)) = 1.57
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ChargeDistortion"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend Off
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Distortion;
                float _Swirl;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalVS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalVS = TransformWorldToViewNormal(TransformObjectToWorldNormal(input.normalOS));
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / input.screenPos.w;

                // 朝向因子：球心（法线正对相机）最强，轮廓处归零。
                float facing = saturate(-normalize(input.normalVS).z);

                // 法线 XY 方向旋转一个旋涡角，制造搅动感的折射偏移。
                float2 dir = normalize(input.normalVS).xy;
                float cs = cos(_Swirl);
                float sn = sin(_Swirl);
                dir = float2(dir.x * cs - dir.y * sn, dir.x * sn + dir.y * cs);

                float2 uv = screenUV + dir * (_Distortion * facing);
                half3 col = SampleSceneColor(uv);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
