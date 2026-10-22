using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using M1vClock.Benchmarks.Stress;
using Microsoft.Extensions.Logging;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace M1vClock.Benchmarks.Gpu;

public sealed record GpuBurnSettings(int Width, int Height, bool Fullscreen, int Msaa, int Shells, int Iterations, string AdapterName)
{
    public const int MinimumShells = 16;
    public const int MaximumShells = 256;
    public const int MaximumIterations = 128;

    public static GpuBurnSettings Normal(int width, int height, bool fullscreen, int msaa, string adapterName) => new(width, height, fullscreen, msaa, 96, 32, adapterName);

    public static GpuBurnSettings High(int width, int height, bool fullscreen, int msaa, string adapterName) => new(width, height, fullscreen, msaa, 128, 48, adapterName);

    public static GpuBurnSettings Extreme(int width, int height, bool fullscreen, int msaa, string adapterName) => new(width, height, fullscreen, msaa, 160, 64, adapterName);
}

public sealed class GpuBurn : IStressWorkload
{
    public static readonly TimeSpan UnresponsiveAfter = TimeSpan.FromSeconds(10);

    private const int FramesInFlight = 6;
    private const double PresentInterval = 1 / 60.0;
    private const double FpsInterval = 0.5;

    private readonly GpuBurnSettings _settings;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private Thread? _thread;
    private volatile bool _stop;
    private long _frames;
    private long _beat;
    private double _fps;
    private double _seconds;
    private string? _error;

