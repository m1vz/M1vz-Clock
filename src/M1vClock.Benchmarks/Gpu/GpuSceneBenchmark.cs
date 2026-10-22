using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using M1vClock.Benchmarks.Statistics;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace M1vClock.Benchmarks.Gpu;

public sealed record GpuSceneOptions(int Width, int Height, TimeSpan Duration, string AdapterName);

public sealed record GpuSceneResult(double AverageFps, double LowFps, int Frames, double Seconds, int Width, int Height, string Adapter)
{
    public const double ScoreFactor = 10;

    public double Score => AverageFps * ScoreFactor;
}

public sealed class GpuSceneBenchmark
{
    public const int MinimumDurationSeconds = 5;

    private const int TerrainVertices = 512;
    private const float TerrainSize = 800f;
    private const float WaterHeight = 6f;
    private const int TreeCount = 40_000;
    private const int ShadowResolution = 4096;
    private const int FramesInFlight = 8;

    private double _progress;
    private double _currentFps;

    public double Progress => Volatile.Read(ref _progress);

    public double CurrentFps => Volatile.Read(ref _currentFps);

    public static float TerrainHeightAt(float x, float z)
    {
        float height = 0;
        float amplitude = 1;
        var frequency = 0.005f;
        for (var octave = 0; octave < 6; octave++)
        {
            height += amplitude * ValueNoise(x * frequency + octave * 17.3f, z * frequency - octave * 9.1f);
            amplitude *= 0.5f;
            frequency *= 2.03f;
        }

        height = height * 55 + 10;
        var river = MathF.Sin(z * 0.007f) * 120;
        var distance = MathF.Abs(x - river);
        var valley = MathF.Min(1, distance / 90f);
        return height * (0.25f + 0.75f * valley * valley) - (1 - valley) * 4;
    }

    public static int TreeInstanceCount => TreeCount;

    public GpuSceneResult Run(GpuSceneOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Duration, TimeSpan.FromSeconds(MinimumDurationSeconds));
        var previousPriority = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        var owned = new List<IDisposable>();
        try
        {
            return Render(options, owned, cancellationToken);
        }
        finally
        {
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                owned[i].Dispose();
            }

