namespace M1vClock.Hardware.Network;

public static class PingStatistics
{
    public static PingStats Summarize(PingRole role, string address, IReadOnlyList<double> roundTrips, int sent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentNullException.ThrowIfNull(roundTrips);
        ArgumentOutOfRangeException.ThrowIfNegative(sent);
        ArgumentOutOfRangeException.ThrowIfLessThan(sent, roundTrips.Count);
        if (roundTrips.Count == 0)
        {
            return new PingStats(role, address, sent, 0, 0, 0, 0, 0);
        }

        return new PingStats(role, address, sent, roundTrips.Count, roundTrips.Min(), roundTrips.Average(), roundTrips.Max(), Jitter(roundTrips));
    }

    public static double Jitter(IReadOnlyList<double> roundTrips)
    {
        ArgumentNullException.ThrowIfNull(roundTrips);
        if (roundTrips.Count < 2)
        {
            return 0;
        }

        var total = 0.0;
        for (var i = 1; i < roundTrips.Count; i++)
        {
            total += Math.Abs(roundTrips[i] - roundTrips[i - 1]);
        }

        return total / (roundTrips.Count - 1);
    }
}
