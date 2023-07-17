cbuffer ConstantBuffer : register(b0)
{
    float4x4 WorldViewProjection;
    float4x4 World;
    float4 BrushColor;
    float2 ClickPosition;
}

Texture2D BrushTexture : register(t0);
SamplerState BrushSampler : register(s0);

struct PixelInput
{
    float4 position : SV_POSITION;
    float3 world : POSITION0;
    float2 texCoord : TEXCOORD;
};

float4 PS(PixelInput input) : SV_TARGET
{
    float alpha = 1.0f - BrushTexture.Sample(BrushSampler, input.texCoord).r;
    return float4(BrushColor.rgb * alpha, alpha);
}