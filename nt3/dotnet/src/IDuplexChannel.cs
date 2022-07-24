using System.Threading.Channels;

namespace WPILib.NT3;

public interface IDuplexChannel<TInput, TOutput>
{
    ChannelReader<TInput> Input { get; }
    ChannelWriter<TInput> InputWriter { get; }
    ChannelWriter<TOutput> Output { get; }
    ChannelReader<TOutput> OutputReader { get; }
}
