# GcotuStationeersCADStyle shader bundles, built with no Unity Editor.
# Own HLSL -> d3dcompiler_47 -> own bundle writer. Pass layout borrowed from the game's Unlit/Texture container.
# Args: <game data dir> <out dir>
import copy, ctypes, os, struct, sys
from ctypes import POINTER, byref, c_char_p, c_size_t, c_uint, c_void_p
import lz4.block
import UnityPy
from UnityPy.helpers.Tpk import get_typetree_node
from UnityPy.helpers.TypeTreeHelper import write_typetree
from UnityPy.streams import EndianBinaryWriter

ENGINE = "2022.3.62f3"
VER = (2022, 3, 62, 3)
NAME = "Hidden/GcotuStationeersCADStyle"

VS = b"""
struct I { float4 p : POSITION; float2 uv : TEXCOORD0; };
struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
O main(I i) { O o; o.p = float4(i.p.xy, 0.5, 1); o.uv = i.uv; return o; }
"""

COMMON = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float> _CameraDepthTexture : register(t0);
Texture2D<float4> _OLParams : register(t1);
float4 P(int i) { return _OLParams.Load(int3(i, 0, 0)); }
float Eye(int2 c) { float4 z = P(0); return 1.0 / (z.z * _CameraDepthTexture.Load(int3(c, 0)) + z.w); }
float3 Pos(int2 c)
{
    float e = Eye(c); float4 f = P(1);
    float2 n = (float2(c) + 0.5) * f.zw;
    return float3((2 * n.x - 1) * f.x * e, (1 - 2 * n.y) * f.y * e, -e);
}
float3 ToWorld(float3 v) { return float3(dot(P(2).xyz, v), dot(P(3).xyz, v), dot(P(4).xyz, v)); }
"""

# Pass 0: runs after the G-buffer, before lighting. Normals between flat (from depth) and the game's own, no shine,
# metals keep their color. The pure flat normals also go to their own target, so lines do not depend on the blend.
PS_GBUFFER = COMMON + b"""
Texture2D<float4> _OLGB0 : register(t2);
Texture2D<float4> _OLGB1 : register(t3);
Texture2D<float4> _OLGB2 : register(t4);
struct O { float4 a : SV_Target0; float4 b : SV_Target1; float4 c : SV_Target2; float4 d : SV_Target3; };
O main(V i)
{
    int2 c = int2(i.p.xy);
    float3 m = Pos(c), l = Pos(c + int2(-1, 0)), r = Pos(c + int2(1, 0)), u = Pos(c + int2(0, -1)), d = Pos(c + int2(0, 1));
    float3 dx = abs(r.z - m.z) < abs(m.z - l.z) ? r - m : m - l;
    float3 dy = abs(d.z - m.z) < abs(m.z - u.z) ? d - m : m - u;
    float3 n = normalize(cross(dy, dx));
    if (dot(n, m) > 0) n = -n;
    float3 w = ToWorld(n);
    float4 g0 = _OLGB0.Load(int3(c, 0));
    float3 g1 = _OLGB1.Load(int3(c, 0)).rgb;
    float3 smoothN = _OLGB2.Load(int3(c, 0)).xyz * 2 - 1;
    float3 mixed = normalize(lerp(w, smoothN, P(8).y) + float3(0, 1e-5, 0));
    O o;
    o.a = float4(saturate(g0.rgb + max(g1 - 0.04, 0)), g0.a);
    o.b = float4(0, 0, 0, 0);
    o.c = float4(mixed * 0.5 + 0.5, 1);
    o.d = float4(w * 0.5 + 0.5, 1);
    return o;
}
"""

# Pass 1: after opaque lighting and the sky. Writes a one pixel wide line mask, faded by distance.
PS_MASK = COMMON + b"""
Texture2D<float4> _OLFlat : register(t2);
Texture2D<float4> _CameraGBufferTexture0 : register(t3);
float Sky(float e) { return e > P(6).w ? 1 : 0; }
float Edge(int2 a, int2 b)
{
    float ea = Eye(a), eb = Eye(b);
    float sa = Sky(ea), sb = Sky(eb);
    if (sa + sb > 1.5) return 0;
    if (sa + sb > 0.5) return 1;
    float3 na = _OLFlat.Load(int3(a, 0)).xyz * 2 - 1;
    float3 nb = _OLFlat.Load(int3(b, 0)).xyz * 2 - 1;
    float4 k = P(5);
    if (dot(na, nb) < k.x) return 1;
    float3 d = ToWorld(Pos(b) - Pos(a));
    float off = min(abs(dot(na, d)) / ea, abs(dot(nb, d)) / eb);
    if (off > k.y) return 1;
    float3 ca = sqrt(saturate(_CameraGBufferTexture0.Load(int3(a, 0)).rgb));
    float3 cb = sqrt(saturate(_CameraGBufferTexture0.Load(int3(b, 0)).rgb));
    float3 dc = abs(ca - cb);
    if (max(dc.r, max(dc.g, dc.b)) > k.z) return 1;
    return 0;
}
float4 main(V i) : SV_Target
{
    int2 c = int2(i.p.xy);
    float e = max(Edge(c, c + int2(1, 0)), Edge(c, c + int2(0, 1)));
    float4 q = P(6);
    float fade = 1 - saturate((Eye(c) - q.y) / (q.z - q.y));
    return float4(e * fade, 0, 0, 1);
}
"""

# Pass 2: grows the mask to the set width with a soft round edge and blends black over the frame. Pixels marked in _OLNoLine get no line.
PS_DRAW = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float4> _OLParams : register(t0);
Texture2D<float> _OLMask : register(t1);
Texture2D<float> _OLNoLine : register(t2);
float4 main(V i) : SV_Target
{
    int2 c = int2(i.p.xy);
    float w = _OLParams.Load(int3(6, 0, 0)).x;
    float reach = w * 0.5 + 0.5;
    int r = (int)ceil(reach) - 1;
    float boost = _OLParams.Load(int3(13, 0, 0)).x;
    float best = 0;
    for (int y = -r; y <= r; y++)
    {
        for (int x = -r; x <= r; x++)
        {
            float cover = saturate(reach - length(float2(x, y)));
            if (cover > best) best = max(best, cover * saturate(_OLMask.Load(int3(c + int2(x, y), 0)) * boost));
        }
    }
    return float4(0, 0, 0, best * saturate(w) * _OLParams.Load(int3(5, 0, 0)).w * (1.0 - saturate(_OLNoLine.Load(int3(c, 0)))));
}
"""

