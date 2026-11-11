using System.IO;
using System.Reflection;
using System.Text.Json;

namespace FreeAstroApi.Tests;

public class OperationCoverageTests
{
    private static List<(string Id, string Method, string Path)> SpecOperations()
    {
        using var spec = JsonDocument.Parse(File.ReadAllText(RepoPaths.Spec));
        var result = new List<(string, string, string)>();
        foreach (var path in spec.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var op in path.Value.EnumerateObject())
            {
                if (op.Value.ValueKind == JsonValueKind.Object && op.Value.TryGetProperty("operationId", out var id))
                {
                    result.Add((id.GetString()!, op.Name.ToUpperInvariant(), path.Name));
                }
            }
        }
        return result;
    }

    [Fact]
    public void EveryOperationIdInTheSpecIsRegistered()
    {
        var spec = SpecOperations();
        var registered = FreeAstroOperations.All;

        Assert.Equal(142, spec.Count);
        Assert.Equal(spec.Select(o => o.Id).Order(), registered.Select(o => o.OperationId).Order());
        foreach (var (id, method, path) in spec)
        {
            var op = registered.Single(o => o.OperationId == id);
            Assert.Equal(method, op.Method);
            Assert.Equal(path, op.Path);
        }
        Assert.Equal(registered.Count, registered.Select(o => o.Accessor).Distinct().Count());
    }

    [Fact]
    public void EveryOperationMapsToAPublicAsyncMethod()
    {
        using var client = new FreeAstroClient(new HttpClient(new FakeHandler()), new FreeAstroClientOptions { ApiKey = "k" });
        foreach (var op in FreeAstroOperations.All)
        {
            object target = client;
            var parts = op.Accessor.Split('.');
            foreach (var property in parts[..^1])
            {
                target = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                    ?? throw new Xunit.Sdk.XunitException($"{op.OperationId}: missing resource {property} in {op.Accessor}");
            }
            var method = target.GetType().GetMethod(parts[^1], BindingFlags.Public | BindingFlags.Instance);
            Assert.True(method is not null, $"{op.OperationId}: missing method {op.Accessor}");
            Assert.True(typeof(Task).IsAssignableFrom(method!.ReturnType), $"{op.Accessor} must return a Task");
            Assert.Equal(typeof(CancellationToken), method.GetParameters()[^1].ParameterType);
            Assert.Equal(op.Deprecated, method.GetCustomAttribute<ObsoleteAttribute>() is not null);
        }
    }

    [Fact]
    public void ResourcesExposeExactlyTheRegisteredMethods()
    {
        using var client = new FreeAstroClient(new HttpClient(new FakeHandler()), new FreeAstroClientOptions { ApiKey = "k" });
        var found = new List<string>();
        void Walk(object resource, string prefix)
        {
            foreach (var method in resource.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.Name.EndsWith("Async", StringComparison.Ordinal)) found.Add(prefix + method.Name);
            }
            foreach (var property in resource.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType.Namespace == "FreeAstroApi.Resources") Walk(property.GetValue(resource)!, prefix + property.Name + ".");
            }
        }
        foreach (var property in typeof(FreeAstroClient).GetProperties().Where(p => p.PropertyType.Namespace == "FreeAstroApi.Resources"))
        {
            Walk(property.GetValue(client)!, property.Name + ".");
        }

        Assert.Equal(FreeAstroOperations.All.Select(o => o.Accessor).Order(), found.Order());
    }

    [Theory]
    [InlineData("v1_natal_calculate", "Natal.CalculateAsync")]
    [InlineData("v1_western_natal_insights", "Western.Natal.InsightsAsync")]
    [InlineData("v2_vedic_chart", "Vedic.ChartAsync")]
    [InlineData("v1_vedic_chart", "Vedic.ChartV1Async")]
    [InlineData("v2_vedic_panchang", "Vedic.PanchangAsync")]
    [InlineData("v3_horoscope_daily_sign", "Horoscope.Daily.SignAsync")]
    [InlineData("v1_horoscope_daily_sign", "Horoscope.Daily.SignV1Async")]
    [InlineData("v2_geo_search", "Geo.SearchAsync")]
    [InlineData("v1_sky_events", "SkyEvents.ListAsync")]
    [InlineData("simple_natal", "Natal.SimpleAsync")]
    [InlineData("v1_western_signs_sun", "Western.Signs.SunAsync")]
    public void NamingMatchesTheOtherSdks(string operationId, string accessor)
    {
        Assert.Equal(accessor, FreeAstroOperations.All.Single(o => o.OperationId == operationId).Accessor);
    }
}
