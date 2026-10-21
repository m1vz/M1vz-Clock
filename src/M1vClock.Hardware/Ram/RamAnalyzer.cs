using System.Globalization;
using System.Text.RegularExpressions;

namespace M1vClock.Hardware.Ram;

public static partial class RamAnalyzer
{
    private const double ProfileTolerance = 0.95;

    public static int SpeedInPartNumber(string? partNumber)
    {
        if (string.IsNullOrWhiteSpace(partNumber))
        {
            return 0;
        }

        var match = SpeedLabel().Match(partNumber);
        return match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : 0;
    }

    public static string? ChannelOf(string? locator)
    {
        if (string.IsNullOrWhiteSpace(locator))
        {
            return null;
        }

        var match = ChannelPattern().Match(locator);
        if (!match.Success)
        {
            return null;
        }

        for (var group = 1; group <= 3; group++)
        {
            if (match.Groups[group].Success)
            {
                return match.Groups[group].Value.ToUpperInvariant();
            }
        }

        return null;
    }

    public static ProfileEstimate EstimateProfile(IReadOnlyList<RamModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var configured = modules.Where(m => m.ConfiguredSpeed > 0).Select(m => m.ConfiguredSpeed).DefaultIfEmpty(0).Min();
        var rated = modules.Select(m => m.RatedSpeed).DefaultIfEmpty(0).Max();
        var label = modules.Select(m => SpeedInPartNumber(m.PartNumber)).DefaultIfEmpty(0).Max();
        var verdict = Classify(configured, rated, label);
        return new ProfileEstimate(verdict, configured, label, rated);
    }

    public static ChannelEstimate EstimateChannels(IReadOnlyList<RamModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var locators = modules.Select(m => m.Locator).ToList();
        if (modules.Count == 1)
        {
            return new ChannelEstimate(ChannelVerdict.SingleModule, 1, locators);
        }

        var channels = modules.Select(m => ChannelOf(m.Locator) ?? ChannelOf(m.Bank)).ToList();
        if (modules.Count == 0 || channels.Any(c => c is null))
        {
            return new ChannelEstimate(ChannelVerdict.Unidentified, 0, locators);
        }

        var distinct = channels.Distinct(StringComparer.Ordinal).Count();
        return distinct >= 2
            ? new ChannelEstimate(ChannelVerdict.MultiChannel, distinct, locators)
            : new ChannelEstimate(ChannelVerdict.SameChannel, 1, locators);
    }

    public static RamSummary Summarize(IReadOnlyList<RamModule> modules, RamBoard board)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(board);
        var profile = EstimateProfile(modules);
        return new RamSummary(
            modules.Count,
            board.Slots,
            modules.Aggregate(0UL, (sum, m) => sum + m.CapacityBytes),
            board.MaxCapacityBytes,
            modules.Select(m => m.Type).Distinct().ToList(),
            profile.ConfiguredSpeed,
            profile.RatedSpeed,
            profile,
            EstimateChannels(modules),
            modules.Any(m => m.HasEcc),
            modules.Where(m => m.ConfiguredVolts is > 0).Select(m => m.ConfiguredVolts!.Value).Distinct().Order().ToList());
    }

    public static RamType TypeOf(int smbiosType) => smbiosType switch
    {
        20 => RamType.Ddr,
        21 => RamType.Ddr2,
        24 => RamType.Ddr3,
        26 => RamType.Ddr4,
        34 => RamType.Ddr5,
        35 => RamType.LpDdr5,
        _ => RamType.Unknown,
    };

    public static RamFormFactor FormFactorOf(int code) => code switch
    {
        0 => RamFormFactor.Unknown,
        8 => RamFormFactor.Dimm,
        12 => RamFormFactor.SoDimm,
        13 => RamFormFactor.MicroDimm,
        15 => RamFormFactor.FbDimm,
        9 => RamFormFactor.Rimm,
        11 => RamFormFactor.Srimm,
        6 => RamFormFactor.Proprietary,
        _ => RamFormFactor.Other,
    };

    public static int RanksOf(int attributes) => attributes is >= 1 and <= 4 ? attributes : 0;

    public static double? VoltsOf(long? millivolts) => millivolts is > 0 ? millivolts.Value / 1000.0 : null;

    private static ProfileVerdict Classify(int configured, int rated, int label)
    {
        if (configured <= 0)
        {
            return ProfileVerdict.SpeedUnreadable;
        }

        if (label > 0 && configured < label * ProfileTolerance)
        {
            return ProfileVerdict.ProbablyOff;
        }

        if (label > 0)
        {
            return ProfileVerdict.ProbablyOn;
        }

        return rated > 0 && configured < rated ? ProfileVerdict.BelowRated : ProfileVerdict.CannotEstimate;
    }

    [GeneratedRegex(@"(?<!\d)(2133|2400|2666|2933|3000|3200|3466|3600|3733|3800|4000|4133|4266|4400|4800|5200|5600|5800|6000|6200|6400|6600|6800|7000|7200|7400|7600|7800|8000|8200|8400|8800)(?!\d)")]
    private static partial Regex SpeedLabel();

    [GeneratedRegex(@"(?i)(?:channel|chan|ch)[\s_-]*([A-H0-9])|dimm[\s_-]*([A-H])\d|(?:^|[^A-Za-z])([A-H])[12](?:$|[^0-9])")]
    private static partial Regex ChannelPattern();
}