# Pass 3: after the G-buffer, multiplies the ambient and emission already written to the frame.
PS_AMBIENT = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float4> _OLParams : register(t0);
float4 main(V i) : SV_Target
{
    float k = _OLParams.Load(int3(8, 0, 0)).x;
    return float4(k, k, k, 1);
}
"""


# Pass 4: after opaque lighting, before the lines. Facet shading and light bands work on the light level,
# which is the lit pixel divided by its surface color, so colors and paint stay as they are.
PS_SHADE = COMMON + b"""
Texture2D<float4> _OLSrc : register(t2);
Texture2D<float4> _CameraGBufferTexture0 : register(t3);
Texture2D<float4> _CameraGBufferTexture2 : register(t4);
float Lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
float4 main(V i) : SV_Target
{
    int2 c = int2(i.p.xy);
    float4 src = _OLSrc.Load(int3(c, 0));
    if (Eye(c) > P(6).w) return src;
    float4 f = P(7);
    float3 n = _CameraGBufferTexture2.Load(int3(c, 0)).xyz * 2 - 1;
    float3 col = src.rgb * max(0, 1 + f.w * dot(n, f.xyz));
    float strength = P(8).z;
    float albedo = Lum(_CameraGBufferTexture0.Load(int3(c, 0)).rgb);
    if (strength > 0 && albedo > 0.01)
    {
        float top = P(9).x, level = Lum(col) / albedo;
        if (level < top * 3)
        {
            float bands = P(8).w;
            float t = pow(saturate(level / top), 1 / 2.2);
            float q = min(floor(t * bands) / (bands - 1), 1);
            float target = top * pow(lerp(P(9).y, 1, q), 2.2);
            col *= lerp(1, target / max(level, 0.0001), strength);
        }
    }
    return float4(col, src.a);
}
"""


# Pass 5: between mask and draw. Crossfades the line mask with the previous frames' mask, followed through camera motion.
# History only counts where its stored depth fits a surface around this pixel now, so a line can't stay behind in the air.
# Neighbor depths (not just this pixel's) let a silhouette pixel keep its history while camera jitter moves it across the edge.
PS_STABLE = COMMON + b"""
Texture2D<float> _OLMask : register(t2);
Texture2D<float4> _OLHist : register(t3);
float4 main(V i) : SV_Target
{
    int2 c = int2(i.p.xy);
    float cur = _OLMask.Load(int3(c, 0));
    float eye = Eye(c);
    float3 rel = ToWorld(Pos(c)) + float3(P(10).w, P(11).w, P(12).w);
    float3 pv = float3(dot(P(10).xyz, rel), dot(P(11).xyz, rel), dot(P(12).xyz, rel));
    float pe = -pv.z;
    if (pe <= 0) return float4(cur, eye, 0, 1);
    float4 f = P(1);
    float2 px = float2(pv.x / (pe * f.x) + 1, 1 - pv.y / (pe * f.y)) * 0.5 / f.zw - 0.5;
    float lo = eye, hi = eye;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float e = Eye(c + int2(x, y));
            lo = min(lo, e);
            hi = max(hi, e);
        }
    }
    float shift = pe - eye, tol = pe * 0.004 + 0.001;
    int2 b = int2(floor(px));
    float2 t = px - float2(b);
    float sum = 0, weight = 0;
    for (int n = 0; n < 4; n++)
    {
        int2 o = int2(n & 1, n >> 1);
        float4 h = _OLHist.Load(int3(b + o, 0));
        float w = (o.x == 1 ? t.x : 1 - t.x) * (o.y == 1 ? t.y : 1 - t.y);
        if (h.g >= lo + shift - tol && h.g <= hi + shift + tol) { sum += w * h.r; weight += w; }
    }
    float keep = P(13).y * saturate(1 - length(px - float2(c)) * 0.5) * saturate(weight * 2);
    return float4(lerp(cur, sum / max(weight, 0.0001), keep), eye, 0, 1);
}
"""


# Pass 6: half size. What goes on to the glow from the emission buffer. Colored glow 0: only white light, the weakest channel above the threshold.
# Colored glow 1: the strongest channel decides, and the glow keeps the light's color. Color boost scales it per channel by how colored the light is:
# white gets none of it, so the normal glow stays as it is, and at boost 1 nothing changes.
PS_GLOW_EXTRACT = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float4> _OLParams : register(t0);
Texture2D<float4> _OLGlowSrc : register(t1);
float4 main(V i) : SV_Target
{
    int2 c = int2(i.p.xy) * 2;
    float3 a = 0;
    for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
            a += _OLGlowSrc.Load(int3(c + int2(x, y), 0)).rgb;
    a *= 0.25;
    float4 k = _OLParams.Load(int3(15, 0, 0));
    float hi = max(a.r, max(a.g, a.b));
    float lo = min(a.r, min(a.g, a.b));
    float v = clamp(lerp(lo, hi, k.y) - k.x, 0, 8);
    float colored = (1.0 - lo / max(hi, 0.0001)) * k.y;
    float3 boost = lerp(float3(1, 1, 1), _OLParams.Load(int3(18, 0, 0)).rgb, colored);
    return float4(lerp(float3(1, 1, 1), a / max(hi, 0.0001), k.y) * v * boost, 1);
}
"""

# Pass 7: half the size of the level before, with soft weights so small lamps don't shimmer.
PS_GLOW_DOWN = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float4> _OLGlowA : register(t0);
float4 main(V i) : SV_Target
{
    int2 c = int2(i.p.xy) * 2;
    uint w, h;
    _OLGlowA.GetDimensions(w, h);
    int2 last = int2(w, h) - 1;
    float3 sum = 0;
    for (int y = -1; y <= 2; y++)
    {
        for (int x = -1; x <= 2; x++)
        {
            float wx = (x == -1 || x == 2) ? 1 : 3, wy = (y == -1 || y == 2) ? 1 : 3;
            sum += _OLGlowA.Load(int3(clamp(c + int2(x, y), int2(0, 0), last), 0)).rgb * wx * wy;
        }
    }
    return float4(sum / 64, 1);
}
"""

# Reads past a level's edge take the edge pixel. Reading them as black would dim a large glow near the screen's edge.
BILINEAR_A = b"""
float3 LoadA(int2 c, int2 last) { return _OLGlowA.Load(int3(clamp(c, int2(0, 0), last), 0)).rgb; }
float3 BilinearA(float2 q)
{
    uint w, h;
    _OLGlowA.GetDimensions(w, h);
    int2 last = int2(w, h) - 1;
    int2 b = int2(floor(q));
    float2 t = q - float2(b);
    return lerp(lerp(LoadA(b, last), LoadA(b + int2(1, 0), last), t.x),
                lerp(LoadA(b + int2(0, 1), last), LoadA(b + int2(1, 1), last), t.x), t.y);
}
"""

# Pass 8: this level plus the smoother level below it. The weight of the lower levels sets the radius.
PS_GLOW_UP = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float4> _OLParams : register(t0);
Texture2D<float4> _OLGlowA : register(t1);
Texture2D<float4> _OLGlowB : register(t2);
""" + BILINEAR_A + b"""
float4 main(V i) : SV_Target
{
    int2 c = int2(i.p.xy);
    float3 lo = BilinearA((float2(c) + 0.5) * 0.5 - 0.5);
    return float4(min(_OLGlowB.Load(int3(c, 0)).rgb + lo * _OLParams.Load(int3(14, 0, 0)).y, 4096.0), 1);
}
"""

