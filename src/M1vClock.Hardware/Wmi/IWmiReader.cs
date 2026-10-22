namespace M1vClock.Hardware.Wmi;

public sealed record WmiRelation(WmiRow Source, IReadOnlyList<WmiRow> Related);

public interface IWmiReader
{
    IReadOnlyList<WmiRow> Query(WmiQuery query);

    IReadOnlyList<WmiRelation> QueryRelated(WmiQuery source, string relatedClass);
}
