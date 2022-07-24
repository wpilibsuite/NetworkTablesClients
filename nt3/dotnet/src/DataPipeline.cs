using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace WPILib.NT3;

internal class DataPipeline : IAsyncDisposable
{
    private readonly IDuplexChannel<WriteEvent, ReadEvent> m_dataChannel;
    private readonly IDuplexPipe m_socketPipe;

    private readonly Task m_pipeToChannelTask;
    private readonly Task m_channelToPipeTask;


    public DataPipeline(IDuplexChannel<WriteEvent, ReadEvent> dataChannel, IDuplexPipe socketPipe)
    {
        ArgumentNullException.ThrowIfNull(dataChannel);
        ArgumentNullException.ThrowIfNull(socketPipe);

        m_dataChannel = dataChannel;
        m_socketPipe = socketPipe;

        m_pipeToChannelTask = ReadPipeIntoChannel();
        m_channelToPipeTask = WriteChannelToPipe();
    }

    public async ValueTask CloseAsync()
    {
        await m_channelToPipeTask;
        await m_pipeToChannelTask;
    }

    public async ValueTask DisposeAsync()
    {
        // TODO
    }

    private async Task WriteChannelToPipe()
    {
        var writer = m_socketPipe.Output;

        await foreach (var item in m_dataChannel.Input.ReadAllAsync())
        {
            int maxWriteLength = item.GetWriteLength();
            Memory<byte> memory = writer.GetMemory(maxWriteLength);
            try
            {
                int written = item.WriteTo(memory);
                writer.Advance(written);
            }
            catch (Exception ex)
            {
                // TODO log
                break;
            }

            FlushResult result = await writer.FlushAsync();

            if (result.IsCompleted)
            {
                break;
            }
        }
        await writer.CompleteAsync().ConfigureAwait(false);
    }

    private async Task ReadPipeIntoChannel()
    {
        var pipeReader = m_socketPipe.Input;
        int minimumNeededBytes = 1;
        while (true)
        {
            ReadResult result = await pipeReader.ReadAtLeastAsync(minimumNeededBytes);
            var buffer = result.Buffer;

            try
            {
                while (true)
                {
                    minimumNeededBytes = TryParseMessage(ref buffer, out var ntEvent);
                    if (minimumNeededBytes != 1)
                    {
                        Debug.Assert(ntEvent == null);
                        break;
                    }
                    if (ntEvent != null)
                    {
                        await m_dataChannel.Output.WriteAsync(ntEvent.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                // TODO log
                break;
            }

            pipeReader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                break;
            }
        }
        await pipeReader.CompleteAsync();
        m_dataChannel.Output.Complete();

    }

    private byte[]? currentByteRead = null;

    private bool TryRead(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out byte value)
    {
        minimumNecessaryBytes++;
        return reader.TryRead(out value);
    }

    private bool TryRead(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out ushort value)
    {
        minimumNecessaryBytes += 2;
        if (!reader.TryReadBigEndian(out short ivalue))
        {
            value = 0;
            return false;
        }
        value = (ushort)ivalue;
        return true;
    }

    private bool TryRead(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out uint value)
    {
        minimumNecessaryBytes += 4;
        if (!reader.TryReadBigEndian(out int ivalue))
        {
            value = 0;
            return false;
        }
        value = (uint)ivalue;
        return true;
    }

    private bool TryRead(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out long value)
    {
        minimumNecessaryBytes += 8;
        if (!reader.TryReadBigEndian(out long ivalue))
        {
            value = 0;
            return false;
        }
        value = ivalue;
        return true;
    }

    private bool TryReadByteArray(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, int length, [NotNullWhen(true)] out byte[]? value)
    {
        if (length == 0)
        {
            value = Array.Empty<byte>();
            return true;
        }
        if (currentByteRead == null)
        {
            currentByteRead = new byte[length];
        }
        Debug.Assert(currentByteRead.Length == length);
        minimumNecessaryBytes += length;
        if (!reader.TryCopyTo(currentByteRead.AsSpan()))
        {
            value = null;
            return false;
        }
        value = currentByteRead;
        currentByteRead = null;
        return true;
    }

    private bool TryReadLeb128(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out int value)
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            if (!TryRead(ref reader, ref minimumNecessaryBytes, out byte next))
            {
                value = 0;
                return false;
            }
            result |= (ulong)(next & 0x7F) << shift;
            shift += 7;

            if ((next & 0x80) == 0)
            {
                break;
            }
        }
        Debug.Assert(result <= int.MaxValue);
        value = (int)(result & 0x7FFFFFFF);
        return true;
    }

