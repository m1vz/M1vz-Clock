using System.Text.RegularExpressions;

namespace M1vClock.Hardware.Wmi;

public sealed partial record WmiQuery
{
    public const string DefaultScope = @"root\cimv2";

    public WmiQuery(string className, IReadOnlyList<string> properties, string scope = DefaultScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (!Identifier().IsMatch(className) || properties.Count == 0 || properties.Any(p => !Identifier().IsMatch(p)))
        {
            throw new ArgumentException("A consulta WMI só aceita nomes de classe e de propriedade fixos, sem texto livre.", nameof(className));
        }

        ClassName = className;
        Properties = properties;
        Scope = scope;
    }

    public string ClassName { get; }

    public IReadOnlyList<string> Properties { get; }

    public string Scope { get; }

    public string ToWql() => $"SELECT {string.Join(", ", Properties)} FROM {ClassName}";

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
