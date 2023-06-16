cbuffer ConstantBuffer : register(b0)
{
    float4x4 WorldViewProjection;
    float4x4 World;
    float4 LightPosition;
}

struct PixelInput
{
    float4 position : SV_POSITION;
    float3 world : POSITION0;
    float3 normal : NORMAL;
};

float4 PS(PixelInput input) : SV_TARGET
{
    float3 normal = input.normal;
    float3 lightDir = normalize(LightPosition.xyz - input.world.xyz);
    float NdotL = max(0, dot(normal, lightDir));
    float4 color = float4(NdotL, NdotL, NdotL, 1.0f);
    return color;
}