    public GpuBurn(GpuBurnSettings settings, ILogger<GpuBurn> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Width, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Height, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Shells, GpuBurnSettings.MinimumShells);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.Shells, GpuBurnSettings.MaximumShells);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.Iterations);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.Iterations, GpuBurnSettings.MaximumIterations);
        _settings = settings;
        _logger = logger;
    }

    public bool IsResponsive
    {
        get
        {
            var beat = Interlocked.Read(ref _beat);
            return beat == 0 || Stopwatch.GetElapsedTime(beat) < UnresponsiveAfter;
        }
    }

    public string AdapterUsed { get; private set; } = string.Empty;

    public WorkloadStatus Status
    {
        get
        {
            var frames = Interlocked.Read(ref _frames);
            lock (_gate)
            {
                var alive = _thread is { IsAlive: true };
                var seconds = Volatile.Read(ref _seconds);
                return new WorkloadStatus(alive, 0, 0, 0, 0, frames, Volatile.Read(ref _fps), seconds > 0 ? frames / seconds : 0, _error);
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_thread is { IsAlive: true })
            {
                throw new InvalidOperationException("O teste de GPU já está rodando.");
            }

            _stop = false;
            Interlocked.Exchange(ref _frames, 0);
            Interlocked.Exchange(ref _beat, 0);
            Volatile.Write(ref _fps, 0);
            Volatile.Write(ref _seconds, 0);
            _error = null;
            _thread = new Thread(Run) { IsBackground = true, Name = "m1vClock GPU stress" };
            _thread.Start();
        }
    }

    public void Halt()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
        }

        _stop = true;
        thread?.Join(TimeSpan.FromSeconds(8));
    }

    public void Dispose() => Halt();

    private void Run()
    {
        var owned = new List<IDisposable>();
        try
        {
            Render(owned);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException or InsufficientMemoryException)
        {
            GpuLog.StressFailed(_logger, ex);
            lock (_gate)
            {
                _error = ex.Message;
            }
        }
        finally
        {
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                owned[i].Dispose();
            }

            Volatile.Write(ref _fps, 0);
        }
    }

    private void Render(List<IDisposable> owned)
    {
        T Own<T>(T disposable)
            where T : IDisposable
        {
            owned.Add(disposable);
            return disposable;
        }

        var window = Own(RenderWindow.Create("m1vClock · Teste de GPU", _settings.Width, _settings.Height, _settings.Fullscreen));
        var factory = Own(DXGI.CreateDXGIFactory1<IDXGIFactory2>());
        var adapter = GpuAdapters.Choose(factory, _settings.AdapterName, owned);
        AdapterUsed = adapter.Description1.Description;
        D3D11.D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out ID3D11Device device,
            out ID3D11DeviceContext context).CheckError();
        Own(device);
        Own(context);

        var msaa = Math.Max(1, _settings.Msaa);
        while (msaa > 1 && device.CheckMultisampleQualityLevels(Format.B8G8R8A8_UNorm, (uint)msaa) == 0)
        {
            msaa /= 2;
        }

        bool tearing;
        using (var factory5 = factory.QueryInterfaceOrNull<IDXGIFactory5>())
        {
            tearing = factory5?.PresentAllowTearing ?? false;
        }

        var swapFlags = tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None;
        var (clientWidth, clientHeight) = window.ClientSize();
        var width = Math.Max(1, clientWidth);
        var height = Math.Max(1, clientHeight);
        var swap = Own(factory.CreateSwapChainForHwnd(
            device,
            window.Handle,
            new SwapChainDescription1((uint)width, (uint)height, Format.B8G8R8A8_UNorm, false, Usage.RenderTargetOutput, 2, Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, swapFlags)));
        factory.MakeWindowAssociation(window.Handle, WindowAssociationFlags.IgnoreAltEnter);

        ID3D11Texture2D? backBuffer = null;
        ID3D11Texture2D? color = null;
        ID3D11Texture2D? depth = null;
        ID3D11RenderTargetView? target = null;
        ID3D11DepthStencilView? depthView = null;

        void ReleaseTargets()
        {
            target?.Dispose();
            depthView?.Dispose();
            depth?.Dispose();
            color?.Dispose();
            backBuffer?.Dispose();
            target = null;
            depthView = null;
            depth = null;
            color = null;
            backBuffer = null;
        }

        void CreateTargets(int w, int h)
        {
            backBuffer = swap.GetBuffer<ID3D11Texture2D>(0);
            var colorDescription = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, (uint)msaa, 0, ResourceOptionFlags.None);
            color = device.CreateTexture2D(in colorDescription);
            target = device.CreateRenderTargetView(color);
            var depthDescription = new Texture2DDescription(Format.D32_Float, (uint)w, (uint)h, 1, 1, BindFlags.DepthStencil, ResourceUsage.Default, CpuAccessFlags.None, (uint)msaa, 0, ResourceOptionFlags.None);
            depth = device.CreateTexture2D(in depthDescription);
            depthView = device.CreateDepthStencilView(depth);
        }

        owned.Add(new ActionDisposable(ReleaseTargets));
        CreateTargets(width, height);

        var vsCode = Compiler.Compile(BurnShaders.Hlsl, "VS", "fur.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3);
        var psCode = Compiler.Compile(BurnShaders.Hlsl, "PS", "fur.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3);
        var vertexShader = Own(device.CreateVertexShader(vsCode.Span));
        var pixelShader = Own(device.CreatePixelShader(psCode.Span));
        var layout = Own(device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0),
            ],
            vsCode.Span));

        BuildTorus(out var vertices, out var indices);
        var vertexBuffer = Own(device.CreateBuffer(vertices, BindFlags.VertexBuffer));
        var indexBuffer = Own(device.CreateBuffer(indices, BindFlags.IndexBuffer));
        var constants = Own(device.CreateBuffer((uint)Marshal.SizeOf<Constants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        var raster = Own(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { MultisampleEnable = msaa > 1 }));
        var blend = Own(device.CreateBlendState(new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha)));

        var fences = new ID3D11Query[FramesInFlight];
        for (var i = 0; i < FramesInFlight; i++)
        {
            fences[i] = Own(device.CreateQuery(QueryType.Event, QueryFlags.None));
        }

        long frame = 0;
        var clock = Stopwatch.StartNew();
        double lastReport = 0;
        double lastPresent = 0;
        long lastFrames = 0;
        var stride = (uint)Marshal.SizeOf<Vertex>();
        Interlocked.Exchange(ref _beat, Stopwatch.GetTimestamp());

        while (!_stop && !window.CloseRequested)
        {
            RenderWindow.Pump();
            if (_stop || window.CloseRequested)
            {
                break;
            }

            Interlocked.Exchange(ref _beat, Stopwatch.GetTimestamp());
            var (currentWidth, currentHeight) = window.ClientSize();
            if (currentWidth <= 0 || currentHeight <= 0)
            {
                Thread.Sleep(50);
                continue;
            }

            if (currentWidth != width || currentHeight != height)
            {
                width = currentWidth;
                height = currentHeight;
                context.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
                ReleaseTargets();
                swap.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, swapFlags).CheckError();
                CreateTargets(width, height);
            }

            var fence = fences[frame % FramesInFlight];
            if (frame >= FramesInFlight)
            {
                while (!context.IsDataAvailable(fence) && !_stop && !window.CloseRequested)
                {
                    Thread.Yield();
                }
            }

            var t = (float)clock.Elapsed.TotalSeconds;
            var world = Matrix4x4.CreateRotationY(t * 0.6f) * Matrix4x4.CreateRotationX(0.55f + 0.25f * MathF.Sin(t * 0.35f));
            var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, -3.5f), Vector3.Zero, Vector3.UnitY);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, width / (float)height, 0.1f, 50f);
            var data = new Constants
            {
                World = world,
                ViewProj = view * projection,
                Time = t,
                ShellCount = _settings.Shells,
                FurLength = 0.2f,
                Iterations = _settings.Iterations,
            };
            var map = context.Map(constants, MapMode.WriteDiscard);
            Marshal.StructureToPtr(data, map.DataPointer, false);
            context.Unmap(constants);

            context.OMSetRenderTargets(target!, depthView);
            context.ClearRenderTargetView(target!, new Color4(0, 0, 0, 1));
            context.ClearDepthStencilView(depthView!, DepthStencilClearFlags.Depth, 1f, 0);
            context.RSSetViewport(0, 0, width, height);
            context.RSSetState(raster);
            context.OMSetBlendState(blend);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.IASetInputLayout(layout);
            context.IASetVertexBuffer(0, vertexBuffer, stride);
            context.IASetIndexBuffer(indexBuffer, Format.R32_UInt, 0);
            context.VSSetShader(vertexShader);
            context.PSSetShader(pixelShader);
            context.VSSetConstantBuffer(0, constants);
            context.PSSetConstantBuffer(0, constants);
            context.DrawIndexedInstanced((uint)indices.Length, (uint)_settings.Shells, 0, 0, 0);
            context.End(fence);
            frame++;
            Interlocked.Increment(ref _frames);

            var now = clock.Elapsed.TotalSeconds;
            if (now - lastPresent >= PresentInterval)
            {
                if (msaa > 1)
                {
                    context.ResolveSubresource(backBuffer!, 0, color!, 0, Format.B8G8R8A8_UNorm);
                }
                else
                {
                    context.CopyResource(backBuffer!, color!);
                }

                swap.Present(0, tearing ? PresentFlags.AllowTearing : PresentFlags.None);
                lastPresent = now;
            }

            Volatile.Write(ref _seconds, now);
            if (now - lastReport >= FpsInterval)
            {
                var total = Interlocked.Read(ref _frames);
                var fps = (total - lastFrames) / (now - lastReport);
                Volatile.Write(ref _fps, fps);
                lastReport = now;
                lastFrames = total;
                window.SetTitle($"m1vClock · Teste de GPU · {fps:0} FPS · {width}x{height}" + (msaa > 1 ? $" · MSAA {msaa}x" : string.Empty) + $" · {AdapterUsed}");
            }
        }
    }

    private static void BuildTorus(out Vertex[] vertices, out uint[] indices)
    {
        const int segments = 160;
        const int sides = 64;
        const float major = 1f;
        const float minor = 0.42f;
        vertices = new Vertex[(segments + 1) * (sides + 1)];
        for (int i = 0, k = 0; i <= segments; i++)
        {
            var u = i / (float)segments * MathF.Tau;
            for (var j = 0; j <= sides; j++, k++)
            {
                var v = j / (float)sides * MathF.Tau;
                var normal = new Vector3(MathF.Cos(u) * MathF.Cos(v), MathF.Sin(v), MathF.Sin(u) * MathF.Cos(v));
                vertices[k] = new Vertex
                {
                    Pos = new Vector3(MathF.Cos(u) * major, 0, MathF.Sin(u) * major) + normal * minor,
                    Normal = normal,
                    Uv = new Vector2(i / (float)segments, j / (float)sides),
                };
            }
        }

        indices = new uint[segments * sides * 6];
        for (int i = 0, k = 0; i < segments; i++)
        {
            for (var j = 0; j < sides; j++)
            {
                var a = (uint)(i * (sides + 1) + j);
                var b = (uint)((i + 1) * (sides + 1) + j);
                indices[k++] = a;
                indices[k++] = b;
                indices[k++] = a + 1;
                indices[k++] = a + 1;
                indices[k++] = b;
                indices[k++] = b + 1;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Pos;
        public Vector3 Normal;
        public Vector2 Uv;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Matrix4x4 World;
        public Matrix4x4 ViewProj;
        public float Time;
        public float ShellCount;
        public float FurLength;
        public float Iterations;
    }

    private sealed class ActionDisposable(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
