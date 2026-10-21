using System.ComponentModel;
using M1vClock.Core.Processes;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Power;

public interface IPowerService
{
    Task<PowerReport> ReadAsync(CancellationToken cancellationToken);
}

public sealed class PowerService(PowerCollector collector, IProcessRunner runner, ILogger<PowerService> logger) : IPowerService
{
    private static readonly TimeSpan PlanTimeout = TimeSpan.FromSeconds(15);

    public async Task<PowerReport> ReadAsync(CancellationToken cancellationToken)
    {
        var batteries = SectionReader.ReadAsync(logger, "baterias", collector.ReadBatteries, cancellationToken);
        var plan = ReadPlanAsync(cancellationToken);
        await Task.WhenAll(batteries, plan).ConfigureAwait(false);
        var read = await batteries.ConfigureAwait(false);
        var source = read.TryGet(out var set) ? set.Source : PowerSource.DesktopMains;
        var reports = read.Map(b => (IReadOnlyList<BatteryReport>)b.Batteries
            .Select(battery => new BatteryReport(battery, BatteryEvaluator.Evaluate(battery, b.OnMains)))
            .ToList());
        return new PowerReport(reports, source, await plan.ConfigureAwait(false));
    }

    private async Task<ReadResult<PowerPlan>> ReadPlanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var request = new ProcessRequest(SystemTools.System32("powercfg.exe"), ["/getactivescheme"]) { Timeout = PlanTimeout };
            var result = await runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return ReadResult.Failure<PowerPlan>($"powercfg terminou com o código {result.ExitCode}.");
            }

            var plan = PowerPlanParser.ParseActive(result.StandardOutput);
            return plan is null ? ReadResult.Failure<PowerPlan>("Não foi possível interpretar a resposta do powercfg.") : ReadResult.Success(plan);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            HardwareLog.SectionFailed(logger, ex, "plano de energia");
            return ReadResult.Failure<PowerPlan>(ex.Message);
        }
    }
}
