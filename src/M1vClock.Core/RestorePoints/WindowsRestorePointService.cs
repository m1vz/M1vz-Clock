using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Win32Registry = Microsoft.Win32.Registry;

namespace M1vClock.Core.RestorePoints;

public sealed class WindowsRestorePointService(ILogger<WindowsRestorePointService> logger) : IRestorePointService
{
    private const int BeginSystemChange = 100;
    private const int EndSystemChange = 101;
    private const int ModifySettings = 12;
    private const int ErrorServiceDisabled = 1058;
    private const int ErrorTooManyRequests = 1060;

    public Task<RestorePointAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.Run(ReadAvailability, cancellationToken);

    public Task<RestorePointCreation> CreateAsync(string description, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return Task.Run(() => Create(description), cancellationToken);
    }

    private RestorePointAvailability ReadAvailability()
    {
        using var policy = Win32Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore");
        if (policy?.GetValue("DisableSR") is int disabledByPolicy && disabledByPolicy != 0)
        {
            return RestorePointAvailability.Disabled;
        }

        using var key = Win32Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
        return key?.GetValue("RPSessionInterval") switch
        {
            int interval when interval > 0 => RestorePointAvailability.Enabled,
            int => RestorePointAvailability.Disabled,
            _ => RestorePointAvailability.Unknown,
        };
    }

    private RestorePointCreation Create(string description)
    {
        var before = LatestSequence();
        var info = new RestorePointInfo
        {
            EventType = BeginSystemChange,
            RestorePointType = ModifySettings,
            SequenceNumber = 0,
            Description = description.Length > 255 ? description[..255] : description,
        };

        if (!SRSetRestorePointW(ref info, out var status))
        {
            CoreLog.RestorePointRefused(logger, status.Status);
            return status.Status switch
            {
                ErrorServiceDisabled => new RestorePointCreation(RestorePointCreationStatus.ProtectionDisabled, status.Status),
                ErrorTooManyRequests => new RestorePointCreation(RestorePointCreationStatus.SkippedRecentPointExists, status.Status),
                _ => new RestorePointCreation(RestorePointCreationStatus.Failed, status.Status),
            };
        }

        var end = new RestorePointInfo
        {
            EventType = EndSystemChange,
            RestorePointType = ModifySettings,
            SequenceNumber = status.SequenceNumber,
            Description = info.Description,
        };
        _ = SRSetRestorePointW(ref end, out _);

        var after = LatestSequence();
        return after is not null && after != before
            ? new RestorePointCreation(RestorePointCreationStatus.Created, 0)
            : new RestorePointCreation(RestorePointCreationStatus.SkippedRecentPointExists, 0);
    }

    private uint? LatestSequence()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber FROM SystemRestore");
            uint? latest = null;
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                using (item)
                {
                    if (item["SequenceNumber"] is uint sequence && (latest is null || sequence > latest))
                    {
                        latest = sequence;
                    }
                }
            }

            return latest;
        }
        catch (ManagementException ex)
        {
            CoreLog.RestorePointListFailed(logger, ex);
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct RestorePointInfo
    {
        public int EventType;
        public int RestorePointType;
        public long SequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct StateManagerStatus
    {
        public int Status;
        public long SequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SRSetRestorePointW(ref RestorePointInfo info, out StateManagerStatus status);
}
