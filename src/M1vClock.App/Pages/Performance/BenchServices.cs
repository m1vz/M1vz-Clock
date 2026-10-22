using Microsoft.Win32;
using M1vClock.App.Services;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Performance;

public interface IFileDialogs
{
    string? PickOpen(string title, string filter);

    string? PickSave(string title, string filter, string suggestedName);
}

public sealed class Win32FileDialogs : IFileDialogs
{
    public string? PickOpen(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSave(string title, string filter, string suggestedName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = suggestedName, OverwritePrompt = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

public interface IBenchJobSource
{
    IReadOnlyList<IBenchJob> Create();
}

public sealed class BenchJobFactory(BenchmarkWorkLocation location, ILoggerFactory loggers, IMachineProfile machine) : IBenchJobSource
{
    public IReadOnlyList<IBenchJob> Create() =>
    [
        new CpuRenderJob(),
        new GameSimJob(),
        new MemoryJob(),
        new LoadingJob(location, loggers),
        new DiskJob(location, loggers),
        new GpuSceneJob(machine),
        new RayTracingJob(machine, loggers.CreateLogger<RayTracingJob>()),
    ];
}
