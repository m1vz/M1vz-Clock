using M1vClock.Core.RegistryValues;
using M1vClock.Core.RestorePoints;

namespace M1vClock.Core.Tests;

internal sealed class FakeRegistry : IRegistryAccess
{
    private readonly Dictionary<string, RegistryValueState> _values = new(StringComparer.Ordinal);

    public Func<RegistryValueAddress, RegistryValueState, bool>? Rejects { get; set; }

    public Exception? ThrowOnWrite { get; set; }

    public int Writes { get; private set; }

    public void Seed(RegistryValueAddress address, RegistryValueState state) => _values[address.Target] = state;

    public RegistryValueState Read(RegistryValueAddress address) =>
        _values.TryGetValue(address.Target, out var state) ? state : RegistryValueState.Absent(keyExists: false);

    public void Write(RegistryValueAddress address, RegistryValueState state)
    {
        Writes++;
        if (ThrowOnWrite is not null)
        {
            var exception = ThrowOnWrite;
            ThrowOnWrite = null;
            throw exception;
        }

        if (Rejects?.Invoke(address, state) == true)
        {
            return;
        }

        if (state.ValueExists)
        {
            _values[address.Target] = state;
        }
        else
        {
            _values.Remove(address.Target);
        }
    }
}

internal sealed class FakeRestorePointGate : IRestorePointGate
{
    public int Calls { get; private set; }

    public RestorePointGateResult Result { get; set; } = new(RestorePointGateStatus.CreatedNow, null);

    public Task<RestorePointGateResult> EnsureForTodayAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeRestorePointService : IRestorePointService
{
    public RestorePointAvailability Availability { get; set; } = RestorePointAvailability.Enabled;

    public RestorePointCreation Creation { get; set; } = new(RestorePointCreationStatus.Created, 0);

    public int Created { get; private set; }

    public Task<RestorePointAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) => Task.FromResult(Availability);

    public Task<RestorePointCreation> CreateAsync(string description, CancellationToken cancellationToken)
    {
        Created++;
        return Task.FromResult(Creation);
    }
}
