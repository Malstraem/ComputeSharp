using System;
using System.IO;
using ComputeSharp.SwapChain.Shaders;
#if WINDOWS_UWP
using ComputeSharp.Uwp;
#else
using ComputeSharp.WinUI;
#endif
using Windows.ApplicationModel;

#nullable enable

namespace ComputeSharp.SwapChain.Core.Shaders.Runners;

/// <summary>
/// A specialized <see cref="IShaderRunner"/> for <see cref="AtmosphericScattering"/>.
/// </summary>
public sealed class AtmosphericScatteringRunner : IShaderRunner
{
    private ReadOnlyTexture2D<Rgba32, Float4>? earthDayTexture;

    private ReadOnlyTexture2D<Rgba32, Float4>? earthNightTexture;

    private Earth earth = Earth.New(sphere: new float4((float3)0, 1), atmosphereThickness: 0.25f);

    /// <inheritdoc/>
    public bool TryExecute(IReadWriteNormalizedTexture2D<Float4> texture, TimeSpan timespan, object? parameter)
    {
        if (this.earthDayTexture is null || this.earthDayTexture.GraphicsDevice != texture.GraphicsDevice)
        {
            string filename = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "Textures", "DayEarth.jpg");

            this.earthDayTexture?.Dispose();

            this.earthDayTexture = texture.GraphicsDevice.LoadReadOnlyTexture2D<Rgba32, Float4>(filename);
        }

        if (this.earthNightTexture is null || this.earthNightTexture.GraphicsDevice != texture.GraphicsDevice)
        {
            string filename = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "Textures", "NightEarth.jpg");

            this.earthNightTexture?.Dispose();

            this.earthNightTexture = texture.GraphicsDevice.LoadReadOnlyTexture2D<Rgba32, Float4>(filename);
        }

        texture.GraphicsDevice.ForEach(texture, new AtmosphericScattering((float)timespan.TotalSeconds,
                                                                          this.earth,
                                                                          this.earthDayTexture,
                                                                          this.earthNightTexture));

        return true;
    }
}