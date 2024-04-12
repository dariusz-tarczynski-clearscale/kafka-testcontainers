namespace DocumentsService;

public interface IKafkaProducer : IDisposable
{
    public Task Produce<TMessage>(string topic, TMessage message);
}