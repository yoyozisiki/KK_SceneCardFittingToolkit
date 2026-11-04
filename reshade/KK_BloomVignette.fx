// KK_BloomVignette.fx
//
// 泛光 + 暗角，一个 technique 搞定。参数和 python_tools/6_视频后期/postfx.py
// 里那套 ffmpeg 滤镜**对齐**，方便两边出片直接对比。
//
// 暗角公式照抄 ffmpeg 的 vignette：t = 到中心的像素距离 / 半对角线，
// 亮度乘 cos(t*angle)^4，angle 由「四角保留比例」反推（a = acos(corner^0.25)）。
// 这样两边的曲线是同一族，不是各调各的。
//
// 泛光：提亮部 → 降到 1/4 → 两次可分离高斯 → screen 混合。
// 和 ffmpeg 那条链（colorlevels → scale → boxblur → blend=screen）等价，
// 只是这里是 HDR(RGBA16F) 缓冲，高光滚降比 8bit 的 boxblur 自然。

#include "ReShade.fxh"

// ------------------------------------------------------------------ 参数
uniform float BloomThreshold <
    ui_type = "slider";
    ui_min = 0.0; ui_max = 1.0; ui_step = 0.01;
    ui_label = "泛光 阈值";
    ui_tooltip = "低于这个亮度的部分不参与泛光。越高光晕越克制（中档 0.62）";
> = 0.62;

uniform float BloomIntensity <
    ui_type = "slider";
    ui_min = 0.0; ui_max = 2.0; ui_step = 0.01;
    ui_label = "泛光 强度";
    ui_tooltip = "screen 混合的比例。0 = 关（中档 0.35）";
> = 0.35;

uniform float BloomRadius <
    ui_type = "slider";
    ui_min = 0.0; ui_max = 12.0; ui_step = 0.5;
    ui_label = "泛光 半径";
    ui_tooltip = "在 1/4 缓冲上的采样步长，视觉半径约等于这个值 ×4（中档 4）";
> = 4.0;

uniform float VignetteStrength <
    ui_type = "slider";
    ui_min = 0.0; ui_max = 1.0; ui_step = 0.01;
    ui_label = "暗角 强度";
    ui_tooltip = "0 = 关。0.40≈四角留 58%，0.74≈37%，0.98≈17%";
> = 0.735;

// ------------------------------------------------------------------ 缓冲
texture texDown {
    Width = BUFFER_WIDTH / 4;
    Height = BUFFER_HEIGHT / 4;
    Format = RGBA16F;
};

texture texBlur {
    Width = BUFFER_WIDTH / 4;
    Height = BUFFER_HEIGHT / 4;
    Format = RGBA16F;
};

sampler sDown { Texture = texDown; };
sampler sBlur { Texture = texBlur; };

// 5 抽头高斯（对称展开成 9 抽头），权重和为 1
static const float GAUSS[5] = {
    0.2270270, 0.1945946, 0.1216216, 0.0540540, 0.0162162
};

// ------------------------------------------------------------ pass 1：提亮部
float4 PS_Down(float4 vpos : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
    // 2x2 四点盒式降采样，顺手把闪烁压掉
    float3 c = 0.0;
    c += tex2D(ReShade::BackBuffer, uv + float2(-0.5, -0.5) * ReShade::PixelSize).rgb;
    c += tex2D(ReShade::BackBuffer, uv + float2( 0.5, -0.5) * ReShade::PixelSize).rgb;
    c += tex2D(ReShade::BackBuffer, uv + float2(-0.5,  0.5) * ReShade::PixelSize).rgb;
    c += tex2D(ReShade::BackBuffer, uv + float2( 0.5,  0.5) * ReShade::PixelSize).rgb;
    c *= 0.25;

    // 阈值：低于阈值归零，其余拉伸到 0..1。和 colorlevels=rimin=THR 一致
    c = max(c - BloomThreshold, 0.0) / max(1.0 - BloomThreshold, 0.001);
    return float4(c, 1.0);
}

// ------------------------------------------------------------ pass 2：横向模糊
float4 PS_BlurH(float4 vpos : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
    // 缓冲是 1/4 尺寸，所以步长要 ×4
    float2 step = ReShade::PixelSize * 4.0 * BloomRadius;
    float3 acc = tex2D(sDown, uv).rgb * GAUSS[0];
    [unroll]
    for (int i = 1; i < 5; i++)
    {
        float o = i * BloomRadius;
        acc += tex2D(sDown, uv + float2(step.x * i, 0.0)).rgb * GAUSS[i];
        acc += tex2D(sDown, uv - float2(step.x * i, 0.0)).rgb * GAUSS[i];
    }
    return float4(acc, 1.0);
}

// ------------------------------------------------------------ pass 3：纵向模糊
float4 PS_BlurV(float4 vpos : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
    float2 step = ReShade::PixelSize * 4.0 * BloomRadius;
    float3 acc = tex2D(sBlur, uv).rgb * GAUSS[0];
    [unroll]
    for (int i = 1; i < 5; i++)
    {
        acc += tex2D(sBlur, uv + float2(0.0, step.y * i)).rgb * GAUSS[i];
        acc += tex2D(sBlur, uv - float2(0.0, step.y * i)).rgb * GAUSS[i];
    }
    return float4(acc, 1.0);
}

// ------------------------------------------------------- pass 4：合成 + 暗角
float4 PS_Combine(float4 vpos : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
    float3 c = tex2D(ReShade::BackBuffer, uv).rgb;

    if (BloomIntensity > 0.0)
    {
        float3 glow = tex2D(sDown, uv).rgb * BloomIntensity;
        c = 1.0 - (1.0 - c) * (1.0 - glow);        // screen 混合
    }

    if (VignetteStrength > 0.0)
    {
        // 四角保留比例 → 角度：corner = cos(angle)^4，四角处 t = 1
        float corner = clamp(1.0 - 0.85 * VignetteStrength, 0.01, 1.0);
        float angle = acos(pow(corner, 0.25));

        float2 halfSize = float2(BUFFER_WIDTH, BUFFER_HEIGHT) * 0.5;
        float2 p = (uv - 0.5) * float2(BUFFER_WIDTH, BUFFER_HEIGHT);
        float t = clamp(length(p) / length(halfSize), 0.0, 1.0);
        c *= pow(cos(t * angle), 4.0);
    }

    return float4(c, 1.0);
}

technique KK_BloomVignette <
    ui_label = "泛光 + 暗角";
    ui_tooltip = "参数和 6_视频后期 的 ffmpeg 滤镜对齐";
>
{
    pass Down    { VertexShader = PostProcessVS; PixelShader = PS_Down;    RenderTarget = texDown; }
    pass BlurH   { VertexShader = PostProcessVS; PixelShader = PS_BlurH;   RenderTarget = texBlur; }
    pass BlurV   { VertexShader = PostProcessVS; PixelShader = PS_BlurV;   RenderTarget = texDown; }
    pass Combine { VertexShader = PostProcessVS; PixelShader = PS_Combine; }
}
