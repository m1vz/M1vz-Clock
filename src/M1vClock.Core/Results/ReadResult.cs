using System.Diagnostics.CodeAnalysis;

namespace M1vClock.Core.Results;

public static class ReadResult
{
    public static ReadResult<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ReadResult<T>(value, null);
    }

    public static ReadResult<T> Failure<T>(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new ReadResult<T>(default, error);
    }
}

public readonly record struct ReadResult<T>
{
    private readonly T? _value;

    internal ReadResult(T? value, string? error)
    {
        _value = value;
        Error = error;
    }

    public bool IsSuccess => Error is null;

    public T? Value => _value;

    public string? Error { get; }

    public bool TryGet([NotNullWhen(true)] out T? value)
    {
        value = _value;
        return IsSuccess && value is not null;
    }

    public ReadResult<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess && _value is not null ? ReadResult.Success(map(_value)) : ReadResult.Failure<TOut>(Error ?? "Leitura sem valor.");
    }
}
