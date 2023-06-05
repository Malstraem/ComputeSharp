using ComputeSharp.D2D1;
using static ComputeSharp.Hlsl;

namespace ComputeSharp.SwapChain.Shaders.D2D1;

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
/// A shader creating an abstract and colorful animation.
/// Ported from <see href="https://www.shadertoy.com/view/WtjyzR"/>.
/// <para>Created by Benoit Marini.</para>
/// <para>License Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.</para>
/// </summary>
[D2DInputCount(0)]
[D2DRequiresScenePosition]
[D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
[AutoConstructor]
internal readonly partial struct AtmosphericScattering : ID2D1PixelShader
{
    /// <summary>
    /// The current time since the start of the application.
    /// </summary>
    private readonly float time;

    /// <summary>
    /// The dispatch size for the current output.
    /// </summary>
    private readonly int2 dispatchSize;

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

    private const float Pi = 3.14159265359f;

    private const int OutScatterCount = 8;
    private const int InScatterCount = 80;

    private readonly Earth earth;

    /*[D2DResourceTextureIndex(0)]
    private readonly D2D1ResourceTexture2D<float4> earthDayTexture;*/

    [D2DResourceTextureIndex(0)]
    private readonly D2D1ResourceTexture2D<float4> earthNightTexture;

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

    private static float3 RayDirection(float fov, float2 size, float2 position)
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

    private float3 InScatter(float3 o, float3 dir, float2 e, float3 l)
    {
        const float phRay = 0.05f;
        const float phMie = 0.02f;

        float3 kRay = new(3.8f, 13.5f, 33.1f);
        float3 k_mie = (float3)21f;

        float3 sum_ray = (float3)0f;
        float3 sum_mie = (float3)0f;

        float n_ray0 = 0f;
        float n_mie0 = 0f;

        float len = (e.Y - e.X) / InScatterCount;
        float3 s = dir * len;
        float3 v = o + (dir * (e.X + (len * 0.5f)));

        for (int i = 0; i < InScatterCount; i++, v += s)
        {
            float d_ray = Density(v, phRay) * len;
            float d_mie = Density(v, phMie) * len;

            n_ray0 += d_ray;
            n_mie0 += d_mie;

            Ray ray = Ray.New(v, l);

            if (i == 0)
            {
                e = GetSphereIntersects(ray, this.earth.Sphere);
                e.X = Max(e.X, 0);

                if (e.X < e.Y)
                {
                    continue;
                }
            }

            float2 f = GetSphereIntersects(ray, this.earth.Atmosphere);
            float3 u = v + (l * f.Y);

            float n_ray1 = Optic(v, u, phRay);
            float n_mie1 = Optic(v, u, phMie);

            float3 att = Exp((-(n_ray0 + n_ray1) * kRay) - ((n_mie0 + n_mie1) * k_mie));

            sum_ray += d_ray * att;
            sum_mie += d_mie * att;
        }

        float c = Dot(dir, -l);

        float cc = c * c;

        float3 scatter = (sum_ray * kRay * RayPhase(cc)) + (sum_mie * k_mie * MiePhase(-0.78f, c, cc));

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
        int2 xy = (int2)D2D.GetScenePosition().XY;
        float2 fragCoord = new(xy.X, this.dispatchSize.Y - xy.Y);

        float3 direction = RayDirection(45f, (float2)this.dispatchSize, fragCoord);

        float3 eye = new(0f, 0f, 3f);

        float3x3 rotation = Rotate(new float2(0f, this.time * 0.2f));

        direction = rotation * direction;
        eye = rotation * eye;

        float3 lightDirection = new(0f, 0f, 1f);

        Ray ray = Ray.New(eye, direction);

        float2 atmosphereIntersects = GetSphereIntersects(ray, this.earth.Atmosphere);
        float2 planetIntersects = GetSphereIntersects(ray, this.earth.Sphere);

        atmosphereIntersects.Y = Min(atmosphereIntersects.Y, planetIntersects.X);

        float3 scatter = InScatter(eye, direction, atmosphereIntersects, lightDirection);

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