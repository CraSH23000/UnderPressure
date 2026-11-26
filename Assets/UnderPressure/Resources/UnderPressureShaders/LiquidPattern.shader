// Patterned liquid for GameConfig.liquids entries whose 'pattern' isn't Solid (see LiquidType / Mats.LiquidMat).
// Lit like URP Lit (PBR, shadows, Forward+ lights, fog), but the albedo and glow come from a world-space pattern of
// two colors instead of one flat color, so it lines up across pipe joints:
//   _Pattern 0 = BANDS  : rings travelling outward from the spout (_UPLiquidOrigin), like a lava lamp poured into a hose
//   _Pattern 1 = MARBLE : two colors swirled together that churn and never mix
// _Fill lerps from the empty pipe (_PipeColor, no glow) to the pattern, matching the stepped fill materials.
// Lives in Resources so it ships in player builds.
Shader "UnderPressure/LiquidPattern"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _Color2 ("Color 2", Color) = (0,0,0,1)
        [HDR] _EmissionColor ("Glow", Color) = (0,0,0,1)
        [HDR] _Emission2 ("Glow 2", Color) = (0,0,0,1)
        _PipeColor ("Empty Pipe Color", Color) = (0.93,0.94,0.96,1)
        _Fill ("Fill (color)", Range(0,1)) = 1
        _FillGlow ("Fill (glow)", Range(0,1)) = 1
        _Pattern ("Pattern (0 bands, 1 marble)", Float) = 0
        _Scale ("Pattern Scale", Float) = 1
        _Speed ("Pattern Speed", Float) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.85
        _Metallic ("Metallic", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

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
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _Color2;
                half4 _EmissionColor;
                half4 _Emission2;
                half4 _PipeColor;
                half _Fill;
                half _FillGlow;
                half _Pattern;
                half _Scale;
                half _Speed;
                half _Smoothness;
                half _Metallic;
            CBUFFER_END

            float4 _UPLiquidOrigin; // world position of the spout, set by Mats each frame
            // per pipe segment (PipePiece.SetChannelPath, via a property block): its two ends in world space, with
            // w = how far along the flow (from the spout) each end is. Zero = not on a path: bands ripple out radially.
            float4 _PathA;
            float4 _PathB;

            // distance along the flow at this point: projected onto the segment the pixel belongs to
            float FlowDistance(float3 posWS)
            {
                float3 ab = _PathB.xyz - _PathA.xyz;
                float len2 = dot(ab, ab);
                if (len2 < 1e-6) return length(posWS.xz - _UPLiquidOrigin.xz);
                float k = saturate(dot(posWS - _PathA.xyz, ab) / len2);
                return lerp(_PathA.w, _PathB.w, k);
            }

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
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // --- small value noise ---
            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0,0,0)), Hash(i + float3(1,0,0)), f.x),
                                 lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                                 lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * Noise(p); p = p * 2.03 + 11.7; a *= 0.5; }
                return v;
            }

            // 0..1: how much of color 2 shows here
            float PatternMask(float3 posWS)
            {
                float t = _Time.y * _Speed;
                float s = max(0.05, _Scale);
                if (_Pattern < 0.5)
                {
                    // BANDS: stripes flowing down the pipe (or rippling out from the spout off the pipes), wobbled a
                    // little so they read as liquid, not a ruler
                    float d = FlowDistance(posWS);
                    float wobble = (Noise(posWS * 0.9 / s + float3(0, t * 0.3, 0)) - 0.5) * 0.6;
                    float w = d / (1.4 * s) - t * 0.8 + wobble;
                    float tri = abs(frac(w) - 0.5) * 2.0;          // 0..1 triangle wave
                    return smoothstep(0.38, 0.62, tri);             // soft-edged stripes
                }
                // MARBLE: domain-warped noise, slowly churning; a sharpened ridge gives thin veins
                float3 p = posWS / (1.2 * s);
                float3 q = float3(Fbm(p + float3(0, 0, t * 0.15)), Fbm(p + float3(5.2, 1.3, -t * 0.12)), Fbm(p + float3(-3.1, 7.7, t * 0.1)));
                float n = Fbm(p + 2.2 * q + float3(t * 0.05, 0, 0));
                float swirl = sin(n * 12.0 + t * 0.4) * 0.5 + 0.5;
                return smoothstep(0.35, 0.65, swirl);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs vp = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = vp.positionCS;
                o.positionWS = vp.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(vp.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float m = PatternMask(input.positionWS);
                half3 liquid = lerp(_BaseColor.rgb, _Color2.rgb, m);
                half3 glow = lerp(_EmissionColor.rgb, _Emission2.rgb, m);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = lerp(_PipeColor.rgb, liquid, _Fill);
                s.metallic = _Metallic;
                s.smoothness = _Smoothness;
                s.occlusion = 1;
                s.emission = glow * _FillGlow;
                s.alpha = 1;

                half4 c = UniversalFragmentPBR(inputData, s);
                c.rgb = MixFog(c.rgb, inputData.fogCoord);
                c.a = 1;
                return c;
            }
            ENDHLSL
        }

        // shadows and depth (SSAO etc.) borrowed from URP Lit: the shape is all that matters there
        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Universal Render Pipeline/Lit"
}
