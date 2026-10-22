namespace M1vClock.Benchmarks.Gpu;

internal static class BurnShaders
{
    public const string Hlsl = @"
cbuffer CB : register(b0)
{
    row_major float4x4 World;
    row_major float4x4 ViewProj;
    float Time; float ShellCount; float FurLength; float Iterations;
};

struct VSIn  { float3 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; uint inst : SV_InstanceID; };
struct PSIn  { float4 pos : SV_POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; float shell : TEXCOORD1; };

PSIn VS(VSIn i)
{
    PSIn o;
    float s = i.inst / ShellCount;                       // 0 = raiz, 1 = ponta
    float3 p = i.pos + i.nrm * (FurLength * s);
    float3 w = mul(float4(p, 1), World).xyz;
    w.y -= 0.10 * s * s;                                  // gravidade
    w   += 0.025 * s * sin(Time * 2.7 + i.pos.yzx * 7.0); // vento
    o.pos = mul(float4(w, 1), ViewProj);
    o.nrm = normalize(mul(float4(i.nrm, 0), World).xyz);
    o.uv = i.uv;
    o.shell = s;
    return o;
}

float hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float4 PS(PSIn i) : SV_TARGET
{
    float h = hash(floor(i.uv * float2(900, 380)));

    // Carga de ALU: cadeia de FMAs vetoriais (o que mais consome energia na GPU), antes do descarte
    // e ligada a ele, para o compilador não poder pular o cálculo.
    float4 a = float4(h, i.uv, i.shell), b = a.wzyx + 0.5;
    [loop] for (int k = 0; k < (int)Iterations; k++)
    {
        a = mad(a, 1.0001, b * 0.0003);
        b = mad(b, 0.9997, a * 0.0002);
    }
    float acc = frac(dot(a, b));

    if (i.shell > 0 && h + acc * 1e-6 < i.shell) discard; // fios afinam até a ponta

    float3 L = normalize(float3(-0.4, 0.8, -0.5));
    float diff = saturate(dot(i.nrm, L)) * 0.75 + 0.25;
    float ao = lerp(0.18, 1.0, i.shell);                  // raiz escura, ponta clara
    float c = diff * ao * (0.9 + 0.1 * acc);
    return float4(c, c, c, lerp(1.0, 0.55, i.shell));     // pontas translúcidas
}";
}
