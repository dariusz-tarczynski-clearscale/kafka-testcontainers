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

public class KafkaHttpClientTests
{

    [Theory]
    [DocumentsControllerSetup]
    public async Task PushOrderToKafka(HttpClient client, IConsumer<Null, string> consumer, Document document)
    {
        var response = await client.PostAsync("/api/documents",
            new StringContent(JsonSerializer.Serialize(document), Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();

        var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));
        var consumedOrder = JsonSerializer.Deserialize<Document>(consumeResult.Message.Value);

        consumedOrder.Should().BeEquivalentTo(document);
    }
}