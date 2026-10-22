namespace M1vClock.Hardware.Devices;

public static class UsbVendors
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2341"] = "Arduino",
        ["2A03"] = "Arduino",
        ["1A86"] = "CH340/CH341",
        ["0403"] = "FTDI",
        ["10C4"] = "CP210x",
        ["303A"] = "Espressif (ESP32)",
        ["239A"] = "Adafruit",
        ["16C0"] = "Teensy",
        ["1B4F"] = "SparkFun",
        ["2E8A"] = "Raspberry Pi",
        ["067B"] = "Prolific PL2303",
        ["0483"] = "STMicroelectronics",
        ["1366"] = "SEGGER",
        ["046D"] = "Logitech",
        ["1532"] = "Razer",
        ["1B1C"] = "Corsair",
        ["0B05"] = "ASUS",
        ["1038"] = "SteelSeries",
        ["0BDA"] = "Realtek",
        ["045E"] = "Microsoft",
        ["05AC"] = "Apple",
        ["054C"] = "Sony",
        ["057E"] = "Nintendo",
        ["28DE"] = "Valve",
        ["0781"] = "SanDisk",
        ["04E8"] = "Samsung",
        ["0951"] = "Kingston",
        ["8087"] = "Intel",
        ["0FD9"] = "Elgato",
        ["05E3"] = "Genesys Logic",
    };

    public static string? Name(string? vendorId) => vendorId is not null && Names.TryGetValue(vendorId, out var name) ? name : null;
}
