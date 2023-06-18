cbuffer ConstantBuffer : register(b0)
{
    float4x4 WorldViewProjection;
    float4x4 World;
    float4 BrushColor;
    float2 ClickPosition;
}

struct VertexInput
{
    float3 position : POSITION;
    float2 texCoord : TEXCOORD;
};

struct VertexOutput
{
    float4 position : SV_POSITION;
    float3 world : POSITION0;
    float2 texCoord : TEXCOORD;
};

VertexOutput VS(VertexInput input)
{
    float4 position = float4(input.position, 1.0f);
    position.xy += ClickPosition;
    
    VertexOutput output;
    output.position = mul(WorldViewProjection, position);
    output.world = mul(World, position).xyz;
    output.texCoord = input.texCoord;
    return output;
}