# Pass 9: added onto the frame over the lines, so a glow lies in front of everything. The first level is a quarter of the size.
PS_GLOW_ADD = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float4> _OLParams : register(t0);
Texture2D<float4> _OLGlowA : register(t1);
""" + BILINEAR_A + b"""
float4 main(V i) : SV_Target
{
    float3 g = BilinearA((float2(int2(i.p.xy)) + 0.5) * 0.25 - 0.5);
    return float4(g * _OLParams.Load(int3(14, 0, 0)).x, 0);
}
"""

# Pass 12: eye adaptation's gamma on the finished opaque frame. A channel above 1 stays as it is, so what is brighter than white
# stays white while everything below it goes darker. Same rule as the screen gamma in his SEGI fork.
PS_EXPOSE = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float4> _OLParams : register(t0);
Texture2D<float4> _OLSrc : register(t1);
float4 main(V i) : SV_Target
{
    float g = _OLParams.Load(int3(17, 0, 0)).x;
    float4 c = _OLSrc.Load(int3(int2(i.p.xy), 0));
    float3 x = c.rgb;
    x.r = x.r <= 1.0 ? pow(max(x.r, 0.0), g) : x.r;
    x.g = x.g <= 1.0 ? pow(max(x.g, 0.0), g) : x.g;
    x.b = x.b <= 1.0 ? pow(max(x.b, 0.0), g) : x.b;
    return float4(x, c.a);
}
"""

# Pass 13: blue noise dither, works with the look on or off. Three taps of one tile so channels don't share a dot pattern. Same rule as in his SEGI fork.
PS_DITHER = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float4> _OLParams : register(t0);
Texture2D<float4> _OLSrc : register(t1);
Texture2D<float4> _OLNoise : register(t2);
float4 main(V i) : SV_Target
{
    float4 k = _OLParams.Load(int3(17, 0, 0));
    float4 c = _OLSrc.Load(int3(int2(i.p.xy), 0));
    uint n = (uint)k.z;
    uint2 u = uint2(i.p.xy);
    float3 d = float3(_OLNoise.Load(int3(u % n, 0)).r, _OLNoise.Load(int3((u + uint2(157, 47)) % n, 0)).r, _OLNoise.Load(int3((u + uint2(311, 199)) % n, 0)).r) - 0.5;
    return float4(max(c.rgb + d * k.y, 0.0), c.a);
}
"""

# Passes 10 and 11: bake a faded copy of a game texture. Palettes fade whole to the paint color, the cell at the top right of the
# shade ramp. Everything else fades whole to its average, which is muddy on purpose. Alpha stays, so cutouts keep their shape.
# Then, for color textures only, the grading: black and white points, gamma and saturation in a roughly perceptual space (power 1/2.2).
# Colors more vivid than Keep vivid keep their saturation, like highlights in a CAD drawing. Its threshold is set per channel and mixed
# by how much each channel stands above the weakest one, so a yellow uses red's and green's. Pixel 4: keep red, on, keep green, keep blue.
FADE_BODY = b"""
float4 main(V i) : SV_Target
{
    int2 c = int2(i.p.xy);
    float4 a = _OLFadeP.Load(int3(0, 0, 0)), b = _OLFadeP.Load(int3(1, 0, 0)), m = _OLFadeP.Load(int3(2, 0, 0));
    int lod = int(a.z), slice = int(a.w);
    float4 s = FADE_LOAD(c, slice, lod);
    float3 goal = m.rgb;
    if (a.y < 0.5)
        goal = FADE_LOAD(int2(b.xy * 0.875), slice, lod).rgb;
    float3 x = lerp(s.rgb, goal, a.x);
    float4 g = _OLFadeP.Load(int3(3, 0, 0)), h = _OLFadeP.Load(int3(4, 0, 0));
    if (h.y < 0.5)
        return float4(x, s.a);
    float3 p = pow(max(x, 0.0), 1.0 / 2.2);
    p = saturate((p - g.x) / max(1.0 - g.y - g.x, 0.001));
    p = pow(p, g.z);
    float lo = min(p.r, min(p.g, p.b)), hi = max(p.r, max(p.g, p.b));
    float3 share = p - lo;
    float shares = share.r + share.g + share.b;
    float keepAbove = shares > 0.0001 ? dot(share, float3(h.x, h.z, h.w)) / shares : 1.0;
    float vivid = keepAbove < 1.0 ? smoothstep(keepAbove, keepAbove + 0.1, hi - lo) : 0.0;
    float l = dot(p, float3(0.2126, 0.7152, 0.0722));
    p = saturate(lerp(float3(l, l, l), p, lerp(g.w, 1.0, vivid)));
    return float4(pow(p, 2.2), s.a);
}
"""
PS_FADE = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float4> _OLFadeP : register(t0);
Texture2D<float4> _OLFadeSrc : register(t1);
#define FADE_LOAD(c, slice, lod) _OLFadeSrc.Load(int3(c, lod))
""" + FADE_BODY
PS_FADE_ARRAY = b"""
struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
Texture2D<float4> _OLFadeP : register(t0);
Texture2DArray<float4> _OLFadeSrc : register(t1);
#define FADE_LOAD(c, slice, lod) _OLFadeSrc.Load(int4(c, slice, lod))
""" + FADE_BODY


