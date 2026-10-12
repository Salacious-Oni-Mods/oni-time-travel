// SPACETIME WARP -- stage 1 of the spacetime-warp effect (oni/docs/spacetime-warp-effect.md §4).
// A full-screen pass on ONI's world view (Klei's Overlay camera, through OniFramework.ScreenEffects).
// Around each of up to 8 points it bends the picture the way a point mass bends light, rolls rings
// of ripple outward (inward when the consumer runs Phase backwards), splits colour along the radius
// and shimmers like air over a hot plate. Beyond a point's radius it changes NOTHING: the falloff
// reaches zero there with zero slope, so there is no visible edge.
//
// Pure visual: no state, no RNG -- the noise is a hash of position and phase.
//
// FX_CHEAP: the low-end variant. One texture sample instead of three, no shimmer noise; the lens,
// the ripple and the tint stay, so the effect reads the same at a glance.
Shader "FxSandbox/SpacetimeWarp"
{
    Properties
    {
        _MainTex ("Frame so far (bound by Graphics.Blit)", 2D) = "white" {}
        _LensScale ("Einstein radius per unit strength, fraction of R", Range(0, 1)) = 0.35
        _LensSoftening ("Lens core softening, fraction of R", Range(0.01, 1)) = 0.12
        _RippleAmp ("Ripple displacement per unit strength, fraction of R", Range(0, 0.2)) = 0.04
        _RippleSpeed ("Rings passing a point per unit phase", Float) = 1.0
        _ChromaScale ("Colour split, fraction of the displacement", Range(0, 1)) = 0.35
        _ShimmerAmp ("Shimmer displacement, pixels", Range(0, 8)) = 2.5
        _ShimmerScale ("Shimmer cells across the radius", Float) = 9.0
        _RimGlow ("Tint brightness on the ripple crests", Range(0, 2)) = 0.6
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
            #define FX_SHIMMER_PERIOD 8.0

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            // Written every frame by OniFramework.ScreenEffects (§3.3).
            float _FxAspect;
            float _FxPointCount;
            float4 _FxPointA[FX_MAX_POINTS];  // xy centre (viewport UV), z radius (viewport heights), w strength
            float4 _FxPointB[FX_MAX_POINTS];  // x phase, y colour shift, z shimmer, w rings
            float4 _FxPointC[FX_MAX_POINTS];  // rgb tint, a tint amount

            // Look, per material (§4.2).
            float _LensScale;
            float _LensSoftening;
            float _RippleAmp;
            float _RippleSpeed;
            float _ChromaScale;
            float _ShimmerAmp;
            float _ShimmerScale;
            float _RimGlow;

            // Stateless hash (no RNG state anywhere): 2D position -> [0, 1).
            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Value noise, smooth, [0, 1).
            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float n00 = hash12(i);
                float n10 = hash12(i + float2(1.0, 0.0));
                float n01 = hash12(i + float2(0.0, 1.0));
                float n11 = hash12(i + float2(1.0, 1.0));
                return lerp(lerp(n00, n10, u.x), lerp(n01, n11, u.x), u.y);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;

                // vp: this pixel in viewport space, (0,0) bottom-left, the space the framework writes
                // centres in. On D3D a flipped source reports a negative texel height; then the
                // picture is upside down relative to the viewport and offsets flip with it.
                float2 vp = uv;
                float flip = 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0.0)
                {
                    vp.y = 1.0 - vp.y;
                    flip = -1.0;
                }
                #endif

                // One pixel in "viewport heights", the unit radii and offsets are measured in.
                float pixel = abs(_MainTex_TexelSize.y);

                float2 offset = float2(0.0, 0.0);   // where to sample, relative to here (aspect-corrected)
                float2 chroma = float2(0.0, 0.0);   // red goes +chroma, blue goes -chroma
                float3 glow = float3(0.0, 0.0, 0.0);
                float shade = 1.0;
                int count = (int)_FxPointCount;

                [loop]
                for (int k = 0; k < FX_MAX_POINTS; k++)
                {
                    if (k >= count)
                    {
                        break;
                    }
                    float4 A = _FxPointA[k];
                    float4 B = _FxPointB[k];
                    float4 C = _FxPointC[k];

                    float R = max(A.z, 1e-4);
                    float2 d = vp - A.xy;
                    d.x *= _FxAspect;               // circles stay circles on a wide screen
                    float r = length(d);
                    float n = r / R;
                    if (n >= 1.0)
                    {
                        continue;                   // outside: exactly no effect
                    }
                    float2 dir = d / max(r, 1e-5);
                    float fall = 1.0 - n * n;
                    fall *= fall;                   // 1 at the centre, 0 with zero slope at the rim

                    float S = A.w;

                    // LENS. The point-mass lens map: what appears at radius r comes from radius
                    // r - thetaE^2 * r / (r^2 + soft^2). Inside thetaE the sample crosses the centre,
                    // which draws an Einstein ring. Softening keeps the core finite (the pull peaks at
                    // thetaE^2 / (2 * soft) at r = soft: about 0.51 R at strength 1).
                    float thetaE = _LensScale * S * R;
                    float soft = _LensSoftening * R;
                    float lens = thetaE * thetaE * r / (r * r + soft * soft);

                    // SHADOW. Once thetaE > soft the map folds back: what appears between the fold
                    // and the Einstein radius rE = sqrt(thetaE^2 - soft^2) (where r - lens = 0) comes
                    // from the far side of the centre, a second, inverted, magnified copy of whatever
                    // sits there (it showed as a spot, and still did once only the disc inside the
                    // fold was dark: it was the emitter's own sprite mirrored into a ring). So the shadow covers everything out
                    // to rE, with a soft edge, and only the direct image is drawn: a black hole.
                    float rE = sqrt(max(thetaE * thetaE - soft * soft, 0.0));
                    if (rE > 1e-5)
                    {
                        shade *= lerp(1.0, smoothstep(0.85 * rE, 1.03 * rE, r), saturate(S));
                    }

                    // RIPPLE. B.w rings across the radius, moving outward as phase rises and inward
                    // as it falls.
                    float wave = sin(FX_TAU * (n * B.w - B.x * _RippleSpeed));
                    float ripple = _RippleAmp * S * R * wave;

                    float radial = (lens + ripple) * fall;
                    offset -= dir * radial;         // sample nearer the centre

                    #if !defined(FX_CHEAP)
                    // CHROMATIC SHIFT. A split along the radius, in proportion to how far the sample
                    // moved, so it follows the lens instead of fringing the whole circle evenly.
                    chroma -= dir * (radial * _ChromaScale * B.y);

                    // TIME-DILATION SHIMMER. Two noise fields, drifting with phase, jitter the sample
                    // by up to _ShimmerAmp pixels. The drift circles once every FX_SHIMMER_PERIOD of
                    // phase instead of running in a straight line, so it is periodic: the framework
                    // wraps phase at ScreenEffects.PhaseWrapSeconds (1024, a multiple of the period),
                    // and a straight drift jumped by ~1700 noise cells at that wrap. Radii keep the
                    // old drift speeds (1.7 and 1.3 cells per unit phase).
                    float2 q = (d / R) * _ShimmerScale;
                    float a = FX_TAU * B.x / FX_SHIMMER_PERIOD;
                    float2 sc = float2(cos(a), sin(a));
                    float2 jit = float2(vnoise(q + sc * (1.7 * FX_SHIMMER_PERIOD / FX_TAU)),
                                        vnoise(q + sc.yx * (1.3 * FX_SHIMMER_PERIOD / FX_TAU) + 17.0)) - 0.5;
                    offset += jit * (2.0 * _ShimmerAmp * pixel * B.z * fall);
                    #endif

                    // TINT on the ripple crests.
                    glow += C.rgb * (C.a * fall * (0.5 + 0.5 * wave) * _RimGlow * saturate(S));
                }

                offset.x /= _FxAspect;
                chroma.x /= _FxAspect;
                offset.y *= flip;
                chroma.y *= flip;

                float2 suv = saturate(uv + offset);
                #if defined(FX_CHEAP)
                fixed3 col = tex2D(_MainTex, suv).rgb;
                #else
                fixed3 col;
                col.r = tex2D(_MainTex, saturate(suv + chroma)).r;
                col.g = tex2D(_MainTex, suv).g;
                col.b = tex2D(_MainTex, saturate(suv - chroma)).b;
                #endif
                col = (col + glow) * shade;
                return fixed4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
