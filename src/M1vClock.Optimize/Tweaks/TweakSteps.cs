using System.Globalization;
using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using Microsoft.Win32;

namespace M1vClock.Optimize.Tweaks;

public sealed record TweakContext(IRegistryAccess Registry, IPowerSchemeAccess Power, IReadOnlyList<JournalEntry> Journal);

public abstract class TweakStep
{
    public bool Decides { get; init; } = true;

    public abstract string Target { get; }

    public virtual bool NeedsAdministrator => false;

    public virtual TweakIssue IssueFor(TweakContext context) => TweakIssue.None;

    public abstract bool? IsOn(TweakContext context);

    public abstract TweakStepChange? ChangeFor(bool turnOn, string title, TweakContext context);
}

public abstract class RegistryStep(RegistryValueAddress address) : TweakStep
{
    public RegistryValueAddress Address { get; } = address;

    public override string Target => Address.Target;

    public override bool NeedsAdministrator => Address.Hive == RegistryHive.LocalMachine;

    protected TweakStepChange Change(string title, string? option, RegistryValueState desired, string? from, string? to) => new(
        RegistryValueChanges.Set(TweakCatalog.Module, title, Address, desired, affectsSystem: NeedsAdministrator),
        option is null ? Address.Display : $"{Address.Display} ({option})",
        from,
        to,
        NeedsAdministrator);

    protected static string? Describe(RegistryValueState state) =>
        state.ValueExists ? Convert.ToString(state.Data, CultureInfo.InvariantCulture) : null;
}

public sealed class DwordStep(RegistryValueAddress address, int on, int off, bool absentIsOn) : RegistryStep(address)
{
    public override bool? IsOn(TweakContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.Registry.Read(Address);
        if (!state.ValueExists)
        {
            return absentIsOn;
        }

        return state.Kind == RegistryValueKind.DWord && Convert.ToInt32(state.Data, CultureInfo.InvariantCulture) == on;
    }

    public override TweakStepChange? ChangeFor(bool turnOn, string title, TweakContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsOn(context) == turnOn)
        {
            return null;
        }

        var current = context.Registry.Read(Address);
        var desired = RegistryValueState.DWord(turnOn ? on : off);
        return Change(title, null, desired, Describe(current), Describe(desired));
    }
}

public sealed class StringStep(RegistryValueAddress address, string on, string off) : RegistryStep(address)
{
    public override bool? IsOn(TweakContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.Registry.Read(Address);
        return state.ValueExists && state.Kind == RegistryValueKind.String && string.Equals(state.Data as string, on, StringComparison.Ordinal);
    }

    public override TweakStepChange? ChangeFor(bool turnOn, string title, TweakContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsOn(context) == turnOn)
        {
            return null;
        }

        var current = context.Registry.Read(Address);
        var desired = RegistryValueState.Text(turnOn ? on : off);
        return Change(title, null, desired, Describe(current), Describe(desired));
    }
}

public sealed class OptionStep(RegistryValueAddress address, string option, string on, string off) : RegistryStep(address)
{
    public static string? OptionOf(string raw, string name)
    {
        ArgumentNullException.ThrowIfNull(raw);
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && string.Equals(part[..separator].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return part[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    public static string WithOption(string raw, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var replaced = false;
        for (var i = 0; i < parts.Count; i++)
        {
            var separator = parts[i].IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && string.Equals(parts[i][..separator].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = $"{name}={value}";
                replaced = true;
            }
        }

        if (!replaced)
        {
            parts.Add($"{name}={value}");
        }

        return string.Concat(parts.Select(p => p + ";"));
    }

    private string Raw(TweakContext context)
    {
        var state = context.Registry.Read(Address);
        return state.ValueExists && state.Kind == RegistryValueKind.String ? state.Data as string ?? string.Empty : string.Empty;
    }

    public override bool? IsOn(TweakContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return string.Equals(OptionOf(Raw(context), option), on, StringComparison.Ordinal);
    }

    public override TweakStepChange? ChangeFor(bool turnOn, string title, TweakContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsOn(context) == turnOn)
        {
            return null;
        }

        var raw = Raw(context);
        var desired = RegistryValueState.Text(WithOption(raw, option, turnOn ? on : off));
        return Change(title, option, desired, OptionOf(raw, option), turnOn ? on : off);
    }
}

public sealed class PowerStep(Guid scheme, Guid fallback) : TweakStep
{
    public const string Where = @"HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\ActivePowerScheme";

    public override string Target => PowerSchemeHandler.Target;

    public override TweakIssue IssueFor(TweakContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Power.Exists(scheme))
        {
            return TweakIssue.NoHighPerformancePlan;
        }

        return context.Power.Active() == scheme && ReturnTo(context) is null ? TweakIssue.NoPlanToReturnTo : TweakIssue.None;
    }

    public override bool? IsOn(TweakContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Power.Active() is { } active ? active == scheme : null;
    }

    public override TweakStepChange? ChangeFor(bool turnOn, string title, TweakContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Power.Active() is not { } active || (active == scheme) == turnOn)
        {
            return null;
        }

        var target = turnOn ? scheme : ReturnTo(context);
        if (target is not { } wanted || !context.Power.Exists(wanted))
        {
            return null;
        }

        var change = new SystemChange(
            PowerSchemeHandler.KindName,
            TweakCatalog.Module,
            title,
            PowerSchemeHandler.Target,
            Where,
            PowerSchemeHandler.AddressOfActive(),
            PowerSchemeHandler.StateOf(wanted),
            AffectsSystem: true);
        return new TweakStepChange(change, Where, NameOrId(context, active), NameOrId(context, wanted), false);
    }

    private static string NameOrId(TweakContext context, Guid id) =>
        context.Power.NameOf(id) ?? id.ToString("D", CultureInfo.InvariantCulture);

    private Guid? ReturnTo(TweakContext context)
    {
        var previous = context.Journal
            .Where(e => e.Module == TweakCatalog.Module && e.Kind == PowerSchemeHandler.KindName && e.CanUndo)
            .Where(e => Guid.TryParse(e.Desired["scheme"]?.GetValue<string>(), out var desired) && desired == scheme)
            .OrderByDescending(e => e.Sequence)
            .Select(e => Guid.TryParse(e.Before["scheme"]?.GetValue<string>(), out var before) ? before : (Guid?)null)
            .FirstOrDefault(before => before is { } id && id != scheme && context.Power.Exists(id));
        if (previous is not null)
        {
            return previous;
        }

        return context.Power.Exists(fallback) ? fallback : null;
    }
}
