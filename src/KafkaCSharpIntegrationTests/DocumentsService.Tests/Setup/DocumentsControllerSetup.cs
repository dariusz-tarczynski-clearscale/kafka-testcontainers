using AutoFixture;
using AutoFixture.Xunit2;

namespace DocumentsService.Tests.Setup;

public class DocumentsControllerSetup : AutoDataAttribute
{
    public DocumentsControllerSetup() : base(() => new Fixture()
        .Customize(new TestContainersSetup())
        .Customize(new KafkaConsumerSetup())
        .Customize(new TestServerSetup())
        .Customize(new KafkaProducerSetup()))

    {
    }
}