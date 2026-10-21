namespace M1vClock.Hardware.Wmi;

public interface IWmiReader
{
    IReadOnlyList<WmiRow> Query(WmiQuery query);
}
