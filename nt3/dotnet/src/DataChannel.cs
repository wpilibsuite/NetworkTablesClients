using System.Threading.Channels;

namespace WPILib.NT3;

public class DataChannel : IDuplexChannel<WriteEvent, ReadEvent>
{
    public ChannelReader<WriteEvent> Input => m_writeEventChannel.Reader;
    public ChannelWriter<WriteEvent> InputWriter => m_writeEventChannel.Writer;

    public ChannelWriter<ReadEvent> Output => m_readEventChannel.Writer;
    public ChannelReader<ReadEvent> OutputReader => m_readEventChannel.Reader;

    private readonly Channel<ReadEvent> m_readEventChannel;

    private readonly Channel<WriteEvent> m_writeEventChannel;

    public DataChannel()
    {
        m_readEventChannel = Channel.CreateUnbounded<ReadEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true
        });

        m_writeEventChannel = Channel.CreateUnbounded<WriteEvent>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
    }
}
