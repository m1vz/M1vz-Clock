using System.Diagnostics;
using M1vClock.Benchmarks.Statistics;

namespace M1vClock.Benchmarks.Cpu;

public sealed record GameSimulationResult(double AverageFps, double LowFps, double MainThreadMilliseconds, double ParallelMilliseconds, int Frames, double Seconds)
{
    public double Score => AverageFps;
}

public sealed class GameSimulation
{
    public const int DefaultEntities = 30_000;
    public const float World = 1000f;
    public static readonly TimeSpan DefaultWarmup = TimeSpan.FromSeconds(2);

    private const int GridSize = 64;
    private const float Cell = World / GridSize;
    private const float Radius = Cell;
    private const float Step60 = 1 / 60f;

    private readonly int _entities;
    private float[] _x;
    private float[] _y;
    private float[] _vx;
    private float[] _vy;
    private float[] _nextX;
    private float[] _nextY;
    private float[] _nextVx;
    private float[] _nextVy;
    private readonly float[] _health;
    private readonly float[] _damage;
    private readonly byte[] _team;
    private readonly int[] _cellStart = new int[GridSize * GridSize + 1];
    private readonly int[] _sorted;
    private readonly int[] _cellOf;
    private readonly float[] _drawKey;
    private readonly int[] _drawList;
    private uint _random;
    private int _frames;
    private int _visible;

