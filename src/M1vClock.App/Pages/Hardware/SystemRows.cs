using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Hardware.Platform;

namespace M1vClock.App.Pages.Hardware;

public static class SystemRows
{
    public static IReadOnlyList<InfoRowViewModel> Os(OsInfo os)
    {
        ArgumentNullException.ThrowIfNull(os);
        var version = os.DisplayVersion is null ? os.Version : $"{os.DisplayVersion} ({os.Version})";
        return
        [
            Row("Hw.System.Os.Name", os.Name),
            Row("Hw.System.Os.Version", version),
            Row("Hw.System.Os.Build", os.FullBuild),
            Row("Hw.System.Os.Architecture", os.Architecture),
            Row("Hw.System.Os.Computer", os.ComputerName),
        ];
    }

    public static IReadOnlyList<InfoRowViewModel> Board(BoardInfo board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return
        [
            Row("Hw.System.Board.Manufacturer", board.Manufacturer),
            Row("Hw.System.Board.Model", board.Model),
            Row("Hw.System.Board.Version", Units.OrMissing(board.Version)),
            new InfoRowViewModel(Loc.Get("Hw.System.Board.Serial"), board.SerialNumber.Display(reveal: true), isSensitive: true),
        ];
    }

    public static IReadOnlyList<InfoRowViewModel> Bios(BiosInfo bios)
    {
        ArgumentNullException.ThrowIfNull(bios);
        return
        [
            Row("Hw.System.Bios.Manufacturer", bios.Manufacturer),
            Row("Hw.System.Bios.Version", bios.Version),
            Row("Hw.System.Bios.Date", Units.Date(bios.ReleaseDate)),
        ];
    }

    public static IReadOnlyList<InfoRowViewModel> Cpu(CpuInfo cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        return
        [
            Row("Hw.System.Cpu.Vendor", cpu.Manufacturer),
            Row("Hw.System.Cpu.Socket", Units.OrMissing(cpu.Socket)),
            Row("Hw.System.Cpu.Cores", Count(cpu.Cores)),
            Row("Hw.System.Cpu.Threads", Count(cpu.Threads)),
            Row("Hw.System.Cpu.Clock", Units.Frequency(cpu.MaxClockMhz)),
            Row("Hw.System.Cpu.L2", cpu.L2CacheKb > 0 ? Units.Kilobytes(cpu.L2CacheKb) : Units.Missing),
            Row("Hw.System.Cpu.L3", cpu.L3CacheKb > 0 ? Units.Kilobytes(cpu.L3CacheKb) : Units.Missing),
            Row("Hw.System.Cpu.Virtualization", Units.YesNo(cpu.VirtualizationEnabled)),
        ];
    }

    private static InfoRowViewModel Row(string labelKey, string value) => new(Loc.Get(labelKey), value);

    private static string Count(int value) => value > 0 ? value.ToString(Loc.Culture) : Units.Missing;
}
