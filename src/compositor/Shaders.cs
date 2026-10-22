namespace SlBridge.Compositor
{
    internal static class Shaders
    {
        public const string Source = @"
Texture2D SlTex : register(t0);
Texture2D McTex : register(t1);
SamplerState Lin : register(s0);

cbuffer P : register(b0)
{
    float3 KeyColor;
    float KeyThreshold;
    float KeySoftness;
    float KeyMinBrightness;
    float ShowMask;
    float HasMc;
    float4 SlUv;   // xy — смещение, zw — масштаб: клиентская часть окна внутри захваченного кадра
    float4 McUv;
    float AdaptiveKey;
    float3 _pad2;
};

struct V { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

V VS(uint id : SV_VertexID)
{
    V o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.uv = uv;
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float Dom(float3 c, float3 kc)
{
    float keyCh = dot(c, kc);
    float other = max(max(c.r * (1 - kc.r), c.g * (1 - kc.g)), c.b * (1 - kc.b));
    return (keyCh - other) / max(keyCh, 1e-3);
}

float3 Hue(float3 c) { return c / max(max(max(c.r, c.g), c.b), 1e-3); }

float4 PS(V i) : SV_Target
{
    float2 slUv = SlUv.xy + i.uv * SlUv.zw;
    float3 s = SlTex.Sample(Lin, slUv).rgb;

    // Эффекты SL (низкое HP, ослепление) обесцвечивают фон. Берём «эталон» фона из краёв кадра —
    // самую «ключевую» из точек — и вырезаем всё, что близко к нему по оттенку.
    static const float2 refPts[8] = { float2(0.5, 0.04), float2(0.04, 0.5), float2(0.96, 0.5), float2(0.25, 0.04),
                                      float2(0.75, 0.04), float2(0.04, 0.25), float2(0.96, 0.25), float2(0.5, 0.3) };
    float3 refC = float3(0, 0, 0);
    float refDom = -1;
    [unroll] for (int j = 0; j < 8; j++)
    {
        float3 c = SlTex.SampleLevel(Lin, SlUv.xy + refPts[j] * SlUv.zw, 0).rgb;
        float d = Dom(c, KeyColor);
        if (d > refDom) { refDom = d; refC = c; }
    }
    // Ключ по «преобладанию» ключевого канала над остальными (для зелёного: g над max(r,b)),
    // делённому на яркость, — освещение, туман и пересвет SL на него почти не влияют.
    float3 kc = KeyColor;
    float keyCh = dot(s, kc);                                    // ключевой канал
    float other = max(max(s.r * (1 - kc.r), s.g * (1 - kc.g)), s.b * (1 - kc.b));
    float dom = (keyCh - other) / max(keyCh, 1e-3);
    float k = saturate((dom - KeyThreshold) / max(KeySoftness, 1e-3));
    k *= saturate((keyCh - KeyMinBrightness) / 0.04);
    if (AdaptiveKey > 0.5 && refDom > 0.04 && refDom < KeyThreshold + KeySoftness)
    {
        float dist = length(Hue(s) - Hue(refC));
        float kr = 1.0 - saturate((dist - 0.05) / 0.08);
        kr *= saturate((dom - refDom * 0.5) / max(refDom * 0.3, 1e-3)); // и всё же «зеленее» оружия
        k = max(k, kr);
    }
    // Despill: убираем зелёный оттенок с полупрозрачного (дым, искры, края) — он становится серым.
    float3 despilled = s;
    float limit = other;
    if (kc.g > 0.5) despilled.g = min(s.g, limit);
    else if (kc.r > 0.5) despilled.r = min(s.r, limit);
    else despilled.b = min(s.b, limit);
    s = despilled;
    if (ShowMask > 0.5) return float4(k, k, k, 1);
    float3 m = HasMc > 0.5 ? McTex.Sample(Lin, McUv.xy + i.uv * McUv.zw).rgb : float3(0.05, 0.05, 0.08);
    return float4(lerp(s, m, k), 1);
}
";
    }
}
