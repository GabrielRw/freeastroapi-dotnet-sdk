using System.Net.Http.Headers;
using FreeAstroApi.Models;

namespace FreeAstroApi.Tests;

public class BinaryResponseTests
{
    private static KPChartRenderRequest Render() => new() { Year = 1990, Month = 6, Day = 15, Hour = 14, Minute = 30, City = "Delhi" };

    [Fact]
    public async Task SvgResponseIsReturnedWithItsContentType()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.OK, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>", "image/svg+xml");

        var chart = await client.Vedic.KpRenderAsync(Render());

        Assert.Equal("image/svg+xml", chart.ContentType);
        Assert.True(chart.IsText);
        Assert.StartsWith("<svg", chart.ReadAsString(), StringComparison.Ordinal);
        Assert.Equal("image/png,image/svg+xml,application/json;q=0.5", handler.Requests[0].Headers["Accept"].Replace(" ", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PngBytesArePreserved()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0xFF];
        var (client, handler, _) = TestClients.Create();
        handler.Respond(_ =>
        {
            var content = new ByteArrayContent(png);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileName = "\"chart.png\"" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var chart = await client.Vedic.Visual.ChartAsync(new VedicVisualChartRequest { Year = 1990, Month = 6, Day = 15, City = "Delhi" });

        Assert.Equal(png, chart.ToArray());
        Assert.Equal("image/png", chart.ContentType);
        Assert.Equal("chart.png", chart.FileName);
        Assert.False(chart.IsText);
        using var stream = chart.OpenRead();
        Assert.Equal(png.Length, stream.Length);
    }

    [Fact]
    public async Task RawEscapeHatchReturnsAnyBody()
    {
        var (client, handler, _) = TestClients.Create();
        handler.Respond(HttpStatusCode.OK, "# Report", "text/markdown");

        var report = await client.SendRawAsync(HttpMethod.Get, "/api/v1/future/report.md", accept: "text/markdown");

        Assert.Equal("# Report", report.ReadAsString());
        Assert.Equal("text/markdown", handler.Requests[0].Headers["Accept"]);
    }
}
