namespace M1vClock.Benchmarks.Gpu;

internal static class SceneShaders
{
    public const string Hlsl = @"
cbuffer Frame : register(b0)
{
    row_major float4x4 ViewProj;
    row_major float4x4 LightViewProj;
    row_major float4x4 InvViewProj;
    float3 CamPos; float Time;
    float3 SunDir; float Pad;
};
StructuredBuffer<float4> Trees : register(t1);
Texture2D<float> ShadowMap : register(t0);
SamplerComparisonState ShadowSampler : register(s0);
Texture2D<float4> Hdr : register(t2);
SamplerState LinearSampler : register(s1);

struct VSIn { float3 pos : POSITION; float3 nrm : NORMAL; float mat : TEXCOORD0; uint iid : SV_InstanceID; };
struct PSIn { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 nrm : NORMAL; float mat : TEXCOORD1; };

float3 TreeWorld(float3 p, uint iid, out float3x3 rot)
{
    float4 t = Trees[iid];
    float a = frac(sin(iid * 12.9898) * 43758.5453) * 6.2831;
    float c = cos(a), s = sin(a);
    rot = float3x3(c, 0, -s, 0, 1, 0, s, 0, c);
    float sway = sin(Time * 1.3 + iid) * 0.03 * p.y;   // vento
    return mul(p, rot) * t.w + t.xyz + float3(sway, 0, sway * 0.5);
}

PSIn TerrainVS(VSIn i)
{
    PSIn o; o.wpos = i.pos; o.nrm = i.nrm; o.mat = i.mat;
    o.pos = mul(float4(i.pos, 1), ViewProj);
    return o;
}

PSIn TreeVS(VSIn i)
{
    PSIn o; float3x3 rot;
    o.wpos = TreeWorld(i.pos, i.iid, rot);
    o.nrm = mul(i.nrm, rot); o.mat = i.mat;
    o.pos = mul(float4(o.wpos, 1), ViewProj);
    return o;
}

float4 TerrainShadowVS(VSIn i) : SV_POSITION { return mul(float4(i.pos, 1), LightViewProj); }
float4 TreeShadowVS(VSIn i) : SV_POSITION { float3x3 r; return mul(float4(TreeWorld(i.pos, i.iid, r), 1), LightViewProj); }

float3 Sky(float3 dir)
{
    float up = saturate(dir.y);
    float3 col = lerp(float3(0.42, 0.42, 0.42), float3(0.12, 0.12, 0.12), pow(up, 0.6));
    float sun = saturate(dot(dir, -SunDir));
    col += pow(sun, 600) * 12 + pow(sun, 12) * 0.25;
    return col;
}

float Shadow(float3 wpos, float3 n)
{
    float4 lp = mul(float4(wpos + n * 0.15, 1), LightViewProj);
    float2 uv = lp.xy * float2(0.5, -0.5) + 0.5;
    if (any(uv < 0) || any(uv > 1)) return 1;
    float sum = 0;
    [unroll] for (int y = -2; y <= 2; y++)
        [unroll] for (int x = -2; x <= 2; x++)
            sum += ShadowMap.SampleCmpLevelZero(ShadowSampler, uv + float2(x, y) / 4096.0, lp.z - 0.0015);
    return sum / 25;
}

float3 Shade(float3 wpos, float3 n, float albedo)
{
    float3 l = -SunDir;
    float ndl = saturate(dot(n, l));
    float3 direct = ndl * Shadow(wpos, n) * 1.5;
    float3 ambient = lerp(0.06, 0.16, n.y * 0.5 + 0.5);
    float3 col = albedo * (direct + ambient);
    float dist = length(wpos - CamPos);
    float fog = 1 - exp(-dist * 0.0022);
    return lerp(col, Sky(normalize(wpos - CamPos)), fog);
}

float4 TerrainPS(PSIn i) : SV_TARGET
{
    float3 n = normalize(i.nrm);
    float slope = 1 - n.y;
    float h = i.wpos.y;
    float albedo = lerp(0.22, 0.15, saturate((h - 8) / 25));               // grama
    albedo = lerp(albedo, 0.28, saturate((slope - 0.25) * 4));             // rocha nas encostas
    albedo = lerp(albedo, 0.70, saturate((h - 62) / 10) * saturate(1 - slope * 3)); // neve só nos picos
    albedo *= 0.9 + 0.1 * frac(sin(dot(floor(i.wpos.xz * 2), float2(12.9898, 78.233))) * 43758.5453);
    return float4(Shade(i.wpos, n, albedo), 1);
}

float4 TreePS(PSIn i) : SV_TARGET
{
    float albedo = i.mat < 1.5 ? 0.08 : 0.11;
    return float4(Shade(i.wpos, normalize(i.nrm), albedo), 1);
}

PSIn WaterVS(VSIn i)
{
    PSIn o; o.wpos = i.pos; o.nrm = float3(0, 1, 0); o.mat = 0;
    o.pos = mul(float4(i.pos, 1), ViewProj);
    return o;
}

float4 WaterPS(PSIn i) : SV_TARGET
{
    float2 p = i.wpos.xz;
    float3 n = float3(0, 1, 0);
    [unroll] for (int k = 0; k < 6; k++)           // ondas somadas
    {
        float2 d = float2(cos(k * 1.7), sin(k * 1.7));
        float f = 0.15 * (k + 1), ph = dot(d, p) * f + Time * (1 + k * 0.4);
        n.xz += d * cos(ph) * 0.06 / (k + 1);
    }
    n = normalize(n);
    float3 v = normalize(i.wpos - CamPos);
    float fres = 0.04 + 0.96 * pow(1 - saturate(dot(-v, n)), 5);
    float3 refl = Sky(reflect(v, n)) * Shadow(i.wpos, n);
    float3 col = lerp(float3(0.05, 0.05, 0.05), refl, fres);
    float fog = 1 - exp(-length(i.wpos - CamPos) * 0.0022);
    return float4(lerp(col, Sky(v), fog), 1);
}

struct FsOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
FsOut FullscreenVS(uint id : SV_VertexID)
{
    FsOut o; o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 1, 1);
    return o;
}

float4 SkyPS(FsOut i) : SV_TARGET
{
    float4 w = mul(float4(i.uv * float2(2, -2) + float2(-1, 1), 1, 1), InvViewProj);
    return float4(Sky(normalize(w.xyz / w.w - CamPos)), 1);
}

float4 PostPS(FsOut i) : SV_TARGET
{
    float3 c = Hdr.Sample(LinearSampler, i.uv).rgb;
    c = c * (2.51 * c + 0.03) / (c * (2.43 * c + 0.59) + 0.14);         // ACES
    float2 d = i.uv - 0.5;
    c *= 1 - dot(d, d) * 0.9;                                           // vinheta
    float g = pow(saturate(c.r), 1 / 2.2);
    return float4(g, g, g, 1);
}";
}
