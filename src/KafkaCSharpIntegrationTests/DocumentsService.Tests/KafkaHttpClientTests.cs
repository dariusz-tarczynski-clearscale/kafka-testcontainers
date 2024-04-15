using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Confluent.Kafka;
using DocumentsService.Tests.Setup;
using FluentAssertions;
using Xunit;

namespace DocumentsService.Tests;

public class KafkaHttpClientTests
{

    [Theory]
    [DocumentsControllerSetup]
    public async Task PushOrderToKafka(HttpClient client, IConsumer<string, byte[]> consumer, Document document)
    {
        var response = await client.PostAsync("/api/documents",
            new StringContent(JsonSerializer.Serialize(document), Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();

        var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));
        var consumedOrder = JsonSerializer.Deserialize<Document>(consumeResult.Message.Value);

        consumedOrder.Should().BeEquivalentTo(document);
    }
}