            Thread.CurrentThread.Priority = previousPriority;
        }
    }

    private GpuSceneResult Render(GpuSceneOptions options, List<IDisposable> owned, CancellationToken cancellationToken)
    {
        T Own<T>(T disposable)
            where T : IDisposable
        {
            owned.Add(disposable);
            return disposable;
        }

        var width = options.Width;
        var height = options.Height;
        var window = Own(RenderWindow.Create("m1vClock · Cena de jogo", width, height, fullscreen: false));
        var factory = Own(DXGI.CreateDXGIFactory1<IDXGIFactory2>());
        var adapter = GpuAdapters.Choose(factory, options.AdapterName, owned);
        var adapterName = adapter.Description1.Description;
        D3D11.D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out ID3D11Device device,
            out ID3D11DeviceContext context).CheckError();
        Own(device);
        Own(context);

        var swap = Own(factory.CreateSwapChainForHwnd(
            device,
            window.Handle,
            new SwapChainDescription1((uint)width, (uint)height, Format.B8G8R8A8_UNorm, false, Usage.RenderTargetOutput, 2, Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapChainFlags.None)));
        factory.MakeWindowAssociation(window.Handle, WindowAssociationFlags.IgnoreAltEnter);
        var backBuffer = Own(swap.GetBuffer<ID3D11Texture2D>(0));

        Texture2DDescription Texture(Format format, int w, int h, BindFlags bind) =>
            new(format, (uint)w, (uint)h, 1, 1, bind, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);

        var hdrDescription = Texture(Format.R16G16B16A16_Float, width, height, BindFlags.RenderTarget | BindFlags.ShaderResource);
        var hdr = Own(device.CreateTexture2D(in hdrDescription));
        var hdrTarget = Own(device.CreateRenderTargetView(hdr));
        var hdrView = Own(device.CreateShaderResourceView(hdr));
        var depthDescription = Texture(Format.D32_Float, width, height, BindFlags.DepthStencil);
        var depth = Own(device.CreateTexture2D(in depthDescription));
        var depthView = Own(device.CreateDepthStencilView(depth));
        var ldrDescription = Texture(Format.B8G8R8A8_UNorm, width, height, BindFlags.RenderTarget);
        var ldr = Own(device.CreateTexture2D(in ldrDescription));
        var ldrTarget = Own(device.CreateRenderTargetView(ldr));
        var shadowDescription = Texture(Format.R32_Typeless, ShadowResolution, ShadowResolution, BindFlags.DepthStencil | BindFlags.ShaderResource);
        var shadowTexture = Own(device.CreateTexture2D(in shadowDescription));
        var shadowDepth = Own(device.CreateDepthStencilView(shadowTexture, new DepthStencilViewDescription(shadowTexture, DepthStencilViewDimension.Texture2D, Format.D32_Float)));
        var shadowView = Own(device.CreateShaderResourceView(shadowTexture, new ShaderResourceViewDescription(shadowTexture, ShaderResourceViewDimension.Texture2D, Format.R32_Float)));

        ReadOnlyMemory<byte> Compile(string entry, string profile) =>
            Compiler.Compile(SceneShaders.Hlsl, entry, "scene.hlsl", profile, ShaderFlags.OptimizationLevel3);

        var terrainCode = Compile("TerrainVS", "vs_5_0");
        var terrainVs = Own(device.CreateVertexShader(terrainCode.Span));
        var treeVs = Own(device.CreateVertexShader(Compile("TreeVS", "vs_5_0").Span));
        var waterVs = Own(device.CreateVertexShader(Compile("WaterVS", "vs_5_0").Span));
        var terrainShadowVs = Own(device.CreateVertexShader(Compile("TerrainShadowVS", "vs_5_0").Span));
        var treeShadowVs = Own(device.CreateVertexShader(Compile("TreeShadowVS", "vs_5_0").Span));
        var fullscreenVs = Own(device.CreateVertexShader(Compile("FullscreenVS", "vs_5_0").Span));
        var terrainPs = Own(device.CreatePixelShader(Compile("TerrainPS", "ps_5_0").Span));
        var treePs = Own(device.CreatePixelShader(Compile("TreePS", "ps_5_0").Span));
        var waterPs = Own(device.CreatePixelShader(Compile("WaterPS", "ps_5_0").Span));
        var skyPs = Own(device.CreatePixelShader(Compile("SkyPS", "ps_5_0").Span));
        var postPs = Own(device.CreatePixelShader(Compile("PostPS", "ps_5_0").Span));
        var layout = Own(device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32_Float, 24, 0),
            ],
            terrainCode.Span));

        var (terrainVertices, terrainIndices) = BuildTerrain();
        var terrainVb = Own(device.CreateBuffer(terrainVertices, BindFlags.VertexBuffer));
        var terrainIb = Own(device.CreateBuffer(terrainIndices, BindFlags.IndexBuffer));
        var (treeVertices, treeIndices) = BuildTree();
        var treeVb = Own(device.CreateBuffer(treeVertices, BindFlags.VertexBuffer));
        var treeIb = Own(device.CreateBuffer(treeIndices, BindFlags.IndexBuffer));
        var trees = PlaceTrees();
        var treeBuffer = Own(device.CreateBuffer(trees, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 0, 16));
        var treeResource = Own(device.CreateShaderResourceView(treeBuffer));
        var half = TerrainSize / 2;
        Vertex[] water =
        [
            new() { Pos = new Vector3(-half, WaterHeight, -half) },
            new() { Pos = new Vector3(half, WaterHeight, -half) },
            new() { Pos = new Vector3(-half, WaterHeight, half) },
            new() { Pos = new Vector3(half, WaterHeight, half) },
        ];
        var waterVb = Own(device.CreateBuffer(water, BindFlags.VertexBuffer));
        var waterIb = Own(device.CreateBuffer(new uint[] { 0, 2, 1, 1, 2, 3 }, BindFlags.IndexBuffer));

        var constants = Own(device.CreateBuffer((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        var mainRaster = Own(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)));
        var shadowRaster = Own(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)
        {
            DepthBias = 800,
            SlopeScaledDepthBias = 2.0f,
            DepthClipEnable = true,
        }));
        var noDepth = Own(device.CreateDepthStencilState(DepthStencilDescription.None));
        var comparisonSampler = Own(device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.ComparisonMinMagLinearMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunc = ComparisonFunction.LessEqual,
            MaxLOD = float.MaxValue,
        }));
        var linearSampler = Own(device.CreateSamplerState(SamplerDescription.LinearClamp));

        var sun = Vector3.Normalize(new Vector3(-0.45f, -0.55f, -0.35f));
        var lightView = Matrix4x4.CreateLookAt(-sun * 900, Vector3.Zero, Vector3.UnitY);
        var lightProjection = Matrix4x4.CreateOrthographic(900, 900, 1, 1900);
        var lightMatrix = lightView * lightProjection;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, width / (float)height, 0.5f, 2000);
        var stride = (uint)Marshal.SizeOf<Vertex>();

        var fences = new ID3D11Query[FramesInFlight];
        for (var i = 0; i < FramesInFlight; i++)
        {
            fences[i] = Own(device.CreateQuery(QueryType.Event, QueryFlags.None));
        }

        var frameTimes = new List<double>(20_000);
        var clock = Stopwatch.StartNew();
        double lastDone = -1;
        double lastPresent = 0;
        double lastTitle = 0;
        var titleFrames = 0;
        long frame = 0;
        var frames = 0;
        var duration = options.Duration;
        Volatile.Write(ref _progress, 0);
        Volatile.Write(ref _currentFps, 0);

        bool Stopping() => cancellationToken.IsCancellationRequested || window.CloseRequested;

        while (!Stopping() && clock.Elapsed < duration)
        {
            RenderWindow.Pump();
            if (Stopping())
            {
                break;
            }

            var fence = fences[frame % FramesInFlight];
            if (frame >= FramesInFlight)
            {
                while (!context.IsDataAvailable(fence) && !Stopping())
                {
                    Thread.SpinWait(20);
                }

                var done = clock.Elapsed.TotalMilliseconds;
                if (lastDone >= 0)
                {
                    frameTimes.Add(done - lastDone);
                }

                lastDone = done;
                frames++;
                titleFrames++;
            }

            var fraction = (float)(clock.Elapsed.TotalSeconds / duration.TotalSeconds);
            var here = PathAt(fraction);
            var ahead = PathAt(fraction + 0.035f);
            var eye = new Vector3(here.X, CameraHeight(here), here.Y);
            var target = new Vector3(ahead.X, CameraHeight(ahead) - 22, ahead.Y);
            var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
            var viewProjection = view * projection;
            Matrix4x4.Invert(viewProjection, out var inverse);
            var data = new FrameConstants
            {
                ViewProj = viewProjection,
                LightViewProj = lightMatrix,
                InvViewProj = inverse,
                CamPos = eye,
                Time = (float)clock.Elapsed.TotalSeconds,
                SunDir = sun,
            };
            var map = context.Map(constants, MapMode.WriteDiscard);
            Marshal.StructureToPtr(data, map.DataPointer, false);
            context.Unmap(constants);

            context.VSSetConstantBuffer(0, constants);
            context.PSSetConstantBuffer(0, constants);
            context.IASetInputLayout(layout);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShaderResource(1, treeResource);

            context.PSSetShaderResource(0, null!);
            context.OMSetRenderTargets((ID3D11RenderTargetView)null!, shadowDepth);
            context.ClearDepthStencilView(shadowDepth, DepthStencilClearFlags.Depth, 1, 0);
            context.RSSetViewport(0, 0, ShadowResolution, ShadowResolution);
            context.RSSetState(shadowRaster);
            context.OMSetDepthStencilState(null);
            context.PSSetShader(null);
            context.VSSetShader(terrainShadowVs);
            context.IASetVertexBuffer(0, terrainVb, stride);
            context.IASetIndexBuffer(terrainIb, Format.R32_UInt, 0);
            context.DrawIndexed((uint)terrainIndices.Length, 0, 0);
            context.VSSetShader(treeShadowVs);
            context.IASetVertexBuffer(0, treeVb, stride);
            context.IASetIndexBuffer(treeIb, Format.R32_UInt, 0);
            context.DrawIndexedInstanced((uint)treeIndices.Length, (uint)trees.Length, 0, 0, 0);

            context.OMSetRenderTargets(hdrTarget, depthView);
            context.ClearDepthStencilView(depthView, DepthStencilClearFlags.Depth, 1, 0);
            context.RSSetViewport(0, 0, width, height);
            context.RSSetState(mainRaster);
            context.PSSetShaderResource(0, shadowView);
            context.PSSetSampler(0, comparisonSampler);
            context.PSSetSampler(1, linearSampler);

            context.OMSetDepthStencilState(noDepth);
            context.VSSetShader(fullscreenVs);
            context.PSSetShader(skyPs);
            context.Draw(3, 0);
            context.OMSetDepthStencilState(null);

            context.VSSetShader(terrainVs);
            context.PSSetShader(terrainPs);
            context.IASetVertexBuffer(0, terrainVb, stride);
            context.IASetIndexBuffer(terrainIb, Format.R32_UInt, 0);
            context.DrawIndexed((uint)terrainIndices.Length, 0, 0);

            context.VSSetShader(treeVs);
            context.PSSetShader(treePs);
            context.IASetVertexBuffer(0, treeVb, stride);
            context.IASetIndexBuffer(treeIb, Format.R32_UInt, 0);
            context.DrawIndexedInstanced((uint)treeIndices.Length, (uint)trees.Length, 0, 0, 0);

            context.VSSetShader(waterVs);
            context.PSSetShader(waterPs);
            context.IASetVertexBuffer(0, waterVb, stride);
            context.IASetIndexBuffer(waterIb, Format.R32_UInt, 0);
            context.DrawIndexed(6, 0, 0);

            context.OMSetRenderTargets(ldrTarget);
            context.OMSetDepthStencilState(noDepth);
            context.PSSetShaderResource(0, null!);
            context.PSSetShaderResource(2, hdrView);
            context.VSSetShader(fullscreenVs);
            context.PSSetShader(postPs);
            context.Draw(3, 0);
            context.PSSetShaderResource(2, null!);

            context.End(fence);
            context.Flush();
            frame++;

            var now = clock.Elapsed.TotalSeconds;
            if (now - lastPresent >= 1 / 60.0)
            {
                context.CopyResource(backBuffer, ldr);
                if (swap.Present(0, PresentFlags.DoNotWait).Success)
                {
                    lastPresent = now;
                }
            }

            if (now - lastTitle >= 0.5)
            {
                var fps = titleFrames / (now - lastTitle);
                Volatile.Write(ref _currentFps, fps);
                titleFrames = 0;
                lastTitle = now;
                var progress = now / duration.TotalSeconds;
                Volatile.Write(ref _progress, Math.Min(1, progress));
                window.SetTitle($"m1vClock · Cena de jogo · {width}x{height} · {fps:0} FPS · {progress:P0}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (window.CloseRequested)
        {
            throw new OperationCanceledException("A janela do teste foi fechada.");
        }

        var seconds = frameTimes.Sum() / 1000;
        return new GpuSceneResult(
            seconds > 0 ? frameTimes.Count / seconds : 0,
            BenchStatistics.LowFps(frameTimes),
            frames,
            clock.Elapsed.TotalSeconds,
            width,
            height,
            adapterName);
    }

    private static float ValueNoise(float x, float z)
    {
        var xi = (int)MathF.Floor(x);
        var zi = (int)MathF.Floor(z);
        var fx = x - xi;
        var fz = z - zi;
        var u = fx * fx * (3 - 2 * fx);
        var v = fz * fz * (3 - 2 * fz);
        var a = Hash(xi, zi);
        var b = Hash(xi + 1, zi);
        var c = Hash(xi, zi + 1);
        var d = Hash(xi + 1, zi + 1);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    private static float Hash(int x, int z)
    {
        var h = unchecked((uint)(x * 374761393 + z * 668265263));
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
    }

    private static (Vertex[] Vertices, uint[] Indices) BuildTerrain()
    {
        var vertices = new Vertex[TerrainVertices * TerrainVertices];
        var step = TerrainSize / (TerrainVertices - 1);
        var half = TerrainSize / 2;
        for (var z = 0; z < TerrainVertices; z++)
        {
            for (var x = 0; x < TerrainVertices; x++)
            {
                var wx = x * step - half;
                var wz = z * step - half;
                var h = TerrainHeightAt(wx, wz);
                var hx = TerrainHeightAt(wx + 0.5f, wz) - TerrainHeightAt(wx - 0.5f, wz);
                var hz = TerrainHeightAt(wx, wz + 0.5f) - TerrainHeightAt(wx, wz - 0.5f);
                vertices[z * TerrainVertices + x] = new Vertex
                {
                    Pos = new Vector3(wx, h, wz),
                    Normal = Vector3.Normalize(new Vector3(-hx, 1, -hz)),
                    Mat = 0,
                };
            }
        }

        var indices = new uint[(TerrainVertices - 1) * (TerrainVertices - 1) * 6];
        var k = 0;
        for (var z = 0; z < TerrainVertices - 1; z++)
        {
            for (var x = 0; x < TerrainVertices - 1; x++)
            {
                var a = (uint)(z * TerrainVertices + x);
                var b = a + 1;
                var c = a + TerrainVertices;
                var d = c + 1;
                indices[k++] = a;
                indices[k++] = c;
                indices[k++] = b;
                indices[k++] = b;
                indices[k++] = c;
                indices[k++] = d;
            }
        }

        return (vertices, indices);
    }

    private static (Vertex[] Vertices, uint[] Indices) BuildTree()
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        void Cone(float baseY, float height, float lowerRadius, float upperRadius, int segments, float material)
        {
            var start = (uint)vertices.Count;
            for (var i = 0; i <= segments; i++)
            {
                var angle = i / (float)segments * MathF.Tau;
                var cos = MathF.Cos(angle);
                var sin = MathF.Sin(angle);
                var slope = (lowerRadius - upperRadius) / height;
                var normal = Vector3.Normalize(new Vector3(cos, slope, sin));
                vertices.Add(new Vertex { Pos = new Vector3(cos * lowerRadius, baseY, sin * lowerRadius), Normal = normal, Mat = material });
                vertices.Add(new Vertex { Pos = new Vector3(cos * upperRadius, baseY + height, sin * upperRadius), Normal = normal, Mat = material });
            }

            for (var i = 0; i < segments; i++)
            {
                var a = start + (uint)i * 2;
                indices.AddRange([a, a + 1, a + 2, a + 2, a + 1, a + 3]);
            }
        }

        Cone(0, 2.0f, 0.25f, 0.2f, 8, 1);
        Cone(1.4f, 2.6f, 2.0f, 0.05f, 12, 2);
        Cone(2.8f, 2.3f, 1.6f, 0.05f, 12, 2);
        Cone(4.0f, 2.0f, 1.1f, 0.02f, 12, 2);
        return ([.. vertices], [.. indices]);
    }

    private static Vector4[] PlaceTrees()
    {
        var list = new List<Vector4>(TreeCount);
        uint state = 2024;

        float Random()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state >> 8) / 16777216f;
        }

        while (list.Count < TreeCount)
        {
            var x = (Random() - 0.5f) * TerrainSize * 0.98f;
            var z = (Random() - 0.5f) * TerrainSize * 0.98f;
            var h = TerrainHeightAt(x, z);
            var slope = MathF.Abs(TerrainHeightAt(x + 1, z) - TerrainHeightAt(x - 1, z)) + MathF.Abs(TerrainHeightAt(x, z + 1) - TerrainHeightAt(x, z - 1));
            if (h < WaterHeight + 1.5f || h > 48 || slope > 1.4f)
            {
                continue;
            }

            list.Add(new Vector4(x, h - 0.2f, z, 0.7f + Random() * 0.9f));
        }

        return [.. list];
    }

    private static readonly Vector2[] CameraPath = BuildPath();

    private static Vector2[] BuildPath()
    {
        var points = new List<Vector2>();
        for (var i = 0; i <= 6; i++)
        {
            var z = -330 + i * 110;
            points.Add(new Vector2(MathF.Sin(z * 0.007f) * 120 + 45, z));
        }

        for (var i = 6; i >= 0; i--)
        {
            var z = -330 + i * 110;
            points.Add(new Vector2(MathF.Sin(z * 0.007f) * 120 - 75, z));
        }

        return [.. points];
    }

    private static Vector2 PathAt(float t)
    {
        var n = CameraPath.Length;
        var f = (t % 1 + 1) % 1 * n;
        var i = (int)f;
        var u = f - i;
        var p0 = CameraPath[(i - 1 + n) % n];
        var p1 = CameraPath[i];
        var p2 = CameraPath[(i + 1) % n];
        var p3 = CameraPath[(i + 2) % n];
        return 0.5f * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u * u * u);
    }

    private static float CameraHeight(Vector2 position)
    {
        var h = WaterHeight;
        for (var dz = -2; dz <= 2; dz++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                h = MathF.Max(h, TerrainHeightAt(position.X + dx * 10, position.Y + dz * 10));
            }
        }

        return h + 26;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Pos;
        public Vector3 Normal;
        public float Mat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 ViewProj;
        public Matrix4x4 LightViewProj;
        public Matrix4x4 InvViewProj;
        public Vector3 CamPos;
        public float Time;
        public Vector3 SunDir;
        public float Pad;
    }
}
