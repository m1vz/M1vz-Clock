using System.Globalization;
using System.Text.Json;
using M1vClock.Core.IO;
using Microsoft.Extensions.Logging;

namespace M1vClock.Core.RestorePoints;

public enum RestorePointAvailability
{
    Enabled,
    Disabled,
    Unknown,
}

public enum RestorePointCreationStatus
{
    Created,
    SkippedRecentPointExists,
    ProtectionDisabled,
    Failed,
}

public sealed record RestorePointCreation(RestorePointCreationStatus Status, int ErrorCode);

public interface IRestorePointService
{
    Task<RestorePointAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);

    Task<RestorePointCreation> CreateAsync(string description, CancellationToken cancellationToken);
}

public enum RestorePointGateStatus
{
    NotRequired,
    CreatedNow,
    AlreadyCreatedToday,
    RecentPointExists,
    ProtectionDisabled,
    Failed,
}

public sealed record RestorePointGateResult(RestorePointGateStatus Status, string? Detail)
{
    public static RestorePointGateResult NotRequired { get; } = new(RestorePointGateStatus.NotRequired, null);

    public bool IsProtected => Status is RestorePointGateStatus.NotRequired
        or RestorePointGateStatus.CreatedNow
        or RestorePointGateStatus.AlreadyCreatedToday
        or RestorePointGateStatus.RecentPointExists;
}

public interface IRestorePointGate
{
    Task<RestorePointGateResult> EnsureForTodayAsync(CancellationToken cancellationToken);
}

public sealed class RestorePointGate : IRestorePointGate, IDisposable
{
    public const string Description = "m1vClock: antes das alterações de hoje";

    private readonly IRestorePointService _service;
    private readonly string _stateFile;
    private readonly TimeProvider _time;
    private readonly ILogger<RestorePointGate> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public RestorePointGate(IRestorePointService service, string stateFile, TimeProvider time, ILogger<RestorePointGate> logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateFile);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _service = service;
        _stateFile = stateFile;
        _time = time;
        _logger = logger;
    }

    public async Task<RestorePointGateResult> EnsureForTodayAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
            if (ReadLastDate() == today)
            {
                return new RestorePointGateResult(RestorePointGateStatus.AlreadyCreatedToday, null);
            }

            var availability = await _service.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            if (availability == RestorePointAvailability.Disabled)
            {
                CoreLog.RestoreProtectionDisabled(_logger);
                return new RestorePointGateResult(RestorePointGateStatus.ProtectionDisabled, null);
            }

            var creation = await _service.CreateAsync(Description, cancellationToken).ConfigureAwait(false);
            switch (creation.Status)
            {
                case RestorePointCreationStatus.Created:
                    WriteLastDate(today);
                    CoreLog.RestorePointCreated(_logger);
                    return new RestorePointGateResult(RestorePointGateStatus.CreatedNow, null);
                case RestorePointCreationStatus.SkippedRecentPointExists:
                    WriteLastDate(today);
                    CoreLog.RestorePointRecent(_logger);
                    return new RestorePointGateResult(RestorePointGateStatus.RecentPointExists, null);
                case RestorePointCreationStatus.ProtectionDisabled:
                    CoreLog.RestoreProtectionDisabled(_logger);
                    return new RestorePointGateResult(RestorePointGateStatus.ProtectionDisabled, null);
                default:
                    CoreLog.RestorePointFailed(_logger, creation.ErrorCode);
                    return new RestorePointGateResult(RestorePointGateStatus.Failed, string.Create(CultureInfo.InvariantCulture, $"código {creation.ErrorCode}"));
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private DateOnly? ReadLastDate()
    {
        if (!File.Exists(_stateFile))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<GateState>(File.ReadAllText(_stateFile));
            return state?.LastDate;
        }
        catch (JsonException ex)
        {
            CoreLog.RestoreStateUnreadable(_logger, ex, _stateFile);
            return null;
        }
    }

    private void WriteLastDate(DateOnly date) =>
        AtomicFile.WriteAllText(_stateFile, JsonSerializer.Serialize(new GateState(date)));

    private sealed record GateState(DateOnly LastDate);
}
