cbuffer ConstantBuffer : register(b0)
{
    float4x4 WorldViewProjection;
    float4x4 World;
    float4 BrushColor;
    float2 ClickPosition;
}

struct VertexInput
{
    float3 position : POSITION0;
    float2 texCoord : TEXCOORD0;
    float2 instancePos : POSITION1;
    float2 instanceScale : TEXCOORD1;
};

struct VertexOutput
{
    float4 position : SV_POSITION;
    float3 world : POSITION0;
    float2 texCoord : TEXCOORD;
};

VertexOutput VS(VertexInput input)
{
    VertexOutput output;
    float3 scaledPos = float3(input.position.x * input.instanceScale.x, input.position.y * input.instanceScale.y, input.position.z);
    float4 worldPos = float4(scaledPos + float3(input.instancePos.x, input.instancePos.y, 0.0), 1.0);
    output.position = mul(worldPos, WorldViewProjection);
    output.texCoord = input.texCoord;
    return output;
}