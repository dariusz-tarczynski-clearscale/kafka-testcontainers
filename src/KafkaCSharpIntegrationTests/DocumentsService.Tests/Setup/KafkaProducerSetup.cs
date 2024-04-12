using AutoFixture;
using Confluent.Kafka;

namespace DocumentsService.Tests.Setup;

public class KafkaProducerSetup : ICustomization
{
    public void Customize(IFixture fixture)
    {
        var kafkaConfig = fixture.Create<KafkaTestConfig>();

        var config = new ProducerConfig { BootstrapServers = kafkaConfig.BootstrapServers };

        var producer = new ProducerBuilder<Null, string>(config).Build();

        fixture.Inject(producer);
    }
}
