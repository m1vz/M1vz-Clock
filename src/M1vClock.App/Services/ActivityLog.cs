namespace M1vClock.App.Services;

public sealed record ActivityEntry(DateTimeOffset At, string IconKey, string Title, string Text);

public sealed class ActivityLog(IUiThread ui)
{
    public const int Capacity = 50;

    private readonly Lock _gate = new();
    private readonly List<ActivityEntry> _entries = [];

    public event EventHandler? Changed;

    public IReadOnlyList<ActivityEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public void Add(ActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > Capacity)
            {
                _entries.RemoveRange(Capacity, _entries.Count - Capacity);
            }
        }

        ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
