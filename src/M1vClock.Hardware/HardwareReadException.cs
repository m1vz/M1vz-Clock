namespace M1vClock.Hardware;

public sealed class HardwareReadException : Exception
{
    public HardwareReadException()
    {
    }

    public HardwareReadException(string message)
        : base(message)
    {
    }

    public HardwareReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
