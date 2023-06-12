cbuffer ConstantBuffer : register(b0)
{
    row_major matrix World;
    row_major matrix View;
    row_major matrix Projection;
    float padding;
}

struct VertexInputType
{
    float3 position : POSITION;
};

struct PixelInputType
{
    float4 position : SV_POSITION;
};

PixelInputType VS(float4 pos : POSITION)
{
    PixelInputType output;
    output.position = mul(mul(mul(pos, World), View), Projection);
    return output;
}

float4 PS() : SV_TARGET
{
    return float4(1.0f, 0.0f, 0.0f, 1.0f);
}