# The game's point lamp lighting, written out from the disassembly of Hidden/Internal-DeferredShading, registers as the game has them.
# With FIX 0 it compiles to the game's own program (build compares them). With FIX 1 each shadow read takes its depth from where its ray
# meets the surface's plane, less the depth change over one shadow map cell, within the limit. _OLFlat holds the surface's flat normal.
PS_LAMP = b"""
struct V { float4 p : SV_POSITION; float4 uv : TEXCOORD0; float3 ray : TEXCOORD1; };
cbuffer Globals : register(b0)
{
    float4 pad0[4]; float4 _LightPos; float4 _LightColor; float4 pad1;
    float4x4 unity_WorldToLight;
};
cbuffer UnityPerCamera : register(b1) { float4 pad2[4]; float3 _WorldSpaceCameraPos; float4 _ProjectionParams; float4 pad3; float4 _ZBufferParams; };
cbuffer UnityPerCameraRare : register(b2) { float4 pad4[18]; float4x4 unity_CameraToWorld; };
cbuffer UnityLighting : register(b3) { float4 pad5[2]; float4 _LightProjectionParams; };
cbuffer UnityShadows : register(b4) { float4 pad6[24]; float4 _LightShadowData; float4 unity_ShadowFadeCenterAndType; };
Texture2D _CameraDepthTexture : register(t0);
SamplerState sampler_CameraDepthTexture : register(s0);
Texture2D _LightTextureB0 : register(t1);
SamplerState sampler_LightTextureB0 : register(s1);
#if COOKIE
TextureCube _LightTexture0 : register(t2);
SamplerState sampler_LightTexture0 : register(s2);
SamplerComparisonState sampler_ShadowMapTexture : register(s3);
Texture2D _CameraGBufferTexture0 : register(t3);
SamplerState sampler_CameraGBufferTexture0 : register(s4);
Texture2D _CameraGBufferTexture1 : register(t4);
SamplerState sampler_CameraGBufferTexture1 : register(s5);
Texture2D _CameraGBufferTexture2 : register(t5);
SamplerState sampler_CameraGBufferTexture2 : register(s6);
TextureCube _ShadowMapTexture : register(t6);
#if FIX
Texture2D<float4> _OLParams : register(t7);
Texture2D<float4> _OLFlat : register(t8);
#endif
#else
SamplerComparisonState sampler_ShadowMapTexture : register(s2);
Texture2D _CameraGBufferTexture0 : register(t2);
SamplerState sampler_CameraGBufferTexture0 : register(s3);
Texture2D _CameraGBufferTexture1 : register(t3);
SamplerState sampler_CameraGBufferTexture1 : register(s4);
Texture2D _CameraGBufferTexture2 : register(t4);
SamplerState sampler_CameraGBufferTexture2 : register(s5);
TextureCube _ShadowMapTexture : register(t5);
#if FIX
Texture2D<float4> _OLParams : register(t6);
Texture2D<float4> _OLFlat : register(t7);
#endif
#endif

float Dom(float3 v) { float3 a = abs(v); return max(max(a.x, a.y), a.z); }
float Ref(float d)
{
    d = max(d - _LightProjectionParams.z, 0.00001) * _LightProjectionParams.w;
    return 1.0 - (_LightProjectionParams.y / d - _LightProjectionParams.x);
}
float Read(float3 d, float r) { return _ShadowMapTexture.SampleCmpLevelZero(sampler_ShadowMapTexture, d, r); }

#if FIX
// One shadow map cell, read through its center. A stored point on the lit surface's own plane can't block the lamp: its ray meets that plane only once.
float Cell(float3 d, float3 v, float3 n, float tol, float r)
{
    float z = _ShadowMapTexture.SampleLevel(sampler_LightTextureB0, d, 0).x;
    if (r >= z) return 1;
    float stored = _LightProjectionParams.y / (1.0 + _LightProjectionParams.x - z);
    return dot(d * (stored / Dom(d)) - v, n) <= tol ? 1 : 0;
}
// The same four cells and weights as the comparison sampler's filter. Cell centers don't depend on how the face is flipped; past a face's edge the direction finds the next face.
float Filtered(float3 d, float3 v, float3 n, float tol, float r, float size)
{
    float3 a = abs(d);
    float3 t1 = (a.x >= a.y && a.x >= a.z) ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 t2 = (a.z >= a.x && a.z >= a.y) ? float3(0, 1, 0) : float3(0, 0, 1);
    float m = Dom(d);
    float ds = dot(d, t1), dt = dot(d, t2);
    float3 major = (d - t1 * ds - t2 * dt) / m;
    float2 uv = (float2(ds, dt) / m + 1) * 0.5 * size - 0.5;
    float2 cellIndex = floor(uv), w = uv - cellIndex;
    float2 c0 = (cellIndex + 0.5) / size * 2 - 1;
    float step = 2 / size;
    float a00 = Cell(major + t1 * c0.x + t2 * c0.y, v, n, tol, r);
    float a10 = Cell(major + t1 * (c0.x + step) + t2 * c0.y, v, n, tol, r);
    float a01 = Cell(major + t1 * c0.x + t2 * (c0.y + step), v, n, tol, r);
    float a11 = Cell(major + t1 * (c0.x + step) + t2 * (c0.y + step), v, n, tol, r);
    return lerp(lerp(a00, a10, w.x), lerp(a01, a11, w.x), w.y);
}
float Fixed(float3 v, float3 d, float3 n, float vn, float base, float slope, float limit, float tol, float size)
{
    float dn = dot(d, n);
    float here = dn < -0.000001 ? Dom(d * (vn / dn)) : base;
    float r = Ref(clamp(here - slope, base - limit, base + limit));
    if (tol > 0) return Filtered(d, v, n, tol, r, size);
    return Read(d, r);
}
float Lower(float3 e, float3 n, float vn, float low, float floorValue)
{
    float en = dot(e, n);
    return min(low, en < -0.000001 ? Dom(e * (vn / en)) : floorValue);
}
#endif

float Shadow(float3 v, int2 pixel)
{
#if FIX
    float4 fix = _OLParams.Load(int3(16, 0, 0));
    float limit = fix.x, tol = fix.z;
    float3 n = _OLFlat.Load(int3(pixel, 0)).xyz * 2 - 1;
    float nn = dot(n, n);
    float vn = dot(v, n);
    if ((limit > 0 || tol > 0) && nn > 0.81 && nn < 1.21 && vn < -0.000001)
    {
        float base = Dom(v);
        float3 a = abs(v);
        float3 t1 = (a.x >= a.y && a.x >= a.z) ? float3(0, 1, 0) : float3(1, 0, 0);
        float3 t2 = (a.z >= a.x && a.z >= a.y) ? float3(0, 1, 0) : float3(0, 0, 1);
        float size, height;
        _ShadowMapTexture.GetDimensions(size, height);
        float cell = base * 2 / size;
        float low = base, floorValue = base - limit;
        low = Lower(v + t1 * cell, n, vn, low, floorValue);
        low = Lower(v - t1 * cell, n, vn, low, floorValue);
        low = Lower(v + t2 * cell, n, vn, low, floorValue);
        low = Lower(v - t2 * cell, n, vn, low, floorValue);
        float slope = base - low;
#if SOFT
        return 0.25 * (Fixed(v, v + float3( 0.0078125,  0.0078125,  0.0078125), n, vn, base, slope, limit, tol, size)
                     + Fixed(v, v + float3(-0.0078125, -0.0078125,  0.0078125), n, vn, base, slope, limit, tol, size)
                     + Fixed(v, v + float3(-0.0078125,  0.0078125, -0.0078125), n, vn, base, slope, limit, tol, size)
                     + Fixed(v, v + float3( 0.0078125, -0.0078125, -0.0078125), n, vn, base, slope, limit, tol, size));
#else
        return Fixed(v, v, n, vn, base, slope, limit, tol, size);
#endif
    }
#endif
    float r = Ref(Dom(v));
#if SOFT
    float4 s;
    s.x = Read(v + float3( 0.0078125,  0.0078125,  0.0078125), r);
    s.y = Read(v + float3(-0.0078125, -0.0078125,  0.0078125), r);
    s.z = Read(v + float3(-0.0078125,  0.0078125, -0.0078125), r);
    s.w = Read(v + float3( 0.0078125, -0.0078125, -0.0078125), r);
    return dot(s, float4(0.25, 0.25, 0.25, 0.25));
#else
    return Read(v, r);
#endif
}

float Pow5(float x) { float y = x * x; return x * y * y; }

float4 main(V i) : SV_Target
{
    float3 ray = i.ray * (_ProjectionParams.z / i.ray.z);
    float2 uv = i.uv.xy / i.uv.w;
    float depth = 1.0 / (_ZBufferParams.x * _CameraDepthTexture.Sample(sampler_CameraDepthTexture, uv).x + _ZBufferParams.y);
    float3 vpos = ray * depth;
    float3 wpos = mul(unity_CameraToWorld, float4(vpos, 1)).xyz;
    float z = vpos.z;
    float toCenter = length(wpos - unity_ShadowFadeCenterAndType.xyz);
    float fadeDist = lerp(z, toCenter, unity_ShadowFadeCenterAndType.w);
    float3 v = wpos - _LightPos.xyz;
    float dd = dot(v, v);
    float3 toLamp = -v * (1.0 / sqrt(dd));
    float atten = _LightTextureB0.Sample(sampler_LightTextureB0, (dd * _LightPos.w).xx).x;
    float fade = saturate(fadeDist * _LightShadowData.z + _LightShadowData.w);
#if SOFT
    float sh = 1.0;
    if (fade < 0.99)
        sh = lerp(_LightShadowData.x, 1.0, Shadow(v, int2(i.p.xy)));
#else
    float sh = lerp(_LightShadowData.x, 1.0, Shadow(v, int2(i.p.xy)));
#endif
    atten *= lerp(sh, 1.0, fade);
#if COOKIE
    atten *= _LightTexture0.SampleBias(sampler_LightTexture0, mul(unity_WorldToLight, float4(wpos, 1)).xyz, -8.0).w;
#endif
    float3 light = _LightColor.rgb * atten;
#if FIX
    if (_OLParams.Load(int3(16, 0, 0)).y > 0.5) light *= float3(1.0, 0.25, 1.0);
#endif
    float4 gb0 = _CameraGBufferTexture0.Sample(sampler_CameraGBufferTexture0, uv);
    float4 gb1 = _CameraGBufferTexture1.Sample(sampler_CameraGBufferTexture1, uv);
    float4 gb2 = _CameraGBufferTexture2.Sample(sampler_CameraGBufferTexture2, uv);
    float3 n0 = normalize(gb2.xyz * 2.0 - 1.0);
    float3 eye = normalize(wpos - _WorldSpaceCameraPos);
    float rough0 = 1.0 - gb1.a;
    float3 h = toLamp - eye;
    h = h * (1.0 / sqrt(max(dot(h, h), 0.001)));
    float nv = dot(n0, -eye);
    float nl = saturate(dot(n0, toLamp));
    float nh = saturate(dot(n0, h));
    float lh = saturate(dot(toLamp, h));
    float fd = 2.0 * lh * lh * rough0 - 0.5;
    float ls = 1.0 + fd * Pow5(1.0 - nl);
    float vs = 1.0 + fd * Pow5(1.0 - abs(nv));
    float rough = max(rough0 * rough0, 0.002);
    float lv = nl * (abs(nv) * (1.0 - rough) + rough);
    float lw = abs(nv) * (nl * (1.0 - rough) + rough);
    float vis = 0.5 / (lv + lw + 0.00001);
    float a2 = rough * rough;
    float dn = (nh * a2 - nh) * nh + 1.0;
    float dd2 = a2 * 0.31830988618 / (dn * dn + 0.0000001);
    float spec = max(vis * dd2 * 3.14159265359 * nl, 0.0);
    spec *= dot(gb1.rgb, gb1.rgb) != 0.0 ? 1.0 : 0.0;
    float diff = vs * ls * nl;
    float3 f = gb1.rgb + (1.0 - gb1.rgb) * Pow5(1.0 - lh);
    float3 col = gb0.rgb * (light * diff) + light * spec * f;
#if HDR
    return float4(col, 1.0);
#else
    return exp2(-float4(col, 1.0));
#endif
}
"""


