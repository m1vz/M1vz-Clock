namespace M1vClock.App.Services.Reports;

public sealed record ReportRow(string Label, string Value);

public sealed record ReportSection(string Id, string Title, IReadOnlyList<ReportRow> Rows, IReadOnlyList<string> Notes)
{
    public bool IsEmpty => Rows.Count == 0 && Notes.Count == 0;
}

public sealed record ReportDocument(DateTimeOffset At, string AppVersion, string? ComputerName, IReadOnlyList<ReportSection> Sections);

public static class ReportSectionIds
{
    public const string System = "system";
    public const string Gpu = "gpu";
    public const string Memory = "memory";
    public const string Storage = "storage";
    public const string Power = "power";
    public const string Events = "events";
    public const string Crashes = "crashes";
    public const string Reliability = "reliability";
    public const string Benchmarks = "benchmarks";

    public static IReadOnlyList<string> All { get; } = [System, Gpu, Memory, Storage, Power, Events, Crashes, Reliability, Benchmarks];
}
