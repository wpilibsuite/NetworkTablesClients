using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace WPILib.NT3;

public class NT3Client : IAsyncDisposable
{
    public static async Task<NT3Client> CreateClient(IPEndPoint endpoint, string? clientName = null, CancellationToken token = default)
    {
        // TODO support multiple connection tries
        Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(endpoint, token);
        return new NT3Client(socket);
    }

    private readonly DataPipeline m_pipeline;
    private readonly Channel<ReadEvent> m_readEventChannel;
    private readonly Channel<WriteEvent> m_writeEventChannel;

    public NT3Client(Socket connectedSocket, string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(connectedSocket);
        if (!connectedSocket.Connected)
        {
            throw new ArgumentOutOfRangeException(nameof(connectedSocket), "Socket must already be connected");
        }

        m_readEventChannel = Channel.CreateUnbounded<ReadEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true
        });

        m_writeEventChannel = Channel.CreateUnbounded<WriteEvent>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

        m_pipeline = new DataPipeline(m_readEventChannel.Writer, m_writeEventChannel.Reader, connectedSocket);
        if (!m_writeEventChannel.Writer.TryWrite(WriteEvent.CreateClientHello(clientName)))
        {
            throw new InvalidOperationException("Cannot fail to write first message synchronously");
        }

    }

    public ValueTask<ReadEvent> ReceiveEventAsync(CancellationToken cts)
    {
        return m_readEventChannel.Reader.ReadAsync(cts);
    }

    public ValueTask WriteClientHelloCompleteAsync(CancellationToken cts)
    {
        return m_writeEventChannel.Writer.WriteAsync(WriteEvent.CreateClientHelloComplete(), cts);
    }

    public async ValueTask DisposeAsync()
    {
        m_writeEventChannel.Writer.Complete();
        await m_pipeline.DisposeAsync();
        await m_readEventChannel.Reader.Completion;
    }
}
