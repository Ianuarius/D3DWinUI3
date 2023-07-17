Texture2D tex1 : register(t0);
Texture2D tex2 : register(t1);
SamplerState sam : register(s0);

struct PixelInput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4 PS(PixelInput input) : SV_TARGET
{
    float2 uv = input.uv;
    
    float4 color1 = tex1.Sample(sam, uv);
    float4 color2 = tex2.Sample(sam, uv);
    
    float4 finalColor = color2 * color2.a + color1 * (1 - color2.a);
    return finalColor;
}