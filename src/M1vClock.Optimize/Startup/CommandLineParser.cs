namespace M1vClock.Optimize.Startup;

public static class CommandLineParser
{
    public static string? ExecutableOf(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            return close > 1 ? text[1..close] : null;
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe >= 0)
        {
            return text[..(exe + 4)];
        }

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? text : text[..space];
    }
}
