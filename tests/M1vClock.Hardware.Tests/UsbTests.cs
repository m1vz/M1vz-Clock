using M1vClock.Hardware.Devices;

namespace M1vClock.Hardware.Tests;

public sealed class UsbDeviceParserTests
{
    private static PnpEntry Entry(string id, string name, string pnpClass, uint problem = 0) => new(id, name, pnpClass, problem);

    private static PnpEntry Composite(string vid, string pid, string instance = "6&3365FBAF&0&5") =>
        Entry($"USB\\VID_{vid}&PID_{pid}\\{instance}", "USB Composite Device", "USB");

    private static PnpEntry Child(string vid, string pid, string mi, string name, string pnpClass, uint problem = 0) =>
        Entry($"USB\\VID_{vid}&PID_{pid}&MI_{mi}\\7&32C8DC79&0&00{mi}", name, pnpClass, problem);

    [Fact]
    public void ACompositeDeviceTakesTheNameOfItsMostDescriptiveInterface()
    {
        var devices = UsbDeviceParser.Parse(
        [
            Composite("3142", "5060"),
            Child("3142", "5060", "00", "Fifine Microphone", "MEDIA"),
            Child("3142", "5060", "02", "Dispositivo de Entrada USB", "HIDClass"),
        ]);

        var device = Assert.Single(devices);
        Assert.Equal("Fifine Microphone", device.Name);
        Assert.Equal(UsbCategory.Audio, device.Category);
        Assert.Equal("3142:5060", device.HardwareId);
        Assert.Null(device.Maker);
        Assert.Null(device.ProblemCode);
        Assert.False(device.HasProblem);
    }

    [Fact]
    public void ADeviceWithOnlyGenericInputInterfacesIsCalledAnInputDeviceNotAComposite()
    {
        var devices = UsbDeviceParser.Parse(
        [
            Composite("1D57", "FA65"),
            Child("1D57", "FA65", "00", "Dispositivo de Entrada USB", "HIDClass"),
            Child("1D57", "FA65", "01", "Dispositivo de Entrada USB", "HIDClass"),
        ]);

        var device = Assert.Single(devices);
        Assert.Equal("Dispositivo de Entrada USB", device.Name);
        Assert.Equal(UsbCategory.Input, device.Category);
    }

    [Fact]
    public void ANamedInputDeviceBeatsTheGenericOne()
    {
        var devices = UsbDeviceParser.Parse(
        [
            Composite("046D", "C52B"),
            Child("046D", "C52B", "00", "Dispositivo de Entrada USB", "HIDClass"),
            Child("046D", "C52B", "01", "Logitech G Pro Mouse", "HIDClass"),
        ]);

        var device = Assert.Single(devices);
        Assert.Equal("Logitech G Pro Mouse", device.Name);
        Assert.Equal("Logitech", device.Maker);
    }

    [Fact]
    public void AWinUsbInterfaceBeatsGenericInputAndKeepsTheMakerOfTheVendorId()
    {
        var devices = UsbDeviceParser.Parse(
        [
            Composite("0B05", "1939", "9876543210"),
            Child("0B05", "1939", "00", "AURA LED Controller", "USBDevice"),
            Child("0B05", "1939", "02", "Dispositivo de Entrada USB", "HIDClass"),
        ]);

        var device = Assert.Single(devices);
        Assert.Equal("AURA LED Controller", device.Name);
        Assert.Equal("ASUS", device.Maker);
        Assert.Equal(UsbCategory.Generic, device.Category);
    }

    [Fact]
    public void ASerialInterfaceGivesTheDeviceItsComPort()
    {
        var devices = UsbDeviceParser.Parse(
        [
            Composite("2341", "8036"),
            Child("2341", "8036", "00", "Arduino Leonardo (COM5)", "Ports"),
            Child("2341", "8036", "02", "Dispositivo de Entrada USB", "HIDClass"),
        ]);

        var device = Assert.Single(devices);
        Assert.Equal("Arduino Leonardo (COM5)", device.Name);
        Assert.Equal("COM5", device.ComPort);
        Assert.Equal("Arduino", device.Maker);
        Assert.Equal(UsbCategory.SerialPort, device.Category);
    }

    [Fact]
    public void ASingleInterfaceSerialAdapterIsTheDeviceItself()
    {
        var devices = UsbDeviceParser.Parse([Entry("USB\\VID_1A86&PID_7523\\5&1A2B3C4D&0&2", "USB-SERIAL CH340 (COM3)", "Ports")]);

        var device = Assert.Single(devices);
        Assert.Equal("COM3", device.ComPort);
        Assert.Equal("CH340/CH341", device.Maker);
    }

