namespace M1vClock.Core.Privacy;

public readonly record struct SensitiveText(string? Value)
{
    public const string Mask = "••••••••";

    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    public string Display(bool reveal) => !HasValue ? "—" : reveal ? Value!.Trim() : Mask;

    public override string ToString() => HasValue ? Mask : string.Empty;
}
