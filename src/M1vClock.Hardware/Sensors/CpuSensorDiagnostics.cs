using M1vClock.Core.RegistryValues;
using Microsoft.Win32;

namespace M1vClock.Hardware.Sensors;

public interface IPawnIoProbe
{
    bool IsInstalled { get; }
}

public sealed class RegistryPawnIoProbe(IRegistryAccess registry) : IPawnIoProbe
{
    public static readonly RegistryValueAddress UninstallEntry = new(
        RegistryHive.LocalMachine,
        RegistryView.Registry64,
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO",
        "DisplayName");

    public bool IsInstalled => registry.Read(UninstallEntry).KeyExists;
}

public static class CpuSensorDiagnostics
{
    public static CpuSensorProblem Diagnose(SensorSnapshot? snapshot, bool elevated, bool pawnIoInstalled)
    {
        if (snapshot?.CpuTemperature is not null)
        {
            return CpuSensorProblem.None;
        }

        if (!elevated)
        {
            return CpuSensorProblem.NotElevated;
        }

        return pawnIoInstalled ? CpuSensorProblem.ReadFailed : CpuSensorProblem.PawnIoMissing;
    }
}
