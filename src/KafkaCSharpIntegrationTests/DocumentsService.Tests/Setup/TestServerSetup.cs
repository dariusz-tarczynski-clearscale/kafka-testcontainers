using AutoFixture;

namespace DocumentsService.Tests.Setup;

public class TestServerSetup : ICustomization
{
    public void Customize(IFixture fixture)
    {
        var client = new CustomWebApplicationFactory<Program>(fixture).CreateClient();
        fixture.Inject(client);
    }
}