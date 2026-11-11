using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using FreeAstroApi.Serialization;

namespace FreeAstroApi.Tests.Contract;

/// <summary>A theory that only runs when <c>PRISM_URL</c> points at a Prism mock of spec/freeastro.json.</summary>
public sealed class ContractTheoryAttribute : TheoryAttribute
{
    public ContractTheoryAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRISM_URL")))
        {
            Skip = "Set PRISM_URL (or run scripts/with_prism.py) to run the contract tests.";
        }
    }
}

/// <summary>
/// Every SDK method against a Stoplight Prism mock of spec/freeastro.json. Prism validates each request
/// (path, query, headers, JSON body) and answers 4xx with an <c>sl-violations</c> header when it does not
/// match, so a passing run means the SDK builds requests the API accepts; the response must also
/// deserialize into the method's return type. Request values are the minimal valid requests from
/// <c>scripts/spec_sync.py samples</c>.
/// <code>python3 scripts/with_prism.py -- dotnet test --filter Category=Contract</code>
/// </summary>
[Trait("Category", "Contract")]
public class ContractTests
{
    private static readonly Lazy<JsonElement> Samples = new(LoadSamples);

    public static TheoryData<string> Operations()
    {
        var data = new TheoryData<string>();
        foreach (var op in FreeAstroOperations.All) data.Add(op.OperationId);
        return data;
    }

    [ContractTheory]
    [MemberData(nameof(Operations))]
    public async Task Operation(string operationId)
    {
        var op = FreeAstroOperations.All.Single(o => o.OperationId == operationId);
        var sample = Samples.Value.GetProperty(operationId);
        var recorder = new RecordingHandler();
        using var client = new FreeAstroClient(new HttpClient(recorder), new FreeAstroClientOptions
        {
            ApiKey = "contract-test-key",
            BaseUrl = new Uri(Environment.GetEnvironmentVariable("PRISM_URL")!),
            MaxRetries = 0,
            Timeout = TimeSpan.FromSeconds(60),
        });

        var (target, method) = Resolve(client, op.Accessor);
        var arguments = BuildArguments(method, sample);
        try
        {
            var task = (Task)method.Invoke(target, arguments)!;
            await task;
        }
        catch (FreeAstroApiException error) when (error.StatusCode == 500 && (error.Body ?? "").Contains("Cannot find serializer", StringComparison.Ordinal))
        {
            // Prism validated the request but cannot serialise schemaless image bodies (SVG/PNG) and answers 500.
            return;
        }
        catch (FreeAstroApiException error)
        {
            Assert.Fail($"{op.Method} {op.Path} failed: {error.Message}\nviolations: {recorder.LastViolations ?? "-"}\nbody: {error.Body}");
        }

        // Prism also flags its own mock bodies when recursive schemas are cut short; only request violations count.
        if (recorder.LastViolations is { } violations)
        {
            using var parsed = JsonDocument.Parse(violations);
            var requestViolations = parsed.RootElement.EnumerateArray()
                .Where(v => v.GetProperty("location")[0].GetString() == "request")
                .Select(v => v.GetRawText())
                .ToList();
            Assert.True(requestViolations.Count == 0, string.Join("\n", requestViolations));
        }
        Assert.True(recorder.LastStatus < 300, $"status {recorder.LastStatus}");
    }

    private static (object Target, MethodInfo Method) Resolve(FreeAstroClient client, string accessor)
    {
        object target = client;
        var parts = accessor.Split('.');
        foreach (var name in parts[..^1]) target = target.GetType().GetProperty(name)!.GetValue(target)!;
        return (target, target.GetType().GetMethod(parts[^1])!);
    }

    private static object?[] BuildArguments(MethodInfo method, JsonElement sample)
    {
        var pathValues = sample.GetProperty("path_params").EnumerateObject().Select(p => p.Value).ToList();
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        var pathIndex = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            args[i] = p.Name switch
            {
                "body" => sample.TryGetProperty("body", out var body) ? body.Deserialize(p.ParameterType, FreeAstroJson.Options) : null,
                "parameters" => sample.GetProperty("query").Deserialize(p.ParameterType, FreeAstroJson.Options),
                "options" => null,
                "cancellationToken" => CancellationToken.None,
                _ => pathValues[pathIndex++].Deserialize(p.ParameterType, FreeAstroJson.Options),
            };
        }
        return args;
    }

    private static JsonElement LoadSamples()
    {
        // CI runs `spec_sync.py samples`; a precomputed file can be passed instead (no Python in the container).
        var file = Environment.GetEnvironmentVariable("FREEASTRO_CONTRACT_SAMPLES");
        string json;
        if (!string.IsNullOrEmpty(file))
        {
            json = File.ReadAllText(file);
        }
        else
        {
            var start = new ProcessStartInfo("python3")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = RepoPaths.Root,
            };
            foreach (var arg in new[] { "scripts/spec_sync.py", "samples", "spec/freeastro.json" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            json = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("spec_sync.py samples failed");
        }
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class RecordingHandler() : DelegatingHandler(new HttpClientHandler())
    {
        public string? LastViolations { get; private set; }

        public int LastStatus { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            LastStatus = (int)response.StatusCode;
            LastViolations = response.Headers.TryGetValues("sl-violations", out var values) ? string.Join("", values) : null;
            return response;
        }
    }
}
