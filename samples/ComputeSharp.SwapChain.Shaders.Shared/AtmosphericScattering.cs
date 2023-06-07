using System;

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

    private readonly float time;

    private readonly Earth earth;

    private readonly IReadOnlyNormalizedTexture2D<float4> earthDayTexture;
    private readonly IReadOnlyNormalizedTexture2D<float4> earthNightTexture;

    private const float Pi = (float)Math.PI;

    private const int InScatterCount = 80;
    private const int OutScatterCount = 8;

    private const int SSAA = 8;

    private static float3 GetRayDirection(float fov, float2 size, float2 position)
    {
        float2 xy = position - (size * 0.5f);

        float halfFovCotangent = Tan(Radians(90f - (fov * 0.5f)));
        float z = size.Y * 0.5f * halfFovCotangent;

        return Normalize(new float3(xy, -z));
    }

    private static float3x3 GetRotation(float2 angle)
    {
        float2 cos = Cos(angle);
        float2 sin = Sin(angle);

        return new float3x3(cos.Y, 0f, -sin.Y,
                            sin.Y * sin.X, cos.X, cos.Y * sin.X,
                            sin.Y * cos.X, -sin.X, cos.Y * cos.X);
    }

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

    private float Density(float3 p, float ph)
    {
        return Exp(-Max(Length(p) - this.earth.Sphere.W, 0f) / ph / this.earth.AtmosphereThickness);
    }

    private float Optic(float3 p, float3 q, float ph)
    {
        float sum = 0f;
        float3 s = (q - p) / OutScatterCount, v = p + (s * 0.5f);

        for (int i = 0; i < OutScatterCount; i++, v += s)
        {
            sum += Density(v, ph);
        }

        return sum * Length(s);
    }

    private float3 GetScattering(Ray ray, float2 intersects, float3 lightDirection)
    {
        static float RayPhase(float cc)
        {
            return 3f / 16f / Pi * (1f + cc);
        }

        static float MiePhase(float g, float c, float cc)
        {
            float gg = g * g;
            float a = (1f - gg) * (1f + cc);
            float b = 1f + gg - (2f * g * c);

            b *= Sqrt(b) * (2f + gg);

            return 3f / 8f / Pi * a / b;
        }

        const float phRay = 0.05f;
        const float phMie = 0.02f;
        const float kMieEx = 1.1f;

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

            float3 attenuate = Exp((-(nRay0 + nRay1) * kRay) - ((nMie0 + nMie1) * kMie * kMieEx));

            sumRay += dRay * attenuate;
            sumMie += dMie * attenuate;
        }

        float c = Dot(ray.Direction, -lightDirection);
        float cc = c * c;

        float3 scatter = (sumRay * kRay * RayPhase(cc)) + (sumMie * kMie * MiePhase(-0.78f, c, cc));

        return 15f * scatter;
    }

    /// <inheritdoc/>
    public float4 Execute()
    {
        float2 fragCoord = new(ThreadIds.X, DispatchSize.Y - ThreadIds.Y);

        float3 eye = new(0f, 0f, 3f);
        float3 lightDirection = new(0f, 0f, 1f);
        float3 rayDirection = GetRayDirection(45f, DispatchSize.XY, fragCoord);

        float3x3 planetRotation = GetRotation(new float2(0f, this.time * 0.05f));
        float3x3 atmosphereRotation = GetRotation(new float2(-0.5f, this.time * 0.2f));

        Ray planetRay = Ray.New(planetRotation * eye, planetRotation * rayDirection);
        Ray atmosphereRay = Ray.New(eye * atmosphereRotation, rayDirection * atmosphereRotation);

        float2 atmosphereIntersects = GetSphereIntersects(atmosphereRay, this.earth.Atmosphere);
        float2 planetIntersects = GetSphereIntersects(planetRay, this.earth.Sphere);

        atmosphereIntersects.Y = Min(atmosphereIntersects.Y, planetIntersects.X);

        float3 planetColor = new(0f, 0f, 0f);

        for (int m = 0; m < SSAA; m++)
        {
            for (int n = 0; n < SSAA; n++)
            {
                float2 aaOffset = new float2(m, n) / SSAA;

                float3 planetRayDirection = GetRayDirection(45f, DispatchSize.XY, fragCoord + aaOffset);

                planetRay.Direction = planetRotation * planetRayDirection;

                planetIntersects = GetSphereIntersects(planetRay, this.earth.Sphere);

                if (planetIntersects.X <= planetIntersects.Y)
                {
                    float3 position = planetRay.Origin + (planetRay.Direction * planetIntersects.X);
                    float3 normal = Normalize(this.earth.Sphere.XYZ - position);

                    float latitude = 90f - (Acos(normal.Y / Length(normal)) * 180f / Pi);
                    float longitude = Atan2(normal.X, normal.Z) * 180f / Pi;

                    float2 uv = new float2(longitude / 360f, latitude / 180f) + 0.5f;

                    float3 dayColor = Pow(this.earthDayTexture.Sample(uv).RGB, (float3)(1f / 2f));
                    float3 nightColor = Pow(this.earthNightTexture.Sample(uv).RGB, 1.5f);

                    float light = Dot(Normalize(atmosphereRay.Origin + (atmosphereRay.Direction * planetIntersects.X)), lightDirection);

                    planetColor += Lerp(nightColor, dayColor * light, SmoothStep(-0.2f, 0.2f, light));
                }
            }
        }

        planetColor /= SSAA * SSAA;

        return new float4(planetColor + GetScattering(atmosphereRay, atmosphereIntersects, lightDirection), 1f);
    }
}