class Vtbl(ctypes.Structure):
    _fields_ = [("QueryInterface", c_void_p), ("AddRef", c_void_p),
                ("Release", ctypes.WINFUNCTYPE(c_uint, c_void_p)),
                ("GetBufferPointer", ctypes.WINFUNCTYPE(c_void_p, c_void_p)),
                ("GetBufferSize", ctypes.WINFUNCTYPE(c_size_t, c_void_p))]


class Blob(ctypes.Structure):
    _fields_ = [("v", POINTER(Vtbl))]


def compile_hlsl(src, target):
    d = ctypes.WinDLL("d3dcompiler_47.dll")
    code, err = POINTER(Blob)(), POINTER(Blob)()
    hr = d.D3DCompile(c_char_p(src), c_size_t(len(src)), None, None, None, b"main", target,
                      1 << 15, 0, byref(code), byref(err))
    if hr != 0:
        p = err.contents.v.contents.GetBufferPointer(err)
        raise SystemExit("HLSL error:\n" + ctypes.string_at(p).decode("latin1"))
    return ctypes.string_at(code.contents.v.contents.GetBufferPointer(code), code.contents.v.contents.GetBufferSize(code))


def swap_code(entry, dxbc, counts=None):
    """entry = head + keyword strings + code length + 38-byte prefix-ish + DXBC + tail."""
    dx = entry.find(b"DXBC")
    old = struct.unpack("<I", entry[dx + 24:dx + 28])[0]
    tail = entry[dx + old:]
    kw = struct.unpack("<i", entry[24:28])[0]
    p = 28
    for _ in range(kw):
        ln = struct.unpack("<i", entry[p:p + 4])[0]
        p += 4 + ((ln + 3) & ~3)
    code_len = struct.unpack("<i", entry[p:p + 4])[0]
    assert code_len == dx - (p + 4) + old
    prefix = entry[p + 4:dx]
    if counts is not None:          # textures, constant buffers, samplers, as the game's own programs store them
        prefix = bytes([prefix[0], counts[0], counts[1], counts[2]]) + prefix[4:]
    return entry[:p] + struct.pack("<i", len(prefix) + len(dxbc)) + prefix + dxbc + tail


