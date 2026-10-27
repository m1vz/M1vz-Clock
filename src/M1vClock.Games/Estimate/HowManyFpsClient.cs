using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using M1vClock.Core.IO;
using M1vClock.Core.Security;
using M1vClock.Games.Settings;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Estimate;

public enum ReferenceFailure
{
    NotConfigured,
    KeyRefused,
    NotFound,
    QuotaExhausted,
    ServerError,
    BadResponse,
    Network,
}

public sealed class ReferenceFpsException(ReferenceFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public ReferenceFailure Failure { get; } = failure;
}

public sealed record ReferenceFps(double Average, double Low1, bool FromCache);

public sealed record ReferenceQuery(string Cpu, string Gpu, string Game, EstimateResolution Resolution, EstimateUpscaling Upscaling)
{
    public string CacheKey => string.Join('|', Cpu, Gpu, Game, ResolutionText, UpscalingText);

    public string ResolutionText => Resolution switch
    {
        EstimateResolution.QuadHd => "2560x1440",
        EstimateResolution.UltraHd => "3840x2160",
        _ => "1920x1080",
    };

    public string UpscalingText => Upscaling.ToString();
}

public interface IReferenceFpsSource
{
    bool IsConfigured { get; }

    Task<ReferenceFps> GetAsync(ReferenceQuery query, CancellationToken cancellationToken);
}

public static class FpsApiKey
{
    public static string? Read(IGameSettings settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var stored = settings.Read(s => s.ProtectedFpsApiKey);
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        try
        {
            return SecretProtector.Unprotect(stored);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            EstimateLog.KeyUnreadable(logger, ex);
            return null;
        }
    }

    public static void Save(IGameSettings settings, string? key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var text = key?.Trim();
        settings.Update(s => s.ProtectedFpsApiKey = string.IsNullOrEmpty(text) ? null : SecretProtector.Protect(text));
    }
}

public sealed class HowManyFpsClient : IReferenceFpsSource, IDisposable
{
    public static readonly Uri Endpoint = new("https://howmanyfps.com/api");
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions CacheOptions = new() { WriteIndented = false };

    private readonly HttpClient _http;
    private readonly IGameSettings _settings;
    private readonly string _cacheFile;
    private readonly TimeProvider _time;
    private readonly ILogger<HowManyFpsClient> _logger;
    private readonly Lock _gate = new();
    private Dictionary<string, CacheEntry>? _cache;

    public HowManyFpsClient(HttpMessageHandler handler, IGameSettings settings, string cacheFile, TimeProvider time, ILogger<HowManyFpsClient> logger)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFile);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(15) };
        _settings = settings;
        _cacheFile = cacheFile;
        _time = time;
        _logger = logger;
    }

    public bool IsConfigured => FpsApiKey.Read(_settings, _logger) is not null;

    public void Dispose() => _http.Dispose();

    public async Task<ReferenceFps> GetAsync(ReferenceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = _time.GetUtcNow();
        if (Cached(query.CacheKey, now) is { } cached)
        {
            return cached;
        }

        var key = FpsApiKey.Read(_settings, _logger) ?? throw new ReferenceFpsException(ReferenceFailure.NotConfigured, "Chave da API não configurada.");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new Request(query.Cpu, query.Gpu, query.Game, query.ResolutionText, query.UpscalingText)),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        request.Headers.TryAddWithoutValidation("x-api-key", key);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ReferenceFpsException(ReferenceFailure.Network, ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReferenceFpsException(ReferenceFailure.Network, "Tempo esgotado.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var failure = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ReferenceFailure.KeyRefused,
                    HttpStatusCode.NotFound => ReferenceFailure.NotFound,
                    HttpStatusCode.TooManyRequests => ReferenceFailure.QuotaExhausted,
                    _ => ReferenceFailure.ServerError,
                };
                throw new ReferenceFpsException(failure, "HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            }

            Response? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<Response>(cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new ReferenceFpsException(ReferenceFailure.BadResponse, ex.Message, ex);
            }

            if (body?.AverageFps is not { } average || average <= 0 || double.IsNaN(average) || average > 10_000)
            {
                throw new ReferenceFpsException(ReferenceFailure.BadResponse, "Resposta sem avgFps válido.");
            }

            var low = body.Low1Fps is { } l && l > 0 && l <= average ? l : 0;
            Store(query.CacheKey, new CacheEntry(average, low, now));
            return new ReferenceFps(average, low, false);
        }
    }

    private ReferenceFps? Cached(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            var cache = Load();
            return cache.TryGetValue(key, out var entry) && now - entry.Fetched < CacheLifetime ? new ReferenceFps(entry.Average, entry.Low1, true) : null;
        }
    }

    private void Store(string key, CacheEntry entry)
    {
        lock (_gate)
        {
            var cache = Load();
            cache[key] = entry;
            try
            {
                AtomicFile.WriteAllText(_cacheFile, JsonSerializer.Serialize(cache, CacheOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                EstimateLog.CacheWriteFailed(_logger, ex, _cacheFile);
            }
        }
    }

    private Dictionary<string, CacheEntry> Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            _cache = File.Exists(_cacheFile)
                ? JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(_cacheFile), CacheOptions) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            EstimateLog.CacheReadFailed(_logger, ex, _cacheFile);
            _cache = [];
        }

        return _cache;
    }

    private sealed record Request(
        [property: JsonPropertyName("cpu")] string Cpu,
        [property: JsonPropertyName("gpu")] string Gpu,
        [property: JsonPropertyName("game")] string Game,
        [property: JsonPropertyName("resolution")] string Resolution,
        [property: JsonPropertyName("upscaling")] string Upscaling);

    private sealed record Response(
        [property: JsonPropertyName("avgFps")] double? AverageFps,
        [property: JsonPropertyName("min1Fps")] double? Low1Fps);

    private sealed record CacheEntry(double Average, double Low1, DateTimeOffset Fetched);
}

internal static partial class EstimateLog
{
    [LoggerMessage(EventId = 7201, Level = LogLevel.Warning, Message = "Chave da HowManyFPS ilegível (outro usuário do Windows ou arquivo alterado).")]
    public static partial void KeyUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Warning, Message = "Cache de FPS em {File} não pôde ser gravado.")]
    public static partial void CacheWriteFailed(ILogger logger, Exception exception, string file);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Warning, Message = "Cache de FPS em {File} ilegível; começa vazio.")]
    public static partial void CacheReadFailed(ILogger logger, Exception exception, string file);
}
