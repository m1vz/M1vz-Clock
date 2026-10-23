using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using M1vClock.Core.Results;
using Microsoft.Win32;

namespace M1vClock.Optimize.Startup;

public interface IStartupService
{
    Task<ReadResult<StartupReport>> ReadAsync(CancellationToken cancellationToken);

    Task<ApplyOutcome> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken cancellationToken);
}

public sealed class StartupService(IStartupSource source, IFileDetailsSource files, IChangeJournal journal, TimeProvider clock) : IStartupService
{
    public const string Module = "Optimize";

    public Task<ReadResult<StartupReport>> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => source.Read().Map(raw => new StartupReport(Describe(raw, cancellationToken))), cancellationToken);

    public Task<ApplyOutcome> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var (hive, keyPath) = StartupApproval.KeyOf(entry.Location);
        var address = new RegistryValueAddress(hive, RegistryView.Registry64, keyPath, entry.Name);
        var bytes = enabled ? StartupApproval.Enabled() : StartupApproval.Disabled(clock.GetUtcNow());
        var state = new RegistryValueState(true, true, RegistryValueKind.Binary, bytes);
        var title = enabled ? $"Ligar na inicialização: {entry.DisplayName}" : $"Desligar da inicialização: {entry.DisplayName}";
        return journal.ApplyAsync(RegistryValueChanges.Set(Module, title, address, state, affectsSystem: entry.Scope == StartupScope.Machine), cancellationToken);
    }

    private List<StartupEntry> Describe(IReadOnlyList<StartupRaw> raw, CancellationToken cancellationToken)
    {
        var entries = new List<StartupEntry>(raw.Count);
        foreach (var item in raw)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executable = CommandLineParser.ExecutableOf(item.Command);
            var details = executable is null ? new FileDetails(false, null, null) : files.Describe(executable);
            entries.Add(new StartupEntry(item.Name, item.Location, item.Command, executable, StartupApproval.IsEnabled(item.Approval), executable is null || !details.Exists, details.Description, details.Company));
        }

        return entries
            .OrderByDescending(e => e.Enabled)
            .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
