using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace WPILib.NT3;

internal class NTPipeline : IAsyncDisposable
{
    private readonly ChannelWriter<NTEvent> m_channelWriter;
    private readonly Socket m_socket;
    private readonly Pipe m_pipe;

    private readonly CancellationTokenSource m_cts;
    private readonly Task m_pipeWriterTask;
    private readonly Task m_pipeReaderTask;


    public NTPipeline(ChannelWriter<NTEvent> channelWriter, Socket socket)
    {
        ArgumentNullException.ThrowIfNull(channelWriter);
        ArgumentNullException.ThrowIfNull(socket);

        m_channelWriter = channelWriter;
        m_socket = socket;

        m_pipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
        m_cts = new CancellationTokenSource();
        m_pipeWriterTask = ReadSocketIntoPipe(m_cts.Token);
        m_pipeReaderTask = ReadPipeIntoChannel(m_cts.Token);
    }

    public async ValueTask Stop()
    {
        if (m_cts.IsCancellationRequested)
        {
            return;
        }

        m_cts.Cancel();
        try
        {
            await m_pipeWriterTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {

        }
        try
        {
            await m_pipeReaderTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {

        }

        await m_socket.DisconnectAsync(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Stop();
        m_socket.Dispose();
        m_cts.Dispose();
    }

    private async Task ReadSocketIntoPipe(CancellationToken cts)
    {
        var pipeWriter = m_pipe.Writer;
        try
        {
            while (true)
            {
                cts.ThrowIfCancellationRequested();
                Memory<byte> memory = pipeWriter.GetMemory(512);
                try
                {
                    int bytesRead = await m_socket.ReceiveAsync(memory, SocketFlags.None, cts).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }
                    pipeWriter.Advance(bytesRead);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // TODO log
                    break;
                }

                FlushResult result = await pipeWriter.FlushAsync().ConfigureAwait(false);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        finally
        {
            await pipeWriter.CompleteAsync().ConfigureAwait(false);
        }
    }

    private async Task ReadPipeIntoChannel(CancellationToken cts)
    {
        var pipeReader = m_pipe.Reader;
        int minimumNeededBytes = 1;
        try
        {
            while (true)
            {
                cts.ThrowIfCancellationRequested();
                ReadResult result = await pipeReader.ReadAtLeastAsync(minimumNeededBytes, cts);
                var buffer = result.Buffer;

                try
                {

                    while (true)
                    {
                        cts.ThrowIfCancellationRequested();
                        minimumNeededBytes = TryParseMessage(ref buffer, out var ntEvent);
                        if (minimumNeededBytes != 1)
                        {
                            Debug.Assert(ntEvent == null);
                            break;
                        }
                        if (ntEvent != null)
                        {
                            await m_channelWriter.WriteAsync(ntEvent, cts).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
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
        }
        finally
        {
            await pipeReader.CompleteAsync().ConfigureAwait(false);
            m_channelWriter.Complete();
        }
    }

    private byte[]? currentByteRead = null;

    private bool TryReadByte(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out byte value)
    {
        minimumNecessaryBytes++;
        return reader.TryRead(out value);
    }

    private bool TryReadUInt32(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out uint value)
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

    private bool TryReadByteArray(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, int length, [NotNullWhen(true)] out byte[]? value)
    {
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
            if (!TryReadByte(ref reader, ref minimumNecessaryBytes, out var next))
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
        return TryReadByteArray(ref reader, ref minimumNecessaryBytes, length, out value);
    }

    private bool TryReadString(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, [NotNullWhen(true)] out string? value)
    {
        if (!TryReadLeb128(ref reader, ref minimumNecessaryBytes, out var length))
        {
            value = null;
            return false;
        }
        if (!TryReadByteArray(ref reader, ref minimumNecessaryBytes, length, out var bytes))
        {
            value = null;
            return false;
        }
        value = Encoding.UTF8.GetString(bytes);
        return true;
    }

    private bool TryParseServerHello(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, [NotNullWhen(true)] out NTEvent? ntEvent)
    {
        if (!TryReadByte(ref reader, ref minimumNecessaryBytes, out var _))
        {
            ntEvent = null;
            return false;
        }
        if (!TryReadString(ref reader, ref minimumNecessaryBytes, out var _))
        {
            ntEvent = null;
            return false;
        }
        ntEvent = new NTEvent(); // TODO Connected
        return true;
    }

    private bool TryParseClearAllEntries(ref SequenceReader<byte> reader, ref int minimumNecessaryBytes, out NTEvent? ntEvent)
    {
        if (!TryReadUInt32(ref reader, ref minimumNecessaryBytes, out var magic))
        {
            ntEvent = null;
            return false;
        }
        if (magic == 0xD06CB27A)
        {
            ntEvent = new NTEvent(); // TODO
        }
        else
        {
            ntEvent = null;
        }
        return true;
    }

    // Return 1 when ready for a new message
    private int TryParseMessage(ref ReadOnlySequence<byte> buffer, out NTEvent? ntEvent)
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
                break;
            case 0x03: // Server Hello Complete
                break;
            case 0x04: // Server Hello
                success = TryParseServerHello(ref reader, ref minimumNecessaryBytes, out ntEvent);
                break;
            case 0x10: // Entry Assignment
                break;
            case 0x11: // Entry Update
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
