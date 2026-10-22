using System.Globalization;
using M1vClock.App.Localization;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Sensors;

namespace M1vClock.App.Formatting;

public static class SensorFormat
{
    private const double FahrenheitScale = 9.0 / 5.0;
    private const double FahrenheitOffset = 32;

    public static string Value(SensorKind kind, float? value, TemperatureUnit unit = TemperatureUnit.Celsius, CultureInfo? culture = null)
    {
        if (value is not { } number || float.IsNaN(number) || float.IsInfinity(number))
        {
            return Units.Missing;
        }

        var c = culture ?? Loc.Culture;
        return kind switch
        {
            SensorKind.Temperature => unit == TemperatureUnit.Fahrenheit
                ? string.Create(c, $"{number * FahrenheitScale + FahrenheitOffset:0.#} °F")
                : string.Create(c, $"{number:0.#} °C"),
            SensorKind.Load or SensorKind.Control or SensorKind.Level or SensorKind.Humidity => string.Create(c, $"{number:0.#} %"),
            SensorKind.Clock => string.Create(c, $"{number:N0} MHz"),
            SensorKind.Power => string.Create(c, $"{number:0.#} W"),
            SensorKind.Voltage => string.Create(c, $"{number:0.###} V"),
            SensorKind.Current => string.Create(c, $"{number:0.###} A"),
            SensorKind.Fan => string.Create(c, $"{number:N0} RPM"),
            SensorKind.Flow => string.Create(c, $"{number:0.#} L/h"),
            SensorKind.Frequency => string.Create(c, $"{number:0.#} Hz"),
            SensorKind.Data => string.Create(c, $"{number:0.##} GB"),
            SensorKind.SmallData => string.Create(c, $"{number:0.#} MB"),
            SensorKind.Throughput => Units.Bytes(number, c) + "/s",
            SensorKind.Energy => string.Create(c, $"{number:0.#} mWh"),
            SensorKind.Duration => string.Create(c, $"{number:N0} s"),
            SensorKind.Noise => string.Create(c, $"{number:0.#} dBA"),
            SensorKind.Conductivity => string.Create(c, $"{number:0.#} µS/cm"),
            _ => string.Create(c, $"{number:0.##}"),
        };
    }
}
