using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.Kafka;
using CloudNative.CloudEvents.SystemTextJson;
using Confluent.Kafka;
using DocumentsService.Tests.Setup;
using FluentAssertions;
using LEGO.AsyncAPI.Readers;
using Xunit;

namespace DocumentsService.Tests;

public class KafkaProducerTests
{
    [Theory]
    [DocumentsControllerSetup]
    public async Task PushOrderToKafka(IConsumer<Null, string> consumer, IProducer<Null, string> producer, Document document)
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
                DataContentType = MediaTypeNames.Text.Xml,
                Data = JsonSerializer.Serialize(document),
                Type = "com.example.myevent"
            };

            if (cloudEvent.IsValid)
            {
                var kafkaMessage = cloudEvent.ToKafkaMessage(ContentMode.Structured, new JsonEventFormatter());

                var serializedKafkaMessage = JsonSerializer.Serialize(kafkaMessage);
                var message = new Message<Null, string> { Value = serializedKafkaMessage };
                await producer.ProduceAsync("orders", message);

                var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));
                var consumedOrder = JsonSerializer.Deserialize<Document>(consumeResult.Message.Value);

                consumedOrder.Should().NotBeNull();

                // consumedOrder.Should().BeEquivalentTo(document);
            }
        }
    }
}