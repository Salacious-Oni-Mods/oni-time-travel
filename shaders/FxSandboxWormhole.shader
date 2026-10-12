// WORMHOLE -- the time machine's transition screen (FxSandbox 0.6.5, fx/TIME-MACHINE-DESIGN.md).
// A full-screen pass on ONI's world view (Klei's Overlay camera, through OniFramework.ScreenEffects),
// so the HUD is never covered. Around ONE point (the machine's glass) an iris opens; inside it is a
// tunnel of blue and cyan streaks rushing toward a white core, the colours of the machine's bolts.
// At the far end the tunnel flares white and the iris closes back into the glass.
//
// The consumer drives everything through the point (FxSandbox.WormholePass):
//   A.w  Strength     how far the iris is open: 0 shut, 1 at the point's radius (which the pass sets
//                     past the screen's far corner, so 1 covers the whole view)
//   B.x  Phase        the streaks' clock, seconds (the sim clock, so a paused game freezes them)
//   B.y  ColourShift  the white flare, 0..1
//   B.z  Shimmer      the tunnel's twist, 0..1
//   C               rgb the streak tint, a its amount
// Only the first point is drawn: there is one wormhole on screen at a time.
//
// Pure visual: no state, no RNG -- streak lanes are a hash of their index.
//
// FX_CHEAP: the low-end variant. One texture sample instead of two, no pull on the world around the
// iris and no world swirled into the tunnel's mouth; the iris, the streaks, the core and the flare
// stay, so it reads the same.
Shader "FxSandbox/Wormhole"
{
    Properties
    {
        _MainTex ("Frame so far (bound by Graphics.Blit)", 2D) = "white" {}
        _Lanes ("Streak lanes around the tunnel (whole number)", Float) = 48
        _StreakSpeed ("Streak travel, depth units per second", Float) = 1.4
        _StreakLength ("Streak length, fraction of a depth unit", Range(0.05, 0.9)) = 0.35
        _Twist ("Turns of twist per depth unit at full twist", Float) = 0.12
        _RimWidth ("Iris rim glow width, fraction of the iris radius", Range(0.005, 0.3)) = 0.06
        _Pull ("World drawn toward the iris, fraction of the iris radius", Range(0, 1)) = 0.35
        _DeepColour ("Tunnel wall at the mouth", Color) = (0.02, 0.04, 0.14, 1)
        _CoreColour ("The far end", Color) = (0.9, 0.97, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "PreviewType" = "Plane" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local __ FX_CHEAP
            #include "UnityCG.cginc"

            #define FX_MAX_POINTS 8
            #define FX_TAU 6.2831853

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            // Written every frame by OniFramework.ScreenEffects.
            float _FxAspect;
            float _FxPointCount;
            float4 _FxPointA[FX_MAX_POINTS];  // xy centre (viewport UV), z radius (viewport heights), w open
            float4 _FxPointB[FX_MAX_POINTS];  // x phase, y flare, z twist, w unused
            float4 _FxPointC[FX_MAX_POINTS];  // rgb tint, a tint amount

            float _Lanes;
            float _StreakSpeed;
            float _StreakLength;
            float _Twist;
            float _RimWidth;
            float _Pull;
            float4 _DeepColour;
            float4 _CoreColour;

            // Stateless hash: 1D -> [0, 1).
            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                fixed3 src = tex2D(_MainTex, uv).rgb;
                if (_FxPointCount < 0.5)
                {
                    return fixed4(src, 1.0);
                }
                float4 A = _FxPointA[0];
                float4 B = _FxPointB[0];
                float4 C = _FxPointC[0];
                float open = saturate(A.w);
                float flare = saturate(B.y);
                if (open <= 0.0 && flare <= 0.0)
                {
                    return fixed4(src, 1.0);
                }

                // vp: this pixel in viewport space, as in FxSandboxSpacetimeWarp.shader.
                float2 vp = uv;
                float flip = 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0.0)
                {
                    vp.y = 1.0 - vp.y;
                    flip = -1.0;
                }
                #endif
                float pixel = abs(_MainTex_TexelSize.y);

                float R = max(A.z, 1e-4);
                float iris = max(R * open, 1e-5);
                float2 d = vp - A.xy;
                d.x *= _FxAspect;
                float r = length(d);
                float2 dir = d / max(r, 1e-5);
                float edge = r - iris;                       // < 0 inside the iris
                float3 tint = lerp(float3(0.35, 0.8, 1.0), C.rgb, saturate(C.a));

                // OUTSIDE: the world leans toward the hole and dims near it.
                float near = exp(-max(edge, 0.0) / max(iris * 0.6, 4.0 * pixel));
                fixed3 world = src;
                #if !defined(FX_CHEAP)
                float2 pull = dir * (_Pull * iris * near);   // sample from further out: drawn inward
                pull.x /= _FxAspect;
                pull.y *= flip;
                world = tex2D(_MainTex, saturate(uv + pull)).rgb;
                #endif
                world *= 1.0 - 0.45 * open * near;

                // INSIDE: the tunnel. Depth u is 0 at the mouth and grows without bound toward the
                // centre, so streaks moving to larger u rush into the core.
                float n = saturate(r / iris);
                float u = -log(max(n, 1e-3));
                float ang = atan2(d.y, d.x) / FX_TAU + _Twist * saturate(B.z) * u + 0.03 * B.x;
                float lanes = max(floor(_Lanes + 0.5), 1.0);
                float lanePos = frac(ang) * lanes;
                float lane = floor(lanePos);
                float h = hash11(lane + 1.0);
                float across = abs(frac(lanePos) - 0.5) * 2.0;  // 0 on the lane's centre line
                float seg = frac(u * (0.6 + 0.8 * h) - B.x * _StreakSpeed * (0.7 + 0.6 * h) + 7.0 * h);
                float streak = smoothstep(0.0, 0.04, seg) * (1.0 - smoothstep(0.04, _StreakLength, seg));
                streak *= 1.0 - smoothstep(0.15, 0.6, across);
                streak *= step(0.3, h);                       // about two lanes in three carry a streak
                streak *= saturate(u * 3.0);                  // none right at the mouth
                float core = exp(-n * n / 0.008);
                float3 tunnel = lerp(_DeepColour.rgb, tint * 0.55, saturate(u * 0.3));
                tunnel += tint * streak * (1.2 + 0.8 * h);
                tunnel += _CoreColour.rgb * core * 1.4;
                #if !defined(FX_CHEAP)
                // The mouth: the world swirls in and fades over the outer quarter of the iris.
                float swirlA = 2.5 * (1.0 - n) * (0.5 + 0.5 * saturate(B.z));
                float2 rot = float2(d.x * cos(swirlA) - d.y * sin(swirlA), d.x * sin(swirlA) + d.y * cos(swirlA));
                float2 suv = A.xy + float2(rot.x / _FxAspect, rot.y) * (0.5 + 0.5 * n);
                #if UNITY_UV_STARTS_AT_TOP
                if (flip < 0.0)
                {
                    suv.y = 1.0 - suv.y;
                }
                #endif
                fixed3 mouth = tex2D(_MainTex, saturate(suv)).rgb;
                tunnel = lerp(tunnel, mouth * 0.6 + tint * 0.15, smoothstep(0.75, 1.0, n) * 0.6);
                #endif

                // The rim, soft both ways, so the iris never shows a stair-stepped edge.
                float inside = 1.0 - smoothstep(-2.0 * pixel, 2.0 * pixel, edge);
                float3 col = lerp(world, tunnel, inside);
                float rim = exp(-abs(edge) / max(_RimWidth * iris, 1.5 * pixel)) * saturate(open * 4.0);
                col += tint * rim * 1.3 + float3(1.0, 1.0, 1.0) * rim * 0.35;

                // The flare: white inside the iris and on its rim, so as the iris closes the white
                // shrinks back into the glass.
                col = lerp(col, float3(1.0, 1.0, 1.0), flare * saturate(inside + rim));
                return fixed4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
