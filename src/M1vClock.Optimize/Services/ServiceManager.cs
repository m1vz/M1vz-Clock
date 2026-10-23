using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using M1vClock.Core.Results;
using M1vClock.Optimize.Startup;
using Microsoft.Win32;

namespace M1vClock.Optimize.Services;

public interface IServiceManager
{
    Task<ReadResult<ServiceReport>> ReadAsync(CancellationToken cancellationToken);

    Task<ApplyOutcome> SetStartModeAsync(ServiceEntry entry, ServiceStartMode mode, CancellationToken cancellationToken);
}

public sealed class ServiceManager(IServiceSource source, IFileDetailsSource files, IChangeJournal journal) : IServiceManager
{
    public const string Module = "Optimize";
    public const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    public static readonly IReadOnlyList<ServiceStartMode> Changeable = [ServiceStartMode.Automatic, ServiceStartMode.Manual, ServiceStartMode.Disabled];

    public static int StartValueOf(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic => 2,
        ServiceStartMode.Manual => 3,
        ServiceStartMode.Disabled => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Só automático, manual e desativado podem ser escolhidos."),
    };

    public static string ModeName(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic => "automático",
        ServiceStartMode.Manual => "manual",
        ServiceStartMode.Disabled => "desativado",
        _ => mode.ToString(),
    };

    public Task<ReadResult<ServiceReport>> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => source.Read(cancellationToken).Map(raw => new ServiceReport(Describe(raw, cancellationToken))), cancellationToken);

    public Task<ApplyOutcome> SetStartModeAsync(ServiceEntry entry, ServiceStartMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.CanChange)
        {
            throw new InvalidOperationException($"O serviço {entry.Name} é protegido e o m1vClock não muda o início dele.");
        }

        if (string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Contains('\\', StringComparison.Ordinal) || entry.Name.Contains('/', StringComparison.Ordinal) || entry.Name.Any(char.IsControl))
        {
            throw new ArgumentException("Nome de serviço inválido.", nameof(entry));
        }

        var address = new RegistryValueAddress(RegistryHive.LocalMachine, RegistryView.Registry64, $@"{ServicesKey}\{entry.Name}", "Start");
        var title = $"Mudar o início do serviço {entry.DisplayName} para {ModeName(mode)}";
        return journal.ApplyAsync(RegistryValueChanges.Set(Module, title, address, RegistryValueState.DWord(StartValueOf(mode))), cancellationToken);
    }

    private List<ServiceEntry> Describe(IReadOnlyList<ServiceRaw> raw, CancellationToken cancellationToken)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var entries = new List<ServiceEntry>(raw.Count);
        foreach (var item in raw)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var display = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : item.DisplayName;
            var image = CommandLineParser.ExecutableOf(item.PathName ?? string.Empty);
            var byPath = ServiceClassifier.OriginOf(image, null, windows);
            var company = byPath == ServiceOrigin.ThirdParty && image is not null ? files.Describe(image).Company : null;
            var origin = ServiceClassifier.OriginOf(image, company, windows);
            entries.Add(new ServiceEntry(
                item.Name,
                display,
                string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
                ServiceClassifier.StateOf(item.State),
                ServiceClassifier.ModeOf(item.StartMode, item.DelayedAutoStart),
                item.Account,
                image,
                origin,
                company,
                ServiceClassifier.ProtectionOf(item.Name, display, company, image, origin)));
        }

        return entries
            .OrderByDescending(e => e.Origin == ServiceOrigin.ThirdParty)
            .ThenByDescending(e => e.IsRunning)
            .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
