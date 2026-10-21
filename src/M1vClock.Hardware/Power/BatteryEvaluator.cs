namespace M1vClock.Hardware.Power;

public static class BatteryEvaluator
{
    public const double WearWorn = 50;
    public const double WearModerate = 30;
    public const double WearNormal = 15;
    public const int CyclesWarning = 1000;
    public const int CyclesNote = 800;
    public const int LowChargePercent = 20;

    public static BatteryAssessment Evaluate(BatteryInfo battery, bool onMains)
    {
        ArgumentNullException.ThrowIfNull(battery);
        var findings = new List<BatteryFinding>();
        BatteryVerdict verdict;
        if (battery.WearPercent is not { } wear)
        {
            verdict = BatteryVerdict.Unknown;
            findings.Add(new BatteryFinding(BatteryFindingKind.NoCapacityData));
        }
        else if (wear >= WearWorn)
        {
            verdict = BatteryVerdict.Worn;
            findings.Add(new BatteryFinding(BatteryFindingKind.WornOut, wear));
        }
        else if (wear >= WearModerate)
        {
            verdict = BatteryVerdict.Warning;
            findings.Add(new BatteryFinding(BatteryFindingKind.Moderate, wear));
        }
        else
        {
            verdict = BatteryVerdict.Good;
            findings.Add(new BatteryFinding(wear >= WearNormal ? BatteryFindingKind.Normal : BatteryFindingKind.Excellent, wear));
        }

        if (battery.Cycles is { } cycles)
        {
            if (cycles >= CyclesWarning && verdict == BatteryVerdict.Good)
            {
                verdict = BatteryVerdict.Warning;
            }

            if (cycles >= CyclesNote)
            {
                findings.Add(new BatteryFinding(BatteryFindingKind.ManyCycles, cycles));
            }
        }

        if (battery.ChargePercent is < LowChargePercent && !onMains)
        {
            findings.Add(new BatteryFinding(BatteryFindingKind.LowCharge, battery.ChargePercent.Value));
        }

        return new BatteryAssessment(verdict, findings);
    }
}
