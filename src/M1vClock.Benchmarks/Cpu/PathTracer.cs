using System.Diagnostics;
using System.Numerics;

namespace M1vClock.Benchmarks.Cpu;

public sealed record PathTraceResult(int Threads, double Seconds, long Samples, int Passes)
{
    public const double ScoreDivisor = 10_000;

    public double Score => Seconds > 0 ? Samples / Seconds / ScoreDivisor : 0;
}

public readonly record struct PathTraceBucket(int X, int Y, int Width, int Height);

public sealed class PathTracer
{
    public const int DefaultWidth = 960;
    public const int DefaultHeight = 540;
    public const int DefaultSamplesPerPixel = 1024;
    public const int BucketSize = 32;

    private const int MaxDepth = 6;
    private const float LampPower = 9f;

    private static readonly Sphere[] Spheres = BuildScene();
    private static readonly Sphere Lamp = new(new Vector3(-4f, 7f, -3f), 2.2f, Material.Light, 0);

    private readonly int _width;
    private readonly int _height;
    private readonly int _samplesPerPixel;
    private readonly int[] _pixels;
    private readonly PathTraceBucket[] _buckets;
    private readonly CameraRig _camera;
    private int[] _active = [];
    private long _samples;
    private int _bucketsDone;

    public PathTracer(int width = DefaultWidth, int height = DefaultHeight, int samplesPerPixel = DefaultSamplesPerPixel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerPixel, 1);
        _width = width;
        _height = height;
        _samplesPerPixel = samplesPerPixel;
        _pixels = new int[width * height];
        _camera = new CameraRig(width, height);
        _buckets = BuildBuckets(width, height);
    }

    public int Width => _width;

    public int Height => _height;

    public int SamplesPerPixel => _samplesPerPixel;

    public IReadOnlyList<PathTraceBucket> Buckets => _buckets;

    public long SamplesDone => Interlocked.Read(ref _samples);

    public int BucketsDone => Volatile.Read(ref _bucketsDone);

    public int BucketsTotal => _buckets.Length;

    public long SamplesPerPass => (long)_width * _height * _samplesPerPixel;

    public void CopyPixelsTo(Span<int> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, _pixels.Length);
        _pixels.CopyTo(destination);
    }

    public void CopyDownsampledPixelsTo(Span<int> destination, int factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        var width = _width / factor;
        var height = _height / factor;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, width * height);
        for (var y = 0; y < height; y++)
        {
            var row = y * factor * _width;
            var target = y * width;
            for (var x = 0; x < width; x++)
            {
                destination[target + x] = _pixels[row + x * factor];
            }
        }
    }

    public int PixelAt(int x, int y) => _pixels[y * _width + x];

    public IReadOnlyList<int> ActiveBuckets()
    {
        var active = Volatile.Read(ref _active);
        var list = new List<int>(active.Length);
        for (var i = 0; i < active.Length; i++)
        {
            var bucket = Volatile.Read(ref active[i]);
            if (bucket >= 0)
            {
                list.Add(bucket);
            }
        }

        return list;
    }

    public PathTraceResult Run(int threads, TimeSpan minimumDuration, CancellationToken cancellationToken, Action? onPass = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threads, 1);
        Interlocked.Exchange(ref _samples, 0);
        var passes = 0;
        var clock = Stopwatch.StartNew();

        do
        {
            RenderPass(threads, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            passes++;
            onPass?.Invoke();
        }
        while (clock.Elapsed < minimumDuration);

        return new PathTraceResult(threads, clock.Elapsed.TotalSeconds, SamplesDone, passes);
    }

    private void RenderPass(int threads, CancellationToken cancellationToken)
    {
        Array.Fill(_pixels, unchecked((int)0xFF000000));
        Volatile.Write(ref _bucketsDone, 0);
        var next = -1;
        var active = new int[threads];
        Array.Fill(active, -1);
        Volatile.Write(ref _active, active);
        var workers = new Thread[threads];
        for (var t = 0; t < threads; t++)
        {
            var index = t;
            workers[t] = new Thread(() =>
            {
                int bucket;
                while (!cancellationToken.IsCancellationRequested && (bucket = Interlocked.Increment(ref next)) < _buckets.Length)
                {
                    Volatile.Write(ref active[index], bucket);
                    RenderBucket(_buckets[bucket], cancellationToken);
                    Interlocked.Increment(ref _bucketsDone);
                }

                Volatile.Write(ref active[index], -1);
            })
            {
                IsBackground = true,
                Name = $"m1vClock bench {index}",
            };
            workers[t].Start();
        }

        foreach (var worker in workers)
        {
            worker.Join();
        }
    }

    private void RenderBucket(PathTraceBucket bucket, CancellationToken cancellationToken)
    {
        for (var y = bucket.Y; y < bucket.Y + bucket.Height; y++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            for (var x = bucket.X; x < bucket.X + bucket.Width; x++)
            {
                var seed = (uint)(y * _width + x) * 9781u + 0x9E3779B9u;
                float sum = 0;
                for (var s = 0; s < _samplesPerPixel; s++)
                {
                    var u = (x + Random(ref seed)) / _width;
                    var v = (y + Random(ref seed)) / _height;
                    sum += MathF.Min(Trace(_camera.Ray(u, v), ref seed), 4f);
                }

                _pixels[y * _width + x] = ToPixel(sum / _samplesPerPixel);
            }

            Interlocked.Add(ref _samples, (long)bucket.Width * _samplesPerPixel);
        }
    }

    private static PathTraceBucket[] BuildBuckets(int width, int height)
    {
        var list = new List<PathTraceBucket>();
        for (var y = 0; y < height; y += BucketSize)
        {
            for (var x = 0; x < width; x += BucketSize)
            {
                list.Add(new PathTraceBucket(x, y, Math.Min(BucketSize, width - x), Math.Min(BucketSize, height - y)));
            }
        }

        return [.. list.OrderBy(b =>
            Math.Pow(b.X + b.Width / 2.0 - width / 2.0, 2) +
            Math.Pow((b.Y + b.Height / 2.0 - height / 2.0) * 1.6, 2))];
    }

    private enum Material : byte
    {
        Diffuse,
        Metal,
        Glass,
        Light,
    }

    private readonly record struct Sphere(Vector3 Center, float Radius, Material Kind, float Albedo, float Roughness = 0);

    private static Sphere[] BuildScene()
    {
        var spheres = new List<Sphere>
        {
            new(new Vector3(0, 1.1f, 0), 1.1f, Material.Glass, 0.98f),
            new(new Vector3(-2.5f, 0.9f, 0.9f), 0.9f, Material.Metal, 0.92f, 0.03f),
            new(new Vector3(2.4f, 0.8f, 0.7f), 0.8f, Material.Diffuse, 0.85f),
            new(new Vector3(1.0f, 0.45f, -1.9f), 0.45f, Material.Metal, 0.85f, 0.25f),
            new(new Vector3(-1.1f, 0.35f, -2.0f), 0.35f, Material.Diffuse, 0.15f),
        };
        for (var i = 0; i < 14; i++)
        {
            var angle = i / 14f * MathF.Tau;
            var kind = (i % 3) switch
            {
                0 => Material.Metal,
                1 => Material.Diffuse,
                _ => Material.Glass,
            };
            spheres.Add(new Sphere(
                new Vector3(MathF.Cos(angle) * 4.2f, 0.28f, MathF.Sin(angle) * 4.2f + 1.5f),
                0.28f,
                kind,
                i % 2 == 0 ? 0.9f : 0.3f,
                0.1f));
        }

        return [.. spheres];
    }

    private sealed class CameraRig
    {
        private static readonly Vector3 Origin = new(0, 2.1f, -7.2f);
        private readonly Vector3 _lower;
        private readonly Vector3 _horizontal;
        private readonly Vector3 _vertical;

        public CameraRig(int width, int height)
        {
            var target = new Vector3(0, 0.75f, 0);
            var verticalFov = 38f * MathF.PI / 180;
            var aspect = (float)width / height;
            var halfHeight = MathF.Tan(verticalFov / 2);
            var halfWidth = aspect * halfHeight;
            var w = Vector3.Normalize(Origin - target);
            var u = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, w));
            var v = Vector3.Cross(w, u);
            _lower = Origin - halfWidth * u + halfHeight * v - w;
            _horizontal = 2 * halfWidth * u;
            _vertical = -2 * halfHeight * v;
        }

        public (Vector3 Origin, Vector3 Direction) Ray(float s, float t) =>
            (Origin, Vector3.Normalize(_lower + s * _horizontal + t * _vertical - Origin));
    }

    private static float Trace((Vector3 Origin, Vector3 Direction) ray, ref uint seed)
    {
        float radiance = 0;
        float throughput = 1;
        var specularBounce = true;
        var (origin, direction) = ray;

        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (!Intersect(origin, direction, out var distance, out var index, out var lamp))
            {
                radiance += throughput * Sky(direction);
                break;
            }

            if (lamp)
            {
                if (specularBounce)
                {
                    radiance += throughput * LampPower;
                }

                break;
            }

            var point = origin + direction * distance;
            Vector3 normal;
            Material material;
            float albedo;
            float roughness = 0;
            if (index < 0)
            {
                normal = Vector3.UnitY;
                material = Material.Diffuse;
                var check = (((int)MathF.Floor(point.X * 0.9f) + (int)MathF.Floor(point.Z * 0.9f)) & 1) == 0;
                albedo = check ? 0.62f : 0.38f;
            }
            else
            {
                var sphere = Spheres[index];
                normal = (point - sphere.Center) / sphere.Radius;
                material = sphere.Kind;
                albedo = sphere.Albedo;
                roughness = sphere.Roughness;
            }

            switch (material)
            {
                case Material.Diffuse:
                    radiance += throughput * albedo * DirectLight(point, normal, ref seed);
                    throughput *= albedo;
                    direction = CosineHemisphere(normal, ref seed);
                    origin = point + normal * 1e-3f;
                    specularBounce = false;
                    break;
                case Material.Metal:
                    var reflected = Vector3.Reflect(direction, normal) + roughness * RandomInSphere(ref seed);
                    if (Vector3.Dot(reflected, normal) <= 0)
                    {
                        return radiance;
                    }

                    direction = Vector3.Normalize(reflected);
                    origin = point + normal * 1e-3f;
                    throughput *= albedo;
                    specularBounce = true;
                    break;
                case Material.Glass:
                    const float refractiveIndex = 1.5f;
                    var outside = Vector3.Dot(direction, normal) < 0;
                    var facing = outside ? normal : -normal;
                    var eta = outside ? 1 / refractiveIndex : refractiveIndex;
                    var cosine = MathF.Min(-Vector3.Dot(direction, facing), 1);
                    var sine2 = eta * eta * (1 - cosine * cosine);
                    var r0 = (1 - refractiveIndex) / (1 + refractiveIndex);
                    r0 *= r0;
                    var fresnel = r0 + (1 - r0) * MathF.Pow(1 - cosine, 5);
                    if (sine2 > 1 || Random(ref seed) < fresnel)
                    {
                        direction = Vector3.Reflect(direction, facing);
                        origin = point + facing * 1e-3f;
                    }
                    else
                    {
                        direction = Vector3.Normalize(eta * direction + (eta * cosine - MathF.Sqrt(1 - sine2)) * facing);
                        origin = point - facing * 1e-3f;
                    }

                    throughput *= albedo;
                    specularBounce = true;
                    break;
                case Material.Light:
                    break;
            }

            if (depth >= 3)
            {
                var stop = MathF.Max(0.05f, 1 - throughput);
                if (Random(ref seed) < stop)
                {
                    break;
                }

                throughput /= 1 - stop;
            }
        }

        return radiance;
    }

    private static float DirectLight(Vector3 point, Vector3 normal, ref uint seed)
    {
        var toCenter = Lamp.Center - point;
        var distance2 = toCenter.LengthSquared();
        var cosMax = MathF.Sqrt(MathF.Max(0, 1 - Lamp.Radius * Lamp.Radius / distance2));
        var w = toCenter / MathF.Sqrt(distance2);

        var r1 = Random(ref seed);
        var r2 = Random(ref seed);
        var cosTheta = 1 - r1 * (1 - cosMax);
        var sinTheta = MathF.Sqrt(1 - cosTheta * cosTheta);
        var phi = MathF.Tau * r2;
        Basis(w, out var a, out var b);
        var direction = Vector3.Normalize(a * (MathF.Cos(phi) * sinTheta) + b * (MathF.Sin(phi) * sinTheta) + w * cosTheta);

        var cosNormal = Vector3.Dot(direction, normal);
        if (cosNormal <= 0)
        {
            return 0;
        }

        if (!Intersect(point + normal * 1e-3f, direction, out _, out _, out var hitLamp) || !hitLamp)
        {
            return 0;
        }

        return LampPower * cosNormal * 2 * (1 - cosMax);
    }

    private static bool Intersect(Vector3 origin, Vector3 direction, out float nearest, out int index, out bool lamp)
    {
        nearest = float.MaxValue;
        index = -2;
        lamp = false;
        if (direction.Y < -1e-6f)
        {
            var t = -origin.Y / direction.Y;
            if (t > 1e-4f && t < 60)
            {
                nearest = t;
                index = -1;
            }
        }

        for (var i = 0; i < Spheres.Length; i++)
        {
            if (HitSphere(Spheres[i], origin, direction, nearest, out var t))
            {
                nearest = t;
                index = i;
            }
        }

        if (HitSphere(Lamp, origin, direction, nearest, out var lampDistance))
        {
            nearest = lampDistance;
            lamp = true;
        }

        return index != -2 || lamp;
    }

    private static bool HitSphere(in Sphere sphere, Vector3 origin, Vector3 direction, float maximum, out float t)
    {
        var offset = origin - sphere.Center;
        var b = Vector3.Dot(offset, direction);
        var c = offset.LengthSquared() - sphere.Radius * sphere.Radius;
        var discriminant = b * b - c;
        t = 0;
        if (discriminant < 0)
        {
            return false;
        }

        var root = MathF.Sqrt(discriminant);
        t = -b - root;
        if (t < 1e-4f)
        {
            t = -b + root;
        }

        return t > 1e-4f && t < maximum;
    }

    private static float Sky(Vector3 direction) => 0.06f + 0.22f * MathF.Max(0, direction.Y);

    private static float Random(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state >> 8) * (1f / 16777216f);
    }

    private static Vector3 RandomInSphere(ref uint state)
    {
        while (true)
        {
            var v = new Vector3(Random(ref state) * 2 - 1, Random(ref state) * 2 - 1, Random(ref state) * 2 - 1);
            if (v.LengthSquared() < 1)
            {
                return v;
            }
        }
    }

    private static Vector3 CosineHemisphere(Vector3 normal, ref uint state)
    {
        var r1 = Random(ref state);
        var r2 = Random(ref state);
        var radius = MathF.Sqrt(r1);
        var phi = MathF.Tau * r2;
        Basis(normal, out var a, out var b);
        return Vector3.Normalize(a * (radius * MathF.Cos(phi)) + b * (radius * MathF.Sin(phi)) + normal * MathF.Sqrt(1 - r1));
    }

    private static void Basis(Vector3 normal, out Vector3 a, out Vector3 b)
    {
        a = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX, normal));
        b = Vector3.Cross(normal, a);
    }

    private static int ToPixel(float value)
    {
        value *= 1.1f;
        value = value * (2.51f * value + 0.03f) / (value * (2.43f * value + 0.59f) + 0.14f);
        var gray = (int)(MathF.Pow(Math.Clamp(value, 0, 1), 1 / 2.2f) * 255 + 0.5f);
        return unchecked((int)0xFF000000) | (gray << 16) | (gray << 8) | gray;
    }
}
