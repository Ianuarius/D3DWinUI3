struct VertexInputType
{
    float3 position : POSITION;
};

struct PixelInputType
{
    float4 position : SV_POSITION;
};

PixelInputType VS(VertexInputType input)
{
    PixelInputType output;
    output.position = float4(input.position, 1.0);
    return output;
}

float4 PS() : SV_TARGET
{
    return float4(1.0f, 0.0f, 0.0f, 1.0f);
}