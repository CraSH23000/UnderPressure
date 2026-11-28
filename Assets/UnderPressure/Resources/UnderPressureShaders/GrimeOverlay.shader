// Full-screen grime film (GrimeOverlay.cs draws it on a screen-space canvas under the HUD).
// Each pixel is projected onto the floor plane, so the film and its rings stick to the world when the camera moves.
//   film = settled grime level, plus each active ring's change inside its radius, plus a transient exaggeration
//   that trails the ring's wavefront (stronger than what's applied for good, then easing back to it).
// Clean rings (delta < 0) wash the film out with a bright edge; grime rings (delta > 0) roll in murk with a dark edge.
Shader "UnderPressure/GrimeOverlay"
{
    Properties
    {
        [HideInInspector] _MainTex ("Unused", 2D) = "white" {}
        _FilmColor ("Film Color", Color) = (0.32, 0.36, 0.16, 1)
        _MurkColor ("Murk Color", Color) = (0.12, 0.10, 0.05, 1)
        _CleanGlow ("Clean Edge Glow", Color) = (0.75, 1.0, 1.0, 1)
        _MaxAlpha ("Film Alpha at Full Grime", Range(0, 1)) = 0.3
        _Smudge ("Smudge Amount", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha // premultiplied: the film covers, the glow adds

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_RINGS 8

            fixed4 _FilmColor, _MurkColor, _CleanGlow;
            float _MaxAlpha, _Smudge;
            float _Level;              // settled grime, 0..1
            float4x4 _InvViewProj;     // the game camera's inverse view-projection (not the UI's)
            float _PlaneY;
            int _RingCount;
            float4 _RingA[MAX_RINGS];  // xy = centre (world xz), z = radius, w = edge width
            float4 _RingB[MAX_RINGS];  // x = level change (settles), y = exaggeration, z = trail length, w = fade (1 -> 0)

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            // where this screen pixel's ray meets the floor plane (world xz)
            float2 FloorPoint(float2 uv)
            {
                float2 ndc = uv * 2 - 1;
                float4 a = mul(_InvViewProj, float4(ndc, -1, 1));
                float4 b = mul(_InvViewProj, float4(ndc, 1, 1));
                float3 p0 = a.xyz / a.w, p1 = b.xyz / b.w;
                float3 d = p1 - p0;
                float t = abs(d.y) > 1e-5 ? (_PlaneY - p0.y) / d.y : 0;
                return (p0 + d * max(t, 0)).xz;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 w = FloorPoint(i.uv);
                float level = _Level;
                float clean = 0, murk = 0;
                for (int k = 0; k < MAX_RINGS; k++)
                {
                    if (k >= _RingCount) break;
                    float4 A = _RingA[k], B = _RingB[k];
                    float dist = length(w - A.xy);
                    float behind = A.z - dist;                       // > 0: the wave has passed here
                    float inside = smoothstep(-A.w, A.w, behind);     // soft front
                    level += B.x * inside;
                    // exaggeration: strongest right behind the front, easing back to the settled change
                    float trail = inside * exp(-max(behind, 0) / max(B.z, 0.01)) * B.w;
                    level += sign(B.x) * B.y * trail;
                    float edge = exp(-pow(behind / max(A.w, 0.01), 2)) * B.w; // the wavefront itself
                    if (B.x < 0) clean += edge; else murk += edge;
                }
                level = saturate(level);

                // smudgy, uneven film rather than a flat tint
                float n = Noise(w * 0.35 + _Time.y * 0.02) * 0.6 + Noise(w * 1.3 - _Time.y * 0.03) * 0.4;
                float filmA = level * _MaxAlpha * lerp(1, 0.4 + n * 1.2, _Smudge);
                filmA = saturate(filmA + murk * 0.35);
                float3 filmCol = lerp(_FilmColor.rgb, _MurkColor.rgb, saturate(level * 0.6 + murk));

                float3 rgb = filmCol * filmA + _CleanGlow.rgb * saturate(clean) * 0.35;
                return fixed4(rgb, filmA);
            }
            ENDCG
        }
    }
}
