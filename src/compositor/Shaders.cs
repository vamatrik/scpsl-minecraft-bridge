namespace SlBridge.Compositor
{
    internal static class Shaders
    {
        public const string Source = @"
Texture2D SlTex : register(t0);
Texture2D McTex : register(t1);
Texture2D RefTex : register(t2);
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
    float2 Cursor;      // курсор в пикселях окна (окна Minecraft)
    float CursorScale;  // 0 — не рисовать
    float KeyRelBrightness; // пиксель темнее фона «сейчас» в столько раз (по ключевому каналу) — уже не фон
    float3 _pad;
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
float Lum(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
bool Same(float3 a, float3 b)
{
    float la = Lum(a), lb = Lum(b);
    return length(Hue(a) - Hue(b)) < 0.12 && la > lb * 0.6 && la < lb * 1.6 + 0.01;
}

// Отдельный проход в текстуру 1×1 раз в кадр: выбрать образец фона (одинаков для всех пикселей — не считать его 2 млн раз).
float4 RefPS(V i) : SV_Target
{
    // Эффекты SL (SCP-268, затемнение, ослепление, низкое HP) перекрашивают и затемняют весь кадр — фон перестаёт
    // быть «эталонным зелёным». Поэтому берём образцы фона прямо из кадра (края и кольцо ближе к центру, без низа,
    // где оружие и интерфейс) и выбираем самый «типичный» — тот, на который похоже больше всего других образцов.
    // Всё, что похоже на него по оттенку и яркости, тоже фон.
    static const float2 refPts[12] = { float2(0.5, 0.04), float2(0.04, 0.5), float2(0.96, 0.5), float2(0.25, 0.04),
                                       float2(0.75, 0.04), float2(0.04, 0.25), float2(0.96, 0.25), float2(0.5, 0.25),
                                       float2(0.3, 0.35), float2(0.7, 0.35), float2(0.25, 0.5), float2(0.75, 0.5) };
    float3 smp[12];
    [unroll] for (int j = 0; j < 12; j++) smp[j] = SlTex.SampleLevel(Lin, SlUv.xy + refPts[j] * SlUv.zw, 0).rgb;
    int best = 0; int bestN = -1;
    [unroll] for (int a = 0; a < 12; a++)
    {
        int n = 0;
        [unroll] for (int b = 0; b < 12; b++) n += Same(smp[a], smp[b]) ? 1 : 0;
        if (n > bestN) { bestN = n; best = a; }
    }
    return float4(smp[best], bestN);
}

float4 PS(V i) : SV_Target
{
    // Окно Minecraft открыто (верстак, инвентарь): показываем Minecraft целиком, без ключа.
    // Оверлей остаётся поверх — так рабочий стол всегда собирает DWM и захват экрана (OBS) не чернеет.
    if (ShowMask > 1.5)
    {
        float3 mc = McTex.Sample(Lin, McUv.xy + i.uv * McUv.zw).rgb;
        if (CursorScale > 0)
        {
            // стрелка-курсор: белая с чёрной обводкой, острие — в точке курсора
            float2 d = (i.pos.xy - Cursor) / CursorScale;
            if (d.x >= 0 && d.y >= 0 && d.y <= 17 && d.x <= d.y * 0.62 + 0.5 && d.x <= 11)
            {
                bool edge = d.x < 1.2 || d.x > d.y * 0.62 - 1.0 || d.y > 15.8 || d.x > 9.8;
                return float4(edge ? float3(0, 0, 0) : float3(1, 1, 1), 1);
            }
        }
        return float4(mc, 1);
    }
    float2 slUv = SlUv.xy + i.uv * SlUv.zw;
    float3 s = SlTex.Sample(Lin, slUv).rgb;

    // образец фона «сейчас» (см. RefPS): rgb — цвет, a — сколько из 12 образцов с ним согласны
    float4 refT = RefTex.Load(int3(0, 0, 0));
    float3 refC = refT.rgb;
    int bestN = (int)(refT.a + 0.5);
    // Ключ по «преобладанию» ключевого канала над остальными (для зелёного: g над max(r,b)),
    // делённому на яркость, — освещение, туман и пересвет SL на него почти не влияют.
    float3 kc = KeyColor;
    float keyCh = dot(s, kc);                                    // ключевой канал
    float other = max(max(s.r * (1 - kc.r), s.g * (1 - kc.g)), s.b * (1 - kc.b));
    float dom = (keyCh - other) / max(keyCh, 1e-3);
    float k = saturate((dom - KeyThreshold) / max(KeySoftness, 1e-3));
    k *= saturate((keyCh - KeyMinBrightness) / 0.04);
    // Отсечка по яркости относительно фона «сейчас»: фон яркий, а тёмный металл с зеленоватым бликом —
    // нет. Пиксель вдвое темнее фона (по зелёному каналу) фоном не считается, каким бы зелёным он ни был.
    float rel = 1.0;
    if (bestN >= 6 && KeyRelBrightness > 0)
    {
        float refK = dot(refC, kc);
        rel = saturate((keyCh - refK * KeyRelBrightness) / max(refK * 0.12, 0.01));
    }
    // фон «сейчас»: половина образцов согласна, что это он, и он не чёрный (маска очков SCP-1344 — не фон)
    if (AdaptiveKey > 0.5 && bestN >= 6 && Lum(refC) > 0.012)
    {
        float dist = length(Hue(s) - Hue(refC));
        float lr = Lum(s) / max(Lum(refC), 1e-3);
        float kr = (1.0 - saturate((dist - 0.05) / 0.08)) * saturate((lr - 0.45) / 0.15) * saturate((2.0 - lr) / 0.3);
        // серый/тёмный металл почти бесцветен, а фон насыщенный — не путаем их по одному оттенку
        float satS = (max(s.r, max(s.g, s.b)) - min(s.r, min(s.g, s.b))) / max(max(s.r, max(s.g, s.b)), 1e-3);
        float satR = (max(refC.r, max(refC.g, refC.b)) - min(refC.r, min(refC.g, refC.b))) / max(max(refC.r, max(refC.g, refC.b)), 1e-3);
        kr *= saturate((satS - satR * 0.6) / max(satR * 0.15, 0.02));
        k = max(k, kr);
    }
    k *= rel;
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