    public GameSimulation(int entities = DefaultEntities)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(entities, 2);
        _entities = entities;
        _x = new float[entities];
        _y = new float[entities];
        _vx = new float[entities];
        _vy = new float[entities];
        _nextX = new float[entities];
        _nextY = new float[entities];
        _nextVx = new float[entities];
        _nextVy = new float[entities];
        _health = new float[entities];
        _damage = new float[entities];
        _team = new byte[entities];
        _sorted = new int[entities];
        _cellOf = new int[entities];
        _drawKey = new float[entities];
        _drawList = new int[entities];
        Reset();
    }

    public int Entities => _entities;

    public int FramesDone => Volatile.Read(ref _frames);

    public int VisibleEntities => _visible;

    public double CurrentFps { get; private set; }

    public double Progress { get; private set; }

    public void CopyView(Span<float> x, Span<float> y, Span<byte> team)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(x.Length, _entities);
        ArgumentOutOfRangeException.ThrowIfLessThan(y.Length, _entities);
        ArgumentOutOfRangeException.ThrowIfLessThan(team.Length, _entities);
        _x.AsSpan(0, _entities).CopyTo(x);
        _y.AsSpan(0, _entities).CopyTo(y);
        _team.AsSpan(0, _entities).CopyTo(team);
    }

    public long Checksum()
    {
        long sum = 17;
        for (var i = 0; i < _entities; i++)
        {
            sum = unchecked(sum * 31 + BitConverter.SingleToInt32Bits(_x[i]) + (long)BitConverter.SingleToInt32Bits(_y[i]) * 7);
        }

        return sum;
    }

    public GameSimulationResult Run(TimeSpan duration, TimeSpan warmup, CancellationToken cancellationToken)
    {
        Reset();
        var warm = Stopwatch.StartNew();
        while (warm.Elapsed < warmup && !cancellationToken.IsCancellationRequested)
        {
            AdvanceFrame(out _, out _);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var frameTimes = new List<double>(20_000);
        double mainMs = 0;
        double parallelMs = 0;
        Volatile.Write(ref _frames, 0);
        var clock = Stopwatch.StartNew();
        var window = Stopwatch.StartNew();
        var windowFrames = 0;
        while (clock.Elapsed < duration && !cancellationToken.IsCancellationRequested)
        {
            var start = Stopwatch.GetTimestamp();
            AdvanceFrame(out var main, out var parallel);
            frameTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            mainMs += main;
            parallelMs += parallel;
            Interlocked.Increment(ref _frames);
            windowFrames++;
            if (window.ElapsedMilliseconds >= 500)
            {
                CurrentFps = windowFrames / window.Elapsed.TotalSeconds;
                windowFrames = 0;
                window.Restart();
            }

            Progress = Math.Min(1, clock.Elapsed / duration);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var count = Math.Max(1, frameTimes.Count);
        return new GameSimulationResult(
            frameTimes.Count / clock.Elapsed.TotalSeconds,
            BenchStatistics.LowFps(frameTimes),
            mainMs / count,
            parallelMs / count,
            frameTimes.Count,
            clock.Elapsed.TotalSeconds);
    }

    public void AdvanceFrame(out double mainMilliseconds, out double parallelMilliseconds)
    {
        var t0 = Stopwatch.GetTimestamp();
        BuildGrid();
        var t1 = Stopwatch.GetTimestamp();
        var chunks = Environment.ProcessorCount * 4;
        var perChunk = (_entities + chunks - 1) / chunks;
        Parallel.For(0, chunks, chunk =>
        {
            var end = Math.Min(_entities, (chunk + 1) * perChunk);
            for (var i = chunk * perChunk; i < end; i++)
            {
                Simulate(i);
            }
        });
        var t2 = Stopwatch.GetTimestamp();
        (_x, _nextX) = (_nextX, _x);
        (_y, _nextY) = (_nextY, _y);
        (_vx, _nextVx) = (_nextVx, _vx);
        (_vy, _nextVy) = (_nextVy, _vy);
        GameLogic();
        PrepareDraw();
        var t3 = Stopwatch.GetTimestamp();
        mainMilliseconds = (t1 - t0 + t3 - t2) * 1000.0 / Stopwatch.Frequency;
        parallelMilliseconds = (t2 - t1) * 1000.0 / Stopwatch.Frequency;
    }

    public void Reset()
    {
        _random = 12345;
        for (var i = 0; i < _entities; i++)
        {
            _team[i] = (byte)(i & 1);
            _x[i] = (_team[i] == 0 ? 0.05f : 0.55f) * World + NextRandom() * World * 0.4f;
            _y[i] = NextRandom() * World;
            _vx[i] = (_team[i] == 0 ? 1 : -1) * (20 + NextRandom() * 10);
            _vy[i] = (NextRandom() - 0.5f) * 10;
            _health[i] = 100;
        }

        Volatile.Write(ref _frames, 0);
    }

    private void BuildGrid()
    {
        Array.Clear(_cellStart);
        for (var i = 0; i < _entities; i++)
        {
            var cx = Math.Clamp((int)(_x[i] / Cell), 0, GridSize - 1);
            var cy = Math.Clamp((int)(_y[i] / Cell), 0, GridSize - 1);
            var cell = cy * GridSize + cx;
            _cellOf[i] = cell;
            _cellStart[cell + 1]++;
        }

        for (var c = 0; c < GridSize * GridSize; c++)
        {
            _cellStart[c + 1] += _cellStart[c];
        }

        var fill = (int[])_cellStart.Clone();
        for (var i = 0; i < _entities; i++)
        {
            _sorted[fill[_cellOf[i]]++] = i;
        }
    }

    private void Simulate(int i)
    {
        float x = _x[i], y = _y[i], vx = _vx[i], vy = _vy[i];
        var team = _team[i];
        float separationX = 0, separationY = 0, alignX = 0, alignY = 0, cohesionX = 0, cohesionY = 0, damage = 0;
        var bestDistance2 = Radius * Radius;
        float enemyX = 0, enemyY = 0;
        var allies = 0;
        var enemy = false;

        var home = _cellOf[i];
        int cx = home % GridSize, cy = home / GridSize;
        for (var gy = Math.Max(0, cy - 1); gy <= Math.Min(GridSize - 1, cy + 1); gy++)
        {
            for (var gx = Math.Max(0, cx - 1); gx <= Math.Min(GridSize - 1, cx + 1); gx++)
            {
                var cell = gy * GridSize + gx;
                for (var k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
                {
                    var j = _sorted[k];
                    if (j == i)
                    {
                        continue;
                    }

                    float dx = _x[j] - x, dy = _y[j] - y, d2 = dx * dx + dy * dy;
                    if (d2 > Radius * Radius || d2 < 1e-6f)
                    {
                        continue;
                    }

                    var inverse = 1 / MathF.Sqrt(d2);
                    if (d2 < 9)
                    {
                        separationX -= dx * inverse;
                        separationY -= dy * inverse;
                    }

                    if (_team[j] == team)
                    {
                        alignX += _vx[j];
                        alignY += _vy[j];
                        cohesionX += dx;
                        cohesionY += dy;
                        allies++;
                    }
                    else
                    {
                        if (d2 < 16)
                        {
                            damage += 4 * Step60 * (1 - MathF.Sqrt(d2) / 4);
                        }

                        if (d2 < bestDistance2)
                        {
                            bestDistance2 = d2;
                            enemyX = dx;
                            enemyY = dy;
                            enemy = true;
                        }
                    }
                }
            }
        }

        float ax = separationX * 60, ay = separationY * 60;
        if (allies > 0)
        {
            ax += (alignX / allies - vx) * 0.5f + cohesionX / allies * 0.3f;
            ay += (alignY / allies - vy) * 0.5f + cohesionY / allies * 0.3f;
        }

        if (enemy)
        {
            var inverse = 1 / MathF.Sqrt(bestDistance2);
            float direction = _health[i] < 30 ? -1 : 1;
            ax += direction * enemyX * inverse * 80;
            ay += direction * enemyY * inverse * 80;
        }
        else
        {
            ax += (team == 0 ? 1 : -1) * 25;
        }

        vx += ax * Step60;
        vy += ay * Step60;
        var speed = MathF.Sqrt(vx * vx + vy * vy);
        if (speed > 60)
        {
            vx *= 60 / speed;
            vy *= 60 / speed;
        }

        x += vx * Step60;
        y += vy * Step60;
        if (x < 0 || x > World)
        {
            vx = -vx;
            x = Math.Clamp(x, 0, World);
        }

        if (y < 0 || y > World)
        {
            vy = -vy;
            y = Math.Clamp(y, 0, World);
        }

        _nextX[i] = x;
        _nextY[i] = y;
        _nextVx[i] = vx;
        _nextVy[i] = vy;
        _damage[i] = damage;
    }

    private void GameLogic()
    {
        for (var i = 0; i < _entities; i++)
        {
            _health[i] = MathF.Min(100, _health[i] - _damage[i] + 0.5f * Step60);
            var scored = _team[i] == 0 ? _x[i] > World * 0.97f : _x[i] < World * 0.03f;
            if (_health[i] > 0 && !scored)
            {
                continue;
            }

            _health[i] = 100;
            _x[i] = (_team[i] == 0 ? 0.02f : 0.98f) * World;
            _y[i] = NextRandom() * World;
            _vx[i] = (_team[i] == 0 ? 1 : -1) * 25;
            _vy[i] = 0;
        }
    }

    private void PrepareDraw()
    {
        var cameraX = World / 2 + MathF.Sin(_frames * 0.01f) * 200;
        const float cameraY = -200;
        var visible = 0;
        for (var i = 0; i < _entities; i++)
        {
            float dx = _x[i] - cameraX, dy = _y[i] - cameraY;
            if (dy <= 0 || MathF.Abs(dx) > dy * 1.2f)
            {
                continue;
            }

            _drawKey[visible] = -dy;
            _drawList[visible++] = i;
        }

        Array.Sort(_drawKey, _drawList, 0, visible);
        _visible = visible;
    }

    private float NextRandom()
    {
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return (_random >> 8) * (1f / 16777216f);
    }
}