def rebuild_dx11(raw, vs, ps_g, extra):
    n = struct.unpack("<i", raw[:4])[0]
    e = []
    for k in range(n):
        off, ln, _ = struct.unpack("<iii", raw[4 + 12 * k:16 + 12 * k])
        e.append(raw[off:off + ln])
    assert n == 8
    t6, t7 = e[6], e[7]
    e[2], e[3] = swap_code(e[2], vs), swap_code(e[3], vs)
    e[6], e[7] = swap_code(t6, ps_g, (5, 0, 0)), swap_code(t7, ps_g, (5, 0, 0))
    for ps, n_tex in extra:
        e += [swap_code(t6, ps, (n_tex, 0, 0)), swap_code(t7, ps, (n_tex, 0, 0))]
    out = struct.pack("<i", len(e))
    pos = 4 + 12 * len(e)
    for x in e:
        out += struct.pack("<iii", pos, len(x), 0)
        pos += len(x)
    return out + b"".join(e)


def normalize(node):
    for i, n in enumerate(node.traverse()):
        n.m_Index = i
        n.m_RefTypeHash = 0
        if n.m_MetaFlag is None:
            n.m_MetaFlag = 0
        n.m_TypeFlags = 1 if n.m_Type == "Array" or n.m_Name == "Array" else (n.m_TypeFlags or 0)
    return node


def to_bytes(data, node):
    w = EndianBinaryWriter(endian="<")
    write_typetree(data, node, w, None)
    return w.bytes


def serialized_file(shader_bytes, shader_node, ab_bytes, ab_node, shader_hash):
    m = EndianBinaryWriter(endian="<")
    m.write_string_to_null(ENGINE)
    m.write_int(19)
    m.write_boolean(True)
    m.write_int(2)
    for cid, h, node in ((142, b"\0" * 16, ab_node), (48, shader_hash, shader_node)):
        m.write_int(cid)
        m.write_boolean(False)
        m.write_short(-1)
        m.write_bytes(h)
        node.dump_blob(m, 22)
        m.write_int(0)
    objs = [(1, 0, ab_bytes), (2, 1, shader_bytes)]
    d = EndianBinaryWriter(endian="<")
    m.write_int(len(objs))
    for pid, tid, b in objs:
        m.align_stream(4)
        m.write_long(pid)
        m.write_long(d.Length)
        m.write_u_int(len(b))
        m.write_int(tid)
        d.write_bytes(b)
        d.align_stream(8)
    m.write_int(0)
    m.write_int(0)
    m.write_int(0)
    m.write_string_to_null("")
    meta, data = m.bytes, d.bytes
    head = 48
    off = head + len(meta)
    off += (16 - off % 16) % 16
    size = off + len(data)
    h = struct.pack(">IIII", 0, 0, 22, 0) + b"\0\0\0\0" + struct.pack(">Iqqq", len(meta), size, off, 0)
    return h + meta + b"\0" * (off - head - len(meta)) + data


def unity_fs(name, payload):
    info = b"\0" * 16 + struct.pack(">I", 1) + struct.pack(">IIH", len(payload), len(payload), 0x40)
    info += struct.pack(">I", 1) + struct.pack(">qqI", 0, len(payload), 4) + name.encode() + b"\0"
    head = b"UnityFS\0" + struct.pack(">I", 8) + b"5.x.x\0" + ENGINE.encode() + b"\0"
    fixed = len(head) + 20
    pad1 = (16 - fixed % 16) % 16
    pos = fixed + pad1 + len(info)
    pad2 = (16 - pos % 16) % 16
    total = pos + pad2 + len(payload)
    return head + struct.pack(">qIII", total, len(info), len(info), 0x40 | 0x200) + b"\0" * pad1 + info + b"\0" * pad2 + payload


def tex_params(names, arrays=()):
    return [{"m_NameIndex": i, "m_Index": i, "m_SamplerIndex": -1, "m_MultiSampled": False, "m_Dim": 5 if n in arrays else 2} for i, n in enumerate(names)]


def disasm_body(code):
    d = ctypes.WinDLL("d3dcompiler_47.dll")
    out = POINTER(Blob)()
    hr = d.D3DDisassemble(code, c_size_t(len(code)), 0, None, byref(out))
    if hr != 0:
        raise SystemExit(f"disassemble failed {hr:#x}")
    text = ctypes.string_at(out.contents.v.contents.GetBufferPointer(out), out.contents.v.contents.GetBufferSize(out)).decode("latin1")
    lines = [ln.strip() for ln in text.splitlines()]
    return [ln for ln in lines if ln and not ln.startswith("//") and not ln.startswith("dcl_temps")]


def entry_dxbc(entry):
    dx = entry.find(b"DXBC")
    return entry[dx:dx + struct.unpack("<I", entry[dx + 24:dx + 28])[0]]


def add_textures(entry, first):
    """Adds _OLParams and _OLFlat to a per-variant parameter list: name, kind 0, slot, sampler -1 (Load only), flags 4 as 2D textures have there."""
    mark = struct.pack("<i", 17) + b"_ShadowMapTexture"
    start = entry.index(mark)
    count = struct.unpack("<i", entry[start - 4:start])[0]
    bound = entry.index(struct.pack("<i", 12) + b"UnityShadows", start)
    extra = b""
    for i, name in enumerate((b"_OLParams", b"_OLFlat")):
        extra += struct.pack("<i", len(name)) + name + b"\0" * ((4 - len(name) % 4) % 4) + struct.pack("<iiii", 0, first + i, -1, 4)
    return entry[:start - 4] + struct.pack("<i", count + 2) + entry[start:bound] + extra + entry[bound:]


LAMP_VARIANTS = (  # blob entry, soft, cookie, hdr
    (48, 0, 0, 0), (49, 1, 0, 0), (50, 0, 1, 0), (51, 1, 1, 0),
    (61, 0, 0, 1), (62, 1, 0, 1), (63, 0, 1, 1), (64, 1, 1, 1))


