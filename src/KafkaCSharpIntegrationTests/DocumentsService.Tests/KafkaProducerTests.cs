using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.Kafka;
using CloudNative.CloudEvents.SystemTextJson;
using Confluent.Kafka;
using DocumentsService.Converters;
using DocumentsService.Tests.Setup;
using FluentAssertions;
using LEGO.AsyncAPI.Readers;
using Newtonsoft.Json;
using Xunit;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DocumentsService.Tests;

public class KafkaProducerTests
{
    [Theory]
    [DocumentsControllerSetup]
    public async Task PushOrderToKafka(IConsumer<string, byte[]> consumer, IProducer<string, byte[]> producer, Document document)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(s => s.EndsWith("Spec.json"));
        var stream = assembly.GetManifestResourceStream(resourceName);
        var kafkaSpec = await new StreamReader(stream).ReadToEndAsync();

        var asyncApiDocument = new AsyncApiStringReader().Read(kafkaSpec, out var diagnostic);

        foreach (var channel in asyncApiDocument.Channels.Values)
        {
            var cloudEvent = new CloudEvent()
            {
                Id = Guid.NewGuid().ToString(),
                Source = new Uri("http://localhost"),
                Time = DateTimeOffset.UtcNow,
                DataContentType = "application/cloudevents+json",
                Data = JsonSerializer.Serialize(document),
                Type = "com.example.myevent"
            };

            if (cloudEvent.IsValid)
            {
                var kafkaMessage = cloudEvent.ToKafkaMessage(ContentMode.Structured, new JsonEventFormatter());
                kafkaMessage.IsCloudEvent().Should().BeTrue();

                await producer.ProduceAsync("orders", kafkaMessage);

                var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));
                var consumedOrder = consumeResult.Message;
                var cloudEventMsg = consumedOrder.ToCloudEvent(new JsonEventFormatter());

                consumedOrder.Should().NotBeNull();
                cloudEventMsg.IsValid.Should().BeTrue();
            }
        }
    }
}