using System.IO;
using FreeAstroApi.Generator;

namespace FreeAstroApi.Tests;

public class GeneratorTests
{
    private static GenerationResult Fixture() =>
        SdkGenerator.Generate(File.ReadAllText(Path.Combine(RepoPaths.Root, "tests", "fixtures", "synthetic-spec.json")));

    [Fact]
    public void CommittedCodeMatchesTheVendoredSpec()
    {
        var result = SdkGenerator.Generate(File.ReadAllText(RepoPaths.Spec));
        var dir = Path.Combine(RepoPaths.Root, "src", "FreeAstroApi", "Generated");

        Assert.Equal(result.Files.Keys.Order(), Directory.GetFiles(dir, "*.g.cs").Select(Path.GetFileName).Order());
        foreach (var (name, content) in result.Files)
        {
            Assert.True(content == File.ReadAllText(Path.Combine(dir, name)), $"{name} is out of date; run scripts/generate.sh");
        }
        Assert.Equal(142, result.OperationCount);
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        var first = Fixture();
        var second = Fixture();

        Assert.Equal(first.Files, second.Files);
    }

    [Fact]
    public void FixtureCoversTheUpcomingSpecFeatures()
    {
        var result = Fixture();
        var models = result.Files["Models.g.cs"];
        var resources = result.Files["Resources.g.cs"];

        // ErrorResponse and sanitised schema names become models.
        Assert.Contains("public partial class ErrorResponse", models, StringComparison.Ordinal);
        Assert.Contains("public partial class ThingInput", models, StringComparison.Ordinal);
        Assert.Contains("public global::FreeAstroApi.Models.Record? Owner { get; set; }", models, StringComparison.Ordinal);
        // A 201 response and a vendor +json response are typed.
        Assert.Contains("Task<global::FreeAstroApi.Models.Record> ItemAsync(string thingId", resources, StringComparison.Ordinal);
        Assert.Contains("Task<global::FreeAstroApi.Models.ThingOutput> CreateAsync(global::FreeAstroApi.Models.ThingInput body", resources, StringComparison.Ordinal);
        Assert.Contains("Task<global::FreeAstroApi.Models.ThingOutput> CreateV1Async(", resources, StringComparison.Ordinal);
        // Text and binary bodies.
        Assert.Contains("global::System.Threading.Tasks.Task<string> SvgAsync(", resources, StringComparison.Ordinal);
        Assert.Contains("global::System.Threading.Tasks.Task<string> MarkdownAsync(", resources, StringComparison.Ordinal);
        Assert.Contains("Task<global::FreeAstroApi.FreeAstroContent> PngAsync(", resources, StringComparison.Ordinal);
        Assert.Contains("Task<global::FreeAstroApi.FreeAstroContent> PdfAsync(", resources, StringComparison.Ordinal);
        Assert.Contains("Task<global::FreeAstroApi.FreeAstroContent> MixedAsync(", resources, StringComparison.Ordinal);
        Assert.Contains("\"text/markdown, application/json;q=0.5\"", resources, StringComparison.Ordinal);
        Assert.Contains("\"image/png, image/svg+xml, application/json;q=0.5\"", resources, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedRequestBodiesFailLoudly()
    {
        const string spec = """
            {"openapi": "3.1.0", "info": {"title": "t", "version": "1"},
             "paths": {"/api/v1/upload/file": {"post": {"operationId": "v1_upload", "responses": {"200": {"description": "ok"}},
               "requestBody": {"content": {"multipart/form-data": {"schema": {"type": "object"}}}}}}}}
            """;

        var error = Assert.ThrowsAny<Exception>(() => SdkGenerator.Generate(spec));

        Assert.Contains("multipart/form-data", error.Message, StringComparison.Ordinal);
    }
}
