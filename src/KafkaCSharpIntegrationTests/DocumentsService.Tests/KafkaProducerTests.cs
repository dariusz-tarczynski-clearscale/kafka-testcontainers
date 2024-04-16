using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.Kafka;
using CloudNative.CloudEvents.SystemTextJson;
using Confluent.Kafka;
using DocumentsService.Tests.Setup;
using LEGO.AsyncAPI.Models;
using LEGO.AsyncAPI.Models.Interfaces;
using LEGO.AsyncAPI.Readers;
using LEGO.AsyncAPI.Validations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Xunit;
using Xunit.Abstractions;

namespace DocumentsService.Tests;

internal class TestData
{
    public string FullName { get; set; }
    
    public string Email { get; set; }
    
    public string Age { get; set; }
}

public class KafkaProducerTests(ITestOutputHelper testOutputHelper)
{
    private static readonly HttpClient Client = new (); 
    
    [Theory]
    [DocumentsControllerSetup]
    public async Task PushObjectToKafka_AllInOneAsyncApiSpec(IConsumer<string?, byte[]> consumer, IProducer<string?, byte[]> producer, Document document)
    {
        var schema = await GetAsyncApiSchema("AllInOneSpec.json");
        ValidateSchemaAndGetPropertiesExampleValues(schema, out var messageBodyValues, out var messageBodyDataValues);

        var cloudEvent = new CloudEvent
        {
            Id = messageBodyValues["id"],
            Source = new Uri(messageBodyValues["source"]),
            Time = DateTimeOffset.UtcNow,
            DataContentType = messageBodyValues["datacontenttype"],
            Data = new TestData
            {
                FullName = messageBodyDataValues["fullName"],
                Email = messageBodyDataValues["email"],
                Age = messageBodyDataValues["age"]
            },
            Type = messageBodyValues["type"]
        };
        
        Assert.True(cloudEvent.IsValid);
        
        var jsonFormatter = new JsonEventFormatter();
        var kafkaMessage = cloudEvent.ToKafkaMessage(ContentMode.Structured, jsonFormatter);

        await producer.ProduceAsync("orders", kafkaMessage);

        var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));
        
        Assert.True(consumeResult.Message.IsCloudEvent());
        var receivedCloudEvent = consumeResult.Message.ToCloudEvent(jsonFormatter);

        Assert.Equal(messageBodyValues["type"], receivedCloudEvent.Type);
        Assert.Equal(new Uri(messageBodyValues["source"]), receivedCloudEvent.Source);
        Assert.Equal(messageBodyValues["id"], receivedCloudEvent.Id);
        Assert.Equal(messageBodyValues["datacontenttype"], receivedCloudEvent.DataContentType);
        
        var testDataJsonElement = Assert.IsType<JsonElement>(receivedCloudEvent.Data);
        var testData = testDataJsonElement.Deserialize<TestData>();
        Assert.NotNull(testData);
        Assert.Equal(messageBodyDataValues["fullName"], testData.FullName);
        Assert.Equal(messageBodyDataValues["email"], testData.Email);
        Assert.Equal(messageBodyDataValues["age"], testData.Age);
    }

    
    [Theory]
    [DocumentsControllerSetup]
    public async Task PushObjectToKafka_AsyncApiWithExternalReference(IConsumer<string?, byte[]> consumer, IProducer<string?, byte[]> producer, Document document)
    {
        var schema = await GetAsyncApiSchema("ExternallyReferencedSchemaSpec.json");
        var externalReference = ValidateSchemaAndGetPropertiesExampleValues(schema, out var messageBodyValues, out var messageBodyDataValues);

        var cloudEvent = new CloudEvent
        {
            Id = "default-id",
            Source = new Uri("http://localhost"),
            Time = DateTimeOffset.UtcNow,
            DataContentType ="application/json",
            Data = new TestData
            {
                FullName = "fullName",
                Email = "email",
                Age = "age"
            },
            Type ="default-type"
        };
        
        Assert.True(cloudEvent.IsValid);
        
        var jsonFormatter = new JsonEventFormatter();
        var kafkaMessage = cloudEvent.ToKafkaMessage(ContentMode.Structured, jsonFormatter);

        await producer.ProduceAsync("orders", kafkaMessage);

        var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));
        
        Assert.True(consumeResult.Message.IsCloudEvent());
        var receivedCloudEvent = consumeResult.Message.ToCloudEvent(jsonFormatter);
        var schemaStr = await GetExternalJsonSchema(externalReference);

        var externalSchema = JSchema.Load(new JsonTextReader(new StringReader(schemaStr)));

        var kafkaMessageString = Encoding.UTF8.GetString(consumeResult.Message.Value);
        var isValidEvent = JObject.Parse(kafkaMessageString).IsValid(externalSchema);
        Assert.True(isValidEvent);

        var testDataJsonElement = Assert.IsType<JsonElement>(receivedCloudEvent.Data);
        var testData = testDataJsonElement.Deserialize<TestData>();
        Assert.NotNull(testData);
        Assert.Equal("fullName", testData.FullName);
        Assert.Equal("email", testData.Email);
        Assert.Equal("age", testData.Age);
    }

    private static async Task<string> GetExternalJsonSchema(string url)
    {
        try
        {
            HttpResponseMessage response = await Client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody;
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine("\nException Caught!");
            Console.WriteLine("Message :{0} ", e.Message);
            return null;
        } 
    }
    private static async Task<string> GetAsyncApiSchema(string schemaFileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(s => s.EndsWith(schemaFileName));
        var stream = assembly.GetManifestResourceStream(resourceName);
        return await new StreamReader(stream).ReadToEndAsync();
    }
    private string? ValidateSchemaAndGetPropertiesExampleValues(string schema, out IDictionary<string, string> messageBodyValues, out IDictionary<string, string> messageBodyDataValues)
    {
        var asyncApiReaderSettings = new AsyncApiReaderSettings();
        var properties = new Dictionary<string, string>();
        var dataProperties = new Dictionary<string, string>();
        // The commented registration below doesn't work. The validation doesn't get triggered.
        //asyncApiReaderSettings.RuleSet.Add(new ValidationRule<AsyncApiChannel>((context, item) =>
        asyncApiReaderSettings.RuleSet.Add(new ValidationRule<IAsyncApiExtensible>((context, item) =>
        {
            if (item is AsyncApiSchema { Title: "MessageBody" } bodyApiSchema)
            {
                WalkThroughTheMessageProperties(context, bodyApiSchema, properties);
            }

            if (item is not AsyncApiSchema { Title: "MessageData" } dataApiSchema) return;
            {
                WalkThroughTheMessageDataProperties(context, dataApiSchema, dataProperties);
            }
        }));
        
        var asyncApiDocument = new AsyncApiStringReader(asyncApiReaderSettings).Read(schema, out var diagnostic);
        asyncApiDocument.ResolveReferences();
        var asyncApiDocExternalReference = asyncApiDocument?.Components?.Schemas?["messagePayload"].AllOf[0]?.Reference?.ExternalResource;
        
        foreach (var diagnosticError in diagnostic.Errors)
        {
            testOutputHelper.WriteLine(diagnosticError.Message);
        }
        Assert.Equal(0, diagnostic.Errors.Count);

        messageBodyValues = properties;
        messageBodyDataValues = dataProperties;

        return asyncApiDocExternalReference;
    }
    private static void OnSpecSchemaProperty(IValidationContext context, KeyValuePair<string, AsyncApiSchema> property, IDictionary<string, string> properties)
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
    private static void WalkThroughTheMessageProperties(IValidationContext context, AsyncApiSchema bodyApiSchema, IDictionary<string, string> exampleValues)
    {
        foreach (var property in bodyApiSchema.Properties)
        {
            switch (property.Key)
            {
                case "specversion":
                case "id":
                case "subject":
                case "source":
                case "type":
                case "time":
                case "datacontenttype":
                    OnSpecSchemaProperty(context, property, exampleValues);
                    break;
            }
        }
    }
    private static void WalkThroughTheMessageDataProperties(IValidationContext context, AsyncApiSchema dataApiSchema, IDictionary<string, string> dataExampleValues)
    {
        foreach (var property in dataApiSchema.Properties)
        {
            switch (property.Key)
            {
                case "fullName":
                case "email":
                case "age":
                    OnSpecSchemaProperty(context, property, dataExampleValues);
                    break;
            }
        }
    }
 
}