def build_lamp_bundle(data_dir, out, shader_node, ab_node, shader_hash):
    env = UnityPy.load(os.path.join(data_dir, "Resources", "unity_builtin_extra"))
    src = None
    for o in env.objects:
        if o.type.name == "Shader":
            d = o.read_typetree()
            if (d.get("m_ParsedForm") or {}).get("m_Name") == "Hidden/Internal-DeferredShading":
                src = d
                break
    if src is None:
        raise SystemExit("the game's deferred shader was not found")
    i = src["platforms"].index(4)
    blob = bytes(src["compressedBlob"])
    raw = lz4.block.decompress(blob[src["offsets"][i][0]:src["offsets"][i][0] + src["compressedLengths"][i][0]], uncompressed_size=src["decompressedLengths"][i][0])
    n = struct.unpack("<i", raw[:4])[0]
    entries = []
    for k in range(n):
        off, ln, seg = struct.unpack("<iii", raw[4 + 12 * k:16 + 12 * k])
        assert seg == 0
        entries.append(raw[off:off + ln])
    for entry_index, soft, cookie, hdr in LAMP_VARIANTS:
        defs = b"#define SOFT %d\n#define COOKIE %d\n#define HDR %d\n" % (soft, cookie, hdr)
        same = compile_hlsl(defs + b"#define FIX 0\n" + PS_LAMP, b"ps_4_0")
        ours, game = disasm_body(same), disasm_body(entry_dxbc(entries[entry_index]))
        print(f"lamp program {entry_index}: without the fix {len(ours)} lines, game {len(game)}, " + ("identical" if ours == game else "differs in " + str(sum(1 for a, b in zip(ours, game) if a != b)) + " places"))
        fixed = compile_hlsl(defs + b"#define FIX 1\n" + PS_LAMP, b"ps_4_0")
        entries[entry_index] = swap_code(entries[entry_index], fixed, (9, 5, 7) if cookie else (8, 5, 6))
    entries[37] = add_textures(entries[37], 6)
    entries[38] = add_textures(entries[38], 7)
    table = struct.pack("<i", n)
    pos = 4 + 12 * n
    for x in entries:
        table += struct.pack("<iii", pos, len(x), 0)
        pos += len(x)
    rawn = table + b"".join(entries)
    comp = lz4.block.compress(rawn, store_size=False, mode="high_compression")
    d = copy.deepcopy(src)
    pf = d["m_ParsedForm"]
    pf["m_Name"] = "Hidden/GcotuStationeersCADLamps"
    d.update(platforms=[4], offsets=[[0]], compressedLengths=[[len(comp)]], decompressedLengths=[[len(rawn)]],
             stageCounts=[src["stageCounts"][i]], compressedBlob=comp)
    for p in pf["m_SubShaders"][0]["m_Passes"]:
        p["m_Platforms"] = [4]
        p["m_EditorDataHash"] = p["m_EditorDataHash"][i:i + 1]
    shader_bytes = to_bytes(d, shader_node)
    name = "cadlamps_dx11"
    ab = {"m_Name": name,
          "m_PreloadTable": [{"m_FileID": 0, "m_PathID": 2}],
          "m_Container": [("assets/cadlamps.shader", {"preloadIndex": 0, "preloadSize": 1, "asset": {"m_FileID": 0, "m_PathID": 2}})],
          "m_MainAsset": {"preloadIndex": 0, "preloadSize": 0, "asset": {"m_FileID": 0, "m_PathID": 0}},
          "m_RuntimeCompatibility": 1, "m_AssetBundleName": name, "m_Dependencies": [],
          "m_IsStreamedSceneAssetBundle": False, "m_ExplicitDataLayout": 0, "m_PathFlags": 7, "m_SceneHashes": []}
    ab_bytes = to_bytes({c.m_Name: ab[c.m_Name] for c in ab_node.m_Children}, ab_node)
    path = os.path.join(out, name + ".bundle")
    with open(path, "wb") as f:
        f.write(unity_fs("CAB-" + name, serialized_file(shader_bytes, shader_node, ab_bytes, ab_node, shader_hash)))
    for o in UnityPy.load(path).objects:
        if o.type.name == "Shader":
            r = o.read_typetree()
            raw2 = lz4.block.decompress(bytes(r["compressedBlob"]), uncompressed_size=r["decompressedLengths"][0][0])
            print(name, "reread ok: name", r["m_ParsedForm"]["m_Name"], "passes", len(r["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"]), "platforms", r["platforms"], "same bytes", raw2 == rawn, "size", os.path.getsize(path))


