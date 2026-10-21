using System.Net;

namespace M1vClock.Hardware.Network;

public sealed record PingSeries(IReadOnlyList<double> RoundTrips, int Sent);

public interface INetworkProbe
{
    Task<PingSeries> PingAsync(IPAddress address, int count, TimeSpan interval, CancellationToken cancellationToken);

    Task<double?> ResolveAsync(string host, CancellationToken cancellationToken);

    Task<double?> MeasureDownloadAsync(TimeSpan duration, CancellationToken cancellationToken);

    Task<double?> MeasureUploadAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public static class SpeedTestEndpoints
{
    public const string Host = "speed.cloudflare.com";
    public const int DownloadBytes = 25_000_000;
    public const int UploadBytes = 1_000_000;

    public static Uri Download { get; } = new($"https://{Host}/__down?bytes={DownloadBytes}");

    public static Uri Upload { get; } = new($"https://{Host}/__up");

    public static bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase);
    }

    public static Uri Require(Uri uri)
    {
        if (!IsAllowed(uri))
        {
            throw new HardwareReadException("O teste de velocidade só fala com o servidor autorizado, por HTTPS.");
        }

        return uri;
    }
}
