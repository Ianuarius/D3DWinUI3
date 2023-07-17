struct VertexInput
{
    float3 position : POSITION0;
    float2 uv : TEXCOORD0;
};

struct VertexOutput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

VertexOutput VS(VertexInput input)
{
    VertexOutput output;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}