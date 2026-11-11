using FreeAstroApi.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FreeAstroApi.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public async Task AddFreeAstroRegistersAClientBackedByHttpClientFactory()
    {
        var handler = new FakeHandler().Respond(HttpStatusCode.OK, """{"results": [], "count": 0}""");
        var services = new ServiceCollection();
        services.AddFreeAstro(options =>
        {
            options.ApiKey = "di-key";
            options.BaseUrl = new Uri("https://di.example");
        }).ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<FreeAstroClient>();
        await client.Geo.SearchAsync(new GeoSearchParams { Q = "Paris" });

        Assert.Equal("di-key", handler.Requests[0].Headers["x-api-key"]);
        Assert.Equal("di.example", handler.Requests[0].Uri.Host);
        Assert.NotSame(client, provider.GetRequiredService<FreeAstroClient>());
    }
}
