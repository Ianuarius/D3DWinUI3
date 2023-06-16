cbuffer ConstantBuffer : register(b0)
{
    float4x4 WorldViewProjection;
    float4x4 World;
    float4 LightPosition;
}

struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct VertexOutput
{
    float4 position : SV_POSITION;
    float3 world : POSITION0;
    float3 normal : NORMAL;
};

VertexOutput VS(VertexInput input)
{
    float3x3 rotation = (float3x3) World;
    float4 position = float4(input.position, 1.0f);
    
    VertexOutput output;
    output.position = mul(WorldViewProjection, position);
    output.world = mul(World, position).xyz;
    output.normal = normalize(mul(rotation, input.normal));
    return output;
}