def main():
    data_dir, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    vs = compile_hlsl(VS, b"vs_4_0")
    ps_g = compile_hlsl(PS_GBUFFER, b"ps_4_0")
    ps_m = compile_hlsl(PS_MASK, b"ps_4_0")
    ps_d = compile_hlsl(PS_DRAW, b"ps_4_0")
    ps_a = compile_hlsl(PS_AMBIENT, b"ps_4_0")
    ps_s = compile_hlsl(PS_SHADE, b"ps_4_0")
    ps_st = compile_hlsl(PS_STABLE, b"ps_4_0")
    extra = [(ps_m, 4), (ps_d, 3), (ps_a, 1), (ps_s, 5), (ps_st, 4)]
    for src_text, n_tex in ((PS_GLOW_EXTRACT, 2), (PS_GLOW_DOWN, 1), (PS_GLOW_UP, 3), (PS_GLOW_ADD, 2), (PS_FADE, 2), (PS_FADE_ARRAY, 2), (PS_EXPOSE, 2), (PS_DITHER, 3)):
        extra.append((compile_hlsl(src_text, b"ps_4_0"), n_tex))
    print("compiled: VS", len(vs), "gbuffer", len(ps_g), "mask", len(ps_m), "draw", len(ps_d), "ambient", len(ps_a), "shade", len(ps_s), "stable", len(ps_st))
    env = UnityPy.load(os.path.join(data_dir, "resources.assets"))
    sf = list(env.files.values())[0]
    shader_hash = next(t.old_type_hash for t in sf.types if t.class_id == 48)
    shader_node = normalize(get_typetree_node(48, VER))
    ab_node = normalize(get_typetree_node(142, VER))
    src = None
    for o in env.objects:
        if o.type.name == "Shader":
            d = o.read_typetree()
            if (d.get("m_ParsedForm") or {}).get("m_Name") == "Unlit/Texture":
                src = d
                break
    blob = bytes(src["compressedBlob"])
    parts = []
    for i, plat in enumerate(src["platforms"]):
        o0, c0, d0 = src["offsets"][i][0], src["compressedLengths"][i][0], src["decompressedLengths"][i][0]
        parts.append((plat, blob[o0:o0 + c0], d0, src["stageCounts"][i]))

    for tag, keep in (("dx11", [4]), ("full", None)):
        d = copy.deepcopy(src)
        pf = d["m_ParsedForm"]
        pf["m_Name"] = NAME
        pf["m_PropInfo"] = {"m_Props": []}
        new_blob, offs, cl, dl, plats, stages = b"", [], [], [], [], []
        for plat, comp, dlen, st in parts:
            if keep is not None and plat not in keep:
                continue
            if plat == 4:
                rawn = rebuild_dx11(lz4.block.decompress(comp, uncompressed_size=dlen), vs, ps_g, extra)
                comp = lz4.block.compress(rawn, store_size=False, mode="high_compression")
                dlen = len(rawn)
            offs.append([len(new_blob)])
            cl.append([len(comp)])
            dl.append([dlen])
            plats.append(plat)
            stages.append(st)
            new_blob += comp
        d.update(platforms=plats, offsets=offs, compressedLengths=cl, decompressedLengths=dl, stageCounts=stages,
                 compressedBlob=new_blob)
        p0 = pf["m_SubShaders"][0]["m_Passes"][0]
        p0["m_Platforms"] = plats
        p0["m_EditorDataHash"] = p0["m_EditorDataHash"][:len(plats)]
        st = p0["m_State"]
        st["zTest"]["val"] = 8.0
        st["zWrite"]["val"] = 0.0
        st["culling"]["val"] = 0.0
        p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13 = [copy.deepcopy(p0) for _ in range(13)]
        names = ((p0, ["_CameraDepthTexture", "_OLParams", "_OLGB0", "_OLGB1", "_OLGB2"]),
                 (p1, ["_CameraDepthTexture", "_OLParams", "_OLFlat", "_CameraGBufferTexture0"]),
                 (p2, ["_OLParams", "_OLMask", "_OLNoLine"]),
                 (p3, ["_OLParams"]),
                 (p4, ["_CameraDepthTexture", "_OLParams", "_OLSrc", "_CameraGBufferTexture0", "_CameraGBufferTexture2"]),
                 (p5, ["_CameraDepthTexture", "_OLParams", "_OLMask", "_OLHist"]),
                 (p6, ["_OLParams", "_OLGlowSrc"]),
                 (p7, ["_OLGlowA"]),
                 (p8, ["_OLParams", "_OLGlowA", "_OLGlowB"]),
                 (p9, ["_OLParams", "_OLGlowA"]),
                 (p10, ["_OLFadeP", "_OLFadeSrc"]),
                 (p11, ["_OLFadeP", "_OLFadeSrc"]),
                 (p12, ["_OLParams", "_OLSrc"]),
                 (p13, ["_OLParams", "_OLSrc", "_OLNoise"]))
        for p, nm in names:
            p["m_NameIndices"] = [(n, i) for i, n in enumerate(nm)]
            p["progFragment"]["m_CommonParameters"]["m_TextureParams"] = tex_params(nm, ("_OLFadeSrc",) if p is p11 else ())
        for p, blobs in ((p1, {6: 8, 7: 9}), (p2, {6: 10, 7: 11}), (p3, {6: 12, 7: 13}), (p4, {6: 14, 7: 15}), (p5, {6: 16, 7: 17}), (p6, {6: 18, 7: 19}), (p7, {6: 20, 7: 21}), (p8, {6: 22, 7: 23}), (p9, {6: 24, 7: 25}), (p10, {6: 26, 7: 27}), (p11, {6: 28, 7: 29}), (p12, {6: 30, 7: 31}), (p13, {6: 32, 7: 33})):
            for lst in p["progFragment"]["m_PlayerSubPrograms"]:
                for e in lst:
                    e["m_BlobIndex"] = blobs[e["m_BlobIndex"]]
        for p, src_b, dst_b in ((p2, 5.0, 10.0), (p3, 0.0, 3.0), (p9, 1.0, 1.0)):   # alpha over; multiply by the shader's color; add
            b0 = p["m_State"]["rtBlend0"]
            b0["srcBlend"]["val"], b0["destBlend"]["val"] = src_b, dst_b
            b0["srcBlendAlpha"]["val"], b0["destBlendAlpha"]["val"] = 0.0, 1.0
        pf["m_SubShaders"][0]["m_Passes"] = [p0, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13]
        shader_bytes = to_bytes(d, shader_node)
        ab = {"m_Name": "cadstyle_" + tag,
              "m_PreloadTable": [{"m_FileID": 0, "m_PathID": 2}],
              "m_Container": [("assets/cadstyle.shader", {"preloadIndex": 0, "preloadSize": 1, "asset": {"m_FileID": 0, "m_PathID": 2}})],
              "m_MainAsset": {"preloadIndex": 0, "preloadSize": 0, "asset": {"m_FileID": 0, "m_PathID": 0}},
              "m_RuntimeCompatibility": 1, "m_AssetBundleName": "cadstyle_" + tag, "m_Dependencies": [],
              "m_IsStreamedSceneAssetBundle": False, "m_ExplicitDataLayout": 0, "m_PathFlags": 7, "m_SceneHashes": []}
        ab_bytes = to_bytes({c.m_Name: ab[c.m_Name] for c in ab_node.m_Children}, ab_node)
        path = os.path.join(out, f"cadstyle_{tag}.bundle")
        with open(path, "wb") as f:
            f.write(unity_fs("CAB-cadstyle" + tag, serialized_file(shader_bytes, shader_node, ab_bytes, ab_node, shader_hash)))
        chk = UnityPy.load(path)
        for o in chk.objects:
            r = o.read_typetree()
            if o.type.name == "Shader":
                i = r["platforms"].index(4)
                b2 = bytes(r["compressedBlob"])
                raw2 = lz4.block.decompress(b2[r["offsets"][i][0]:r["offsets"][i][0] + r["compressedLengths"][i][0]],
                                            uncompressed_size=r["decompressedLengths"][i][0])
                ps = r["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"]
                print(tag, "reread ok: passes", len(ps), "platforms", r["platforms"], "code present", vs in raw2, ps_g in raw2, ps_m in raw2, ps_d in raw2, all(x[0] in raw2 for x in extra),
                      "blend2", ps[2]["m_State"]["rtBlend0"]["srcBlend"]["val"], "blend3", ps[3]["m_State"]["rtBlend0"]["destBlend"]["val"],
                      "size", os.path.getsize(path))
    build_lamp_bundle(data_dir, out, shader_node, ab_node, shader_hash)


main()
