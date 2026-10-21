using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Hardware.Ram;

namespace M1vClock.App.Pages.Hardware;

public static class RamRows
{
    public static IReadOnlyList<InfoRowViewModel> Summary(RamSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var rows = new List<InfoRowViewModel>
        {
            Row("Hw.Ram.Total", Units.Bytes(summary.TotalBytes)),
            Row("Hw.Ram.Modules", summary.Slots > 0 ? Loc.Format("Hw.Ram.Modules.Value", summary.Installed, summary.Slots) : summary.Installed.ToString(Loc.Culture)),
        };
        if (summary.MaxBytes > 0)
        {
            rows.Add(Row("Hw.Ram.Max", Units.Bytes(summary.MaxBytes)));
        }

        rows.Add(Row("Hw.Ram.Type", string.Join(" / ", summary.Types.Select(TypeText))));
        rows.Add(Row("Hw.Ram.SpeedInUse", Speed(summary.ConfiguredSpeed)));
        rows.Add(Row("Hw.Ram.SpeedRated", Speed(summary.RatedSpeed)));
        rows.Add(Row("Hw.Ram.Profile", ProfileText(summary.Profile)));
        rows.Add(Row("Hw.Ram.Channels", ChannelText(summary.Channels)));
        rows.Add(Row("Hw.Ram.Ecc", summary.Ecc ? Loc.Get("Hw.Ram.Ecc.Yes") : Loc.Get("Hw.Common.No")));
        if (summary.Volts.Count > 0)
        {
            rows.Add(Row("Hw.Ram.Volts", string.Join(" / ", summary.Volts.Select(Volts))));
        }

        rows.Add(Row("Hw.Ram.Timings", Loc.Get("Hw.Ram.Timings.Note")));
        return rows;
    }

    public static IReadOnlyList<InfoRowViewModel> Usage(RamUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var virtualFraction = usage.VirtualTotalBytes == 0 ? 0 : (double)usage.VirtualUsedBytes / usage.VirtualTotalBytes;
        return
        [
            Row("Hw.Ram.Usage.Physical", Loc.Format("Hw.Ram.Usage.Value", Units.Bytes(usage.UsedBytes), Units.Bytes(usage.TotalBytes), Percent(usage.UsedFraction))),
            Row("Hw.Ram.Usage.Virtual", Loc.Format("Hw.Ram.Usage.Value", Units.Bytes(usage.VirtualUsedBytes), Units.Bytes(usage.VirtualTotalBytes), Percent(virtualFraction))),
        ];
    }

    public static IReadOnlyList<InfoRowViewModel> Module(RamModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var rows = new List<InfoRowViewModel>
        {
            Row("Hw.Ram.Module.Capacity", Units.Bytes(module.CapacityBytes)),
            Row("Hw.Ram.Type", TypeText(module.Type)),
            Row("Hw.Ram.Module.Form", FormText(module.FormFactor)),
            Row("Hw.Ram.Module.Ranks", module.Ranks > 0 ? Loc.Get($"Hw.Ram.Rank.{module.Ranks}") : Units.Missing),
            Row("Hw.Ram.SpeedInUse", Speed(module.ConfiguredSpeed)),
            Row("Hw.Ram.SpeedRated", Speed(module.RatedSpeed)),
            Row("Hw.Ram.Volts", module.ConfiguredVolts is { } volts ? Volts(volts) : Units.Missing),
            Row("Hw.Ram.Module.VoltRange", VoltRange(module)),
            Row("Hw.Ram.Module.Width", module.DataWidth > 0 ? Loc.Format("Hw.Ram.Module.Width.Value", module.DataWidth, module.TotalWidth) : Units.Missing),
            Row("Hw.Ram.Module.Manufacturer", Units.OrMissing(module.Manufacturer)),
            Row("Hw.Ram.Module.Part", Units.OrMissing(module.PartNumber)),
            new InfoRowViewModel(Loc.Get("Hw.Ram.Module.Serial"), module.SerialNumber.Display(reveal: true), isSensitive: true),
        };
        return rows;
    }

    public static string ModuleTitle(RamModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var slot = string.IsNullOrWhiteSpace(module.Locator) ? module.Bank : module.Locator;
        var name = string.IsNullOrWhiteSpace(slot) ? Loc.Get("Hw.Ram.Summary") : slot;
        return $"{name} · {Units.Bytes(module.CapacityBytes)}";
    }

    public static string ProfileText(ProfileEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        return estimate.Verdict switch
        {
            ProfileVerdict.SpeedUnreadable => Loc.Get("Hw.Ram.Profile.Unreadable"),
            ProfileVerdict.ProbablyOff => Loc.Format("Hw.Ram.Profile.Off", estimate.ConfiguredSpeed, estimate.LabelSpeed),
            ProfileVerdict.ProbablyOn => Loc.Format("Hw.Ram.Profile.On", estimate.ConfiguredSpeed, estimate.LabelSpeed),
            ProfileVerdict.BelowRated => Loc.Format("Hw.Ram.Profile.Below", estimate.ConfiguredSpeed, estimate.RatedSpeed),
            _ => Loc.Get("Hw.Ram.Profile.None"),
        };
    }

    public static string ChannelText(ChannelEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        return estimate.Verdict switch
        {
            ChannelVerdict.SingleModule => Loc.Get("Hw.Ram.Channels.Single"),
            ChannelVerdict.MultiChannel => Loc.Format("Hw.Ram.Channels.Multi", estimate.Channels),
            ChannelVerdict.SameChannel => Loc.Get("Hw.Ram.Channels.Same"),
            _ => Loc.Format("Hw.Ram.Channels.Unknown", string.Join(", ", estimate.Locators)),
        };
    }

    public static string TypeText(RamType type) => type switch
    {
        RamType.Ddr => "DDR",
        RamType.Ddr2 => "DDR2",
        RamType.Ddr3 => "DDR3",
        RamType.Ddr4 => "DDR4",
        RamType.Ddr5 => "DDR5",
        RamType.LpDdr5 => "LPDDR5",
        _ => Loc.Get("Hw.Common.Unknown"),
    };

    public static string FormText(RamFormFactor form) => form switch
    {
        RamFormFactor.Dimm => "DIMM",
        RamFormFactor.SoDimm => "SO-DIMM",
        RamFormFactor.MicroDimm => "Micro-DIMM",
        RamFormFactor.FbDimm => "FB-DIMM",
        RamFormFactor.Rimm => "RIMM",
        RamFormFactor.Srimm => "SRIMM",
        RamFormFactor.Proprietary => Loc.Get("Hw.Ram.Form.Proprietary"),
        RamFormFactor.Other => Loc.Get("Hw.Ram.Form.Other"),
        _ => Units.Missing,
    };

    private static string Speed(int megatransfers) => megatransfers > 0 ? Loc.Format("Hw.Ram.Speed.Value", megatransfers) : Units.Missing;

    private static string Volts(double volts) => string.Create(Loc.Culture, $"{volts:0.00#} V");

    private static string VoltRange(RamModule module) => module.MinVolts is { } min && module.MaxVolts is { } max
        ? Loc.Format("Hw.Ram.Volts.Range", Volts(min), Volts(max))
        : Units.Missing;

    private static string Percent(double fraction) => fraction.ToString("P0", Loc.Culture);

    private static InfoRowViewModel Row(string labelKey, string value) => new(Loc.Get(labelKey), value);
}
