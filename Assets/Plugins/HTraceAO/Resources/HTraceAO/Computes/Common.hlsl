
// ------------------------ COMMON PROPERTIES -------------------------
float _HScaleFactorAO;


// ------------------------ EDGE STOPPING FUNCTIONS -------------------------
float ProbePlaneWeighting(float4 Plane, float3 WorldPosSample, float DepthLinearCenter, float Multiplier)
{
    float PlaneDistance = abs(dot(float4(WorldPosSample, -1), Plane));
    float DepthDifference = PlaneDistance / DepthLinearCenter;
    float PlaneWeight = exp2(-100.0f * Multiplier * (DepthDifference * DepthDifference));
    return PlaneWeight;
}


// ------------------------ PACKING FUNCTIONS -------------------------
uint PackOcclusionVelocity(float Occlusion, float Velocity)
{
    uint OcclusionPacked = uint(Occlusion * 255.0f + 0.5f) & 0xFF;
    uint VelocityPacked = uint(Velocity * 255.0f + 0.5f) & 0xFF;
    return (VelocityPacked << 8) | OcclusionPacked; 
}

float2 UnpackOcclusionVelocity(uint PackedData)
{
    float2 UnpackedData;
    UnpackedData.x = (PackedData & 0xFF) / 255.0f;
    UnpackedData.y = ((PackedData >> 8) & 0xFF) / 255.0f;

    return UnpackedData; 
}

uint PackOcclusion(float Occlusion)
{
    return uint(Occlusion * 255.0f + 0.5f) & 0xFF;
}

float UnpackOcclusion(uint OcclusionPacked)
{
    return (OcclusionPacked & 0xFF) / 255.0f;
}

uint PackTemporalData(float Occlusion, float SampleCount, float Depth)
{
    uint OcclusionPacked = uint(Occlusion * 2047.0f + 0.5f) & 0x7FF;
    uint SampleCountPacked = uint(SampleCount) & 0x1F;
    uint DepthPacked = uint(f32tof16(Depth));

    return (DepthPacked << 16) | (SampleCountPacked << 11) | OcclusionPacked;
}

float3 UnpackTemporalData(uint PackedData)
{
    float3 UnpackedData;
    UnpackedData.x = (PackedData & 0x7FF) / 2047.0f;            // Occlusion
    UnpackedData.y = (PackedData >> 11) & 0x1F;                 // SampleCount
    UnpackedData.z = f16tof32((PackedData >> 16) & 0xFFFF);     // Depth
    
    return UnpackedData;
}

uint PackSpatialData(float Occlusion, float Depth, float3 Normal)
{
    uint OcclusionPacked = uint(Occlusion * 255.0f + 0.5f) & 0xFF;
    uint DepthPacked = uint(f32tof16(Depth)) & 0x7FFF;

    Normal = Normal * 0.5f + 0.5f;
    uint3 NormalPacked;
    NormalPacked.x = uint(Normal.x * 7.0f + 0.5f) & 0x7; // 00000000111000000000000000000000
    NormalPacked.y = uint(Normal.y * 7.0f + 0.5f) & 0x7; // 00000000000111000000000000000000
    NormalPacked.z = uint(Normal.z * 7.0f + 0.5f) & 0x7; // 00000000000000111000000000000000
    
    return (DepthPacked << 17) | (NormalPacked.x << 14) | (NormalPacked.y << 11) | (NormalPacked.z << 8) | OcclusionPacked;
}

void UnpackSpatialData(uint PackedData, out float Occlusion, out float Depth, out float3 Normal)
{
    Occlusion = (PackedData & 0xFF) / 255.0f;
    Depth = f16tof32((PackedData >> 17) & 0x7FFF);

    Normal.x = float((PackedData >> 14) & 0x7) / 7.0f;
    Normal.y = float((PackedData >> 11) & 0x7) / 7.0f;
    Normal.z = float((PackedData >>  8) & 0x7) / 7.0f;
    Normal = Normal * 2.0f - 1.0f;
}
