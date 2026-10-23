using System.Runtime.InteropServices;

namespace M1vClock.Optimize.Tweaks;

public readonly record struct MouseAcceleration(int Threshold1, int Threshold2, int Speed);

public interface INativeMouse
{
    MouseAcceleration? Read();

    bool Apply(MouseAcceleration value);
}

public sealed partial class WindowsNativeMouse : INativeMouse
{
    private const uint GetMouse = 0x0003;
    private const uint SetMouse = 0x0004;
    private const uint SendChange = 0x0002;

    public MouseAcceleration? Read()
    {
        var values = new MouseParameters();
        return SystemParametersInfo(GetMouse, 0, ref values, 0)
            ? new MouseAcceleration(values.Threshold1, values.Threshold2, values.Speed)
            : null;
    }

    public bool Apply(MouseAcceleration value)
    {
        var values = new MouseParameters { Threshold1 = value.Threshold1, Threshold2 = value.Threshold2, Speed = value.Speed };
        return SystemParametersInfo(SetMouse, 0, ref values, SendChange);
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint parameter, ref MouseParameters value, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseParameters
    {
        public int Threshold1;

        public int Threshold2;

        public int Speed;
    }
}
