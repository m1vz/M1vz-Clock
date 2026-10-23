using M1vClock.Core.Results;

namespace M1vClock.Optimize.Services;

public enum ServiceStartMode
{
    Boot,
    System,
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
    Unknown,
}

public enum ServiceRunState
{
    Running,
    Stopped,
    Other,
}

public enum ServiceOrigin
{
    Microsoft,
    ThirdParty,
}

public enum ProtectionReason
{
    None,
    Windows,
    SecuritySoftware,
    AntiCheat,
}

public sealed record ServiceRaw(
    string Name,
    string DisplayName,
    string? Description,
    string State,
    string StartMode,
    bool DelayedAutoStart,
    string? Account,
    string? PathName);

public sealed record ServiceEntry(
    string Name,
    string DisplayName,
    string? Description,
    ServiceRunState State,
    ServiceStartMode StartMode,
    string? Account,
    string? ImagePath,
    ServiceOrigin Origin,
    string? Company,
    ProtectionReason Protection)
{
    public bool CanChange => Protection == ProtectionReason.None;

    public bool IsRunning => State == ServiceRunState.Running;

    public bool StartsAutomatically => StartMode is ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed;
}

public sealed record ServiceReport(IReadOnlyList<ServiceEntry> Services)
{
    public int Total => Services.Count;

    public int ThirdParty => Services.Count(s => s.Origin == ServiceOrigin.ThirdParty);

    public int Running => Services.Count(s => s.IsRunning);

    public int Disabled => Services.Count(s => s.StartMode == ServiceStartMode.Disabled);

    public int AutomaticThirdParty => Services.Count(s => s.Origin == ServiceOrigin.ThirdParty && s.StartsAutomatically);
}

public interface IServiceSource
{
    ReadResult<IReadOnlyList<ServiceRaw>> Read(CancellationToken cancellationToken);
}
