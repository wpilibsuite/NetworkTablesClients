using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace WPILib.NT3;

internal class NT3Client : IAsyncDisposable
{
    public static async Task<NT3Client> CreateClient(IPEndPoint endpoint, string? clientName = null, CancellationToken token = default)
    {
        // TODO support multiple connection tries
        Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(endpoint, token);
        SocketPipeline socketPipe = new(socket);
        DataChannel dataChannel = new();
        return new(dataChannel, socketPipe, clientName);
    }

    private readonly DataPipeline m_pipeline;
    private readonly ChannelWriter<WriteEvent> m_inputWriter;
    private readonly ChannelReader<ReadEvent> m_outputReader;

    public NT3Client(IDuplexChannel<WriteEvent, ReadEvent> channels, IDuplexPipe pipes, string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(pipes);

        m_pipeline = new DataPipeline(channels, pipes);
        m_inputWriter = channels.InputWriter;
        m_outputReader = channels.OutputReader;

        if (!m_inputWriter.TryWrite(WriteEvent.CreateClientHello(clientName)))
        {
            throw new InvalidOperationException("Cannot fail to write first message synchronously");
        }

    }

    public ValueTask<ReadEvent> ReceiveEventAsync(CancellationToken cts)
    {
        return m_outputReader.ReadAsync(cts);
    }

    public ValueTask WriteClientHelloCompleteAsync(CancellationToken cts)
    {
        return m_inputWriter.WriteAsync(WriteEvent.CreateClientHelloComplete(), cts);
    }

    public async ValueTask CloseAsync()
    {
        m_inputWriter.Complete();
        await m_pipeline.CloseAsync();
        await m_outputReader.Completion;
    }

    public async ValueTask DisposeAsync()
    {
        // TODO figure more of this out
        await m_pipeline.DisposeAsync();
    }
}
