using System;
using AutoFixture;
using Confluent.Kafka;

namespace DocumentsService.Tests.Setup;

public class KafkaConsumerSetup : ICustomization
{
    public void Customize(IFixture fixture)
    {
        var kafkaConfig = fixture.Create<KafkaTestConfig>();

        var config = new ConsumerConfig
        {
            BootstrapServers = kafkaConfig.BootstrapServers,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        var consumer = new ConsumerBuilder<Null, string>(config).Build();

        consumer.Subscribe("orders");

        fixture.Inject(consumer);
    }
}