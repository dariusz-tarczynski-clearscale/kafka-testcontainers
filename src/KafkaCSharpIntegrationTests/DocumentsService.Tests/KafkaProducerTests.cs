using System;
using System.Collections.Generic;
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
using DocumentsService.Tests.Setup;
using FluentAssertions;
using LEGO.AsyncAPI.Models;
using LEGO.AsyncAPI.Models.Interfaces;
using LEGO.AsyncAPI.Readers;
using LEGO.AsyncAPI.Validations;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DocumentsService.Tests;

internal class TestData
{
    public string FullName { get; set; }
    
    public string Email { get; set; }
    
    public string Age { get; set; }
}

public class KafkaProducerTests
{
    
    private readonly ITestOutputHelper _testOutputHelper;

    public KafkaProducerTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Theory]
    [DocumentsControllerSetup]
    public async Task PushOrderToKafka(IConsumer<string?, byte[]> consumer, IProducer<string?, byte[]> producer, Document document)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(s => s.EndsWith("Spec.json"));
        var stream = assembly.GetManifestResourceStream(resourceName);
        var kafkaSpec = await new StreamReader(stream).ReadToEndAsync();

        var asyncApiReaderSettings = new AsyncApiReaderSettings();
        asyncApiReaderSettings.RuleSet = new ValidationRuleSet();
        var properties = new Dictionary<string, string>();
        var dataProperties = new Dictionary<string, string>();
        asyncApiReaderSettings.RuleSet.Add(new ValidationRule<IAsyncApiExtensible>((context, item) =>
        {
            if (item is AsyncApiSchema { Title: "MessageBody" } bodyApiSchema)
            {
                foreach (var property in bodyApiSchema.Properties)
                {
                    switch (property.Key)
                    {
                        case "specversion":
                            OnSpecSchemaProperty(context, property, properties);
                            break;
                        case "id":
                            OnSpecSchemaProperty(context, property, properties);
                            break;
                        case "subject":
                            OnSpecSchemaProperty(context, property, properties);
                            break;
                        case "source":
                            OnSpecSchemaProperty(context, property, properties);
                            break;
                        case "type":
                            OnSpecSchemaProperty(context, property, properties);
                            break;
                        case "time":
                            OnSpecSchemaProperty(context, property, properties);
                            break;
                        case "datacontenttype":
                            OnSpecSchemaProperty(context, property, properties);
                            break;
                    }
                }
            }


            if (item is not AsyncApiSchema { Title: "MessageData" } dataApiSchema) return;
            {
                foreach (var property in dataApiSchema.Properties)
                {
                    switch (property.Key)
                    {
                        case "fullName":
                            OnSpecSchemaProperty(context, property, dataProperties);
                            break;
                        case "email":
                            OnSpecSchemaProperty(context, property, dataProperties);
                            break;
                        case "age":
                            OnSpecSchemaProperty(context, property, dataProperties);
                            break;
                    }
                }
            }
        }));
        
        var asyncApiDocument = new AsyncApiStringReader(asyncApiReaderSettings).Read(kafkaSpec, out var diagnostic);

        foreach (var diagnosticError in diagnostic.Errors)
        {
            _testOutputHelper.WriteLine(diagnosticError.Message);
        }
        Assert.Equal(0, diagnostic.Errors.Count);

        var cloudEvent = new CloudEvent
        {
            Id = properties["id"],
            Source = new Uri(properties["source"]),
            Time = DateTimeOffset.UtcNow,
            DataContentType = properties["datacontenttype"],
            Data = new TestData
            {
                FullName = dataProperties["fullName"],
                Email = dataProperties["email"],
                Age = dataProperties["age"]
            },
            Type = properties["type"]
        };
        if (cloudEvent.IsValid)
        {
            var jsonFormatter = new JsonEventFormatter();
            var kafkaMessage = cloudEvent.ToKafkaMessage(ContentMode.Structured, jsonFormatter);

            await producer.ProduceAsync("orders", kafkaMessage);

            var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));
            var serialized = JsonConvert.SerializeObject(consumeResult.Message, new HeaderConverter());
            var messageCopy = JsonConvert.DeserializeObject<Message<string?, byte[]>>(serialized, new HeadersConverter(), new HeaderConverter())!;
            Assert.True(messageCopy.IsCloudEvent());
            var receivedCloudEvent = messageCopy.ToCloudEvent(jsonFormatter);
            

            Assert.Equal(properties["type"], receivedCloudEvent.Type);
            Assert.Equal(new Uri(properties["source"]), receivedCloudEvent.Source);
            Assert.Equal(properties["id"], receivedCloudEvent.Id);
            Assert.Equal(properties["datacontenttype"], receivedCloudEvent.DataContentType);
        }
    }
    
    private void OnSpecSchemaProperty(IValidationContext context, KeyValuePair<string, AsyncApiSchema> property, Dictionary<string, string> properties)
    {
        context.Enter("message");
        if (property.Value.Type != SchemaType.String)
        {
            context.CreateError(property.Key, "Type should be String");
        }
        else
        {
            properties.Add(property.Key, property.Value.Examples[0].GetValue<string>());
        }
        context.Exit();
    }
}
public class HeadersConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Headers);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }
        else
        {
            var surrogate = serializer.Deserialize<List<Header>>(reader)!;
            var headers = new Headers();

            foreach (var header in surrogate)
            {
                headers.Add(header.Key, header.GetValueBytes());
            }
            return headers;
        }
    }

    public override void WriteJson(JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }
}

public class HeaderConverter : JsonConverter
{
    private class HeaderContainer
    {
        public string? Key { get; set; }
        public byte[]? Value { get; set; }
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Header) || objectType == typeof(IHeader);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        var headerContainer = serializer.Deserialize<HeaderContainer>(reader)!;
        return new Header(headerContainer.Key, headerContainer.Value);
    }

    public override void WriteJson(JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer)
    {
        var header = (IHeader) value!;
        var container = new HeaderContainer { Key = header.Key, Value = header.GetValueBytes() };
        serializer.Serialize(writer, container);
    }
}