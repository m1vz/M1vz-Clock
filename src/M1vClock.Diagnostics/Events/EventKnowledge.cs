namespace M1vClock.Diagnostics.Events;

public static class EventKnowledge
{
    private static readonly Dictionary<(string Provider, int Id), KnownEvent> Exact = Build();

    private static readonly Dictionary<string, KnownEvent> WholeProvider = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft-Windows-WHEA-Logger"] = new("Whea", KnownEventKind.Hardware),
        ["nvlddmkm"] = new("GpuDriver", KnownEventKind.Display),
        ["amdkmdag"] = new("GpuDriver", KnownEventKind.Display),
        ["amdwddmg"] = new("GpuDriver", KnownEventKind.Display),
        ["Microsoft-Windows-Perflib"] = new("Perflib", KnownEventKind.Noise),
    };

    public static IReadOnlyCollection<string> AllKeys { get; } = Exact.Values.Concat(WholeProvider.Values).Select(k => k.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    public static KnownEvent? Find(string provider, int id)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (Exact.TryGetValue((provider.ToUpperInvariant(), id), out var exact))
        {
            return exact;
        }

        return WholeProvider.TryGetValue(provider, out var whole) ? whole : null;
    }

    private static Dictionary<(string, int), KnownEvent> Build()
    {
        var map = new Dictionary<(string, int), KnownEvent>();
        void Add(string provider, int id, string key, KnownEventKind kind) => map[(provider.ToUpperInvariant(), id)] = new KnownEvent(key, kind);

        Add("Microsoft-Windows-Kernel-Power", 41, "KernelPower41", KnownEventKind.UnexpectedShutdown);
        Add("EventLog", 6008, "UnexpectedShutdown6008", KnownEventKind.UnexpectedShutdown);
        Add("Microsoft-Windows-WER-SystemErrorReporting", 1001, "BugCheck1001", KnownEventKind.UnexpectedShutdown);
        Add("disk", 7, "DiskBadBlock", KnownEventKind.Disk);
        Add("disk", 11, "DiskController", KnownEventKind.Disk);
        Add("disk", 51, "DiskPaging", KnownEventKind.Disk);
        Add("disk", 153, "DiskRetried", KnownEventKind.Disk);
        Add("Ntfs", 55, "NtfsCorruption", KnownEventKind.Disk);
        Add("stornvme", 129, "NvmeReset", KnownEventKind.Disk);
        Add("Display", 4101, "DisplayTimeout", KnownEventKind.Display);
        Add("Microsoft-Windows-Kernel-PnP", 219, "DriverNotLoaded", KnownEventKind.Hardware);
        Add("TPM", 14, "TpmProvisioning", KnownEventKind.Hardware);
        Add("Microsoft-Windows-DistributedCOM", 10016, "DcomPermission", KnownEventKind.Noise);
        Add("Microsoft-Windows-DistributedCOM", 10010, "DcomNoResponse", KnownEventKind.Service);
        Add("ESENT", 455, "EsentFile", KnownEventKind.Noise);
        Add("ESENT", 490, "EsentFile", KnownEventKind.Noise);
        Add("Microsoft-Windows-Time-Service", 135, "TimeSync", KnownEventKind.Service);
        Add("Service Control Manager", 7000, "ServiceFailedToStart", KnownEventKind.Service);
        Add("Service Control Manager", 7009, "ServiceTimeout", KnownEventKind.Service);
        Add("Service Control Manager", 7023, "ServiceTerminated", KnownEventKind.Service);
        Add("Service Control Manager", 7031, "ServiceCrashed", KnownEventKind.Service);
        Add("Service Control Manager", 7034, "ServiceCrashed", KnownEventKind.Service);
        Add("Microsoft-Windows-FilterManager", 11, "FilterAttach", KnownEventKind.Service);
        Add("VSS", 8193, "VssError", KnownEventKind.Service);
        Add("VSS", 13, "VssError", KnownEventKind.Service);
        Add("Volsnap", 24, "VolsnapError", KnownEventKind.Service);
        Add("Volsnap", 35, "VolsnapError", KnownEventKind.Service);
        Add("Application Error", 1000, "AppCrash", KnownEventKind.Application);
        Add(".NET Runtime", 1000, "DotNetCrash", KnownEventKind.Application);
        Add("Application Hang", 1002, "AppHang", KnownEventKind.Application);
        Add("Windows Error Reporting", 1001, "AppReport", KnownEventKind.Application);
        Add("Microsoft-Windows-WindowsUpdateClient", 20, "UpdateFailed", KnownEventKind.Update);
        Add("Microsoft-Windows-WindowsUpdateClient", 25, "UpdateFailed", KnownEventKind.Update);
        return map;
    }
}
