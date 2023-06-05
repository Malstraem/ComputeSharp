using static ComputeSharp.Hlsl;

namespace ComputeSharp.SwapChain.Shaders;

public struct Earth
{
    public float4 Sphere;

    public float4 Atmosphere;

    public float AtmosphereThickness;

    public static Earth New(float4 sphere, float atmosphereThickness)
    {
        Earth earth;
        earth.Sphere = sphere;
        earth.Atmosphere = new float4(sphere.XYZ, sphere.W + atmosphereThickness);
        earth.AtmosphereThickness = atmosphereThickness;

        return earth;
    }
}

/// <summary>
/// Shader showing atmospheric scattering effect.
/// Based on <see href="https://www.shadertoy.com/view/MldyDH"/>.
/// <para>License Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.</para>
/// </summary>
[AutoConstructor]
#if SAMPLE_APP
[EmbeddedBytecode(DispatchAxis.XY)]
#endif
internal readonly partial struct AtmosphericScattering : IPixelShader<float4>
{
    private struct Ray
    {
        public float3 Origin;

        public float3 Direction;

        public static Ray New(float3 origin, float3 direction)
        {
            Ray ray;

            ray.Origin = origin;
            ray.Direction = direction;

            return ray;
        }
    };

    /// <summary>
    /// The current time since the start of the application.
    /// </summary>
    private readonly float time;

    private const float Pi = 3.14159265359f;

    private const int OutScatterCount = 8;
    private const int InScatterCount = 80;

    private readonly Earth earth;

    private readonly IReadOnlyNormalizedTexture2D<float4> earthDayTexture;

    private readonly IReadOnlyNormalizedTexture2D<float4> earthNightTexture;

    private static float2 GetSphereIntersects(Ray ray, float4 sphere)
    {
        float b = Dot(ray.Origin, ray.Direction);
        float c = Dot(ray.Origin, ray.Origin) - (sphere.W * sphere.W);

        float d = (b * b) - c;

        if (d < 0.0)
        {
            return new float2(1e4f, -1e4f);
        }

        d = Sqrt(d);

        return new float2(-b - d, -b + d);
    }

    private static float3 GetRayDirection(float fov, float2 size, float2 position)
    {
        float2 xy = position - (size * 0.5f);

        float halfFovCotangent = Tan(Radians(90f - (fov * 0.5f)));
        float z = size.Y * 0.5f * halfFovCotangent;

        return Normalize(new float3(xy, -z));
    }

    private static float MiePhase(float g, float c, float cc)
    {
        float gg = g * g;

        float a = (1f - gg) * (1f + cc);

        float b = 1f + gg - (2f * g * c);
        b *= Sqrt(b);
        b *= 2f + gg;

        return 3f / 8f / Pi * a / b;
    }

    private static float RayPhase(float cc)
    {
        return 3f / 16f / Pi * (1f + cc);
    }

    private float Density(float3 p, float ph)
    {
        return Exp(-Max(Length(p) - this.earth.Sphere.W, 0f) / ph / this.earth.AtmosphereThickness);
    }

    private float Optic(float3 p, float3 q, float ph)
    {
        float3 s = (q - p) / OutScatterCount;
        float3 v = p + (s * 0.5f);

        float sum = 0f;

        for (int i = 0; i < OutScatterCount; i++)
        {
            sum += Density(v, ph);
            v += s;
        }

        sum *= Length(s);

        return sum;
    }

    private float3 InScatter(Ray ray, float2 intersects, float3 lightDirection)
    {
        const float phRay = 0.05f;
        const float phMie = 0.02f;

        float3 kRay = new(3.8f, 13.5f, 33.1f);
        float3 kMie = (float3)21f;

        float3 sumRay = default;
        float3 sumMie = default;

        float nRay0 = 0f;
        float nMie0 = 0f;

        float len = (intersects.Y - intersects.X) / InScatterCount;
        float3 s = ray.Direction * len;
        float3 v = ray.Origin + (ray.Direction * (intersects.X + (len * 0.5f)));

        for (int i = 0; i < InScatterCount; i++, v += s)
        {
            float dRay = Density(v, phRay) * len;
            float dMie = Density(v, phMie) * len;

            nRay0 += dRay;
            nMie0 += dMie;

            Ray stepRay = Ray.New(v, lightDirection);

            float2 stepIntersects = GetSphereIntersects(stepRay, this.earth.Atmosphere);
            float3 u = v + (lightDirection * stepIntersects.Y);

            float nRay1 = Optic(v, u, phRay);
            float nMie1 = Optic(v, u, phMie);

            float3 att = Exp((-(nRay0 + nRay1) * kRay) - ((nMie0 + nMie1) * kMie));

            sumRay += dRay * att;
            sumMie += dMie * att;
        }

        float c = Dot(ray.Direction, -lightDirection);

        float cc = c * c;

        float3 scatter = (sumRay * kRay * RayPhase(cc)) + (sumMie * kMie * MiePhase(-0.78f, c, cc));

        return 10f * scatter;
    }

    private static float3x3 Rotate(float2 angle)
    {
        float2 cos = Cos(angle);
        float2 sin = Sin(angle);

        return new float3x3(cos.Y, 0f, -sin.Y,
                            sin.Y * sin.X, cos.X, cos.Y * sin.X,
                            sin.Y * cos.X, -sin.X, cos.Y * cos.X);
    }

    /// <inheritdoc/>
    public float4 Execute()
    {
        float2 fragCoord = new(ThreadIds.X, DispatchSize.Y - ThreadIds.Y);

        float3 direction = GetRayDirection(45f, DispatchSize.XY, fragCoord);

        float3 eye = new(0f, 0f, 3f);

        float3x3 rotation = Rotate(new float2(0f, this.time * 0.2f));

        direction = rotation * direction;
        eye = rotation * eye;

        float3 lightDirection = new(0f, 0f, 1f);

        Ray ray = Ray.New(eye, direction);

        float2 atmosphereIntersects = GetSphereIntersects(ray, this.earth.Atmosphere);
        float2 planetIntersects = GetSphereIntersects(ray, this.earth.Sphere);

        atmosphereIntersects.Y = Min(atmosphereIntersects.Y, planetIntersects.X);

        float3 scatter = InScatter(ray, atmosphereIntersects, lightDirection);

        float4 fragColor = default;

        if (planetIntersects.X < planetIntersects.Y)
        {
            float3 position = ray.Origin + (ray.Direction * planetIntersects.X);
            float3 normal = Normalize(this.earth.Sphere.XYZ - position);

            float latitude = 90f - (Acos(normal.Y / Length(normal)) * 180f / Pi);
            float longitude = Atan2(normal.X, normal.Z) * 180f / Pi;
            float2 uv = new float2(longitude / 360f, latitude / 180f) + 0.5f;

            //float3 dayColor = this.earthDayTexture.Sample(uv).RGB;
            float3 nightColor = this.earthNightTexture.Sample(uv).RGB;

            fragColor.RGB = nightColor;
        }

        fragColor.RGB += scatter;

        return fragColor;
    }
}