using System.Text.RegularExpressions;

namespace M1vClock.Tuning.Bios;

public enum BoardMaker
{
    Other,
    Asus,
    Gigabyte,
    Msi,
    AsRock,
}

public enum CpuFamily
{
    Unknown,
    AmdAm4,
    AmdAm5,
    IntelUnlocked,
    IntelLocked,
}

public enum GuideStepKind
{
    SaveProfile,
    MemoryProfile,
    PrecisionBoostOverdrive,
    CurveOptimizer,
    BoostOverride,
    EcoMode,
    IntelPowerLimits,
    IntelUndervolt,
    TestEachStep,
}

public sealed record GuideStep(GuideStepKind Kind, string Menu);

public sealed record BiosGuide(BoardMaker Maker, string Board, CpuFamily Family, string MemoryProfileName, IReadOnlyList<GuideStep> Steps);

public static partial class BiosGuides
{
    [GeneratedRegex(@"Ryzen\s+(?:\d\s+)?(?:PRO\s+)?(?<gen>\d)\d{3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex RyzenModel();

    [GeneratedRegex(@"i[3579]-\d{4,5}(?<suffix>[A-Z]{0,2})\b|Core(?:\(TM\))?\s+Ultra\s+[579]\s+\d{3}(?<suffix>[A-Z]{0,2})\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex IntelModel();

    public static BoardMaker MakerOf(string? manufacturer)
    {
        var text = manufacturer ?? string.Empty;
        if (text.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
        {
            return BoardMaker.Asus;
        }

        if (text.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase))
        {
            return BoardMaker.Gigabyte;
        }

        if (text.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || text.Contains("MSI", StringComparison.Ordinal))
        {
            return BoardMaker.Msi;
        }

        return text.Contains("ASRock", StringComparison.OrdinalIgnoreCase) ? BoardMaker.AsRock : BoardMaker.Other;
    }

    public static CpuFamily FamilyOf(string? cpuName)
    {
        var text = cpuName ?? string.Empty;
        var ryzen = RyzenModel().Match(text);
        if (ryzen.Success)
        {
            var generation = ryzen.Groups["gen"].Value[0] - '0';
            return generation >= 7 ? CpuFamily.AmdAm5 : CpuFamily.AmdAm4;
        }

        var intel = IntelModel().Match(text);
        if (intel.Success)
        {
            return intel.Groups["suffix"].Value.Contains('K', StringComparison.Ordinal) ? CpuFamily.IntelUnlocked : CpuFamily.IntelLocked;
        }

        return CpuFamily.Unknown;
    }

    public static BiosGuide Build(string? manufacturer, string? board, string? cpuName)
    {
        var maker = MakerOf(manufacturer);
        var family = FamilyOf(cpuName);
        var memoryName = MemoryProfileName(maker, family);
        var amd = AmdOverclockingMenu(maker);
        var steps = new List<GuideStep>
        {
            new(GuideStepKind.SaveProfile, SaveMenu(maker)),
            new(GuideStepKind.MemoryProfile, MemoryMenu(maker, family)),
        };
        switch (family)
        {
            case CpuFamily.AmdAm4:
            case CpuFamily.AmdAm5:
                steps.Add(new GuideStep(GuideStepKind.PrecisionBoostOverdrive, amd));
                steps.Add(new GuideStep(GuideStepKind.CurveOptimizer, amd + " → Curve Optimizer"));
                steps.Add(new GuideStep(GuideStepKind.BoostOverride, amd));
                steps.Add(new GuideStep(GuideStepKind.EcoMode, amd + " → Eco Mode"));
                break;
            case CpuFamily.IntelUnlocked:
                steps.Add(new GuideStep(GuideStepKind.IntelPowerLimits, IntelPowerMenu(maker)));
                steps.Add(new GuideStep(GuideStepKind.IntelUndervolt, IntelVoltageMenu(maker)));
                break;
            case CpuFamily.IntelLocked:
                steps.Add(new GuideStep(GuideStepKind.IntelPowerLimits, IntelPowerMenu(maker)));
                break;
            default:
                break;
        }

        steps.Add(new GuideStep(GuideStepKind.TestEachStep, string.Empty));
        return new BiosGuide(maker, (board ?? string.Empty).Trim(), family, memoryName, steps);
    }

    private static string MemoryProfileName(BoardMaker maker, CpuFamily family) => family switch
    {
        CpuFamily.AmdAm5 => "EXPO",
        CpuFamily.AmdAm4 => maker switch
        {
            BoardMaker.Asus => "D.O.C.P.",
            BoardMaker.Msi => "A-XMP",
            _ => "XMP",
        },
        _ => "XMP",
    };

    private static string SaveMenu(BoardMaker maker) => maker switch
    {
        BoardMaker.Asus => "Tool → ASUS User Profile",
        BoardMaker.Gigabyte => "Save & Exit → Save Profiles",
        BoardMaker.Msi => "OC Profile → Save to Profile 1",
        BoardMaker.AsRock => "OC Tweaker → Save User UEFI Setup Profile",
        _ => "Save & Exit",
    };

    private static string MemoryMenu(BoardMaker maker, CpuFamily family) => (maker, family) switch
    {
        (BoardMaker.Asus, CpuFamily.AmdAm5) => "Ai Tweaker → Ai Overclock Tuner → EXPO I",
        (BoardMaker.Asus, CpuFamily.AmdAm4) => "Ai Tweaker → Ai Overclock Tuner → D.O.C.P.",
        (BoardMaker.Asus, _) => "Ai Tweaker → Ai Overclock Tuner → XMP I",
        (BoardMaker.Gigabyte, CpuFamily.AmdAm5) => "Tweaker → Extreme Memory Profile (X.M.P.) → EXPO1",
        (BoardMaker.Gigabyte, _) => "Tweaker → Extreme Memory Profile (X.M.P.) → Profile1",
        (BoardMaker.Msi, CpuFamily.AmdAm5) => "OC → A-XMP/EXPO → Profile 1",
        (BoardMaker.Msi, CpuFamily.AmdAm4) => "OC → A-XMP → Profile 1",
        (BoardMaker.Msi, _) => "OC → Extreme Memory Profile (XMP) → Profile 1",
        (BoardMaker.AsRock, CpuFamily.AmdAm5) => "OC Tweaker → DRAM Configuration → Load EXPO Setting",
        (BoardMaker.AsRock, _) => "OC Tweaker → DRAM Configuration → Load XMP Setting",
        _ => "XMP / EXPO / D.O.C.P.",
    };

    private static string AmdOverclockingMenu(BoardMaker maker) => maker switch
    {
        BoardMaker.Asus => "Advanced → AMD Overclocking → Precision Boost Overdrive",
        BoardMaker.Gigabyte => "Tweaker → Advanced CPU Settings → Precision Boost Overdrive",
        BoardMaker.Msi => "OC → Advanced CPU Configuration → AMD Overclocking → Precision Boost Overdrive",
        BoardMaker.AsRock => "Advanced → AMD Overclocking → Precision Boost Overdrive",
        _ => "Advanced → AMD Overclocking → Precision Boost Overdrive",
    };

    private static string IntelPowerMenu(BoardMaker maker) => maker switch
    {
        BoardMaker.Asus => "Ai Tweaker → Internal CPU Power Management",
        BoardMaker.Gigabyte => "Tweaker → Advanced CPU Settings → Turbo Power Limits",
        BoardMaker.Msi => "OC → CPU Features / Advanced CPU Configuration",
        BoardMaker.AsRock => "OC Tweaker → CPU Configuration",
        _ => "CPU Power Management",
    };

    private static string IntelVoltageMenu(BoardMaker maker) => maker switch
    {
        BoardMaker.Asus => "Ai Tweaker → CPU Core/Cache Voltage → Adaptive Mode → Offset",
        BoardMaker.Gigabyte => "Tweaker → Advanced Voltage Settings → CPU Vcore",
        BoardMaker.Msi => "OC → CPU Core Voltage Mode → Adaptive + Offset",
        BoardMaker.AsRock => "OC Tweaker → Voltage Configuration → CPU Vcore Voltage",
        _ => "CPU Core Voltage → Offset",
    };
}
