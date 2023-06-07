using System;
using System.IO;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
#if WINDOWS_UWP
using ComputeSharp.D2D1.Uwp;
#else
using ComputeSharp.D2D1.WinUI;
#endif
using ComputeSharp.SwapChain.Shaders.D2D1;
using Windows.ApplicationModel;

#nullable enable

namespace ComputeSharp.SwapChain.Core.Shaders.Runners;

/// <summary>
/// A specialized <see cref="PixelShaderEffect"/> for <see cref="AtmosphericScattering"/>.
/// </summary>
public sealed class D2D1AtmosphericScatteringEffect : PixelShaderEffect
{
    /// <summary>
    /// The reusable <see cref="PixelShaderEffect{T}"/> node to use to render frames.
    /// </summary>
    private static readonly EffectNode<PixelShaderEffect<AtmosphericScattering>> PixelShaderEffect = new();

    private Earth earth = Earth.New(sphere: new float4((float3)0, 1), atmosphereThickness: 0.25f);

    private static unsafe D2D1ResourceTextureManager CreateTextureManager(string filename)
    {
        ReadOnlyTexture2D<Rgba32, float4> readonlyTexture = GraphicsDevice.GetDefault().LoadReadOnlyTexture2D<Rgba32, float4>(filename);
        ReadBackTexture2D<Rgba32> readbackTexture = GraphicsDevice.GetDefault().AllocateReadBackTexture2D<Rgba32>(readonlyTexture.Width, readonlyTexture.Height);

        readonlyTexture.CopyTo(readbackTexture);

        Rgba32* dataBuffer = readbackTexture.View.DangerousGetAddressAndByteStride(out int strideInBytes);
        int bufferSize = ((readbackTexture.Height - 1) * strideInBytes) + (readbackTexture.Width * sizeof(Rgba32));

        return new(
            extents: stackalloc uint[] { (uint)readbackTexture.Width, (uint)readbackTexture.Height },
            bufferPrecision: D2D1BufferPrecision.UInt8Normalized,
            channelDepth: D2D1ChannelDepth.Four,
            filter: D2D1Filter.MinMagMipLinear,
            extendModes: stackalloc D2D1ExtendMode[] { D2D1ExtendMode.Mirror, D2D1ExtendMode.Mirror },
            data: new ReadOnlySpan<byte>(dataBuffer, bufferSize),
            strides: stackalloc uint[] { (uint)strideInBytes });
    }

    /// <inheritdoc/>
    protected override unsafe void BuildEffectGraph(EffectGraph effectGraph)
    {
        string dayFilename = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "Textures", "DayEarth.jpg");
        string nightFilename = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "Textures", "NightEarth.jpg");

        D2D1ResourceTextureManager dayTextureManager = CreateTextureManager(dayFilename);
        D2D1ResourceTextureManager nightTextureManager = CreateTextureManager(nightFilename);

        PixelShaderEffect<AtmosphericScattering> pixelShaderEffect = new()
        {
            ResourceTextureManagers =
            {
                [0] = dayTextureManager,
                [1] = nightTextureManager
            }
        };

        effectGraph.RegisterOutputNode(PixelShaderEffect, pixelShaderEffect);
    }

    /// <inheritdoc/>
    protected override void ConfigureEffectGraph(EffectGraph effectGraph)
    {
        int2 dispatchSize = new(ScreenWidth, ScreenHeight);
        effectGraph.GetNode(PixelShaderEffect).ConstantBuffer = new AtmosphericScattering((float)ElapsedTime.TotalSeconds, dispatchSize, this.earth);
    }
}