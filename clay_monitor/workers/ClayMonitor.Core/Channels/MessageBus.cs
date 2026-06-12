using System.Threading.Channels;

namespace ClayMonitor.Core.Channels;

public interface IMessageBus
{
    ChannelWriter<T> GetWriter<T>();
    ChannelReader<T> GetReader<T>();

    Task PublishAsync<T>(T message, CancellationToken ct = default);
    IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken ct = default);
}

public class MessageBus : IMessageBus
{
    private readonly Dictionary<Type, object> _channels = new();
    private readonly object _lock = new();

    public ChannelWriter<T> GetWriter<T>()
    {
        return GetOrCreateChannel<T>().Writer;
    }

    public ChannelReader<T> GetReader<T>()
    {
        return GetOrCreateChannel<T>().Reader;
    }

    public Task PublishAsync<T>(T message, CancellationToken ct = default)
    {
        var writer = GetWriter<T>();
        return writer.WriteAsync(message, ct).AsTask();
    }

    public async IAsyncEnumerable<T> SubscribeAsync<T>(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var reader = GetReader<T>();
        while (await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out var message))
            {
                yield return message;
            }
        }
    }

    private Channel<T> GetOrCreateChannel<T>()
    {
        lock (_lock)
        {
            var type = typeof(T);
            if (_channels.TryGetValue(type, out var existing))
            {
                return (Channel<T>)existing;
            }

            var channel = Channel.CreateUnbounded<T>(
                new UnboundedChannelOptions
                {
                    SingleReader = false,
                    SingleWriter = false,
                    AllowSynchronousContinuations = true
                });

            _channels[type] = channel;
            return channel;
        }
    }
}
