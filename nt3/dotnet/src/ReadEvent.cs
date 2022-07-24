using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WPILib.NT3;

public enum ReadEventType
{
    KeepAlive,
    ServerHello,
    ServerHelloComplete,
    EntryAssignment,
    EntryUpdate,
    EntryFlagsUpdate,
    EntryDelete,
    ClearAllEntires,
    RpcResponse
}

public enum EntryType
{
    Boolean = 0x00,
    Double = 0x01,
    String = 0x02,
    Raw = 0x03,
    BooleanArray = 0x10,
    DoubleArray = 0x11,
    StringArray = 0x12,
    RpcDefinition = 0x20
}

public record class EntryAssignmentEvent(string Name, EntryType Type, ushort Id, ushort SequenceNumber, byte Flags, double ValueStorage, object? ObjectStorage);
[StructLayout(LayoutKind.Auto)]
public record struct EntryUpdateEvent(EntryType Type, ushort Id, ushort SequenceNumber, double ValueStorage, object? ObjectStorage);

[StructLayout(LayoutKind.Auto)]
internal record struct ReadEventMetadataStorage(ReadEventType EventType, EntryType Type, ushort Id, ushort SequenceNumber, double ValueStorage)
{
    public EntryUpdateEvent GetEntryUpdateEvent(object? objectStorage) => new EntryUpdateEvent(Type, Id, SequenceNumber, ValueStorage, objectStorage);
}

[StructLayout(LayoutKind.Auto)]
public readonly struct ReadEvent
{
    private readonly ReadEventMetadataStorage m_storage;

    private object? ObjectStorage { get; }

    public ReadEventType EventType => m_storage.EventType;

    public EntryAssignmentEvent EntryAssignmentEvent
    {
        get
        {
            Debug.Assert(EventType == ReadEventType.EntryAssignment);
            Debug.Assert(ObjectStorage is EntryAssignmentEvent);
            return (EntryAssignmentEvent)ObjectStorage;
        }
    }

    public EntryUpdateEvent EntryUpdateEvent
    {
        get
        {
            Debug.Assert(EventType == ReadEventType.EntryUpdate);
            return m_storage.GetEntryUpdateEvent(ObjectStorage);
        }
    }

    public static ReadEvent CreateKeepAlive()
    {
        return new ReadEvent(ReadEventType.KeepAlive);
    }

    public static ReadEvent CreateServerHello()
    {
        return new ReadEvent(ReadEventType.ServerHello);
    }

    public static ReadEvent CreateServerHelloComplete()
    {
        return new ReadEvent(ReadEventType.ServerHelloComplete);
    }

    public static ReadEvent CreateClearAllEntries()
    {
        return new ReadEvent(ReadEventType.ClearAllEntires);
    }

    public static ReadEvent CreateEntryAssignment(EntryAssignmentEvent assignmentEvent)
    {
        return new ReadEvent(ReadEventType.EntryAssignment, assignmentEvent);
    }

    public static ReadEvent CreateEntryUpdate(in EntryUpdateEvent updateEvent)
    {
        return new ReadEvent(new ReadEventMetadataStorage(ReadEventType.EntryUpdate, updateEvent.Type, updateEvent.Id, updateEvent.SequenceNumber, updateEvent.ValueStorage), updateEvent.ObjectStorage);
    }

    private ReadEvent(ReadEventType eventType, object? objectStorage = null)
    {
        m_storage = new ReadEventMetadataStorage
        {
            EventType = eventType
        };
        ObjectStorage = objectStorage;
    }
    private ReadEvent(in ReadEventMetadataStorage metadata, object? objectStorage = null)
    {
        m_storage = metadata;
        ObjectStorage = objectStorage;
    }
}
