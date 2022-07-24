using System.Diagnostics;

namespace WPILib.NT3;

public enum WriteEventType
{
    ClientHello,
    ClientHelloComplete
}

public readonly struct WriteEvent
{
    public WriteEventType EventType { get; }
    public object? ObjectStorage { get; }
    public int GetWriteLength()
    {
        switch (EventType)
        {
            case WriteEventType.ClientHello:
                Debug.Assert(ObjectStorage is string);
                return 3 + GetMaxStringLength((string?)ObjectStorage);
            case WriteEventType.ClientHelloComplete:
                return 1;
            default:
                Debug.Assert(false);
                throw new InvalidOperationException("Cannot write unknown type");
        }
    }
    public int WriteTo(Memory<byte> memory)
    {
        var span = memory.Span;
        switch (EventType)
        {
            case WriteEventType.ClientHello:
                Debug.Assert(span.Length >= 4); // 4 is the smallest this can be
                Debug.Assert(ObjectStorage is string);
                span[0] = 0x01;
                span[1] = 0x03;
                span[2] = 0x00;
                return 3 + WriteString(span.Slice(3), (string?)ObjectStorage);
            case WriteEventType.ClientHelloComplete:
                Debug.Assert(span.Length >= 1);
                span[0] = 0x05;
                return 1;
            default:
                Debug.Assert(false);
                throw new InvalidOperationException("Cannot write unknown type");
        }
    }

    private int GetMaxStringLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1;
        }
        throw new NotSupportedException();
    }

    private int WriteString(Span<byte> output, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            output[0] = 0;
            return 1;
        }
        throw new NotSupportedException(); // Can only currently write empty string
    }


    private WriteEvent(WriteEventType eventType, object? objectStorage = null)
    {
        EventType = eventType;
        ObjectStorage = objectStorage;
    }

    public static WriteEvent CreateClientHello(string? clientName)
    {
        return new WriteEvent(WriteEventType.ClientHello, clientName);
    }

    public static WriteEvent CreateClientHelloComplete()
    {
        return new WriteEvent(WriteEventType.ClientHelloComplete);
    }
}

