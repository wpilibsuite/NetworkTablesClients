using System.IO.Pipelines;
using System.Net.Sockets;

namespace WPILib.NT3;

internal class SocketPipeline : IDuplexPipe, IAsyncDisposable
{
    public PipeReader Input => m_readFromSocketPipe.Reader;

    public PipeWriter Output => m_writeToSocketPipe.Writer;

    private readonly Pipe m_writeToSocketPipe;
    private readonly Pipe m_readFromSocketPipe;
    private readonly Socket m_socket;
    private readonly Task m_readTask;
    private readonly Task m_writeTask;

    public SocketPipeline(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        m_socket = socket;

        m_writeToSocketPipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
        m_readFromSocketPipe = new Pipe(new PipeOptions(useSynchronizationContext: false));

        m_readTask = ReadPipeAsync();
        m_writeTask = WritePipeAsync();
    }

    private async Task ReadPipeAsync()
    {
        while (true)
        {
            Memory<byte> memory = m_readFromSocketPipe.Writer.GetMemory(512);
            try
            {
                int bytesRead = await m_socket.ReceiveAsync(memory, SocketFlags.None);
                if (bytesRead == 0)
                {
                    break;
                }
                Output.Advance(bytesRead);
            }
            catch (Exception ex)
            {
                // TODO log
                break;
            }

            FlushResult result = await m_readFromSocketPipe.Writer.FlushAsync();

            if (result.IsCompleted)
            {
                break;
            }
        }

        await m_readFromSocketPipe.Writer.CompleteAsync();
        m_socket.Shutdown(SocketShutdown.Receive);
    }

    private async Task WritePipeAsync()
    {
        while (true)
        {
            var readResult = await m_writeToSocketPipe.Reader.ReadAsync();
            var buffer = readResult.Buffer;

            try
            {
                foreach (var buf in buffer)
                {
                    int written = await m_socket.SendAsync(buf, SocketFlags.None);
                    if (written != buf.Length)
                    {
                        buffer = buffer.Slice(written);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // TODO log
                break;
            }

            m_writeToSocketPipe.Reader.AdvanceTo(buffer.Start, buffer.End);

            if (readResult.IsCompleted)
            {
                break;
            }
        }

        await m_writeToSocketPipe.Reader.CompleteAsync();
        m_socket.Shutdown(SocketShutdown.Send);
    }

    public async ValueTask DisposeAsync()
    {
        // TODO figure out shutdown
        await m_writeTask;
        await m_readTask;
        m_socket.Dispose();
    }
}