    [Fact]
    public void RootHubsAreIgnoredAndOtherHubsComeLast()
    {
        var devices = UsbDeviceParser.Parse(
        [
            Entry("USB\\ROOT_HUB30\\5&2BCE96AA&0&0", "USB Root Hub (USB 3.0)", "USB"),
            Entry("USB\\VID_05E3&PID_0608\\6&3365FBAF&0&11", "Generic USB Hub", "USB"),
            Entry("USB\\VID_0781&PID_5581\\0123456789", "USB Mass Storage Device", "USB"),
            Child("0B05", "1939", "00", "AURA LED Controller", "USBDevice"),
            Composite("0B05", "1939"),
        ]);

        Assert.Equal(["AURA LED Controller", "USB Mass Storage Device", "Generic USB Hub"], devices.Select(d => d.Name));
        Assert.Equal(UsbCategory.Hub, devices[^1].Category);
        Assert.Equal(UsbCategory.Storage, devices[1].Category);
        Assert.Equal("SanDisk", devices[1].Maker);
    }

    [Fact]
    public void AProblemInAnyInterfaceMarksTheWholeDevice()
    {
        var devices = UsbDeviceParser.Parse(
        [
            Composite("2341", "0043"),
            Child("2341", "0043", "00", "Dispositivo desconhecido", "USB", problem: 28),
        ]);

        var device = Assert.Single(devices);
        Assert.Equal(28u, device.ProblemCode);
        Assert.True(device.HasProblem);
    }

    [Fact]
    public void TwoIdenticalDevicesStayTwoEntries()
    {
        var devices = UsbDeviceParser.Parse(
        [
            Entry("USB\\VID_046D&PID_C077\\AAA", "Mouse USB óptico", "HIDClass"),
            Entry("USB\\VID_046D&PID_C077\\BBB", "Mouse USB óptico", "HIDClass"),
        ]);

        Assert.Equal(2, devices.Count);
        Assert.Equal(["USB\\VID_046D&PID_C077\\AAA", "USB\\VID_046D&PID_C077\\BBB"], devices.Select(d => d.InstanceId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NonUsbEntriesAreIgnoredAndTheVendorIdIsNormalized()
    {
        var devices = UsbDeviceParser.Parse(
        [
            Entry("HID\\VID_046D&PID_C52B&MI_00\\7&1&0&0000", "Mouse HID", "HIDClass"),
            Entry("PCI\\VEN_10DE&DEV_2204\\4&1&0&0008", "Placa de vídeo", "Display"),
            Entry("usb\\vid_046d&pid_c52b\\xyz", "Receptor", "USBDevice"),
        ]);

        var device = Assert.Single(devices);
        Assert.Equal("046D", device.Vid);
        Assert.Equal("C52B", device.Pid);
    }

    [Fact]
    public void AnEmptyListGivesNoDevices()
    {
        Assert.Empty(UsbDeviceParser.Parse([]));
    }

    [Theory]
    [InlineData("Ports", "USB Serial", UsbCategory.SerialPort)]
    [InlineData("MEDIA", "Microfone", UsbCategory.Audio)]
    [InlineData("HIDClass", "Teclado", UsbCategory.Input)]
    [InlineData("DiskDrive", "Disco", UsbCategory.Storage)]
    [InlineData("USB", "USB Mass Storage Device", UsbCategory.Storage)]
    [InlineData("USB", "Dispositivo de armazenamento em massa USB", UsbCategory.Storage)]
    [InlineData("Camera", "Webcam", UsbCategory.Imaging)]
    [InlineData("Bluetooth", "Rádio Bluetooth", UsbCategory.Bluetooth)]
    [InlineData("Net", "Adaptador USB", UsbCategory.Network)]
    [InlineData("USB", "Generic USB Hub", UsbCategory.Hub)]
    [InlineData("USBDevice", "Controlador", UsbCategory.Generic)]
    public void ClassAndNameBecomeACategory(string pnpClass, string name, UsbCategory expected)
    {
        Assert.Equal(expected, UsbDeviceParser.CategoryOf(pnpClass, name));
    }

    [Theory]
    [InlineData("046d", "Logitech")]
    [InlineData("1B1C", "Corsair")]
    [InlineData("FFFF", null)]
    [InlineData(null, null)]
    public void VendorsAreLookedUpWithoutCase(string? vid, string? expected)
    {
        Assert.Equal(expected, UsbVendors.Name(vid));
    }
}
