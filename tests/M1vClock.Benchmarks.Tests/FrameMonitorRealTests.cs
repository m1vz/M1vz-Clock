using System.Diagnostics;
using M1vClock.Benchmarks.Gpu;
using M1vClock.Core.Security;
using M1vClock.Games.Frames;
using Microsoft.Extensions.Logging.Abstractions;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace M1vClock.Benchmarks.Tests;

public sealed class FrameMonitorRealTests
{
    private const int TargetPresentsPerSecond = 100;
    private const double RunSeconds = 5;

    [Fact]
    public async Task ElevatedTheEtwFrameMonitorCountsEveryPresentOfAKnownPresenter()
    {
        Assert.SkipUnless(Environment.UserInteractive, "Precisa de uma sessão interativa para abrir a janela.");
        Assert.SkipUnless(GpuSupport.HasHardwareAdapter(), "Este PC não tem GPU de hardware.");
        var elevation = ElevationInfo.Current();
        Assert.SkipUnless(elevation.IsElevated, "Precisa de administrador.");
        using var monitor = new FrameMonitor(
            new EtwFrameEventSource(elevation, NullLogger<EtwFrameEventSource>.Instance),
            new WindowsProcessNames(NullLogger<WindowsProcessNames>.Instance),
            new WindowsForegroundWindow(),
            TimeProvider.System,
            NullLogger<FrameMonitor>.Instance);
        using var lease = monitor.Acquire();
        Assert.Equal(FrameMonitorState.Running, monitor.State);

        var measured = new List<double>();
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(
            async () =>
            {
                await Task.Delay(2000, CancellationToken.None);
                while (!stop.IsCancellationRequested)
                {
                    if (monitor.GetStats(Environment.ProcessId) is { } stats)
                    {
                        lock (measured)
                        {
                            measured.Add(stats.Fps);
                        }
                    }

                    await Task.Delay(400, CancellationToken.None);
                }
            },
            CancellationToken.None);

        var (presents, seconds) = Present();
        await stop.CancelAsync();
        await sampler;

        Assert.NotEmpty(measured);
        var expected = presents / seconds;
        var average = measured.Average();
        Console.WriteLine($"ETW: {average:0.0} FPS em {measured.Count} amostras; apresentador: {presents} Present em {seconds:0.00} s = {expected:0.0} por segundo");
        Assert.InRange(expected, TargetPresentsPerSecond * 0.5, TargetPresentsPerSecond * 1.1);
        Assert.InRange(average, expected * 0.9, expected * 1.1);
    }

    private static (int Presents, double Seconds) Present()
    {
        var owned = new List<IDisposable>();
        try
        {
            var window = RenderWindow.Create("m1vClock · teste de quadros", 640, 360, fullscreen: false);
            owned.Add(window);
            var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
            owned.Add(factory);
            var adapter = GpuAdapters.Choose(factory, string.Empty, owned);
            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            owned.Add(device);
            owned.Add(context);
            var swap = factory.CreateSwapChainForHwnd(
                device,
                window.Handle,
                new SwapChainDescription1(640, 360, Format.B8G8R8A8_UNorm, false, Usage.RenderTargetOutput, 2, Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapChainFlags.None));
            owned.Add(swap);
            var back = swap.GetBuffer<ID3D11Texture2D>(0);
            owned.Add(back);
            var target = device.CreateRenderTargetView(back);
            owned.Add(target);

            var clock = Stopwatch.StartNew();
            var interval = 1000.0 / TargetPresentsPerSecond;
            var next = 0.0;
            var presents = 0;
            while (clock.Elapsed.TotalSeconds < RunSeconds)
            {
                RenderWindow.Pump();
                if (clock.Elapsed.TotalMilliseconds < next)
                {
                    Thread.SpinWait(200);
                    continue;
                }

                context.ClearRenderTargetView(target, presents % 2 == 0 ? new Color4(0.1f, 0.2f, 0.5f, 1f) : new Color4(0.5f, 0.2f, 0.1f, 1f));
                if (swap.Present(0, PresentFlags.None).Success)
                {
                    presents++;
                }

                next += interval;
            }

            return (presents, clock.Elapsed.TotalSeconds);
        }
        finally
        {
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                owned[i].Dispose();
            }
        }
    }
}