    private bool TryReadRaw(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, [NotNullWhen(true)] out byte[]? value)
    {
        if (!TryReadLeb128(ref reader, ref minimumNecessaryBytes, out var length))
        {
            value = null;
            return false;
        }
        if (length == 0)
        {
            value = Array.Empty<byte>();
            return true;
        }
        return TryReadByteArray(ref reader, ref minimumNecessaryBytes, length, out value);
    }

    private bool TryReadString(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, [NotNullWhen(true)] out string? value)
    {
        if (!TryReadLeb128(ref reader, ref minimumNecessaryBytes, out var length))
        {
            value = null;
            return false;
        }
        if (length == 0)
        {
            value = "";
            return true;
        }
        if (!TryReadByteArray(ref reader, ref minimumNecessaryBytes, length, out var bytes))
        {
            value = null;
            return false;
        }
        value = Encoding.UTF8.GetString(bytes);
        return true;
    }

    private bool TryParseServerHello(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out ReadEvent? ntEvent)
    {
        if (!TryRead(ref reader, ref minimumNecessaryBytes, out byte _))
        {
            ntEvent = null;
            return false;
        }
        if (!TryReadString(ref reader, ref minimumNecessaryBytes, out var _))
        {
            ntEvent = null;
            return false;
        }
        ntEvent = ReadEvent.CreateServerHello();
        return true;
    }

    private bool TryParseClearAllEntries(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out ReadEvent? ntEvent)
    {
        if (!TryRead(ref reader, ref minimumNecessaryBytes, out uint magic))
        {
            ntEvent = null;
            return false;
        }
        if (magic == 0xD06CB27A)
        {
            ntEvent = ReadEvent.CreateClearAllEntries();
        }
        else
        {
            ntEvent = null;
        }
        return true;
    }



    private bool TryParseEntryValue(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, EntryType type, out (double, object?) value)
    {
        switch (type)
        {
            case EntryType.Boolean:
                {
                    if (!TryRead(ref reader, ref minimumNecessaryBytes, out byte boolValue))
                    {
                        value = (0, null);
                        return false;
                    }
                    value = (boolValue, null);
                    return true;
                }
            case EntryType.Double:
                {
                    if (!TryRead(ref reader, ref minimumNecessaryBytes, out long longValue))
                    {
                        value = (0, null);
                        return false;
                    }
                    value = (BitConverter.Int64BitsToDouble(longValue), null);
                    return true;
                }
            case EntryType.String:
                {
                    if (!TryReadString(ref reader, ref minimumNecessaryBytes, out string? stringValue))
                    {
                        value = (0, null);
                        return false;
                    }
                    Debug.Assert(stringValue != null);
                    value = (0, stringValue);
                    return true;
                }
            case EntryType.Raw:
                {
                    if (!TryReadRaw(ref reader, ref minimumNecessaryBytes, out byte[]? byteArrValue))
                    {
                        value = (0, null);
                        return false;
                    }
                    Debug.Assert(byteArrValue != null);
                    value = (0, byteArrValue);
                    return true;
                }
            case EntryType.BooleanArray:
            case EntryType.DoubleArray:
            case EntryType.StringArray:
            case EntryType.RpcDefinition:
            default:
                throw new InvalidDataException();
        }
    }

