using System.Text.RegularExpressions;

namespace M1vClock.Hardware.Power;

public static partial class PowerPlanParser
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static PowerPlan? ParseActive(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var match = SchemePattern().Match(output);
        if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out var id))
        {
            return null;
        }

        var name = match.Groups[2].Value.Trim();
        return new PowerPlan(id, name.Length > 0 ? name : id.ToString(), KindOf(id));
    }

    public static PowerPlanKind KindOf(Guid id)
    {
        if (id == Balanced)
        {
            return PowerPlanKind.Balanced;
        }

        if (id == HighPerformance)
        {
            return PowerPlanKind.HighPerformance;
        }

        if (id == PowerSaver)
        {
            return PowerPlanKind.PowerSaver;
        }

        return id == Ultimate ? PowerPlanKind.Ultimate : PowerPlanKind.Custom;
    }

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*(?:\(([^)]*)\))?")]
    private static partial Regex SchemePattern();
}
