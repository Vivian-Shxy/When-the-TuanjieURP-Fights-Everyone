Shader "FullScreen/VolumetricFogURP"
{
    // ============================================================
    // 从 VolumetricFogHDRP.shader 改写为 URP 版本
    // 适配 Tuanjie / Unity 2022 LTS + URP 14+
    // 使用 Full Screen Pass Renderer Feature 调用
    // 兼容 VR Single Pass Instanced (Vive Focus 3)
    // ============================================================

    Properties
    {
        _Color("Fog Color (灰绿色)", Color) = (0.48, 0.55, 0.48, 1)
        _MaxDistance("Max Distance", Float) = 30
        _StepSize("Step Size (VR推荐≥1.5)", Range(0.1, 5)) = 1.5
        _DensityMultiplier("Density Multiplier", Range(0, 10)) = 4.0
        _NoiseOffset("Noise Offset", Float) = 1.5

        _FogNoise("Fog Noise (3D)", 3D) = "white" {}
        _NoiseTiling("Noise Tiling", Float) = 4
        _DensityThreshold("Density Threshold", Range(0, 1)) = 0.4
        _ErosionStrength("Erosion Strength", Range(0, 1)) = 0.3

        // 高度衰减：上方浓、下方淡（适合"天光从穹顶透下来"）
        _HeightTop      ("Height Top (m)", Float) = 8.0
        _HeightBottom   ("Height Bottom (m)", Float) = 0.0
        _HeightFalloff  ("Height Falloff", Range(0.5, 4)) = 1.5

        [HDR]_LightContribution("Light Contribution (丁达尔强度)", Color) = (2, 2, 2, 1)
        _LightScattering("Light Scattering (丁达尔聚拢度)", Range(-1, 1)) = 0.7

        // VR 性能保护：硬上限步数
        _MaxSteps("Max Steps (VR推荐24以下)", Range(8, 128)) = 24
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "VolumetricFog"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            // VR 关键：开启实例化支持
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // ----- 参数 -----
            float4 _Color;
            float  _MaxDistance;
            float  _StepSize;
            float  _DensityMultiplier;
            float  _NoiseOffset;

            TEXTURE3D(_FogNoise);
            SAMPLER(sampler_FogNoise);
            float  _NoiseTiling;
            float  _DensityThreshold;
            float  _ErosionStrength;

            float  _HeightTop;
            float  _HeightBottom;
            float  _HeightFalloff;

            float4 _LightContribution;
            float  _LightScattering;

            int    _MaxSteps;

            // ----- Henyey-Greenstein 相函数（丁达尔效应核心）-----
            // g 越接近 1：光越聚拢成柱，正向散射明显
            // g = 0：均匀散射
            // g 负值：背向散射
            float HenyeyGreenstein(float cosAngle, float g)
            {
                float g2    = g * g;
                float denom = 1.0 + g2 - 2.0 * g * cosAngle;
                return (1.0 - g2) / (4.0 * PI * pow(max(denom, 1e-4), 1.5));
            }

            // ----- 密度场：3D 噪声 + 高度衰减 -----
            float GetDensity(float3 worldPos)
            {
                float4 noise = SAMPLE_TEXTURE3D_LOD(
                    _FogNoise, sampler_FogNoise,
                    worldPos * 0.01 * _NoiseTiling, 0);

                float baseShape = noise.r;
                float detail    = noise.g * 0.5 + noise.b * 0.3 + noise.a * 0.2;

                // 边缘侵蚀：让雾形状破碎，更像云絮
                float n = saturate(baseShape - (1.0 - detail) * _ErosionStrength);

                // 阈值化 + 强度
                float density = saturate(n - _DensityThreshold) * _DensityMultiplier;

                // 高度衰减
                float h = saturate((worldPos.y - _HeightBottom)
                                   / max(_HeightTop - _HeightBottom, 0.01));
                density *= pow(h, _HeightFalloff);

                return density;
            }

            // ----- IGN 抖动：抹平 banding -----
            float IGN(float2 pixCoord, int frameCount)
            {
                pixCoord += float(frameCount % 64) * 5.588238f;
                return frac(52.9829189f *
                            frac(0.06711056f * pixCoord.x +
                                 0.00583715f * pixCoord.y));
            }

            // ============================================================
            // Frag：每像素发一条射线，沿射线 raymarch
            // ============================================================
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // 1. 深度 → 世界坐标
                float depth = SampleSceneDepth(uv);
                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                // 2. 原画面颜色（_BlitTexture 由 Full Screen Pass 自动绑定）
                float4 sceneCol = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // 3. 射线建立
                float3 entryPoint = _WorldSpaceCameraPos.xyz;
                float3 viewVec    = worldPos - entryPoint;
                float  viewLen    = length(viewVec);
                float3 rayDir     = viewVec / max(viewLen, 1e-4);
                float  distLimit  = min(viewLen, _MaxDistance);

                // 4. 起点抖动，避免条带
                float frameSeed = _Time.y * 60.0;
                float jitter    = IGN(input.positionCS.xy, (int)frameSeed);
                float distTravelled = jitter * _NoiseOffset;

                // 5. 主光（URP 的 GetMainLight 替代 HDRP 的 _DirectionalLightDatas[0]）
                Light mainLight  = GetMainLight();
                float3 L         = mainLight.direction;
                float3 lightColor = mainLight.color;

                // 6. 相函数（决定丁达尔效应）
                float phase = HenyeyGreenstein(dot(rayDir, L), _LightScattering);

                // 7. Raymarch 累积
                float  transmittance = 1.0;
                float3 fogCol        = _Color.rgb;

                int stepCount = 0;
                UNITY_LOOP
                while (distTravelled < distLimit && stepCount < _MaxSteps)
                {
                    float3 rayPos  = entryPoint + rayDir * distTravelled;
                    float  density = GetDensity(rayPos);

                    if (density > 0)
                    {
                        // 注：此版本不采样阴影。如需主光阴影遮挡：
                        //   float4 shadowCoord = TransformWorldToShadowCoord(rayPos);
                        //   float shadow = MainLightRealtimeShadow(shadowCoord);
                        // 性能开销很大，VR 上慎用。
                        float shadow = 1.0;

                        fogCol += lightColor * _LightContribution.rgb
                                  * phase * density * shadow * _StepSize;

                        transmittance *= exp(-density * _StepSize);

                        if (transmittance < 0.01) break;
                    }

                    distTravelled += _StepSize;
                    stepCount++;
                }

                // 8. Beer's Law 混合
                float3 finalRGB = lerp(sceneCol.rgb, fogCol, 1.0 - saturate(transmittance));
                return half4(finalRGB, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