    private bool TryParseEntryAssignment(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out ReadEvent? ntEvent)
    {
        if (!TryReadString(ref reader, ref minimumNecessaryBytes, out var entryName))
        {
            ntEvent = null;
            return false;
        }
        if (!TryRead(ref reader, ref minimumNecessaryBytes, out byte entryType))
        {
            ntEvent = null;
            return false;
        }
        if (!TryRead(ref reader, ref minimumNecessaryBytes, out ushort entryId))
        {
            ntEvent = null;
            return false;
        }
        if (!TryRead(ref reader, ref minimumNecessaryBytes, out ushort entrySequenceNumber))
        {
            ntEvent = null;
            return false;
        }
        if (!TryRead(ref reader, ref minimumNecessaryBytes, out byte entryFlags))
        {
            ntEvent = null;
            return false;
        }
        if (!TryParseEntryValue(ref reader, ref minimumNecessaryBytes, (EntryType)entryType, out var value))
        {
            ntEvent = null;
            return false;
        }
        ntEvent = ReadEvent.CreateEntryAssignment(new EntryAssignmentEvent(entryName, (EntryType)entryType, entryId, entrySequenceNumber, entryFlags, value.Item1, value.Item2));
        return true;
    }

    private bool TryParseEntryUpdate(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out ReadEvent? ntEvent)
    {

        if (!TryRead(ref reader, ref minimumNecessaryBytes, out ushort entryId))
        {
            ntEvent = null;
            return false;
        }
        if (!TryRead(ref reader, ref minimumNecessaryBytes, out ushort entrySequenceNumber))
        {
            ntEvent = null;
            return false;
        }
        if (!TryRead(ref reader, ref minimumNecessaryBytes, out byte entryType))
        {
            ntEvent = null;
            return false;
        }
        if (!TryParseEntryValue(ref reader, ref minimumNecessaryBytes, (EntryType)entryType, out var value))
        {
            ntEvent = null;
            return false;
        }
        ntEvent = ReadEvent.CreateEntryUpdate(new EntryUpdateEvent((EntryType)entryType, entryId, entrySequenceNumber, value.Item1, value.Item2));
        return true;
    }

    // Return 1 when ready for a new message
    private int TryParseMessage(ref ReadOnlySequence<byte> buffer, out ReadEvent? ntEvent)
    {
        SequenceReader<byte> reader = new SequenceReader<byte>(buffer);
        int minimumNecessaryBytes = 1;
        if (!reader.TryRead(out byte frameType))
        {
            ntEvent = null;
            return minimumNecessaryBytes;
        }
        ntEvent = null;
        bool success = true;
        switch (frameType)
        {
            case 0x00: // Keep alive
                ntEvent = ReadEvent.CreateKeepAlive();
                break;
            case 0x03: // Server Hello Complete
                ntEvent = ReadEvent.CreateServerHelloComplete();
                break;
            case 0x04: // Server Hello
                success = TryParseServerHello(ref reader, ref minimumNecessaryBytes, out ntEvent);
                break;
            case 0x10: // Entry Assignment
                success = TryParseEntryAssignment(ref reader, ref minimumNecessaryBytes, out ntEvent);
                break;
            case 0x11: // Entry Update
                success = TryParseEntryUpdate(ref reader, ref minimumNecessaryBytes, out ntEvent);
                break;
            case 0x12: // Entry Flags Update
                break;
            case 0x13: // Entry Delete
                break;
            case 0x14: // Clear All Entries
                success = TryParseClearAllEntries(ref reader, ref minimumNecessaryBytes, out ntEvent);
                break;
            case 0x21: // RPC Response
                break;
            default:
                throw new InvalidDataException(nameof(frameType));
        }
        if (success)
        {
            buffer = reader.UnreadSequence;
            minimumNecessaryBytes = 1;
        }
        return minimumNecessaryBytes;
    }
}
